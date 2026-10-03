using System.Buffers.Binary;
using System.Text;
using EtchForge.Models;

namespace EtchForge.Services;

internal static class DiskLayoutInspector
{
    public static async Task<(string Scheme, int Sector, string Health, string Boot, IReadOnlyList<PartitionInfo> Partitions)>
        InspectAsync(VirtualDiskReader reader, long size, bool optical, CancellationToken token)
    {
        if (optical)
        {
            var pvd = await reader.ReadAsync(16L * 2048, 2048, token);
            var iso9660 = pvd.AsSpan(1, 5).SequenceEqual("CD001"u8);
            var label = iso9660 ? Encoding.ASCII.GetString(pvd, 40, 32).Trim('\0', ' ') : null;
            var fileSystem = iso9660 ? "ISO 9660" : "UDF";
            return ($"{fileSystem} / 光盘", 2048, "已识别光盘卷描述符", "光盘引导状态未检查",
                [new PartitionInfo(1, "光盘卷", 0, size, fileSystem, label, false)]);
        }
        foreach (var sector in new[] { 512, 4096 })
        {
            if (size < sector * 3L) continue;
            var header = await reader.ReadAsync(sector, sector, token);
            if (!header.AsSpan(0, 8).SequenceEqual("EFI PART"u8)) continue;
            return await InspectGptAsync(reader, size, sector, header, token);
        }
        if (size < 512) return ("未识别 / 无分区", 512, "镜像小于一个扇区", "未发现", []);
        var mbr = await reader.ReadAsync(0, 512, token);
        if (mbr[510] != 0x55 || mbr[511] != 0xAA)
            return ("未识别 / 无分区", 512, "没有有效的 MBR/GPT 标记", "未发现", []);
        var partitions = new List<PartitionInfo>();
        var issues = new List<string>();
        if (Enumerable.Range(0, 4).Any(i => mbr[446 + i * 16 + 4] == 0xEE))
            issues.Add("发现保护 MBR，但 GPT 表头无效或缺失");
        for (var i = 0; i < 4; i++)
        {
            var entry = mbr.AsSpan(446 + i * 16, 16);
            var type = entry[4];
            var start = BinaryPrimitives.ReadUInt32LittleEndian(entry[8..12]);
            var count = BinaryPrimitives.ReadUInt32LittleEndian(entry[12..16]);
            if (type == 0 || count == 0) continue;
            var offset = start * 512L;
            var length = count * 512L;
            if (offset < 512 || offset > size || length > size - offset)
            {
                issues.Add($"分区 {i + 1} 超出镜像范围");
                continue;
            }
            var boot = entry[0] == 0x80;
            if (type is 0x05 or 0x0F or 0x85)
            {
                partitions.Add(new PartitionInfo(i + 1, "扩展分区", offset, length, "容器", null, false));
                await InspectExtendedAsync(reader, start, count, size, partitions, issues, token);
                continue;
            }
            var (fs, label) = await FileSystemAsync(reader, offset, size - offset, token);
            partitions.Add(new PartitionInfo(i + 1, $"MBR 0x{type:X2}", offset, length, fs, label, boot));
        }
        if (partitions.Count == 0)
        {
            var (fs, label) = await FileSystemAsync(reader, 0, size, token);
            return ("未识别 / 无分区", 512, fs == "未知" ? "仅有引导扇区签名，无有效分区项" : "无分区表，识别到卷",
                "未发现", fs == "未知" ? [] : [new PartitionInfo(1, "整盘卷", 0, size, fs, label, false)]);
        }
        CheckOverlap(partitions, issues);
        var bootHint = partitions.Any(p => p.Bootable) ? "MBR 活动分区" : "未发现活动分区";
        return ("MBR", 512, issues.Count == 0 ? "分区范围正常" : string.Join("；", issues), bootHint, partitions);
    }

    private static async Task InspectExtendedAsync(VirtualDiskReader reader, uint baseLba, uint sectors,
        long diskSize, List<PartitionInfo> partitions, List<string> issues, CancellationToken token)
    {
        var current = (long)baseLba;
        var visited = new HashSet<long>();
        for (var index = 0; index < 128 && visited.Add(current); index++)
        {
            if (current * 512 > diskSize - 512) { issues.Add("扩展分区链越界"); return; }
            var ebr = await reader.ReadAsync(current * 512, 512, token);
            if (ebr[510] != 0x55 || ebr[511] != 0xAA) { issues.Add("扩展分区链标记无效"); return; }
            var entry = ebr.AsSpan(446, 16);
            var type = entry[4];
            var relative = BinaryPrimitives.ReadUInt32LittleEndian(entry[8..12]);
            var count = BinaryPrimitives.ReadUInt32LittleEndian(entry[12..16]);
            var start = current + relative;
            if (type != 0 && count != 0)
            {
                if (start < baseLba || start + count > (long)baseLba + sectors ||
                    start * 512 > diskSize || count * 512L > diskSize - start * 512)
                    issues.Add("逻辑分区越界");
                else
                {
                    var (fs, label) = await FileSystemAsync(reader, start * 512, count * 512L, token);
                    partitions.Add(new PartitionInfo(5 + index, $"MBR 0x{type:X2}", start * 512,
                        count * 512L, fs, label, false));
                }
            }
            var link = ebr.AsSpan(462, 16);
            var next = BinaryPrimitives.ReadUInt32LittleEndian(link[8..12]);
            if (link[4] == 0 || next == 0) return;
            current = (long)baseLba + next;
        }
        issues.Add("扩展分区链循环或过长");
    }

    private static async Task<(string, int, string, string, IReadOnlyList<PartitionInfo>)> InspectGptAsync(
        VirtualDiskReader reader, long size, int sector, byte[] header, CancellationToken token)
    {
        var issues = new List<string>();
        var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12, 4));
        if (headerSize is < 92 or > 4096 || headerSize > sector)
            return ("GPT", sector, "GPT 表头长度无效", "未检查", []);
        var savedCrc = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(16, 4));
        var check = header.AsSpan(0, (int)headerSize).ToArray();
        check.AsSpan(16, 4).Clear();
        if (Crc32(check) != savedCrc) issues.Add("主 GPT 表头 CRC 错误");
        if (BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(24, 8)) != 1)
            issues.Add("主 GPT 表头位置异常");
        var protective = await reader.ReadAsync(0, 512, token);
        if (protective[510] != 0x55 || protective[511] != 0xAA ||
            !Enumerable.Range(0, 4).Any(i => protective[446 + i * 16 + 4] == 0xEE))
            issues.Add("保护 MBR 缺失");
        var entryLba = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(72, 8));
        var count = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(80, 4));
        var entrySize = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(84, 4));
        if (count == 0 || count > 16384 || entrySize is < 128 or > 1024 || (ulong)count * entrySize > 16 * 1024 * 1024 ||
            entryLba > (ulong)(size / sector) || (ulong)count * entrySize > (ulong)size - entryLba * (ulong)sector)
            return ("GPT", sector, "GPT 分区项范围无效", "未检查", []);
        var entries = await reader.ReadAsync((long)entryLba * sector, (int)(count * entrySize), token);
        var entryCrc = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(88, 4));
        if (Crc32(entries) != entryCrc) issues.Add("GPT 分区项 CRC 错误");
        var backupLba = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(32, 8));
        if (backupLba >= (ulong)(size / sector)) issues.Add("备份 GPT 表头位置越界");
        else
        {
            var backup = await reader.ReadAsync((long)backupLba * sector, sector, token);
            if (!backup.AsSpan(0, 8).SequenceEqual("EFI PART"u8)) issues.Add("未找到备份 GPT 表头");
            else
            {
                var backupSize = BinaryPrimitives.ReadUInt32LittleEndian(backup.AsSpan(12, 4));
                if (backupSize is < 92 or > 4096 || backupSize > sector) issues.Add("备份 GPT 表头长度无效");
                else
                {
                    var crc = BinaryPrimitives.ReadUInt32LittleEndian(backup.AsSpan(16, 4));
                    backup.AsSpan(16, 4).Clear();
                    if (Crc32(backup.AsSpan(0, (int)backupSize)) != crc) issues.Add("备份 GPT 表头 CRC 错误");
                    if (BinaryPrimitives.ReadUInt64LittleEndian(backup.AsSpan(24, 8)) != backupLba ||
                        BinaryPrimitives.ReadUInt64LittleEndian(backup.AsSpan(32, 8)) != 1 ||
                        !backup.AsSpan(56, 16).SequenceEqual(header.AsSpan(56, 16)) ||
                        BinaryPrimitives.ReadUInt32LittleEndian(backup.AsSpan(80, 4)) != count ||
                        BinaryPrimitives.ReadUInt32LittleEndian(backup.AsSpan(84, 4)) != entrySize ||
                        BinaryPrimitives.ReadUInt32LittleEndian(backup.AsSpan(88, 4)) != entryCrc)
                        issues.Add("备份 GPT 表头与主表不一致");
                    var backupEntryLba = BinaryPrimitives.ReadUInt64LittleEndian(backup.AsSpan(72, 8));
                    if (backupEntryLba > (ulong)(size / sector) ||
                        (ulong)entries.Length > (ulong)size - backupEntryLba * (ulong)sector)
                        issues.Add("备份 GPT 分区项位置越界");
                    else
                    {
                        var backupEntries = await reader.ReadAsync((long)backupEntryLba * sector, entries.Length, token);
                        if (Crc32(backupEntries) != entryCrc || !backupEntries.AsSpan().SequenceEqual(entries))
                            issues.Add("备份 GPT 分区项不一致");
                    }
                }
            }
        }
        var partitions = new List<PartitionInfo>();
        var firstUsable = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(40, 8));
        var lastUsable = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(48, 8));
        for (var i = 0; i < count; i++)
        {
            var entry = entries.AsSpan((int)(i * entrySize), (int)entrySize);
            if (entry[..16].IndexOfAnyExcept((byte)0) < 0) continue;
            var first = BinaryPrimitives.ReadUInt64LittleEndian(entry[32..40]);
            var last = BinaryPrimitives.ReadUInt64LittleEndian(entry[40..48]);
            if (last < first || first < firstUsable || last > lastUsable ||
                first > (ulong)(size / sector) || last >= (ulong)(size / sector))
            {
                issues.Add($"分区 {i + 1} 超出镜像范围");
                continue;
            }
            var type = new Guid(entry[..16]);
            var name = Encoding.Unicode.GetString(entry.Slice(56, Math.Min(72, entry.Length - 56))).TrimEnd('\0');
            var offset = (long)first * sector;
            var length = checked(((long)last - (long)first + 1) * sector);
            var (fs, label) = await FileSystemAsync(reader, offset, length, token);
            var kind = type == Guid.Parse("c12a7328-f81f-11d2-ba4b-00a0c93ec93b") ? "EFI 系统分区" :
                type == Guid.Parse("0fc63daf-8483-4772-8e79-3d69d8477de4") ? "Linux 数据" :
                type == Guid.Parse("ebd0a0a2-b9e5-4433-87c0-68b6b72699c7") ? "Microsoft 数据" : type.ToString();
            partitions.Add(new PartitionInfo(i + 1, kind, offset, length, fs, string.IsNullOrWhiteSpace(name) ? label : name, kind == "EFI 系统分区"));
        }
        CheckOverlap(partitions, issues);
        var boot = partitions.Any(p => p.Type == "EFI 系统分区") ? "EFI 系统分区" : "未发现 EFI 系统分区";
        return ("GPT", sector, issues.Count == 0 ? "GPT 主/备表头与分区项 CRC 正常" : string.Join("；", issues), boot, partitions);
    }

    private static void CheckOverlap(List<PartitionInfo> partitions, List<string> issues)
    {
        var ordered = partitions.Where(p => p.Type != "扩展分区").OrderBy(p => p.StartBytes).ToArray();
        for (var i = 1; i < ordered.Length; i++)
            if (ordered[i].StartBytes < ordered[i - 1].StartBytes + ordered[i - 1].SizeBytes)
                issues.Add($"分区 {ordered[i - 1].Number} 与 {ordered[i].Number} 重叠");
    }

    private static async Task<(string FileSystem, string? Label)> FileSystemAsync(
        VirtualDiskReader reader, long offset, long length, CancellationToken token)
    {
        if (length < 512) return ("未知", null);
        var boot = await reader.ReadAsync(offset, 512, token);
        string Label(int start, int count) => Encoding.ASCII.GetString(boot, start, count).Trim('\0', ' ');
        if (boot.AsSpan(3, 8).SequenceEqual("NTFS    "u8)) return ("NTFS", null);
        if (boot.AsSpan(3, 8).SequenceEqual("EXFAT   "u8)) return ("exFAT", null);
        if (boot.AsSpan(3, 8).SequenceEqual("-FVE-FS-"u8)) return ("BitLocker 加密", null);
        if (boot.AsSpan(0, 4).SequenceEqual("XFSB"u8)) return ("XFS", null);
        if (boot.AsSpan(32, 4).SequenceEqual("NXSB"u8)) return ("APFS 容器", null);
        if (boot.AsSpan(82, 5).SequenceEqual("FAT32"u8)) return ("FAT32", Label(71, 11));
        if (boot.AsSpan(54, 3).SequenceEqual("FAT"u8)) return ("FAT12/16", Label(43, 11));
        if (boot.AsSpan(0, 6).SequenceEqual(new byte[] { 0x4C, 0x55, 0x4B, 0x53, 0xBA, 0xBE }))
            return ("LUKS 加密", null);
        if (length >= 2048)
        {
            var super = await reader.ReadAsync(offset + 1024, 1024, token);
            if (super[56] == 0x53 && super[57] == 0xEF)
            {
                var incompat = BinaryPrimitives.ReadUInt32LittleEndian(super.AsSpan(96, 4));
                var compat = BinaryPrimitives.ReadUInt32LittleEndian(super.AsSpan(92, 4));
                var kind = (incompat & 0x40) != 0 ? "ext4" : (compat & 0x04) != 0 ? "ext3" : "ext2";
                return (kind, Encoding.UTF8.GetString(super, 120, 16).TrimEnd('\0'));
            }
            if (super[0] == 0x48 && super[1] == 0x2B) return ("HFS+", null);
        }
        return ("未知", null);
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(int)(crc & 1));
        }
        return ~crc;
    }
}

using System.Buffers;
using System.Security.Cryptography;
using EtchForge.Models;

namespace EtchForge.Services;

public static class ImageInspector
{
    private static readonly HashSet<string> SupportedFormats =
        ["raw", "qcow2", "vmdk", "vpc", "vhdx", "vdi", "dmg"];

    public static async Task<ImageInfo> InspectAsync(string path, IProgress<double>? progress, CancellationToken token)
    {
        var file = new FileInfo(path);
        if (!file.Exists) throw new FileNotFoundException("找不到镜像文件。", path);
        if (file.Length == 0) throw new InvalidDataException("镜像文件为空。");
        var initialLength = file.Length;
        var initialWriteTime = file.LastWriteTimeUtc;
        var qemu = ConversionService.FindQemuImg();
        var format = "raw";
        long virtualBytes = file.Length;
        long? allocated = null;
        string? backing = null, container = null;
        var optical = false;
        await using (var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                         4096, FileOptions.Asynchronous | FileOptions.RandomAccess))
        {
            if (file.Length >= 16L * 2048 + 2048)
            {
                for (var sector = 16; sector < 32 && (sector + 1L) * 2048 <= file.Length; sector++)
                {
                    probe.Position = sector * 2048L;
                    var descriptor = new byte[6];
                    await probe.ReadExactlyAsync(descriptor, token);
                    if (descriptor.AsSpan(1, 5).SequenceEqual("CD001"u8) ||
                        descriptor.AsSpan(1, 5).SequenceEqual("NSR02"u8) ||
                        descriptor.AsSpan(1, 5).SequenceEqual("NSR03"u8))
                    {
                        optical = true;
                        break;
                    }
                }
            }
        }
        if (optical) format = "iso";
        else if (qemu is not null)
        {
            var info = await VirtualDiskReader.GetInfoAsync(qemu, path, token);
            format = info.Format;
            virtualBytes = info.VirtualBytes;
            allocated = info.AllocatedBytes;
            backing = info.BackingFile;
            container = info.Details;
            if (!SupportedFormats.Contains(format))
                throw new InvalidDataException($"暂不支持此输入格式：{format}");
        }
        else
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is not (".img" or ".raw" or ".dd" or ".ima" or ".bin"))
                throw new FileNotFoundException("读取此镜像格式需要 qemu-img。请从设置页打开官方安装文档。");
            await using var probe = File.OpenRead(path);
            var head = new byte[(int)Math.Min(file.Length, 1024)];
            await probe.ReadExactlyAsync(head, token);
            if (head.AsSpan(0, Math.Min(4, head.Length)).SequenceEqual(new byte[] { 0x51, 0x46, 0x49, 0xFB }) ||
                head.AsSpan(0, Math.Min(8, head.Length)).SequenceEqual("vhdxfile"u8) ||
                head.AsSpan(0, Math.Min(4, head.Length)).SequenceEqual("KDMV"u8) ||
                head.AsSpan(0, Math.Min(21, head.Length)).SequenceEqual("# Disk DescriptorFile"u8) ||
                head.Length >= 68 && head.AsSpan(64, 4).SequenceEqual(new byte[] { 0x7F, 0x10, 0xDA, 0xBE }))
                throw new FileNotFoundException("这是容器镜像；需要 qemu-img 才能读取。请从设置页打开官方安装文档。");
            if (file.Length >= 512)
            {
                probe.Position = file.Length - 512;
                var tail = new byte[512];
                await probe.ReadExactlyAsync(tail, token);
                if (tail.AsSpan(0, 8).SequenceEqual("conectix"u8) ||
                    tail.AsSpan(0, 4).SequenceEqual("koly"u8))
                    throw new FileNotFoundException("这是容器镜像；需要 qemu-img 才能读取。请从设置页打开官方安装文档。");
            }
        }
        if (format == "raw" && file.Length % 512 != 0)
            throw new InvalidDataException("raw 磁盘镜像大小必须是 512 字节的整数倍。");
        if (virtualBytes <= 0) throw new InvalidDataException("镜像的虚拟容量无效。");
        if (!optical && virtualBytes % 512 != 0)
            throw new InvalidDataException("虚拟磁盘容量必须是 512 字节的整数倍。");
        allocated ??= await FileAllocation.GetAsync(path, token);

        await using var reader = new VirtualDiskReader(path, format, qemu);
        var layout = await DiskLayoutInspector.InspectAsync(reader, virtualBytes, optical, token);
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            4 * 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        var buffer = ArrayPool<byte>.Shared.Rent(4 * 1024 * 1024);
        try
        {
            long processed = 0;
            int count;
            while ((count = await input.ReadAsync(buffer, token)) != 0)
            {
                sha.AppendData(buffer, 0, count);
                md5.AppendData(buffer, 0, count);
                processed += count;
                progress?.Report((double)processed / file.Length);
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
        file.Refresh();
        if (file.Length != initialLength || file.LastWriteTimeUtc != initialWriteTime)
            throw new IOException("读取期间源文件发生变化，请停止写入后重试。");
        return new ImageInfo(path, file.Length, layout.Scheme,
            Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant(),
            Convert.ToHexString(md5.GetHashAndReset()).ToLowerInvariant())
        {
            Format = format, VirtualBytes = virtualBytes, AllocatedBytes = allocated,
            LogicalSectorSize = layout.Sector, IsOptical = optical, LayoutHealth = layout.Health,
            BootHints = layout.Boot, Partitions = layout.Partitions, BackingFile = backing,
            ContainerDetails = container, LastWriteTimeUtc = file.LastWriteTimeUtc
        };
    }
}

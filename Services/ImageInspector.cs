using System.Buffers;
using System.Security.Cryptography;
using EtchForge.Models;

namespace EtchForge.Services;

public static class ImageInspector
{
    public static async Task<ImageInfo> InspectAsync(string path, IProgress<double>? progress, CancellationToken token)
    {
        var file = new FileInfo(path);
        if (!file.Exists) throw new FileNotFoundException("找不到镜像文件。", path);
        if (file.Length == 0 || file.Length % 512 != 0)
            throw new InvalidDataException("镜像必须非空，且大小为 512 字节的整数倍。请选择完整的 raw .img 磁盘镜像。");

        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            4 * 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var header = new byte[Math.Min(1024, checked((int)Math.Min(file.Length, 1024)))];
        await input.ReadExactlyAsync(header, token);
        if (header.AsSpan(0, 4).SequenceEqual(new byte[] { 0x51, 0x46, 0x49, 0xFB }) ||
            header.AsSpan(0, 8).SequenceEqual("vhdxfile"u8) ||
            header.AsSpan(0, 4).SequenceEqual("KDMV"u8))
            throw new InvalidDataException("文件内容像 QCOW2、VHDX 或 VMDK 容器，不是 raw .img 镜像。");

        var partition = header[510] == 0x55 && header[511] == 0xAA
            ? header.Length >= 520 && header.AsSpan(512, 8).SequenceEqual("EFI PART"u8) ? "GPT · 带保护 MBR" : "MBR"
            : "未识别分区表 / 无分区磁盘";
        input.Position = 0;

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

        if (new FileInfo(path).Length != file.Length)
            throw new IOException("计算哈希期间源文件大小发生变化，请停止写入后重试。");
        return new ImageInfo(path, file.Length, partition,
            Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant(),
            Convert.ToHexString(md5.GetHashAndReset()).ToLowerInvariant());
    }
}

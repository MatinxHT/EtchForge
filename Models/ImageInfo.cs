namespace EtchForge.Models;

public sealed record ImageInfo(string Path, long Bytes, string PartitionScheme, string Sha256, string Md5)
{
    public string FileName => System.IO.Path.GetFileName(Path);
    public string SizeText => Bytes >= 1024L * 1024 * 1024
        ? $"{Bytes / 1024d / 1024d / 1024d:0.00} GiB · {Bytes:N0} bytes"
        : $"{Bytes / 1024d / 1024d:0.0} MiB · {Bytes:N0} bytes";
    public string Format { get; init; } = "raw";
    public long VirtualBytes { get; init; }
    public long? AllocatedBytes { get; init; }
    public int LogicalSectorSize { get; init; } = 512;
    public bool IsOptical { get; init; }
    public string LayoutHealth { get; init; } = "未检查";
    public string BootHints { get; init; } = "未发现";
    public string? BackingFile { get; init; }
    public string? ContainerDetails { get; init; }
    public DateTime LastWriteTimeUtc { get; init; }
    public IReadOnlyList<PartitionInfo> Partitions { get; init; } = [];
}

public sealed record PartitionInfo(int Number, string Type, long StartBytes, long SizeBytes,
    string FileSystem, string? Label, bool Bootable);

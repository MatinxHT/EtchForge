namespace EtchForge.Models;

public sealed record ImageInfo(string Path, long Bytes, string PartitionScheme, string Sha256, string Md5)
{
    public string FileName => System.IO.Path.GetFileName(Path);
    public string SizeText => $"{Bytes / 1024d / 1024d / 1024d:0.00} GiB · {Bytes:N0} bytes";
}

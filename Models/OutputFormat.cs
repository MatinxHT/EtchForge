namespace EtchForge.Models;

public sealed record OutputFormat(string Id, string Name, string Description,
    string EnglishName, string EnglishDescription, string Extension,
    string? QemuFormat = null, string? QemuOptions = null)
{
    public bool IsNativeEsxi => Id == "esxi";

    public static IReadOnlyList<OutputFormat> All { get; } =
    [
        new("esxi", "ESXi flat VMDK", "双文件 · VMFS 描述符 + 原始数据",
            "ESXi flat VMDK", "Two files · VMFS descriptor + raw data", ".vmdk"),
        new("vhd", "VHD", "固定大小 · 旧版 Hyper-V / Virtual PC",
            "VHD", "Fixed size · legacy Hyper-V / Virtual PC", ".vhd", "vpc", "subformat=fixed"),
        new("vhdx", "VHDX", "动态大小 · Hyper-V",
            "VHDX", "Dynamic size · Hyper-V", ".vhdx", "vhdx", "subformat=dynamic"),
        new("qcow2", "QCOW2", "稀疏磁盘 · QEMU / KVM",
            "QCOW2", "Sparse disk · QEMU / KVM", ".qcow2", "qcow2"),
        new("vmdk-sparse", "VMDK · 稀疏单文件", "monolithicSparse · VMware Workstation",
            "VMDK · sparse", "monolithicSparse · VMware Workstation", ".vmdk", "vmdk", "subformat=monolithicSparse"),
        new("vmdk-flat", "VMDK · 平坦磁盘", "monolithicFlat · 描述符 + 数据文件",
            "VMDK · flat", "monolithicFlat · descriptor + data", ".vmdk", "vmdk", "subformat=monolithicFlat"),
        new("vmdk-split", "VMDK · 2 GB 分卷", "twoGbMaxExtentSparse · 多文件",
            "VMDK · 2 GB extents", "twoGbMaxExtentSparse · multiple files", ".vmdk", "vmdk", "subformat=twoGbMaxExtentSparse"),
        new("vmdk-stream", "VMDK · 流优化", "streamOptimized · OVF 分发",
            "VMDK · stream optimized", "streamOptimized · OVF distribution", ".vmdk", "vmdk", "subformat=streamOptimized")
    ];
}

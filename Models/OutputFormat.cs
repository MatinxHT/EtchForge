namespace EtchForge.Models;

public sealed record OutputFormat(string Id, string Name, string Description, string Extension,
    string? QemuFormat = null, string? QemuOptions = null)
{
    public bool IsNativeEsxi => Id == "esxi";

    public static IReadOnlyList<OutputFormat> All { get; } =
    [
        new("esxi", "ESXi flat VMDK", "双文件 · VMFS 描述符 + 原始数据", ".vmdk"),
        new("vhd", "VHD", "固定大小 · 旧版 Hyper-V / Virtual PC", ".vhd", "vpc", "subformat=fixed"),
        new("vhdx", "VHDX", "动态大小 · Hyper-V", ".vhdx", "vhdx", "subformat=dynamic"),
        new("qcow2", "QCOW2", "稀疏磁盘 · QEMU / KVM", ".qcow2", "qcow2"),
        new("vmdk-sparse", "VMDK · 稀疏单文件", "monolithicSparse · VMware Workstation", ".vmdk", "vmdk", "subformat=monolithicSparse"),
        new("vmdk-flat", "VMDK · 平坦磁盘", "monolithicFlat · 描述符 + 数据文件", ".vmdk", "vmdk", "subformat=monolithicFlat"),
        new("vmdk-split", "VMDK · 2 GB 分卷", "twoGbMaxExtentSparse · 多文件", ".vmdk", "vmdk", "subformat=twoGbMaxExtentSparse"),
        new("vmdk-stream", "VMDK · 流优化", "streamOptimized · OVF 分发", ".vmdk", "vmdk", "subformat=streamOptimized")
    ];
}

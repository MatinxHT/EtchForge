using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using EtchForge.Models;
using EtchForge.Services;

namespace EtchForge;

public sealed class ImageDetailsWindow : Window
{
    public ImageDetailsWindow(ImageInfo image, UiLanguage language)
    {
        var en = language == UiLanguage.English;
        string V(string value) => !en ? value : value switch
        {
            "未识别 / 无分区" => "Unknown / unpartitioned",
            "ISO 9660 / 光盘" => "ISO 9660 / optical disc",
            "UDF / 光盘" => "UDF / optical disc",
            "分区范围正常" => "Partition ranges valid",
            "GPT 主/备表头与分区项 CRC 正常" => "GPT primary/backup headers and entries valid",
            "已识别光盘卷描述符" => "Optical volume descriptor detected",
            "没有有效的 MBR/GPT 标记" => "No valid MBR/GPT marker",
            "仅有引导扇区签名，无有效分区项" => "Boot sector signature without valid partitions",
            "无分区表，识别到卷" => "Volume found without partition table",
            "MBR 活动分区" => "MBR active partition",
            "未发现活动分区" => "No active partition found",
            "EFI 系统分区" => "EFI system partition",
            "未发现 EFI 系统分区" => "No EFI system partition found",
            "光盘引导状态未检查" => "Optical boot status not checked",
            "扩展分区" => "Extended partition",
            "光盘卷" => "Optical volume",
            "Linux 数据" => "Linux data",
            "Microsoft 数据" => "Microsoft data",
            "容器" => "Container",
            "未知" => "Unknown",
            _ => value
        };
        Title = en ? "Image details" : "镜像详情";
        Width = 760;
        Height = 640;
        MinWidth = 600;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush.Parse("#F9FAF6");
        Foreground = Brush.Parse("#27372B");
        var panel = new StackPanel { Spacing = 9, Margin = new Thickness(24) };
        void Line(string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            panel.Children.Add(new TextBlock { Text = $"{label}: {value}", TextWrapping = TextWrapping.Wrap });
        }
        panel.Children.Add(new TextBlock { Text = image.FileName, FontSize = 21, FontWeight = FontWeight.SemiBold });
        Line(en ? "Path" : "路径", image.Path);
        Line(en ? "Format" : "格式", image.Format == "vpc" ? "VHD" : image.Format.ToUpperInvariant());
        Line(en ? "File size" : "文件大小", $"{image.Bytes:N0} bytes");
        Line(en ? "Virtual capacity" : "虚拟容量", $"{image.VirtualBytes:N0} bytes");
        if (image.AllocatedBytes is { } allocated)
            Line(en ? "Host space used" : "宿主机占用", $"{allocated:N0} bytes ({allocated * 100d / image.VirtualBytes:0.0}%)");
        Line(en ? "Modified (UTC)" : "修改时间 (UTC)", image.LastWriteTimeUtc.ToString("u"));
        Line(en ? "Logical sector" : "逻辑扇区", $"{image.LogicalSectorSize} bytes");
        Line(en ? "Layout" : "分区布局", V(image.PartitionScheme));
        Line(en ? "Layout check" : "布局检查", V(image.LayoutHealth));
        Line(en ? "Boot clues" : "启动线索", V(image.BootHints));
        Line(en ? "Backing file" : "后备文件", image.BackingFile);
        Line(en ? "Selected file SHA-256" : "所选文件 SHA-256", image.Sha256);
        Line(en ? "Selected file MD5" : "所选文件 MD5", image.Md5);
        panel.Children.Add(new TextBlock { Text = en ? "Compare a published SHA-256" : "核对发布方 SHA-256", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 9, 0, 0) });
        var expected = new TextBox { Name = "ExpectedShaText", PlaceholderText = en ? "Paste the expected SHA-256" : "粘贴预期 SHA-256" };
        var comparison = new TextBlock { Name = "HashComparisonText" };
        expected.TextChanged += (_, _) =>
        {
            var value = expected.Text?.Trim();
            comparison.Text = string.IsNullOrEmpty(value) ? "" :
                value.Length != 64 || !value.All(Uri.IsHexDigit) ? (en ? "Enter a 64-character hex value" : "请输入 64 位十六进制校验值") :
                value.Equals(image.Sha256, StringComparison.OrdinalIgnoreCase) ? (en ? "Match" : "一致") : (en ? "Mismatch" : "不一致");
        };
        panel.Children.Add(expected);
        panel.Children.Add(comparison);
        panel.Children.Add(new TextBlock { Text = en ? "Partitions / volumes" : "分区 / 卷", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 9, 0, 0) });
        if (image.Partitions.Count == 0)
            Line(en ? "Result" : "结果", en ? "No recognized partitions" : "未识别到分区");
        foreach (var part in image.Partitions)
        {
            var size = part.SizeBytes >= 1024L * 1024 * 1024
                ? $"{part.SizeBytes / 1024d / 1024d / 1024d:0.00} GiB"
                : $"{part.SizeBytes / 1024d / 1024d:0.0} MiB";
            Line($"#{part.Number} {V(part.Type)}",
                $"{part.StartBytes:N0} B · {size} · {V(part.FileSystem)}" +
                (string.IsNullOrWhiteSpace(part.Label) ? "" : $" · {part.Label}") +
                (part.Bootable ? (en ? " · boot clue" : " · 启动线索") : ""));
        }
        if (!string.IsNullOrWhiteSpace(image.ContainerDetails))
        {
            panel.Children.Add(new TextBlock { Text = en ? "Container metadata" : "容器元数据", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 9, 0, 0) });
            panel.Children.Add(new TextBox { Text = image.ContainerDetails, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90 });
        }
        if (image.IsOptical)
            Line(en ? "Note" : "提示", en ? "Optical images cannot be converted into a bootable hard disk here." : "光盘镜像不能在此直接转换为可启动硬盘。");
        Content = new ScrollViewer { Content = panel };
    }
}

namespace EtchForge.Services;

public static class UiText
{
    public static string Get(string key, UiLanguage language)
    {
        var (zh, en) = key switch
        {
            "settings" => ("设置", "Settings"),
            "language" => ("界面语言", "Display language"),
            "engine" => ("转换引擎", "Conversion engine"),
            "engineReady" => ("ESXi 与 qemu-img 均可用", "ESXi and qemu-img are available"),
            "engineMissing" => ("内置 ESXi 可用；其他格式需要 qemu-img", "Built-in ESXi is ready; other formats need qemu-img"),
            "close" => ("关闭", "Close"),
            "saveFailed" => ("语言已切换，但设置未能保存。", "Language changed, but the setting could not be saved."),
            "chooseImage" => ("选择 IMG 镜像", "Choose IMG image"),
            "chooseFolder" => ("选择保存位置", "Choose save location"),
            "convert" => ("开始转换", "Convert"),
            "cancel" => ("取消转换", "Cancel conversion"),
            "openFolder" => ("打开输出文件夹", "Open output folder"),
            "copy" => ("复制", "Copy"),
            "copied" => ("已复制", "Copied"),
            "copyFailed" => ("复制失败", "Copy failed"),
            "localOnly" => ("所有文件都在本机处理", "All files stay on this computer"),
            "folderHint" => ("导入后建议保存位置", "Suggested after import"),
            "pathHint" => ("选择镜像后显示产物路径", "Output path appears after import"),
            "resultHint" => ("转换完成后显示产物信息", "Output details appear after conversion"),
            "elapsed" => ("用时", "Elapsed"),
            "products" => ("产物信息", "Output files"),
            "files" => ("个文件", "files"),
            "reading" => ("正在读取镜像…", "Reading image…"),
            "calculating" => ("正在计算…", "Calculating…"),
            "calculatingHashes" => ("计算 SHA-256 / MD5", "Calculating SHA-256 / MD5"),
            "imageDetails" => ("读取镜像结构并计算校验值", "Reading disk layout and checksums"),
            "readFailed" => ("镜像读取失败", "Could not read image"),
            "ready" => ("校验完成 · 可开始转换", "Checksums ready · conversion available"),
            "needsQemu" => ("此格式需要安装 qemu-img", "This format requires qemu-img"),
            "waiting" => ("等待开始", "Ready to convert"),
            "preparing" => ("准备转换…", "Preparing conversion…"),
            "converting" => ("正在转换镜像…", "Converting image…"),
            "completed" => ("转换完成", "Conversion complete"),
            "canceled" => ("已取消；临时文件已清理", "Canceled; temporary files removed"),
            "failed" => ("转换失败", "Conversion failed"),
            "gpt" => ("GPT · 带保护 MBR", "GPT · protective MBR"),
            "unknownPartitions" => ("未识别分区表 / 无分区磁盘", "Unknown partition table / unpartitioned disk"),
            _ => (key, key)
        };
        return language == UiLanguage.English ? en : zh;
    }
}

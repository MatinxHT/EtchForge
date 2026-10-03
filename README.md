# EtchForge

一个用 Avalonia 12 和 .NET 10 编写的本地磁盘镜像转换桌面应用。固定 21:9 的主窗口沿一条横向流程完成：左侧导入 raw `.img`，中间选择格式、保存位置并转换，右侧打开输出文件夹、查看耗时和产物信息。导入后直接显示 SHA-256 和 MD5，可选中文本或点击对应的“复制”按钮。右上角设置可在简体中文与 English 之间即时切换，选择会保存到本机。不会连接 ESXi、Hyper-V 或其他虚拟机平台。

界面采用 CommunityToolkit.Mvvm 的可观察属性与命令；Microsoft.Extensions.Hosting 管理应用生命周期，Microsoft.Extensions.DependencyInjection 负责窗口、ViewModel 和服务的创建。

![界面预览](docs/preview.png)

## 运行

需要 .NET 10 SDK。Windows、macOS 和 Linux 都使用同一份项目：

```sh
dotnet restore
dotnet run
```

在 VS Code 中打开本仓库文件夹，按 **F5** 并选择 **EtchForge (.NET 10)** 即可先构建再调试。需要安装 C# 扩展或 C# Dev Kit。

应用内置 **ESXi flat VMDK** 转换，无需其他程序。选择 VHD、VHDX、QCOW2 或其他 VMDK 类型时，需要在本机安装 [QEMU 的 `qemu-img`](https://www.qemu.org/docs/master/tools/qemu-img.html)，并将其加入 `PATH`；也可用 `QEMU_IMG_PATH` 环境变量指定可执行文件。macOS Homebrew 通常使用 `brew install qemu`。设置窗口会显示转换引擎检测结果；缺少 `qemu-img` 时仍可使用内置的 ESXi 格式。

## 支持的输出

| 格式 | 实现 | 说明 |
| --- | --- | --- |
| ESXi flat VMDK | 内置 | VMFS 描述符 + 原始数据文件；字节保持不变 |
| VHD | qemu-img | 固定大小 |
| VHDX | qemu-img | 动态大小 |
| QCOW2 | qemu-img | 默认兼容格式 |
| VMDK monolithicSparse | qemu-img | 稀疏单文件 |
| VMDK monolithicFlat | qemu-img | 描述符和数据文件 |
| VMDK twoGbMaxExtentSparse | qemu-img | 2 GB 稀疏分卷 |
| VMDK streamOptimized | qemu-img | 用于 OVF 分发 |

输入目前限定为**完整的 raw 磁盘镜像**，且大小须为 512 字节的整数倍。扩展名为 `.img` 不一定代表 raw；程序会拒绝可识别的 QCOW2、VHDX 和 VMDK 容器，但无法识别所有误命名文件。转换运行前应停止对源镜像的写入。

导入后程序按流读取输入，显示大小、MBR/GPT 概况、SHA-256 和 MD5。输出写入目标目录内的临时文件夹，完成后再移动到目标路径；已有文件不会被覆盖。取消或转换失败时清理临时文件。

## 验证

```sh
dotnet run --project tests/EtchForge.Smoke/EtchForge.Smoke.csproj
```

该测试使用 1 MiB 临时镜像检查哈希、ESXi 描述符和完整字节内容；通过 Host 解析 ViewModel 与窗口，执行导入、转换、复制命令，并用 Avalonia Headless 验证数据绑定、界面渲染和中英语言切换。测试不依赖 `qemu-img`；其他格式需在已安装 QEMU 的平台上进一步验证。

## 项目结构

- `AppHost.cs`：Generic Host 与依赖注入注册；`Program.cs` 启停 Host。
- `MainWindow.axaml`、`SettingsWindow.axaml`：视图及数据绑定，代码隐藏仅处理视图生命周期。
- `ViewModels/`：界面状态、异步命令、进度与语言更新。
- `Services/DesktopInteraction.cs`：文件选择、剪贴板、设置弹窗和文件管理器的桌面接口。
- `Services/ImageOperations.cs`：镜像检查与转换服务接口及适配器。
- `Services/AppPreferences.cs`：语言设置持久化与变更通知。
- `Services/ImageInspector.cs`：格式初筛、磁盘信息和哈希。
- `Services/ConversionService.cs`：内置 ESXi 转换、`qemu-img` 调用、进度和暂存发布。
- `Models/OutputFormat.cs`：支持格式及其转换参数。
- `tests/EtchForge.Smoke`：转换与界面烟测。

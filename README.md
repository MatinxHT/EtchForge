# EtchForge

一个用 Avalonia 12 和 .NET 10 编写的本地磁盘镜像转换桌面应用。固定 21:9 的主窗口沿一条横向流程完成：左侧导入镜像，中间选择格式、保存位置并转换，右侧打开输出文件夹、查看耗时和产物信息。导入后直接显示 SHA-256 和 MD5，详情窗口显示分区、文件系统、容器元数据，并可粘贴预期 SHA-256 核对。右上角设置可在简体中文与 English 之间即时切换，选择会保存到本机。不会连接 ESXi、Hyper-V 或其他虚拟机平台。

界面采用 CommunityToolkit.Mvvm 的可观察属性与命令；Microsoft.Extensions.Hosting 管理应用生命周期，Microsoft.Extensions.DependencyInjection 负责窗口、ViewModel 和服务的创建。

![界面预览](docs/preview.png)

## 运行

需要 .NET 10 SDK。Windows、macOS 和 Linux 都使用同一份项目：

```sh
dotnet restore
dotnet run
```

在 VS Code 中打开本仓库文件夹，按 **F5** 并选择 **EtchForge (.NET 10)** 即可先构建再调试。需要安装 C# 扩展或 C# Dev Kit。

### macOS 应用包与图标

在 macOS 上运行 `./scripts/package-macos.sh`，会按当前 CPU 架构发布应用包到 `dist/osx-arm64/EtchForge.app` 或 `dist/osx-x64/EtchForge.app`。默认产物需要本机安装 .NET 10 运行时；传入 `--self-contained` 可发布自包含版本，但需要相应的 .NET runtime pack。也可传入 `arm64` 或 `x64` 指定目标架构。用 Finder 或 `open dist/osx-arm64/EtchForge.app` 启动应用包，Dock 和 Finder 图标来自 `Contents/Resources/etchforge.icns`。直接运行 `dotnet run` 或 `bin/` 下的裸可执行文件不会提供完整的 macOS 应用包图标。

应用内置 **raw 到 ESXi flat VMDK** 转换，无需其他程序。读取 VMDK、VHD、VHDX、QCOW2、VDI、DMG，以及其他格式的转换，需要在本机安装 [QEMU 的 `qemu-img`](https://www.qemu.org/docs/master/tools/qemu-img.html)，并将其加入 `PATH`；也可用 `QEMU_IMG_PATH` 环境变量指定可执行文件。设置窗口提供 [QEMU 官方下载与安装说明](https://www.qemu.org/download/)入口，页面含 Windows、macOS 和 Linux 的安装方式。缺少 `qemu-img` 时仍可读取 raw 与 ISO，并可将 raw 转为 ESXi flat VMDK。

## 支持的输出

| 格式 | 实现 | 说明 |
| --- | --- | --- |
| ESXi flat VMDK | 内置 | VMFS 描述符 + 原始数据文件；字节保持不变 |
| VHD | qemu-img | 固定大小 |
| VHDX | qemu-img | 动态大小 |
| QCOW2 | qemu-img | 默认兼容格式 |
| VDI | qemu-img | VirtualBox 动态磁盘 |
| RAW | qemu-img | 原始磁盘 |
| VMDK monolithicSparse | qemu-img | 稀疏单文件 |
| VMDK monolithicFlat | qemu-img | 描述符和数据文件 |
| VMDK twoGbMaxExtentSparse | qemu-img | 2 GB 稀疏分卷 |
| VMDK streamOptimized | qemu-img | 用于 OVF 分发 |

输入支持 raw（`.img`、`.raw`、`.dd`、`.ima`、`.bin`）、VMDK、VHD、VHDX、QCOW2、VDI、DMG；ISO 9660 光盘镜像可检查，但不能当作可启动硬盘转换。raw 大小须为 512 字节的整数倍；扩展名不代表实际内容。容器识别依赖 `qemu-img info`，多文件 VMDK 与后备文件链需要其引用文件齐全。转换运行前应停止对源镜像及其后备文件的写入。

导入后程序按流计算输入文件的 SHA-256 和 MD5，显示文件大小、虚拟容量、分区布局及其健康状态。详情窗口展示分区范围、部分文件系统及卷标、启动线索、后备文件、容器元数据和哈希核对结果。GPT 会检查主/备表头与分区项 CRC、范围及重叠；MBR 检查分区范围及重叠。文件系统和启动信息是只读线索，不保证镜像可启动。输出写入目标目录内的临时文件夹，完成后再移动到目标路径；已有文件不会被覆盖。取消或转换失败时清理临时文件。

## 版本管理

应用版本统一在 `EtchForge.csproj` 的 `<Version>` 中维护，采用 `主版本.次版本.修订版本` 格式。升级时只需修改这一处；程序集版本、主页面底部版本号和 macOS 应用包版本会同步更新。页脚从构建生成的版本信息读取版本，并隐藏 Git 提交哈希等构建元数据。

推送到 `main` 后，Release workflow 会读取项目版本，使用 `v0.1.0` 这样的标签和发布名称。已发布的版本会自动跳过；需要发布新版时，先递增 `<Version>`。也可在 Actions 中对 `main` 手动运行 Release workflow。

发布流程为：检查版本 → 构建 Windows x64、macOS ARM64、Linux x64/ARM64 安装包 → 汇总产物 → 上传至草稿 → 公开发布。下载文件包含版本号，例如 `EtchForge-v0.1.0-windows-x64.zip`。构建失败不会创建发布页，上传失败则保留草稿，可重跑同一次 workflow 恢复；已公开的版本不会被覆盖。原有 `build-*` 历史 Release 保留不变。

## 验证

```sh
dotnet run --project tests/EtchForge.Smoke/EtchForge.Smoke.csproj
```

该测试使用临时 raw、GPT 和 ISO 样本检查哈希、分区校验、光盘识别、ESXi 描述符及完整字节内容；在 macOS/Linux 上通过 `qemu-img` 测试替身检查容器信息和转换命令的参数传递。它还通过 Host 解析 ViewModel 与窗口，执行导入、转换、复制命令，并用 Avalonia Headless 验证数据绑定、界面渲染和中英语言切换。真实容器转换仍需在已安装 QEMU 的平台上进一步验证。

## 项目结构

- `AppHost.cs`：Generic Host 与依赖注入注册；`Program.cs` 启停 Host。
- `MainWindow.axaml`、`SettingsWindow.axaml`：视图及数据绑定，代码隐藏仅处理视图生命周期。
- `ViewModels/`：界面状态、异步命令、进度与语言更新。
- `Services/DesktopInteraction.cs`：文件选择、剪贴板、设置弹窗和文件管理器的桌面接口。
- `Services/ImageOperations.cs`：镜像检查与转换服务接口及适配器。
- `Services/AppPreferences.cs`：语言设置持久化与变更通知。
- `Services/ImageInspector.cs`：格式初筛、磁盘信息和哈希。
- `Services/VirtualDiskReader.cs`、`Services/DiskLayoutInspector.cs`：虚拟扇区读取、分区与文件系统线索。
- `ImageDetailsWindow.cs`：镜像详情和 SHA-256 核对。
- `Services/ConversionService.cs`：内置 ESXi 转换、`qemu-img` 调用、进度和暂存发布。
- `Models/OutputFormat.cs`：支持格式及其转换参数。
- `tests/EtchForge.Smoke`：转换与界面烟测。

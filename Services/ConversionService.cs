using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using EtchForge.Models;

namespace EtchForge.Services;

public static class ConversionService
{
    private static readonly Regex ProgressPattern = new(@"\((\d+(?:\.\d+)?)/100%\)", RegexOptions.Compiled);

    public static string? FindQemuImg()
    {
        var candidates = new List<string>();
        var configured = Environment.GetEnvironmentVariable("QEMU_IMG_PATH");
        if (!string.IsNullOrWhiteSpace(configured)) candidates.Add(configured);
        candidates.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Combine(dir, OperatingSystem.IsWindows() ? "qemu-img.exe" : "qemu-img")));
        if (OperatingSystem.IsMacOS())
        {
            candidates.Add("/opt/homebrew/bin/qemu-img");
            candidates.Add("/usr/local/bin/qemu-img");
        }
        return candidates.FirstOrDefault(File.Exists);
    }

    public static async Task<IReadOnlyList<string>> ConvertAsync(ImageInfo image, OutputFormat format,
        string outputDirectory, IProgress<double>? progress, CancellationToken token)
    {
        if (!Directory.Exists(outputDirectory)) throw new DirectoryNotFoundException("输出文件夹不存在。");
        if (!File.Exists(image.Path) || new FileInfo(image.Path).Length != image.Bytes ||
            new FileInfo(image.Path).LastWriteTimeUtc != image.LastWriteTimeUtc)
            throw new IOException("源镜像已被移动或大小发生变化，请重新导入。");
        if (image.IsOptical) throw new InvalidDataException("ISO 光盘镜像不能作为虚拟硬盘转换。");
        var baseName = $"{Path.GetFileNameWithoutExtension(image.Path)}-{format.Id}";
        if (format.IsNativeEsxi && baseName.IndexOfAny(['"', '\\', '\n', '\r']) >= 0)
            throw new InvalidDataException("ESXi VMDK 文件名不能包含引号、反斜杠或换行。");
        var destination = Path.Combine(outputDirectory, baseName + format.Extension);
        if (File.Exists(destination)) throw new IOException($"输出文件已存在：{destination}");

        var staging = Path.Combine(outputDirectory, ".etchforge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var moved = new List<string>();
        try
        {
            var stagedOutput = Path.Combine(staging, baseName + format.Extension);
            if (format.IsNativeEsxi)
                await ConvertEsxiFlatAsync(image, staging, baseName, progress, token);
            else
            {
                var qemu = FindQemuImg() ?? throw new FileNotFoundException(
                    "此格式需要 qemu-img。请安装 QEMU，或设置 QEMU_IMG_PATH 指向 qemu-img 可执行文件。");
                await ConvertWithQemuAsync(qemu, image.Path, stagedOutput, image.Format,
                    format.QemuFormat!, format.QemuOptions, progress, token);
            }

            var generated = Directory.GetFiles(staging);
            if (generated.Length == 0 || !File.Exists(stagedOutput))
                throw new IOException("转换程序未生成预期的输出文件。");
            foreach (var source in generated)
            {
                var target = Path.Combine(outputDirectory, Path.GetFileName(source));
                if (File.Exists(target)) throw new IOException($"输出文件已存在：{target}");
            }
            // Publish the data extents before their descriptor. Roll back only files moved by this run.
            foreach (var source in generated.OrderBy(path => path == stagedOutput ? 1 : 0))
            {
                var target = Path.Combine(outputDirectory, Path.GetFileName(source));
                File.Move(source, target);
                moved.Add(target);
            }
            progress?.Report(1);
            return moved.AsReadOnly();
        }
        catch
        {
            foreach (var path in moved) File.Delete(path);
            throw;
        }
        finally { Directory.Delete(staging, recursive: true); }
    }

    private static async Task ConvertEsxiFlatAsync(ImageInfo image, string directory, string baseName,
        IProgress<double>? progress, CancellationToken token)
    {
        var flatName = baseName + "-flat.vmdk";
        var flatPath = Path.Combine(directory, flatName);
        if (image.Format != "raw")
        {
            var qemu = FindQemuImg() ?? throw new FileNotFoundException("转换此镜像需要 qemu-img。");
            await ConvertWithQemuAsync(qemu, image.Path, flatPath, image.Format, "raw", null, progress, token);
        }
        else
        {
        await using (var source = new FileStream(image.Path, FileMode.Open, FileAccess.Read, FileShare.Read,
            4 * 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
        await using (var target = new FileStream(flatPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            4 * 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            using var sha = System.Security.Cryptography.IncrementalHash.CreateHash(
                System.Security.Cryptography.HashAlgorithmName.SHA256);
            var buffer = ArrayPool<byte>.Shared.Rent(4 * 1024 * 1024);
            try
            {
                long copied = 0;
                int count;
                while ((count = await source.ReadAsync(buffer, token)) != 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, count), token);
                    sha.AppendData(buffer, 0, count);
                    copied += count;
                    progress?.Report(Math.Min(0.98, (double)copied / image.Bytes));
                }
                if (copied != image.Bytes) throw new IOException("复制期间源镜像大小发生变化。");
                await target.FlushAsync(token);
                if (!Convert.ToHexString(sha.GetHashAndReset()).Equals(image.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("源镜像自导入后发生变化；输出已放弃，请重新导入。");
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }
        }
        if (new FileInfo(flatPath).Length != image.VirtualBytes)
            throw new IOException("输出大小与源镜像不一致。");

        var sectors = image.VirtualBytes / 512;
        var cylinders = Math.Max(1, sectors / (255 * 63));
        var cid = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
        var descriptor = $"""
            # Disk DescriptorFile
            version=1
            encoding="UTF-8"
            CID={cid}
            parentCID=ffffffff
            createType="vmfs"

            # Extent description
            RW {sectors} VMFS "{flatName}"

            # The Disk Data Base
            #DDB
            ddb.adapterType = "lsilogic"
            ddb.geometry.cylinders = "{cylinders}"
            ddb.geometry.heads = "255"
            ddb.geometry.sectors = "63"
            """;
        await File.WriteAllTextAsync(Path.Combine(directory, baseName + ".vmdk"),
            descriptor + "\n", new UTF8Encoding(false), token);
    }

    private static async Task ConvertWithQemuAsync(string executable, string source, string destination,
        string sourceFormat, string targetFormat, string? options, IProgress<double>? progress, CancellationToken token)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var part in new[] { "convert", "-p", "-f", sourceFormat, "-O", targetFormat })
            start.ArgumentList.Add(part);
        if (options is not null)
        {
            start.ArgumentList.Add("-o");
            start.ArgumentList.Add(options);
        }
        start.ArgumentList.Add(source);
        start.ArgumentList.Add(destination);

        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new IOException("无法启动 qemu-img。");
        using var registration = token.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        });
        var output = new StringBuilder();
        var stderr = ReadOutputAsync(process.StandardError, output, progress);
        var stdout = ReadOutputAsync(process.StandardOutput, output, progress);
        try { await process.WaitForExitAsync(token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await Task.WhenAll(stderr, stdout);
            throw;
        }
        await Task.WhenAll(stderr, stdout);
        if (process.ExitCode != 0)
            throw new IOException($"qemu-img 退出码 {process.ExitCode}：{output.ToString().Trim()}");
    }

    private static async Task ReadOutputAsync(StreamReader reader, StringBuilder output, IProgress<double>? progress)
    {
        var buffer = new char[256];
        var tail = "";
        int count;
        while ((count = await reader.ReadAsync(buffer)) != 0)
        {
            var chunk = new string(buffer, 0, count);
            lock (output) output.Append(chunk);
            tail = (tail + chunk);
            foreach (Match match in ProgressPattern.Matches(tail))
                if (double.TryParse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, out var value))
                    progress?.Report(Math.Min(0.98, value / 100));
            if (tail.Length > 128) tail = tail[^128..];
        }
    }
}

using System.Diagnostics;
using System.Text.Json;

namespace EtchForge.Services;

internal sealed class VirtualDiskReader(string path, string format, string? qemu) : IAsyncDisposable
{
    private readonly FileStream? _raw = format is "raw" or "iso"
        ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.RandomAccess) : null;

    public async Task<byte[]> ReadAsync(long offset, int count, CancellationToken token)
    {
        if (offset < 0 || count < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        var result = new byte[count];
        if (_raw is not null)
        {
            _raw.Position = offset;
            var read = 0;
            while (read < count)
            {
                var next = await _raw.ReadAsync(result.AsMemory(read), token);
                if (next == 0) break;
                read += next;
            }
            return result;
        }
        if (qemu is null) throw new FileNotFoundException("读取此镜像需要 qemu-img。");
        var aligned = offset / 512 * 512;
        var prefix = checked((int)(offset - aligned));
        var blocks = (prefix + (long)count + 511) / 512;
        var temporary = Path.Combine(Path.GetTempPath(), "etchforge-read-" + Guid.NewGuid().ToString("N"));
        try
        {
            var start = new ProcessStartInfo(qemu) { UseShellExecute = false,
                RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true };
            foreach (var arg in new[] { "dd", "-f", format, "-O", "raw", "bs=512",
                $"skip={aligned / 512}", $"count={blocks}", $"if={path}", $"of={temporary}" })
                start.ArgumentList.Add(arg);
            await RunAsync(start, token);
            await using var stream = File.OpenRead(temporary);
            stream.Position = prefix;
            var read = 0;
            while (read < count)
            {
                var next = await stream.ReadAsync(result.AsMemory(read), token);
                if (next == 0) break;
                read += next;
            }
            return result;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public ValueTask DisposeAsync() => _raw?.DisposeAsync() ?? ValueTask.CompletedTask;

    public static async Task<(string Format, long VirtualBytes, long? AllocatedBytes, string? BackingFile, string? Details)>
        GetInfoAsync(string qemu, string path, CancellationToken token)
    {
        var start = new ProcessStartInfo(qemu) { UseShellExecute = false,
            RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true };
        foreach (var arg in new[] { "info", "--output=json", path }) start.ArgumentList.Add(arg);
        var output = await RunAsync(start, token);
        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;
        string? String(string key) => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
        long? Number(string key) => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt64() : null;
        var format = String("format") ?? throw new InvalidDataException("qemu-img 未返回镜像格式。");
        var size = Number("virtual-size") ?? throw new InvalidDataException("qemu-img 未返回虚拟容量。");
        var details = root.TryGetProperty("format-specific", out var specific) ? specific.ToString() : null;
        if (root.TryGetProperty("snapshots", out var snapshots) && snapshots.ValueKind == JsonValueKind.Array)
            details = $"Snapshots: {snapshots.GetArrayLength()}\n{details}";
        return (format, size, Number("actual-size"), String("backing-filename"), details);
    }

    internal static async Task<string> RunAsync(ProcessStartInfo start, CancellationToken token)
    {
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new IOException("无法启动 qemu-img。");
        using var registration = token.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        });
        var stdout = process.StandardOutput.ReadToEndAsync(token);
        var stderr = process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        var output = await stdout;
        var error = await stderr;
        if (process.ExitCode != 0) throw new InvalidDataException($"qemu-img: {error.Trim()}");
        return output;
    }
}

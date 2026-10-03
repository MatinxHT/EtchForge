using System.Diagnostics;
using System.Runtime.InteropServices;

namespace EtchForge.Services;

internal static class FileAllocation
{
    [DllImport("kernel32.dll", EntryPoint = "GetCompressedFileSizeW", SetLastError = true,
        CharSet = CharSet.Unicode)]
    private static extern uint GetCompressedFileSize(string path, out uint high);

    public static async Task<long?> GetAsync(string path, CancellationToken token)
    {
        if (OperatingSystem.IsWindows())
        {
            var low = GetCompressedFileSize(path, out var high);
            if (low == uint.MaxValue && Marshal.GetLastWin32Error() != 0) return null;
            return ((long)high << 32) | low;
        }
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
        {
            try
            {
                var start = new ProcessStartInfo("du") { UseShellExecute = false,
                    RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                start.ArgumentList.Add("-k");
                start.ArgumentList.Add(path);
                using var process = Process.Start(start);
                if (process is null) return null;
                var output = await process.StandardOutput.ReadToEndAsync(token);
                await process.WaitForExitAsync(token);
                var field = output.Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                return process.ExitCode == 0 && long.TryParse(field, out var kib) ? kib * 1024 : null;
            }
            catch (OperationCanceledException) { throw; }
            catch { return null; }
        }
        return null;
    }
}

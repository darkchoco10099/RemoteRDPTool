using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace RemoteRDPTool.Services;

public interface IRdpLauncher
{
    Task LaunchAsync(string host, string? username, string? password);
}

public sealed class WindowsRdpLauncher : IRdpLauncher
{
    public async Task LaunchAsync(string host, string? username, string? password)
    {
        if (string.IsNullOrWhiteSpace(host))
            return;

        host = host.Trim();

        if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
        {
            await RunAsync("cmdkey.exe", $"/generic:\"TERMSRV/{host}\" /user:\"{username}\" /pass:\"{password}\"");
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "mstsc.exe",
            Arguments = $"/v:{host}",
            UseShellExecute = true
        });
    }

    private static async Task RunAsync(string fileName, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false
        });

        if (process is null)
            return;

        await process.WaitForExitAsync();
    }
}

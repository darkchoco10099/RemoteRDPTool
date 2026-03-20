using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace RemoteRDPTool.Services;

public interface IRdpLauncher
{
  Task LaunchAsync(string host, string? username, string? password, CancellationToken cancellationToken = default);
}

public interface IShareDiskLauncher
{
  Task OpenAsync(string host, string shareDisk, string? username, string? password, CancellationToken cancellationToken = default);
}

public sealed class WindowsRdpLauncher : IRdpLauncher
{
  public async Task LaunchAsync(string host, string? username, string? password, CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(host))
      return;

    host = host.Trim();

    if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
    {
      await RunAsync("cmdkey.exe", $"/generic:\"TERMSRV/{host}\" /user:\"{username}\" /pass:\"{password}\"", cancellationToken);
    }

    var startedAt = DateTime.UtcNow;
    using var process = Process.Start(new ProcessStartInfo
    {
      FileName = "mstsc.exe",
      Arguments = $"/v:{host}",
      UseShellExecute = true
    });

    if (process is null)
      throw new InvalidOperationException("无法启动远程桌面客户端。");

    using var cancellationRegistration = RegisterKillOnCancel(process, startedAt, cancellationToken);
    await WaitForMainWindowOrExitAsync(process, cancellationToken);
  }

  private static async Task WaitForMainWindowOrExitAsync(Process process, CancellationToken cancellationToken)
  {
    while (true)
    {
      cancellationToken.ThrowIfCancellationRequested();

      try
      {
        if (process.HasExited || process.MainWindowHandle != IntPtr.Zero)
          return;
        process.Refresh();
      }
      catch
      {
        return;
      }

      await Task.Delay(120, cancellationToken);
    }
  }

  private static async Task RunAsync(string fileName, string arguments, CancellationToken cancellationToken)
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

    await process.WaitForExitAsync(cancellationToken);
  }

  private static CancellationTokenRegistration RegisterKillOnCancel(Process process, DateTime startedAt, CancellationToken cancellationToken)
  {
    return cancellationToken.Register(() =>
    {
      if (!IsFreshProcess(process, startedAt))
        return;
      TryKillProcess(process);
    });
  }

  private static bool IsFreshProcess(Process process, DateTime startedAt)
  {
    try
    {
      var started = process.StartTime.ToUniversalTime();
      return started >= startedAt.AddSeconds(-1);
    }
    catch
    {
      return true;
    }
  }

  private static void TryKillProcess(Process process)
  {
    try
    {
      if (!process.HasExited)
        process.Kill(true);
    }
    catch
    {
    }
  }
}

public sealed class WindowsShareDiskLauncher : IShareDiskLauncher
{
  public async Task OpenAsync(string host, string shareDisk, string? username, string? password, CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(shareDisk))
      return;

    var normalizedHost = host.Trim();
    var path = BuildSharePath(normalizedHost, shareDisk.Trim());
    if (string.IsNullOrWhiteSpace(path))
      return;

    var connected = false;
    try
    {
      if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
      {
        var connect = await ConnectShareAsync(path, username, password, cancellationToken);
        if (connect.ExitCode != 0 && IsMultipleCredentialConflict(connect))
        {
          await ClearHostSessionsAsync(normalizedHost, cancellationToken);
          connect = await ConnectShareAsync(path, username, password, cancellationToken);
          if (connect.ExitCode != 0 && IsMultipleCredentialConflict(connect))
          {
            await RunAsync("net.exe", "use * /delete /y", cancellationToken);
            connect = await ConnectShareAsync(path, username, password, cancellationToken);
          }
        }

        if (connect.ExitCode != 0)
        {
          throw new InvalidOperationException(BuildNetUseError(path, username, connect));
        }

        connected = true;
      }

      cancellationToken.ThrowIfCancellationRequested();

      var startedAt = DateTime.UtcNow;
      using var explorer = Process.Start(new ProcessStartInfo
      {
        FileName = "explorer.exe",
        Arguments = path,
        UseShellExecute = true
      });

      if (explorer is null)
        throw new InvalidOperationException("无法启动文件管理器。");

      using var cancellationRegistration = RegisterKillOnCancel(explorer, startedAt, cancellationToken);
      await WaitForMainWindowOrExitAsync(explorer, cancellationToken);
    }
    catch (OperationCanceledException)
    {
      if (connected)
      {
        await RunAsync("net.exe", $"use \"{path}\" /delete /y", CancellationToken.None);
      }

      throw;
    }
  }

  private static string BuildSharePath(string host, string shareDisk)
  {
    if (shareDisk.StartsWith(@"\\", StringComparison.Ordinal))
      return shareDisk;

    var cleaned = shareDisk.Trim().TrimStart('\\', '/').Replace('/', '\\');
    if (string.IsNullOrWhiteSpace(cleaned))
      return string.Empty;

    return $@"\\{host}\{cleaned}";
  }

  private static async Task RunAsync(string fileName, string arguments, CancellationToken cancellationToken)
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

    await process.WaitForExitAsync(cancellationToken);
  }

  private static async Task WaitForMainWindowOrExitAsync(Process process, CancellationToken cancellationToken)
  {
    while (true)
    {
      cancellationToken.ThrowIfCancellationRequested();

      try
      {
        if (process.HasExited || process.MainWindowHandle != IntPtr.Zero)
          return;
        process.Refresh();
      }
      catch
      {
        return;
      }

      await Task.Delay(120, cancellationToken);
    }
  }

  private static CancellationTokenRegistration RegisterKillOnCancel(Process process, DateTime startedAt, CancellationToken cancellationToken)
  {
    return cancellationToken.Register(() =>
    {
      if (!IsFreshProcess(process, startedAt))
        return;
      TryKillProcess(process);
    });
  }

  private static bool IsFreshProcess(Process process, DateTime startedAt)
  {
    try
    {
      var started = process.StartTime.ToUniversalTime();
      return started >= startedAt.AddSeconds(-1);
    }
    catch
    {
      return true;
    }
  }

  private static void TryKillProcess(Process process)
  {
    try
    {
      if (!process.HasExited)
        process.Kill(true);
    }
    catch
    {
    }
  }

  private static async Task<CommandResult> RunWithOutputAsync(string fileName, string arguments, CancellationToken cancellationToken)
  {
    using var process = Process.Start(new ProcessStartInfo
    {
      FileName = fileName,
      Arguments = arguments,
      CreateNoWindow = true,
      UseShellExecute = false,
      RedirectStandardOutput = true,
      RedirectStandardError = true
    });

    if (process is null)
      return new CommandResult(-1, string.Empty, "无法启动进程。");

    var outputTask = process.StandardOutput.ReadToEndAsync();
    var errorTask = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync(cancellationToken);
    var output = await outputTask;
    var error = await errorTask;
    return new CommandResult(process.ExitCode, output, error);
  }

  private static Task<CommandResult> ConnectShareAsync(string path, string username, string password, CancellationToken cancellationToken)
      => RunWithOutputAsync("net.exe", $"use \"{path}\" /user:\"{username}\" \"{password}\" /persistent:no", cancellationToken);

  private static bool IsMultipleCredentialConflict(CommandResult result)
  {
    var text = $"{result.Output}\n{result.Error}";
    return text.Contains("1219", StringComparison.OrdinalIgnoreCase);
  }

  private static async Task ClearHostSessionsAsync(string host, CancellationToken cancellationToken)
  {
    var list = await RunWithOutputAsync("net.exe", "use", cancellationToken);
    var targets = ExtractHostTargets(list.Output, host);
    targets.Add($@"\\{host}\IPC$");

    foreach (var target in targets.Distinct(StringComparer.OrdinalIgnoreCase))
    {
      await RunAsync("net.exe", $"use \"{target}\" /delete /y", cancellationToken);
    }
  }

  private static List<string> ExtractHostTargets(string output, string host)
  {
    var targets = new List<string>();
    if (string.IsNullOrWhiteSpace(output))
      return targets;

    var matches = Regex.Matches(output, @"\\\\[^\s]+");
    foreach (Match match in matches)
    {
      var value = match.Value.Trim();
      if (value.Length <= 2 || !value.StartsWith(@"\\", StringComparison.Ordinal))
        continue;

      var slash = value.IndexOf('\\', 2);
      var remoteHost = slash > 2 ? value.Substring(2, slash - 2) : value.Substring(2);
      if (string.Equals(remoteHost, host, StringComparison.OrdinalIgnoreCase))
        targets.Add(value);
    }

    return targets;
  }

  private static string BuildNetUseError(string path, string username, CommandResult result)
  {
    var output = string.IsNullOrWhiteSpace(result.Output) ? "(empty)" : result.Output.Trim();
    var error = string.IsNullOrWhiteSpace(result.Error) ? "(empty)" : result.Error.Trim();
    return $"共享盘认证失败。\n路径: {path}\n账号: {username}\n退出码: {result.ExitCode}\n输出: {output}\n错误: {error}";
  }

  private sealed record CommandResult(int ExitCode, string Output, string Error);
}

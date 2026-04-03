using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RemoteRDPTool.Services;

public interface IProcessProbeService
{
  Task<ProcessProbeResult> ProbeAsync(string host, string username, string password, IReadOnlyList<string> expectedProcessNames, CancellationToken cancellationToken = default);
}

public sealed record ProcessProbeResult(bool IsSuccess, IReadOnlyList<string> MissingProcesses, string ErrorMessage);

public sealed class WindowsProcessProbeService : IProcessProbeService
{
  public async Task<ProcessProbeResult> ProbeAsync(string host, string username, string password, IReadOnlyList<string> expectedProcessNames, CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(host))
      return new ProcessProbeResult(false, expectedProcessNames, "主机为空");

    var expected = NormalizeProcessNames(expectedProcessNames);
    if (expected.Count == 0)
      return new ProcessProbeResult(true, [], string.Empty);

    var result = await RunTaskListAsync(host.Trim(), username.Trim(), password, cancellationToken);
    if (result.ExitCode != 0)
    {
      var error = string.IsNullOrWhiteSpace(result.Error)
          ? result.Output.Trim()
          : result.Error.Trim();
      return new ProcessProbeResult(false, expectedProcessNames, string.IsNullOrWhiteSpace(error) ? "进程检测失败" : error);
    }

    var running = ParseRunningProcessNames(result.Output);
    var missing = expected
        .Where(name => !running.Contains(name))
        .ToList();
    return new ProcessProbeResult(true, missing, string.Empty);
  }

  private static HashSet<string> ParseRunningProcessNames(string output)
  {
    var lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var line in lines)
    {
      var firstColumn = TryReadFirstCsvField(line);
      if (string.IsNullOrWhiteSpace(firstColumn))
        continue;

      var normalized = NormalizeProcessName(firstColumn);
      if (!string.IsNullOrWhiteSpace(normalized))
        names.Add(normalized);
    }

    return names;
  }

  private static string? TryReadFirstCsvField(string line)
  {
    var trimmed = line.Trim();
    if (trimmed.Length == 0)
      return null;

    if (trimmed[0] == '"')
    {
      var quoteEnd = trimmed.IndexOf('"', 1);
      if (quoteEnd <= 1)
        return null;
      return trimmed[1..quoteEnd];
    }

    var comma = trimmed.IndexOf(',');
    return comma > 0 ? trimmed[..comma].Trim() : trimmed;
  }

  private static HashSet<string> NormalizeProcessNames(IReadOnlyList<string> processNames)
  {
    return processNames
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Select(NormalizeProcessName)
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Select(x => x!)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
  }

  private static string NormalizeProcessName(string processName)
  {
    var value = processName.Trim();
    if (value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
      value = value[..^4];
    return value;
  }

  private static async Task<CommandResult> RunTaskListAsync(string host, string username, string password, CancellationToken cancellationToken)
  {
    using var process = Process.Start(new ProcessStartInfo
    {
      FileName = "tasklist.exe",
      Arguments = $"/S \"{host}\" /U \"{username}\" /P \"{password}\" /FO CSV /NH",
      CreateNoWindow = true,
      UseShellExecute = false,
      RedirectStandardOutput = true,
      RedirectStandardError = true
    });

    if (process is null)
      return new CommandResult(-1, string.Empty, "无法启动 tasklist。");

    var outputTask = process.StandardOutput.ReadToEndAsync();
    var errorTask = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync(cancellationToken);
    var output = await outputTask;
    var error = await errorTask;
    return new CommandResult(process.ExitCode, output, error);
  }

  private sealed record CommandResult(int ExitCode, string Output, string Error);
}

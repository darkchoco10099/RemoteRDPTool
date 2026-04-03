using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using RemoteRDPTool.Models;

namespace RemoteRDPTool.Services;

public interface IAppConfigStore
{
  Task<AppConfig> LoadAsync();
  Task SaveAsync(AppConfig config);
  string ConfigPath { get; }
}

public interface IAppSettingsStore
{
  Task<AppSettings> LoadAsync();
  Task SaveAsync(AppSettings settings);
  string ConfigPath { get; }
}

public sealed class FileAppConfigStore : IAppConfigStore
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    WriteIndented = true
  };

  public FileAppConfigStore(string configPath)
  {
    ConfigPath = configPath;
  }

  public string ConfigPath { get; }

  public async Task<AppConfig> LoadAsync()
  {
    var data = await LoadDataAsync(ConfigPath);
    return new AppConfig
    {
      Groups = data.Groups
    };
  }

  public async Task SaveAsync(AppConfig config)
  {
    var data = await LoadDataAsync(ConfigPath);
    data.Groups = config.Groups ?? [];
    EnsureValidData(data);
    await SaveDataAsync(ConfigPath, data);
  }

  internal static async Task<AppData> LoadDataAsync(string path)
  {
    if (!File.Exists(path))
    {
      var initial = CreateInitialData();
      await SaveDataAsync(path, initial);
      return initial;
    }

    await using var stream = File.OpenRead(path);
    var data = await JsonSerializer.DeserializeAsync<AppData>(stream, JsonOptions) ?? CreateInitialData();
    EnsureValidData(data);
    return data;
  }

  internal static async Task SaveDataAsync(string path, AppData data)
  {
    EnsureValidData(data);
    var dir = Path.GetDirectoryName(path);
    if (!string.IsNullOrWhiteSpace(dir))
      Directory.CreateDirectory(dir);

    var json = JsonSerializer.Serialize(data, JsonOptions);
    await File.WriteAllTextAsync(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
  }

  internal static AppData CreateInitialData()
  {
    return new AppData
    {
      Groups =
      [
        new RdpGroup { Name = "默认", Connections = [] }
      ],
      Settings = new AppSettings()
    };
  }

  internal static void EnsureValidData(AppData data)
  {
    data.Groups ??= [];
    if (data.Groups.Count == 0)
      data.Groups.Add(new RdpGroup { Name = "默认", Connections = [] });

    foreach (var group in data.Groups)
    {
      group.Connections ??= [];
      foreach (var connection in group.Connections)
        connection.ShareDisk ??= string.Empty;
    }

    data.Settings ??= new AppSettings();
    data.Settings.PingIntervalSeconds = Math.Max(2, data.Settings.PingIntervalSeconds);
    data.Settings.ReducedPingIntervalSeconds = Math.Max(data.Settings.PingIntervalSeconds, data.Settings.ReducedPingIntervalSeconds);
    data.Settings.ProcessWatchIntervalSeconds = Math.Max(5, data.Settings.ProcessWatchIntervalSeconds);
    data.Settings.ProcessWatchTimeoutSeconds = Math.Clamp(data.Settings.ProcessWatchTimeoutSeconds, 3, 30);
    data.Settings.ProcessWatchNames ??= [];
    data.Settings.ProcessWatchNames = data.Settings.ProcessWatchNames
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Select(x => x.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
    if (string.IsNullOrWhiteSpace(data.Settings.SummonHotkey))
      data.Settings.SummonHotkey = "Ctrl+R";
    else
      data.Settings.SummonHotkey = data.Settings.SummonHotkey.Trim();
  }
}

public sealed class FileAppSettingsStore : IAppSettingsStore
{
  public FileAppSettingsStore(string configPath)
  {
    ConfigPath = configPath;
  }

  public string ConfigPath { get; }

  public async Task<AppSettings> LoadAsync()
  {
    var data = await FileAppConfigStore.LoadDataAsync(ConfigPath);
    return data.Settings;
  }

  public async Task SaveAsync(AppSettings settings)
  {
    var data = await FileAppConfigStore.LoadDataAsync(ConfigPath);
    data.Settings = settings ?? new AppSettings();
    FileAppConfigStore.EnsureValidData(data);
    await FileAppConfigStore.SaveDataAsync(ConfigPath, data);
  }
}

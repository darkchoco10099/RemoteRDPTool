using System;
using System.IO;
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
    if (!File.Exists(ConfigPath))
    {
      var dir = Path.GetDirectoryName(ConfigPath);
      if (!string.IsNullOrWhiteSpace(dir))
        Directory.CreateDirectory(dir);

      var initial = new AppConfig
      {
        Groups =
          [
              new RdpGroup { Name = "默认", Connections = [] }
          ]
      };

      await SaveAsync(initial);
      return initial;
    }

    await using var stream = File.OpenRead(ConfigPath);
    var config = await JsonSerializer.DeserializeAsync<AppConfig>(stream, JsonOptions);
    if (config is null)
      return new AppConfig();

    config.Groups ??= [];
    if (config.Groups.Count == 0)
      config.Groups.Add(new RdpGroup { Name = "默认", Connections = [] });

    return config;
  }

  public async Task SaveAsync(AppConfig config)
  {
    var dir = Path.GetDirectoryName(ConfigPath);
    if (!string.IsNullOrWhiteSpace(dir))
      Directory.CreateDirectory(dir);

    var json = JsonSerializer.Serialize(config, JsonOptions);
    await File.WriteAllTextAsync(ConfigPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
  }
}

public sealed class FileAppSettingsStore : IAppSettingsStore
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    WriteIndented = true
  };

  public FileAppSettingsStore(string configPath)
  {
    ConfigPath = configPath;
  }

  public string ConfigPath { get; }

  public async Task<AppSettings> LoadAsync()
  {
    if (!File.Exists(ConfigPath))
    {
      var initial = new AppSettings();
      await SaveAsync(initial);
      return initial;
    }

    await using var stream = File.OpenRead(ConfigPath);
    var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions) ?? new AppSettings();
    settings.PingIntervalSeconds = Math.Max(2, settings.PingIntervalSeconds);
    settings.ReducedPingIntervalSeconds = Math.Max(settings.PingIntervalSeconds, settings.ReducedPingIntervalSeconds);
    return settings;
  }

  public async Task SaveAsync(AppSettings settings)
  {
    var dir = Path.GetDirectoryName(ConfigPath);
    if (!string.IsNullOrWhiteSpace(dir))
      Directory.CreateDirectory(dir);

    settings.PingIntervalSeconds = Math.Max(2, settings.PingIntervalSeconds);
    settings.ReducedPingIntervalSeconds = Math.Max(settings.PingIntervalSeconds, settings.ReducedPingIntervalSeconds);
    var json = JsonSerializer.Serialize(settings, JsonOptions);
    await File.WriteAllTextAsync(ConfigPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
  }
}

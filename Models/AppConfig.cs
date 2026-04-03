using System;
using System.Collections.Generic;

namespace RemoteRDPTool.Models;

public sealed class AppConfig
{
  public List<RdpGroup> Groups { get; set; } = [];
}

public sealed class AppData
{
  public List<RdpGroup> Groups { get; set; } = [];

  public AppSettings Settings { get; set; } = new();
}

public sealed class AppSettings
{
  public bool AutoReducePingFrequency { get; set; } = true;

  public int PingIntervalSeconds { get; set; } = 5;

  public int ReducedPingIntervalSeconds { get; set; } = 8;

  public string SummonHotkey { get; set; } = "Ctrl+R";

  public bool ProcessWatchEnabled { get; set; }

  public int ProcessWatchIntervalSeconds { get; set; } = 20;

  public int ProcessWatchTimeoutSeconds { get; set; } = 10;

  public List<string> ProcessWatchNames { get; set; } = [];

  public bool ShowProcessDebugLog { get; set; }

  public string CardIconStyle { get; set; } = "color";
}

public sealed class RdpGroup
{
  public string Name { get; set; } = "默认";

  public List<RdpConnection> Connections { get; set; } = [];
}

public sealed class RdpConnection
{
  public Guid Id { get; set; } = Guid.NewGuid();

  public string Name { get; set; } = string.Empty;

  public string Host { get; set; } = string.Empty;

  public string Username { get; set; } = string.Empty;

  public string? Password { get; set; }

  public string ShareDisk { get; set; } = string.Empty;

  public bool EnableProcessWatch { get; set; }
}

public sealed record RdpConnectionEntry
{
  public Guid Id { get; init; }

  public string Name { get; init; } = string.Empty;

  public string Host { get; init; } = string.Empty;

  public string Username { get; init; } = string.Empty;

  public string? Password { get; init; }

  public string ShareDisk { get; init; } = string.Empty;

  public string Group { get; init; } = "默认";

  public bool EnableProcessWatch { get; init; }

  public static RdpConnectionEntry FromConnection(string groupName, RdpConnection connection)
  {
    return new RdpConnectionEntry
    {
      Id = connection.Id,
      Name = connection.Name,
      Host = connection.Host,
      Username = connection.Username,
      Password = connection.Password,
      ShareDisk = connection.ShareDisk,
      Group = groupName,
      EnableProcessWatch = connection.EnableProcessWatch
    };
  }

  public RdpConnection ToConnection()
  {
    return new RdpConnection
    {
      Id = Id,
      Name = Name,
      Host = Host,
      Username = Username,
      Password = Password,
      ShareDisk = ShareDisk,
      EnableProcessWatch = EnableProcessWatch
    };
  }
}

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
}

public sealed record RdpConnectionEntry
{
  public Guid Id { get; init; }

  public string Name { get; init; } = string.Empty;

  public string Host { get; init; } = string.Empty;

  public string Username { get; init; } = string.Empty;

  public string? Password { get; init; }

  public string Group { get; init; } = "默认";

  public static RdpConnectionEntry FromConnection(string groupName, RdpConnection connection)
  {
    return new RdpConnectionEntry
    {
      Id = connection.Id,
      Name = connection.Name,
      Host = connection.Host,
      Username = connection.Username,
      Password = connection.Password,
      Group = groupName
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
      Password = Password
    };
  }
}

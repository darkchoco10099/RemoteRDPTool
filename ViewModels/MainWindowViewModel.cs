using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Threading;
using System.Net.NetworkInformation;
using RemoteRDPTool.Models;
using RemoteRDPTool.Services;

namespace RemoteRDPTool.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
  private readonly IAppConfigStore _configStore;
  private readonly IAppSettingsStore _settingsStore;
  private readonly IRdpLauncher _rdpLauncher;
  private readonly IWindowService _windowService;

  private AppConfig _config = new();
  private CancellationTokenSource? _pingTokenSource;
  private bool _isApplyingSettings;

  public MainWindowViewModel()
      : this(new DesignAppConfigStore(), new DesignAppSettingsStore(), new DesignRdpLauncher(), new DesignWindowService())
  {
    _config = CreateDesignConfig();
    RefreshGroups();
    RefreshConnections();
  }

  public MainWindowViewModel(IAppConfigStore configStore, IAppSettingsStore settingsStore, IRdpLauncher rdpLauncher, IWindowService windowService)
  {
    _configStore = configStore;
    _settingsStore = settingsStore;
    _rdpLauncher = rdpLauncher;
    _windowService = windowService;

    Groups = new ObservableCollection<string>();
    FilteredConnections = new ObservableCollection<RdpConnectionEntry>();
    GroupViews = new ObservableCollection<GroupView>();
  }

  public ObservableCollection<string> Groups { get; }

  public ObservableCollection<RdpConnectionEntry> FilteredConnections { get; }

  public ObservableCollection<GroupView> GroupViews { get; }

  public string ConfigFilePath => _configStore.ConfigPath;

  [ObservableProperty]
  private string? searchText;

  [ObservableProperty]
  private string selectedGroup = "全部";

  [ObservableProperty]
  private ConnectionView? selectedConnection;

  [ObservableProperty]
  private bool isConnectionPage = true;

  [ObservableProperty]
  private bool isSettingsPage;

  [ObservableProperty]
  private bool autoReducePingFrequency = true;

  [ObservableProperty]
  private int pingIntervalSeconds = 5;

  [ObservableProperty]
  private int reducedPingIntervalSeconds = 8;

  public async Task InitializeAsync()
  {
    try
    {
      var settings = await _settingsStore.LoadAsync();
      ApplySettings(settings);
    }
    catch (Exception ex)
    {
      await _windowService.ShowMessageAsync("加载设置失败", $"{ex.Message}\n\n{_settingsStore.ConfigPath}");
    }

    try
    {
      _config = await _configStore.LoadAsync();
      if (EnsureConnectionIds())
        await _configStore.SaveAsync(_config);
    }
    catch (Exception ex)
    {
      await _windowService.ShowMessageAsync("加载配置失败", $"{ex.Message}\n\n{_configStore.ConfigPath}");
      _config = new AppConfig();
    }
    RefreshGroups();
    RefreshConnections();
    StartPingLoop();
  }

  [RelayCommand]
  private async Task ReloadConfigAsync()
  {
    try
    {
      _pingTokenSource?.Cancel();
      var settings = await _settingsStore.LoadAsync();
      ApplySettings(settings);
      _config = await _configStore.LoadAsync();
      if (EnsureConnectionIds())
        await _configStore.SaveAsync(_config);
      RefreshGroups();
      RefreshConnections();
      StartPingLoop();
    }
    catch (Exception ex)
    {
      await _windowService.ShowMessageAsync("重新加载失败", $"{ex.Message}\n\n{_configStore.ConfigPath}");
    }
  }

  [RelayCommand]
  private async Task OpenConfigAsync()
  {
    try
    {
      var path = _configStore.ConfigPath;
      var dir = Path.GetDirectoryName(path);

      if (OperatingSystem.IsWindows())
      {
        var args = File.Exists(path)
            ? $"/select,\"{path}\""
            : $"\"{dir ?? path}\"";

        Process.Start(new ProcessStartInfo
        {
          FileName = "explorer.exe",
          Arguments = args,
          UseShellExecute = true
        });
        return;
      }

      await _windowService.ShowMessageAsync("无法打开", path);
    }
    catch (Exception ex)
    {
      await _windowService.ShowMessageAsync("打开配置失败", ex.Message);
    }
  }

  partial void OnSearchTextChanged(string? value) => RefreshConnections();

  partial void OnSelectedGroupChanged(string value) => RefreshConnections();

  partial void OnSelectedConnectionChanged(ConnectionView? value)
  {
    EditConnectionCommand.NotifyCanExecuteChanged();
    DeleteConnectionCommand.NotifyCanExecuteChanged();
    ConnectCommand.NotifyCanExecuteChanged();
  }

  [RelayCommand]
  private async Task AddGroupAsync()
  {
    try
    {
      var name = await _windowService.PromptTextAsync("新增分组", "分组名称", string.Empty);
      if (string.IsNullOrWhiteSpace(name))
        return;

      if (_config.Groups.Any(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase)))
        return;

      _config.Groups.Add(new RdpGroup { Name = name, Connections = [] });
      await _configStore.SaveAsync(_config);
      RefreshGroups();
      SelectedGroup = name;
    }
    catch (Exception ex)
    {
      await _windowService.ShowMessageAsync("新增分组失败", $"{ex.Message}\n\n{_configStore.ConfigPath}");
    }
  }

  [RelayCommand(CanExecute = nameof(CanEditOrDeleteGroup))]
  private async Task RenameGroupAsync()
  {
    try
    {
      var oldName = SelectedGroup;
      var newName = await _windowService.PromptTextAsync("重命名分组", "分组名称", oldName);
      if (string.IsNullOrWhiteSpace(newName))
        return;

      if (string.Equals(oldName, newName, StringComparison.Ordinal))
        return;

      if (_config.Groups.Any(g => string.Equals(g.Name, newName, StringComparison.OrdinalIgnoreCase)))
        return;

      var group = _config.Groups.FirstOrDefault(g => string.Equals(g.Name, oldName, StringComparison.Ordinal));
      if (group is null)
        return;

      group.Name = newName;
      await _configStore.SaveAsync(_config);
      RefreshGroups();
      SelectedGroup = newName;
    }
    catch (Exception ex)
    {
      await _windowService.ShowMessageAsync("重命名分组失败", $"{ex.Message}\n\n{_configStore.ConfigPath}");
    }
  }

  [RelayCommand(CanExecute = nameof(CanEditOrDeleteGroup))]
  private async Task DeleteGroupAsync()
  {
    try
    {
      var group = _config.Groups.FirstOrDefault(g => string.Equals(g.Name, SelectedGroup, StringComparison.Ordinal));
      if (group is null)
        return;

      if (group.Connections.Count > 0)
      {
        await _windowService.ShowMessageAsync("无法删除分组", "分组内仍有连接，请先删除或移动连接。");
        return;
      }

      var confirmed = await _windowService.ConfirmAsync("删除分组确认", $"确认删除分组“{group.Name}”？", "删除分组");
      if (!confirmed)
        return;

      _config.Groups.Remove(group);
      await _configStore.SaveAsync(_config);
      RefreshGroups();
      SelectedGroup = "全部";
    }
    catch (Exception ex)
    {
      await _windowService.ShowMessageAsync("删除分组失败", $"{ex.Message}\n\n{_configStore.ConfigPath}");
    }
  }

  private bool CanEditOrDeleteGroup() => SelectedGroup != "全部";

  [RelayCommand]
  private async Task AddConnectionAsync(string? groupName)
  {
    try
    {
      var groups = _config.Groups.Select(g => g.Name).ToArray();
      var initialGroup = !string.IsNullOrWhiteSpace(groupName)
          ? groupName
          : groups.FirstOrDefault() ?? "默认";

      if (_config.Groups.All(g => !string.Equals(g.Name, initialGroup, StringComparison.Ordinal)))
      {
        _config.Groups.Add(new RdpGroup { Name = initialGroup, Connections = [] });
      }

      var entry = new RdpConnectionEntry
      {
        Id = Guid.NewGuid(),
        Name = string.Empty,
        Host = string.Empty,
        Username = string.Empty,
        Password = string.Empty,
        Group = initialGroup
      };

      var result = await _windowService.EditConnectionAsync(entry, _config.Groups.Select(g => g.Name).ToArray());
      if (result is null)
        return;

      var targetGroup = GetOrCreateGroup(result.Group);
      targetGroup.Connections.Add(result.ToConnection());
      await _configStore.SaveAsync(_config);

      RefreshGroups();
      RefreshConnections();
      SelectedConnection = GroupViews.SelectMany(g => g.Connections).FirstOrDefault(c => c.Id == result.Id);
    }
    catch (Exception ex)
    {
      await _windowService.ShowMessageAsync("新增连接失败", $"{ex.Message}\n\n{_configStore.ConfigPath}");
    }
  }

  [RelayCommand]
  private async Task RenameGroupByNameAsync(string groupName)
  {
    try
    {
      if (groupName == "全部")
        return;

      var newName = await _windowService.PromptTextAsync("重命名分组", "分组名称", groupName);
      if (string.IsNullOrWhiteSpace(newName))
        return;

      if (string.Equals(groupName, newName, StringComparison.Ordinal))
        return;

      if (_config.Groups.Any(g => string.Equals(g.Name, newName, StringComparison.OrdinalIgnoreCase)))
        return;

      var group = _config.Groups.FirstOrDefault(g => string.Equals(g.Name, groupName, StringComparison.Ordinal));
      if (group is null)
        return;

      group.Name = newName;
      await _configStore.SaveAsync(_config);
      RefreshGroups();
      RefreshConnections();
    }
    catch (Exception ex)
    {
      await _windowService.ShowMessageAsync("重命名分组失败", $"{ex.Message}\n\n{_configStore.ConfigPath}");
    }
  }

  [RelayCommand]
  private async Task DeleteGroupByNameAsync(string groupName)
  {
    try
    {
      if (groupName == "全部")
        return;

      var group = _config.Groups.FirstOrDefault(g => string.Equals(g.Name, groupName, StringComparison.Ordinal));
      if (group is null)
        return;

      if (group.Connections.Count > 0)
      {
        await _windowService.ShowMessageAsync("无法删除分组", "分组内仍有连接，请先删除或移动连接。");
        return;
      }

      var confirmed = await _windowService.ConfirmAsync("删除分组确认", $"确认删除分组“{group.Name}”？", "删除分组");
      if (!confirmed)
        return;

      _config.Groups.Remove(group);
      await _configStore.SaveAsync(_config);
      RefreshGroups();
      RefreshConnections();
    }
    catch (Exception ex)
    {
      await _windowService.ShowMessageAsync("删除分组失败", $"{ex.Message}\n\n{_configStore.ConfigPath}");
    }
  }

  [RelayCommand(CanExecute = nameof(HasSelectedConnection))]
  private async Task EditConnectionAsync()
  {
    try
    {
      if (SelectedConnection is null)
        return;

      var original = SelectedConnection;
      var result = await _windowService.EditConnectionAsync(original.Entry with { }, _config.Groups.Select(g => g.Name).ToArray());
      if (result is null)
        return;

      RemoveConnectionById(original.Id);
      var targetGroup = GetOrCreateGroup(result.Group);
      targetGroup.Connections.Add(result.ToConnection());

      await _configStore.SaveAsync(_config);
      RefreshGroups();
      RefreshConnections();
      SelectedConnection = GroupViews.SelectMany(g => g.Connections).FirstOrDefault(c => c.Id == result.Id);
    }
    catch (Exception ex)
    {
      await _windowService.ShowMessageAsync("编辑连接失败", $"{ex.Message}\n\n{_configStore.ConfigPath}");
    }
  }

  [RelayCommand(AllowConcurrentExecutions = true)]
  private async Task EditEntryAsync(RdpConnectionEntry entry)
  {
    var cv = GroupViews.SelectMany(g => g.Connections).FirstOrDefault(c => c.Id == entry.Id);
    if (cv is null)
      return;
    SelectedConnection = cv;
    await EditConnectionAsync();
  }

  [RelayCommand(CanExecute = nameof(HasSelectedConnection))]
  private async Task DeleteConnectionAsync()
  {
    try
    {
      if (SelectedConnection is null)
        return;

      var confirmed = await _windowService.ConfirmAsync("删除连接确认", $"确认删除连接“{SelectedConnection.Name}”({SelectedConnection.Host})？", "删除连接");
      if (!confirmed)
        return;

      RemoveConnectionById(SelectedConnection.Id);
      await _configStore.SaveAsync(_config);
      RefreshGroups();
      RefreshConnections();
      SelectedConnection = null;
    }
    catch (Exception ex)
    {
      await _windowService.ShowMessageAsync("删除连接失败", $"{ex.Message}\n\n{_configStore.ConfigPath}");
    }
  }

  [RelayCommand(AllowConcurrentExecutions = true)]
  private async Task DeleteEntryAsync(RdpConnectionEntry entry)
  {
    var cv = GroupViews.SelectMany(g => g.Connections).FirstOrDefault(c => c.Id == entry.Id);
    if (cv is null)
      return;
    SelectedConnection = cv;
    await DeleteConnectionAsync();
  }

  [RelayCommand(CanExecute = nameof(HasSelectedConnection))]
  private async Task ConnectAsync()
  {
    try
    {
      if (SelectedConnection is null)
        return;

      var entry = SelectedConnection.Entry;
      var password = entry.Password;
      var username = entry.Username;

      if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
      {
        var prompt = await _windowService.PromptCredentialAsync(entry);
        if (prompt is null)
          return;

        username = prompt.Username;
        password = prompt.Password;

        if (prompt.SaveToConfig)
        {
          var updated = entry with { Username = username, Password = password };
          RemoveConnectionById(entry.Id);
          var group = GetOrCreateGroup(updated.Group);
          group.Connections.Add(updated.ToConnection());
          await _configStore.SaveAsync(_config);
          RefreshConnections();
          SelectedConnection = GroupViews.SelectMany(g => g.Connections).FirstOrDefault(c => c.Id == updated.Id);
        }
      }

      await _rdpLauncher.LaunchAsync(entry.Host, username, password);
    }
    catch (Exception ex)
    {
      await _windowService.ShowMessageAsync("连接失败", ex.Message);
    }
  }

  [RelayCommand(AllowConcurrentExecutions = true)]
  private async Task ConnectEntryAsync(RdpConnectionEntry entry)
  {
    var cv = GroupViews.SelectMany(g => g.Connections).FirstOrDefault(c => c.Id == entry.Id);
    if (cv is null)
      return;
    SelectedConnection = cv;
    await ConnectAsync();
  }

  [RelayCommand]
  private void ShowConnectionsPage()
  {
    IsConnectionPage = true;
    IsSettingsPage = false;
  }

  [RelayCommand]
  private void ShowSettingsPage()
  {
    IsConnectionPage = false;
    IsSettingsPage = true;
  }

  [RelayCommand(AllowConcurrentExecutions = true)]
  private async Task CheckEntryAsync(RdpConnectionEntry entry)
  {
    var cv = GroupViews.SelectMany(g => g.Connections).FirstOrDefault(c => c.Id == entry.Id);
    if (cv is null)
      return;

    await ProbeConnectionAsync(cv);
  }

  partial void OnAutoReducePingFrequencyChanged(bool value)
  {
    if (_isApplyingSettings)
      return;
    ResetPingSchedule();
    _ = PersistSettingsAsync();
  }

  partial void OnPingIntervalSecondsChanged(int value)
  {
    if (value < 2)
      PingIntervalSeconds = 2;
    if (ReducedPingIntervalSeconds < PingIntervalSeconds)
      ReducedPingIntervalSeconds = PingIntervalSeconds;
    if (_isApplyingSettings)
      return;
    ResetPingSchedule();
    _ = PersistSettingsAsync();
  }

  partial void OnReducedPingIntervalSecondsChanged(int value)
  {
    if (value < PingIntervalSeconds)
      ReducedPingIntervalSeconds = PingIntervalSeconds;
    if (_isApplyingSettings)
      return;
    ResetPingSchedule();
    _ = PersistSettingsAsync();
  }

  private bool HasSelectedConnection() => SelectedConnection is not null;

  private void RefreshGroups()
  {
    var current = SelectedGroup;
    Groups.Clear();
    Groups.Add("全部");

    foreach (var name in _config.Groups.Select(g => g.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
    {
      Groups.Add(name);
    }

    if (Groups.Contains(current))
      SelectedGroup = current;
    else
      SelectedGroup = "全部";
  }

  private void RefreshConnections()
  {
    var entries = _config.Groups
        .SelectMany(g => g.Connections.Select(c => RdpConnectionEntry.FromConnection(g.Name, c)))
        .ToList();

    if (!string.IsNullOrWhiteSpace(SearchText))
    {
      var q = SearchText.Trim();
      entries = entries.Where(e =>
              e.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
              e.Host.Contains(q, StringComparison.OrdinalIgnoreCase) ||
              (e.Username?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
              (e.Group?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false))
          .ToList();
    }

    entries = entries
        .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
        .ThenBy(e => e.Host, StringComparer.OrdinalIgnoreCase)
        .ToList();

    FilteredConnections.Clear();
    foreach (var e in entries)
      FilteredConnections.Add(e);

    RebuildGroupViews(entries);

    EditConnectionCommand.NotifyCanExecuteChanged();
    DeleteConnectionCommand.NotifyCanExecuteChanged();
    ConnectCommand.NotifyCanExecuteChanged();
    RenameGroupCommand.NotifyCanExecuteChanged();
    DeleteGroupCommand.NotifyCanExecuteChanged();
  }

  private void RebuildGroupViews(List<RdpConnectionEntry> entries)
  {
    var grouped = entries
        .GroupBy(e => e.Group)
        .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

    GroupViews.Clear();

    foreach (var name in _config.Groups.Select(g => g.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
    {
      grouped.TryGetValue(name, out var items);
      GroupViews.Add(new GroupView(name, items ?? [], isExpanded: (items?.Count ?? 0) > 0));
    }

  }

  private void ResortGroupForConnection(ConnectionView conn)
  {
    var group = GroupViews.FirstOrDefault(g => g.Connections.Contains(conn));
    if (group is null)
      return;

    var ordered = group.Connections
        .OrderBy(c => c.IsOnline ? 0 : 1)
        .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
        .ThenBy(c => c.Host, StringComparer.OrdinalIgnoreCase)
        .ToList();

    for (var i = 0; i < ordered.Count; i++)
    {
      var currentIndex = group.Connections.IndexOf(ordered[i]);
      if (currentIndex >= 0 && currentIndex != i)
        group.Connections.Move(currentIndex, i);
    }
  }

  private void StartPingLoop()
  {
    _pingTokenSource?.Cancel();
    var cts = new CancellationTokenSource();
    _pingTokenSource = cts;

    Task.Run(() => PingLoopAsync(cts.Token));
  }

  private async Task PingLoopAsync(CancellationToken token)
  {
    while (!token.IsCancellationRequested)
    {
      var snapshot = GroupViews
          .SelectMany(g => g.Connections)
          .ToList();

      var now = DateTime.UtcNow;
      foreach (var conn in snapshot)
      {
        if (token.IsCancellationRequested)
          break;

        if (now < conn.NextPingAtUtc || conn.IsChecking)
          continue;

        await ProbeConnectionAsync(conn);
      }

      try
      {
        await Task.Delay(TimeSpan.FromSeconds(1), token);
      }
      catch (TaskCanceledException)
      {
        break;
      }
    }
  }

  private async Task ProbeConnectionAsync(ConnectionView conn)
  {
    await Dispatcher.UIThread.InvokeAsync(() => conn.IsChecking = true);
    var host = conn.Host;
    if (string.IsNullOrWhiteSpace(host))
    {
      await Dispatcher.UIThread.InvokeAsync(() =>
      {
        conn.IsChecking = false;
        conn.IsOnline = false;
        conn.PingStatus = "未配置";
        conn.PingBorderBrush = "#E2B93B";
        conn.ConsecutiveFailureCount++;
        conn.NextPingAtUtc = DateTime.UtcNow + GetPingInterval(conn.ConsecutiveFailureCount);
        ResortGroupForConnection(conn);
      });
      return;
    }

    try
    {
      using var ping = new Ping();
      var reply = await ping.SendPingAsync(host, 1000);
      var ok = reply.Status == IPStatus.Success;
      await Dispatcher.UIThread.InvokeAsync(() =>
      {
        conn.IsChecking = false;
        conn.IsOnline = ok;
        conn.PingStatus = ok ? "在线" : "不可达";
        conn.PingBorderBrush = ok ? "#2EAD5A" : "#D9534F";
        conn.ConsecutiveFailureCount = ok ? 0 : conn.ConsecutiveFailureCount + 1;
        conn.NextPingAtUtc = DateTime.UtcNow + GetPingInterval(conn.ConsecutiveFailureCount);
        ResortGroupForConnection(conn);
      });
    }
    catch
    {
      await Dispatcher.UIThread.InvokeAsync(() =>
      {
        conn.IsChecking = false;
        conn.IsOnline = false;
        conn.PingStatus = "错误";
        conn.PingBorderBrush = "#E2B93B";
        conn.ConsecutiveFailureCount++;
        conn.NextPingAtUtc = DateTime.UtcNow + GetPingInterval(conn.ConsecutiveFailureCount);
        ResortGroupForConnection(conn);
      });
    }
  }

  private TimeSpan GetPingInterval(int consecutiveFailureCount)
  {
    var normal = TimeSpan.FromSeconds(PingIntervalSeconds);
    var reduced = TimeSpan.FromSeconds(ReducedPingIntervalSeconds);
    if (AutoReducePingFrequency && consecutiveFailureCount >= 3)
      return reduced;
    return normal;
  }

  private bool EnsureConnectionIds()
  {
    var changed = false;
    var used = new HashSet<Guid>();

    foreach (var group in _config.Groups)
    {
      group.Connections ??= [];
      foreach (var conn in group.Connections)
      {
        if (conn.Id == Guid.Empty || !used.Add(conn.Id))
        {
          conn.Id = Guid.NewGuid();
          changed = true;
          used.Add(conn.Id);
        }
      }
    }

    return changed;
  }

  private void ResetPingSchedule()
  {
    foreach (var conn in GroupViews.SelectMany(g => g.Connections))
      conn.NextPingAtUtc = DateTime.MinValue;
  }

  private void ApplySettings(AppSettings settings)
  {
    _isApplyingSettings = true;
    AutoReducePingFrequency = settings.AutoReducePingFrequency;
    PingIntervalSeconds = settings.PingIntervalSeconds;
    ReducedPingIntervalSeconds = settings.ReducedPingIntervalSeconds;
    _isApplyingSettings = false;
    ResetPingSchedule();
  }

  private async Task PersistSettingsAsync()
  {
    try
    {
      var settings = new AppSettings
      {
        AutoReducePingFrequency = AutoReducePingFrequency,
        PingIntervalSeconds = PingIntervalSeconds,
        ReducedPingIntervalSeconds = ReducedPingIntervalSeconds
      };
      await _settingsStore.SaveAsync(settings);
    }
    catch
    {
    }
  }

  private RdpGroup GetOrCreateGroup(string groupName)
  {
    var existing = _config.Groups.FirstOrDefault(g => string.Equals(g.Name, groupName, StringComparison.Ordinal));
    if (existing is not null)
      return existing;

    var group = new RdpGroup { Name = groupName, Connections = [] };
    _config.Groups.Add(group);
    return group;
  }

  private void RemoveConnectionById(Guid id)
  {
    foreach (var group in _config.Groups)
    {
      var idx = group.Connections.FindIndex(c => c.Id == id);
      if (idx >= 0)
      {
        group.Connections.RemoveAt(idx);
        break;
      }
    }
  }

  private static AppConfig CreateDesignConfig()
  {
    return new AppConfig
    {
      Groups =
      [
        new RdpGroup
        {
          Name = "默认",
          Connections =
          [
            new RdpConnection
            {
              Id = Guid.NewGuid(),
              Name = "测试服务器",
              Host = "10.0.0.8",
              Username = "Administrator",
              Password = string.Empty
            }
          ]
        },
        new RdpGroup { Name = "开发", Connections = [] }
      ]
    };
  }

  private sealed class DesignAppConfigStore : IAppConfigStore
  {
    public string ConfigPath => "rdp-connections.json";

    public Task<AppConfig> LoadAsync() => Task.FromResult(CreateDesignConfig());

    public Task SaveAsync(AppConfig config) => Task.CompletedTask;
  }

  private sealed class DesignAppSettingsStore : IAppSettingsStore
  {
    public string ConfigPath => "rdp-settings.json";

    public Task<AppSettings> LoadAsync() => Task.FromResult(new AppSettings());

    public Task SaveAsync(AppSettings settings) => Task.CompletedTask;
  }

  private sealed class DesignRdpLauncher : IRdpLauncher
  {
    public Task LaunchAsync(string host, string? username, string? password) => Task.CompletedTask;
  }

  private sealed class DesignWindowService : IWindowService
  {
    public Task<string?> PromptTextAsync(string title, string label, string initialText) => Task.FromResult<string?>(null);

    public Task<RdpConnectionEntry?> EditConnectionAsync(RdpConnectionEntry entry, IReadOnlyList<string> groups) => Task.FromResult<RdpConnectionEntry?>(null);

    public Task<CredentialPromptResult?> PromptCredentialAsync(RdpConnectionEntry entry) => Task.FromResult<CredentialPromptResult?>(null);

    public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;

    public Task<bool> ConfirmAsync(string title, string message, string confirmText) => Task.FromResult(true);
  }

  public sealed partial class ConnectionView : ObservableObject
  {
    public ConnectionView(RdpConnectionEntry entry)
    {
      Entry = entry;
      Id = entry.Id;
      Name = entry.Name;
      Host = entry.Host;
      Username = entry.Username;
      Group = entry.Group;
      PingStatus = "检测中";
    }

    public RdpConnectionEntry Entry { get; }

    public Guid Id { get; }

    public string Name { get; }

    public string Host { get; }

    public string Username { get; }

    public string Group { get; }

    [ObservableProperty]
    private string pingStatus;

    [ObservableProperty]
    private string pingBorderBrush = "#E2B93B";

    [ObservableProperty]
    private bool isChecking;

    [ObservableProperty]
    private bool isOnline;

    [ObservableProperty]
    private int consecutiveFailureCount;

    [ObservableProperty]
    private DateTime nextPingAtUtc = DateTime.MinValue;

    [ObservableProperty]
    private bool canManualCheck;

    [ObservableProperty]
    private bool showQuickCheck;

    partial void OnPingStatusChanged(string value)
    {
      PingBorderBrush = value switch
      {
        "在线" => "#2EAD5A",
        "不可达" => "#D9534F",
        _ => "#E2B93B"
      };
      UpdateCanManualCheck();
    }

    partial void OnIsCheckingChanged(bool value) => UpdateCanManualCheck();

    partial void OnIsOnlineChanged(bool value) => UpdateCanManualCheck();

    private void UpdateCanManualCheck()
    {
      CanManualCheck = !IsChecking && !IsOnline && !string.IsNullOrWhiteSpace(Host);
      ShowQuickCheck = CanManualCheck && PingStatus == "不可达";
    }
  }

  public sealed partial class GroupView : ObservableObject
  {
    public GroupView(string name, IReadOnlyList<RdpConnectionEntry> connections, bool isExpanded)
    {
      Name = name;
      Connections = new ObservableCollection<ConnectionView>(connections.Select(c => new ConnectionView(c)));
      Count = Connections.Count;
      IsExpanded = isExpanded && Count > 0;
    }

    public string Name { get; }

    public ObservableCollection<ConnectionView> Connections { get; }

    public int Count { get; }

    public bool HasConnections => Count > 0;

    public bool CanManage => true;

    [ObservableProperty]
    private bool isExpanded;
  }
}

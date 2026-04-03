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
  private readonly IShareDiskLauncher _shareDiskLauncher;
  private readonly IProcessProbeService _processProbeService;
  private readonly IWindowService _windowService;

  private AppConfig _config = new();
  private CancellationTokenSource? _pingTokenSource;
  private CancellationTokenSource? _globalLoadingTokenSource;
  private long _globalLoadingOperationId;
  private bool _globalLoadingCanceledByUser;
  private bool _isApplyingSettings;
  private const int GlobalLoadingTimeoutSeconds = 12;

  public MainWindowViewModel()
      : this(new DesignAppConfigStore(), new DesignAppSettingsStore(), new DesignRdpLauncher(), new DesignShareDiskLauncher(), new DesignProcessProbeService(), new DesignWindowService())
  {
    _config = CreateDesignConfig();
    RefreshGroups();
    RefreshConnections();
  }

  public MainWindowViewModel(IAppConfigStore configStore, IAppSettingsStore settingsStore, IRdpLauncher rdpLauncher, IShareDiskLauncher shareDiskLauncher, IProcessProbeService processProbeService, IWindowService windowService)
  {
    _configStore = configStore;
    _settingsStore = settingsStore;
    _rdpLauncher = rdpLauncher;
    _shareDiskLauncher = shareDiskLauncher;
    _processProbeService = processProbeService;
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
  private bool isGlobalLoading;

  [ObservableProperty]
  private string globalLoadingText = string.Empty;

  [ObservableProperty]
  private bool autoReducePingFrequency = true;

  [ObservableProperty]
  private int pingIntervalSeconds = 5;

  [ObservableProperty]
  private int reducedPingIntervalSeconds = 8;

  [ObservableProperty]
  private string summonHotkey = "Ctrl+R";

  [ObservableProperty]
  private bool processWatchEnabled;

  [ObservableProperty]
  private int processWatchIntervalSeconds = 20;

  [ObservableProperty]
  private int processWatchTimeoutSeconds = 10;

  [ObservableProperty]
  private string processWatchNamesText = string.Empty;

  [ObservableProperty]
  private int cardIconStyleIndex;

  public bool IsLinearCardIconStyle => CardIconStyleIndex == 1;

  public bool IsColorCardIconStyle => CardIconStyleIndex != 1;

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
    OpenShareDiskCommand.NotifyCanExecuteChanged();
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

      await RunWithGlobalLoadingAsync(
          "正在远程连接...",
          "远程连接超时，请重试。",
          token => _rdpLauncher.LaunchAsync(entry.Host, username, password, token));
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

  [RelayCommand(CanExecute = nameof(HasSelectedConnection))]
  private async Task OpenShareDiskAsync()
  {
    try
    {
      if (SelectedConnection is null)
        return;

      var entry = SelectedConnection.Entry;
      if (string.IsNullOrWhiteSpace(entry.Host))
      {
        await _windowService.ShowMessageAsync("打开共享盘失败", "未配置主机地址。");
        return;
      }

      if (string.IsNullOrWhiteSpace(entry.ShareDisk))
      {
        await _windowService.ShowMessageAsync("打开共享盘失败", "未配置共享盘符。");
        return;
      }

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

      await RunWithGlobalLoadingAsync(
          "正在打开共享盘...",
          "打开共享盘超时，请重试。",
          token => _shareDiskLauncher.OpenAsync(entry.Host, entry.ShareDisk, username, password, token));
    }
    catch (Exception ex)
    {
      await _windowService.ShowMessageAsync("打开共享盘失败", ex.Message);
    }
  }

  [RelayCommand(AllowConcurrentExecutions = true)]
  private async Task OpenShareDiskEntryAsync(RdpConnectionEntry entry)
  {
    var cv = GroupViews.SelectMany(g => g.Connections).FirstOrDefault(c => c.Id == entry.Id);
    if (cv is null)
      return;
    SelectedConnection = cv;
    await OpenShareDiskAsync();
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

  partial void OnSummonHotkeyChanged(string value)
  {
    if (_isApplyingSettings)
      return;

    var normalized = string.IsNullOrWhiteSpace(value) ? "Ctrl+R" : value.Trim();
    if (!string.Equals(value, normalized, StringComparison.Ordinal))
    {
      SummonHotkey = normalized;
      return;
    }

    _ = PersistSettingsAsync();
  }

  partial void OnProcessWatchEnabledChanged(bool value)
  {
    if (_isApplyingSettings)
      return;
    ResetProcessWatchSchedule();
    UpdateProcessWatchStates();
    _ = PersistSettingsAsync();
  }

  partial void OnProcessWatchIntervalSecondsChanged(int value)
  {
    if (value < 5)
      ProcessWatchIntervalSeconds = 5;
    if (_isApplyingSettings)
      return;
    ResetProcessWatchSchedule();
    _ = PersistSettingsAsync();
  }

  partial void OnProcessWatchTimeoutSecondsChanged(int value)
  {
    var normalized = Math.Clamp(value, 3, 30);
    if (value != normalized)
    {
      ProcessWatchTimeoutSeconds = normalized;
      return;
    }

    if (_isApplyingSettings)
      return;
    _ = PersistSettingsAsync();
  }

  partial void OnProcessWatchNamesTextChanged(string value)
  {
    if (_isApplyingSettings)
      return;

    var normalized = string.Join(", ", ParseProcessWatchNames(value));
    if (!string.Equals(value, normalized, StringComparison.Ordinal))
    {
      ProcessWatchNamesText = normalized;
      return;
    }

    ResetProcessWatchSchedule();
    UpdateProcessWatchStates();
    _ = PersistSettingsAsync();
  }

  partial void OnCardIconStyleIndexChanged(int value)
  {
    if (value is < 0 or > 1)
    {
      CardIconStyleIndex = value < 0 ? 0 : 1;
      return;
    }

    OnPropertyChanged(nameof(IsLinearCardIconStyle));
    OnPropertyChanged(nameof(IsColorCardIconStyle));
    if (_isApplyingSettings)
      return;
    _ = PersistSettingsAsync();
  }

  private bool HasSelectedConnection() => SelectedConnection is not null;

  [RelayCommand(CanExecute = nameof(CanCancelGlobalLoading))]
  private void CancelGlobalLoading()
  {
    _globalLoadingCanceledByUser = true;
    _globalLoadingTokenSource?.Cancel();
  }

  private bool CanCancelGlobalLoading() => IsGlobalLoading;

  partial void OnIsGlobalLoadingChanged(bool value)
  {
    CancelGlobalLoadingCommand.NotifyCanExecuteChanged();
  }

  private async Task RunWithGlobalLoadingAsync(string loadingText, string timeoutMessage, Func<CancellationToken, Task> action)
  {
    if (IsGlobalLoading)
      return;

    var operationId = Interlocked.Increment(ref _globalLoadingOperationId);
    _globalLoadingCanceledByUser = false;
    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(GlobalLoadingTimeoutSeconds));
    _globalLoadingTokenSource = timeoutCts;
    IsGlobalLoading = true;
    GlobalLoadingText = loadingText;
    try
    {
      await action(timeoutCts.Token);
    }
    catch (OperationCanceledException) when (operationId == _globalLoadingOperationId)
    {
      if (!_globalLoadingCanceledByUser)
      {
        await _windowService.ShowMessageAsync("连接失败", timeoutMessage);
      }
    }
    finally
    {
      if (operationId == _globalLoadingOperationId)
      {
        IsGlobalLoading = false;
        GlobalLoadingText = string.Empty;
        _globalLoadingTokenSource = null;
      }
    }
  }

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
              (e.ShareDisk?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
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
    OpenShareDiskCommand.NotifyCanExecuteChanged();
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

    UpdateProcessWatchStates();
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

        await ProbeConnectionAsync(conn, token);
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

  private async Task ProbeConnectionAsync(ConnectionView conn, CancellationToken cancellationToken = default)
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
        conn.ProcessStatus = "主机未配置";
        conn.ProcessStatusBrush = "#E2B93B";
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
        if (!ok)
        {
          conn.ProcessStatus = "主机离线";
          conn.ProcessStatusBrush = "#D9534F";
        }
        ResortGroupForConnection(conn);
      });
      if (ok)
        await ProbeConnectionProcessesAsync(conn, cancellationToken);
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
        conn.ProcessStatus = "检测失败";
        conn.ProcessStatusBrush = "#E2B93B";
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

  private async Task ProbeConnectionProcessesAsync(ConnectionView conn, CancellationToken cancellationToken)
  {
    if (!ProcessWatchEnabled || !conn.EnableProcessWatch)
    {
      await Dispatcher.UIThread.InvokeAsync(() =>
      {
        conn.IsProcessChecking = false;
        conn.ProcessStatus = "未启用";
        conn.ProcessStatusBrush = "#8F9BB0";
      });
      return;
    }

    var processNames = ParseProcessWatchNames(ProcessWatchNamesText);
    if (processNames.Count == 0)
    {
      await Dispatcher.UIThread.InvokeAsync(() =>
      {
        conn.IsProcessChecking = false;
        conn.ProcessStatus = "未配置进程";
        conn.ProcessStatusBrush = "#E2B93B";
      });
      return;
    }

    var now = DateTime.UtcNow;
    if (now < conn.NextProcessCheckAtUtc || conn.IsProcessChecking)
      return;

    if (string.IsNullOrWhiteSpace(conn.Username) || string.IsNullOrWhiteSpace(conn.Entry.Password))
    {
      await Dispatcher.UIThread.InvokeAsync(() =>
      {
        conn.IsProcessChecking = false;
        conn.ProcessStatus = "缺少凭据";
        conn.ProcessStatusBrush = "#E2B93B";
        conn.NextProcessCheckAtUtc = DateTime.UtcNow + GetProcessWatchInterval();
      });
      return;
    }

    await Dispatcher.UIThread.InvokeAsync(() => conn.IsProcessChecking = true);
    try
    {
      var probeTask = _processProbeService.ProbeAsync(conn.Host, conn.Username, conn.Entry.Password!, processNames, cancellationToken);
      var probe = await probeTask.WaitAsync(TimeSpan.FromSeconds(ProcessWatchTimeoutSeconds), cancellationToken);
      await Dispatcher.UIThread.InvokeAsync(() =>
      {
        conn.IsProcessChecking = false;
        conn.NextProcessCheckAtUtc = DateTime.UtcNow + GetProcessWatchInterval();
        if (!probe.IsSuccess)
        {
          conn.ProcessStatus = "检测失败";
          conn.ProcessStatusBrush = "#E2B93B";
          return;
        }

        if (probe.MissingProcesses.Count == 0)
        {
          conn.ProcessStatus = "进程正常";
          conn.ProcessStatusBrush = "#2EAD5A";
          return;
        }

        if (probe.MissingProcesses.Count == 1)
        {
          conn.ProcessStatus = $"缺失: {probe.MissingProcesses[0]}";
        }
        else
        {
          var preview = string.Join(", ", probe.MissingProcesses.Take(2));
          conn.ProcessStatus = $"缺失 {probe.MissingProcesses.Count} 项 ({preview})";
        }
        conn.ProcessStatusBrush = "#D9534F";
      });
    }
    catch (TimeoutException)
    {
      await Dispatcher.UIThread.InvokeAsync(() =>
      {
        conn.IsProcessChecking = false;
        conn.ProcessStatus = "检测超时";
        conn.ProcessStatusBrush = "#E2B93B";
        conn.NextProcessCheckAtUtc = DateTime.UtcNow + GetProcessWatchInterval();
      });
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      await Dispatcher.UIThread.InvokeAsync(() =>
      {
        conn.IsProcessChecking = false;
        conn.NextProcessCheckAtUtc = DateTime.UtcNow + GetProcessWatchInterval();
      });
    }
    catch
    {
      await Dispatcher.UIThread.InvokeAsync(() =>
      {
        conn.IsProcessChecking = false;
        conn.ProcessStatus = "检测失败";
        conn.ProcessStatusBrush = "#E2B93B";
        conn.NextProcessCheckAtUtc = DateTime.UtcNow + GetProcessWatchInterval();
      });
    }
  }

  private TimeSpan GetProcessWatchInterval()
  {
    return TimeSpan.FromSeconds(Math.Max(5, ProcessWatchIntervalSeconds));
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

  private void ResetProcessWatchSchedule()
  {
    foreach (var conn in GroupViews.SelectMany(g => g.Connections))
      conn.NextProcessCheckAtUtc = DateTime.MinValue;
  }

  private void UpdateProcessWatchStates()
  {
    var processNames = ParseProcessWatchNames(ProcessWatchNamesText);
    foreach (var conn in GroupViews.SelectMany(g => g.Connections))
    {
      if (!ProcessWatchEnabled || !conn.EnableProcessWatch)
      {
        conn.ProcessStatus = "未启用";
        conn.ProcessStatusBrush = "#8F9BB0";
        conn.IsProcessChecking = false;
      }
      else if (processNames.Count == 0)
      {
        conn.ProcessStatus = "未配置进程";
        conn.ProcessStatusBrush = "#E2B93B";
      }
      else if (!conn.IsOnline)
      {
        conn.ProcessStatus = "等待主机在线";
        conn.ProcessStatusBrush = "#8F9BB0";
      }
    }
  }

  private static List<string> ParseProcessWatchNames(string? source)
  {
    if (string.IsNullOrWhiteSpace(source))
      return [];

    return source
        .Split([',', ';', '\r', '\n', '，', '；'], StringSplitOptions.RemoveEmptyEntries)
        .Select(x => x.Trim())
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
  }

  private void ApplySettings(AppSettings settings)
  {
    _isApplyingSettings = true;
    AutoReducePingFrequency = settings.AutoReducePingFrequency;
    PingIntervalSeconds = settings.PingIntervalSeconds;
    ReducedPingIntervalSeconds = settings.ReducedPingIntervalSeconds;
    SummonHotkey = string.IsNullOrWhiteSpace(settings.SummonHotkey) ? "Ctrl+R" : settings.SummonHotkey.Trim();
    ProcessWatchEnabled = settings.ProcessWatchEnabled;
    ProcessWatchIntervalSeconds = Math.Max(5, settings.ProcessWatchIntervalSeconds);
    ProcessWatchTimeoutSeconds = Math.Clamp(settings.ProcessWatchTimeoutSeconds, 3, 30);
    ProcessWatchNamesText = string.Join(", ", settings.ProcessWatchNames ?? []);
    CardIconStyleIndex = string.Equals(settings.CardIconStyle, "linear", StringComparison.OrdinalIgnoreCase) || settings.UseLinearCardIcons ? 1 : 0;
    _isApplyingSettings = false;
    ResetPingSchedule();
    ResetProcessWatchSchedule();
    UpdateProcessWatchStates();
  }

  private async Task PersistSettingsAsync()
  {
    try
    {
      var settings = new AppSettings
      {
        AutoReducePingFrequency = AutoReducePingFrequency,
        PingIntervalSeconds = PingIntervalSeconds,
        ReducedPingIntervalSeconds = ReducedPingIntervalSeconds,
        SummonHotkey = string.IsNullOrWhiteSpace(SummonHotkey) ? "Ctrl+R" : SummonHotkey.Trim(),
        ProcessWatchEnabled = ProcessWatchEnabled,
        ProcessWatchIntervalSeconds = Math.Max(5, ProcessWatchIntervalSeconds),
        ProcessWatchTimeoutSeconds = Math.Clamp(ProcessWatchTimeoutSeconds, 3, 30),
        ProcessWatchNames = ParseProcessWatchNames(ProcessWatchNamesText),
        UseLinearCardIcons = CardIconStyleIndex == 1,
        CardIconStyle = CardIconStyleIndex == 1 ? "linear" : "color"
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
              Password = string.Empty,
              ShareDisk = "c$",
              EnableProcessWatch = true
            }
          ]
        },
        new RdpGroup { Name = "开发", Connections = [] }
      ]
    };
  }

  private sealed class DesignAppConfigStore : IAppConfigStore
  {
    public string ConfigPath => "rdp-config.json";

    public Task<AppConfig> LoadAsync() => Task.FromResult(CreateDesignConfig());

    public Task SaveAsync(AppConfig config) => Task.CompletedTask;
  }

  private sealed class DesignAppSettingsStore : IAppSettingsStore
  {
    public string ConfigPath => "rdp-config.json";

    public Task<AppSettings> LoadAsync() => Task.FromResult(new AppSettings());

    public Task SaveAsync(AppSettings settings) => Task.CompletedTask;
  }

  private sealed class DesignRdpLauncher : IRdpLauncher
  {
    public Task LaunchAsync(string host, string? username, string? password, CancellationToken cancellationToken = default) => Task.CompletedTask;
  }

  private sealed class DesignShareDiskLauncher : IShareDiskLauncher
  {
    public Task OpenAsync(string host, string shareDisk, string? username, string? password, CancellationToken cancellationToken = default) => Task.CompletedTask;
  }

  private sealed class DesignProcessProbeService : IProcessProbeService
  {
    public Task<ProcessProbeResult> ProbeAsync(string host, string username, string password, IReadOnlyList<string> expectedProcessNames, CancellationToken cancellationToken = default)
        => Task.FromResult(new ProcessProbeResult(true, [], string.Empty));
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
      ShareDisk = entry.ShareDisk;
      SharePathDisplay = string.IsNullOrWhiteSpace(entry.ShareDisk) ? string.Empty : $@"\\{entry.Host}\{entry.ShareDisk.Trim().TrimStart('\\', '/')}";
      Group = entry.Group;
      EnableProcessWatch = entry.EnableProcessWatch;
      PingStatus = "检测中";
      ProcessStatus = "未启用";
    }

    public RdpConnectionEntry Entry { get; }

    public Guid Id { get; }

    public string Name { get; }

    public string Host { get; }

    public string Username { get; }

    public string ShareDisk { get; }

    public string SharePathDisplay { get; }

    public string Group { get; }

    public bool EnableProcessWatch { get; }

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
    private bool isProcessChecking;

    [ObservableProperty]
    private DateTime nextProcessCheckAtUtc = DateTime.MinValue;

    [ObservableProperty]
    private string processStatus;

    [ObservableProperty]
    private string processStatusBrush = "#8F9BB0";

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

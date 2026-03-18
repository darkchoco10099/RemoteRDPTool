using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemoteRDPTool.Models;
using RemoteRDPTool.Services;

namespace RemoteRDPTool.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
  private readonly IAppConfigStore _configStore;
  private readonly IRdpLauncher _rdpLauncher;
  private readonly IWindowService _windowService;

  private AppConfig _config = new();

  public MainWindowViewModel(IAppConfigStore configStore, IRdpLauncher rdpLauncher, IWindowService windowService)
  {
    _configStore = configStore;
    _rdpLauncher = rdpLauncher;
    _windowService = windowService;

    Groups = new ObservableCollection<string>();
    FilteredConnections = new ObservableCollection<RdpConnectionEntry>();
  }

  public ObservableCollection<string> Groups { get; }

  public ObservableCollection<RdpConnectionEntry> FilteredConnections { get; }

  [ObservableProperty]
  private string? searchText;

  [ObservableProperty]
  private string selectedGroup = "全部";

  [ObservableProperty]
  private RdpConnectionEntry? selectedConnection;

  public async Task InitializeAsync()
  {
    _config = await _configStore.LoadAsync();
    RefreshGroups();
    RefreshConnections();
  }

  partial void OnSearchTextChanged(string? value) => RefreshConnections();

  partial void OnSelectedGroupChanged(string value) => RefreshConnections();

  partial void OnSelectedConnectionChanged(RdpConnectionEntry? value)
  {
    EditConnectionCommand.NotifyCanExecuteChanged();
    DeleteConnectionCommand.NotifyCanExecuteChanged();
    ConnectCommand.NotifyCanExecuteChanged();
  }

  [RelayCommand]
  private async Task AddGroupAsync()
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

  [RelayCommand(CanExecute = nameof(CanEditOrDeleteGroup))]
  private async Task RenameGroupAsync()
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

  [RelayCommand(CanExecute = nameof(CanEditOrDeleteGroup))]
  private async Task DeleteGroupAsync()
  {
    var group = _config.Groups.FirstOrDefault(g => string.Equals(g.Name, SelectedGroup, StringComparison.Ordinal));
    if (group is null)
      return;

    if (group.Connections.Count > 0)
      return;

    _config.Groups.Remove(group);
    await _configStore.SaveAsync(_config);
    RefreshGroups();
    SelectedGroup = "全部";
  }

  private bool CanEditOrDeleteGroup() => SelectedGroup is not ("全部" or "未分组");

  [RelayCommand]
  private async Task AddConnectionAsync()
  {
    var groups = _config.Groups.Select(g => g.Name).ToArray();
    var initialGroup = SelectedGroup is "全部" or "未分组" ? groups.FirstOrDefault() ?? "默认" : SelectedGroup;

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
    SelectedGroup = result.Group;
    RefreshConnections();
    SelectedConnection = FilteredConnections.FirstOrDefault(c => c.Id == result.Id);
  }

  [RelayCommand(CanExecute = nameof(HasSelectedConnection))]
  private async Task EditConnectionAsync()
  {
    if (SelectedConnection is null)
      return;

    var original = SelectedConnection;
    var result = await _windowService.EditConnectionAsync(original with { }, _config.Groups.Select(g => g.Name).ToArray());
    if (result is null)
      return;

    RemoveConnectionById(original.Id);
    var targetGroup = GetOrCreateGroup(result.Group);
    targetGroup.Connections.Add(result.ToConnection());

    await _configStore.SaveAsync(_config);
    RefreshGroups();
    SelectedGroup = result.Group;
    RefreshConnections();
    SelectedConnection = FilteredConnections.FirstOrDefault(c => c.Id == result.Id);
  }

  [RelayCommand(CanExecute = nameof(HasSelectedConnection))]
  private async Task DeleteConnectionAsync()
  {
    if (SelectedConnection is null)
      return;

    RemoveConnectionById(SelectedConnection.Id);
    await _configStore.SaveAsync(_config);
    RefreshGroups();
    RefreshConnections();
    SelectedConnection = null;
  }

  [RelayCommand(CanExecute = nameof(HasSelectedConnection))]
  private async Task ConnectAsync()
  {
    if (SelectedConnection is null)
      return;

    var entry = SelectedConnection;
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
        SelectedConnection = FilteredConnections.FirstOrDefault(c => c.Id == updated.Id);
      }
    }

    await _rdpLauncher.LaunchAsync(entry.Host, username, password);
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

    Groups.Add("未分组");
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

    if (SelectedGroup == "未分组")
    {
      entries = entries.Where(e => string.IsNullOrWhiteSpace(e.Group)).ToList();
    }
    else if (SelectedGroup != "全部")
    {
      entries = entries.Where(e => string.Equals(e.Group, SelectedGroup, StringComparison.Ordinal)).ToList();
    }

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

    entries = entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();

    FilteredConnections.Clear();
    foreach (var e in entries)
      FilteredConnections.Add(e);

    EditConnectionCommand.NotifyCanExecuteChanged();
    DeleteConnectionCommand.NotifyCanExecuteChanged();
    ConnectCommand.NotifyCanExecuteChanged();
    RenameGroupCommand.NotifyCanExecuteChanged();
    DeleteGroupCommand.NotifyCanExecuteChanged();
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
}

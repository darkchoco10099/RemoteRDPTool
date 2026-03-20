using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using RemoteRDPTool.Models;

namespace RemoteRDPTool.Views;

public partial class ConnectionEditWindow : Window
{
  private readonly RdpConnectionEntry _original;

  public ConnectionEditWindow()
  {
    InitializeComponent();
    _original = new RdpConnectionEntry
    {
      Id = Guid.Empty,
      Name = string.Empty,
      Host = string.Empty,
      Username = string.Empty,
      Password = string.Empty,
      Group = "默认"
    };

    var groupItems = new List<string> { "默认" };
    GroupBox.ItemsSource = groupItems;
    GroupBox.SelectedItem = groupItems[0];
  }

  public ConnectionEditWindow(RdpConnectionEntry entry, IReadOnlyList<string> groups)
  {
    InitializeComponent();
    _original = entry;

    NameBox.Text = entry.Name;
    HostBox.Text = entry.Host;
    UserBox.Text = entry.Username;
    PasswordBox.Text = entry.Password ?? string.Empty;

    var groupItems = groups.Distinct().Where(g => !string.IsNullOrWhiteSpace(g)).OrderBy(g => g).ToList();
    if (groupItems.Count == 0)
      groupItems.Add("默认");

    GroupBox.ItemsSource = groupItems;
    GroupBox.SelectedItem = groupItems.Contains(entry.Group) ? entry.Group : groupItems[0];
  }

  private void Save_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
  {
    var group = GroupBox.Text?.Trim();
    if (string.IsNullOrWhiteSpace(group))
      group = (GroupBox.SelectedItem as string) ?? "默认";

    var result = _original with
    {
      Name = NameBox.Text?.Trim() ?? string.Empty,
      Host = HostBox.Text?.Trim() ?? string.Empty,
      Username = UserBox.Text?.Trim() ?? string.Empty,
      Password = PasswordBox.Text,
      Group = group.Trim()
    };

    Close(result);
  }

  private void Cancel_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
  {
    Close(null);
  }

  private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
  {
    BeginMoveDrag(e);
  }
}

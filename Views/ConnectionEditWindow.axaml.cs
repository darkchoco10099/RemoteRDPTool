using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using RemoteRDPTool.Models;

namespace RemoteRDPTool.Views;

public partial class ConnectionEditWindow : Window
{
  private readonly RdpConnectionEntry _original;

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
}

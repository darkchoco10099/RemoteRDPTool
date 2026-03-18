using Avalonia.Controls;
using RemoteRDPTool.Models;
using RemoteRDPTool.Services;

namespace RemoteRDPTool.Views;

public partial class CredentialPromptWindow : Window
{
  public CredentialPromptWindow()
  {
    InitializeComponent();
    TargetBlock.Text = "示例连接 (10.0.0.8)";
    UserBox.Text = string.Empty;
    PasswordBox.Text = string.Empty;
    SaveCheckBox.IsChecked = false;
  }

  public CredentialPromptWindow(RdpConnectionEntry entry)
  {
    InitializeComponent();
    TargetBlock.Text = $"{entry.Name} ({entry.Host})";
    UserBox.Text = entry.Username ?? string.Empty;
    PasswordBox.Text = string.Empty;
    SaveCheckBox.IsChecked = false;
  }

  private void Connect_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
  {
    var user = UserBox.Text?.Trim() ?? string.Empty;
    var pass = PasswordBox.Text ?? string.Empty;

    if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
    {
      Close(null);
      return;
    }

    Close(new CredentialPromptResult(user, pass, SaveCheckBox.IsChecked == true));
  }

  private void Cancel_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
  {
    Close(null);
  }
}

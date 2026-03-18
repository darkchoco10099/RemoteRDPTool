using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using RemoteRDPTool.Models;
using RemoteRDPTool.Views;

namespace RemoteRDPTool.Services;

public sealed record CredentialPromptResult(string Username, string Password, bool SaveToConfig);

public interface IWindowService
{
  Task<string?> PromptTextAsync(string title, string label, string initialText);
  Task<RdpConnectionEntry?> EditConnectionAsync(RdpConnectionEntry entry, IReadOnlyList<string> groups);
  Task<CredentialPromptResult?> PromptCredentialAsync(RdpConnectionEntry entry);
}

public sealed class WindowService : IWindowService
{
  private static Window? GetMainWindow()
  {
    if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
      return desktop.MainWindow;

    return null;
  }

  public async Task<string?> PromptTextAsync(string title, string label, string initialText)
  {
    var owner = GetMainWindow();
    if (owner is null)
      return null;

    var window = new TextPromptWindow(title, label, initialText);
    return await window.ShowDialog<string?>(owner);
  }

  public async Task<RdpConnectionEntry?> EditConnectionAsync(RdpConnectionEntry entry, IReadOnlyList<string> groups)
  {
    var owner = GetMainWindow();
    if (owner is null)
      return null;

    var window = new ConnectionEditWindow(entry, groups);
    return await window.ShowDialog<RdpConnectionEntry?>(owner);
  }

  public async Task<CredentialPromptResult?> PromptCredentialAsync(RdpConnectionEntry entry)
  {
    var owner = GetMainWindow();
    if (owner is null)
      return null;

    var window = new CredentialPromptWindow(entry);
    return await window.ShowDialog<CredentialPromptResult?>(owner);
  }
}

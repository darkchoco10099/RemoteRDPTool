using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using System;
using System.IO;
using System.Text.Json;
using Avalonia.Threading;
using RemoteRDPTool.ViewModels;
using RemoteRDPTool.Views;
using RemoteRDPTool.Services;

namespace RemoteRDPTool;

public partial class App : Application
{
  public override void Initialize()
  {
    AvaloniaXamlLoader.Load(this);
  }

  public override void OnFrameworkInitializationCompleted()
  {
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
    {
      // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
      // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
      DisableAvaloniaDataAnnotationValidation();

      var appDir = AppContext.BaseDirectory;
      var configPath = Path.Combine(appDir, "rdp-connections.json");

      var appDataConfigDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RemoteRDPTool");
      var appDataConfigPath = Path.Combine(appDataConfigDir, "rdp-connections.json");

      var legacyConfigPath = Path.Combine(Environment.CurrentDirectory, "rdp-connections.json");
      if (!File.Exists(configPath) || (CountConnections(configPath) == 0 && CountConnections(appDataConfigPath) > 0))
      {
        if (File.Exists(appDataConfigPath))
        {
          Directory.CreateDirectory(appDir);
          File.Copy(appDataConfigPath, configPath, overwrite: true);
        }
        else if (File.Exists(legacyConfigPath))
        {
          Directory.CreateDirectory(appDir);
          File.Copy(legacyConfigPath, configPath, overwrite: true);
        }
      }

      var configStore = new FileAppConfigStore(configPath);
      var rdpLauncher = new WindowsRdpLauncher();
      var windowService = new WindowService();
      var vm = new MainWindowViewModel(configStore, rdpLauncher, windowService);

      desktop.MainWindow = new MainWindow
      {
        DataContext = vm,
      };

      Dispatcher.UIThread.Post(async () => await vm.InitializeAsync());
    }

    base.OnFrameworkInitializationCompleted();
  }

  private static int CountConnections(string path)
  {
    if (!File.Exists(path))
      return 0;

    try
    {
      var json = File.ReadAllText(path);
      using var doc = JsonDocument.Parse(json);

      if (!doc.RootElement.TryGetProperty("groups", out var groups) || groups.ValueKind != JsonValueKind.Array)
        return 0;

      var total = 0;
      foreach (var group in groups.EnumerateArray())
      {
        if (!group.TryGetProperty("connections", out var conns) || conns.ValueKind != JsonValueKind.Array)
          continue;

        total += conns.GetArrayLength();
      }

      return total;
    }
    catch
    {
      return 0;
    }
  }

  private void DisableAvaloniaDataAnnotationValidation()
  {
    // Get an array of plugins to remove
    var dataValidationPluginsToRemove =
        BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

    // remove each entry found
    foreach (var plugin in dataValidationPluginsToRemove)
    {
      BindingPlugins.DataValidators.Remove(plugin);
    }
  }
}

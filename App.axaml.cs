using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using System;
using System.IO;
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

            var configPath = Path.Combine(Environment.CurrentDirectory, "rdp-connections.json");
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

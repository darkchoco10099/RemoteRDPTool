using Avalonia.Controls;
using Avalonia.Interactivity;
using RemoteRDPTool.ViewModels;

namespace RemoteRDPTool.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void ConnectionsGrid_DoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            await vm.ConnectCommand.ExecuteAsync(null);
    }
}

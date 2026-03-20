using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using RemoteRDPTool.ViewModels;

namespace RemoteRDPTool.Views;

public partial class MainWindow : Window
{
  public MainWindow()
  {
    InitializeComponent();
  }

  private async void ConnectionsGrid_DoubleTapped(object? sender, TappedEventArgs e)
  {
    if (DataContext is MainWindowViewModel vm)
      await vm.ConnectCommand.ExecuteAsync(null);
  }

  private void ConnectionsArea_PointerPressed(object? sender, PointerPressedEventArgs e)
  {
    if (e.Source is not Control source)
      return;

    if (source.FindAncestorOfType<ListBoxItem>() is not null)
      return;

    if (DataContext is MainWindowViewModel vm)
      vm.SelectedConnection = null;
  }
}

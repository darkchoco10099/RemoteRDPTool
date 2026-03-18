using Avalonia.Controls;

namespace RemoteRDPTool.Views;

public partial class TextPromptWindow : Window
{
    public TextPromptWindow(string title, string label, string initialText)
    {
        InitializeComponent();
        Title = title;
        LabelBlock.Text = label;
        InputBox.Text = initialText;
        InputBox.CaretIndex = InputBox.Text?.Length ?? 0;
    }

    private void Ok_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(InputBox.Text?.Trim());
    }

    private void Cancel_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(null);
    }
}

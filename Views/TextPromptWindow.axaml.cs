using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace RemoteRDPTool.Views;

public partial class TextPromptWindow : Window
{
  public TextPromptWindow()
      : this("提示", string.Empty, string.Empty, readOnly: false, showCancel: true, okText: "确定")
  {
  }

  public TextPromptWindow(string title, string label, string initialText)
      : this(title, label, initialText, readOnly: false, showCancel: true, okText: "确定")
  {
  }

  public TextPromptWindow(string title, string label, string initialText, bool readOnly, bool showCancel, string okText)
  {
    InitializeComponent();
    Title = title;
    TitleBlock.Text = title;
    LabelBlock.Text = label;
    InputBox.Text = initialText;

    CancelButton.IsVisible = showCancel;
    OkButton.Content = okText;

    if (readOnly)
    {
      InputBox.IsReadOnly = true;
      InputBox.AcceptsReturn = true;
      InputBox.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
      InputBox.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
      InputBox.MinHeight = 140;
    }
    else
    {
      InputBox.AcceptsReturn = false;
      InputBox.TextWrapping = Avalonia.Media.TextWrapping.NoWrap;
      InputBox.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
      InputBox.CaretIndex = InputBox.Text?.Length ?? 0;
    }
  }

  private void Ok_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
  {
    Close(InputBox.Text?.Trim());
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

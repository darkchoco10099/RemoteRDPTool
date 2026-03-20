using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RemoteRDPTool.ViewModels;

namespace RemoteRDPTool.Views;

public partial class MainWindow : Window
{
  private readonly TrayIcon _trayIcon;
  private readonly NativeMenuItem _toggleWindowMenuItem;
  private MainWindowViewModel? _viewModel;
  private bool _isExiting;
  private bool _isApplyingWindowState;
  private readonly GlobalHotkeyManager _hotkeyManager = new();

  public MainWindow()
  {
    InitializeComponent();
    _toggleWindowMenuItem = new NativeMenuItem("显示窗口");
    var exitMenuItem = new NativeMenuItem("退出");
    _toggleWindowMenuItem.Click += (_, _) => ToggleWindowVisibility();
    exitMenuItem.Click += (_, _) => ExitApplication();

    var menu = new NativeMenu();
    menu.Add(_toggleWindowMenuItem);
    menu.Add(new NativeMenuItemSeparator());
    menu.Add(exitMenuItem);

    _trayIcon = new TrayIcon
    {
      ToolTipText = "RemoteRDPTool",
      Icon = CreateTrayWindowIcon(),
      IsVisible = true,
      Menu = menu
    };
    _trayIcon.Clicked += (_, _) => ToggleWindowVisibility();

    DataContextChanged += OnDataContextChanged;
    PropertyChanged += OnWindowPropertyChanged;
    Closing += OnWindowClosing;
    Opened += OnWindowOpened;

    _hotkeyManager.Pressed += OnGlobalHotkeyPressed;
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

  private void SummonHotkeyInput_GotFocus(object? sender, GotFocusEventArgs e)
  {
    _hotkeyManager.IsPaused = true;
  }

  private void SummonHotkeyInput_LostFocus(object? sender, RoutedEventArgs e)
  {
    _hotkeyManager.IsPaused = false;
  }

  private void SummonHotkeyInput_KeyDown(object? sender, KeyEventArgs e)
  {
    e.Handled = true;
    if (sender is not TextBox input)
      return;

    if (!TryBuildHotkeyText(e, out var hotkey))
      return;

    input.Text = hotkey;
    if (_viewModel is not null)
      _viewModel.SummonHotkey = hotkey;
  }

  private void OnWindowOpened(object? sender, EventArgs e)
  {
    if (_viewModel is not null)
      ApplyHotkeySetting(_viewModel.SummonHotkey);
  }

  private void OnDataContextChanged(object? sender, EventArgs e)
  {
    if (_viewModel is not null)
      _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

    _viewModel = DataContext as MainWindowViewModel;
    if (_viewModel is null)
      return;

    _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    ApplyHotkeySetting(_viewModel.SummonHotkey);
  }

  private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
  {
    if (e.PropertyName == nameof(MainWindowViewModel.SummonHotkey) && _viewModel is not null)
      ApplyHotkeySetting(_viewModel.SummonHotkey);
  }

  private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
  {
    if (e.Property != WindowStateProperty || _isApplyingWindowState || _isExiting)
      return;

    if (WindowState == WindowState.Minimized)
      MinimizeToTray();
  }

  private void MinimizeToTray()
  {
    if (_isExiting)
      return;

    _isApplyingWindowState = true;
    WindowState = WindowState.Minimized;
    _isApplyingWindowState = false;
    Hide();
    _toggleWindowMenuItem.Header = "显示窗口";
  }

  private void RestoreFromTray()
  {
    Show();
    _isApplyingWindowState = true;
    if (WindowState == WindowState.Minimized)
      WindowState = WindowState.Normal;
    _isApplyingWindowState = false;
    Activate();
    _toggleWindowMenuItem.Header = "最小化到托盘";
  }

  private void ToggleWindowVisibility()
  {
    if (!IsVisible || WindowState == WindowState.Minimized)
      RestoreFromTray();
    else
      MinimizeToTray();
  }

  private void ApplyHotkeySetting(string hotkeyText)
  {
    var normalized = _hotkeyManager.Update(hotkeyText);
    if (_viewModel is not null && !string.Equals(_viewModel.SummonHotkey, normalized, StringComparison.Ordinal))
      _viewModel.SummonHotkey = normalized;
  }

  private void OnGlobalHotkeyPressed()
  {
    Dispatcher.UIThread.Post(ToggleWindowVisibility);
  }

  private void ExitApplication()
  {
    _isExiting = true;
    _hotkeyManager.Dispose();
    _trayIcon.IsVisible = false;
    Close();
  }

  private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
  {
    _hotkeyManager.Dispose();
    _trayIcon.IsVisible = false;
    _trayIcon.Dispose();

    if (_viewModel is not null)
      _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
  }

  private static WindowIcon CreateTrayWindowIcon()
  {
    using var stream = AssetLoader.Open(new Uri("avares://RemoteRDPTool/Assets/logo.ico"));
    return new WindowIcon(stream);
  }

  private static bool TryBuildHotkeyText(KeyEventArgs e, out string hotkeyText)
  {
    hotkeyText = string.Empty;
    var key = e.Key;
    if (key == Key.None || key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt || key == Key.LeftShift || key == Key.RightShift || key == Key.LWin || key == Key.RWin)
      return false;

    var parts = new List<string>(5);
    if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
      parts.Add("Ctrl");
    if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
      parts.Add("Alt");
    if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
      parts.Add("Shift");
    if (e.KeyModifiers.HasFlag(KeyModifiers.Meta))
      parts.Add("Win");

    if (parts.Count == 0)
      return false;

    if (!TryMapKeyToHotkeyToken(key, out var keyToken))
      return false;

    parts.Add(keyToken);
    hotkeyText = string.Join("+", parts);
    return true;
  }

  private static bool TryMapKeyToHotkeyToken(Key key, out string keyToken)
  {
    keyToken = string.Empty;
    if (key is >= Key.A and <= Key.Z)
    {
      keyToken = key.ToString().ToUpperInvariant();
      return true;
    }

    if (key is >= Key.D0 and <= Key.D9)
    {
      keyToken = ((char)('0' + (key - Key.D0))).ToString();
      return true;
    }

    if (key is >= Key.NumPad0 and <= Key.NumPad9)
    {
      keyToken = ((char)('0' + (key - Key.NumPad0))).ToString();
      return true;
    }

    if (key is >= Key.F1 and <= Key.F24)
    {
      keyToken = $"F{key - Key.F1 + 1}";
      return true;
    }

    keyToken = key switch
    {
      Key.Space => "Space",
      Key.Tab => "Tab",
      Key.Enter => "Enter",
      Key.Escape => "Esc",
      _ => string.Empty
    };

    return !string.IsNullOrEmpty(keyToken);
  }

  private sealed class GlobalHotkeyManager : IDisposable
  {
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkShift = 0x10;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;
    private const int KeyDownMask = 0x8000;

    private HotkeyBinding _binding = HotkeyBinding.Default;
    private HookProc? _hookProc;
    private IntPtr _hookHandle = IntPtr.Zero;
    private bool _isMainKeyPressed;

    public event Action? Pressed;
    public bool IsPaused { get; set; }

    public string Update(string text)
    {
      if (!HotkeyBinding.TryParse(text, out var parsed))
        parsed = HotkeyBinding.Default;

      _binding = parsed;
      EnsureHookInstalled();
      return parsed.ToDisplayText();
    }

    public void Dispose()
    {
      if (_hookHandle != IntPtr.Zero)
      {
        UnhookWindowsHookEx(_hookHandle);
        _hookHandle = IntPtr.Zero;
      }
    }

    private void EnsureHookInstalled()
    {
      if (!OperatingSystem.IsWindows() || _hookHandle != IntPtr.Zero)
        return;

      _hookProc = HookCallback;
      using var process = Process.GetCurrentProcess();
      using var module = process.MainModule;
      var moduleHandle = module is null ? IntPtr.Zero : GetModuleHandle(module.ModuleName);
      _hookHandle = SetWindowsHookEx(WhKeyboardLl, _hookProc, moduleHandle, 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
      if (nCode < 0)
        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

      var msg = unchecked((int)(long)wParam);
      var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);

      if (msg == WmKeyDown || msg == WmSysKeyDown)
      {
        if (data.VkCode == _binding.VirtualKey && AreModifiersMatched(_binding.Modifiers))
        {
          if (!_isMainKeyPressed && !IsPaused)
          {
            _isMainKeyPressed = true;
            Pressed?.Invoke();
          }
        }
      }
      else if ((msg == WmKeyUp || msg == WmSysKeyUp) && data.VkCode == _binding.VirtualKey)
      {
        _isMainKeyPressed = false;
      }

      return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private static bool AreModifiersMatched(HotkeyModifiers expected)
    {
      var ctrlPressed = IsPressed(VkControl);
      var altPressed = IsPressed(VkMenu);
      var shiftPressed = IsPressed(VkShift);
      var winPressed = IsPressed(VkLWin) || IsPressed(VkRWin);

      var needCtrl = expected.HasFlag(HotkeyModifiers.Ctrl);
      var needAlt = expected.HasFlag(HotkeyModifiers.Alt);
      var needShift = expected.HasFlag(HotkeyModifiers.Shift);
      var needWin = expected.HasFlag(HotkeyModifiers.Win);

      return ctrlPressed == needCtrl &&
             altPressed == needAlt &&
             shiftPressed == needShift &&
             winPressed == needWin;
    }

    private static bool IsPressed(int virtualKey)
    {
      return (GetAsyncKeyState(virtualKey) & KeyDownMask) != 0;
    }

    private readonly record struct HotkeyBinding(int VirtualKey, HotkeyModifiers Modifiers, string KeyText)
    {
      public static HotkeyBinding Default => new(0x52, HotkeyModifiers.Ctrl, "R");

      public string ToDisplayText()
      {
        var parts = new System.Collections.Generic.List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl))
          parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
          parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
          parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win))
          parts.Add("Win");
        parts.Add(KeyText);
        return string.Join("+", parts);
      }

      public static bool TryParse(string? text, out HotkeyBinding binding)
      {
        binding = Default;
        if (string.IsNullOrWhiteSpace(text))
          return false;

        var segments = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
          return false;

        HotkeyModifiers modifiers = HotkeyModifiers.None;
        for (var i = 0; i < segments.Length - 1; i++)
        {
          var segment = segments[i];
          if (segment.Equals("ctrl", StringComparison.OrdinalIgnoreCase) || segment.Equals("control", StringComparison.OrdinalIgnoreCase))
            modifiers |= HotkeyModifiers.Ctrl;
          else if (segment.Equals("alt", StringComparison.OrdinalIgnoreCase))
            modifiers |= HotkeyModifiers.Alt;
          else if (segment.Equals("shift", StringComparison.OrdinalIgnoreCase))
            modifiers |= HotkeyModifiers.Shift;
          else if (segment.Equals("win", StringComparison.OrdinalIgnoreCase) || segment.Equals("windows", StringComparison.OrdinalIgnoreCase) || segment.Equals("meta", StringComparison.OrdinalIgnoreCase))
            modifiers |= HotkeyModifiers.Win;
          else
            return false;
        }

        if (modifiers == HotkeyModifiers.None)
          return false;

        var keyPart = segments[^1];
        if (!TryParseVirtualKey(keyPart, out var vk, out var normalizedKey))
          return false;

        binding = new HotkeyBinding(vk, modifiers, normalizedKey);
        return true;
      }

      private static bool TryParseVirtualKey(string keyPart, out int virtualKey, out string normalizedKey)
      {
        virtualKey = 0;
        normalizedKey = string.Empty;

        if (keyPart.Length == 1)
        {
          var c = char.ToUpperInvariant(keyPart[0]);
          if (c is >= 'A' and <= 'Z')
          {
            virtualKey = c;
            normalizedKey = c.ToString();
            return true;
          }

          if (c is >= '0' and <= '9')
          {
            virtualKey = c;
            normalizedKey = c.ToString();
            return true;
          }
        }

        if (keyPart.StartsWith("F", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(keyPart[1..], out var fnNumber)
            && fnNumber is >= 1 and <= 24)
        {
          virtualKey = 0x70 + (fnNumber - 1);
          normalizedKey = $"F{fnNumber}";
          return true;
        }

        if (keyPart.Equals("Space", StringComparison.OrdinalIgnoreCase))
        {
          virtualKey = 0x20;
          normalizedKey = "Space";
          return true;
        }

        if (keyPart.Equals("Tab", StringComparison.OrdinalIgnoreCase))
        {
          virtualKey = 0x09;
          normalizedKey = "Tab";
          return true;
        }

        if (keyPart.Equals("Enter", StringComparison.OrdinalIgnoreCase))
        {
          virtualKey = 0x0D;
          normalizedKey = "Enter";
          return true;
        }

        if (keyPart.Equals("Esc", StringComparison.OrdinalIgnoreCase) || keyPart.Equals("Escape", StringComparison.OrdinalIgnoreCase))
        {
          virtualKey = 0x1B;
          normalizedKey = "Esc";
          return true;
        }

        return false;
      }
    }

    [Flags]
    private enum HotkeyModifiers
    {
      None = 0,
      Alt = 1,
      Ctrl = 2,
      Shift = 4,
      Win = 8
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
      public int VkCode;
      public int ScanCode;
      public int Flags;
      public int Time;
      public IntPtr DwExtraInfo;
    }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
  }
}

using System;
using System.Linq;
using System.Threading.Tasks;
using AtomNES.Services;
using AtomNES.ViewModels;
using AtomUI.Desktop.Controls;
using Avalonia.Controls;
using Window = AtomUI.Desktop.Controls.Window;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace AtomNES.Views;

public partial class MainWindow : Window
{
    private readonly KeyboardInput _keyboard = new();
    private WindowMessageManager? _messages;
    private DebuggerWindow? _debugger;

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnKeyUpTunnel, RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DropEvent, OnDrop);
        Deactivated += (_, _) =>
        {
            if (Vm is { } vm) _keyboard.Clear(vm.Host.Bus.Controller1, vm.Host.Bus.Controller2);
        };
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (Vm is not { } vm) return;
        vm.PickRomFile = PickRomFileAsync;
        vm.ShowMessage = ShowMessage;
        vm.OpenDebugger = OpenDebugger;
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.ScaleMode)) ApplyScaleWidth();
        };
        ApplyScaleWidth();
    }

    private void OnAboutClick(object? sender, RoutedEventArgs e) => new AboutWindow().ShowDialog(this);

    private void OnKeyMapClick(object? sender, RoutedEventArgs e) => ShowKeyMap();

    private void ShowKeyMap()
    {
        if (Vm is { } vm) _keyboard.Clear(vm.Host.Bus.Controller1, vm.Host.Bus.Controller2);
        new KeyMapWindow(_keyboard).ShowDialog(this);
    }

    // 窗口宽度严格等于画面宽度（256 × 8/7 × 倍率），高度由内容决定
    private void ApplyScaleWidth()
    {
        if (Vm is not { } vm) return;
        var scale = vm.ScaleMode switch
        {
            Controls.ScreenScaleMode.X1 => 1,
            Controls.ScreenScaleMode.X3 => 3,
            _ => 2
        };
        Width = Math.Ceiling(256 * 8.0 / 7.0 * scale);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _messages = new WindowMessageManager(this) { MaxItems = 3, Position = NotificationPosition.TopRight };
    }

    protected override void OnClosed(EventArgs e)
    {
        _debugger?.Close();
        Vm?.Dispose();
        base.OnClosed(e);
    }

    private void ShowMessage(string text, MessageType type) =>
        _messages?.Show(new Message(text, type, expiration: TimeSpan.FromSeconds(2.5)));

    private async Task<string?> PickRomFileAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "打开 NES ROM",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("NES ROM") { Patterns = ["*.nes", "*.NES"] }]
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    private void OpenDebugger()
    {
        if (Vm is not { } vm) return;
        if (_debugger is not null)
        {
            _debugger.Activate();
            return;
        }

        _debugger = new DebuggerWindow { DataContext = new DebuggerViewModel(vm.Host) };
        _debugger.Closed += (_, _) => _debugger = null;
        _debugger.Show(this);
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var file = e.DataTransfer.TryGetFiles()?.FirstOrDefault()?.TryGetLocalPath();
        if (file is not null) Vm?.LoadRom(file);
    }

    private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        if (Vm is not { } vm) return;
        switch (e.Key)
        {
            case Key.F5: vm.TogglePauseCommand.Execute(null); e.Handled = true; return;
            case Key.F2: vm.ResetCommand.Execute(null); e.Handled = true; return;
            case Key.F6: vm.IsMuted = !vm.IsMuted; e.Handled = true; return;
            case Key.F8: OpenDebugger(); e.Handled = true; return;
            case Key.F1: ShowKeyMap(); e.Handled = true; return;
            case Key.O when e.KeyModifiers == KeyModifiers.Control: vm.OpenRomCommand.Execute(null); e.Handled = true; return;
        }

        if (_keyboard.Handle(e.Key, true, vm.Host.Bus.Controller1, vm.Host.Bus.Controller2)) e.Handled = true;
    }

    private void OnKeyUpTunnel(object? sender, KeyEventArgs e)
    {
        if (Vm is not { } vm) return;
        if (_keyboard.Handle(e.Key, false, vm.Host.Bus.Controller1, vm.Host.Bus.Controller2)) e.Handled = true;
    }

    private void OnExitClick(object? sender, RoutedEventArgs e) => Close();
}

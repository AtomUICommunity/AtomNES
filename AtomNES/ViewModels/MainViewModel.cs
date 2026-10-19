using System;
using System.IO;
using System.Threading.Tasks;
using AtomNES.Controls;
using AtomNES.Device.Display;
using AtomNES.Services;
using AtomUI.Desktop.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AtomNES.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly DispatcherTimer _statusTimer;

    public MainViewModel()
    {
        Host.StateChanged += () => Dispatcher.UIThread.Post(RefreshState);
        Host.Faulted += ex => Dispatcher.UIThread.Post(() => ShowMessage?.Invoke($"模拟异常：{ex.Message}", MessageType.Error));
        _statusTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background,
            (_, _) => Fps = Host.MeasuredFps);
        _statusTimer.Start();
    }

    public EmulatorHost Host { get; } = new();

    public FrameBuffer Screen => Host.Screen;

    /// <summary>由视图注入：弹出文件选择框。</summary>
    public Func<Task<string?>>? PickRomFile { get; set; }

    /// <summary>由视图注入：弹出全局提示。</summary>
    public Action<string, MessageType>? ShowMessage { get; set; }

    /// <summary>由视图注入：打开调试器窗口。</summary>
    public Action? OpenDebugger { get; set; }

    [ObservableProperty] private bool _isLoaded;
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private string _romName = "";

    public string WindowTitle => string.IsNullOrEmpty(RomName) ? "AtomNES" : $"AtomNES - {RomName}";
    [ObservableProperty] private string _mapperText = "";
    [ObservableProperty] private double _fps;
    [ObservableProperty] private ScreenScaleMode _scaleMode = ScreenScaleMode.X2;
    [ObservableProperty] private double _speed = 1.0;

    public string StateText => !IsLoaded ? "空闲" : IsPaused ? "已暂停" : "运行中";

    partial void OnIsLoadedChanged(bool value) => OnPropertyChanged(nameof(StateText));

    partial void OnIsPausedChanged(bool value) => OnPropertyChanged(nameof(StateText));

    partial void OnSpeedChanged(double value) => Host.Speed = value;

    private void RefreshState()
    {
        IsLoaded = Host.IsLoaded;
        IsPaused = Host.IsPaused;
    }

    [RelayCommand]
    private async Task OpenRom()
    {
        if (PickRomFile is null) return;
        var path = await PickRomFile();
        if (string.IsNullOrEmpty(path)) return;
        LoadRom(path);
    }

    public void LoadRom(string path)
    {
        try
        {
            Host.Load(path);
            var rom = Host.Rom!;
            RomName = Path.GetFileNameWithoutExtension(path);
            MapperText = $"Mapper {rom.MapperId:D3}";
            RefreshState();
            ShowMessage?.Invoke($"已加载：{RomName}", MessageType.Success);
        }
        catch (NotSupportedException)
        {
            ShowMessage?.Invoke("暂不支持该 ROM 的 Mapper", MessageType.Warning);
        }
        catch (Exception ex)
        {
            ShowMessage?.Invoke($"加载失败：{ex.Message}", MessageType.Error);
        }
    }

    [RelayCommand]
    private void UnloadRom()
    {
        Host.Unload();
        RomName = "";
        MapperText = "";
        RefreshState();
    }

    [RelayCommand]
    private void Reset() => Host.Reset();

    [RelayCommand]
    private void PowerCycle() => Host.PowerCycle();

    [RelayCommand]
    private void TogglePause()
    {
        if (!Host.IsLoaded) return;
        if (Host.IsPaused) Host.Resume();
        else Host.Pause();
    }

    [RelayCommand]
    private void SetSpeed(string value) => Speed = double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    [RelayCommand]
    private void SetScale(ScreenScaleMode mode) => ScaleMode = mode;

    [RelayCommand]
    private void ShowDebugger() => OpenDebugger?.Invoke();

    public void Dispose()
    {
        _statusTimer.Stop();
        Host.Dispose();
    }
}

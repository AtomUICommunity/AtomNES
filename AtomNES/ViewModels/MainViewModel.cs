using System;
using System.IO;
using System.Threading.Tasks;
using AtomNES.Controls;
using AtomNES.Device.Display;
using AtomNES.Localization;
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
        Host.Faulted += ex => Dispatcher.UIThread.Post(() => ShowMessage?.Invoke(Lang.Format(AppLangResourceKind.MsgEmuError, ex.Message), MessageType.Error));
        Lang.Changed += (_, _) => OnPropertyChanged(nameof(StateText));
        _statusTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background,
            (_, _) => Fps = Host.MeasuredFps);
        _statusTimer.Start();
        Host.Volume = (float)Volume;
        if (Host.Audio is null)
        {
            Dispatcher.UIThread.Post(() => ShowMessage?.Invoke(Lang.Format(AppLangResourceKind.MsgAudioFailed, Host.AudioError), MessageType.Warning));
        }
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
    [ObservableProperty] private double _volume = 0.8;

    partial void OnVolumeChanged(double value)
    {
        Host.Volume = (float)value;
        Host.Muted = value <= 0;
    }

    [RelayCommand]
    private void SetVolume(string value) => Volume = double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    public string StateText => Lang.Get(!IsLoaded ? AppLangResourceKind.StateIdle
        : IsPaused ? AppLangResourceKind.StatePaused : AppLangResourceKind.StateRunning);

    [RelayCommand]
    private void SetLanguage(string tag) => Lang.Change(AtomUI.Localization.LanguageTag.Parse(tag));

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
            ShowMessage?.Invoke(Lang.Format(AppLangResourceKind.MsgLoaded, RomName), MessageType.Success);
        }
        catch (NotSupportedException)
        {
            var id = TryReadMapperId(path);
            ShowMessage?.Invoke(Lang.Format(AppLangResourceKind.MsgUnsupportedMapper, id), MessageType.Warning);
        }
        catch (Exception ex)
        {
            ShowMessage?.Invoke(Lang.Format(AppLangResourceKind.MsgLoadFailed, ex.Message), MessageType.Error);
        }
    }

    private static string TryReadMapperId(string path)
    {
        try
        {
            return AtomNES.Device.Cartridges.RomImage.Load(path).MapperId.ToString();
        }
        catch
        {
            return "?";
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

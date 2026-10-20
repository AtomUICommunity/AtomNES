using System.Collections.Generic;
using System.Linq;
using AtomNES.Device.Input;
using AtomNES.Localization;
using AtomNES.Services;
using Avalonia.Input;
using Window = AtomUI.Desktop.Controls.Window;

namespace AtomNES.Views;

public sealed record KeyMapRow(string Button, string Key);

public partial class KeyMapWindow : Window
{
    private static readonly NesButtons[] Order =
    [
        NesButtons.Up, NesButtons.Down, NesButtons.Left, NesButtons.Right,
        NesButtons.A, NesButtons.B, NesButtons.Select, NesButtons.Start
    ];

    public KeyMapWindow() : this(new KeyboardInput()) { }

    public KeyMapWindow(KeyboardInput input)
    {
        InitializeComponent();
        Player1List.ItemsSource = BuildRows(input.Player1);
        Player2List.ItemsSource = BuildRows(input.Player2);
        HotkeyList.ItemsSource = new[]
        {
            new KeyMapRow(Lang.Get(AppLangResourceKind.MenuOpenRom).TrimEnd('…'), "Ctrl + O"),
            new KeyMapRow(Lang.Get(AppLangResourceKind.MenuPause), "F5"),
            new KeyMapRow(Lang.Get(AppLangResourceKind.MenuReset), "F2"),
            new KeyMapRow(Lang.Get(AppLangResourceKind.MenuDebugger), "F8"),
            new KeyMapRow(Lang.Get(AppLangResourceKind.MenuKeyMap), "F1"),
        };
    }

    private static List<KeyMapRow> BuildRows(Dictionary<Key, NesButtons> map) =>
        Order.Select(b => new KeyMapRow(ButtonName(b),
                string.Join(" / ", map.Where(p => p.Value == b).Select(p => KeyName(p.Key)))))
            .ToList();

    private static string ButtonName(NesButtons b) => b switch
    {
        NesButtons.Up => Lang.Get(AppLangResourceKind.ButtonUp),
        NesButtons.Down => Lang.Get(AppLangResourceKind.ButtonDown),
        NesButtons.Left => Lang.Get(AppLangResourceKind.ButtonLeft),
        NesButtons.Right => Lang.Get(AppLangResourceKind.ButtonRight),
        _ => b.ToString()
    };

    private static string KeyName(Key k) => k switch
    {
        Key.Up => "↑",
        Key.Down => "↓",
        Key.Left => "←",
        Key.Right => "→",
        Key.RightCtrl => Lang.Get(AppLangResourceKind.KeyRightCtrl),
        Key.Add => Lang.Get(AppLangResourceKind.KeyNumPad) + " +",
        Key.Subtract => Lang.Get(AppLangResourceKind.KeyNumPad) + " -",
        >= Key.NumPad0 and <= Key.NumPad9 => Lang.Get(AppLangResourceKind.KeyNumPad) + " " + (k - Key.NumPad0),
        _ => k.ToString()
    };
}

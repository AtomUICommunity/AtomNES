using System.Collections.Generic;
using System.Linq;
using AtomNES.Device.Input;
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
            new KeyMapRow("打开 ROM", "Ctrl + O"),
            new KeyMapRow("暂停 / 继续", "F5"),
            new KeyMapRow("复位 (Reset)", "F2"),
            new KeyMapRow("调试器", "F8"),
        };
    }

    private static List<KeyMapRow> BuildRows(Dictionary<Key, NesButtons> map) =>
        Order.Select(b => new KeyMapRow(ButtonName(b),
                string.Join(" / ", map.Where(p => p.Value == b).Select(p => KeyName(p.Key)))))
            .ToList();

    private static string ButtonName(NesButtons b) => b switch
    {
        NesButtons.Up => "上",
        NesButtons.Down => "下",
        NesButtons.Left => "左",
        NesButtons.Right => "右",
        _ => b.ToString()
    };

    private static string KeyName(Key k) => k switch
    {
        Key.Up => "↑",
        Key.Down => "↓",
        Key.Left => "←",
        Key.Right => "→",
        Key.RightCtrl => "右 Ctrl",
        Key.Add => "小键盘 +",
        Key.Subtract => "小键盘 -",
        >= Key.NumPad0 and <= Key.NumPad9 => "小键盘 " + (k - Key.NumPad0),
        _ => k.ToString()
    };
}

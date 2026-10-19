using System.Collections.Generic;
using AtomNES.Device.Input;
using Avalonia.Input;

namespace AtomNES.Services;

/// <summary>
/// 键盘 → 手柄映射。默认与旧项目保持一致：P1 = WASD/J/K/Enter/RightCtrl，P2 = 方向键/小键盘。
/// </summary>
public sealed class KeyboardInput
{
    private readonly HashSet<Key> _pressed = [];

    public Dictionary<Key, NesButtons> Player1 { get; } = new()
    {
        [Key.K] = NesButtons.A,
        [Key.J] = NesButtons.B,
        [Key.RightCtrl] = NesButtons.Select,
        [Key.Enter] = NesButtons.Start,
        [Key.W] = NesButtons.Up,
        [Key.S] = NesButtons.Down,
        [Key.A] = NesButtons.Left,
        [Key.D] = NesButtons.Right,
    };

    public Dictionary<Key, NesButtons> Player2 { get; } = new()
    {
        [Key.NumPad2] = NesButtons.A,
        [Key.NumPad1] = NesButtons.B,
        [Key.Add] = NesButtons.Select,
        [Key.Subtract] = NesButtons.Start,
        [Key.Up] = NesButtons.Up,
        [Key.Down] = NesButtons.Down,
        [Key.Left] = NesButtons.Left,
        [Key.Right] = NesButtons.Right,
    };

    /// <summary>返回该按键是否属于手柄映射（属于则应吞掉事件）。</summary>
    public bool Handle(Key key, bool pressed, StandardController p1, StandardController p2)
    {
        if (!Player1.ContainsKey(key) && !Player2.ContainsKey(key)) return false;
        if (pressed) _pressed.Add(key);
        else _pressed.Remove(key);
        p1.Buttons = Compose(Player1);
        p2.Buttons = Compose(Player2);
        return true;
    }

    public void Clear(StandardController p1, StandardController p2)
    {
        _pressed.Clear();
        p1.Buttons = NesButtons.None;
        p2.Buttons = NesButtons.None;
    }

    private NesButtons Compose(Dictionary<Key, NesButtons> map)
    {
        var buttons = NesButtons.None;
        foreach (var key in _pressed)
        {
            if (map.TryGetValue(key, out var b)) buttons |= b;
        }

        // 真实手柄的十字键无法同时按下相反方向，部分游戏遇到会出错
        if ((buttons & (NesButtons.Left | NesButtons.Right)) == (NesButtons.Left | NesButtons.Right))
            buttons &= ~(NesButtons.Left | NesButtons.Right);
        if ((buttons & (NesButtons.Up | NesButtons.Down)) == (NesButtons.Up | NesButtons.Down))
            buttons &= ~(NesButtons.Up | NesButtons.Down);
        return buttons;
    }
}

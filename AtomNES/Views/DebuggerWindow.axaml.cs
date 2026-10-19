using System;
using AtomNES.ViewModels;
using AtomUI.Desktop.Controls;

namespace AtomNES.Views;

public partial class DebuggerWindow : Window
{
    public DebuggerWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosed(EventArgs e)
    {
        (DataContext as DebuggerViewModel)?.Dispose();
        base.OnClosed(e);
    }
}

using System;
using System.Reflection;
using Window = AtomUI.Desktop.Controls.Window;

namespace AtomNES.Views;

public sealed record LibraryRow(string Name, string Version, bool IsAtom = false);

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionTag.Text = "v" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0");
        LibraryList.ItemsSource = new[]
        {
            new LibraryRow("AtomUI.Desktop.Controls", VersionOf("AtomUI.Desktop.Controls"), true),
            new LibraryRow("AtomUI.Icons.AntDesign", VersionOf("AtomUI.Icons.AntDesign"), true),
            new LibraryRow("AtomUI.Fonts.AlibabaSans", VersionOf("AtomUI.Fonts.AlibabaSans"), true),
            new LibraryRow("Avalonia", VersionOf("Avalonia.Base")),
            new LibraryRow("CommunityToolkit.Mvvm", VersionOf("CommunityToolkit.Mvvm")),
            new LibraryRow(".NET", Environment.Version.ToString(3)),
        };
    }

    private static string VersionOf(string assembly)
    {
        try
        {
            var asm = Assembly.Load(assembly);
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrEmpty(info)) return info.Split('+')[0];
            return asm.GetName().Version?.ToString(3) ?? "-";
        }
        catch
        {
            return "-";
        }
    }
}

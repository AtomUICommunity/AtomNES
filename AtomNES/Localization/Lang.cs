using System;
using AtomUI;
using AtomUI.Localization;
using Avalonia;

namespace AtomNES.Localization;

/// <summary>代码中获取本地化文本、切换语言的便捷入口（基于 AtomUI 的 ILanguageManager / ILocalizer）。</summary>
public static class Lang
{
    public static readonly LanguageTag Chinese = LanguageTags.ZhCN;
    public static readonly LanguageTag English = LanguageTags.EnUS;

    private static ILanguageManager? Manager => Application.Current?.GetLanguageManager();

    private static ILocalizer? Localizer => Application.Current?.GetLocalizer();

    public static LanguageTag Current => Manager?.Current.CurrentLanguage ?? Chinese;

    public static event EventHandler? Changed;

    public static string Get(AppLangResourceKind key) => Localizer?.Get(key) ?? key.ToString();

    public static string Format(AppLangResourceKind key, params object?[] args) =>
        Localizer?.Format(key, args) ?? key.ToString();

    public static void Attach()
    {
        if (Manager is { } m) m.LanguageChanged += (_, _) => Changed?.Invoke(null, EventArgs.Empty);
    }

    public static void Change(LanguageTag language) => Manager?.ChangeLanguage(language);
}

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using MyDriver.Core.Logging;
using MyDriver.Core.Settings;

namespace MyDriver.App.Services;

/// <summary>
/// Mengatur tema terang/gelap: warna aplikasi (Themes/Light.xaml, Themes/Dark.xaml),
/// gaya kontrol bawaan WPF Fluent, dan warna bilah judul jendela.
/// </summary>
public sealed class ThemeService
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmUseImmersiveDarkModeOld = 19;
    private bool _fluentAvailable = true;

    private readonly Application _app;
    private readonly ILogService _log;
    private ResourceDictionary _palette;
    private AppTheme _theme = AppTheme.System;

    public ThemeService(Application app, ILogService log)
    {
        _app = app;
        _log = log;
        _palette = _app.Resources.MergedDictionaries
            .First(d => d.Source?.OriginalString.EndsWith("Themes/Light.xaml", StringComparison.OrdinalIgnoreCase) == true);

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public AppTheme Theme => _theme;

    public bool IsDark { get; private set; }

    public void Apply(AppTheme theme)
    {
        _theme = theme;
        ApplyEffective();
    }

    /// <summary>Menyamakan warna bilah judul jendela dengan tema.</summary>
    public void Attach(Window window)
    {
        window.SourceInitialized += (_, _) => SetTitleBar(window, IsDark);
    }

    private void ApplyEffective()
    {
        var dark = _theme switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => SystemUsesDarkTheme(),
        };
        IsDark = dark;

        var palette = new ResourceDictionary
        {
            Source = new Uri($"/MyDriver;component/Themes/{(dark ? "Dark" : "Light")}.xaml", UriKind.Relative),
        };
        var dictionaries = _app.Resources.MergedDictionaries;
        var index = dictionaries.IndexOf(_palette);
        if (index >= 0)
        {
            dictionaries[index] = palette;
        }
        else
        {
            dictionaries.Add(palette);
        }
        _palette = palette;

        // Gaya kontrol Fluent (Windows 10/11). Bila gagal di versi Windows tertentu,
        // aplikasi tetap jalan dengan gaya kontrol klasik dan warna tema sendiri.
        if (_fluentAvailable)
        {
            try
            {
#pragma warning disable WPF0001 // ThemeMode masih berlabel eksperimen di WPF
                _app.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001
            }
            catch (Exception ex)
            {
                _fluentAvailable = false;
                _log.Warning("Tema Fluent tidak bisa dipakai di Windows ini; memakai gaya kontrol klasik.", ex);
            }
        }

        foreach (Window window in _app.Windows)
        {
            SetTitleBar(window, dark);
        }

        _log.Debug($"Tema diterapkan: {_theme} ({(dark ? "gelap" : "terang")}).");
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_theme == AppTheme.System && e.Category == UserPreferenceCategory.General)
        {
            _app.Dispatcher.BeginInvoke(ApplyEffective);
        }
    }

    private static bool SystemUsesDarkTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }

    private static void SetTitleBar(Window window, bool dark)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }
        var value = dark ? 1 : 0;
        // Windows 10 sebelum build 18985 memakai atribut 19 untuk bilah judul gelap.
        if (DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref value, sizeof(int)) != 0)
        {
            _ = DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkModeOld, ref value, sizeof(int));
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}

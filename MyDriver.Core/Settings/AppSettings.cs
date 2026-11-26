namespace MyDriver.Core.Settings;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>Pengaturan aplikasi yang disimpan di <c>settings.json</c>.</summary>
public sealed class AppSettings
{
    public AppTheme Theme { get; set; } = AppTheme.System;
}

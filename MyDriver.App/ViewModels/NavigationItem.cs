using CommunityToolkit.Mvvm.ComponentModel;

namespace MyDriver.App.ViewModels;

/// <summary>Satu butir menu samping beserta halaman yang ditampilkannya.</summary>
public sealed record NavigationItem(string Title, string Glyph, ObservableObject Page);

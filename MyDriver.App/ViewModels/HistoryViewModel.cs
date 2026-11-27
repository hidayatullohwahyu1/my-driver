using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MyDriver.App.Services;
using MyDriver.Core.Repair;

namespace MyDriver.App.ViewModels;

public sealed record HistoryFilter(string Label, Func<HistoryEntry, bool> Include);

/// <summary>Halaman Riwayat: semua scan dan aksi, berhasil/gagal, dan restore point yang dibuat.</summary>
public sealed partial class HistoryViewModel : ObservableObject
{
    private const int MaxShown = 300;

    private readonly WorkspaceService _workspace;

    public HistoryViewModel(WorkspaceService workspace)
    {
        _workspace = workspace;
        _workspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WorkspaceService.History))
            {
                Rebuild();
            }
        };
        _selectedFilter = Filters[0];
        Rebuild();
    }

    public IReadOnlyList<HistoryFilter> Filters { get; } =
    [
        new("Aksi dan pemulihan", e => e.Kind is not HistoryKind.Scan),
        new("Semua, termasuk scan", _ => true),
        new("Hanya yang gagal", e => !e.Success),
    ];

    [ObservableProperty]
    private HistoryFilter _selectedFilter;

    public ObservableCollection<HistoryItemViewModel> Items { get; } = [];

    public bool IsEmpty => Items.Count == 0;

    public string SummaryText
    {
        get
        {
            var all = _workspace.History;
            if (all.Count == 0)
            {
                return "Belum ada riwayat untuk komputer ini.";
            }
            var actions = all.Count(e => e.Kind is HistoryKind.Action or HistoryKind.Restore);
            var failed = all.Count(e => !e.Success);
            var restorePoints = all.Count(e => e.RestorePoint?.StartsWith("Dibuat", StringComparison.Ordinal) == true);
            return $"{actions} aksi · {failed} gagal · {restorePoints} restore point dibuat · {all.Count(e => e.Kind == HistoryKind.Scan)} scan";
        }
    }

    partial void OnSelectedFilterChanged(HistoryFilter value) => Rebuild();

    private void Rebuild()
    {
        Items.Clear();
        foreach (var entry in _workspace.History.Where(SelectedFilter.Include).Take(MaxShown))
        {
            Items.Add(new HistoryItemViewModel(entry));
        }
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(SummaryText));
    }
}

public sealed class HistoryItemViewModel
{
    public HistoryItemViewModel(HistoryEntry entry)
    {
        Entry = entry;
    }

    public HistoryEntry Entry { get; }

    public string Title => Entry.Title;

    public string TimeText => $"{Format.DateTime(Entry.Time)} ({Format.Age(Entry.Time)})";

    public string? Message => Entry.Message;

    public string? RestorePointText => Entry.RestorePoint is null ? null : $"Restore point: {Entry.RestorePoint}";

    public bool HasCommands => Entry.Commands.Count > 0;

    public string CommandsText => string.Join("\n", Entry.Commands);

    /// <summary>"ok", "fail", "scan", atau "resolved" untuk warna dan ikon.</summary>
    public string Kind => !Entry.Success ? "fail"
        : Entry.Kind == HistoryKind.Scan ? "scan"
        : Entry.Kind == HistoryKind.Resolved ? "resolved"
        : "ok";

    public string Glyph => Kind switch
    {
        "fail" => "",
        "scan" => "",
        "resolved" => "",
        _ => Entry.Kind switch
        {
            HistoryKind.Backup => "",
            HistoryKind.Restore => "",
            _ => "",
        },
    };

    public string StatusText => Kind switch
    {
        "fail" => "Gagal",
        "scan" => "Scan",
        "resolved" => "Teratasi",
        _ => "Berhasil",
    };
}

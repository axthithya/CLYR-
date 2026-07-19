using Clyr.Contracts;
using Clyr.Core;

namespace Clyr.App.ViewModels;

public sealed class HistoryViewModel(AppSessionViewModel session, ISnapshotStore store) : PageViewModel(session)
{
    public IReadOnlyList<SnapshotSummary> Items { get; private set; } = [];
    public IReadOnlyDictionary<Guid, StorageSnapshot> Details { get; private set; } =
        new Dictionary<Guid, StorageSnapshot>();
    public HistorySettings Settings { get; private set; } = HistorySettings.Default;
    public SnapshotComparison? Comparison { get; private set; }

    public async Task LoadAsync()
    {
        Items = await store.ListAsync();
        var details = new Dictionary<Guid, StorageSnapshot>();
        foreach (var item in Items)
        {
            var snapshot = await store.GetAsync(item.Id);
            if (snapshot is not null) details[item.Id] = snapshot;
        }
        Details = details;
        Settings = await store.GetSettingsAsync();
    }

    public StorageSnapshot? Detail(Guid id) => Details.GetValueOrDefault(id);

    public bool CanOpenResult(Guid id) => Detail(id)?.ScanId is { } scanId && Session.Result?.ScanId == scanId;

    public bool OpenResult(Guid id)
    {
        if (!CanOpenResult(id)) return false;
        Navigate("Results");
        return true;
    }

    public async Task<SnapshotComparison?> CompareAsync(Guid first, Guid second)
    {
        var snapshots = await Task.WhenAll(store.GetAsync(first), store.GetAsync(second));
        if (snapshots[0] is null || snapshots[1] is null) return null;
        var ordered = snapshots.OrderBy(item => item!.CapturedAtUtc).ToArray();
        Comparison = SnapshotComparer.Compare(ordered[0]!, ordered[1]!);
        return Comparison;
    }

    public async Task<bool> DeleteAsync(Guid id) { var deleted = await store.DeleteAsync(id); await LoadAsync(); return deleted; }
    public async Task<int> ClearAsync() { var count = await store.ClearAsync(); await LoadAsync(); return count; }
}

public sealed class SettingsViewModel(AppSessionViewModel session, ISnapshotStore store) : PageViewModel(session)
{
    public HistorySettings History { get; private set; } = HistorySettings.Default;
    public async Task LoadAsync() => History = await store.GetSettingsAsync();
    public async Task SaveHistoryAsync(bool enabled, int retention)
    { History = History with { IsEnabled = enabled, RetentionPerDrive = retention }; await store.SetSettingsAsync(History); }
    public Task<int> ClearHistoryAsync() => store.ClearAsync();
}

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using WinGuard.Localization;
using WinGuard.Models;
using WinGuard.Services;

namespace WinGuard.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly TweakEngine _engine = new();
    private readonly BackupService _backup;
    private string _searchText = "";
    private string _scopeFilter = "All";
    private CategoryViewModel? _selectedCategory;
    private bool _isBusy;
    private string _status = Loc.T("Ready");
    private bool _autoReapply;

    public ObservableCollection<SettingViewModel> Settings { get; } = new();
    public ObservableCollection<CategoryViewModel> Categories { get; } = new();
    public ObservableCollection<string> Log { get; } = new();
    public ICollectionView SettingsView { get; }

    public string Edition { get; } = SystemInfo.Edition();
    public bool IsAdmin { get; } = SafeIsAdmin();
    public string AdminText => Loc.T(IsAdmin ? "AdminYes" : "AdminNo");
    public Loc L => Loc.Instance;
    public string LanguageLabel => Loc.Instance.IsFrench ? "FR" : "EN";
    public ICommand ToggleLanguageCommand { get; }

    public ICommand ApplyRecommendedCommand { get; }
    public ICommand ApplyLimitedCommand { get; }
    public ICommand ApplyAllCommand { get; }
    public ICommand UndoAllCommand { get; }
    public ICommand RestorePointCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand RestoreBackupCommand { get; }
    public ICommand OpenBackupFolderCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ClearLogCommand { get; }
    public ICommand MaximumPrivacyCommand { get; }

    public MainViewModel()
    {
        _backup = new BackupService(_engine.Registry);
        List<Setting> models;
        try { models = SettingsRepository.Load(); }
        catch (Exception ex) { models = new(); AddLog(Loc.F("Log.LoadFailed", ex.Message)); }

        foreach (Setting m in models) Settings.Add(new SettingViewModel(m, OnToggleAsync));

        Categories.Add(new CategoryViewModel("All settings", Settings.Count));
        foreach (var g in models.GroupBy(m => m.Category))
            Categories.Add(new CategoryViewModel(g.Key, g.Count()));
        _selectedCategory = Categories[0];

        SettingsView = CollectionViewSource.GetDefaultView(Settings);
        SettingsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SettingViewModel.Category)));
        SettingsView.Filter = Filter;

        ApplyRecommendedCommand = new AsyncRelayCommand(_ => ApplyWhereAsync(r => r == Recommendation.Recommended, "Label.recommended"), () => !IsBusy);
        ApplyLimitedCommand = new AsyncRelayCommand(_ => ApplyWhereAsync(r => r != Recommendation.NotRecommended, "Label.limited"), () => !IsBusy);
        ApplyAllCommand = new AsyncRelayCommand(_ => ApplyWhereAsync(_ => true, "Label.all"), () => !IsBusy);
        UndoAllCommand = new AsyncRelayCommand(_ => UndoAllAsync(), () => !IsBusy);
        RestorePointCommand = new AsyncRelayCommand(_ => RunBusy(Loc.T("Status.CreatingRp"), CreateRestorePointAsync), () => !IsBusy);
        ExportCommand = new RelayCommand(_ => Export());
        ImportCommand = new AsyncRelayCommand(_ => ImportAsync(), () => !IsBusy);
        RestoreBackupCommand = new AsyncRelayCommand(_ => RestoreBackupAsync(), () => !IsBusy);
        OpenBackupFolderCommand = new RelayCommand(_ => OpenBackupFolder());
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync(), () => !IsBusy);
        ClearLogCommand = new RelayCommand(_ => Log.Clear());
        MaximumPrivacyCommand = new AsyncRelayCommand(_ => ApplyWhereAsync(_ => true, "Label.max"), () => !IsBusy);
        ToggleLanguageCommand = new RelayCommand(_ => Loc.Instance.Toggle());
        Loc.LanguageChanged += OnLanguageChanged;
        _ = InitAutoReapplyAsync();

        AddLog(Loc.F("Log.Loaded", Settings.Count, Edition));
        AppLog.Info("Log file: " + AppLog.FilePath);
        if (!IsAdmin) AddLog(Loc.T("Log.NotAdmin"));
        if (SafeIsHome()) AddLog(Loc.T("Log.Home"));
        _ = RefreshAsync();
    }

    private void OnLanguageChanged()
    {
        foreach (var s in Settings) s.RefreshLanguage();
        foreach (var c in Categories) c.RefreshLanguage();
        if (!IsBusy) Status = Loc.T("Ready");
        OnPropertyChanged(nameof(AdminText)); OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(SummaryText)); OnPropertyChanged(nameof(LanguageLabel));
        SettingsView.Refresh();
        AddLog(Loc.T("Log.LanguageChanged"));
    }

    private static bool SafeIsAdmin() { try { return SystemInfo.IsAdministrator(); } catch { return false; } }
    private static bool SafeIsHome() { try { return SystemInfo.IsHomeEdition(); } catch { return false; } }

    // ---------- properties ----------
    public string SearchText { get => _searchText; set { if (SetProperty(ref _searchText, value)) SettingsView.Refresh(); } }
    public string ScopeFilter { get => _scopeFilter; set { if (SetProperty(ref _scopeFilter, value)) SettingsView.Refresh(); } }
    public CategoryViewModel? SelectedCategory
    {
        get => _selectedCategory;
        set { if (SetProperty(ref _selectedCategory, value)) { SettingsView.Refresh(); OnPropertyChanged(nameof(PageTitle)); } }
    }
    public string PageTitle => SelectedCategory?.DisplayName ?? Strings.Category("All settings");
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            foreach (var s in Settings) s.IsLocked = value;
            CommandManager.InvalidateRequerySuggested();
        }
    }
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public bool AutoReapply
    {
        get => _autoReapply;
        set
        {
            if (!SetProperty(ref _autoReapply, value)) return;
            _ = Task.Run(async () =>
            {
                try
                {
                    if (value) SaveAutoProfile();
                    AddLog(await AutoReapplyService.SetEnabledAsync(value));
                }
                catch (Exception ex) { AppLog.Error("Auto-reapply toggle", ex); AddLog(Loc.F("Log.Error", ex.Message)); }
            });
        }
    }

    private async Task InitAutoReapplyAsync()
    {
        try { _autoReapply = await AutoReapplyService.IsEnabledAsync(); OnPropertyChanged(nameof(AutoReapply)); }
        catch (Exception ex) { AppLog.Warn("Auto-reapply state query failed: " + ex.Message); }
    }

    private void SaveAutoProfile()
    {
        try
        {
            var snapshot = OnUi(() => Settings.Select(s => new ProfileEntry { Id = s.Id, Applied = s.IsAppliedByUs }).ToList());
            AutoReapplyService.SaveProfile(snapshot);
        }
        catch (Exception ex) { AddLog(Loc.F("Log.ProfileSaveFailed", ex.Message)); }
    }

    /// <summary>Weighted privacy score: recommended settings count 3, limited 2, not-recommended 1.</summary>
    public int PrivacyScore
    {
        get
        {
            static int W(Recommendation r) => r switch { Recommendation.Recommended => 3, Recommendation.LimitedRecommended => 2, _ => 1 };
            int total = Settings.Sum(s => W(s.Recommendation));
            return total == 0 ? 0 : (int)Math.Round(100.0 * Settings.Where(s => s.IsApplied).Sum(s => W(s.Recommendation)) / total);
        }
    }

    /// <summary>Settings WinGuard applied (native "already optimal" ones excluded).</summary>
    public int AppliedCount => Settings.Count(s => s.IsAppliedByUs);
    public int NativeCount => Settings.Count(s => s.IsNative);
    public string SummaryText => NativeCount == 0 ? Loc.F("Summary", AppliedCount, Settings.Count - NativeCount)
        : Loc.F("SummaryNative", AppliedCount, Settings.Count - NativeCount, NativeCount);
    public double ProtectionPercent => PrivacyScore;

    private bool Filter(object o)
    {
        if (o is not SettingViewModel s) return false;
        if (SelectedCategory is { IsAll: false } c && s.Category != c.Name) return false;
        if (ScopeFilter != "All" && s.Scope != ScopeFilter) return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        string q = SearchText.Trim();
        return s.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
            || s.Description.Contains(q, StringComparison.OrdinalIgnoreCase)
            || s.Category.Contains(q, StringComparison.OrdinalIgnoreCase)
            || Strings.Category(s.Category).Contains(q, StringComparison.OrdinalIgnoreCase)
            || s.Model.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
            || s.DetailText.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- core ----------
    /// <summary>Thread-safe: adds to the on-screen activity log and the diagnostic file. Never throws.</summary>
    public void AddLog(string msg)
    {
        AppLog.Info(msg.Trim());
        string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
        void Add()
        {
            try { Log.Insert(0, line); if (Log.Count > 500) Log.RemoveAt(Log.Count - 1); }
            catch (Exception ex) { AppLog.Warn("Activity log update failed: " + ex.Message); }
        }
        try
        {
            if (Application.Current?.Dispatcher is { } d && !d.CheckAccess()) d.BeginInvoke(Add); else Add();
        }
        catch { }
    }

    private static T OnUi<T>(Func<T> f) =>
        Application.Current?.Dispatcher is { } d && !d.CheckAccess() ? d.Invoke(f) : f();

    private void UpdateCounters()
    {
        foreach (CategoryViewModel c in Categories)
            c.AppliedCount = c.IsAll ? AppliedCount : Settings.Count(s => s.Category == c.Name && s.IsAppliedByUs);
        OnPropertyChanged(nameof(AppliedCount));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(ProtectionPercent));
        OnPropertyChanged(nameof(PrivacyScore));
        if (_autoReapply && !IsBusy) SaveAutoProfile();
    }

    private async Task RunBusy(string status, Func<Task> work)
    {
        if (IsBusy) return; // never run two batches at once
        IsBusy = true; Status = status;
        try { await work(); }
        catch (Exception ex) { AppLog.Error("Operation failed: " + status, ex); AddLog(Loc.F("Log.Error", ex.Message)); }
        finally
        {
            IsBusy = false; Status = Loc.T("Ready");
            try { UpdateCounters(); } catch (Exception ex) { AppLog.Warn("Counter update failed: " + ex.Message); }
        }
    }

    /// <summary>Reads one setting's state; on any error logs it and reports "not applied" so the UI stays usable.</summary>
    private async Task<(bool, NativeKind)> SafeDetectAsync(SettingViewModel s)
    {
        try { return await _engine.DetectAsync(s.Model); }
        catch (Exception ex) { AddLog(Loc.F("Log.ReadFailed", s.Name, ex.Message)); return (false, NativeKind.None); }
    }
    private async Task Redetect(SettingViewModel s) { var (a, n) = await SafeDetectAsync(s); s.SetState(a, n); }

    public async Task RefreshAsync()
    {
        await RunBusy(Loc.T("Status.Reading"), async () =>
        {
            // Registry-only settings are fast; evaluate them first, then the process-based ones.
            foreach (SettingViewModel s in Settings.Where(s => !TweakEngine.NeedsProcess(s.Model)))
                await Redetect(s);
            UpdateCounters();
            var slow = Settings.Where(s => TweakEngine.NeedsProcess(s.Model)).ToList();
            // Bounded parallelism: dozens of simultaneous PowerShell processes can starve a slow PC.
            using var gate = new SemaphoreSlim(6);
            var results = await Task.WhenAll(slow.Select(s => Task.Run(async () =>
            {
                await gate.WaitAsync();
                try { return await _engine.DetectAsync(s.Model); }
                catch (Exception ex) { AppLog.Warn($"State read failed for {s.Id}: {ex.Message}"); return (false, NativeKind.None); }
                finally { gate.Release(); }
            })));
            for (int i = 0; i < slow.Count; i++) slow[i].SetState(results[i].Item1, results[i].Item2);
            AddLog(NativeCount == 0 ? Loc.F("Log.Refreshed", AppliedCount, Settings.Count)
                : Loc.F("Log.RefreshedNative", AppliedCount, Settings.Count - NativeCount, NativeCount));
        });
    }

    private async Task CreateRestorePointAsync()
    {
        AddLog(Loc.T("Log.CreatingRp"));
        var (ok, msg) = await RestorePointService.CreateAsync($"WinGuard {DateTime.Now:yyyy-MM-dd HH:mm}");
        AddLog(ok ? "✔ " + msg : "✖ " + msg);
    }

    private async Task ChangeAsync(IReadOnlyList<SettingViewModel> items, bool enable)
    {
        if (items.Count == 0) { AddLog(Loc.T("Log.Nothing")); return; }
        AddLog(Loc.T("Log.CreatingRp"));
        var (rpOk, rpMsg) = await RestorePointService.CreateAsync($"WinGuard {DateTime.Now:yyyy-MM-dd HH:mm}");
        AddLog(rpOk ? "✔ " + rpMsg : "✖ " + rpMsg);
        if (!rpOk)
        {
            AddLog(Loc.T("Log.RpAborted"));
            foreach (var s in items) await Redetect(s);
            return;
        }
        string file;
        try { file = await Task.Run(() => _backup.Create(items.Select(i => i.Model).ToList(), enable)); }
        catch (Exception ex)
        {
            // Without a backup we don't touch anything: safer than a change that can't be undone.
            AppLog.Error("Backup creation failed", ex);
            AddLog(Loc.F("Log.BackupFailed", ex.Message));
            foreach (var s in items) await Redetect(s);
            return;
        }
        AddLog(Loc.F("Log.BackupSaved", file));
        while (_engine.ManualReinstall.TryTake(out _)) { }
        int okCount = 0, warnCount = 0, errCount = 0, n = 0;
        var stuck = new List<string>();
        foreach (SettingViewModel s in items)
        {
            n++;
            s.IsBusy = true;
            Status = Loc.F(enable ? "Status.Applying" : "Status.Reverting", $"{s.Name} ({n}/{items.Count})");
            try
            {
                bool ok = await Task.Run(() => _engine.ApplyAsync(s.Model, enable, AddLog));
                if (ok) okCount++; else warnCount++;
                AddLog($"{(ok ? "✔" : "⚠")} {Loc.T(enable ? "Log.Applied" : "Log.Reverted")}: {s.Name}");
            }
            catch (Exception ex)
            {
                errCount++;
                AppLog.Error($"Setting {s.Id} failed", ex);
                AddLog(Loc.F("Log.SettingSkipped", s.Name, ex.Message));
            }
            finally
            {
                s.IsBusy = false;
                await Redetect(s);
                // Never let a switch silently snap back: say so when the real state didn't follow.
                if (s.IsAppliedByUs != enable && !(enable && s.IsNative)) stuck.Add(s.Name);
                UpdateCounters();
            }
        }
        if (items.Count > 1) AddLog(Loc.F("Log.BatchSummary", okCount, warnCount, errCount));
        if (stuck.Count > 0) AddLog(Loc.F(enable ? "Log.StuckOff" : "Log.StuckOn", string.Join(", ", stuck)));
        var manual = _engine.ManualReinstall.Distinct().ToList();
        if (!enable && manual.Count > 0)
        {
            AddLog(Loc.F("Log.ManualReinstall", string.Join(", ", manual)));
            OnUi(() =>
            {
                if (MessageBox.Show(Loc.F("Msg.ManualReinstall", string.Join(Environment.NewLine + "  • ", manual)), "WinGuard",
                        MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-windows-store://home") { UseShellExecute = true }); }
                    catch (Exception ex) { AddLog("✖ " + ex.Message); }
                return true;
            });
        }
    }

    private async Task OnToggleAsync(SettingViewModel s, bool enable)
    {
        if (IsBusy || s.IsNative) { s.SetState(!enable); return; }
        if (enable && s.Recommendation == Recommendation.NotRecommended &&
            MessageBox.Show(Loc.F("Msg.NotRecommended", s.Name),
                "WinGuard", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        { s.SetState(false); return; }
        await RunBusy(s.Name, () => ChangeAsync(new[] { s }, enable));
    }

    private async Task ApplyWhereAsync(Func<Recommendation, bool> predicate, string labelKey)
    {
        string label = Loc.T(labelKey);
        var items = Settings.Where(s => predicate(s.Recommendation) && !s.IsApplied).ToList();
        if (items.Count == 0) { AddLog(Loc.F("Log.AllApplied", label)); return; }
        if (MessageBox.Show(Loc.F("Msg.ApplyConfirm", items.Count, label),
                "WinGuard", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        await RunBusy(Loc.F("Status.ApplyingSet", label), () => ChangeAsync(items, true));
        AddLog(Loc.T("Log.NeedRestart"));
    }

    private async Task UndoAllAsync()
    {
        var items = Settings.Where(s => s.IsAppliedByUs).ToList();
        if (items.Count == 0) { AddLog(Loc.T("Log.NoneToUndo")); return; }
        if (MessageBox.Show(Loc.F("Msg.UndoConfirm", items.Count), "WinGuard",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        await RunBusy(Loc.T("Status.RestoringDefaults"), () => ChangeAsync(items, false));
    }

    private async Task RestoreBackupAsync()
    {
        var dlg = new OpenFileDialog { Filter = Loc.T("Filter.Backup"), InitialDirectory = BackupService.BackupFolder };
        if (dlg.ShowDialog() != true) return;
        await RunBusy(Loc.T("Status.RestoringBackup"), async () =>
        {
            BackupFile file;
            try { file = _backup.Load(dlg.FileName); }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            { AddLog("✖ " + ex.Message); return; }
            AddLog(Loc.T("Log.CreatingRp"));
            var (rpOk, rpMsg) = await RestorePointService.CreateAsync($"WinGuard restore backup {DateTime.Now:yyyy-MM-dd HH:mm}");
            AddLog(rpOk ? "✔ " + rpMsg : "✖ " + rpMsg);
            if (!rpOk) { AddLog(Loc.T("Log.RpAborted")); return; }
            int failed = await Task.Run(() => _backup.RestoreRegistry(file, AddLog));
            // Non-registry actions (tasks/apps) are reverted through the engine.
            foreach (var s in Settings.Where(s => file.AppliedSettingIds.Contains(s.Id) && TweakEngine.NeedsProcess(s.Model)).ToList())
            {
                try { if (!await Task.Run(() => _engine.ApplyAsync(s.Model, false, AddLog))) failed++; }
                catch (Exception ex) { failed++; AddLog(Loc.F("Log.SettingSkipped", s.Name, ex.Message)); }
            }
            TweakEngine.SyncSnippingExemption(AddLog);
            AddLog(Loc.F("Log.BackupRestored", file.Registry.Count, failed));
        });
        await RefreshAsync();
    }

    private void OpenBackupFolder()
    {
        try { System.Diagnostics.Process.Start("explorer.exe", BackupService.BackupFolder); }
        catch (Exception ex) { AddLog(Loc.F("Log.OpenFolderFailed", ex.Message)); }
    }

    private void Export()
    {
        var dlg = new SaveFileDialog { Filter = Loc.T("Filter.Profile"), FileName = "WinGuard-profile.json" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var profile = Settings.Select(s => new ProfileEntry { Id = s.Id, Applied = s.IsApplied }).ToList();
            AppLog.WriteAllTextAtomic(dlg.FileName, JsonSerializer.Serialize(profile, SettingsRepository.Json));
            AddLog(Loc.F("Log.Exported", dlg.FileName));
        }
        catch (Exception ex) { AppLog.Error("Export failed", ex); AddLog(Loc.F("Log.ExportFailed", ex.Message)); }
    }

    private async Task ImportAsync()
    {
        var dlg = new OpenFileDialog { Filter = Loc.T("Filter.Profile") };
        if (dlg.ShowDialog() != true) return;
        List<ProfileEntry>? profile;
        try { profile = JsonSerializer.Deserialize<List<ProfileEntry>>(File.ReadAllText(dlg.FileName), SettingsRepository.Json); }
        catch (Exception ex) { AddLog(Loc.F("Log.InvalidProfile", ex.Message)); return; }
        if (profile is null) { AddLog(Loc.F("Log.InvalidProfile", "empty")); return; }
        // Tolerate duplicates/nulls in hand-edited profiles (last entry wins).
        var map = new Dictionary<string, bool>();
        foreach (var p in profile.Where(p => p is not null && !string.IsNullOrEmpty(p.Id))) map[p.Id] = p.Applied;
        var toApply = Settings.Where(s => map.TryGetValue(s.Id, out bool a) && a && !s.IsApplied).ToList();
        var toRevert = Settings.Where(s => map.TryGetValue(s.Id, out bool a) && !a && s.IsAppliedByUs).ToList();
        if (MessageBox.Show(Loc.F("Msg.ImportConfirm", toApply.Count, toRevert.Count), "WinGuard",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        await RunBusy(Loc.T("Status.Importing"), async () =>
        {
            await ChangeAsync(toApply, true);
            await ChangeAsync(toRevert, false);
        });
    }
}

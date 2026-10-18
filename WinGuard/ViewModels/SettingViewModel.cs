using WinGuard.Localization;
using WinGuard.Models;
using WinGuard.Services;

namespace WinGuard.ViewModels;

public sealed class SettingViewModel : ObservableObject
{
    private readonly Func<SettingViewModel, bool, Task> _onToggle;
    private bool _isApplied;
    private bool _isBusy;
    private bool _isKnown;

    public SettingViewModel(Setting model, Func<SettingViewModel, bool, Task> onToggle)
    { Model = model; _onToggle = onToggle; }

    public Setting Model { get; }
    public string Id => Model.Id;
    public string Name => Loc.Pick(Model.Name, Model.NameFr);
    public string Description
    {
        get { string d = Loc.Pick(Model.Description, Model.DescriptionFr); return string.IsNullOrWhiteSpace(d) ? DetailText : d; }
    }
    public string InfoText
    {
        get { string d = Loc.Pick(Model.Info ?? "", Model.InfoFr); return string.IsNullOrWhiteSpace(d) ? DetailText : d; }
    }
    public string ScopeText => Strings.Scope(Model.Scope);
    public void RefreshLanguage() { OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(Description)); OnPropertyChanged(nameof(InfoText)); OnPropertyChanged(nameof(ScopeText)); OnPropertyChanged(nameof(RecommendationText)); OnPropertyChanged(nameof(StateText)); OnPropertyChanged(nameof(NativeBadge)); OnPropertyChanged(nameof(NativeTooltip)); }
    public string RecommendationText => Recommendation switch { Recommendation.Recommended => Loc.T("Recommended"), Recommendation.LimitedRecommended => Loc.T("Limited"), _ => Loc.T("NotRecommended") };
    public string StateText => Loc.T(_isApplied ? "On" : "Off");
    public string Category => Model.Category;
    public string Scope => Model.Scope;
    public Recommendation Recommendation => Model.Recommendation;
    public string DetailText => string.Join(Environment.NewLine, Model.Actions.Select(a => a.ToString()));

    /// <summary>Bound two-way to the toggle switch. User changes trigger apply/revert.</summary>
    public bool IsApplied
    {
        get => _isApplied;
        set
        {
            if (_isApplied == value || _isBusy) return;
            _isApplied = value;
            OnPropertyChanged(); OnPropertyChanged(nameof(StateText));
            _ = _onToggle(this, value);
        }
    }

    /// <summary>Updates state without triggering an apply.</summary>
    public void SetState(bool applied, NativeKind native = NativeKind.None)
    {
        _isApplied = applied; IsKnown = true;
        _native = applied ? native : NativeKind.None;
        OnPropertyChanged(nameof(IsApplied)); OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(Native)); OnPropertyChanged(nameof(IsNative)); OnPropertyChanged(nameof(NativeBadge));
        OnPropertyChanged(nameof(NativeTooltip)); OnPropertyChanged(nameof(IsEditable)); OnPropertyChanged(nameof(IsAppliedByUs));
    }

    private NativeKind _native;
    /// <summary>Already in the target state without WinGuard having changed it: shown ON but locked, and never counted or reverted.</summary>
    public NativeKind Native => _native;
    public bool IsNative => _native != NativeKind.None;
    public bool IsAppliedByUs => _isApplied && !IsNative;
    public string NativeBadge => _native switch
    {
        NativeKind.NotInstalled => Loc.T(Model.Actions.All(a => a.Type == ActionType.Appx) ? "Badge.NotInstalled" : "Badge.Absent"),
        NativeKind.AlreadyOff => Loc.T("Badge.AlreadyOff"),
        NativeKind.AlreadySet => Loc.T("Badge.AlreadySet"),
        _ => ""
    };
    public string NativeTooltip => _native switch
    {
        NativeKind.None => "",
        NativeKind.AlreadyOff => Loc.T("Badge.AlreadyOffTip"),
        NativeKind.AlreadySet => Loc.T("Badge.AlreadySetTip"),
        _ => Loc.T("Badge.NotInstalledTip")
    };

    public bool IsBusy { get => _isBusy; set { if (SetProperty(ref _isBusy, value)) OnPropertyChanged(nameof(IsEditable)); } }
    public bool IsKnown { get => _isKnown; set { if (SetProperty(ref _isKnown, value)) OnPropertyChanged(nameof(IsEditable)); } }
    private bool _isLocked;
    /// <summary>Set while a global batch runs, so individual switches can't be toggled mid-batch.</summary>
    public bool IsLocked { get => _isLocked; set { if (SetProperty(ref _isLocked, value)) OnPropertyChanged(nameof(IsEditable)); } }
    public bool IsEditable => !_isBusy && _isKnown && !_isLocked && !IsNative;
}

public sealed class CategoryViewModel : ObservableObject
{
    private static readonly Dictionary<string, string> Icons = new()
    {
        ["All settings"] = "\uE80F", ["Privacy"] = "\uE72E", ["Activity History & Clipboard"] = "\uE81C",
        ["App Privacy"] = "\uE71D", ["Windows Recall"] = "\uE7B3", ["Copilot & AI"] = "\uE945",
        ["Telemetry & Diagnostics"] = "\uE9D9", ["Services"] = "\uE912", ["Scheduled Tasks"] = "\uE823",
        ["Edge & Bing"] = "\uE774", ["Cortana & Search"] = "\uE721", ["Lock Screen & Widgets"] = "\uE72E",
        ["OneDrive"] = "\uE753", ["Network"] = "\uE701", ["Windows Update & Store"] = "\uE895",
        ["Secure Boot & Security"] = "\uEA18", ["Debloat Apps"] = "\uE74D", ["Firewall"] = "\uE83D"
    };
    private int _appliedCount;
    public CategoryViewModel(string name, int total) { Name = name; Total = total; }
    public string Name { get; }
    public string DisplayName => Strings.Category(Name);
    public void RefreshLanguage() => OnPropertyChanged(nameof(DisplayName));
    public int Total { get; }
    public string Icon => Icons.TryGetValue(Name, out var i) ? i : "\uE713";
    public int AppliedCount { get => _appliedCount; set { if (SetProperty(ref _appliedCount, value)) OnPropertyChanged(nameof(CountText)); } }
    public string CountText => $"{AppliedCount}/{Total}";
    public bool IsAll => Name == "All settings";
}

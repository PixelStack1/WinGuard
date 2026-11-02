using WinGuard.Localization;
using WinGuard.Models;
using WinGuard.Services;
using WinGuard.ViewModels;
using Xunit;

namespace WinGuard.Windows.Tests;

public class DetectionStateTests
{
    [Fact]
    public void Failed_detection_locks_setting_until_successful_refresh()
    {
        var setting = new SettingViewModel(new Setting(), (_, _) => Task.CompletedTask);
        setting.SetState(false, NativeKind.Unknown);
        Assert.False(setting.IsKnown);
        Assert.False(setting.IsEditable);
        Assert.False(setting.IsNative);
        Assert.False(setting.IsAppliedByUs);
        Assert.Equal(Loc.Pick("Unknown", "Inconnu"), setting.StateText);

        setting.SetState(false);
        Assert.True(setting.IsKnown);
        Assert.True(setting.IsEditable);
        Assert.Equal(Loc.T("Off"), setting.StateText);
    }

    [Fact]
    public void Native_setting_remains_distinct_from_setting_applied_by_WinGuard()
    {
        var setting = new SettingViewModel(new Setting(), (_, _) => Task.CompletedTask);
        setting.SetState(true, NativeKind.AlreadySet);
        Assert.True(setting.IsKnown);
        Assert.True(setting.IsNative);
        Assert.False(setting.IsAppliedByUs);
        Assert.False(setting.IsEditable);

        setting.SetState(true);
        Assert.True(setting.IsAppliedByUs);
        Assert.True(setting.IsEditable);
    }
}

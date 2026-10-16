using System.Text.Json.Serialization;

namespace WinGuard.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Recommendation { Recommended, LimitedRecommended, NotRecommended }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActionType { Registry, ScheduledTask, Appx, Hosts, Firewall }

/// <summary>One atomic change. "Enabled" = privacy tweak applied; "Disabled" = Windows default (null = value deleted).</summary>
public sealed class SettingAction
{
    public ActionType Type { get; set; } = ActionType.Registry;
    public string? Hive { get; set; }
    public string? Path { get; set; }
    public string? Name { get; set; }
    public string Kind { get; set; } = "DWord";
    public string? EnabledValue { get; set; }
    public string? DisabledValue { get; set; }
    /// <summary>Scheduled task full path or Appx package name.</summary>
    public string? Target { get; set; }
    /// <summary>Microsoft Store product id used to reinstall a removed app.</summary>
    public string? StoreId { get; set; }

    public override string ToString() => Type switch
    {
        ActionType.Registry => $@"{Hive}\{Path}\{Name} = {EnabledValue}",
        ActionType.ScheduledTask => $"Task {Target}",
        ActionType.Hosts => $"hosts: {Target?.Replace("|", ", ")}",
        ActionType.Firewall => $"Firewall outbound block: {Target}",
        _ => $"Appx {Target}"
    };
}

public sealed class Setting
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string? NameFr { get; set; }
    public string? DescriptionFr { get; set; }
    /// <summary>Detailed explanation (effect, exact changes, trade-offs, risk) shown by the row's info icon.</summary>
    public string? Info { get; set; }
    public string? InfoFr { get; set; }
    public string Category { get; set; } = "";
    public string Scope { get; set; } = "Local Machine";
    public Recommendation Recommendation { get; set; }
    public List<SettingAction> Actions { get; set; } = new();
}

public sealed class BackupEntry
{
    public string Hive { get; set; } = "";
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Existed { get; set; }
    public string Kind { get; set; } = "DWord";
    public string? Value { get; set; }
}

public sealed class BackupFile
{
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public string Machine { get; set; } = Environment.MachineName;
    public List<BackupEntry> Registry { get; set; } = new();
    /// <summary>Setting ids that were applied by this operation (for task/appx reverts).</summary>
    public List<string> AppliedSettingIds { get; set; } = new();
}

public sealed class ProfileEntry
{
    public string Id { get; set; } = "";
    public bool Applied { get; set; }
}

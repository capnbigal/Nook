using MudBlazor;
using Nook.Models;

namespace Nook.Services;

/// <summary>Presentation helpers mapping graph enums to icons, labels and colors.</summary>
public static class NodeUi
{
    public static string Icon(NodeKind kind) => kind switch
    {
        NodeKind.Unclassified => Icons.Material.Filled.Bolt,
        NodeKind.Note => Icons.Material.Filled.Notes,
        NodeKind.Journal => Icons.Material.Filled.Book,
        NodeKind.Observation => Icons.Material.Filled.Visibility,
        NodeKind.Idea => Icons.Material.Filled.Lightbulb,
        NodeKind.Reference => Icons.Material.Filled.MenuBook,
        NodeKind.Bookmark => Icons.Material.Filled.Bookmark,
        NodeKind.List => Icons.Material.Filled.List,
        NodeKind.Person => Icons.Material.Filled.Person,
        NodeKind.Project => Icons.Material.Filled.Folder,
        NodeKind.Place => Icons.Material.Filled.Place,
        NodeKind.Organization => Icons.Material.Filled.Business,
        NodeKind.Topic => Icons.Material.Filled.Tag,
        NodeKind.Resource => Icons.Material.Filled.Inventory2,
        NodeKind.Collection => Icons.Material.Filled.Collections,
        NodeKind.Event => Icons.Material.Filled.Event,
        _ => Icons.Material.Filled.Circle,
    };

    public static string StateLabel(NodeState state) => state.ToString();

    public static Color StateColor(NodeState state) => state switch
    {
        NodeState.Inbox => Color.Info,
        NodeState.Active => Color.Success,
        NodeState.Archived => Color.Default,
        _ => Color.Default,
    };

    public static Color ActionStatusColor(ActionStatus status) => status switch
    {
        ActionStatus.Open => Color.Primary,
        ActionStatus.InProgress => Color.Info,
        ActionStatus.Done => Color.Success,
        ActionStatus.Cancelled => Color.Default,
        _ => Color.Default,
    };

    public static string ActionIcon(ActionKind kind) => kind switch
    {
        ActionKind.Task => Icons.Material.Filled.CheckBox,
        ActionKind.Reminder => Icons.Material.Filled.Alarm,
        ActionKind.ChecklistItem => Icons.Material.Filled.CheckBoxOutlineBlank,
        _ => Icons.Material.Filled.CheckBox,
    };

    public static string PriorityColorHex(ActionPriority? p) => p switch
    {
        ActionPriority.Urgent => "#D32F2F",
        ActionPriority.High => "#F57C00",
        ActionPriority.Medium => "#1976D2",
        ActionPriority.Low => "#757575",
        _ => "#9E9E9E",
    };

    public static string Format(DateTime? value) =>
        value is null ? "—" : value.Value.ToLocalTime().ToString("g");

    /// <summary>Kinds offered when promoting/classifying a node, in a sensible order.</summary>
    public static readonly NodeKind[] AssignableKinds =
    {
        NodeKind.Unclassified, NodeKind.Note, NodeKind.Journal, NodeKind.Observation,
        NodeKind.Idea, NodeKind.Reference, NodeKind.Bookmark, NodeKind.List,
        NodeKind.Person, NodeKind.Project, NodeKind.Place, NodeKind.Organization,
        NodeKind.Topic, NodeKind.Resource,
    };

    /// <summary>Per-kind accent color (hex) from the design spec's 16-kind map.</summary>
    public static string KindAccent(NodeKind kind) => kind switch
    {
        NodeKind.Unclassified => "#8C8578",
        NodeKind.Note         => "#4C7DF0",
        NodeKind.Journal      => "#E0863C",
        NodeKind.Observation  => "#14B8A6",
        NodeKind.Idea         => "#F5B417",
        NodeKind.Reference    => "#5878A6",
        NodeKind.Bookmark     => "#F2416A",
        NodeKind.List         => "#23A968",
        NodeKind.Person       => "#F76F5A",
        NodeKind.Project      => "#7C5CFF",
        NodeKind.Place        => "#A15C34",
        NodeKind.Organization => "#A24C8C",
        NodeKind.Topic        => "#17B0C4",
        NodeKind.Resource     => "#7E9C24",
        NodeKind.Collection   => "#B78430",
        NodeKind.Event        => "#EE5D9A",
        _ => "#8C8578",
    };

    /// <summary>The CSS custom-property name backing a kind's accent (e.g. --kind-note).</summary>
    public static string KindAccentVar(NodeKind kind) =>
        "--kind-" + kind.ToString().ToLowerInvariant();
}

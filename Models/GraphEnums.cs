namespace Nook.Models;

/// <summary>
/// Lightweight classification of a <see cref="Node"/>. Used only for display,
/// filtering, and choosing an optional specialised profile — never as a source
/// of behaviour. Stored as a string in the database.
/// </summary>
public enum NodeKind
{
    /// <summary>The quick-capture default: a valid, first-class node with no assigned kind.</summary>
    Unclassified,

    // ---- Record kinds (authored content) ----
    Note,
    Journal,
    Observation,
    Idea,
    Reference,
    Bookmark,
    List,

    // ---- Entity kinds (canonical real-world things) ----
    Person,
    Project,
    Place,
    Organization,
    Topic,
    Resource,

    // ---- Structural kinds (node-backed profiles) ----
    Collection,
    Event
}

/// <summary>
/// The explicit lifecycle of a <see cref="Node"/>. Stored as a string.
/// Inbox = captured but not yet intentionally organised (still fully valid and
/// searchable). Active = intentionally kept. Archived = removed from default
/// daily views without deletion.
/// </summary>
public enum NodeState
{
    Inbox,
    Active,
    Archived
}

/// <summary>Grouping for relation types, used to organise the connections UI.</summary>
public enum RelationCategory
{
    General,
    Structure,
    Reference,
    People,
    Ownership,
    Reasoning,
    Planning,
    Place
}

/// <summary>The kind of an <see cref="ActionItem"/>. Stored as a string.</summary>
public enum ActionKind
{
    Task,
    Reminder,
    ChecklistItem
}

/// <summary>Workflow status of an <see cref="ActionItem"/>. Stored as a string.</summary>
public enum ActionStatus
{
    Open,
    InProgress,
    Done,
    Cancelled
}

/// <summary>Relative importance of an <see cref="ActionItem"/>. Stored as a string.</summary>
public enum ActionPriority
{
    Low,
    Medium,
    High,
    Urgent
}

/// <summary>
/// An optional verb describing the intent of an <see cref="ActionItem"/>
/// (e.g. Read this, Watch this). Stored as a string.
/// </summary>
public enum ActionVerb
{
    None,
    Read,
    Watch,
    Listen,
    Tell,
    Buy,
    Research,
    FollowUp,
    Call,
    Visit
}

/// <summary>
/// The role a <see cref="Node"/> plays in the context of an <see cref="ActionItem"/>.
/// Stored as a string. Enables rollups such as "all open actions related to Jamie".
/// </summary>
public enum ActionContextRole
{
    Target,
    Source,
    Person,
    Project,
    Place,
    Queue,
    Context,
    Related
}

/// <summary>The kind of a <see cref="Collection"/>. Stored as a string.</summary>
public enum CollectionKind
{
    Folder,
    List,
    Queue,
    Plain
}

/// <summary>The role a participant <see cref="Node"/> plays in an event. Stored as a string.</summary>
public enum EventParticipantRole
{
    Participant,
    Introducer,
    Introduced,
    Mentioned
}

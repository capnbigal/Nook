namespace Nook.Models;

/// <summary>The kind of captured item. Stored as a string in the database.</summary>
public enum ItemType
{
    Note,
    Reminder,
    Bookmark,
    Thought,
    List,
    Todo,
    Idea,
    Reference
}

/// <summary>Workflow status of an item. Stored as a string in the database.</summary>
public enum ItemStatus
{
    Open,
    InProgress,
    Done,
    Cancelled
}

/// <summary>Relative importance of an item. Stored as a string in the database.</summary>
public enum Priority
{
    Low,
    Medium,
    High,
    Urgent
}

using Nook.Models;

namespace Nook.Services;

/// <summary>Criteria for the global Actions list.</summary>
public class ActionFilter
{
    public ActionStatus? Status { get; set; }
    public bool IncludeCompleted { get; set; }
    public ActionKind? Kind { get; set; }
    public ActionPriority? Priority { get; set; }
    public bool DueOnly { get; set; }
    public bool OverdueOnly { get; set; }
    public bool RemindersOnly { get; set; }
    public int? TargetNodeId { get; set; }
    public string? SearchText { get; set; }
    /// <summary>Exclude checklist items (children) from top-level lists.</summary>
    public bool ExcludeChecklistItems { get; set; } = true;
}

/// <summary>A context association to attach to an action.</summary>
public readonly record struct ActionContextInput(int NodeId, ActionContextRole Role);

/// <summary>Application service for actions (tasks, reminders, checklist items) and their contexts.</summary>
public interface IActionService
{
    Task<ActionItem?> GetByIdAsync(int id);
    Task<List<ActionItem>> QueryAsync(ActionFilter filter);
    Task<List<ActionItem>> GetForNodeAsync(int nodeId, bool includeCompleted = true);
    Task<int> CountOpenForNodeAsync(int nodeId);
    Task<List<ActionItem>> GetChildrenAsync(int parentActionId);

    Task<ActionItem> CreateAsync(ActionItem action, IEnumerable<ActionContextInput>? contexts = null);
    Task UpdateAsync(ActionItem action);
    Task CompleteAsync(int id);
    Task ReopenAsync(int id);
    Task CancelAsync(int id);
    Task RescheduleAsync(int id, DateTime? dueDate, DateTime? remindAt);
    Task DeleteAsync(int id);

    Task<ActionItem> AddChecklistItemAsync(int parentActionId, string title);

    Task<bool> AddContextAsync(int actionId, int nodeId, ActionContextRole role);
    Task RemoveContextAsync(int actionId, int nodeId, ActionContextRole role);
    Task<List<ActionContext>> GetContextsAsync(int actionId);

    // ---- Today / planning ----
    Task<List<ActionItem>> GetOpenDueAsync(DateTime before, int count = 50);
    Task<List<ActionItem>> GetOverdueAsync(int count = 50);
    Task<List<ActionItem>> GetUpcomingRemindersAsync(int count = 50);
    Task<List<ActionItem>> GetOverdueRemindersAsync(int count = 50);
}

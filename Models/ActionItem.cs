using System.ComponentModel.DataAnnotations;

namespace Nook.Models;

/// <summary>
/// A unit of work or follow-up: a task, reminder or checklist item. Named
/// <c>ActionItem</c> to avoid ambiguity with <see cref="System.Action"/>; the
/// product term is "Action". An action is a dated instance separate from the
/// reusable knowledge in a <see cref="Node"/>: completing an action never marks
/// its target node done or archived, and one node may spawn many actions over time.
/// </summary>
public class ActionItem
{
    public int ActionItemId { get; set; }

    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public ActionKind Kind { get; set; } = ActionKind.Task;

    public ActionStatus Status { get; set; } = ActionStatus.Open;

    public ActionPriority? Priority { get; set; }

    [Required]
    [MaxLength(300)]
    public string Title { get; set; } = string.Empty;

    /// <summary>Optional intent verb (Read, Watch, Buy, …).</summary>
    public ActionVerb Verb { get; set; } = ActionVerb.None;

    public DateTime? DueDate { get; set; }
    public DateTime? RemindAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>Optional primary target node this action is about.</summary>
    public int? TargetNodeId { get; set; }
    public Node? TargetNode { get; set; }

    /// <summary>Optional parent action; groups checklist items under a header.</summary>
    public int? ParentActionId { get; set; }
    public ActionItem? ParentAction { get; set; }
    public ICollection<ActionItem> Children { get; set; } = new List<ActionItem>();

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<ActionContext> Contexts { get; set; } = new List<ActionContext>();
}

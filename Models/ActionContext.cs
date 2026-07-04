namespace Nook.Models;

/// <summary>
/// Associates an <see cref="ActionItem"/> with a node in a particular role,
/// beyond the single primary target. Enables rollups such as "all open actions
/// related to Jamie / Project X / a queue / created from this note".
/// </summary>
public class ActionContext
{
    public int ActionItemId { get; set; }
    public ActionItem? ActionItem { get; set; }

    public int NodeId { get; set; }
    public Node? Node { get; set; }

    public ActionContextRole Role { get; set; }

    /// <summary>Owner. Denormalised for scoping.</summary>
    public string UserId { get; set; } = string.Empty;
}

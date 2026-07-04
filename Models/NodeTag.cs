namespace Nook.Models;

/// <summary>Join entity for the many-to-many relationship between nodes and tags.</summary>
public class NodeTag
{
    public int NodeId { get; set; }
    public Node Node { get; set; } = null!;

    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}

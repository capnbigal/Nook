using System.ComponentModel.DataAnnotations;

namespace Nook.Models;

/// <summary>
/// A required 1:1 profile of a <see cref="Node"/> whose <see cref="Node.Kind"/>
/// is Event. The node's Title holds the free-text summary and its Body holds
/// optional notes; this profile records the structured who/what/when/where.
/// A valid event needs only a title (on the node) and an <see cref="OccurredAt"/>.
/// </summary>
public class EventDetails
{
    /// <summary>PK and FK to the backing node (1:1).</summary>
    public int NodeId { get; set; }
    public Node? Node { get; set; }

    /// <summary>When the event happened. Required.</summary>
    public DateTime OccurredAt { get; set; }

    public int? VerbId { get; set; }
    public Verb? Verb { get; set; }

    /// <summary>The actor. Null defaults conceptually to the user's self Person.</summary>
    public int? SubjectNodeId { get; set; }
    public Node? SubjectNode { get; set; }

    /// <summary>The primary object/target of the event.</summary>
    public int? ObjectNodeId { get; set; }
    public Node? ObjectNode { get; set; }

    /// <summary>Where it happened.</summary>
    public int? PlaceNodeId { get; set; }
    public Node? PlaceNode { get; set; }

    public ICollection<EventParticipant> Participants { get; set; } = new List<EventParticipant>();
}

namespace Nook.Models;

/// <summary>
/// A node participating in an event in a particular role (e.g. "Jamie introduced
/// me to Alex" → Jamie as Introducer, Alex as Introduced).
/// </summary>
public class EventParticipant
{
    /// <summary>The NodeId of the event-backing node.</summary>
    public int EventNodeId { get; set; }
    public EventDetails? Event { get; set; }

    public int ParticipantNodeId { get; set; }
    public Node? ParticipantNode { get; set; }

    public EventParticipantRole Role { get; set; } = EventParticipantRole.Participant;

    /// <summary>Owner. Denormalised for scoping.</summary>
    public string UserId { get; set; } = string.Empty;
}

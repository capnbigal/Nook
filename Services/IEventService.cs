using Nook.Models;

namespace Nook.Services;

/// <summary>A participant to attach to an event.</summary>
public readonly record struct EventParticipantInput(int NodeId, EventParticipantRole Role);

/// <summary>Application service for node-backed events (the experience log).</summary>
public interface IEventService
{
    Task<List<Verb>> GetVerbsAsync();

    /// <summary>A valid event needs only a title and an occurrence time; all else is optional.</summary>
    Task<Node> CreateAsync(
        string title, DateTime occurredAt, string? body = null,
        int? verbId = null, int? subjectNodeId = null, int? objectNodeId = null, int? placeNodeId = null,
        IEnumerable<EventParticipantInput>? participants = null);

    Task UpdateAsync(int eventNodeId, string title, DateTime occurredAt, string? body,
        int? verbId, int? subjectNodeId, int? objectNodeId, int? placeNodeId);

    Task<EventDetails?> GetByNodeIdAsync(int eventNodeId);
    Task<List<Node>> GetTimelineAsync(int count = 100);
    Task<List<Node>> GetEventsForNodeAsync(int nodeId);

    Task<bool> AddParticipantAsync(int eventNodeId, int participantNodeId, EventParticipantRole role);
    Task RemoveParticipantAsync(int eventNodeId, int participantNodeId, EventParticipantRole role);
    Task<List<EventParticipant>> GetParticipantsAsync(int eventNodeId);

    /// <summary>Lazily creates (or returns) the user's optional "self" Person node.</summary>
    Task<Node> GetOrCreateSelfPersonAsync();
}

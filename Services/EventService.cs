using Microsoft.EntityFrameworkCore;
using Nook.Data;
using Nook.Models;

namespace Nook.Services;

public sealed class EventService : IEventService
{
    private readonly IDbContextFactory<NookContext> _factory;
    private readonly ICurrentUser _currentUser;
    private readonly IActivityService _activity;

    public EventService(IDbContextFactory<NookContext> factory, ICurrentUser currentUser, IActivityService activity)
    {
        _factory = factory;
        _currentUser = currentUser;
        _activity = activity;
    }

    public async Task<List<Verb>> GetVerbsAsync()
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Verbs.Where(v => v.UserId == null || v.UserId == userId)
            .OrderBy(v => v.Name).ToListAsync();
    }

    public async Task<Node> CreateAsync(
        string title, DateTime occurredAt, string? body = null,
        int? verbId = null, int? subjectNodeId = null, int? objectNodeId = null, int? placeNodeId = null,
        IEnumerable<EventParticipantInput>? participants = null)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();

        subjectNodeId = await NullIfNotOwned(db, userId, subjectNodeId);
        objectNodeId = await NullIfNotOwned(db, userId, objectNodeId);
        placeNodeId = await NullIfNotOwned(db, userId, placeNodeId);
        if (verbId is int v && !await db.Verbs.AnyAsync(x => x.VerbId == v && (x.UserId == null || x.UserId == userId)))
            verbId = null;

        var node = new Node
        {
            UserId = userId,
            Kind = NodeKind.Event,
            State = NodeState.Active,
            Title = title.Trim(),
            Body = string.IsNullOrWhiteSpace(body) ? null : body,
            EventDetails = new EventDetails
            {
                OccurredAt = occurredAt,
                VerbId = verbId,
                SubjectNodeId = subjectNodeId,
                ObjectNodeId = objectNodeId,
                PlaceNodeId = placeNodeId,
            },
        };
        db.Nodes.Add(node);
        await db.SaveChangesAsync();

        if (participants is not null)
            foreach (var p in participants.DistinctBy(p => (p.NodeId, p.Role)))
                if (await OwnsNode(db, userId, p.NodeId))
                    db.EventParticipants.Add(new EventParticipant
                    {
                        EventNodeId = node.NodeId, ParticipantNodeId = p.NodeId, Role = p.Role, UserId = userId
                    });
        await db.SaveChangesAsync();

        await _activity.LogNodeAsync(userId, ActivityType.Created, node.NodeId, node.Title, "event");
        return node;
    }

    public async Task UpdateAsync(int eventNodeId, string title, DateTime occurredAt, string? body,
        int? verbId, int? subjectNodeId, int? objectNodeId, int? placeNodeId)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        var node = await db.Nodes.Include(n => n.EventDetails)
            .FirstOrDefaultAsync(n => n.NodeId == eventNodeId && n.UserId == userId && n.Kind == NodeKind.Event);
        if (node?.EventDetails is null) return;
        node.Title = title.Trim();
        node.Body = string.IsNullOrWhiteSpace(body) ? null : body;
        node.EventDetails.OccurredAt = occurredAt;
        node.EventDetails.VerbId = await ValidVerb(db, userId, verbId);
        node.EventDetails.SubjectNodeId = await NullIfNotOwned(db, userId, subjectNodeId);
        node.EventDetails.ObjectNodeId = await NullIfNotOwned(db, userId, objectNodeId);
        node.EventDetails.PlaceNodeId = await NullIfNotOwned(db, userId, placeNodeId);
        await db.SaveChangesAsync();
        await _activity.LogNodeAsync(userId, ActivityType.Updated, node.NodeId, node.Title);
    }

    public async Task<EventDetails?> GetByNodeIdAsync(int eventNodeId)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        return await db.EventDetails
            .Include(e => e.Node)
            .Include(e => e.Verb)
            .Include(e => e.SubjectNode)
            .Include(e => e.ObjectNode)
            .Include(e => e.PlaceNode)
            .Include(e => e.Participants).ThenInclude(p => p.ParticipantNode)
            .FirstOrDefaultAsync(e => e.NodeId == eventNodeId && e.Node!.UserId == userId);
    }

    public async Task<List<Node>> GetTimelineAsync(int count = 100)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        return await db.EventDetails
            .Where(e => e.Node!.UserId == userId && e.Node.State != NodeState.Archived)
            .OrderByDescending(e => e.OccurredAt)
            .Include(e => e.Node)
            .Include(e => e.Verb)
            .Take(count)
            .Select(e => e.Node!)
            .ToListAsync();
    }

    public async Task<List<Node>> GetEventsForNodeAsync(int nodeId)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        var partEventIds = db.EventParticipants
            .Where(p => p.UserId == userId && p.ParticipantNodeId == nodeId)
            .Select(p => p.EventNodeId);
        return await db.EventDetails
            .Where(e => e.Node!.UserId == userId &&
                (e.SubjectNodeId == nodeId || e.ObjectNodeId == nodeId || e.PlaceNodeId == nodeId
                 || partEventIds.Contains(e.NodeId)))
            .OrderByDescending(e => e.OccurredAt)
            .Include(e => e.Node).Include(e => e.Verb)
            .Select(e => e.Node!)
            .ToListAsync();
    }

    public async Task<bool> AddParticipantAsync(int eventNodeId, int participantNodeId, EventParticipantRole role)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        bool eventOk = await db.EventDetails.AnyAsync(e => e.NodeId == eventNodeId && e.Node!.UserId == userId);
        if (!eventOk || !await OwnsNode(db, userId, participantNodeId)) return false;
        bool exists = await db.EventParticipants.AnyAsync(p =>
            p.EventNodeId == eventNodeId && p.ParticipantNodeId == participantNodeId && p.Role == role);
        if (exists) return false;
        db.EventParticipants.Add(new EventParticipant
        {
            EventNodeId = eventNodeId, ParticipantNodeId = participantNodeId, Role = role, UserId = userId
        });
        await db.SaveChangesAsync();
        return true;
    }

    public async Task RemoveParticipantAsync(int eventNodeId, int participantNodeId, EventParticipantRole role)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        var p = await db.EventParticipants.FirstOrDefaultAsync(x =>
            x.EventNodeId == eventNodeId && x.ParticipantNodeId == participantNodeId
            && x.Role == role && x.UserId == userId);
        if (p is null) return;
        db.EventParticipants.Remove(p);
        await db.SaveChangesAsync();
    }

    public async Task<List<EventParticipant>> GetParticipantsAsync(int eventNodeId)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        return await db.EventParticipants
            .Where(p => p.EventNodeId == eventNodeId && p.UserId == userId)
            .Include(p => p.ParticipantNode)
            .ToListAsync();
    }

    public async Task<Node> GetOrCreateSelfPersonAsync()
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        var user = await db.Users.FirstAsync(u => u.Id == userId);
        if (user.SelfNodeId is int existingId)
        {
            var existing = await db.Nodes.FirstOrDefaultAsync(n => n.NodeId == existingId && n.UserId == userId);
            if (existing is not null) return existing;
        }
        var self = new Node
        {
            UserId = userId,
            Kind = NodeKind.Person,
            State = NodeState.Active,
            Title = string.IsNullOrWhiteSpace(user.UserName) ? "Me" : user.UserName!,
        };
        db.Nodes.Add(self);
        await db.SaveChangesAsync();
        user.SelfNodeId = self.NodeId;
        await db.SaveChangesAsync();
        return self;
    }

    private static Task<bool> OwnsNode(NookContext db, string userId, int nodeId) =>
        db.Nodes.AnyAsync(n => n.NodeId == nodeId && n.UserId == userId);

    private static async Task<int?> NullIfNotOwned(NookContext db, string userId, int? nodeId) =>
        nodeId is int id && await OwnsNode(db, userId, id) ? id : null;

    private static async Task<int?> ValidVerb(NookContext db, string userId, int? verbId) =>
        verbId is int v && await db.Verbs.AnyAsync(x => x.VerbId == v && (x.UserId == null || x.UserId == userId))
            ? verbId : null;
}

using Microsoft.EntityFrameworkCore;
using Nook.Data;
using Nook.Models;

namespace Nook.Services;

public sealed class ActionService : IActionService
{
    private readonly IDbContextFactory<NookContext> _factory;
    private readonly ICurrentUser _currentUser;
    private readonly IActivityService _activity;

    public ActionService(IDbContextFactory<NookContext> factory, ICurrentUser currentUser, IActivityService activity)
    {
        _factory = factory;
        _currentUser = currentUser;
        _activity = activity;
    }

    private static IQueryable<ActionItem> WithTarget(IQueryable<ActionItem> q) =>
        q.Include(a => a.TargetNode);

    public async Task<ActionItem?> GetByIdAsync(int id)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        return await WithTarget(db.ActionItems)
            .Include(a => a.Contexts).ThenInclude(c => c.Node)
            .FirstOrDefaultAsync(a => a.ActionItemId == id && a.UserId == userId);
    }

    public async Task<List<ActionItem>> QueryAsync(ActionFilter filter)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        var q = WithTarget(db.ActionItems).Where(a => a.UserId == userId);

        if (filter.ExcludeChecklistItems) q = q.Where(a => a.ParentActionId == null);
        if (filter.Status.HasValue) q = q.Where(a => a.Status == filter.Status.Value);
        else if (!filter.IncludeCompleted)
            q = q.Where(a => a.Status != ActionStatus.Done && a.Status != ActionStatus.Cancelled);
        if (filter.Kind.HasValue) q = q.Where(a => a.Kind == filter.Kind.Value);
        if (filter.Priority.HasValue) q = q.Where(a => a.Priority == filter.Priority.Value);
        if (filter.TargetNodeId.HasValue) q = q.Where(a => a.TargetNodeId == filter.TargetNodeId.Value);
        if (filter.RemindersOnly) q = q.Where(a => a.RemindAt != null);
        if (filter.DueOnly) q = q.Where(a => a.DueDate != null);

        var now = DateTime.UtcNow;
        if (filter.OverdueOnly)
            q = q.Where(a => a.DueDate != null && a.DueDate < now
                             && a.Status != ActionStatus.Done && a.Status != ActionStatus.Cancelled);

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var s = filter.SearchText.Trim();
            q = q.Where(a => a.Title.Contains(s));
        }

        return await q
            .OrderBy(a => a.Status == ActionStatus.Done || a.Status == ActionStatus.Cancelled)
            .ThenBy(a => a.DueDate == null)
            .ThenBy(a => a.DueDate)
            .ThenByDescending(a => a.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<ActionItem>> GetForNodeAsync(int nodeId, bool includeCompleted = true)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        var contextActionIds = db.ActionContexts
            .Where(c => c.UserId == userId && c.NodeId == nodeId)
            .Select(c => c.ActionItemId);

        var q = WithTarget(db.ActionItems)
            .Where(a => a.UserId == userId
                        && (a.TargetNodeId == nodeId || contextActionIds.Contains(a.ActionItemId)));
        if (!includeCompleted)
            q = q.Where(a => a.Status != ActionStatus.Done && a.Status != ActionStatus.Cancelled);

        return await q
            .OrderBy(a => a.Status == ActionStatus.Done || a.Status == ActionStatus.Cancelled)
            .ThenBy(a => a.ParentActionId ?? a.ActionItemId)
            .ThenBy(a => a.SortOrder)
            .ThenBy(a => a.DueDate)
            .ToListAsync();
    }

    public async Task<int> CountOpenForNodeAsync(int nodeId)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        var contextActionIds = db.ActionContexts
            .Where(c => c.UserId == userId && c.NodeId == nodeId).Select(c => c.ActionItemId);
        return await db.ActionItems.CountAsync(a => a.UserId == userId
            && (a.TargetNodeId == nodeId || contextActionIds.Contains(a.ActionItemId))
            && a.Status != ActionStatus.Done && a.Status != ActionStatus.Cancelled);
    }

    public async Task<List<ActionItem>> GetChildrenAsync(int parentActionId)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        return await db.ActionItems
            .Where(a => a.UserId == userId && a.ParentActionId == parentActionId)
            .OrderBy(a => a.SortOrder).ThenBy(a => a.CreatedAt)
            .ToListAsync();
    }

    public async Task<ActionItem> CreateAsync(ActionItem action, IEnumerable<ActionContextInput>? contexts = null)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        action.UserId = userId;

        // Guard: an action cannot reference another user's node.
        if (action.TargetNodeId is int target && !await OwnsNodeAsync(db, userId, target))
            action.TargetNodeId = null;

        db.ActionItems.Add(action);
        await db.SaveChangesAsync();

        if (contexts is not null)
            foreach (var c in contexts.DistinctBy(c => (c.NodeId, c.Role)))
                if (await OwnsNodeAsync(db, userId, c.NodeId))
                    db.ActionContexts.Add(new ActionContext
                    {
                        ActionItemId = action.ActionItemId, NodeId = c.NodeId, Role = c.Role, UserId = userId
                    });

        // A primary target implies a Target context for uniform rollups.
        if (action.TargetNodeId is int t &&
            !await db.ActionContexts.AnyAsync(c => c.ActionItemId == action.ActionItemId
                && c.NodeId == t && c.Role == ActionContextRole.Target))
            db.ActionContexts.Add(new ActionContext
            {
                ActionItemId = action.ActionItemId, NodeId = t, Role = ActionContextRole.Target, UserId = userId
            });

        await db.SaveChangesAsync();
        await _activity.LogNodeAsync(userId, ActivityType.Created, action.TargetNodeId, action.Title,
            $"action ({action.Kind})");
        return action;
    }

    public async Task UpdateAsync(ActionItem action)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        var existing = await db.ActionItems
            .FirstOrDefaultAsync(a => a.ActionItemId == action.ActionItemId && a.UserId == userId);
        if (existing is null) return;
        existing.Title = action.Title;
        existing.Kind = action.Kind;
        existing.Status = action.Status;
        existing.Priority = action.Priority;
        existing.Verb = action.Verb;
        existing.DueDate = action.DueDate;
        existing.RemindAt = action.RemindAt;
        if (existing.Status == ActionStatus.Done && existing.CompletedAt is null)
            existing.CompletedAt = DateTime.UtcNow;
        if (existing.Status != ActionStatus.Done) existing.CompletedAt = null;
        await db.SaveChangesAsync();
    }

    public Task CompleteAsync(int id) => SetStatusAsync(id, ActionStatus.Done, ActivityType.Completed);
    public Task ReopenAsync(int id) => SetStatusAsync(id, ActionStatus.Open, ActivityType.Reopened);
    public Task CancelAsync(int id) => SetStatusAsync(id, ActionStatus.Cancelled, ActivityType.Updated);

    private async Task SetStatusAsync(int id, ActionStatus status, ActivityType audit)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        var a = await db.ActionItems.FirstOrDefaultAsync(x => x.ActionItemId == id && x.UserId == userId);
        if (a is null) return;
        a.Status = status;
        a.CompletedAt = status == ActionStatus.Done ? DateTime.UtcNow : null;
        await db.SaveChangesAsync();
        // Completing an action never changes its target node — only the action.
        await _activity.LogNodeAsync(userId, audit, a.TargetNodeId, a.Title, $"action {status}");
    }

    public async Task RescheduleAsync(int id, DateTime? dueDate, DateTime? remindAt)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        var a = await db.ActionItems.FirstOrDefaultAsync(x => x.ActionItemId == id && x.UserId == userId);
        if (a is null) return;
        a.DueDate = dueDate;
        a.RemindAt = remindAt;
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        var a = await db.ActionItems.Include(x => x.Children)
            .FirstOrDefaultAsync(x => x.ActionItemId == id && x.UserId == userId);
        if (a is null) return;
        // Reparent nothing — checklist children are removed with their parent.
        db.ActionItems.RemoveRange(a.Children);
        db.ActionItems.Remove(a);
        await db.SaveChangesAsync();
    }

    public async Task<ActionItem> AddChecklistItemAsync(int parentActionId, string title)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        var parent = await db.ActionItems
            .FirstOrDefaultAsync(a => a.ActionItemId == parentActionId && a.UserId == userId);
        if (parent is null) throw new InvalidOperationException("Parent action not found.");
        int nextOrder = (await db.ActionItems
            .Where(a => a.ParentActionId == parentActionId)
            .Select(a => (int?)a.SortOrder).MaxAsync() ?? -1) + 1;
        var child = new ActionItem
        {
            UserId = userId,
            Kind = ActionKind.ChecklistItem,
            Status = ActionStatus.Open,
            Title = title.Trim(),
            ParentActionId = parentActionId,
            TargetNodeId = parent.TargetNodeId,
            SortOrder = nextOrder,
        };
        db.ActionItems.Add(child);
        await db.SaveChangesAsync();
        return child;
    }

    public async Task<bool> AddContextAsync(int actionId, int nodeId, ActionContextRole role)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        bool actionOk = await db.ActionItems.AnyAsync(a => a.ActionItemId == actionId && a.UserId == userId);
        if (!actionOk || !await OwnsNodeAsync(db, userId, nodeId)) return false;
        bool exists = await db.ActionContexts.AnyAsync(c =>
            c.ActionItemId == actionId && c.NodeId == nodeId && c.Role == role);
        if (exists) return false;
        db.ActionContexts.Add(new ActionContext
        {
            ActionItemId = actionId, NodeId = nodeId, Role = role, UserId = userId
        });
        await db.SaveChangesAsync();
        return true;
    }

    public async Task RemoveContextAsync(int actionId, int nodeId, ActionContextRole role)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        var c = await db.ActionContexts.FirstOrDefaultAsync(x =>
            x.ActionItemId == actionId && x.NodeId == nodeId && x.Role == role && x.UserId == userId);
        if (c is null) return;
        db.ActionContexts.Remove(c);
        await db.SaveChangesAsync();
    }

    public async Task<List<ActionContext>> GetContextsAsync(int actionId)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        return await db.ActionContexts
            .Where(c => c.ActionItemId == actionId && c.UserId == userId)
            .Include(c => c.Node)
            .ToListAsync();
    }

    // ---- Today / planning ----

    private async Task<List<ActionItem>> PlanningAsync(Func<IQueryable<ActionItem>, IQueryable<ActionItem>> shape)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        var q = WithTarget(db.ActionItems).Where(a => a.UserId == userId
            && a.Status != ActionStatus.Done && a.Status != ActionStatus.Cancelled);
        return await shape(q).ToListAsync();
    }

    public Task<List<ActionItem>> GetOpenDueAsync(DateTime before, int count = 50) =>
        PlanningAsync(q => q.Where(a => a.DueDate != null && a.DueDate <= before)
            .OrderBy(a => a.DueDate).Take(count));

    public Task<List<ActionItem>> GetOverdueAsync(int count = 50)
    {
        var now = DateTime.UtcNow;
        return PlanningAsync(q => q.Where(a => a.DueDate != null && a.DueDate < now)
            .OrderBy(a => a.DueDate).Take(count));
    }

    public Task<List<ActionItem>> GetUpcomingRemindersAsync(int count = 50)
    {
        var now = DateTime.UtcNow;
        return PlanningAsync(q => q.Where(a => a.RemindAt != null && a.RemindAt >= now)
            .OrderBy(a => a.RemindAt).Take(count));
    }

    public Task<List<ActionItem>> GetOverdueRemindersAsync(int count = 50)
    {
        var now = DateTime.UtcNow;
        return PlanningAsync(q => q.Where(a => a.RemindAt != null && a.RemindAt < now)
            .OrderBy(a => a.RemindAt).Take(count));
    }

    private static Task<bool> OwnsNodeAsync(NookContext db, string userId, int nodeId) =>
        db.Nodes.AnyAsync(n => n.NodeId == nodeId && n.UserId == userId);
}

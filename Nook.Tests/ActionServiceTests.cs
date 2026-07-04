using Nook.Models;
using Nook.Services;
using Xunit;

namespace Nook.Tests;

public class ActionServiceTests
{
    [Fact]
    public async Task Completing_an_action_never_changes_the_target_node()
    {
        var h = new GraphHarness();
        var node = await h.Nodes("u").CreateAsync(new Node
        {
            Title = "Have Jamie pick me up before X", Kind = NodeKind.Note, State = NodeState.Active
        });
        var act = h.Actions("u");
        var action = await act.CreateAsync(new ActionItem
        {
            Kind = ActionKind.Task, Title = "Confirm pickup with Jamie", TargetNodeId = node.NodeId
        });

        await act.CompleteAsync(action.ActionItemId);

        var reloadedNode = await h.Nodes("u").GetByIdAsync(node.NodeId);
        Assert.Equal(NodeState.Active, reloadedNode!.State);         // node untouched
        Assert.Null(reloadedNode.ArchivedAt);
        var reloadedAction = await act.GetByIdAsync(action.ActionItemId);
        Assert.Equal(ActionStatus.Done, reloadedAction!.Status);
        Assert.NotNull(reloadedAction.CompletedAt);
    }

    [Fact]
    public async Task Reusable_node_can_spawn_multiple_dated_actions_over_time()
    {
        var h = new GraphHarness();
        var node = await h.Nodes("u").CreateAsync(new Node { Title = "Weekly check-in with Jamie" });
        var act = h.Actions("u");

        var first = await act.CreateAsync(new ActionItem
        {
            Kind = ActionKind.Task, Title = "Check in (week 1)", TargetNodeId = node.NodeId,
            DueDate = DateTime.UtcNow.AddDays(1)
        });
        await act.CompleteAsync(first.ActionItemId);

        var second = await act.CreateAsync(new ActionItem
        {
            Kind = ActionKind.Task, Title = "Check in (week 2)", TargetNodeId = node.NodeId,
            DueDate = DateTime.UtcNow.AddDays(8)
        });

        var all = await act.GetForNodeAsync(node.NodeId);
        Assert.Equal(2, all.Count);
        // The completed one is untouched; the new one is independent and open.
        Assert.Contains(all, a => a.ActionItemId == first.ActionItemId && a.Status == ActionStatus.Done);
        Assert.Contains(all, a => a.ActionItemId == second.ActionItemId && a.Status == ActionStatus.Open);
    }

    [Fact]
    public async Task ActionContext_rollup_returns_actions_related_to_a_person()
    {
        var h = new GraphHarness();
        var jamie = await h.Nodes("u").CreateAsync(new Node { Title = "Jamie", Kind = NodeKind.Person });
        var note = await h.Nodes("u").CreateAsync(new Node { Title = "A note", Kind = NodeKind.Note });
        var act = h.Actions("u");

        // Action targets the note but is contextually about Jamie.
        var action = await act.CreateAsync(
            new ActionItem { Kind = ActionKind.Task, Title = "Tell Jamie", TargetNodeId = note.NodeId },
            new[] { new ActionContextInput(jamie.NodeId, ActionContextRole.Person) });

        var forJamie = await act.GetForNodeAsync(jamie.NodeId);
        Assert.Single(forJamie);
        Assert.Equal(action.ActionItemId, forJamie[0].ActionItemId);
        Assert.Equal(1, await act.CountOpenForNodeAsync(jamie.NodeId));
    }

    [Fact]
    public async Task Checklist_items_group_under_a_parent()
    {
        var h = new GraphHarness();
        var project = await h.Nodes("u").CreateAsync(new Node { Title = "Launch", Kind = NodeKind.Project });
        var act = h.Actions("u");
        var header = await act.CreateAsync(new ActionItem
        {
            Kind = ActionKind.Task, Title = "Launch checklist", TargetNodeId = project.NodeId
        });

        await act.AddChecklistItemAsync(header.ActionItemId, "Write copy");
        await act.AddChecklistItemAsync(header.ActionItemId, "Ship it");

        var children = await act.GetChildrenAsync(header.ActionItemId);
        Assert.Equal(2, children.Count);
        Assert.All(children, c => Assert.Equal(ActionKind.ChecklistItem, c.Kind));
        Assert.All(children, c => Assert.Equal(project.NodeId, c.TargetNodeId)); // inherit target
    }

    [Fact]
    public async Task Actions_are_scoped_per_user()
    {
        var h = new GraphHarness();
        var mine = await h.Nodes("a").CreateAsync(new Node { Title = "mine" });
        await h.Actions("a").CreateAsync(new ActionItem { Title = "a task", TargetNodeId = mine.NodeId });

        Assert.Single(await h.Actions("a").QueryAsync(new ActionFilter()));
        Assert.Empty(await h.Actions("b").QueryAsync(new ActionFilter()));
    }

    [Fact]
    public async Task Action_cannot_target_another_users_node()
    {
        var h = new GraphHarness();
        var theirs = await h.Nodes("other").CreateAsync(new Node { Title = "theirs" });
        var action = await h.Actions("u").CreateAsync(new ActionItem
        {
            Kind = ActionKind.Task, Title = "sneaky", TargetNodeId = theirs.NodeId
        });
        // Target was rejected (nulled) because it isn't the user's node.
        Assert.Null(action.TargetNodeId);
    }
}

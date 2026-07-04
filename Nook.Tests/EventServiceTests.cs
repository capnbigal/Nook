using Nook.Models;
using Nook.Services;
using Xunit;

namespace Nook.Tests;

public class EventServiceTests
{
    [Fact]
    public async Task Free_text_event_is_valid_with_title_and_time_only()
    {
        var h = new GraphHarness();
        var node = await h.Events("u").CreateAsync("Met Jamie for coffee", DateTime.UtcNow);

        Assert.Equal(NodeKind.Event, node.Kind);
        var details = await h.Events("u").GetByNodeIdAsync(node.NodeId);
        Assert.NotNull(details);
        Assert.Null(details!.VerbId);
        Assert.Null(details.SubjectNodeId);
    }

    [Fact]
    public async Task Event_records_subject_object_place_and_participants()
    {
        var h = new GraphHarness();
        await h.Migration().SeedSystemDataAsync();
        var jamie = await h.Nodes("u").CreateAsync(new Node { Title = "Jamie", Kind = NodeKind.Person });
        var alex = await h.Nodes("u").CreateAsync(new Node { Title = "Alex", Kind = NodeKind.Person });
        var place = await h.Nodes("u").CreateAsync(new Node { Title = "The Meetup", Kind = NodeKind.Place });
        var verbs = await h.Events("u").GetVerbsAsync();
        var introduced = verbs.First(v => v.Name == "introduced");

        var node = await h.Events("u").CreateAsync(
            "Jamie introduced me to Alex", DateTime.UtcNow, null,
            introduced.VerbId, jamie.NodeId, alex.NodeId, place.NodeId,
            new[] { new EventParticipantInput(alex.NodeId, EventParticipantRole.Introduced) });

        var details = await h.Events("u").GetByNodeIdAsync(node.NodeId);
        Assert.Equal(jamie.NodeId, details!.SubjectNodeId);
        Assert.Equal(alex.NodeId, details.ObjectNodeId);
        Assert.Equal(place.NodeId, details.PlaceNodeId);
        Assert.Single(details.Participants);

        // The event surfaces on the participant's node.
        var forAlex = await h.Events("u").GetEventsForNodeAsync(alex.NodeId);
        Assert.Contains(forAlex, n => n.NodeId == node.NodeId);
    }

    [Fact]
    public async Task Self_person_is_created_lazily_and_reused()
    {
        var h = new GraphHarness();
        await using (var db = h.Factory.CreateDbContext())
        {
            db.Users.Add(new ApplicationUser { Id = "u", UserName = "me@nook.local" });
            await db.SaveChangesAsync();
        }

        var first = await h.Events("u").GetOrCreateSelfPersonAsync();
        var second = await h.Events("u").GetOrCreateSelfPersonAsync();

        Assert.Equal(NodeKind.Person, first.Kind);
        Assert.Equal(first.NodeId, second.NodeId); // reused, not duplicated
    }

    [Fact]
    public async Task Events_are_scoped_per_user()
    {
        var h = new GraphHarness();
        await h.Events("a").CreateAsync("A's event", DateTime.UtcNow);
        Assert.Single(await h.Events("a").GetTimelineAsync());
        Assert.Empty(await h.Events("b").GetTimelineAsync());
    }
}

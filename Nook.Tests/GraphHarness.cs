using Nook.Services;

namespace Nook.Tests;

/// <summary>Builds a set of graph services sharing one InMemory database, scoped to a user.</summary>
public sealed class GraphHarness
{
    public TestDbContextFactory Factory { get; }
    public ActivityService Activity { get; }

    public GraphHarness(TestDbContextFactory? factory = null)
    {
        Factory = factory ?? new TestDbContextFactory();
        Activity = new ActivityService(Factory);
    }

    public NodeService Nodes(string userId) => new(Factory, new FakeCurrentUser(userId), Activity);
    public RelationService Relations(string userId) => new(Factory, new FakeCurrentUser(userId), Activity);
    public CollectionService Collections(string userId) => new(Factory, new FakeCurrentUser(userId), Activity);
    public ActionService Actions(string userId) => new(Factory, new FakeCurrentUser(userId), Activity);
    public EventService Events(string userId) => new(Factory, new FakeCurrentUser(userId), Activity);
    public TagService Tags(string userId) => new(Factory, new FakeCurrentUser(userId), Activity);

    public GraphMigrationService Migration() => new(Factory);
}

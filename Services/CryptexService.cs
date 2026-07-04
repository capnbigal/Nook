using Microsoft.EntityFrameworkCore;
using Nook.Data;
using Nook.Models;

namespace Nook.Services;

public sealed class CryptexService : ICryptexService
{
    // _nodes/_tags/_collections/_relations are used by AddNodeWithCodeAsync (Task 3).
    private readonly IDbContextFactory<NookContext> _factory;
    private readonly ICurrentUser _currentUser;
    private readonly INodeService _nodes;
    private readonly ITagService _tags;
    private readonly ICollectionService _collections;
    private readonly IRelationService _relations;

    public CryptexService(
        IDbContextFactory<NookContext> factory, ICurrentUser currentUser,
        INodeService nodes, ITagService tags, ICollectionService collections, IRelationService relations)
    {
        _factory = factory;
        _currentUser = currentUser;
        _nodes = nodes;
        _tags = tags;
        _collections = collections;
        _relations = relations;
    }

    public async Task<List<CryptexNode>> GetDatasetAsync()
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();

        var nodes = await db.Nodes.Where(n => n.UserId == userId)
            .Select(n => new { n.NodeId, n.Title, n.Kind, n.State, n.Body })
            .ToListAsync();

        var tags = (await db.NodeTags.Where(nt => nt.Node.UserId == userId)
                .Select(nt => new { nt.NodeId, nt.Tag.Name }).ToListAsync())
            .GroupBy(x => x.NodeId).ToDictionary(g => g.Key, g => g.Select(x => x.Name).ToList());

        // Only ACTIVE collections surface as facet values (spec: "active collection names");
        // archived collections are intentionally excluded.
        var colls = (await db.CollectionMemberships
                .Where(m => m.UserId == userId && m.Collection!.Node!.State != NodeState.Archived)
                .Select(m => new { m.MemberNodeId, Title = m.Collection!.Node!.Title }).ToListAsync())
            .GroupBy(x => x.MemberNodeId).ToDictionary(g => g.Key, g => g.Select(x => x.Title).ToList());

        // People = Person-kind endpoints of any relation the node is part of.
        var rels = await db.NodeRelations.Where(r => r.UserId == userId)
            .Select(r => new {
                r.SourceNodeId, r.TargetNodeId,
                SourceKind = r.SourceNode!.Kind, TargetKind = r.TargetNode!.Kind,
                SourceTitle = r.SourceNode!.Title, TargetTitle = r.TargetNode!.Title })
            .ToListAsync();
        var people = new Dictionary<int, HashSet<string>>();
        void AddPerson(int nodeId, string name) =>
            (people.TryGetValue(nodeId, out var s) ? s : people[nodeId] = new()).Add(name);
        foreach (var r in rels)
        {
            if (r.TargetKind == NodeKind.Person) AddPerson(r.SourceNodeId, r.TargetTitle);
            if (r.SourceKind == NodeKind.Person) AddPerson(r.TargetNodeId, r.SourceTitle);
        }

        return nodes.Select(n => new CryptexNode(
            n.NodeId, n.Title, n.Kind, n.State,
            n.Body is null ? null : (n.Body.Length > 160 ? n.Body[..160] + "…" : n.Body),
            tags.TryGetValue(n.NodeId, out var t) ? t : new List<string>(),
            colls.TryGetValue(n.NodeId, out var c) ? c : new List<string>(),
            people.TryGetValue(n.NodeId, out var p) ? p.ToList() : new List<string>()))
            .ToList();
    }

    public async Task<int> AddNodeWithCodeAsync(string title, IReadOnlyDictionary<CryptexRing, string> code)
    {
        var kind = code.TryGetValue(CryptexRing.Kind, out var k) && Enum.TryParse<NodeKind>(k, out var pk)
            ? pk : NodeKind.Unclassified;
        var state = code.TryGetValue(CryptexRing.State, out var s) && Enum.TryParse<NodeState>(s, out var ps)
            ? ps : NodeState.Inbox;

        var node = await _nodes.CreateAsync(new Node { Title = title.Trim(), Kind = kind, State = state });

        if (code.TryGetValue(CryptexRing.Tag, out var tagName) && !string.IsNullOrWhiteSpace(tagName))
        {
            var tag = await _tags.GetOrCreateAsync(tagName);
            await _tags.AssignToNodeAsync(node.NodeId, tag.TagId);
        }

        if (code.TryGetValue(CryptexRing.Collection, out var collName) && !string.IsNullOrWhiteSpace(collName))
        {
            var collection = (await _collections.GetCollectionsAsync())
                .FirstOrDefault(c => string.Equals(c.Node.Title, collName, StringComparison.OrdinalIgnoreCase));
            if (collection is not null)
                await _collections.AddMemberAsync(collection.Node.NodeId, node.NodeId);
        }

        if (code.TryGetValue(CryptexRing.People, out var personName) && !string.IsNullOrWhiteSpace(personName))
        {
            var userId = await _currentUser.GetRequiredUserIdAsync();
            await using var db = await _factory.CreateDbContextAsync();
            var person = await db.Nodes.FirstOrDefaultAsync(n =>
                n.UserId == userId && n.Kind == NodeKind.Person && n.Title == personName);
            var assoc = (await _relations.GetRelationTypesAsync())
                .FirstOrDefault(r => r.Name == "associated with");
            if (person is not null && assoc is not null)
                await _relations.AddRelationAsync(node.NodeId, person.NodeId, assoc.RelationTypeId);
        }

        return node.NodeId;
    }
}

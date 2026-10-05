using EverythingLabeled.Service.Services.Abstractions;

namespace EverythingLabeled.Service.Services.Relations;

public readonly record struct RelationEdge(Guid FromTagId, Guid ToTagId, string Kind);

public sealed record RelationGraphFailure(
    int Index,
    string Code,
    string Message,
    Guid FromTagId,
    Guid ToTagId,
    string? Kind);

public static class RelationGraph
{
    public const string Child = "child";
    public const string Related = "related";
    public const int BatchMin = 1;
    public const int BatchMax = 100;

    public static bool IsKnownKind(string? kind) =>
        kind is Child or Related;

    public static RelationGraphFailure? Validate(
        IReadOnlyList<RelationEdge> existing,
        IReadOnlyList<RelationEdge> proposed)
    {
        var unique = new HashSet<(Guid From, Guid To, string Kind)>();
        var childParent = new Dictionary<Guid, Guid>();
        var childAdj = new Dictionary<Guid, List<Guid>>();

        foreach (var edge in existing)
        {
            unique.Add((edge.FromTagId, edge.ToTagId, edge.Kind));
            if (edge.Kind != Child)
                continue;

            childParent[edge.ToTagId] = edge.FromTagId;
            AddChild(childAdj, edge.FromTagId, edge.ToTagId);
        }

        for (var i = 0; i < proposed.Count; i++)
        {
            var edge = proposed[i];
            if (!IsKnownKind(edge.Kind))
            {
                return Fail(i, ErrorCodes.ValidationFailed, "Invalid relation kind.", edge);
            }

            if (edge.FromTagId == edge.ToTagId)
            {
                return Fail(i, ErrorCodes.ValidationFailed, "Self-loop is not allowed.", edge);
            }

            if (!unique.Add((edge.FromTagId, edge.ToTagId, edge.Kind)))
            {
                return Fail(i, ErrorCodes.RelationConflict, "Relation already exists.", edge);
            }

            if (edge.Kind != Child)
                continue;

            if (childParent.ContainsKey(edge.ToTagId))
            {
                return Fail(i, ErrorCodes.RelationConflict, "Child tag already has a parent.", edge);
            }

            if (Reaches(childAdj, edge.ToTagId, edge.FromTagId))
            {
                return Fail(i, ErrorCodes.RelationCycle, "Child relation would create a cycle.", edge);
            }

            childParent[edge.ToTagId] = edge.FromTagId;
            AddChild(childAdj, edge.FromTagId, edge.ToTagId);
        }

        return null;
    }

    private static RelationGraphFailure Fail(int index, string code, string message, RelationEdge edge) =>
        new(index, code, message, edge.FromTagId, edge.ToTagId, edge.Kind);

    private static void AddChild(Dictionary<Guid, List<Guid>> adj, Guid from, Guid to)
    {
        if (!adj.TryGetValue(from, out var children))
        {
            children = [];
            adj[from] = children;
        }

        children.Add(to);
    }

    private static bool Reaches(Dictionary<Guid, List<Guid>> adj, Guid start, Guid target)
    {
        if (start == target)
            return true;

        var seen = new HashSet<Guid> { start };
        var queue = new Queue<Guid>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (!adj.TryGetValue(node, out var children))
                continue;

            foreach (var child in children)
            {
                if (child == target)
                    return true;
                if (seen.Add(child))
                    queue.Enqueue(child);
            }
        }

        return false;
    }
}

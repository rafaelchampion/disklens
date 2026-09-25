using DiskTree.Core.Classify;
using DiskTree.Core.Tree;

namespace DiskTree.Core.Insights;

public abstract record Finding
{
    public sealed record ReclaimableSpace(
        Reclaim Reason, string DirName) : Finding;
    public sealed record Worktrees(
        int Count, long OldestDays) : Finding;
    public sealed record StaleExperiments(
        int Count) : Finding;

    public string Description => this switch
    {
        ReclaimableSpace r =>
            $"{r.Reason.Label()} — {r.DirName}",
        Worktrees w =>
            $"{w.Count} worktrees, oldest {w.OldestDays}d",
        StaleExperiments s =>
            $"{s.Count} untouched > 30d",
        _ => "reclaimable"
    };
}

public sealed record Candidate(
    int[] Crumbs,
    ulong Bytes,
    Finding Finding,
    string Path
);

public static class InsightFinder
{
    private const long Day = 86_400;
    public const long StaleDays = 30;
    // 64 MB minimum to show up
    private const ulong MinBytes = 64 * 1024 * 1024;

    public static List<Candidate> WorthALook(
        Node root, long nowUnixSeconds, int limit = 10)
    {
        var found = new List<Candidate>();
        var crumbs = new List<int>();
        var pathParts = new List<string> { root.Name };

        for (int i = 0; i < root.Children.Count; i++)
        {
            crumbs.Add(i);
            pathParts.Add(root.Children[i].Name);
            Visit(root.Children[i], crumbs, pathParts,
                nowUnixSeconds, found);
            pathParts.RemoveAt(pathParts.Count - 1);
            crumbs.RemoveAt(crumbs.Count - 1);
        }

        found.RemoveAll(c => c.Bytes < MinBytes);
        found.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));

        if (found.Count > limit)
            found.RemoveRange(limit, found.Count - limit);

        return found;
    }

    private static void Visit(
        Node node,
        List<int> crumbs,
        List<string> pathParts,
        long now,
        List<Candidate> found)
    {
        if (!node.IsDirectory)
            return;

        string currentPath = string.Join(
            System.IO.Path.DirectorySeparatorChar, pathParts);

        // Topmost reclaimable: children inherit it, so only
        // report the topmost candidate
        if (node.Reclaim.HasValue)
        {
            found.Add(new Candidate(
                crumbs.ToArray(),
                node.Bytes,
                new Finding.ReclaimableSpace(
                    node.Reclaim.Value, node.Name),
                currentPath));
            return;
        }

        string name = node.Name.ToLowerInvariant();
        bool isScratch =
            node.Category == Category.AgentScratch;

        if (isScratch && name == "worktrees")
        {
            var trees = node.Children
                .Where(c => c.IsDirectory).ToList();
            if (trees.Count > 0)
            {
                long oldest = trees
                    .Where(t => t.Modified > 0)
                    .Select(t => t.Modified)
                    .DefaultIfEmpty(now)
                    .Min();

                long oldestDays = Math.Max(
                    0, (now - oldest) / Day);
                found.Add(new Candidate(
                    crumbs.ToArray(),
                    node.Bytes,
                    new Finding.Worktrees(
                        trees.Count, oldestDays),
                    currentPath));
                return;
            }
        }

        for (int i = 0; i < node.Children.Count; i++)
        {
            crumbs.Add(i);
            pathParts.Add(node.Children[i].Name);
            Visit(node.Children[i], crumbs, pathParts,
                now, found);
            pathParts.RemoveAt(pathParts.Count - 1);
            crumbs.RemoveAt(crumbs.Count - 1);
        }
    }
}

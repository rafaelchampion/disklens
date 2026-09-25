using DiskTree.Core.Classify;
using DiskTree.Core.Removal;
using DiskTree.Core.Tree;
using DiskTree.Core.Treemap;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DiskTree.Core.Tests;

[TestClass]
public class CoreTests
{
    [TestMethod]
    public void TreeAggregation_ComputesSubtreeAndDirectTotals()
    {
        var root = Node.CreateDirectory("root");
        var dirA = Node.CreateDirectory("dirA");
        var fileA1 = Node.CreateEntry("fileA1.txt", NodeKind.File, 1000);
        var fileA2 = Node.CreateEntry("fileA2.txt", NodeKind.File, 2000);
        dirA.Children.Add(fileA1);
        dirA.Children.Add(fileA2);

        var fileRoot = Node.CreateEntry("rootFile.txt", NodeKind.File, 500);

        root.Children.Add(dirA);
        root.Children.Add(fileRoot);

        TreeAggregation.Aggregate(root, Metric.Bytes);

        Assert.AreEqual(3500UL, root.Bytes);
        Assert.AreEqual(500UL, root.OwnBytes);
        Assert.AreEqual(3UL, root.Files);
        Assert.AreEqual(1UL, root.OwnFiles);
        Assert.AreEqual(2UL, root.Dirs); // root + dirA

        Assert.AreEqual(3000UL, dirA.Bytes);
        Assert.AreEqual(3000UL, dirA.OwnBytes);
        Assert.AreEqual(2UL, dirA.Files);

        // Children should be sorted largest first: dirA (3000) then fileRoot (500)
        Assert.AreEqual("dirA", root.Children[0].Name);
        Assert.AreEqual("rootFile.txt", root.Children[1].Name);
    }

    [TestMethod]
    public void Classifier_IdentifiesCategoriesAndReclaimableSpace()
    {
        var root = Node.CreateDirectory("my-project");

        var srcDir = Node.CreateDirectory("src");
        srcDir.Children.Add(Node.CreateEntry("main.rs", NodeKind.File, 200));

        var targetDir = Node.CreateDirectory("target");
        targetDir.Children.Add(Node.CreateEntry("app.exe", NodeKind.File, 50000));

        var cargoToml = Node.CreateEntry("Cargo.toml", NodeKind.File, 100);

        root.Children.Add(srcDir);
        root.Children.Add(targetDir);
        root.Children.Add(cargoToml);

        TreeAggregation.Aggregate(root, Metric.Bytes);
        Classifier.Classify(root);

        Assert.AreEqual(Category.Code, srcDir.Category);
        Assert.AreEqual(Reclaim.BuildOutput, targetDir.Reclaim);
    }

    [TestMethod]
    public void TreemapLayout_SquarifiesWithinBounds()
    {
        var root = Node.CreateDirectory("root");
        for (int i = 0; i < 5; i++)
        {
            root.Children.Add(Node.CreateEntry($"file{i}.bin", NodeKind.File, (ulong)((i + 1) * 1000)));
        }

        TreeAggregation.Aggregate(root, Metric.Bytes);

        var area = new TreemapRect(0, 0, 800, 600);
        var options = new LayoutOptions { MaxDepth = 2, Padding = 1.0f };
        var tiles = TreemapLayout.Layout(root, [], area, Metric.Bytes, options);

        Assert.IsTrue(tiles.Count > 0);
        foreach (var tile in tiles)
        {
            Assert.IsTrue(tile.Rect.X >= area.X);
            Assert.IsTrue(tile.Rect.Y >= area.Y);
            Assert.IsTrue(tile.Rect.Right <= area.Right + 0.1f);
            Assert.IsTrue(tile.Rect.Bottom <= area.Bottom + 0.1f);
        }
    }

    [TestMethod]
    public void RemovalGuards_RefuseSystemRootsAndParentPaths()
    {
        string root = @"D:\Projects\TestFolder";

        // Scanned root itself cannot be deleted
        Assert.IsNotNull(RemovalEngine.Refuse(@"D:\Projects\TestFolder", root));

        // Drive root cannot be deleted
        Assert.IsNotNull(RemovalEngine.Refuse(@"D:\", root));

        // Outside scanned root cannot be deleted
        Assert.IsNotNull(RemovalEngine.Refuse(@"D:\OtherFolder", root));

        // Windows folder cannot be deleted
        string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        Assert.IsNotNull(RemovalEngine.Refuse(winDir, root));

        // Valid subfolder under scanned root CAN be removed
        Assert.IsNull(RemovalEngine.Refuse(@"D:\Projects\TestFolder\build", root));
    }

    [TestMethod]
    public void RemovalPlan_CoversNestedTargets()
    {
        string root = @"D:\Projects\TestFolder";
        var targets = new List<Target>
        {
            new(@"D:\Projects\TestFolder\sub", 1000, true, false),
            new(@"D:\Projects\TestFolder\sub\inner", 500, true, false),
            new(@"D:\Projects\TestFolder\other", 200, false, false)
        };

        var plan = RemovalEngine.BuildPlan(targets, root);

        Assert.AreEqual(2, plan.Targets.Count); // sub and other
        Assert.AreEqual(1, plan.Covered.Count); // sub\inner is covered by sub
        Assert.AreEqual(0, plan.Blocked.Count);
    }
}

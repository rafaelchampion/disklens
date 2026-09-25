namespace DiskTree.Core.Tree;

/// <summary>
/// How a node's importance is measured.
/// </summary>
public enum Metric : byte
{
    /// <summary>
    /// Bytes (allocated on-disk or apparent).
    /// </summary>
    Bytes,

    /// <summary>
    /// Number of files at or beneath the node.
    /// </summary>
    Files
}

public static class MetricExtensions
{
    public static string Label(this Metric metric) => metric switch
    {
        Metric.Bytes => "size",
        Metric.Files => "files",
        _ => "size"
    };

    public static Metric Toggled(this Metric metric) => metric switch
    {
        Metric.Bytes => Metric.Files,
        Metric.Files => Metric.Bytes,
        _ => Metric.Bytes
    };
}

namespace Zaya.DocumentScanner.Impl.Shared.Models;

/// <summary>One source image's recognized lines plus its pixel metrics - one physical page when several scans are combined into a single export/preview.</summary>
public sealed record ScannedPage(
    IReadOnlyList<ScannedLine> Lines, IReadOnlyList<RotatedTextCluster> RotatedClusters, PageMetrics Metrics);

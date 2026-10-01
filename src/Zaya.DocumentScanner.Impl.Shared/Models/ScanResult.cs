namespace Zaya.DocumentScanner.Impl.Shared.Models;

/// <summary>One scan pass's output: normal horizontal-flow lines plus any rotated text clusters found alongside them.</summary>
public sealed record ScanResult(IReadOnlyList<ScannedLine> Lines, IReadOnlyList<RotatedTextCluster> RotatedClusters);

namespace Zaya.DocumentScanner.Impl.Shared.Models;

/// <summary>
/// One page's font sizes in points, decided once for the whole page by <c>FontSizeEstimator</c>
/// (a small set of clustered sizes, see there) and looked up per word / per line by the exporter,
/// the live preview and the page-fit estimate, so all three agree. Lookups are by reference: the
/// same <see cref="ScannedWord"/>/<see cref="ScannedLine"/> instances the estimate was built from.
/// </summary>
public sealed class PageFontSizes(
    IReadOnlyDictionary<ScannedWord, float> wordPt,
    IReadOnlyDictionary<ScannedLine, float> linePt,
    IReadOnlyList<float> clusterPt,
    float fallbackPt)
{
    /// <summary>The distinct sizes found on the page, ascending.</summary>
    public IReadOnlyList<float> ClusterPt { get; } = clusterPt;

    public float ForWord(ScannedWord word) => wordPt.TryGetValue(word, out var pt) ? pt : fallbackPt;

    public float ForLine(ScannedLine line) => linePt.TryGetValue(line, out var pt) ? pt : fallbackPt;
}

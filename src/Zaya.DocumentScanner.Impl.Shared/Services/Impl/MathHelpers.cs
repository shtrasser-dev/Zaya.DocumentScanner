namespace Zaya.DocumentScanner.Impl.Shared.Services.Impl;

internal static class MathHelpers
{
    public static float Median(IReadOnlyList<float> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2f : sorted[mid];
    }
}

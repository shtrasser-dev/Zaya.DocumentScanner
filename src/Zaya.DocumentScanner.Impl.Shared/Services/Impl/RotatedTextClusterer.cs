using System.Numerics;
using Zaya.DocumentScanner.Impl.Shared.Models;
using Zaya.Primitives.OCR;

namespace Zaya.DocumentScanner.Impl.Shared.Services.Impl;

/// <summary>
/// Separates OCR words whose own text angle marks them as rotated/curved content (e.g. text around
/// a round stamp) from the page's normal horizontal flow, collects distinct sightings of such
/// words across the whole scan (the sliding window sees the same physical word many times), and
/// groups them into position-based clusters - one per physical stamp/rotated block - instead of
/// trying to make the straight-line word-to-line assembler recognize them (tried and abandoned,
/// see DocumentScanService's own notes on that attempt).
/// </summary>
public static class RotatedTextClusterer
{
    /// <summary>
    /// Splits this frame's words by whether their own angle is close to the frame's typical
    /// (median) word angle. Ordinary printed body text sits at one consistent angle across a
    /// whole frame (give or take a photographed page's own small, uniform tilt); a round stamp's
    /// text follows the curve, so its words swing through a wide range of angles word to word.
    /// </summary>
    public static (List<IOCRWord> Normal, List<IOCRWord> Outlier) SplitByAngle(
        IReadOnlyList<IOCRWord> words, float maxDeviationDegrees)
    {
        if (words.Count < 2)
            return (words.ToList(), []);

        var typicalAngle = Median(words.Select(w => (double)w.Bounds.AngleDegrees).ToList());

        var normal = new List<IOCRWord>();
        var outlier = new List<IOCRWord>();
        foreach (var w in words)
        {
            if (AngleDelta(w.Bounds.AngleDegrees, typicalAngle) <= maxDeviationDegrees)
                normal.Add(w);
            else
                outlier.Add(w);
        }

        return (normal, outlier);
    }

    private sealed record Observation(string Text, float MinX, float MaxX, float MinY, float MaxY, float AngleDegrees);

    /// <summary>
    /// Accumulates distinct rotated-word sightings across every frame of a scan. The sliding
    /// window passes over the same physical stamp many times, so each call is de-duplicated
    /// against everything already recorded by bounding-box overlap - the same physical word
    /// should land at nearly the same absolute position every time it's seen.
    /// </summary>
    public sealed class ObservationSet
    {
        private readonly List<Observation> _items = [];

        public void Add(IReadOnlyList<IOCRWord> words)
        {
            foreach (var w in words)
            {
                var candidate = new Observation(
                    w.Text, w.Bounds.MinX, w.Bounds.MaxX, w.Bounds.MinY, w.Bounds.MaxY, w.Bounds.AngleDegrees);
                if (_items.Any(existing => Overlaps(existing, candidate)))
                    continue;

                _items.Add(candidate);
            }
        }

        public List<RotatedTextCluster> BuildClusters() => RotatedTextClusterer.BuildClusters(_items);
    }

    private static bool Overlaps(Observation a, Observation b)
    {
        var overlapX = Math.Min(a.MaxX, b.MaxX) - Math.Max(a.MinX, b.MinX);
        var overlapY = Math.Min(a.MaxY, b.MaxY) - Math.Max(a.MinY, b.MinY);
        if (overlapX <= 0 || overlapY <= 0)
            return false;

        var overlapArea = overlapX * overlapY;
        var smallerArea = Math.Min((a.MaxX - a.MinX) * (a.MaxY - a.MinY), (b.MaxX - b.MinX) * (b.MaxY - b.MinY));
        return smallerArea > 0 && overlapArea / smallerArea > 0.5f;
    }

    /// <summary>
    /// Groups observations by proximity (union-find over a "close enough to be the same
    /// stamp/block" graph), one cluster per connected group. Each cluster's words are ordered by
    /// angular position around the cluster's own centroid - a natural, parameter-free
    /// approximation of reading order around a circular stamp, without needing the word-to-line
    /// assembler to solve it. Single-word groups are dropped as more likely a stray misread than
    /// genuinely rotated content worth its own text box.
    /// </summary>
    private static List<RotatedTextCluster> BuildClusters(IReadOnlyList<Observation> observations)
    {
        if (observations.Count == 0)
            return [];

        var heights = observations.Select(o => o.MaxY - o.MinY).Where(h => h > 0).ToList();
        var typicalHeight = heights.Count > 0 ? Median(heights) : 20f;
        var neighborDistance = typicalHeight * 4f;

        var n = observations.Count;
        var centers = observations.Select(o => new Vector2((o.MinX + o.MaxX) / 2f, (o.MinY + o.MaxY) / 2f)).ToList();

        var parent = Enumerable.Range(0, n).ToArray();
        int Find(int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }
            return x;
        }
        void Union(int a, int b)
        {
            var ra = Find(a);
            var rb = Find(b);
            if (ra != rb)
                parent[ra] = rb;
        }

        for (var i = 0; i < n; i++)
            for (var j = i + 1; j < n; j++)
                if (Vector2.Distance(centers[i], centers[j]) <= neighborDistance)
                    Union(i, j);

        var groups = new Dictionary<int, List<int>>();
        for (var i = 0; i < n; i++)
        {
            var root = Find(i);
            if (!groups.TryGetValue(root, out var members))
                groups[root] = members = [];
            members.Add(i);
        }

        var clusters = new List<RotatedTextCluster>();
        foreach (var members in groups.Values)
        {
            if (members.Count < 2)
                continue;

            var words = members.Select(i => observations[i]).ToList();
            var minX = words.Min(w => w.MinX);
            var maxX = words.Max(w => w.MaxX);
            var minY = words.Min(w => w.MinY);
            var maxY = words.Max(w => w.MaxY);
            var centerX = (minX + maxX) / 2f;
            var centerY = (minY + maxY) / 2f;

            var ordered = words
                .OrderBy(w => Math.Atan2(((w.MinY + w.MaxY) / 2f) - centerY, ((w.MinX + w.MaxX) / 2f) - centerX))
                .ToList();

            var angle = (float)Median(words.Select(w => (double)w.AngleDegrees).ToList());
            var text = string.Join(" ", ordered.Select(w => w.Text));

            clusters.Add(new RotatedTextCluster(text, centerX, centerY, maxX - minX, maxY - minY, angle));
        }

        return clusters;
    }

    private static double Median(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2.0 : sorted[mid];
    }

    private static float Median(List<float> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2f : sorted[mid];
    }

    /// <summary>Smallest angle between two directions, wrapped into [0, 180].</summary>
    private static double AngleDelta(double a, double b)
    {
        var d = Math.Abs(a - b) % 360.0;
        return d > 180.0 ? 360.0 - d : d;
    }
}

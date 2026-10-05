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

    /// <param name="TextHeight">Glyph height along the word's own tilt (mean of the P1-P4 and P2-P3 edges) -
    /// for a word rotated ~65°, MaxY - MinY is closer to the word's length than to its glyph height.</param>
    /// <param name="TextLength">Word length along its own tilt (mean of the P1-P2 and P4-P3 edges).</param>
    private sealed record Observation(
        string Text, float MinX, float MaxX, float MinY, float MaxY, float AngleDegrees, float TextHeight, float TextLength);

    /// <summary>
    /// Accumulates distinct rotated-word sightings across every frame of a scan. The sliding
    /// window passes over the same physical stamp many times, so each call is de-duplicated
    /// against everything already recorded by bounding-box overlap - the same physical word
    /// should land at nearly the same absolute position every time it's seen.
    /// <para/>
    /// A rotated word is tall on the page, so the horizontal window clips it in most frames it
    /// appears in - and the first sighting is exactly the one where it has only just entered the
    /// window. Sightings touching the window's top or bottom edge are therefore ignored, and of
    /// two overlapping sightings the longer reading is kept: on a test stamp at -55°, keeping the
    /// first sighting turned "КОПИЯ ВЕРНА" into fragments like "КОПИ", "Я", "ВЕРН".
    /// </summary>
    public sealed class ObservationSet
    {
        private readonly List<Observation> _items = [];

        public void Add(IReadOnlyList<IOCRWord> words, int windowY, int windowHeight)
        {
            foreach (var w in words)
            {
                var candidate = new Observation(
                    w.Text, w.Bounds.MinX, w.Bounds.MaxX, w.Bounds.MinY, w.Bounds.MaxY, w.Bounds.AngleDegrees,
                    (Vector2.Distance(w.Bounds.P1, w.Bounds.P4) + Vector2.Distance(w.Bounds.P2, w.Bounds.P3)) / 2f,
                    (Vector2.Distance(w.Bounds.P1, w.Bounds.P2) + Vector2.Distance(w.Bounds.P4, w.Bounds.P3)) / 2f);

                var guard = 0.5f * candidate.TextHeight;
                if (candidate.MinY - windowY < guard || windowY + windowHeight - candidate.MaxY < guard)
                    continue;

                var existing = _items.FindIndex(e => Overlaps(e, candidate));
                if (existing < 0)
                    _items.Add(candidate);
                else if (candidate.Text.Trim().Length > _items[existing].Text.Trim().Length)
                    _items[existing] = candidate;
            }
        }

        public List<RotatedTextCluster> BuildClusters() => RotatedTextClusterer.BuildClusters(_items);
    }

    /// <summary>
    /// Whether two sightings are the same physical word, measured in the word's own direction:
    /// about the same angle, centers within ~half a glyph height ACROSS the text, and ALONG it
    /// within half the longer reading's length - so a fragment read from a clipped frame ("ОО",
    /// "ир") still counts as the word it's part of ("ООО", "Иркутск"), while the next word on the
    /// same line (further along than that) doesn't. Not axis-aligned box overlap: two neighboring
    /// lines of a stamp set at 35° have page-aligned boxes overlapping by well over half, which
    /// merged "«РОМАШКА»" and the "3801234567" line below it into one word.
    /// </summary>
    private static bool Overlaps(Observation a, Observation b)
    {
        if (AngleDelta(a.AngleDegrees, b.AngleDegrees) >= 15)
            return false;

        var longer = a.TextLength >= b.TextLength ? a : b;
        var rad = longer.AngleDegrees * (MathF.PI / 180f);
        var dir = new Vector2(MathF.Cos(rad), MathF.Sin(rad));
        var normal = new Vector2(-dir.Y, dir.X);

        var delta = new Vector2((a.MinX + a.MaxX - b.MinX - b.MaxX) / 2f, (a.MinY + a.MaxY - b.MinY - b.MaxY) / 2f);
        var height = Math.Max(1f, Math.Min(a.TextHeight, b.TextHeight));
        return Math.Abs(Vector2.Dot(delta, normal)) < 0.6f * height
            && Math.Abs(Vector2.Dot(delta, dir)) <= longer.TextLength / 2f;
    }

    /// <summary>
    /// Groups observations by proximity (union-find over a "close enough to be the same
    /// stamp/block" graph), one cluster per connected group. A cluster whose words all sit at
    /// about one angle is laid out as straight lines (<see cref="TryBuildStraightCluster"/>);
    /// otherwise its words are ordered by angular position around the cluster's own centroid - a
    /// natural, parameter-free approximation of reading order around a circular stamp, without
    /// needing the word-to-line assembler to solve it. Single-word groups are dropped as more
    /// likely a stray misread than genuinely rotated content worth its own text box.
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

            // The font size has to come from the glyphs' own height along their tilt, not the
            // axis-aligned box: that grows toward the word's length as it rotates, and gave the
            // exported stamp text a font several times too large.
            var wordHeights = words.Select(w => (double)w.TextHeight).Where(h => h > 0).ToList();
            var typicalWordHeight = wordHeights.Count > 0 ? (float)Median(wordHeights) : 20f;

            if (TryBuildStraightCluster(words, typicalWordHeight) is { } straight)
            {
                clusters.Add(straight);
                continue;
            }

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

            clusters.Add(new RotatedTextCluster(
                text, centerX, centerY, maxX - minX, maxY - minY, angle, typicalWordHeight));
        }

        return clusters;
    }

    /// <summary>
    /// How tightly the words' angles must agree (length of their mean unit direction, 1 = all
    /// identical) for the cluster to count as straight text set at one angle - a rectangular or
    /// triangular stamp put on askew - rather than text following a curve. ~0.95 allows about
    /// ±18° of per-word scatter; a round stamp's words span far more.
    /// </summary>
    private const double StraightAngleCoherence = 0.95;

    /// <summary>
    /// Straight rotated text (all words at about one angle): lay its words out as ordinary lines
    /// in the cluster's own rotated frame - lines by position across the text direction, words in
    /// a line by position along it - and size the box in that frame too. The circular reading
    /// order (angle around the centroid) and the page-aligned footprint used for curved clusters
    /// would scramble such a block's lines and make its box the wrong shape. Returns null for a
    /// curved cluster.
    /// </summary>
    private static RotatedTextCluster? TryBuildStraightCluster(List<Observation> words, float typicalWordHeight)
    {
        var sum = Vector2.Zero;
        foreach (var w in words)
        {
            var rad = w.AngleDegrees * (MathF.PI / 180f);
            sum += new Vector2(MathF.Cos(rad), MathF.Sin(rad));
        }

        if (sum.Length() / words.Count < StraightAngleCoherence)
            return null;

        var dir = Vector2.Normalize(sum);
        var normal = new Vector2(-dir.Y, dir.X);
        var origin = new Vector2(words.Average(w => (w.MinX + w.MaxX) / 2f), words.Average(w => (w.MinY + w.MaxY) / 2f));

        var placed = words
            .Select(w =>
            {
                var c = new Vector2((w.MinX + w.MaxX) / 2f, (w.MinY + w.MaxY) / 2f) - origin;
                return (Word: w, U: Vector2.Dot(c, dir), V: Vector2.Dot(c, normal));
            })
            .OrderBy(p => p.V)
            .ToList();

        var lines = new List<List<(Observation Word, float U, float V)>>();
        foreach (var p in placed)
        {
            if (lines.Count > 0 && p.V - lines[^1][0].V <= 0.6f * typicalWordHeight)
                lines[^1].Add(p);
            else
                lines.Add([p]);
        }

        var text = string.Join('\n', lines.Select(line => string.Join(" ", DropFragments(line).OrderBy(p => p.U).Select(p => p.Word.Text))));

        var uMin = placed.Min(p => p.U - p.Word.TextLength / 2f);
        var uMax = placed.Max(p => p.U + p.Word.TextLength / 2f);
        var vMin = placed.Min(p => p.V - p.Word.TextHeight / 2f);
        var vMax = placed.Max(p => p.V + p.Word.TextHeight / 2f);
        var center = origin + dir * ((uMin + uMax) / 2f) + normal * ((vMin + vMax) / 2f);
        var angle = MathF.Atan2(dir.Y, dir.X) * (180f / MathF.PI);

        return new RotatedTextCluster(text, center.X, center.Y, uMax - uMin, vMax - vMin, angle, typicalWordHeight);
    }

    /// <summary>
    /// Drops a word that is just a clipped reading of a longer word on the same line - its text
    /// contained in the longer one's (ignoring case) and its extent along the line overlapping it.
    /// Fragments read with a wobbly angle can slip past ObservationSet's same-word check ("ир"
    /// next to "Иркутск" on a test stamp); within one assembled line the evidence is clearer.
    /// </summary>
    private static List<(Observation Word, float U, float V)> DropFragments(List<(Observation Word, float U, float V)> line)
        => line.Where(p => !line.Any(q =>
                !ReferenceEquals(q.Word, p.Word)
                && q.Word.Text.Length > p.Word.Text.Length
                && q.Word.Text.Contains(p.Word.Text.Trim(), StringComparison.OrdinalIgnoreCase)
                && Math.Abs(q.U - p.U) < (q.Word.TextLength + p.Word.TextLength) / 2f))
            .ToList();

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

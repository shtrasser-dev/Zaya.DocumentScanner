using Zaya.DocumentScanner.Impl.Shared.Models;

namespace Zaya.DocumentScanner.Impl.Shared.Services.Impl;

/// <summary>
/// A grid of scanned lines: <see cref="Rows"/>[row][column] holds the lines of one cell (often
/// one, sometimes a wrapped continuation too, empty when that column has nothing in that row).
/// </summary>
/// <param name="ColumnWidthFractions">Each column's share of the table's width, summing to 1.</param>
public sealed record TextTable(
    IReadOnlyList<float> ColumnWidthFractions,
    IReadOnlyList<IReadOnlyList<IReadOnlyList<ScannedLine>>> Rows);

/// <summary>
/// Turns a block of lines sitting in side-by-side columns into a row/column grid, so a 3-4 column
/// letterhead (labels | values | address box | clinic block) or a two-column exercise table
/// comes out with each source row's cells side by side - instead of each column stacked as one
/// tall cell, where every line of every sub-column took its own row and the shorter columns
/// drifted further and further down relative to the rest.
/// </summary>
public static class TableLayout
{
    private const int MaxColumnSplitDepth = 4;

    /// <summary>
    /// Columns of a paragraph group, left to right, found by applying
    /// <see cref="DocumentLineGrouping.TryDetectTwoColumns"/> recursively to each side of every
    /// split it finds - the same evidence rule (two or more lines on each side, non-overlapping
    /// extents) at every level, so a sub-split only happens where that rule holds again. Lines
    /// that straddle a split at any level are returned in <paramref name="ambiguous"/>.
    /// </summary>
    public static List<List<ScannedLine>> DetectColumns(IReadOnlyList<ScannedLine> group, out List<ScannedLine> ambiguous)
    {
        ambiguous = [];
        var columns = new List<List<ScannedLine>>();
        Split(group, 0, columns, ambiguous);
        return columns.OrderBy(c => c.Min(l => l.MinX)).ToList();
    }

    private static void Split(IReadOnlyList<ScannedLine> lines, int depth, List<List<ScannedLine>> columns, List<ScannedLine> ambiguous)
    {
        if (depth < MaxColumnSplitDepth
            && DocumentLineGrouping.TryDetectTwoColumns(lines, out var left, out var right, out var straddling))
        {
            ambiguous.AddRange(straddling);
            Split(left, depth + 1, columns, ambiguous);
            Split(right, depth + 1, columns, ambiguous);
            return;
        }

        if (lines.Count > 0)
            columns.Add(lines.ToList());
    }

    /// <summary>
    /// Lays the given columns' lines out into rows, using each line's vertical position corrected
    /// for the page's own tilt: on a rotated photo a value to the right of its label sits
    /// measurably higher than the label (11-18px on a real letterhead), which a plain
    /// top-to-bottom order reads as belonging to the row above.
    /// <para/>
    /// Rows are anchored on one column - the leftmost one holding at least half as many lines as
    /// the fullest column (a letterhead's labels, an exercise table's left side): each of its
    /// lines starts a row, except a line starting in lowercase, which continues the row above (a
    /// wrapped cell). Every other column's line goes to the row whose band it falls in, the band
    /// starting half a line pitch above its anchor - so a cell keeps all its lines, a wrapped
    /// second line included, instead of a greedy nearest-row match pairing the offset lines of a
    /// side-by-side box with the wrong labels.
    /// <para/>
    /// <paramref name="pageSlope"/> is the whole page's tilt (see <see cref="EstimatePageTilt"/>),
    /// not this block's own: a letterhead's longest lines can be a rubber stamp's, set at its own
    /// angle, which put every value one row above its label on a real scan.
    /// </summary>
    public static TextTable BuildTable(IReadOnlyList<IReadOnlyList<ScannedLine>> columns, float pageSlope)
    {
        var lineHeight = TypicalWordHeight(columns.SelectMany(c => c).ToList());
        var slope = pageSlope;
        var pitch = EstimateRowPitch(columns, slope, lineHeight);

        var placed = columns
            .Select(col => col.Select(line => (Line: line, Y: CorrectedY(line, slope))).OrderBy(p => p.Y).ToList())
            .ToList();

        var maxCount = placed.Max(c => c.Count);
        var anchor = placed.FindIndex(c => 2 * c.Count >= maxCount);

        var rows = new List<List<ScannedLine>[]>();
        var rowStarts = new List<float>();
        foreach (var (line, y) in placed[anchor])
        {
            if (rows.Count > 0 && StartsLowercase(line.Text))
            {
                rows[^1][anchor].Add(line);
                continue;
            }

            var cells = Enumerable.Range(0, columns.Count).Select(_ => new List<ScannedLine>()).ToArray();
            cells[anchor].Add(line);
            rows.Add(cells);
            rowStarts.Add(y - 0.5f * pitch);
        }

        for (var c = 0; c < placed.Count; c++)
        {
            if (c == anchor)
                continue;

            foreach (var (line, y) in placed[c])
            {
                var row = 0;
                while (row + 1 < rowStarts.Count && rowStarts[row + 1] <= y)
                    row++;
                rows[row][c].Add(line);
            }
        }

        return new TextTable(
            ColumnWidthFractions(columns),
            rows.Select(cells => (IReadOnlyList<IReadOnlyList<ScannedLine>>)cells.Select(c => (IReadOnlyList<ScannedLine>)c).ToList()).ToList());
    }

    /// <summary>
    /// Each column's share of the table width: column extents meet halfway across the gap
    /// between neighbors, so the layout keeps roughly the source's proportions.
    /// </summary>
    private static List<float> ColumnWidthFractions(IReadOnlyList<IReadOnlyList<ScannedLine>> columns)
    {
        var minX = columns.Select(c => c.Min(l => l.MinX)).ToList();
        var maxX = columns.Select(c => c.Max(l => l.MaxX)).ToList();
        var bounds = new List<float> { minX[0] };
        for (var i = 1; i < columns.Count; i++)
            bounds.Add((Math.Max(bounds[^1], maxX[i - 1]) + minX[i]) / 2f);
        bounds.Add(Math.Max(bounds[^1] + 1, maxX[^1]));

        var total = bounds[^1] - bounds[0];
        var widths = Enumerable.Range(0, columns.Count)
            .Select(i => Math.Max(0.05f, (bounds[i + 1] - bounds[i]) / total))
            .ToList();
        var sum = widths.Sum();
        return widths.Select(w => w / sum).ToList();
    }

    /// <summary>The page's tilt as dy/dx (see <see cref="EstimateTiltSlope"/>), over all of its lines.</summary>
    public static float EstimatePageTilt(IReadOnlyList<ScannedLine> lines)
        => EstimateTiltSlope(lines, TypicalWordHeight(lines));

    /// <summary>
    /// The page's tilt as dy/dx, from the lines' own words: the median over lines long enough
    /// (several word-heights wide) to give a meaningful slope through their word centers.
    /// </summary>
    private static float EstimateTiltSlope(IReadOnlyList<ScannedLine> lines, float lineHeight)
    {
        var slopes = new List<float>();
        foreach (var line in lines)
        {
            if (line.Words.Count < 2 || line.MaxX - line.MinX < 3 * lineHeight)
                continue;

            var xs = line.Words.Select(w => (w.MinX + w.MaxX) / 2f).ToList();
            var ys = line.Words.Select(w => (w.MinY + w.MaxY) / 2f).ToList();
            var mx = xs.Average();
            var my = ys.Average();
            var sxx = xs.Sum(x => (x - mx) * (x - mx));
            if (sxx <= 0)
                continue;
            slopes.Add(xs.Zip(ys, (x, y) => (x - mx) * (y - my)).Sum() / sxx);
        }

        return slopes.Count > 0 ? Math.Clamp(MathHelpers.Median(slopes), -0.2f, 0.2f) : 0f;
    }

    /// <summary>Vertical center of the line's leftmost word, moved along the page tilt to x = 0.</summary>
    private static float CorrectedY(ScannedLine line, float slope)
    {
        if (line.Words.Count == 0)
            return line.TopY + line.Height / 2f - slope * line.MinX;

        var first = line.Words.MinBy(w => w.MinX)!;
        return (first.MinY + first.MaxY) / 2f - slope * first.MinX;
    }

    /// <summary>Typical spacing between consecutive lines within a column, tilt-corrected.</summary>
    private static float EstimateRowPitch(IReadOnlyList<IReadOnlyList<ScannedLine>> columns, float slope, float lineHeight)
    {
        var gaps = new List<float>();
        foreach (var column in columns)
        {
            var ys = column.Select(l => CorrectedY(l, slope)).OrderBy(y => y).ToList();
            for (var i = 1; i < ys.Count; i++)
            {
                var gap = ys[i] - ys[i - 1];
                if (gap > 0.3f * lineHeight)
                    gaps.Add(gap);
            }
        }

        return gaps.Count > 0 ? MathHelpers.Median(gaps) : 1.5f * lineHeight;
    }

    private static float TypicalWordHeight(IReadOnlyList<ScannedLine> lines)
    {
        var heights = lines.SelectMany(l => l.Words).Select(w => w.TextHeight).Where(h => h > 0).ToList();
        if (heights.Count > 0)
            return MathHelpers.Median(heights);
        var lineHeights = lines.Select(l => l.Height).Where(h => h > 0).ToList();
        return lineHeights.Count > 0 ? MathHelpers.Median(lineHeights) : 20f;
    }

    /// <summary>True only when the very first visible character is a lowercase letter - "№ карты:" is not a continuation.</summary>
    private static bool StartsLowercase(string text)
    {
        var trimmed = text.TrimStart();
        return trimmed.Length > 0 && char.IsLower(trimmed[0]);
    }
}

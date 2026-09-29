using Zaya.Primitives;

namespace Zaya.DocumentScanner.Impl.Shared.Services.Impl;

/// <summary>
/// Measures how much of a word's own bounding box is "ink" (dark) vs "paper" (light), as a proxy
/// for font weight — validated against a real scan: individual bold words cluster distinctly
/// denser than regular ones, though only at word granularity (a whole line's box dilutes the
/// signal with inter-word gaps, so this must be measured per word, not per line). Works directly
/// off the raw page pixels so it doesn't depend on OCR reporting style/weight metadata, which
/// none of the engines in this ecosystem do.
/// </summary>
internal static class InkDensityEstimator
{
    /// <summary>Otsu's method on the whole page's luminance histogram — the ink/paper boundary.</summary>
    public static double ComputePageInkThreshold(IRawImage page)
    {
        var histogram = new int[256];
        var data = page.GetPixelData();
        var bytesPerPixel = page.Format.BytesPerPixel;

        for (var y = 0; y < page.Height; y += 2)
        {
            var rowStart = y * page.Stride;
            for (var x = 0; x < page.Width; x += 2)
            {
                var offset = rowStart + x * bytesPerPixel;
                if (offset + bytesPerPixel > data.Length)
                    continue;
                histogram[Math.Clamp((int)Luminance(data, offset, bytesPerPixel), 0, 255)]++;
            }
        }

        return OtsuFromHistogram(histogram);
    }

    /// <summary>Fraction of pixels inside the box darker than <paramref name="inkThreshold"/>.</summary>
    public static double MeasureDensity(IRawImage page, float minX, float minY, float maxX, float maxY, double inkThreshold)
    {
        var data = page.GetPixelData();
        var bytesPerPixel = page.Format.BytesPerPixel;
        var x0 = Math.Max(0, (int)minX);
        var y0 = Math.Max(0, (int)minY);
        var x1 = Math.Min(page.Width - 1, (int)maxX);
        var y1 = Math.Min(page.Height - 1, (int)maxY);

        long dark = 0, total = 0;
        for (var y = y0; y <= y1; y++)
        {
            var rowStart = y * page.Stride;
            for (var x = x0; x <= x1; x++)
            {
                var offset = rowStart + x * bytesPerPixel;
                if (offset + bytesPerPixel > data.Length)
                    continue;
                total++;
                if (Luminance(data, offset, bytesPerPixel) < inkThreshold)
                    dark++;
            }
        }

        return total == 0 ? 0 : (double)dark / total;
    }

    /// <summary>
    /// Generic Otsu split on arbitrary values (not 0-255 pixels) — used to find the bold/regular
    /// density cutoff from the document's own words, rather than a fixed constant that wouldn't
    /// generalize across different scans, fonts or lighting.
    /// </summary>
    public static double OtsuSplit(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
            return 0;

        var min = values.Min();
        var max = values.Max();
        if (max <= min)
            return min;

        const int bins = 256;
        var histogram = new int[bins];
        foreach (var v in values)
            histogram[Math.Clamp((int)((v - min) / (max - min) * (bins - 1)), 0, bins - 1)]++;

        var binThreshold = OtsuFromHistogram(histogram);
        return min + (max - min) * binThreshold / (bins - 1);
    }

    private static double Luminance(ReadOnlySpan<byte> data, int offset, int bytesPerPixel)
    {
        // Bgra32/Bgr24 layout: B,G,R[,A]. Gray8: single byte already is the luminance.
        if (bytesPerPixel == 1)
            return data[offset];

        var b = data[offset];
        var g = data[offset + 1];
        var r = data[offset + 2];
        return 0.299 * r + 0.587 * g + 0.114 * b;
    }

    private static double OtsuFromHistogram(int[] histogram)
    {
        var total = histogram.Sum();
        if (total == 0)
            return 128;

        var sumAll = 0.0;
        for (var i = 0; i < histogram.Length; i++)
            sumAll += i * (double)histogram[i];

        double sumBackground = 0;
        long weightBackground = 0;
        var bestVariance = 0.0;
        var bestThreshold = 128.0;

        for (var t = 0; t < histogram.Length; t++)
        {
            weightBackground += histogram[t];
            if (weightBackground == 0)
                continue;
            var weightForeground = total - weightBackground;
            if (weightForeground == 0)
                break;

            sumBackground += t * (double)histogram[t];
            var meanBackground = sumBackground / weightBackground;
            var meanForeground = (sumAll - sumBackground) / weightForeground;

            var variance = (double)weightBackground * weightForeground * Math.Pow(meanBackground - meanForeground, 2);
            if (variance <= bestVariance)
                continue;
            bestVariance = variance;
            bestThreshold = t;
        }

        return bestThreshold;
    }
}

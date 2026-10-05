using Zaya.DocumentScanner.Impl.Shared.Models;

namespace Zaya.DocumentScanner.Impl.Shared.Services.Impl;

/// <summary>
/// Decides one page's font sizes from its words' own measured heights, instead of per paragraph
/// from line heights. A line's height is the layout engine's stabilized display rail (inherited
/// frame to frame, frozen on an unchanged reading - see LineRailSnapper), not a glyph measurement:
/// on a real scan it ranged 20-41px across lines whose words all measured 39-43px. Taken per
/// paragraph, that noise came out as 6-13pt for text set in one size.
/// <para/>
/// Three steps:
/// <list type="number">
/// <item>Normalize each word's height for its letters. A word with no ascenders/descenders
/// ("клюв") can measure lower than one with both ("Ребята") at the same size. Each word is
/// classed by its letters (x-height only / ascender / descender / both) and scaled to the "both"
/// class, with the per-class ratios measured from this page itself rather than assumed for some
/// font. On real scans (once clipped readings are excluded - see DocumentScanService's
/// CleanWordHeights) OneOCR's boxes turned out only loosely glyph-fitted: the class ratios came
/// out at just 3-9%, so most of the robustness comes from the per-line assignment below.</item>
/// <item>Cluster the normalized heights (log scale, weighted by letter count) into the few sizes
/// documents are actually set in: split a cluster in two only when the halves differ by a real
/// font-size step and are well separated relative to their own spread.</item>
/// <item>Assign a size per LINE (weighted median of its words), not per word, so words of one
/// size never alternate between sizes across a cluster boundary. A word only gets a different
/// size than its line when it, together with its neighbors, clearly belongs to another cluster -
/// e.g. two differently sized fragments OCR merged into one line.</item>
/// </list>
/// </summary>
public static class FontSizeEstimator
{
    /// <summary>
    /// Smallest ratio between two clusters' heights that counts as two different font sizes -
    /// close to the smallest common step (11 vs 12pt is ~1.09, 10 vs 12pt is 1.2), but above what
    /// per-word measurement noise and a photo's perspective (bottom of the page nearer the camera)
    /// leave after letter normalization.
    /// </summary>
    private const double MinClusterRatio = 1.15;

    /// <summary>
    /// Minimum distance between two halves' means in units of their pooled spread. Even a single
    /// unimodal size gets split by the best-split search (halves of a normal distribution sit
    /// ~2.7 spreads apart), so a split has to clear that comfortably to be a real second size.
    /// </summary>
    private const double MinClusterSeparation = 3.5;

    /// <summary>Smallest cluster, in letters - enough for a one-word heading, not for one misread box.</summary>
    private const double MinClusterLetters = 8;

    private const int MaxClusters = 4;

    /// <summary>A long word measures more reliably than a short one, but shouldn't outvote a whole line alone.</summary>
    private const int MaxWordWeight = 12;

    /// <summary>Words needed in a letter class (and in the reference class) to measure its ratio from the page.</summary>
    private const int MinCalibrationWords = 4;

    /// <summary>Words shorter than this never pull their own size away from their line's.</summary>
    private const int MinOverrideWordLetters = 3;

    /// <summary>Letters needed in a run of consecutive off-size words before it gets its own size.</summary>
    private const int MinOverrideRunLetters = 6;

    /// <summary>
    /// How far past the midpoint between its line's cluster and another one a word's height must
    /// be (as a fraction of the distance between the two centers) to count as that other size -
    /// the hysteresis that keeps borderline words on their line's size.
    /// </summary>
    private const double OverrideMarginFraction = 0.25;

    private enum LetterProfile { XHeight = 0, Ascender = 1, Descender = 2, Full = 3 }

    /// <summary>
    /// Fallback log-ratios of each class's height to the Full class, when the page has too few
    /// words of a class to measure it. Rounded from what three real scans measured (x-height
    /// 1.03-1.12, ascender 1.03-1.09, descender 1.00-1.07) - small, because OneOCR's word boxes
    /// are only loosely fitted to the glyphs.
    /// </summary>
    private static readonly double[] DefaultProfileOffsets = [Math.Log(1.08), Math.Log(1.05), Math.Log(1.04), 0];

    private const string AscenderLetters = "бёйbdfhklt" + "0123456789" + "()[]{}/\\|!?\"'№%&@#$";
    private const string DescenderLetters = "руфдцщДЦЩgjpqy" + "()[]{}/\\|,;";

    private sealed record Sample(ScannedWord Word, int Letters, double Weight, double Value);

    public static PageFontSizes Estimate(IReadOnlyList<ScannedLine> lines, double pixelsPerPoint, float minPt, float maxPt)
    {
        var raw = new List<(ScannedWord Word, int Letters, LetterProfile Profile, double LogHeight)>();
        foreach (var line in lines)
        {
            foreach (var word in line.Words)
            {
                var letters = word.Text.Count(char.IsLetterOrDigit);
                if (letters == 0 || word.TextHeight <= 0)
                    continue;
                raw.Add((word, letters, Classify(word.Text), Math.Log(word.TextHeight)));
            }
        }

        if (raw.Count == 0 || pixelsPerPoint <= 0)
        {
            var fallback = PageLayoutEstimator.EstimateFontSizePoints(lines, pixelsPerPoint, minPt, maxPt);
            return new PageFontSizes(
                new Dictionary<ScannedWord, float>(), new Dictionary<ScannedLine, float>(), [fallback], fallback);
        }

        var offsets = MeasureProfileOffsets(raw.Select(r => (r.Letters, r.Profile, r.LogHeight)).ToList());
        var samples = raw
            .Select(r => new Sample(r.Word, r.Letters, Math.Min(r.Letters, MaxWordWeight), r.LogHeight + offsets[(int)r.Profile]))
            .ToList();

        var centers = Cluster(samples);
        var clusterPt = centers
            .Select(c => Math.Clamp((float)(Math.Exp(c) / pixelsPerPoint), minPt, maxPt))
            .ToList();
        var dominant = DominantCluster(samples, centers);

        var wordPt = new Dictionary<ScannedWord, float>(ReferenceEqualityComparer.Instance);
        var linePt = new Dictionary<ScannedLine, float>(ReferenceEqualityComparer.Instance);
        var byWord = new Dictionary<ScannedWord, Sample>(ReferenceEqualityComparer.Instance);
        foreach (var s in samples)
            byWord[s.Word] = s;

        foreach (var line in lines)
        {
            var lineSamples = line.Words.Where(byWord.ContainsKey).Select(w => byWord[w]).ToList();
            var lineCluster = lineSamples.Count > 0
                ? Nearest(centers, WeightedMedian(lineSamples))
                : dominant;
            linePt[line] = clusterPt[lineCluster];

            var wordClusters = AssignWords(line.Words, byWord, centers, lineCluster);
            for (var i = 0; i < line.Words.Count; i++)
                wordPt[line.Words[i]] = clusterPt[wordClusters[i]];
        }

        return new PageFontSizes(wordPt, linePt, clusterPt, clusterPt[dominant]);
    }

    private static LetterProfile Classify(string text)
    {
        var up = false;
        var down = false;
        foreach (var c in text)
        {
            if (char.IsUpper(c) || AscenderLetters.Contains(c))
                up = true;
            if (DescenderLetters.Contains(c))
                down = true;
        }

        return (up, down) switch
        {
            (true, true) => LetterProfile.Full,
            (true, false) => LetterProfile.Ascender,
            (false, true) => LetterProfile.Descender,
            _ => LetterProfile.XHeight,
        };
    }

    /// <summary>
    /// Log-ratio of each letter class's typical height to the Full class's, measured on this page
    /// (median over words of 2+ letters, so stray single glyphs don't skew it) when both classes
    /// have enough words, else <see cref="DefaultProfileOffsets"/>. Medians, so a page's body text
    /// - the bulk of every class - sets the ratio, not a heading in one class.
    /// </summary>
    private static double[] MeasureProfileOffsets(IReadOnlyList<(int Letters, LetterProfile Profile, double LogHeight)> raw)
    {
        var offsets = (double[])DefaultProfileOffsets.Clone();
        var byProfile = raw
            .Where(r => r.Letters >= 2)
            .GroupBy(r => r.Profile)
            .ToDictionary(g => g.Key, g => g.Select(r => r.LogHeight).ToList());

        if (!byProfile.TryGetValue(LetterProfile.Full, out var full) || full.Count < MinCalibrationWords)
            return offsets;

        var fullMedian = Median(full);
        foreach (var (profile, heights) in byProfile)
        {
            if (profile == LetterProfile.Full || heights.Count < MinCalibrationWords)
                continue;
            offsets[(int)profile] = Math.Clamp(fullMedian - Median(heights), 0, Math.Log(2));
        }

        return offsets;
    }

    /// <summary>
    /// Top-down 1D clustering: start with every word in one cluster, repeatedly apply the most
    /// clearly separated acceptable best split (weighted Otsu - maximum between-class variance)
    /// of any cluster. Returns cluster centers (weighted medians, log px), ascending.
    /// </summary>
    private static List<double> Cluster(List<Sample> samples)
    {
        var clusters = new List<List<Sample>> { samples.OrderBy(s => s.Value).ToList() };
        while (clusters.Count < MaxClusters)
        {
            var bestIndex = -1;
            var bestSplit = 0;
            var bestSeparation = 0.0;
            for (var c = 0; c < clusters.Count; c++)
            {
                if (TryBestSplit(clusters[c], out var split, out var separation) && separation > bestSeparation)
                {
                    bestIndex = c;
                    bestSplit = split;
                    bestSeparation = separation;
                }
            }

            if (bestIndex < 0)
                break;

            var cluster = clusters[bestIndex];
            clusters[bestIndex] = cluster.Take(bestSplit).ToList();
            clusters.Insert(bestIndex + 1, cluster.Skip(bestSplit).ToList());
        }

        return clusters.Select(WeightedMedian).OrderBy(v => v).ToList();
    }

    /// <summary>Best split of a value-sorted cluster, if it passes the size-step, separation and minimum-size checks.</summary>
    private static bool TryBestSplit(List<Sample> sorted, out int split, out double separation)
    {
        split = -1;
        separation = 0;
        var n = sorted.Count;
        if (n < 2)
            return false;

        var w = new double[n + 1];
        var wv = new double[n + 1];
        var wv2 = new double[n + 1];
        for (var i = 0; i < n; i++)
        {
            var s = sorted[i];
            w[i + 1] = w[i] + s.Weight;
            wv[i + 1] = wv[i] + s.Weight * s.Value;
            wv2[i + 1] = wv2[i] + s.Weight * s.Value * s.Value;
        }

        var bestScore = 0.0;
        for (var i = 1; i < n; i++)
        {
            if (sorted[i].Value <= sorted[i - 1].Value)
                continue;
            var w1 = w[i];
            var w2 = w[n] - w[i];
            var mu1 = wv[i] / w1;
            var mu2 = (wv[n] - wv[i]) / w2;
            var score = w1 * w2 * (mu2 - mu1) * (mu2 - mu1);
            if (score > bestScore)
            {
                bestScore = score;
                split = i;
            }
        }

        if (split < 0)
            return false;

        var leftW = w[split];
        var rightW = w[n] - w[split];
        if (leftW < MinClusterLetters || rightW < MinClusterLetters)
            return false;

        var leftMu = wv[split] / leftW;
        var rightMu = (wv[n] - wv[split]) / rightW;
        var leftVar = Math.Max(0, wv2[split] / leftW - leftMu * leftMu);
        var rightVar = Math.Max(0, (wv2[n] - wv2[split]) / rightW - rightMu * rightMu);
        // Floored at ~1% so two perfectly uniform halves don't read as infinitely separated.
        var pooledSd = Math.Max(0.01, Math.Sqrt((leftW * leftVar + rightW * rightVar) / (leftW + rightW)));

        separation = (rightMu - leftMu) / pooledSd;
        return Math.Exp(rightMu - leftMu) >= MinClusterRatio && separation >= MinClusterSeparation;
    }

    /// <summary>
    /// Per-word cluster for one line: the line's own cluster, except for runs of consecutive
    /// words that each sit clearly on another cluster's side (see <see cref="OverrideMarginFraction"/>)
    /// and together have enough letters to be trusted. Unmeasured words (punctuation) and short
    /// words neither start nor break a run; inside an accepted run they follow it.
    /// </summary>
    private static int[] AssignWords(
        IReadOnlyList<ScannedWord> words, Dictionary<ScannedWord, Sample> byWord, List<double> centers, int lineCluster)
    {
        var result = Enumerable.Repeat(lineCluster, words.Count).ToArray();
        if (centers.Count < 2)
            return result;

        // Per word: another cluster it clearly belongs to, lineCluster when it clearly doesn't, null when neutral.
        var candidate = new int?[words.Count];
        for (var i = 0; i < words.Count; i++)
        {
            if (!byWord.TryGetValue(words[i], out var s) || s.Letters < MinOverrideWordLetters)
                continue;

            var k = Nearest(centers, s.Value);
            var gap = Math.Abs(centers[k] - centers[lineCluster]);
            var clearlyOther = k != lineCluster
                && Math.Abs(s.Value - centers[lineCluster]) - Math.Abs(s.Value - centers[k]) >= 2 * OverrideMarginFraction * gap;
            candidate[i] = clearlyOther ? k : lineCluster;
        }

        var start = 0;
        while (start < words.Count)
        {
            if (candidate[start] is not { } k || k == lineCluster)
            {
                start++;
                continue;
            }

            var end = start;
            var letters = 0;
            var lettersToLastMatch = 0;
            var lastMatch = start;
            for (var i = start; i < words.Count; i++)
            {
                if (candidate[i] is { } c && c != k)
                    break;
                end = i;
                letters += byWord.TryGetValue(words[i], out var s) ? s.Letters : 0;
                if (candidate[i] == k)
                {
                    lastMatch = i;
                    lettersToLastMatch = letters;
                }
            }

            if (lettersToLastMatch >= MinOverrideRunLetters)
            {
                for (var i = start; i <= lastMatch; i++)
                    result[i] = k;
            }

            start = end + 1;
        }

        return result;
    }

    private static int DominantCluster(List<Sample> samples, List<double> centers)
    {
        var weights = new double[centers.Count];
        foreach (var s in samples)
            weights[Nearest(centers, s.Value)] += s.Weight;
        return Array.IndexOf(weights, weights.Max());
    }

    private static int Nearest(List<double> centers, double value)
    {
        var best = 0;
        for (var i = 1; i < centers.Count; i++)
        {
            if (Math.Abs(centers[i] - value) < Math.Abs(centers[best] - value))
                best = i;
        }

        return best;
    }

    private static double WeightedMedian(List<Sample> samples)
    {
        var sorted = samples.OrderBy(s => s.Value).ToList();
        var half = sorted.Sum(s => s.Weight) / 2;
        var acc = 0.0;
        foreach (var s in sorted)
        {
            acc += s.Weight;
            if (acc >= half)
                return s.Value;
        }

        return sorted[^1].Value;
    }

    private static double Median(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2 : sorted[mid];
    }
}

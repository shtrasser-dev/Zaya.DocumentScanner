using System.Numerics;
using Zaya.DocumentScanner.Impl.Shared.Models;
using Zaya.OCR.Services;
using Zaya.Primitives;

namespace Zaya.DocumentScanner.Impl.Shared.Services.Impl;

public sealed class DocumentScanService : IDocumentScanService
{
    private readonly IOCRService _ocr;
    private readonly ITextLayoutService _layout;

    public DocumentScanService(IOCRService ocr, ITextLayoutService layout)
    {
        _ocr = ocr;
        _layout = layout;
    }

    public async Task<IReadOnlyList<ScannedLine>> ScanAsync(
        IRawImage page,
        DocumentScanOptions options,
        CancellationToken cancellationToken = default)
    {
        var windowHeight = options.WindowHeight ?? Math.Max(1, page.Height / 4);

        using var ocrSession = await _ocr.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
        using var layoutSession = await _layout.CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        var tracked = new Dictionary<Guid, TrackedLine>();
        var committed = new List<TrackedLine>();

        for (var y = 0; y + windowHeight <= page.Height; y += options.Step)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var crop = RawImageCropper.CropRows(page, y, windowHeight);
            var ocrResult = await ocrSession.RecognizeAsync(crop, cancellationToken).ConfigureAwait(false);

            var trusted = TrustCenter.Filter(ocrResult, windowHeight, options.MarginMultiplier);
            var offset = new OffsetOcrResult(trusted, ocrResult.Confidence, new Vector2(0, y));
            var textResult = await layoutSession.ProcessAsync(offset, cancellationToken).ConfigureAwait(false);

            Accumulate(textResult.Paragraphs.SelectMany(p => p.Lines), tracked, committed, options.MissingFrameGrace);
        }

        committed.AddRange(tracked.Values);
        return Dedupe(committed);
    }

    /// <summary>
    /// Updates the tracked-line table with this frame's lines (keeping the longest reading seen
    /// across each line's lifetime — a tilted source line can lose trailing words to the
    /// trust-center margin one frame before it exits the window, and a plain "latest wins"
    /// update would let that one bad frame clobber many good preceding reads), then commits and
    /// drops any previously tracked line that didn't reappear this frame for long enough.
    /// </summary>
    private static void Accumulate(
        IEnumerable<Zaya.Primitives.OCR.ITextLine> lines,
        Dictionary<Guid, TrackedLine> tracked,
        List<TrackedLine> committed,
        int missingFrameGrace)
    {
        var seenThisFrame = new HashSet<Guid>();
        foreach (var line in lines)
        {
            seenThisFrame.Add(line.Id);

            if (tracked.TryGetValue(line.Id, out var existing))
            {
                if (line.Text.Trim().Length >= existing.Text.Trim().Length)
                {
                    existing.Text = line.Text;
                    existing.MinX = line.Bounds.MinX;
                    existing.MaxX = line.Bounds.MaxX;
                    existing.Height = line.Bounds.TextHeight;
                }

                existing.TopY = line.Bounds.MinY;
                existing.MissingFrames = 0;
            }
            else
            {
                tracked[line.Id] = new TrackedLine
                {
                    Text = line.Text,
                    TopY = line.Bounds.MinY,
                    MinX = line.Bounds.MinX,
                    MaxX = line.Bounds.MaxX,
                    Height = line.Bounds.TextHeight,
                    MissingFrames = 0,
                };
            }
        }

        foreach (var (id, state) in tracked.ToList())
        {
            if (seenThisFrame.Contains(id))
                continue;

            state.MissingFrames++;
            if (state.MissingFrames <= missingFrameGrace)
                continue;

            committed.Add(state);
            tracked.Remove(id);
        }
    }

    /// <summary>
    /// Cross-frame line linking occasionally misses a hop (the layout engine only compares
    /// against the immediately preceding frame), re-identifying the same physical line as "new"
    /// a few pixels away. Neighboring, overlapping text is collapsed here, keeping the
    /// longer/more complete reading — the same rule the engine itself uses internally.
    /// </summary>
    private static IReadOnlyList<ScannedLine> Dedupe(List<TrackedLine> committed)
    {
        var ordered = committed.OrderBy(l => l.TopY).ToList();

        var deduped = new List<TrackedLine>();
        foreach (var line in ordered)
        {
            var text = line.Text.Trim();
            if (text.Length == 0)
                continue;

            if (deduped.Count > 0)
            {
                var prev = deduped[^1];
                var prevText = prev.Text.Trim();
                var closeEnough = line.TopY - prev.TopY < 60; // roughly one text line's height
                var overlapsText = text.Contains(prevText, StringComparison.Ordinal)
                    || prevText.Contains(text, StringComparison.Ordinal);
                if (closeEnough && overlapsText)
                {
                    if (text.Length > prevText.Length)
                        deduped[^1] = line;
                    continue;
                }
            }

            deduped.Add(line);
        }

        return deduped
            .Select(l => new ScannedLine(l.Text.Trim(), l.TopY, l.MinX, l.MaxX, l.Height))
            .ToList();
    }

    private sealed class TrackedLine
    {
        public required string Text { get; set; }
        public required float TopY { get; set; }
        public float MinX { get; set; }
        public float MaxX { get; set; }
        public float Height { get; set; }
        public int MissingFrames { get; set; }
    }
}

using System.Numerics;
using Zaya.Primitives;
using Zaya.Primitives.OCR;

namespace Zaya.DocumentScanner.Impl.Shared.Services.Impl;

/// <summary>
/// Wraps a word list as an <see cref="IOCRResult"/>, offsetting every bound by a fixed delta.
/// ProximityTextLayoutService links lines across calls by comparing bounds directly in whatever
/// coordinate space it's given — this converts crop-relative bounds into absolute page
/// coordinates so a physical line reports the same position regardless of which window it was
/// read from.
/// </summary>
public sealed class OffsetOcrResult : IOCRResult
{
    public OffsetOcrResult(IEnumerable<IOCRWord> words, double confidence, Vector2 offset)
    {
        Words = words.Select(w => (IOCRWord)new OffsetOcrWord(w, offset)).ToList();
        Confidence = confidence;
    }

    public IReadOnlyList<IOCRWord> Words { get; }
    public double Confidence { get; }
}

file sealed class OffsetOcrWord : IOCRWord
{
    public OffsetOcrWord(IOCRWord inner, Vector2 offset)
    {
        Text = inner.Text;
        Confidence = inner.Confidence;
        Bounds = new BoundingBox(
            inner.Bounds.P1 + offset,
            inner.Bounds.P2 + offset,
            inner.Bounds.P3 + offset,
            inner.Bounds.P4 + offset);
    }

    public string Text { get; }
    public BoundingBox Bounds { get; }
    public double Confidence { get; }
}

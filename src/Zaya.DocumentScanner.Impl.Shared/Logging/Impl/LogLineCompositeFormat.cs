using System.Diagnostics;
using System.Globalization;
using System.Text;
using Zaya.DocumentScanner.Impl.Shared.Constants;

namespace Zaya.DocumentScanner.Impl.Shared.Logging.Impl;

/// <summary>
/// Turns named log line templates into <see cref="CompositeFormat"/> for <c>string.Format</c>.
/// </summary>
/// <remarks>
/// Placeholders in <c>log.json</c>:
/// <c>{timestamp}</c> / <c>{timestamp:…}</c>, <c>{level}</c>, <c>{category}</c>,
/// <c>{message}</c>, <c>{newline}</c>, <c>{exception}</c>.
/// At compile time a known name is rewritten to an index only when followed by
/// <c>}</c> or <c>:</c> (<c>{timestamp</c> → <c>{0</c>), so format specifiers keep working.
/// </remarks>
internal static class LogLineCompositeFormat
{
    /// <summary>0 timestamp, 1 level, 2 category, 3 message, 4 newline, 5 exception.</summary>
    private static readonly (string Name, int Index)[] Placeholders =
    [
        (LogConstants.PlaceholderTimestamp, 0),
        (LogConstants.PlaceholderException, 5),
        (LogConstants.PlaceholderCategory, 2),
        (LogConstants.PlaceholderMessage, 3),
        (LogConstants.PlaceholderNewline, 4),
        (LogConstants.PlaceholderLevel, 1),
    ];

    private static readonly (string Name, int Index)[] PlaceholdersByLength =
        Placeholders.OrderByDescending(p => p.Name.Length).ToArray();

    public static CompositeFormat CompileOrDefault(string namedFormat, string defaultNamedFormat)
    {
        if (TryCompile(namedFormat, out var compiled))
            return compiled;

        Debug.WriteLine(LogConstants.InvalidFormatFallbackDebugPrefix + namedFormat);
        return Compile(defaultNamedFormat);
    }

    public static bool TryCompile(string? namedFormat, out CompositeFormat format)
    {
        if (string.IsNullOrWhiteSpace(namedFormat))
        {
            format = null!;
            return false;
        }

        try
        {
            format = Compile(namedFormat);
            return true;
        }
        catch (FormatException)
        {
            format = null!;
            return false;
        }
    }

    public static CompositeFormat Compile(string namedFormat)
        => CompositeFormat.Parse(ToIndexed(namedFormat));

    public static string ToIndexed(string namedFormat)
    {
        var result = new StringBuilder(namedFormat.Length);
        for (var i = 0; i < namedFormat.Length; i++)
        {
            var ch = namedFormat[i];
            if (ch != '{')
            {
                result.Append(ch);
                continue;
            }

            // Format escape: "{{" → literal '{'
            if (i + 1 < namedFormat.Length && namedFormat[i + 1] == '{')
            {
                result.Append(LogConstants.FormatBraceEscape);
                i++;
                continue;
            }

            if (TryMatchPlaceholder(namedFormat, i + 1, out var index, out var nameLength))
            {
                result.Append('{');
                result.Append(index.ToString(CultureInfo.InvariantCulture));
                i += nameLength; // sit on last char of name; loop advances past it
                continue;
            }

            if (TryReadUnknownName(namedFormat, i + 1, out var unknown))
            {
                throw new FormatException(
                    LogConstants.UnknownPlaceholderMessagePrefix
                    + unknown
                    + LogConstants.UnknownPlaceholderMessageSuffix);
            }

            // Likely a raw composite item ({0}, {0:…}) — leave '{' as-is.
            result.Append('{');
        }

        return result.ToString();
    }

    /// <summary>
    /// Known name at <paramref name="nameStart"/> only if the next char after the name is <c>}</c> or <c>:</c>.
    /// </summary>
    private static bool TryMatchPlaceholder(string format, int nameStart, out int index, out int nameLength)
    {
        foreach (var (name, placeholderIndex) in PlaceholdersByLength)
        {
            if (nameStart + name.Length > format.Length)
                continue;

            if (!format.AsSpan(nameStart, name.Length).Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;

            var boundary = nameStart + name.Length;
            if (boundary >= format.Length)
                continue;

            var next = format[boundary];
            if (next is not ('}' or ':'))
                continue;

            index = placeholderIndex;
            nameLength = name.Length;
            return true;
        }

        index = 0;
        nameLength = 0;
        return false;
    }

    /// <summary>
    /// Identifier-like text after <c>{</c> that ends at <c>}</c> or <c>:</c> but is not a known placeholder.
    /// </summary>
    private static bool TryReadUnknownName(string format, int nameStart, out string name)
    {
        if (nameStart >= format.Length || !IsNameStart(format[nameStart]))
        {
            name = string.Empty;
            return false;
        }

        var i = nameStart + 1;
        while (i < format.Length && IsNamePart(format[i]))
            i++;

        if (i >= format.Length || format[i] is not ('}' or ':'))
        {
            name = string.Empty;
            return false;
        }

        name = format[nameStart..i];
        return true;
    }

    private static bool IsNameStart(char c) => char.IsLetter(c) || c == '_';

    private static bool IsNamePart(char c) => char.IsLetterOrDigit(c) || c == '_';
}

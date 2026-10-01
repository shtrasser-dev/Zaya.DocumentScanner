namespace Zaya.DocumentScanner.Impl.Shared.Constants;

internal static class LogConstants
{
    public const string ConfigFileName = "log.json";
    public const string LogsFolderName = "logs";

    public const string PlaceholderTimestamp = "timestamp";
    public const string PlaceholderLevel = "level";
    public const string PlaceholderCategory = "category";
    public const string PlaceholderMessage = "message";
    public const string PlaceholderNewline = "newline";
    public const string PlaceholderException = "exception";

    /// <summary>
    /// Named placeholders with optional format: <c>{timestamp:yyyy-MM-dd HH:mm:ss.fff}</c>,
    /// <c>{level}</c>, <c>{category}</c>, <c>{message}</c>.
    /// </summary>
    public const string DefaultFileLineFormat =
        "{" + PlaceholderTimestamp + ":yyyy-MM-dd HH:mm:ss.fff} [{" + PlaceholderLevel + "}] {"
        + PlaceholderCategory + "}: {" + PlaceholderMessage + "}";

    /// <summary>
    /// Same as <see cref="DefaultFileLineFormat"/> plus <c>{newline}</c> and <c>{exception}</c>.
    /// </summary>
    public const string DefaultFileLineFormatWithException =
        DefaultFileLineFormat + "{" + PlaceholderNewline + "}{" + PlaceholderException + "}";

    public const string FormatBraceEscape = "{{";

    public const string InvalidFormatFallbackDebugPrefix =
        "[LogConfig] Invalid file line format; falling back to default. Value: ";

    public const string UnknownPlaceholderMessagePrefix = "Unknown log line placeholder '{";
    public const string UnknownPlaceholderMessageSuffix = "}'.";
}

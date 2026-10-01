using System.Text.Json.Serialization;
using Zaya.DocumentScanner.Impl.Shared.Constants;
using Mel = Microsoft.Extensions.Logging;

namespace Zaya.DocumentScanner.Impl.Shared.Logging;

/// <summary>
/// Host logging settings loaded from <c>%AppData%\Zaya\DocumentScanner\log.json</c>.
/// Getters coerce invalid values to defaults; the on-disk file is left as-is until rewritten.
/// </summary>
public sealed class LogConfig
{
    public const Mel.LogLevel DefaultLevel = Mel.LogLevel.Information;
    public const long DefaultMaxFileSizeBytes = 5 * 1024 * 1024;
    public const int DefaultMaxFileCount = 5;

    private Mel.LogLevel _level = DefaultLevel;
    private long _maxFileSizeBytes = DefaultMaxFileSizeBytes;
    private int _maxFileCount = DefaultMaxFileCount;
    private string _fileLineFormat = LogConstants.DefaultFileLineFormat;
    private string _fileLineFormatWithException = LogConstants.DefaultFileLineFormatWithException;

    /// <summary>
    /// Minimum MEL level. <see cref="Mel.LogLevel.None"/> is treated as <see cref="DefaultLevel"/>.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Mel.LogLevel Level
    {
        get => _level == Mel.LogLevel.None ? DefaultLevel : _level;
        set => _level = value;
    }

    /// <summary>
    /// When true, write to the debugger (Visual Studio Output / DebugView).
    /// </summary>
    public bool WriteToDebug { get; set; } = true;

    /// <summary>
    /// When true, write rolling files under the logs directory.
    /// </summary>
    public bool WriteToFile { get; set; } = true;

    /// <summary>
    /// Maximum size of the active log file in bytes before rotation.
    /// </summary>
    public long MaxFileSizeBytes
    {
        get => _maxFileSizeBytes > 0 ? _maxFileSizeBytes : DefaultMaxFileSizeBytes;
        set => _maxFileSizeBytes = value;
    }

    /// <summary>
    /// Maximum number of log files to keep (including the active file).
    /// </summary>
    public int MaxFileCount
    {
        get => _maxFileCount > 0 ? _maxFileCount : DefaultMaxFileCount;
        set => _maxFileCount = value;
    }

    /// <summary>
    /// Format for file lines without an exception.
    /// Placeholders: <c>{timestamp}</c> / <c>{timestamp:…}</c>, <c>{level}</c>, <c>{category}</c>, <c>{message}</c>.
    /// </summary>
    public string FileLineFormat
    {
        get => string.IsNullOrWhiteSpace(_fileLineFormat) ? LogConstants.DefaultFileLineFormat : _fileLineFormat;
        set => _fileLineFormat = value;
    }

    /// <summary>
    /// Format for file lines with an exception.
    /// Placeholders: same as <see cref="FileLineFormat"/>, plus <c>{newline}</c> and <c>{exception}</c>.
    /// </summary>
    public string FileLineFormatWithException
    {
        get => string.IsNullOrWhiteSpace(_fileLineFormatWithException)
            ? LogConstants.DefaultFileLineFormatWithException
            : _fileLineFormatWithException;
        set => _fileLineFormatWithException = value;
    }

    public static LogConfig CreateDefault() => new();
}

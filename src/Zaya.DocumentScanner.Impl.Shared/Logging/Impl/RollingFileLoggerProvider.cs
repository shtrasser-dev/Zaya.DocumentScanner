using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Zaya.DocumentScanner.Impl.Shared.Constants;

namespace Zaya.DocumentScanner.Impl.Shared.Logging.Impl;

/// <summary>
/// Simple size-based rolling file logger for the host.
/// Active file: <c>zaya.log</c>; older files: <c>zaya.1.log</c> … <c>zaya.N.log</c>.
/// </summary>
public sealed class RollingFileLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, RollingFileLogger> _loggers = new(StringComparer.Ordinal);
    private readonly RollingFileSink _sink;
    private readonly CompositeFormat _lineFormat;
    private readonly CompositeFormat _lineFormatWithException;
    private bool _disposed;

    public RollingFileLoggerProvider(string directory, LogConfig options)
    {
        Directory.CreateDirectory(directory);
        _lineFormat = LogLineCompositeFormat.CompileOrDefault(
            options.FileLineFormat,
            LogConstants.DefaultFileLineFormat);
        _lineFormatWithException = LogLineCompositeFormat.CompileOrDefault(
            options.FileLineFormatWithException,
            LogConstants.DefaultFileLineFormatWithException);
        _sink = new RollingFileSink(directory, options.MaxFileSizeBytes, options.MaxFileCount);
    }

    public ILogger CreateLogger(string categoryName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _loggers.GetOrAdd(
            categoryName,
            static (name, state) => new RollingFileLogger(name, state.Sink, state.LineFormat, state.LineFormatWithException),
            (Sink: _sink, LineFormat: _lineFormat, LineFormatWithException: _lineFormatWithException));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _loggers.Clear();
        _sink.Dispose();
    }

    private sealed class RollingFileLogger : ILogger
    {
        private readonly string _category;
        private readonly RollingFileSink _sink;
        private readonly CompositeFormat _lineFormat;
        private readonly CompositeFormat _lineFormatWithException;

        public RollingFileLogger(
            string category,
            RollingFileSink sink,
            CompositeFormat lineFormat,
            CompositeFormat lineFormatWithException)
        {
            _category = category;
            _sink = sink;
            _lineFormat = lineFormat;
            _lineFormatWithException = lineFormatWithException;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var message = formatter(state, exception);
            var format = exception is null ? _lineFormat : _lineFormatWithException;
            // 0 timestamp, 1 level, 2 category, 3 message, 4 newline, 5 exception
            var line = string.Format(
                CultureInfo.InvariantCulture,
                format,
                DateTimeOffset.Now,
                logLevel,
                _category,
                message,
                Environment.NewLine,
                exception);
            _sink.WriteLine(line);
        }
    }

    private sealed class RollingFileSink : IDisposable
    {
        private readonly string _directory;
        private readonly long _maxFileSizeBytes;
        private readonly int _maxFileCount;
        private readonly object _gate = new();
        private StreamWriter? _writer;
        private long _length;
        private bool _disposed;

        public RollingFileSink(string directory, long maxFileSizeBytes, int maxFileCount)
        {
            _directory = directory;
            _maxFileSizeBytes = Math.Max(1, maxFileSizeBytes);
            _maxFileCount = Math.Max(1, maxFileCount);
            OpenWriter();
        }

        public void WriteLine(string line)
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_writer is null)
                    OpenWriter();

                var bytes = Encoding.UTF8.GetByteCount(line) + Encoding.UTF8.GetByteCount(Environment.NewLine);
                if (_length > 0 && _length + bytes > _maxFileSizeBytes)
                    Roll();

                _writer!.WriteLine(line);
                _writer.Flush();
                _length += bytes;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                    return;
                _disposed = true;
                _writer?.Dispose();
                _writer = null;
            }
        }

        private void OpenWriter()
        {
            var path = ActivePath();
            var exists = File.Exists(path);
            _writer = new StreamWriter(
                new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read),
                Encoding.UTF8)
            {
                AutoFlush = false,
            };
            _length = exists ? new FileInfo(path).Length : 0;
        }

        private void Roll()
        {
            _writer?.Dispose();
            _writer = null;

            // Shift zaya.(n-1).log -> zaya.n.log; drop beyond maxFileCount-1 archives + active.
            var archiveSlots = Math.Max(0, _maxFileCount - 1);
            if (archiveSlots == 0)
            {
                TryDelete(ActivePath());
                OpenWriter();
                return;
            }

            TryDelete(ArchivePath(archiveSlots));
            for (var i = archiveSlots - 1; i >= 1; i--)
            {
                var from = ArchivePath(i);
                var to = ArchivePath(i + 1);
                if (File.Exists(from))
                    TryMove(from, to);
            }

            if (File.Exists(ActivePath()))
                TryMove(ActivePath(), ArchivePath(1));

            OpenWriter();
        }

        private string ActivePath() => Path.Combine(_directory, "zaya.log");

        private string ArchivePath(int index) => Path.Combine(_directory, $"zaya.{index}.log");

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // ignore locked/missing files
            }
        }

        private static void TryMove(string from, string to)
        {
            try
            {
                TryDelete(to);
                File.Move(from, to);
            }
            catch
            {
                // ignore locked files
            }
        }
    }
}

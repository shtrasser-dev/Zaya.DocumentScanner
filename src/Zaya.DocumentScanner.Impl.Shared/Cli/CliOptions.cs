namespace Zaya.DocumentScanner.Impl.Shared.Cli;

public sealed class CliArgumentException(string message) : Exception(message);

/// <summary>Parsed command-line invocation: scan one or more images headlessly and export a combined DOCX.</summary>
public sealed class CliOptions
{
    public const string Usage =
        """
        Использование:
          Zaya.DocumentScanner.exe --output <файл.docx> [--profile <имя>] [--log <файл.log>] <изображение1> [изображение2 ...]

        Параметры:
          --output <путь>   Обязательный. Путь для сохранения итогового DOCX.
          --profile <имя>   Необязательный. Имя профиля настроек - если не указан, используется текущий сохранённый профиль.
          --log <путь>      Необязательный. Файл для записи журнала выполнения.
          <изображение...>  Один или несколько путей к изображениям. Несколько файлов объединяются в один DOCX.
        """;

    public required string OutputPath { get; init; }
    public required IReadOnlyList<string> ImagePaths { get; init; }
    public string? ProfileName { get; init; }
    public string? LogPath { get; init; }

    public static CliOptions Parse(string[] args)
    {
        string? profile = null;
        string? log = null;
        string? output = null;
        var images = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--profile":
                    profile = RequireValue(args, ref i, "--profile");
                    break;
                case "--log":
                    log = RequireValue(args, ref i, "--log");
                    break;
                case "--output":
                    output = RequireValue(args, ref i, "--output");
                    break;
                default:
                    images.Add(args[i]);
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(output))
            throw new CliArgumentException("Не указан обязательный параметр --output.");
        if (images.Count == 0)
            throw new CliArgumentException("Не указано ни одного изображения для распознавания.");

        return new CliOptions
        {
            OutputPath = output,
            ImagePaths = images,
            ProfileName = profile,
            LogPath = log,
        };
    }

    private static string RequireValue(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length)
            throw new CliArgumentException($"Отсутствует значение для параметра {flag}.");
        return args[++i];
    }
}

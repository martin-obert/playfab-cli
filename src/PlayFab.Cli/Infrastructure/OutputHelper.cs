using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Json;

namespace PlayFab.Cli.Infrastructure;

/// <summary>Writes structured output as JSON or Spectre.Console tables.</summary>
public static class OutputHelper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Serializes to JSON; syntax-highlighted when interactive, plain when piped.</summary>
    public static void WriteJson(object value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);

        if (Console.IsOutputRedirected)
        {
            Console.WriteLine(json);
        }
        else
        {
            AnsiConsole.Write(new JsonText(json));
            AnsiConsole.WriteLine();
        }
    }

    /// <summary>Renders a key-value dictionary as a Spectre.Console rounded table.</summary>
    public static void WriteTable(string title, IDictionary<string, string> data)
    {
        var table = new Table().Title(title).Border(TableBorder.Rounded);
        table.AddColumn("Key");
        table.AddColumn("Value");

        foreach (var (key, value) in data)
            table.AddRow(Markup.Escape(key), Markup.Escape(value));

        AnsiConsole.Write(table);
    }
}

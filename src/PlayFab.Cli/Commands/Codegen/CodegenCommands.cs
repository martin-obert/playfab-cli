using System.CommandLine;
using PlayFab.Cli.Infrastructure;
using PlayFab.Cli.Models;

namespace PlayFab.Cli.Commands.Codegen;

public static class CodegenCommands
{
    public static Command CreateGroup(GlobalOptions globalOptions)
    {
        var statisticsDirOption = new Option<string>("--statistics-dir")
        {
            Description = "Path to statistic definitions directory",
            DefaultValueFactory = _ => "statistics",
            Recursive = true,
        };
        var leaderboardsDirOption = new Option<string>("--leaderboards-dir")
        {
            Description = "Path to leaderboard definitions directory",
            DefaultValueFactory = _ => "leaderboards",
            Recursive = true,
        };
        var outputDirOption = new Option<string>("--output-dir")
        {
            Description = "Directory to write generated C# files to",
            DefaultValueFactory = _ => "generated",
            Recursive = true,
        };
        var namespaceOption = new Option<string>("--namespace")
        {
            Description = "Namespace for generated classes",
            DefaultValueFactory = _ => "PlayFab.Generated",
            Recursive = true,
        };

        var group = new Command("codegen", "Generate strongly-typed C# classes from statistic/leaderboard definition files");
        group.Add(statisticsDirOption);
        group.Add(leaderboardsDirOption);
        group.Add(outputDirOption);
        group.Add(namespaceOption);
        group.Add(CreateStatisticsCommand(statisticsDirOption, outputDirOption, namespaceOption));
        group.Add(CreateLeaderboardsCommand(statisticsDirOption, leaderboardsDirOption, outputDirOption, namespaceOption));
        group.Add(CreateAllCommand(statisticsDirOption, leaderboardsDirOption, outputDirOption, namespaceOption));
        return group;
    }

    private static Command CreateStatisticsCommand(Option<string> statisticsDirOption, Option<string> outputDirOption, Option<string> namespaceOption)
    {
        var nameOption = new Option<string?>("--name")
        {
            Description = "Generate only this statistic by name",
        };

        var command = new Command("statistics", "Generate C# classes from statistic definition files");
        command.Add(nameOption);

        command.SetAction(parseResult =>
        {
            var statisticsDir = parseResult.GetValue(statisticsDirOption)!;
            var outputDir = parseResult.GetValue(outputDirOption)!;
            var @namespace = parseResult.GetValue(namespaceOption)!;
            var name = parseResult.GetValue(nameOption);

            var statistics = LoadAndValidateStatistics(statisticsDir, name);
            if (statistics is null)
                return;

            if (statistics.Count == 0)
            {
                Console.WriteLine("No statistic definitions to generate.");
                return;
            }

            var count = WriteStatisticClasses(statistics, outputDir, @namespace);
            Console.WriteLine($"{count} statistic class(es) generated to '{Path.Combine(outputDir, "Statistics")}/'.");
        });

        return command;
    }

    private static Command CreateLeaderboardsCommand(
        Option<string> statisticsDirOption, Option<string> leaderboardsDirOption, Option<string> outputDirOption, Option<string> namespaceOption)
    {
        var nameOption = new Option<string?>("--name")
        {
            Description = "Generate only this leaderboard by name",
        };

        var command = new Command("leaderboards", "Generate C# classes from leaderboard definition files");
        command.Add(nameOption);

        command.SetAction(parseResult =>
        {
            var statisticsDir = parseResult.GetValue(statisticsDirOption)!;
            var leaderboardsDir = parseResult.GetValue(leaderboardsDirOption)!;
            var outputDir = parseResult.GetValue(outputDirOption)!;
            var @namespace = parseResult.GetValue(namespaceOption)!;
            var name = parseResult.GetValue(nameOption);

            // Statistics are always loaded in full (unfiltered) so linked-column type resolution works
            // even when --name narrows which leaderboards get generated.
            var statistics = LoadAndValidateStatistics(statisticsDir, nameFilter: null);
            if (statistics is null)
                return;

            var leaderboards = LoadAndValidateLeaderboards(leaderboardsDir, name);
            if (leaderboards is null)
                return;

            if (leaderboards.Count == 0)
            {
                Console.WriteLine("No leaderboard definitions to generate.");
                return;
            }

            var count = WriteLeaderboardClasses(leaderboards, statistics, outputDir, @namespace);
            Console.WriteLine($"{count} leaderboard class(es) generated to '{Path.Combine(outputDir, "Leaderboards")}/'.");
        });

        return command;
    }

    private static Command CreateAllCommand(
        Option<string> statisticsDirOption, Option<string> leaderboardsDirOption, Option<string> outputDirOption, Option<string> namespaceOption)
    {
        var command = new Command("all", "Generate C# classes from both statistic and leaderboard definition files");

        command.SetAction(parseResult =>
        {
            var statisticsDir = parseResult.GetValue(statisticsDirOption)!;
            var leaderboardsDir = parseResult.GetValue(leaderboardsDirOption)!;
            var outputDir = parseResult.GetValue(outputDirOption)!;
            var @namespace = parseResult.GetValue(namespaceOption)!;

            var statistics = LoadAndValidateStatistics(statisticsDir, nameFilter: null);
            if (statistics is null)
                return;

            var leaderboards = LoadAndValidateLeaderboards(leaderboardsDir, nameFilter: null);
            if (leaderboards is null)
                return;

            var statisticCount = WriteStatisticClasses(statistics, outputDir, @namespace);
            var leaderboardCount = WriteLeaderboardClasses(leaderboards, statistics, outputDir, @namespace);

            Console.WriteLine($"{statisticCount} statistic class(es) and {leaderboardCount} leaderboard class(es) generated to '{outputDir}/'.");
        });

        return command;
    }

    private static List<StatisticDefinitionFile>? LoadAndValidateStatistics(string statisticsDir, string? nameFilter)
    {
        List<StatisticDefinitionFile> statistics;
        try
        {
            statistics = StatisticDefinitionStore.LoadAll(statisticsDir);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load statistic definitions from '{statisticsDir}': {ex.Message}");
            return null;
        }

        if (nameFilter is not null)
            statistics = statistics.Where(s => string.Equals(s.Name, nameFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        var errors = statistics.SelectMany(s => StatisticDefinitionStore.Validate(s).Select(e => $"{s.Name}: {e}")).ToList();
        if (errors.Count == 0)
            return statistics;

        Console.Error.WriteLine("Validation failed:");
        foreach (var error in errors)
            Console.Error.WriteLine($"  {error}");
        return null;
    }

    private static List<LeaderboardDefinitionFile>? LoadAndValidateLeaderboards(string leaderboardsDir, string? nameFilter)
    {
        List<LeaderboardDefinitionFile> leaderboards;
        try
        {
            leaderboards = LeaderboardDefinitionStore.LoadAll(leaderboardsDir);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load leaderboard definitions from '{leaderboardsDir}': {ex.Message}");
            return null;
        }

        if (nameFilter is not null)
            leaderboards = leaderboards.Where(l => string.Equals(l.Name, nameFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        var errors = leaderboards.SelectMany(l => LeaderboardDefinitionStore.Validate(l).Select(e => $"{l.Name}: {e}")).ToList();
        if (errors.Count == 0)
            return leaderboards;

        Console.Error.WriteLine("Validation failed:");
        foreach (var error in errors)
            Console.Error.WriteLine($"  {error}");
        return null;
    }

    private static int WriteStatisticClasses(List<StatisticDefinitionFile> statistics, string outputDir, string @namespace)
    {
        var dir = Path.Combine(outputDir, "Statistics");
        Directory.CreateDirectory(dir);

        foreach (var statistic in statistics)
        {
            var spec = CodeGenerator.BuildStatisticClassSpec(statistic);
            var source = CodeGenerator.Render(spec, @namespace);
            var filePath = Path.Combine(dir, $"{CodeGenerator.SanitizeIdentifier(statistic.Name)}.cs");
            File.WriteAllText(filePath, source);
            Console.WriteLine($"  Wrote {filePath}");
        }

        return statistics.Count;
    }

    private static int WriteLeaderboardClasses(
        List<LeaderboardDefinitionFile> leaderboards, List<StatisticDefinitionFile> statistics, string outputDir, string @namespace)
    {
        var dir = Path.Combine(outputDir, "Leaderboards");
        Directory.CreateDirectory(dir);

        foreach (var leaderboard in leaderboards)
        {
            var spec = CodeGenerator.BuildLeaderboardClassSpec(leaderboard, statistics);
            var source = CodeGenerator.Render(spec, @namespace);
            var filePath = Path.Combine(dir, $"{CodeGenerator.SanitizeIdentifier(leaderboard.Name)}.cs");
            File.WriteAllText(filePath, source);
            Console.WriteLine($"  Wrote {filePath}");
        }

        return leaderboards.Count;
    }
}

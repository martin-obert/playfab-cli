using System.Text.RegularExpressions;
using PlayFab.Cli.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PlayFab.Cli.Infrastructure;

public static partial class MigrationLoader
{
    [GeneratedRegex(@"^(\d{14})_(.+)$")]
    private static partial Regex FilenamePattern();

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static List<MigrationFile> LoadAll(string migrationsDirectory)
    {
        if (!Directory.Exists(migrationsDirectory))
            return [];

        var files = Directory.GetFiles(migrationsDirectory, "*.yml")
            .Concat(Directory.GetFiles(migrationsDirectory, "*.yaml"))
            .Distinct()
            .OrderBy(f => Path.GetFileNameWithoutExtension(f))
            .ToList();

        var migrations = new List<MigrationFile>();
        foreach (var file in files)
        {
            var stem = Path.GetFileNameWithoutExtension(file);
            var match = FilenamePattern().Match(stem);
            if (!match.Success)
                continue;

            var id = match.Groups[1].Value;
            var name = match.Groups[2].Value;
            var definition = ParseYaml(file);
            migrations.Add(new MigrationFile(id, name, file, definition));
        }

        return migrations;
    }

    public static MigrationFile LoadSingle(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Migration file not found: '{filePath}'", filePath);

        var stem = Path.GetFileNameWithoutExtension(filePath);
        var match = FilenamePattern().Match(stem);
        if (!match.Success)
            throw new InvalidOperationException(
                $"Migration filename must match pattern '<14-digit-timestamp>_<name>.yml': '{Path.GetFileName(filePath)}'");

        var id = match.Groups[1].Value;
        var name = match.Groups[2].Value;
        var definition = ParseYaml(filePath);
        return new MigrationFile(id, name, filePath, definition);
    }

    public static List<string> Validate(MigrationFile migration)
    {
        var errors = new List<string>();
        var def = migration.Definition;

        if (string.IsNullOrWhiteSpace(def.Description))
            errors.Add("Description must not be empty.");

        if (def.Up.Count == 0)
            errors.Add("Up steps must not be empty.");

        ValidateSteps(def.Up, "up", errors);
        ValidateSteps(def.Down, "down", errors);

        return errors;
    }

    private static void ValidateSteps(List<MigrationStep> steps, string direction, List<string> errors)
    {
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var prefix = $"{direction}[{i}]";

            if (!OperationTypes.All.Contains(step.Type))
            {
                errors.Add($"{prefix}: Unknown operation type '{step.Type}'.");
                continue;
            }

            if (OperationTypes.StatisticOperations.Contains(step.Type))
            {
                ValidateStatisticStep(step, prefix, errors);
                continue;
            }

            if (OperationTypes.LeaderboardOperations.Contains(step.Type))
            {
                ValidateLeaderboardStep(step, prefix, errors);
                continue;
            }

            var hasKey = step.Key is not null;
            var hasKeys = step.Keys is not null;
            var hasValues = step.Values is not null;
            var formCount = (hasKey ? 1 : 0) + (hasKeys ? 1 : 0) + (hasValues ? 1 : 0);

            if (formCount == 0)
            {
                errors.Add($"{prefix}: Must specify 'key', 'keys', or 'values'.");
                continue;
            }

            if (formCount > 1)
            {
                errors.Add($"{prefix}: Only one of 'key', 'keys', or 'values' may be specified.");
                continue;
            }

            var isSet = step.Type.StartsWith("set-", StringComparison.Ordinal);
            var isDelete = step.Type.StartsWith("delete-", StringComparison.Ordinal);

            if (hasKey)
            {
                if (string.IsNullOrWhiteSpace(step.Key))
                    errors.Add($"{prefix}: Key must not be empty.");

                if (isSet && string.IsNullOrEmpty(step.Value))
                    errors.Add($"{prefix}: Value is required for '{step.Type}'.");

                if (isDelete && step.Value is not null)
                    errors.Add($"{prefix}: Value must not be set for '{step.Type}'.");
            }
            else if (hasKeys)
            {
                if (!isDelete)
                {
                    errors.Add($"{prefix}: 'keys' can only be used with delete operations, not '{step.Type}'.");
                    continue;
                }

                if (step.Keys!.Count == 0)
                {
                    errors.Add($"{prefix}: 'keys' must not be empty.");
                    continue;
                }

                for (var j = 0; j < step.Keys!.Count; j++)
                {
                    if (string.IsNullOrWhiteSpace(step.Keys[j]))
                        errors.Add($"{prefix}: keys[{j}] must not be empty.");
                }
            }
            else
            {
                if (!isSet)
                {
                    errors.Add($"{prefix}: 'values' can only be used with set operations, not '{step.Type}'.");
                    continue;
                }

                if (step.Values!.Count == 0)
                {
                    errors.Add($"{prefix}: 'values' must not be empty.");
                    continue;
                }

                foreach (var kvp in step.Values!)
                {
                    if (string.IsNullOrWhiteSpace(kvp.Key))
                        errors.Add($"{prefix}: values key must not be empty.");

                    if (string.IsNullOrEmpty(kvp.Value))
                        errors.Add($"{prefix}: values['{kvp.Key}'] must not be empty.");
                }
            }
        }
    }

    private static HashSet<string> ValidAggregationMethods => ValidationConstants.ValidAggregationMethods;
    private static HashSet<string> ValidResetIntervals => ValidationConstants.ValidResetIntervals;
    private static HashSet<string> ValidSortDirections => ValidationConstants.ValidSortDirections;

    private static void ValidateName(MigrationStep step, string prefix, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(step.Name))
            errors.Add($"{prefix}: Name must not be empty.");
    }

    private static void ValidateNoKeyValueFields(MigrationStep step, string prefix, List<string> errors)
    {
        if (step.Key is not null || step.Keys is not null || step.Values is not null)
            errors.Add($"{prefix}: 'key', 'keys', and 'values' cannot be used with '{step.Type}'.");
    }

    private static void ValidateResetInterval(MigrationStep step, string prefix, List<string> errors)
    {
        if (step.ResetInterval is not null && !ValidResetIntervals.Contains(step.ResetInterval))
            errors.Add($"{prefix}: Invalid resetInterval '{step.ResetInterval}'.");
    }

    private static void ValidateMaxQueryableVersions(MigrationStep step, string prefix, List<string> errors)
    {
        if (step.MaxQueryableVersions is not null && step.MaxQueryableVersions < 1)
            errors.Add($"{prefix}: maxQueryableVersions must be >= 1.");
    }

    private static void ValidateStatisticStep(MigrationStep step, string prefix, List<string> errors)
    {
        ValidateName(step, prefix, errors);
        ValidateNoKeyValueFields(step, prefix, errors);

        if (step.Type == OperationTypes.DeleteStatistic)
        {
            if (step.EntityType is not null || step.AggregationMethod is not null || step.ResetInterval is not null
                || step.MaxQueryableVersions is not null || step.Columns is not null)
                errors.Add($"{prefix}: 'entityType', 'aggregationMethod', 'resetInterval', 'maxQueryableVersions', and 'columns' cannot be used with '{step.Type}'.");

            return;
        }

        // create-statistic
        var hasAggregationMethod = step.AggregationMethod is not null;
        var hasColumns = step.Columns is not null;

        if (!hasAggregationMethod && !hasColumns)
        {
            errors.Add($"{prefix}: Must specify either 'aggregationMethod' (single-column) or 'columns' (multi-column).");
        }
        else if (hasAggregationMethod && hasColumns)
        {
            errors.Add($"{prefix}: Only one of 'aggregationMethod' or 'columns' may be specified.");
        }
        else if (hasAggregationMethod)
        {
            if (!ValidAggregationMethods.Contains(step.AggregationMethod!))
                errors.Add($"{prefix}: Invalid aggregationMethod '{step.AggregationMethod}'.");
        }
        else
        {
            if (step.Columns!.Count == 0)
            {
                errors.Add($"{prefix}: 'columns' must not be empty.");
            }
            else
            {
                var colNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (var j = 0; j < step.Columns!.Count; j++)
                {
                    var col = step.Columns[j];
                    if (string.IsNullOrWhiteSpace(col.Name))
                        errors.Add($"{prefix}: columns[{j}] name must not be empty.");
                    else if (!colNames.Add(col.Name))
                        errors.Add($"{prefix}: duplicate column name '{col.Name}'.");

                    if (!ValidAggregationMethods.Contains(col.AggregationMethod))
                        errors.Add($"{prefix}: columns[{j}] has invalid aggregationMethod '{col.AggregationMethod}'.");
                }
            }
        }

        ValidateResetInterval(step, prefix, errors);
        ValidateMaxQueryableVersions(step, prefix, errors);
    }

    private static void ValidateLeaderboardStep(MigrationStep step, string prefix, List<string> errors)
    {
        ValidateName(step, prefix, errors);
        ValidateNoKeyValueFields(step, prefix, errors);

        if (step.Type == OperationTypes.DeleteLeaderboard)
        {
            if (step.EntityType is not null || step.SizeLimit is not null || step.ResetInterval is not null
                || step.MaxQueryableVersions is not null || step.LeaderboardColumns is not null
                || step.AggregationMethod is not null || step.Columns is not null)
                errors.Add($"{prefix}: 'entityType', 'sizeLimit', 'resetInterval', 'maxQueryableVersions', and 'leaderboardColumns' cannot be used with '{step.Type}'.");

            return;
        }

        // create-leaderboard
        if (step.AggregationMethod is not null || step.Columns is not null)
            errors.Add($"{prefix}: 'aggregationMethod' and 'columns' cannot be used with '{step.Type}'.");

        if (step.LeaderboardColumns is null || step.LeaderboardColumns.Count == 0)
        {
            errors.Add($"{prefix}: 'leaderboardColumns' must not be empty.");
        }
        else
        {
            var colNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var j = 0; j < step.LeaderboardColumns.Count; j++)
            {
                var col = step.LeaderboardColumns[j];
                if (string.IsNullOrWhiteSpace(col.Name))
                    errors.Add($"{prefix}: leaderboardColumns[{j}] name must not be empty.");
                else if (!colNames.Add(col.Name))
                    errors.Add($"{prefix}: duplicate leaderboard column name '{col.Name}'.");

                if (!ValidSortDirections.Contains(col.SortDirection))
                    errors.Add($"{prefix}: leaderboardColumns[{j}] has invalid sortDirection '{col.SortDirection}'.");

                var hasLinkedStatistic = col.LinkedStatistic is not null;
                var hasLinkedStatisticColumn = col.LinkedStatisticColumn is not null;

                if (hasLinkedStatistic != hasLinkedStatisticColumn)
                {
                    errors.Add($"{prefix}: leaderboardColumns[{j}] 'linkedStatistic' and 'linkedStatisticColumn' must both be set or both be omitted.");
                }
                else if (hasLinkedStatistic)
                {
                    if (string.IsNullOrWhiteSpace(col.LinkedStatistic))
                        errors.Add($"{prefix}: leaderboardColumns[{j}] linkedStatistic must not be empty.");
                    if (string.IsNullOrWhiteSpace(col.LinkedStatisticColumn))
                        errors.Add($"{prefix}: leaderboardColumns[{j}] linkedStatisticColumn must not be empty.");
                }
            }
        }

        if (step.SizeLimit is not null && step.SizeLimit < 1)
            errors.Add($"{prefix}: sizeLimit must be >= 1.");

        ValidateResetInterval(step, prefix, errors);
        ValidateMaxQueryableVersions(step, prefix, errors);
    }

    private static MigrationDefinition ParseYaml(string filePath)
    {
        var yaml = File.ReadAllText(filePath);
        return Deserializer.Deserialize<MigrationDefinition>(yaml)
               ?? throw new InvalidOperationException($"Failed to deserialize migration from '{filePath}'.");
    }
}

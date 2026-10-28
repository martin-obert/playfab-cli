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

    private static MigrationDefinition ParseYaml(string filePath)
    {
        var yaml = File.ReadAllText(filePath);
        return Deserializer.Deserialize<MigrationDefinition>(yaml)
               ?? throw new InvalidOperationException($"Failed to deserialize migration from '{filePath}'.");
    }
}

using PlayFab.Cli.Models;
using PlayFab.ProgressionModels;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PlayFab.Cli.Infrastructure;

public static class StatisticDefinitionStore
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    public static List<StatisticDefinitionFile> LoadAll(string definitionsDir)
    {
        if (!Directory.Exists(definitionsDir))
            return [];

        var files = Directory.GetFiles(definitionsDir, "*.yml")
            .Concat(Directory.GetFiles(definitionsDir, "*.yaml"))
            .Distinct()
            .OrderBy(Path.GetFileNameWithoutExtension)
            .ToList();

        return files.Select(LoadSingle).ToList();
    }

    public static StatisticDefinitionFile LoadSingle(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Statistic definition file not found: '{filePath}'", filePath);

        var yaml = File.ReadAllText(filePath);
        return Deserializer.Deserialize<StatisticDefinitionFile>(yaml)
               ?? throw new InvalidOperationException($"Failed to deserialize statistic definition from '{filePath}'.");
    }

    public static void Save(StatisticDefinitionFile definition, string definitionsDir)
    {
        Directory.CreateDirectory(definitionsDir);
        var filePath = Path.Combine(definitionsDir, $"{definition.Name}.yml");
        var yaml = Serializer.Serialize(definition);
        File.WriteAllText(filePath, yaml);
    }

    public static List<string> Validate(StatisticDefinitionFile definition)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(definition.Name))
            errors.Add("Name must not be empty.");

        if (string.IsNullOrWhiteSpace(definition.EntityType))
            errors.Add("EntityType must not be empty.");

        if (!ValidationConstants.ValidResetIntervals.Contains(definition.ResetInterval))
            errors.Add($"Invalid resetInterval '{definition.ResetInterval}'.");

        if (definition.MaxQueryableVersions < 1)
            errors.Add("maxQueryableVersions must be >= 1.");

        if (definition.Columns is null || definition.Columns.Count == 0)
        {
            errors.Add("Columns must not be empty.");
        }
        else
        {
            var colNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < definition.Columns.Count; i++)
            {
                var col = definition.Columns[i];
                if (string.IsNullOrWhiteSpace(col.Name))
                    errors.Add($"columns[{i}] name must not be empty.");
                else if (!colNames.Add(col.Name))
                    errors.Add($"Duplicate column name '{col.Name}'.");

                if (!ValidationConstants.ValidAggregationMethods.Contains(col.AggregationMethod))
                    errors.Add($"columns[{i}] has invalid aggregationMethod '{col.AggregationMethod}'.");

                if (!ValidationConstants.ValidColumnTypes.Contains(col.Type))
                    errors.Add($"columns[{i}] has invalid type '{col.Type}'.");
            }
        }

        return errors;
    }

    public static StatisticDefinitionFile MapFromPlayFab(GetStatisticDefinitionResponse response)
    {
        return new StatisticDefinitionFile
        {
            Name = response.Name,
            EntityType = response.EntityType ?? "title_player_account",
            ResetInterval = response.VersionConfiguration?.ResetInterval.ToString() ?? "Manual",
            MaxQueryableVersions = (int)(response.VersionConfiguration?.MaxQueryableVersions ?? 1),
            Columns = (response.Columns ?? []).Select(c => new StatisticColumnDefinition
            {
                Name = c.Name,
                AggregationMethod = c.AggregationMethod.ToString(),
            }).ToList(),
        };
    }
}

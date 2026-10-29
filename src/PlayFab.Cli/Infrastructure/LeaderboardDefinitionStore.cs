using PlayFab;
using PlayFab.Cli.Models;
using PlayFab.ProgressionModels;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PlayFab.Cli.Infrastructure;

public static class LeaderboardDefinitionStore
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    public static List<LeaderboardDefinitionFile> LoadAll(string definitionsDir)
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

    public static LeaderboardDefinitionFile LoadSingle(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Leaderboard definition file not found: '{filePath}'", filePath);

        var yaml = File.ReadAllText(filePath);
        return Deserializer.Deserialize<LeaderboardDefinitionFile>(yaml)
               ?? throw new InvalidOperationException($"Failed to deserialize leaderboard definition from '{filePath}'.");
    }

    public static void Save(LeaderboardDefinitionFile definition, string definitionsDir)
    {
        Directory.CreateDirectory(definitionsDir);
        var filePath = Path.Combine(definitionsDir, $"{definition.Name}.yml");
        var yaml = Serializer.Serialize(definition);
        File.WriteAllText(filePath, yaml);
    }

    public static List<string> Validate(LeaderboardDefinitionFile definition)
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

        if (definition.SizeLimit < 1)
            errors.Add("sizeLimit must be >= 1.");

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

                if (!ValidationConstants.ValidSortDirections.Contains(col.SortDirection))
                    errors.Add($"columns[{i}] has invalid sortDirection '{col.SortDirection}'.");

                var hasLinkedStat = col.LinkedStatistic is not null;
                var hasLinkedCol = col.LinkedStatisticColumn is not null;
                if (hasLinkedStat != hasLinkedCol)
                    errors.Add($"columns[{i}] 'linkedStatistic' and 'linkedStatisticColumn' must both be set or both be omitted.");
                else if (hasLinkedStat)
                {
                    if (string.IsNullOrWhiteSpace(col.LinkedStatistic))
                        errors.Add($"columns[{i}] linkedStatistic must not be empty.");
                    if (string.IsNullOrWhiteSpace(col.LinkedStatisticColumn))
                        errors.Add($"columns[{i}] linkedStatisticColumn must not be empty.");
                }
            }
        }

        return errors;
    }

    public static LeaderboardDefinitionFile MapFromPlayFab(GetLeaderboardDefinitionResponse response)
    {
        return new LeaderboardDefinitionFile
        {
            Name = response.Name,
            EntityType = response.EntityType ?? "title_player_account",
            ResetInterval = response.VersionConfiguration?.ResetInterval.ToString() ?? "Manual",
            MaxQueryableVersions = (int)(response.VersionConfiguration?.MaxQueryableVersions ?? 1),
            SizeLimit = response.SizeLimit,
            Columns = (response.Columns ?? []).Select(MapColumn).ToList(),
        };
    }

    public static LeaderboardDefinitionFile MapFromPlayFab(LeaderboardDefinition definition)
    {
        return new LeaderboardDefinitionFile
        {
            Name = definition.Name,
            EntityType = definition.EntityType ?? "title_player_account",
            ResetInterval = definition.VersionConfiguration?.ResetInterval.ToString() ?? "Manual",
            MaxQueryableVersions = (int)(definition.VersionConfiguration?.MaxQueryableVersions ?? 1),
            SizeLimit = definition.SizeLimit,
            Columns = (definition.Columns ?? []).Select(MapColumn).ToList(),
        };
    }

    public static async Task<List<LeaderboardDefinitionFile>> FetchAllFromPlayFabAsync()
    {
        var definitions = new List<LeaderboardDefinitionFile>();
        string? skipToken = null;

        do
        {
            var result = await PlayFabProgressionAPI.ListLeaderboardDefinitionsAsync(
                new ListLeaderboardDefinitionsRequest { SkipToken = skipToken });

            if (result.Error is not null)
                throw new InvalidOperationException($"ListLeaderboardDefinitions failed: {result.Error.ErrorMessage}");

            foreach (var def in result.Result.LeaderboardDefinitions ?? [])
                definitions.Add(MapFromPlayFab(def));

            skipToken = result.Result.SkipToken;
        } while (skipToken is not null);

        return definitions;
    }

    private static LeaderboardColumnDefinition MapColumn(LeaderboardColumn column)
    {
        return new LeaderboardColumnDefinition
        {
            Name = column.Name,
            SortDirection = column.SortDirection.ToString(),
            LinkedStatistic = column.LinkedStatisticColumn?.LinkedStatisticName,
            LinkedStatisticColumn = column.LinkedStatisticColumn?.LinkedStatisticColumnName,
        };
    }
}

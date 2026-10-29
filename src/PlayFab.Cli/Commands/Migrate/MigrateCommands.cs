using System.CommandLine;
using PlayFab.Cli.Infrastructure;
using PlayFab.Cli.Models;
using Spectre.Console;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

using SpectreTable = Spectre.Console.Table;

namespace PlayFab.Cli.Commands.Migrate;

public static class MigrateCommands
{
    private static readonly ISerializer YamlSerializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    public static Command CreateGroup(GlobalOptions globalOptions)
    {
        var migrationsDirOption = new Option<string>("--migrations-dir")
        {
            Description = "Path to migrations directory",
            DefaultValueFactory = _ => "migrations",
            Recursive = true,
        };

        var group = new Command("migrate", "Manage PlayFab title configuration migrations");
        group.Add(migrationsDirOption);
        group.Add(CreateStatusCommand(globalOptions, migrationsDirOption));
        group.Add(CreateUpCommand(globalOptions, migrationsDirOption));
        group.Add(CreateDownCommand(globalOptions, migrationsDirOption));
        group.Add(CreateCreateCommand(migrationsDirOption));
        group.Add(CreatePlanGroup(globalOptions, migrationsDirOption));
        return group;
    }

    private static Command CreateStatusCommand(GlobalOptions globalOptions, Option<string> migrationsDirOption)
    {
        var command = new Command("status", "Show applied and pending migrations");

        command.SetAction(async parseResult =>
        {
            var titleId = parseResult.GetValue(globalOptions.TitleId);
            var secretKey = parseResult.GetValue(globalOptions.SecretKey);
            var output = parseResult.GetValue(globalOptions.Output);
            var migrationsDir = parseResult.GetValue(migrationsDirOption)!;

            PlayFabAuth.RequireCredentials(titleId, secretKey);

            var localMigrations = MigrationLoader.LoadAll(migrationsDir);
            var ledger = await MigrationLedgerService.ReadLedgerAsync();
            var appliedIds = ledger.AppliedMigrations.Select(r => r.Id).ToHashSet();

            var rows = localMigrations.Select(m =>
            {
                var record = ledger.AppliedMigrations.FirstOrDefault(r => r.Id == m.Id);
                return new
                {
                    Id = m.Id,
                    Name = m.Name,
                    Status = appliedIds.Contains(m.Id) ? "Applied" : "Pending",
                    AppliedAt = record?.AppliedAt.ToString("o") ?? "",
                };
            }).ToList();

            if (output == OutputFormat.Table)
            {
                var table = new SpectreTable()
                    .Title("Migration Status")
                    .Border(TableBorder.Rounded);
                table.AddColumn("ID");
                table.AddColumn("Name");
                table.AddColumn("Status");
                table.AddColumn("Applied At");

                foreach (var row in rows)
                    table.AddRow(row.Id, row.Name, row.Status, row.AppliedAt);

                AnsiConsole.Write(table);
            }
            else
            {
                OutputHelper.WriteJson(rows);
            }
        });

        return command;
    }

    private static Command CreateUpCommand(GlobalOptions globalOptions, Option<string> migrationsDirOption)
    {
        var toOption = new Option<string?>("--to") { Description = "Apply up to and including this migration ID" };

        var command = new Command("up", "Apply pending migrations");
        command.Add(toOption);

        command.SetAction(async parseResult =>
        {
            var titleId = parseResult.GetValue(globalOptions.TitleId);
            var secretKey = parseResult.GetValue(globalOptions.SecretKey);
            var migrationsDir = parseResult.GetValue(migrationsDirOption)!;
            var toId = parseResult.GetValue(toOption);

            await PlayFabAuth.RequireEntityCredentialsAsync(titleId, secretKey);

            var localMigrations = MigrationLoader.LoadAll(migrationsDir);
            var ledger = await MigrationLedgerService.ReadLedgerAsync();
            var appliedIds = ledger.AppliedMigrations.Select(r => r.Id).ToHashSet();

            var pending = localMigrations.Where(m => !appliedIds.Contains(m.Id)).ToList();
            if (toId is not null)
                pending = pending.Where(m => string.Compare(m.Id, toId, StringComparison.Ordinal) <= 0).ToList();

            if (pending.Count == 0)
            {
                Console.WriteLine("No pending migrations.");
                return;
            }

            var applied = 0;
            foreach (var migration in pending)
            {
                Console.Write($"Applying {migration.Id}_{migration.Name}...");
                var result = await MigrationRunner.ApplyAsync(migration, ledger);
                if (result.Success)
                {
                    Console.WriteLine(" done.");
                    applied++;
                }
                else
                {
                    Console.WriteLine($" FAILED: {result.ErrorMessage}");
                    break;
                }
            }

            Console.WriteLine($"{applied}/{pending.Count} migration(s) applied.");
        });

        return command;
    }

    private static Command CreateDownCommand(GlobalOptions globalOptions, Option<string> migrationsDirOption)
    {
        var toOption = new Option<string?>("--to") { Description = "Rollback down to (but not including) this migration ID" };

        var command = new Command("down", "Rollback applied migrations");
        command.Add(toOption);

        command.SetAction(async parseResult =>
        {
            var titleId = parseResult.GetValue(globalOptions.TitleId);
            var secretKey = parseResult.GetValue(globalOptions.SecretKey);
            var migrationsDir = parseResult.GetValue(migrationsDirOption)!;
            var toId = parseResult.GetValue(toOption);

            await PlayFabAuth.RequireEntityCredentialsAsync(titleId, secretKey);

            var localMigrations = MigrationLoader.LoadAll(migrationsDir);
            var ledger = await MigrationLedgerService.ReadLedgerAsync();
            var localById = localMigrations.ToDictionary(m => m.Id);

            List<MigrationFile> toRollback;
            if (toId is not null)
            {
                toRollback = ledger.AppliedMigrations
                    .Where(r => string.Compare(r.Id, toId, StringComparison.Ordinal) > 0)
                    .OrderByDescending(r => r.Id)
                    .Select(r => localById.GetValueOrDefault(r.Id))
                    .Where(m => m is not null)
                    .ToList()!;
            }
            else
            {
                var last = ledger.AppliedMigrations.MaxBy(r => r.Id);
                toRollback = last is not null && localById.TryGetValue(last.Id, out var m) ? [m] : [];
            }

            if (toRollback.Count == 0)
            {
                Console.WriteLine("No migrations to rollback.");
                return;
            }

            var rolledBack = 0;
            foreach (var migration in toRollback)
            {
                Console.Write($"Rolling back {migration.Id}_{migration.Name}...");
                var result = await MigrationRunner.RollbackAsync(migration, ledger);
                if (result.Success)
                {
                    Console.WriteLine(" done.");
                    rolledBack++;
                }
                else
                {
                    Console.WriteLine($" FAILED: {result.ErrorMessage}");
                    break;
                }
            }

            Console.WriteLine($"{rolledBack}/{toRollback.Count} migration(s) rolled back.");
        });

        return command;
    }

    private static Command CreateCreateCommand(Option<string> migrationsDirOption)
    {
        var nameArgument = new Argument<string>("name") { Description = "Migration name (e.g., add_leaderboards)" };

        var command = new Command("create", "Scaffold a new migration file");
        command.Add(nameArgument);

        command.SetAction(parseResult =>
        {
            var migrationsDir = parseResult.GetValue(migrationsDirOption)!;
            var name = parseResult.GetValue(nameArgument);

            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var filename = $"{timestamp}_{name}.yml";
            var filePath = Path.Combine(migrationsDir, filename);

            Directory.CreateDirectory(migrationsDir);

            var scaffold = new MigrationDefinition
            {
                Description = "",
                Up = [new MigrationStep { Type = OperationTypes.SetTitleData, Key = "ExampleKey", Value = "ExampleValue" }],
                Down = [new MigrationStep { Type = OperationTypes.DeleteTitleData, Key = "ExampleKey" }],
            };

            var yaml = YamlSerializer.Serialize(scaffold);
            File.WriteAllText(filePath, yaml);

            Console.WriteLine($"Created: {filePath}");
        });

        return command;
    }

    private static Command CreatePlanGroup(GlobalOptions globalOptions, Option<string> migrationsDirOption)
    {
        var planGroup = new Command("plan", "Compare desired-state definitions against PlayFab and generate a migration");
        planGroup.Add(CreatePlanStatisticsCommand(globalOptions, migrationsDirOption));
        return planGroup;
    }

    private static Command CreatePlanStatisticsCommand(GlobalOptions globalOptions, Option<string> migrationsDirOption)
    {
        var statisticsDirOption = new Option<string>("--statistics-dir")
        {
            Description = "Path to statistic definitions directory",
            DefaultValueFactory = _ => "statistics",
        };
        var includeDeletesOption = new Option<bool>("--include-deletes")
        {
            Description = "Generate delete-statistic steps for statistics on PlayFab with no local definition",
        };
        var allowRecreateOption = new Option<bool>("--allow-recreate")
        {
            Description = "Generate delete+create steps for column mismatches (destructive: loses player data)",
        };

        var command = new Command("statistics", "Reconcile local statistic definition files against PlayFab");
        command.Add(statisticsDirOption);
        command.Add(includeDeletesOption);
        command.Add(allowRecreateOption);

        command.SetAction(async parseResult =>
        {
            var titleId = parseResult.GetValue(globalOptions.TitleId);
            var secretKey = parseResult.GetValue(globalOptions.SecretKey);
            var migrationsDir = parseResult.GetValue(migrationsDirOption)!;
            var statisticsDir = parseResult.GetValue(statisticsDirOption)!;
            var includeDeletes = parseResult.GetValue(includeDeletesOption);
            var allowRecreate = parseResult.GetValue(allowRecreateOption);

            var localDefinitions = StatisticDefinitionStore.LoadAll(statisticsDir);

            var validationErrors = new List<string>();
            foreach (var def in localDefinitions)
            {
                var errors = StatisticDefinitionStore.Validate(def);
                foreach (var error in errors)
                    validationErrors.Add($"{def.Name}: {error}");
            }

            if (validationErrors.Count > 0)
            {
                Console.Error.WriteLine("Local definition validation failed:");
                foreach (var error in validationErrors)
                    Console.Error.WriteLine($"  {error}");
                return;
            }

            await PlayFabAuth.RequireEntityCredentialsAsync(titleId, secretKey);

            List<StatisticDefinitionFile> remoteDefinitions;
            try
            {
                remoteDefinitions = await StatisticDefinitionStore.FetchAllFromPlayFabAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return;
            }

            var options = new ReconcileOptions(includeDeletes, allowRecreate);
            var result = StatisticReconciler.Reconcile(localDefinitions, remoteDefinitions, options);

            var table = new SpectreTable()
                .Title("Statistic Reconciliation")
                .Border(TableBorder.Rounded);
            table.AddColumn("Statistic");
            table.AddColumn("Status");
            table.AddColumn("Details");

            foreach (var action in result.Actions)
            {
                var (status, details) = action.Kind switch
                {
                    ReconcileActionKind.Create => ("Create", "Local only — will create on PlayFab"),
                    ReconcileActionKind.Delete => ("Delete", "PlayFab only — will delete from PlayFab"),
                    ReconcileActionKind.Recreate => ("Recreate", "Column mismatch — will delete and recreate (destructive)"),
                    ReconcileActionKind.Synced when action.Warning is not null => ("Warning", action.Warning),
                    ReconcileActionKind.VersionConfigDrift => ("Warning", action.Warning ?? ""),
                    _ => ("Synced", ""),
                };

                table.AddRow(
                    Markup.Escape(action.StatisticName),
                    Markup.Escape(status),
                    Markup.Escape(details));
            }

            AnsiConsole.Write(table);

            if (!result.HasChanges)
            {
                Console.WriteLine("No differences found.");
                return;
            }

            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var filename = $"{timestamp}_plan_statistics.yml";
            var filePath = Path.Combine(migrationsDir, filename);

            Directory.CreateDirectory(migrationsDir);

            var migration = MigrationGenerator.GenerateStatisticMigration(result, "Auto-generated from statistic definition reconciliation");
            var yaml = YamlSerializer.Serialize(migration);
            File.WriteAllText(filePath, yaml);

            Console.WriteLine($"Generated migration: {filePath}");
        });

        return command;
    }
}

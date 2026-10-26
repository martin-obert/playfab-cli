using System.CommandLine;
using PlayFab.Cli.Infrastructure;
using PlayFab.AdminModels;
using PlayFab;

namespace PlayFab.Cli.Commands.Policy;

/// <summary>Commands for inspecting PlayFab API policies.</summary>
public static class PolicyCommands
{
    /// <summary>Registers the 'policy' command group with the get subcommand.</summary>
    public static Command CreateGroup(Option<string?> globalTitleId, Option<string?> globalSecretKey, Option<OutputFormat> outputOption)
    {
        var group = new Command("policy", "Manage PlayFab API policies");
        group.Add(CreateGetCommand(globalTitleId, globalSecretKey, outputOption));
        return group;
    }

    private static Command CreateGetCommand(Option<string?> globalTitleId, Option<string?> globalSecretKey, Option<OutputFormat> outputOption)
    {
        var command = new Command("get", "Get API policy statements for the title");

        command.SetAction(async parseResult =>
        {
            var titleId = parseResult.GetValue(globalTitleId);
            var secretKey = parseResult.GetValue(globalSecretKey);
            var output = parseResult.GetValue(outputOption);

            var config = CliConfig.Load().MergeWith(titleId, secretKey);
            PlayFabClientFactory.Configure(config);

            var result = await PlayFabAdminAPI.GetPolicyAsync(new GetPolicyRequest
            {
                PolicyName = "ApiPolicy",
            });

            if (result.Error is not null)
            {
                Console.Error.WriteLine($"Error: {result.Error.ErrorMessage}");
                Environment.ExitCode = 1;
                return;
            }

            var statements = result.Result.Statements ?? [];

            if (output == OutputFormat.Table)
            {
                var rows = new Dictionary<string, string>();
                for (var i = 0; i < statements.Count; i++)
                {
                    var s = statements[i];
                    var condition = s.ApiConditions?.HasSignatureOrEncryption is not null
                        ? $" [HasSignatureOrEncryption={s.ApiConditions.HasSignatureOrEncryption}]"
                        : "";
                    rows[$"#{i + 1} {s.Action}"] = $"{s.Effect} {s.Resource}{condition}";
                }
                OutputHelper.WriteTable($"ApiPolicy v{result.Result.PolicyVersion} ({statements.Count} statements)", rows);
            }
            else
            {
                OutputHelper.WriteJson(new
                {
                    policyName = result.Result.PolicyName,
                    policyVersion = result.Result.PolicyVersion,
                    lastUpdated = result.Result.LastUpdated,
                    statements,
                });
            }
        });

        return command;
    }
}

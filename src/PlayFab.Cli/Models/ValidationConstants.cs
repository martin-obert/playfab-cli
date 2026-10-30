namespace PlayFab.Cli.Models;

public static class ValidationConstants
{
    public static readonly HashSet<string> ValidAggregationMethods = new(StringComparer.OrdinalIgnoreCase) { "Last", "Min", "Max", "Sum" };
    public static readonly HashSet<string> ValidResetIntervals = new(StringComparer.OrdinalIgnoreCase) { "Manual", "Hour", "Day", "Week", "Month" };
    public static readonly HashSet<string> ValidSortDirections = new(StringComparer.OrdinalIgnoreCase) { "Ascending", "Descending" };

    /// <summary>Local column "type" annotation -> C# type keyword. PlayFab has no concept of this; it only drives code generation.</summary>
    public static readonly IReadOnlyDictionary<string, string> CSharpTypeByColumnType =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["string"] = "string",
            ["bool"] = "bool",
            ["number"] = "float", // legacy alias, kept forever
            ["int8"] = "sbyte",
            ["int16"] = "short",
            ["int32"] = "int",
            ["int64"] = "long",
            ["uint8"] = "byte",
            ["uint16"] = "ushort",
            ["uint32"] = "uint",
            ["uint64"] = "ulong",
            ["float32"] = "float",
            ["float64"] = "double",
        };

    public static readonly HashSet<string> ValidColumnTypes = new(CSharpTypeByColumnType.Keys, StringComparer.OrdinalIgnoreCase);
}

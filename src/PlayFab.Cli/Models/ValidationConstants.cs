namespace PlayFab.Cli.Models;

public static class ValidationConstants
{
    public static readonly HashSet<string> ValidAggregationMethods = new(StringComparer.OrdinalIgnoreCase) { "Last", "Min", "Max", "Sum" };
    public static readonly HashSet<string> ValidResetIntervals = new(StringComparer.OrdinalIgnoreCase) { "Manual", "Hour", "Day", "Week", "Month" };
    public static readonly HashSet<string> ValidSortDirections = new(StringComparer.OrdinalIgnoreCase) { "Ascending", "Descending" };
    public static readonly HashSet<string> ValidColumnTypes = new(StringComparer.OrdinalIgnoreCase) { "string", "number", "bool" };
}

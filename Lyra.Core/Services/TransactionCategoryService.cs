namespace Lyra.Core.Services;

public class TransactionCategoryService
{
    private record CategoryMeta(string Color, string Icon);

    private static readonly Dictionary<string, CategoryMeta> _known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Rent"]             = new(ColorPalette.Get(1),  "home"),
        ["Healthcare"]       = new(ColorPalette.Get(2),  "favorite"),
        ["Subscriptions"]    = new(ColorPalette.Get(3),  "subscriptions"),
        ["Uncategorized"]    = new(ColorPalette.Get(4),  "help_outline"),
        ["Transportation"]   = new(ColorPalette.Get(5),  "directions_bus"),
        ["Groceries"]        = new(ColorPalette.Get(6),  "check_box"),
        ["Transfer"]         = new(ColorPalette.Get(7),  "swap_horiz"),
        ["Hobby"]            = new(ColorPalette.Get(8),  "redeem"),
        ["Miscellaneous"]    = new(ColorPalette.Get(9),  "more_horiz"),
        ["Sports & Fitness"] = new(ColorPalette.Get(10), "fitness_center"),
        ["Food & Drink"]     = new(ColorPalette.Get(11), "restaurant"),
        ["Fees"]             = new(ColorPalette.Get(12), "receipt"),
        ["Entertainment"]    = new(ColorPalette.Get(13), "movie"),
        ["Savings"]          = new(ColorPalette.Get(14), "savings"),
        ["Insurance"]        = new(ColorPalette.Get(15), "security"),
        ["Education"]        = new(ColorPalette.Get(16), "school"),
    };

    /// <summary>Returns the display color for a category label.</summary>
    public string GetColor(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return _known["Uncategorized"].Color;

        if (_known.TryGetValue(category, out var meta))
            return meta.Color;

        var index = Math.Abs(category.GetHashCode()) % ColorPalette.Colors.Count;
        return ColorPalette.Get(index);
    }

    /// <summary>Returns the Material icon ligature name for a category label.</summary>
    public string GetIcon(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return _known["Uncategorized"].Icon;

        return _known.TryGetValue(category, out var meta) ? meta.Icon : "label";
    }

    /// <summary>Builds a label → color map for a set of data points.</summary>
    public Dictionary<string, string> BuildColorMap(IEnumerable<string> labels)
        => labels.Distinct(StringComparer.OrdinalIgnoreCase)
                 .ToDictionary(l => l, GetColor, StringComparer.OrdinalIgnoreCase);
}
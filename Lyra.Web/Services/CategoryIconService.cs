using MudBlazor;

namespace Lyra.Web.Services;

/// <summary>
/// Maps category labels to MudBlazor icon strings.
/// Kept in Lyra.Web so that Lyra.Core has no dependency on MudBlazor.
/// </summary>
public class CategoryIconService
{
    private static readonly Dictionary<string, string> _icons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Rent"]             = Icons.Material.Filled.Home,
        ["Healthcare"]       = Icons.Material.Filled.Favorite,
        ["Subscriptions"]    = Icons.Material.Filled.Subscriptions,
        ["Uncategorized"]    = Icons.Material.Filled.HelpOutline,
        ["Transportation"]   = Icons.Material.Filled.DirectionsBus,
        ["Groceries"]        = Icons.Material.Filled.ShoppingCart,
        ["Transfer"]         = Icons.Material.Filled.SwapHoriz,
        ["Hobby"]            = Icons.Material.Filled.Redeem,
        ["Miscellaneous"]    = Icons.Material.Filled.MoreHoriz,
        ["Sports & Fitness"] = Icons.Material.Filled.FitnessCenter,
        ["Food & Drink"]     = Icons.Material.Filled.Restaurant,
        ["Fees"]             = Icons.Material.Filled.Receipt,
        ["Entertainment"]    = Icons.Material.Filled.Movie,
        ["Savings"]          = Icons.Material.Filled.Savings,
        ["Insurance"]        = Icons.Material.Filled.Security,
        ["Education"]        = Icons.Material.Filled.School,
    };

    /// <summary>Returns the MudBlazor icon string for a category label.</summary>
    public string GetIcon(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return _icons["Uncategorized"];

        return _icons.TryGetValue(category, out var icon) ? icon : Icons.Material.Filled.Label;
    }
}
using Lyra.Core.Services;

namespace Lyra.Web.Components;

internal static class CategoryStyleHelper
{
    /// <summary>Converts a #RRGGBB hex color to rgba(r,g,b,alpha) for chip backgrounds.</summary>
    public static string HexWithAlpha(string hex, double alpha)
    {
        hex = hex.TrimStart('#');
        if (hex.Length != 6) return $"rgba(128,128,128,{alpha.ToString(System.Globalization.CultureInfo.InvariantCulture)})";
        var r = Convert.ToInt32(hex[..2], 16);
        var g = Convert.ToInt32(hex[2..4], 16);
        var b = Convert.ToInt32(hex[4..6], 16);
        return $"rgba({r},{g},{b},{alpha.ToString(System.Globalization.CultureInfo.InvariantCulture)})";
    }

    public static string ChipStyle(TransactionCategoryService svc, string? category)
    {
        var color = svc.GetColor(category);
        return $"background-color: {HexWithAlpha(color, 0.15)}; color: {color};";
    }

    public static string IconColorStyle(TransactionCategoryService svc, string? category)
        => $"color: {svc.GetColor(category)};";
}
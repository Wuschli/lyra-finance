using Microsoft.Kiota.Abstractions;

namespace Lyra.Core.Extensions;

public static class DateExtensions
{
    /// <summary>
    /// Converts a nullable Kiota Date or DateOnly to a nullable DateTime (at midnight).
    /// </summary>
    /// <param name="date">The Kiota Date to convert.</param>
    /// <returns>DateTime at midnight or null.</returns>
    public static DateTime? ToDateTimeOrNull(this Date? date)
    {
        if (date == null)
            return null;
        // Implicit cast to DateOnly, then to DateTime at midnight
        return ((DateOnly)date.Value).ToDateTime(TimeOnly.MinValue);
    }
}
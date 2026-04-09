namespace Lyra.Core.Services.CsvImport;

/// <summary>
/// The DB target fields a CSV column can be mapped to.
/// </summary>
public enum CsvTargetField
{
    Ignore,
    BookingDate,
    TransactionDate,
    ValueDate,
    CounterpartyName,
    CounterpartyIban,
    Description,
    Amount,
    Currency,
    ExternalIdentifier
}

/// <summary>
/// A single parsed row from the raw CSV � every cell is kept as a string.
/// </summary>
public record CsvRawRow(IReadOnlyDictionary<string, string> Cells);

/// <summary>
/// The result of the initial CSV read: headers + raw rows, ready to be shown
/// in the mapping UI.
/// </summary>
public record CsvReadResult(
    IReadOnlyList<string> Headers,
    IReadOnlyList<CsvRawRow> Rows);

/// <summary>
/// A user-defined mapping from one CSV column to one DB target field.
/// </summary>
public record CsvColumnMapping(string CsvColumn, CsvTargetField TargetField);

/// <summary>
/// A single skipped (duplicate) row with key display values.
/// </summary>
public record CsvSkippedRow(
    int RowNumber,
    DateTimeOffset Date,
    decimal Amount,
    string Currency,
    string CounterpartyName,
    string? Description);

/// <summary>
/// Result returned to the dialog after the import completes.
/// </summary>
public record CsvImportResult(
    int Imported,
    int Skipped,
    IReadOnlyList<string> Errors,
    IReadOnlyList<CsvSkippedRow> SkippedRows,
    decimal? DetectedClosingBalance);
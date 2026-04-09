using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Dapper;
using Lyra.Core.Models;
using Lyra.Core.Services.CsvImport;

namespace Lyra.Core.Services;

public class CsvImportService
{
    private static readonly string[] DateFormats =
        ["dd.MM.yyyy", "dd.MM.yy", "yyyy-MM-dd", "MM/dd/yyyy", "d.M.yyyy"];

    private static readonly CultureInfo[] Cultures =
        [new CultureInfo("de-DE"), CultureInfo.InvariantCulture];

    private readonly IDbConnectionFactory _connectionFactory;

    public CsvImportService(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    // ── Step 1: Read headers and raw rows ────────────────────────────────────

    public CsvReadResult ReadCsv(Stream stream, int skipTop = 0, int skipBottom = 0)
    {
        var config = new CsvConfiguration(new CultureInfo("de-DE"))
        {
            Delimiter = ";",
            HasHeaderRecord = true,
            MissingFieldFound = null,
            BadDataFound = null,
            ShouldSkipRecord = args =>
                args.Row.Parser.Record == null ||
                args.Row.Parser.Record.All(string.IsNullOrWhiteSpace),
            IgnoreBlankLines = true
        };

        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        using var csv = new CsvReader(reader, config);

        // Skip preamble lines manually before reading header
        for (int i = 0; i < skipTop; i++)
        {
            if (!csv.Parser.Read()) break;
        }

        if (!csv.Read() || !csv.ReadHeader())
            return new CsvReadResult([], []);

        var headers = (csv.HeaderRecord ?? [])
            .Select(h => h.Trim())
            .ToList();

        if (headers.Count == 0)
            return new CsvReadResult([], []);

        var rows = new List<CsvRawRow>();
        while (csv.Read())
        {
            var record = csv.Parser.Record ?? [];
            if (record.All(string.IsNullOrWhiteSpace))
                continue;

            var cells = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in headers)
                cells[h] = csv.GetField(h)?.Trim() ?? string.Empty;

            rows.Add(new CsvRawRow(cells));
        }

        // Trim footer rows
        int take = Math.Max(0, rows.Count - skipBottom);
        return new CsvReadResult(headers, rows.Take(take).ToList());
    }

    // ── Step 2: Auto-suggest mappings based on column names ──────────────────

    public static Dictionary<string, CsvTargetField> SuggestMappings(IReadOnlyList<string> headers)
    {
        var result = new Dictionary<string, CsvTargetField>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in headers)
        {
            result[header] = GuessMappingForHeader(header);
        }

        return result;
    }

    private static CsvTargetField GuessMappingForHeader(string header)
    {
        var h = header.ToLowerInvariant();

        if (h.Contains("buchungstag") || h.Contains("buchungsdatum") || h.Contains("booking"))
            return CsvTargetField.BookingDate;
        if (h.Contains("umsatztag") || h.Contains("valuta") || h.Contains("value date"))
            return CsvTargetField.ValueDate;
        if (h.Contains("transaction") && h.Contains("date"))
            return CsvTargetField.TransactionDate;
        if (h.Contains("vorgang") || h.Contains("auftraggeber") || h.Contains("beguenstigter") ||
            h.Contains("empfaenger") || h.Contains("counterparty") || h.Contains("name"))
            return CsvTargetField.CounterpartyName;
        if (h.Contains("iban"))
            return CsvTargetField.CounterpartyIban;
        if (h.Contains("verwendungszweck") || h.Contains("buchungstext") || h.Contains("description") ||
            h.Contains("text") || h.Contains("betreff"))
            return CsvTargetField.Description;
        if (h.Contains("umsatz") || h.Contains("betrag") || h.Contains("amount") || h.Contains("summe"))
            return CsvTargetField.Amount;
        if (h.Contains("währung") || h.Contains("waehrung") || h.Contains("currency"))
            return CsvTargetField.Currency;
        if (h.Contains("identifier") || h.Contains("id") || h.Contains("transaktionsnummer") ||
            h.Contains("auftragsnummer") || h.Contains("referenznummer") || h.Contains("referenz"))
            return CsvTargetField.ExternalIdentifier;

        return CsvTargetField.Ignore;
    }

    // ── Step 3: Apply mappings and persist ───────────────────────────────────

    public async Task<CsvImportResult> ImportAsync(
        Guid accountId,
        IReadOnlyList<CsvRawRow> rows,
        IReadOnlyList<CsvColumnMapping> mappings,
        decimal? manualClosingBalance = null)
    {
        var errors = new List<string>();
        var transactions = new List<object>();

        var deDE = new CultureInfo("de-DE");

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var lineNumber = i + 1;

            string? Get(CsvTargetField field)
            {
                var mapping = mappings.FirstOrDefault(m => m.TargetField == field);
                if (mapping == null) return null;
                row.Cells.TryGetValue(mapping.CsvColumn, out var val);
                return string.IsNullOrWhiteSpace(val) ? null : val.Trim();
            }

            // Amount is required
            var amountStr = Get(CsvTargetField.Amount);
            if (amountStr == null)
            {
                errors.Add($"Row {lineNumber}: No amount value found.");
                continue;
            }

            decimal amount = 0;
            bool parsedAmount = false;
            foreach (var culture in Cultures)
            {
                if (decimal.TryParse(amountStr, NumberStyles.Number, culture, out amount))
                {
                    parsedAmount = true;
                    break;
                }
            }
            if (!parsedAmount)
            {
                errors.Add($"Row {lineNumber}: Could not parse amount '{amountStr}'.");
                continue;
            }

            // At least one date is required
            var bookingDateStr = Get(CsvTargetField.BookingDate);
            var transactionDateStr = Get(CsvTargetField.TransactionDate);
            var valueDateStr = Get(CsvTargetField.ValueDate);

            DateTimeOffset? bookingDate = ParseDate(bookingDateStr, deDE);
            DateTimeOffset? transactionDate = ParseDate(transactionDateStr, deDE);
            DateTimeOffset? valueDate = ParseDate(valueDateStr, deDE);

            var effectiveDate = transactionDate ?? bookingDate ?? valueDate;
            if (effectiveDate == null)
            {
                errors.Add($"Row {lineNumber}: Could not determine a transaction date.");
                continue;
            }

            var counterpartyName = Get(CsvTargetField.CounterpartyName) ?? string.Empty;
            var counterpartyIban = Get(CsvTargetField.CounterpartyIban) ?? string.Empty;
            var description = Get(CsvTargetField.Description);
            var currency = Get(CsvTargetField.Currency) ?? "EUR";

            // Strip currency suffix from amount string if it came from a combined cell (e.g. "306,94 EUR")
            currency = ExtractCurrencyCode(currency);

            // Use mapped external identifier if available, otherwise fall back to computed hash
            var externalIdentifier = Get(CsvTargetField.ExternalIdentifier)
                ?? ComputeHash(accountId, amount, currency, counterpartyName, description, effectiveDate.Value);

            transactions.Add(new
            {
                AccountId = accountId,
                CounterpartyName = counterpartyName,
                CounterpartyIban = counterpartyIban,
                Description = description,
                Amount = amount,
                Currency = currency,
                TransactionDate = effectiveDate.Value,
                BookingDate = bookingDate,
                ValueDate = valueDate,
                ExternalIdentifier = externalIdentifier,
                IsPending = false
            });
        }

        if (transactions.Count == 0)
            return new CsvImportResult(0, 0, errors.Count > 0 ? errors : ["No valid rows found."], [], null);

        using var db = await _connectionFactory.CreateConnectionAsync();

        const string sql = """
            INSERT INTO lyra.transactions
                (account_id, counterparty_name, counterparty_iban, description, amount, currency,
                 transaction_date, booking_date, value_date, external_identifier, is_pending)
            VALUES
                (@AccountId, @CounterpartyName, @CounterpartyIban, @Description, @Amount, @Currency,
                 @TransactionDate, @BookingDate, @ValueDate, @ExternalIdentifier, @IsPending)
            ON CONFLICT (account_id, external_identifier)
                WHERE external_identifier IS NOT NULL
            DO NOTHING
            """;

        int imported = 0, skipped = 0;
        var skippedRows = new List<CsvSkippedRow>();
        for (int i = 0; i < transactions.Count; i++)
        {
            var param = transactions[i];
            var rows2 = await db.ExecuteAsync(sql, param);
            if (rows2 > 0)
            {
                imported++;
            }
            else
            {
                skipped++;
                dynamic d = param;
                skippedRows.Add(new CsvSkippedRow(
                    RowNumber: i + 1,
                    Date: (DateTimeOffset)d.TransactionDate,
                    Amount: (decimal)d.Amount,
                    Currency: (string)d.Currency,
                    CounterpartyName: (string)d.CounterpartyName,
                    Description: (string?)d.Description));
            }
        }

        return new CsvImportResult(imported, skipped, errors, skippedRows, manualClosingBalance);
    }

    // ── Preview: Read raw text lines for trim preview ─────────────────────────

    public static List<string[]> ReadRawLines(Stream stream)
    {
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true, leaveOpen: true);

        var lines = new List<string[]>();
        string? line;
        int count = 0;
        while ((line = reader.ReadLine()) != null )
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                lines.Add(line.Split(';'));
                count++;
            }
        }
        return lines;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static DateTimeOffset? ParseDate(string? value, CultureInfo primary)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        foreach (var culture in new[] { primary, CultureInfo.InvariantCulture })
        {
            foreach (var fmt in DateFormats)
            {
                if (DateTimeOffset.TryParseExact(value, fmt, culture,
                        DateTimeStyles.AssumeLocal, out var result))
                    return result;
            }
        }
        return null;
    }

    private static string ExtractCurrencyCode(string value)
    {
        // Handle values like "0,00 EUR" — take the last word if it looks like a 3-letter code
        var parts = value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            var last = parts[^1];
            if (last.Length == 3 && last.All(char.IsLetter))
                return last.ToUpperInvariant();
        }
        // Already a plain currency code or something else — return as-is if looks valid
        if (value.Length == 3 && value.All(char.IsLetter))
            return value.ToUpperInvariant();
        return "EUR";
    }

    private static string ComputeHash(
        Guid accountId, decimal amount, string currency,
        string counterpartyName, string? description, DateTimeOffset date)
    {
        var input = string.Concat(
            accountId,
            amount.ToString(CultureInfo.InvariantCulture),
            currency,
            counterpartyName,
            description ?? string.Empty,
            date.ToString("O"));

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return "csv:" + Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
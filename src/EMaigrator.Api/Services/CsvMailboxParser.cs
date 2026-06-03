using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CsvHelper;
using CsvHelper.Configuration;
using EMaigrator.Core.Preflight;   // MailboxPair

namespace EMaigrator.Api.Services;

/// <summary>
/// Raised when an uploaded mailbox CSV is malformed (missing header, blank/invalid field, or a duplicate
/// source mailbox). The message is row-numbered so the operator can fix the offending line; the scope
/// endpoint surfaces it as a 400 with an <c>errors</c> array.
/// </summary>
public sealed class CsvValidationException : Exception
{
    public CsvValidationException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Parses a <c>source_mailbox,destination_mailbox</c> CSV upload into <see cref="MailboxPair"/> rows,
/// validating the header, rejecting blank/non-email-ish values, and rejecting duplicate source mailboxes.
/// Each rejection carries the 1-based row number (the header is row 1, so the first data row is row 2).
/// </summary>
public static class CsvMailboxParser
{
    public static IReadOnlyList<MailboxPair> Parse(Stream csv)
    {
        ArgumentNullException.ThrowIfNull(csv);

        using var reader = new StreamReader(csv);
        using var parser = new CsvParser(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            TrimOptions = TrimOptions.Trim,
        });

        // Wrap the CsvHelper read loop so its own malformed-input exceptions (bad quoting/escaping, etc.)
        // surface as a 400-mapped CsvValidationException rather than escaping as a 500. Our own
        // CsvValidationException does not derive from CsvHelperException, so the catch below never
        // re-wraps it — it propagates unchanged with its original validation message and row number.
        try
        {
            if (!parser.Read())
            {
                throw new CsvValidationException("CSV is empty; expected a header 'source_mailbox,destination_mailbox'.");
            }

            var header = parser.Record ?? [];
            var srcIdx = Array.FindIndex(header, h => string.Equals(h, "source_mailbox", StringComparison.OrdinalIgnoreCase));
            var dstIdx = Array.FindIndex(header, h => string.Equals(h, "destination_mailbox", StringComparison.OrdinalIgnoreCase));
            if (srcIdx < 0 || dstIdx < 0)
            {
                throw new CsvValidationException("CSV header must contain 'source_mailbox' and 'destination_mailbox'.");
            }

            var pairs = new List<MailboxPair>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rowNum = 1;
            while (parser.Read())
            {
                rowNum++;
                var rec = parser.Record ?? [];
                var src = srcIdx < rec.Length ? rec[srcIdx]?.Trim() ?? "" : "";
                var dst = dstIdx < rec.Length ? rec[dstIdx]?.Trim() ?? "" : "";
                if (string.IsNullOrWhiteSpace(src) || string.IsNullOrWhiteSpace(dst))
                {
                    throw new CsvValidationException(
                        string.Create(CultureInfo.InvariantCulture, $"Blank mailbox value at row {rowNum}."));
                }

                if (!src.Contains('@', StringComparison.Ordinal) || !dst.Contains('@', StringComparison.Ordinal))
                {
                    throw new CsvValidationException(
                        string.Create(CultureInfo.InvariantCulture, $"Invalid mailbox address at row {rowNum}."));
                }

                if (!seen.Add(src))
                {
                    throw new CsvValidationException(
                        string.Create(CultureInfo.InvariantCulture, $"duplicate source mailbox '{src}' at row {rowNum}."));
                }

                pairs.Add(new MailboxPair(src, dst));
            }

            if (pairs.Count == 0)
            {
                throw new CsvValidationException("CSV contains no mailbox pairs.");
            }

            return pairs;
        }
        catch (CsvHelperException ex)
        {
            throw new CsvValidationException($"CSV format error: {ex.Message}");
        }
    }
}

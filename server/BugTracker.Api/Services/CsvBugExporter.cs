using System.Globalization;
using System.Text;
using BugTracker.Api.Contracts;

namespace BugTracker.Api.Services;

/// <summary>
/// Writes a defect list as RFC 4180 CSV. Emits a UTF-8 BOM so Excel picks up the
/// encoding instead of mangling non-ASCII characters in descriptions and notes.
/// </summary>
public class CsvBugExporter
{
    private static readonly string[] Header =
    [
        "Id",
        "Title",
        "Date Found",
        "Description",
        "Severity",
        "Environment",
        "Notes",
        "Resolved",
        "Date Resolved",
        "Found By Automation",
        "Created (UTC)",
        "Last Updated (UTC)"
    ];

    public byte[] Write(IReadOnlyList<BugDto> bugs)
    {
        var builder = new StringBuilder();

        builder.AppendLine(string.Join(',', Header.Select(Escape)));

        foreach (var bug in bugs)
        {
            string[] row =
            [
                bug.Id.ToString(CultureInfo.InvariantCulture),
                bug.Title,
                bug.DateFound.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                bug.Description,
                bug.Severity.ToString(),
                bug.Environment.ToString(),
                bug.Notes ?? string.Empty,
                bug.Resolved ? "Yes" : "No",
                bug.DateResolved?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
                bug.FoundByAutomation ? "Yes" : "No",
                bug.CreatedUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                bug.UpdatedUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            ];

            builder.AppendLine(string.Join(',', row.Select(Escape)));
        }

        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(builder.ToString())).ToArray();
    }

    private static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        // Guard against CSV formula injection when the file is opened in a spreadsheet.
        // Tab and carriage return are on the OWASP list too: some spreadsheet apps strip
        // them and then evaluate what follows. The API trims input, but rows can also
        // reach the table through imports or direct SQL.
        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        var needsQuoting = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');

        return needsQuoting ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}

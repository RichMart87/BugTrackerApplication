using System.Globalization;
using BugTracker.Api.Contracts;
using BugTracker.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace BugTracker.Api.Controllers;

[ApiController]
[Route("api/bugs")]
[Produces("application/json")]
// The largest legitimate body is title + description + notes (~8.2k chars), which is
// under 64 KB even when every character arrives as a \uXXXX escape. Without this,
// Kestrel reads and deserializes up to 30 MB before validation rejects it.
[RequestSizeLimit(128 * 1024)]
public class BugsController(
    IBugService bugs,
    CsvBugExporter csvExporter,
    PdfBugExporter pdfExporter,
    IConfiguration configuration) : ControllerBase
{
    // PDF rendering is CPU-heavy (~10 rows per page), so it gets a far lower ceiling than CSV.
    private const int DefaultMaxCsvRows = 50_000;
    private const int DefaultMaxPdfRows = 2_000;

    /// <summary>Filtered, sorted and paged list of defects.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<BugDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<BugDto>>> GetAll([FromQuery] BugQueryParameters query, CancellationToken ct)
        => Ok(await bugs.GetPageAsync(query, ct));

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(BugDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BugDto>> GetById(int id, CancellationToken ct)
    {
        var bug = await bugs.GetByIdAsync(id, ct);
        return bug is null ? NotFound() : Ok(bug);
    }

    [HttpPost]
    [ProducesResponseType(typeof(BugDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BugDto>> Create([FromBody] CreateBugRequest request, CancellationToken ct)
    {
        ValidateDates(request.DateFound, null);

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var created = await bugs.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(BugDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BugDto>> Update(int id, [FromBody] UpdateBugRequest request, CancellationToken ct)
    {
        ValidateDates(request.DateFound, request.DateResolved);

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var updated = await bugs.UpdateAsync(id, request, ct);
        return updated is null ? NotFound() : Ok(updated);
    }

    /// <summary>Flips the resolved flag from the grid without sending the whole record.</summary>
    [HttpPatch("{id:int}/resolved")]
    [ProducesResponseType(typeof(BugDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BugDto>> SetResolved(int id, [FromBody] SetResolvedRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var updated = await bugs.SetResolvedAsync(id, request.Resolved!.Value, ct);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
        => await bugs.DeleteAsync(id, ct) ? NoContent() : NotFound();

    /// <summary>Headline counters for the dashboard tiles.</summary>
    [HttpGet("stats/summary")]
    [ProducesResponseType(typeof(DashboardSummary), StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardSummary>> GetSummary(CancellationToken ct)
        => Ok(await bugs.GetSummaryAsync(ct));

    /// <summary>Bugs found per month with the month-over-month delta.</summary>
    [HttpGet("stats/monthly")]
    [ProducesResponseType(typeof(IReadOnlyList<MonthlyTrendPoint>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MonthlyTrendPoint>>> GetMonthlyTrend([FromQuery] int months = 12, CancellationToken ct = default)
        => Ok(await bugs.GetMonthlyTrendAsync(months, ct));

    /// <summary>Exports every defect matching the current filters as CSV.</summary>
    [HttpGet("export/csv")]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportCsv([FromQuery] BugQueryParameters query, CancellationToken ct)
    {
        var limit = configuration.GetValue("Exports:MaxCsvRows", DefaultMaxCsvRows);
        var rows = await bugs.GetAllMatchingAsync(query, limit, ct);

        if (rows.Count > limit)
        {
            return ExportTooLarge(limit);
        }

        var bytes = csvExporter.Write(rows);

        return File(bytes, "text/csv", BuildFileName("csv"));
    }

    /// <summary>Exports every defect matching the current filters as a printable PDF report.</summary>
    [HttpGet("export/pdf")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportPdf([FromQuery] BugQueryParameters query, CancellationToken ct)
    {
        var limit = configuration.GetValue("Exports:MaxPdfRows", DefaultMaxPdfRows);
        var rows = await bugs.GetAllMatchingAsync(query, limit, ct);

        if (rows.Count > limit)
        {
            return ExportTooLarge(limit);
        }

        var trend = await bugs.GetMonthlyTrendAsync(12, ct);
        var summary = await bugs.GetSummaryAsync(ct);

        var bytes = pdfExporter.Generate(rows, trend, summary, DescribeFilters(query));

        return File(bytes, "application/pdf", BuildFileName("pdf"));
    }

    private void ValidateDates(DateOnly? dateFound, DateOnly? dateResolved)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        if (dateFound is { } found)
        {
            if (found > today)
            {
                ModelState.AddModelError(nameof(CreateBugRequest.DateFound), "The date found cannot be in the future.");
            }

            if (dateResolved is { } resolved && resolved < found)
            {
                ModelState.AddModelError(nameof(UpdateBugRequest.DateResolved), "The resolved date cannot be earlier than the date found.");
            }
        }
    }

    /// <summary>
    /// Refuses rather than silently truncating, so nobody mistakes a partial report for
    /// the full picture.
    /// </summary>
    private ObjectResult ExportTooLarge(int limit) => Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Export too large",
        detail: $"This export matches more than {limit.ToString("N0", CultureInfo.InvariantCulture)} bugs. Narrow the filters and try again.");

    private static string BuildFileName(string extension)
        => $"bug-report-{DateTime.UtcNow:yyyy-MM-dd}.{extension}";

    /// <summary>Human-readable description of the applied filters, printed on the PDF cover line.</summary>
    private static string DescribeFilters(BugQueryParameters query)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            parts.Add($"matching \"{query.Search.Trim()}\"");
        }

        if (query.Severity is { Length: > 0 })
        {
            parts.Add($"severity {string.Join("/", query.Severity)}");
        }

        if (query.Environment is { Length: > 0 })
        {
            parts.Add($"environment {string.Join("/", query.Environment)}");
        }

        if (query.Resolved.HasValue)
        {
            parts.Add(query.Resolved.Value ? "resolved only" : "open only");
        }

        if (query.FoundByAutomation.HasValue)
        {
            parts.Add(query.FoundByAutomation.Value ? "found by automation" : "found manually");
        }

        if (query.From.HasValue || query.To.HasValue)
        {
            var from = query.From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "the beginning";
            var to = query.To?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "today";
            parts.Add($"found between {from} and {to}");
        }

        return parts.Count == 0
            ? "All defects, no filters applied"
            : "Filtered: " + string.Join("; ", parts);
    }
}

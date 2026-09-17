using System.Globalization;
using BugTracker.Api.Contracts;
using BugTracker.Api.Data;
using BugTracker.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace BugTracker.Api.Services;

public class BugService(BugTrackerDbContext db) : IBugService
{
    private const int MaxTrendMonths = 36;

    public async Task<PagedResult<BugDto>> GetPageAsync(BugQueryParameters query, CancellationToken ct = default)
    {
        var filtered = ApplyFilters(db.Bugs.AsNoTracking(), query);

        var total = await filtered.CountAsync(ct);

        var items = await ApplySort(filtered, query)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(Projection)
            .ToListAsync(ct);

        return new PagedResult<BugDto>(items, query.Page, query.PageSize, total);
    }

    public async Task<IReadOnlyList<BugDto>> GetAllMatchingAsync(BugQueryParameters query, int maxRows, CancellationToken ct = default)
    {
        var filtered = ApplyFilters(db.Bugs.AsNoTracking(), query);

        // Fetching one row past the limit lets the caller detect an oversized export
        // without a separate COUNT, and never loads more than maxRows + 1 rows.
        return await ApplySort(filtered, query)
            .Take(maxRows + 1)
            .Select(Projection)
            .ToListAsync(ct);
    }

    public async Task<BugDto?> GetByIdAsync(int id, CancellationToken ct = default) =>
        await db.Bugs.AsNoTracking().Where(b => b.Id == id).Select(Projection).FirstOrDefaultAsync(ct);

    public async Task<BugDto> CreateAsync(CreateBugRequest request, CancellationToken ct = default)
    {
        var bug = new Bug
        {
            Title = request.Title.Trim(),
            DateFound = request.DateFound!.Value,
            Description = request.Description.Trim(),
            Severity = request.Severity!.Value,
            Environment = request.Environment!.Value,
            Notes = Normalize(request.Notes),
            Resolved = request.Resolved,
            // A bug can be logged as already-resolved; stamp the close date so the metrics line up.
            DateResolved = request.Resolved ? DateOnly.FromDateTime(DateTime.UtcNow.Date) : null,
            FoundByAutomation = request.FoundByAutomation
        };

        db.Bugs.Add(bug);
        await db.SaveChangesAsync(ct);

        return ToDto(bug);
    }

    public async Task<BugDto?> UpdateAsync(int id, UpdateBugRequest request, CancellationToken ct = default)
    {
        var bug = await db.Bugs.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (bug is null)
        {
            return null;
        }

        var wasResolved = bug.Resolved;

        bug.Title = request.Title.Trim();
        bug.DateFound = request.DateFound!.Value;
        bug.Description = request.Description.Trim();
        bug.Severity = request.Severity!.Value;
        bug.Environment = request.Environment!.Value;
        bug.Notes = Normalize(request.Notes);
        bug.Resolved = request.Resolved;
        bug.FoundByAutomation = request.FoundByAutomation;

        if (request.DateResolved.HasValue)
        {
            bug.DateResolved = request.Resolved ? request.DateResolved : null;
        }
        else if (request.Resolved != wasResolved)
        {
            bug.DateResolved = request.Resolved ? DateOnly.FromDateTime(DateTime.UtcNow.Date) : null;
        }

        await db.SaveChangesAsync(ct);

        return ToDto(bug);
    }

    public async Task<BugDto?> SetResolvedAsync(int id, bool resolved, CancellationToken ct = default)
    {
        var bug = await db.Bugs.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (bug is null)
        {
            return null;
        }

        if (bug.Resolved != resolved)
        {
            bug.Resolved = resolved;
            bug.DateResolved = resolved ? DateOnly.FromDateTime(DateTime.UtcNow.Date) : null;
            await db.SaveChangesAsync(ct);
        }

        return ToDto(bug);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var bug = await db.Bugs.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (bug is null)
        {
            return false;
        }

        db.Bugs.Remove(bug);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<MonthlyTrendPoint>> GetMonthlyTrendAsync(int months, CancellationToken ct = default)
    {
        months = Math.Clamp(months, 2, MaxTrendMonths);

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var currentMonthStart = new DateOnly(today.Year, today.Month, 1);
        var windowStart = currentMonthStart.AddMonths(-(months - 1));

        // Pull one extra month before the window so the first visible bucket still has a
        // baseline to compute its month-over-month delta against.
        var baselineStart = windowStart.AddMonths(-1);
        var exclusiveEnd = currentMonthStart.AddMonths(1);

        // Grouping happens in memory over a two-column projection: DateOnly date-part
        // translation differs between SQLite and SQL Server, and this keeps the rollup
        // identical on both providers. The window is bounded, so the row count stays small.
        var rows = await db.Bugs.AsNoTracking()
            .Where(b => b.DateFound >= baselineStart && b.DateFound < exclusiveEnd)
            .Select(b => new { b.DateFound, b.Resolved })
            .ToListAsync(ct);

        var buckets = rows
            .GroupBy(r => new DateOnly(r.DateFound.Year, r.DateFound.Month, 1))
            .ToDictionary(g => g.Key, g => (Found: g.Count(), Resolved: g.Count(r => r.Resolved)));

        // A month is only comparable when the preceding month is inside recorded history.
        // Without this, the oldest bucket would report its own count as a rise from a
        // month that simply has no data.
        var earliest = await db.Bugs.AsNoTracking()
            .Select(b => (DateOnly?)b.DateFound)
            .MinAsync(ct);

        var historyStart = earliest.HasValue
            ? new DateOnly(earliest.Value.Year, earliest.Value.Month, 1)
            : (DateOnly?)null;

        var points = new List<MonthlyTrendPoint>(months);

        for (var monthStart = windowStart; monthStart <= currentMonthStart; monthStart = monthStart.AddMonths(1))
        {
            buckets.TryGetValue(monthStart, out var current);
            buckets.TryGetValue(monthStart.AddMonths(-1), out var previous);

            var hasBaseline = historyStart.HasValue && monthStart.AddMonths(-1) >= historyStart.Value;
            int? change = hasBaseline ? current.Found - previous.Found : null;

            double? percentChange = hasBaseline && previous.Found > 0
                ? Math.Round((current.Found - previous.Found) / (double)previous.Found * 100d, 1)
                : null;

            points.Add(new MonthlyTrendPoint(
                Month: monthStart.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                Label: monthStart.ToString("MMM yyyy", CultureInfo.InvariantCulture),
                Found: current.Found,
                Resolved: current.Resolved,
                Open: current.Found - current.Resolved,
                Change: change,
                PercentChange: percentChange));
        }

        return points;
    }

    public async Task<DashboardSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var thisMonthStart = new DateOnly(today.Year, today.Month, 1);
        var lastMonthStart = thisMonthStart.AddMonths(-1);
        var nextMonthStart = thisMonthStart.AddMonths(1);

        var totals = await db.Bugs.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Resolved = g.Count(b => b.Resolved),
                OpenCritical = g.Count(b => !b.Resolved && b.Severity == Severity.Critical),
                OpenInProduction = g.Count(b => !b.Resolved && b.Environment == BugEnvironment.Production),
                Automation = g.Count(b => b.FoundByAutomation),
                ThisMonth = g.Count(b => b.DateFound >= thisMonthStart && b.DateFound < nextMonthStart),
                LastMonth = g.Count(b => b.DateFound >= lastMonthStart && b.DateFound < thisMonthStart)
            })
            .FirstOrDefaultAsync(ct);

        if (totals is null)
        {
            return new DashboardSummary(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, null, null);
        }

        int? momChange = totals.ThisMonth - totals.LastMonth;
        double? momPercent = totals.LastMonth > 0
            ? Math.Round((totals.ThisMonth - totals.LastMonth) / (double)totals.LastMonth * 100d, 1)
            : null;

        return new DashboardSummary(
            Total: totals.Total,
            Open: totals.Total - totals.Resolved,
            Resolved: totals.Resolved,
            OpenCritical: totals.OpenCritical,
            OpenInProduction: totals.OpenInProduction,
            FoundByAutomation: totals.Automation,
            AutomationSharePercent: Percent(totals.Automation, totals.Total),
            ResolutionRatePercent: Percent(totals.Resolved, totals.Total),
            FoundThisMonth: totals.ThisMonth,
            FoundLastMonth: totals.LastMonth,
            MonthOverMonthChange: momChange,
            MonthOverMonthPercent: momPercent);
    }

    private static double Percent(int part, int whole) =>
        whole == 0 ? 0 : Math.Round(part / (double)whole * 100d, 1);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private const string LikeEscape = "\\";

    private static string EscapeLikePattern(string term) => term
        .Replace(LikeEscape, LikeEscape + LikeEscape)
        .Replace("%", LikeEscape + "%")
        .Replace("_", LikeEscape + "_")
        .Replace("[", LikeEscape + "[");

    private static IQueryable<Bug> ApplyFilters(IQueryable<Bug> source, BugQueryParameters query)
    {
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Relies on the provider's default case-insensitive collation
            // (SQL Server CI_AS, SQLite LIKE) rather than forcing ToLower(), which would
            // prevent index use on larger tables.
            //
            // User input is escaped so %, _ and [ are matched literally. Unescaped, a
            // search for "_" matches every row and crafted wildcard patterns can force
            // expensive scans.
            var pattern = $"%{EscapeLikePattern(query.Search.Trim())}%";
            source = source.Where(b =>
                EF.Functions.Like(b.Title, pattern, LikeEscape) ||
                EF.Functions.Like(b.Description, pattern, LikeEscape) ||
                (b.Notes != null && EF.Functions.Like(b.Notes, pattern, LikeEscape)));
        }

        if (query.Severity is { Length: > 0 } severities)
        {
            source = source.Where(b => severities.Contains(b.Severity));
        }

        if (query.Environment is { Length: > 0 } environments)
        {
            source = source.Where(b => environments.Contains(b.Environment));
        }

        if (query.Resolved.HasValue)
        {
            source = source.Where(b => b.Resolved == query.Resolved.Value);
        }

        if (query.FoundByAutomation.HasValue)
        {
            source = source.Where(b => b.FoundByAutomation == query.FoundByAutomation.Value);
        }

        if (query.From.HasValue)
        {
            source = source.Where(b => b.DateFound >= query.From.Value);
        }

        if (query.To.HasValue)
        {
            source = source.Where(b => b.DateFound <= query.To.Value);
        }

        return source;
    }

    private static IQueryable<Bug> ApplySort(IQueryable<Bug> source, BugQueryParameters query)
    {
        var descending = !string.Equals(query.Direction, "asc", StringComparison.OrdinalIgnoreCase);

        // Id is the tie-breaker everywhere so paging stays stable across requests.
        return (query.Sort?.ToLowerInvariant()) switch
        {
            "title" => descending
                ? source.OrderByDescending(b => b.Title).ThenByDescending(b => b.Id)
                : source.OrderBy(b => b.Title).ThenBy(b => b.Id),
            // Enums persist as text, so ordering on the column itself would sort alphabetically
            // (Critical, High, Low, Medium). These rank expressions translate to a SQL CASE and
            // keep the business ordering: Critical first, Production last.
            "severity" => descending
                ? source.OrderByDescending(SeverityRank).ThenByDescending(b => b.Id)
                : source.OrderBy(SeverityRank).ThenBy(b => b.Id),
            "environment" => descending
                ? source.OrderByDescending(EnvironmentRank).ThenByDescending(b => b.Id)
                : source.OrderBy(EnvironmentRank).ThenBy(b => b.Id),
            "resolved" => descending
                ? source.OrderByDescending(b => b.Resolved).ThenByDescending(b => b.DateFound).ThenByDescending(b => b.Id)
                : source.OrderBy(b => b.Resolved).ThenByDescending(b => b.DateFound).ThenByDescending(b => b.Id),
            "created" => descending
                ? source.OrderByDescending(b => b.CreatedUtc).ThenByDescending(b => b.Id)
                : source.OrderBy(b => b.CreatedUtc).ThenBy(b => b.Id),
            _ => descending
                ? source.OrderByDescending(b => b.DateFound).ThenByDescending(b => b.Id)
                : source.OrderBy(b => b.DateFound).ThenBy(b => b.Id)
        };
    }

    private static readonly System.Linq.Expressions.Expression<Func<Bug, int>> SeverityRank = b =>
        b.Severity == Severity.Critical ? 0
        : b.Severity == Severity.High ? 1
        : b.Severity == Severity.Medium ? 2
        : 3;

    private static readonly System.Linq.Expressions.Expression<Func<Bug, int>> EnvironmentRank = b =>
        b.Environment == BugEnvironment.Local ? 0
        : b.Environment == BugEnvironment.QA ? 1
        : b.Environment == BugEnvironment.Staging ? 2
        : 3;

    private static readonly System.Linq.Expressions.Expression<Func<Bug, BugDto>> Projection = b => new BugDto(
        b.Id,
        b.Title,
        b.DateFound,
        b.Description,
        b.Severity,
        b.Environment,
        b.Notes,
        b.Resolved,
        b.DateResolved,
        b.FoundByAutomation,
        b.CreatedUtc,
        b.UpdatedUtc);

    private static BugDto ToDto(Bug b) => new(
        b.Id,
        b.Title,
        b.DateFound,
        b.Description,
        b.Severity,
        b.Environment,
        b.Notes,
        b.Resolved,
        b.DateResolved,
        b.FoundByAutomation,
        b.CreatedUtc,
        b.UpdatedUtc);
}

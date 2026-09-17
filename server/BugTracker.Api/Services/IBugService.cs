using BugTracker.Api.Contracts;

namespace BugTracker.Api.Services;

public interface IBugService
{
    Task<PagedResult<BugDto>> GetPageAsync(BugQueryParameters query, CancellationToken ct = default);

    /// <summary>
    /// Rows matching the filters, ignoring paging, for the CSV and PDF exporters.
    /// Returns at most <paramref name="maxRows"/> + 1 rows so callers can tell when the
    /// result set is over the limit.
    /// </summary>
    Task<IReadOnlyList<BugDto>> GetAllMatchingAsync(BugQueryParameters query, int maxRows, CancellationToken ct = default);

    Task<BugDto?> GetByIdAsync(int id, CancellationToken ct = default);

    Task<BugDto> CreateAsync(CreateBugRequest request, CancellationToken ct = default);

    Task<BugDto?> UpdateAsync(int id, UpdateBugRequest request, CancellationToken ct = default);

    Task<BugDto?> SetResolvedAsync(int id, bool resolved, CancellationToken ct = default);

    Task<bool> DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>Bugs found per calendar month, with the delta against the preceding month.</summary>
    Task<IReadOnlyList<MonthlyTrendPoint>> GetMonthlyTrendAsync(int months, CancellationToken ct = default);

    Task<DashboardSummary> GetSummaryAsync(CancellationToken ct = default);
}

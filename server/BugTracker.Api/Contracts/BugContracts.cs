using System.ComponentModel.DataAnnotations;
using BugTracker.Api.Domain;

namespace BugTracker.Api.Contracts;

/// <summary>A defect as returned to clients.</summary>
public record BugDto(
    int Id,
    string Title,
    DateOnly DateFound,
    string Description,
    Severity Severity,
    BugEnvironment Environment,
    string? Notes,
    bool Resolved,
    DateOnly? DateResolved,
    bool FoundByAutomation,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

/// <summary>Payload for creating a defect. Notes are optional and can be captured up front.</summary>
public class CreateBugRequest
{
    [Required(ErrorMessage = "A short title is required.")]
    [StringLength(200, MinimumLength = 3)]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "The date the bug was found is required.")]
    public DateOnly? DateFound { get; set; }

    [Required(ErrorMessage = "A description is required.")]
    [StringLength(4000, MinimumLength = 5)]
    public string Description { get; set; } = string.Empty;

    [Required]
    [EnumDataType(typeof(Severity), ErrorMessage = "Severity must be Critical, High, Medium or Low.")]
    public Severity? Severity { get; set; }

    [Required]
    [EnumDataType(typeof(BugEnvironment), ErrorMessage = "Environment must be Local, QA, Staging or Production.")]
    public BugEnvironment? Environment { get; set; }

    [StringLength(4000)]
    public string? Notes { get; set; }

    public bool Resolved { get; set; }

    public bool FoundByAutomation { get; set; }
}

/// <summary>Full replacement payload for an existing defect.</summary>
public class UpdateBugRequest : CreateBugRequest
{
    /// <summary>
    /// Optional. When omitted the API manages the resolved date automatically
    /// (stamped when <see cref="CreateBugRequest.Resolved"/> flips to true, cleared when it flips back).
    /// </summary>
    public DateOnly? DateResolved { get; set; }
}

/// <summary>Partial update used by the grid's inline resolve toggle.</summary>
public class SetResolvedRequest
{
    [Required]
    public bool? Resolved { get; set; }
}

/// <summary>Filtering, sorting and paging options shared by the grid and both exporters.</summary>
public class BugQueryParameters
{
    private const int MaxPageSize = 200;

    // (MaxPage - 1) * MaxPageSize must stay inside int range, otherwise the SQL
    // OFFSET overflows to a negative number.
    private const int MaxPage = 1_000_000;

    private int _pageSize = 25;
    private int _page = 1;

    /// <summary>Free-text match against title, description and notes.</summary>
    [StringLength(200, ErrorMessage = "Search terms are limited to 200 characters.")]
    public string? Search { get; set; }

    public Severity[]? Severity { get; set; }

    public BugEnvironment[]? Environment { get; set; }

    public bool? Resolved { get; set; }

    public bool? FoundByAutomation { get; set; }

    /// <summary>Inclusive lower bound on DateFound.</summary>
    public DateOnly? From { get; set; }

    /// <summary>Inclusive upper bound on DateFound.</summary>
    public DateOnly? To { get; set; }

    /// <summary>One of: dateFound, severity, environment, title, resolved, created.</summary>
    public string Sort { get; set; } = "dateFound";

    /// <summary>asc or desc.</summary>
    public string Direction { get; set; } = "desc";

    public int Page
    {
        get => _page;
        set => _page = Math.Clamp(value, 1, MaxPage);
    }

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value switch
        {
            < 1 => 25,
            > MaxPageSize => MaxPageSize,
            _ => value
        };
    }
}

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;
}

/// <summary>One bucket of the month-over-month trend.</summary>
/// <param name="Month">Sortable key, e.g. 2026-09.</param>
/// <param name="Label">Display label, e.g. Sep 2026.</param>
/// <param name="Found">Defects with a DateFound inside this month.</param>
/// <param name="Resolved">Defects found in this month that have since been resolved.</param>
/// <param name="Open">Found minus Resolved.</param>
/// <param name="Change">Found delta against the previous month; null for the first bucket.</param>
/// <param name="PercentChange">Percentage delta against the previous month; null when there is no comparable baseline.</param>
public record MonthlyTrendPoint(
    string Month,
    string Label,
    int Found,
    int Resolved,
    int Open,
    int? Change,
    double? PercentChange);

/// <summary>Headline numbers for the dashboard tiles.</summary>
public record DashboardSummary(
    int Total,
    int Open,
    int Resolved,
    int OpenCritical,
    int OpenInProduction,
    int FoundByAutomation,
    double AutomationSharePercent,
    double ResolutionRatePercent,
    int FoundThisMonth,
    int FoundLastMonth,
    int? MonthOverMonthChange,
    double? MonthOverMonthPercent);

namespace BugTracker.Api.Domain;

/// <summary>A single defect report.</summary>
public class Bug
{
    public int Id { get; set; }

    /// <summary>Short summary used as the headline in lists and reports.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Calendar date the defect was observed (not the row's insert date).</summary>
    public DateOnly DateFound { get; set; }

    public string Description { get; set; } = string.Empty;

    public Severity Severity { get; set; }

    public BugEnvironment Environment { get; set; }

    /// <summary>Free-form notes captured at creation time or appended later.</summary>
    public string? Notes { get; set; }

    public bool Resolved { get; set; }

    /// <summary>Set automatically when <see cref="Resolved"/> flips to true, cleared when it flips back.</summary>
    public DateOnly? DateResolved { get; set; }

    /// <summary>True when an automated test surfaced the defect rather than a person.</summary>
    public bool FoundByAutomation { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }
}

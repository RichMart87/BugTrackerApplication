namespace BugTracker.Api.Domain;

/// <summary>Impact of the defect. Ordered from most to least severe.</summary>
public enum Severity
{
    Critical = 0,
    High = 1,
    Medium = 2,
    Low = 3
}

/// <summary>Environment the defect was observed in.</summary>
public enum BugEnvironment
{
    Local = 0,
    QA = 1,
    Staging = 2,
    Production = 3
}

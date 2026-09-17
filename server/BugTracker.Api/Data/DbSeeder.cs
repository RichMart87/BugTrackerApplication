using BugTracker.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace BugTracker.Api.Data;

/// <summary>
/// Seeds a spread of demo defects across the last 14 months so the dashboard and the
/// month-over-month trend have something to render on a fresh database.
/// Controlled by <c>Database:SeedSampleData</c>; it is a no-op when rows already exist.
/// </summary>
public static class DbSeeder
{
    private static readonly string[] Titles =
    [
        "Login button unresponsive on first click",
        "Order total ignores promotional discount",
        "Export to CSV drops the final row",
        "Session expires after 5 minutes instead of 60",
        "Avatar upload fails for PNG files over 2 MB",
        "Search returns archived records",
        "Timezone offset applied twice on invoice dates",
        "Password reset email never delivered",
        "Dashboard chart renders blank on Safari",
        "Pagination resets when a filter is applied",
        "Duplicate webhook delivered on retry",
        "Null reference when customer has no billing address",
        "Report totals disagree with the detail grid",
        "Bulk import silently skips malformed rows",
        "API returns 500 instead of 400 for bad payloads",
        "Mobile nav traps keyboard focus",
        "Currency rounding is off by one cent",
        "Stale cache served after a product update",
        "Concurrent saves overwrite each other",
        "PDF export truncates long descriptions"
    ];

    private static readonly string[] Descriptions =
    [
        "Reproduced consistently on a clean profile. The request is issued but the response handler never fires, leaving the UI in a pending state.",
        "Calculation runs before the discount is applied to the cart, so the persisted total is higher than the amount displayed at checkout.",
        "Off-by-one in the writer loop. The last buffered record is never flushed before the stream closes.",
        "Sliding expiration is not being refreshed on activity, so the cookie falls back to the default lifetime.",
        "Upload succeeds against the API but the thumbnail pipeline rejects the file and the transaction is rolled back without surfacing an error.",
        "The soft-delete predicate is missing from the search projection, so archived rows leak into results.",
        "The value is converted to UTC on write and again on read, shifting dates by the local offset.",
        "Message is queued but the provider rejects it because the sender domain fails DMARC validation.",
        "Canvas element receives a zero height before the data loads and is never re-measured.",
        "Filter change dispatches a new query without resetting the page index, so the grid requests a page that no longer exists.",
        "Retry policy does not check the idempotency key, so downstream consumers process the event twice."
    ];

    private static readonly string?[] Notes =
    [
        "Attached HAR file and console output to the ticket.",
        "Regression - worked correctly in the previous release.",
        "Blocked the nightly regression suite until a workaround was applied.",
        "Reported by two separate customers within the same hour.",
        "Only reproducible with a seeded data set larger than 10k rows.",
        "Workaround: refresh the page and retry the action.",
        null,
        null
    ];

    public static async Task SeedAsync(BugTrackerDbContext db, CancellationToken cancellationToken = default)
    {
        if (await db.Bugs.AnyAsync(cancellationToken))
        {
            return;
        }

        // Fixed seed keeps demo data stable between runs and across machines.
        var random = new Random(20260915);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var firstOfThisMonth = new DateOnly(today.Year, today.Month, 1);

        // Monthly volumes that deliberately rise and fall so the trend chart shows
        // both increases and decreases month over month.
        int[] monthlyVolume = [9, 12, 8, 14, 17, 11, 15, 21, 16, 12, 19, 23, 18, 13];

        var bugs = new List<Bug>();

        for (var index = 0; index < monthlyVolume.Length; index++)
        {
            var monthsAgo = monthlyVolume.Length - 1 - index;
            var monthStart = firstOfThisMonth.AddMonths(-monthsAgo);
            var daysInMonth = DateTime.DaysInMonth(monthStart.Year, monthStart.Month);
            var count = monthlyVolume[index];

            // In the current month, spread the defects over the days elapsed so far rather
            // than the whole month - clamping to "today" would pile them all onto one date.
            var lastDay = monthStart.Year == today.Year && monthStart.Month == today.Month
                ? today.Day
                : daysInMonth;

            for (var i = 0; i < count; i++)
            {
                var day = random.Next(1, lastDay + 1);
                var dateFound = new DateOnly(monthStart.Year, monthStart.Month, day);

                var severity = WeightedSeverity(random);
                var environment = WeightedEnvironment(random);

                // Older defects are far more likely to have been closed out.
                var resolveChance = monthsAgo switch
                {
                    0 => 0.25,
                    1 => 0.55,
                    2 => 0.75,
                    _ => 0.92
                };

                var resolved = random.NextDouble() < resolveChance;
                DateOnly? dateResolved = null;

                if (resolved)
                {
                    var turnaround = severity switch
                    {
                        Severity.Critical => random.Next(0, 4),
                        Severity.High => random.Next(1, 10),
                        Severity.Medium => random.Next(3, 25),
                        _ => random.Next(5, 45)
                    };

                    dateResolved = dateFound.AddDays(turnaround);
                    if (dateResolved > today)
                    {
                        dateResolved = today;
                    }
                }

                // Clamp to "now" so a defect dated today never carries a future timestamp.
                var now = DateTime.UtcNow;
                var createdUtc = dateFound.ToDateTime(new TimeOnly(random.Next(8, 19), random.Next(0, 60)), DateTimeKind.Utc);
                if (createdUtc > now)
                {
                    createdUtc = now;
                }

                bugs.Add(new Bug
                {
                    Title = Titles[random.Next(Titles.Length)],
                    DateFound = dateFound,
                    Description = Descriptions[random.Next(Descriptions.Length)],
                    Severity = severity,
                    Environment = environment,
                    Notes = Notes[random.Next(Notes.Length)],
                    Resolved = resolved,
                    DateResolved = dateResolved,
                    // Automation catches proportionally more defects in the lower environments.
                    FoundByAutomation = random.NextDouble() < (environment is BugEnvironment.Local or BugEnvironment.QA ? 0.55 : 0.18),
                    // Back-dated stamps are preserved by the context, which only fills in blanks.
                    CreatedUtc = createdUtc,
                    UpdatedUtc = dateResolved.HasValue
                        ? Min(dateResolved.Value.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc), now)
                        : createdUtc
                });
            }
        }

        db.Bugs.AddRange(bugs);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    private static Severity WeightedSeverity(Random random) => random.NextDouble() switch
    {
        < 0.10 => Severity.Critical,
        < 0.35 => Severity.High,
        < 0.75 => Severity.Medium,
        _ => Severity.Low
    };

    private static BugEnvironment WeightedEnvironment(Random random) => random.NextDouble() switch
    {
        < 0.18 => BugEnvironment.Local,
        < 0.58 => BugEnvironment.QA,
        < 0.80 => BugEnvironment.Staging,
        _ => BugEnvironment.Production
    };
}

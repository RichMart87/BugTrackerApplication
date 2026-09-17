using System.Globalization;
using BugTracker.Api.Contracts;
using BugTracker.Api.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BugTracker.Api.Services;

/// <summary>
/// Renders a printable defect report: headline metrics, a month-over-month bar chart
/// and the filtered defect table.
/// </summary>
public class PdfBugExporter
{
    private const string Ink = "#0f172a";
    private const string Muted = "#64748b";
    private const string Line = "#e2e8f0";
    private const string Accent = "#2a78d6";
    private const string Surface = "#f8fafc";

    public byte[] Generate(
        IReadOnlyList<BugDto> bugs,
        IReadOnlyList<MonthlyTrendPoint> trend,
        DashboardSummary summary,
        string filterSummary)
    {
        var generatedAt = DateTime.UtcNow;

        return Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(24);
                page.DefaultTextStyle(text => text.FontSize(8.5f).FontColor(Ink));

                page.Header().Element(container => ComposeHeader(container, generatedAt, filterSummary, bugs.Count));
                page.Content().Element(container => ComposeContent(container, bugs, trend, summary));
                page.Footer().Element(ComposeFooter);
            });
        }).GeneratePdf();
    }

    private static void ComposeHeader(IContainer container, DateTime generatedAt, string filterSummary, int rowCount)
    {
        container.PaddingBottom(12).Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Column(title =>
                {
                    title.Item().Text("Bug Report").FontSize(20).SemiBold().FontColor(Ink);
                    title.Item().PaddingTop(2).Text(filterSummary).FontSize(8).FontColor(Muted);
                });

                row.ConstantItem(190).AlignRight().Column(meta =>
                {
                    meta.Item().AlignRight().Text("BugTracker").FontSize(10).SemiBold().FontColor(Accent);
                    meta.Item().AlignRight().Text($"Generated {generatedAt:yyyy-MM-dd HH:mm} UTC").FontSize(8).FontColor(Muted);
                    meta.Item().AlignRight().Text($"{rowCount} record{(rowCount == 1 ? string.Empty : "s")} in this report").FontSize(8).FontColor(Muted);
                });
            });

            column.Item().PaddingTop(10).LineHorizontal(1).LineColor(Line);
        });
    }

    private static void ComposeContent(
        IContainer container,
        IReadOnlyList<BugDto> bugs,
        IReadOnlyList<MonthlyTrendPoint> trend,
        DashboardSummary summary)
    {
        container.Column(column =>
        {
            column.Item().Element(c => ComposeMetrics(c, summary));

            // The tiles and the chart are portfolio-wide; only the table honours the filters.
            column.Item().PaddingTop(5).Text("Metrics and trend cover every recorded defect. The table lists only the rows matching this report's filters.")
                .FontSize(7).FontColor(Muted);

            if (trend.Count > 0)
            {
                column.Item().PaddingTop(14).Element(c => ComposeTrend(c, trend));
            }

            column.Item().PaddingTop(14).Element(c => ComposeTable(c, bugs));
        });
    }

    private static void ComposeMetrics(IContainer container, DashboardSummary summary)
    {
        var momLabel = summary.MonthOverMonthPercent is { } percent
            ? $"{(percent >= 0 ? "+" : string.Empty)}{percent.ToString("0.#", CultureInfo.InvariantCulture)}% vs last month"
            : "No prior month baseline";

        container.Row(row =>
        {
            Metric(row, "Total bugs", summary.Total.ToString(CultureInfo.InvariantCulture), $"{summary.ResolutionRatePercent.ToString("0.#", CultureInfo.InvariantCulture)}% resolved");
            Metric(row, "Open", summary.Open.ToString(CultureInfo.InvariantCulture), $"{summary.OpenCritical} critical / {summary.OpenInProduction} in production");
            Metric(row, "Found this month", summary.FoundThisMonth.ToString(CultureInfo.InvariantCulture), momLabel);
            Metric(row, "Found by automation", summary.FoundByAutomation.ToString(CultureInfo.InvariantCulture), $"{summary.AutomationSharePercent.ToString("0.#", CultureInfo.InvariantCulture)}% of all defects");
        });

        static void Metric(RowDescriptor row, string label, string value, string caption)
        {
            row.RelativeItem().PaddingRight(8).Background(Surface).Border(1).BorderColor(Line)
                .PaddingVertical(8).PaddingHorizontal(10).Column(card =>
                {
                    card.Item().Text(label.ToUpperInvariant()).FontSize(7).SemiBold().FontColor(Muted).LetterSpacing(0.08f);
                    card.Item().PaddingTop(2).Text(value).FontSize(17).SemiBold().FontColor(Ink);
                    card.Item().Text(caption).FontSize(7.5f).FontColor(Muted);
                });
        }
    }

    private static void ComposeTrend(IContainer container, IReadOnlyList<MonthlyTrendPoint> trend)
    {
        // Keep the printed chart readable: the last 12 buckets at most.
        var points = trend.Count > 12 ? trend.Skip(trend.Count - 12).ToList() : trend.ToList();
        var peak = Math.Max(1, points.Max(p => p.Found));
        const float chartHeight = 78f;

        container.Border(1).BorderColor(Line).Padding(12).Column(column =>
        {
            column.Item().Text("Bugs found month over month").FontSize(10).SemiBold();
            column.Item().PaddingTop(1).Text("Bar height is the count of bugs found; the delta below each bar compares it with the preceding month.")
                .FontSize(7.5f).FontColor(Muted);

            column.Item().PaddingTop(10).Row(row =>
            {
                foreach (var point in points)
                {
                    var barHeight = Math.Max(2f, (float)point.Found / peak * chartHeight);

                    row.RelativeItem().PaddingHorizontal(4).Column(bar =>
                    {
                        bar.Item().AlignCenter().Text(point.Found.ToString(CultureInfo.InvariantCulture))
                            .FontSize(7.5f).SemiBold();

                        bar.Item().Height(chartHeight).AlignBottom().AlignCenter().Width(20).Height(barHeight).Background(Accent);

                        bar.Item().PaddingTop(4).AlignCenter().Text(point.Label.Replace(' ', '\n'))
                            .FontSize(6.5f).FontColor(Muted).LineHeight(1.1f);

                        bar.Item().AlignCenter().Text(DeltaLabel(point))
                            .FontSize(6.5f).SemiBold().FontColor(DeltaColor(point));
                    });
                }
            });
        });
    }

    private static string DeltaLabel(MonthlyTrendPoint point) => point.Change switch
    {
        null => "-",
        0 => "0",
        > 0 => $"+{point.Change}",
        _ => point.Change.Value.ToString(CultureInfo.InvariantCulture)
    };

    private static string DeltaColor(MonthlyTrendPoint point) => point.Change switch
    {
        // More bugs found than last month reads as a regression, fewer as an improvement.
        > 0 => "#dc2626",
        < 0 => "#059669",
        _ => Muted
    };

    private static void ComposeTable(IContainer container, IReadOnlyList<BugDto> bugs)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(28);   // Id
                columns.RelativeColumn(2.0f); // Title
                columns.ConstantColumn(64);   // Date found - fits yyyy-MM-dd on one line
                columns.RelativeColumn(2.9f); // Description
                columns.ConstantColumn(50);   // Severity
                columns.ConstantColumn(60);   // Environment
                columns.RelativeColumn(1.7f); // Notes
                columns.ConstantColumn(50);   // Resolved
                columns.ConstantColumn(58);   // Automation
            });

            table.Header(header =>
            {
                HeaderCell(header, "ID");
                HeaderCell(header, "Title");
                HeaderCell(header, "Date found");
                HeaderCell(header, "Description");
                HeaderCell(header, "Severity");
                HeaderCell(header, "Environment");
                HeaderCell(header, "Notes");
                HeaderCell(header, "Resolved");
                HeaderCell(header, "Automation");
            });

            if (bugs.Count == 0)
            {
                table.Cell().ColumnSpan(9).Element(Cell).AlignCenter()
                    .Text("No bugs matched the selected filters.").FontColor(Muted).Italic();
                return;
            }

            var striped = false;

            foreach (var bug in bugs)
            {
                string background = striped ? "#f8fafc" : "#ffffff";
                striped = !striped;

                table.Cell().Element(c => Cell(c).Background(background)).Text(bug.Id.ToString(CultureInfo.InvariantCulture)).FontColor(Muted);
                table.Cell().Element(c => Cell(c).Background(background)).Text(bug.Title).SemiBold();
                table.Cell().Element(c => Cell(c).Background(background)).Text(bug.DateFound.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                table.Cell().Element(c => Cell(c).Background(background)).Text(Truncate(bug.Description, 260));
                table.Cell().Element(c => Cell(c).Background(background)).Text(bug.Severity.ToString()).SemiBold().FontColor(SeverityColor(bug.Severity));
                table.Cell().Element(c => Cell(c).Background(background)).Text(bug.Environment.ToString());
                table.Cell().Element(c => Cell(c).Background(background)).Text(string.IsNullOrWhiteSpace(bug.Notes) ? "-" : Truncate(bug.Notes, 180)).FontColor(Muted);
                table.Cell().Element(c => Cell(c).Background(background)).Text(bug.Resolved ? "Yes" : "No")
                    .SemiBold().FontColor(bug.Resolved ? "#059669" : "#b45309");
                table.Cell().Element(c => Cell(c).Background(background)).Text(bug.FoundByAutomation ? "Yes" : "No");
            }
        });

        static void HeaderCell(TableCellDescriptor header, string label) =>
            header.Cell().Background(Ink).PaddingVertical(5).PaddingHorizontal(5)
                .Text(label).FontSize(7.5f).SemiBold().FontColor(Colors.White);

        // ShowEntire keeps a row whole instead of splitting it across a page break.
        // Safe because the long text columns are truncated above, so no row can
        // outgrow a single page.
        static IContainer Cell(IContainer container) =>
            container.ShowEntire().BorderBottom(1).BorderColor(Line).PaddingVertical(4).PaddingHorizontal(5);
    }

    /// <summary>Keeps printed rows compact; the full text always lives in the app and the CSV.</summary>
    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength].TrimEnd() + "…";

    private static string SeverityColor(Severity severity) => severity switch
    {
        Severity.Critical => "#be123c",
        Severity.High => "#c2410c",
        Severity.Medium => "#b45309",
        _ => "#0369a1"
    };

    private static void ComposeFooter(IContainer container)
    {
        container.PaddingTop(8).BorderTop(1).BorderColor(Line).PaddingTop(6).Row(row =>
        {
            row.RelativeItem().Text("BugTracker - confidential defect report").FontSize(7.5f).FontColor(Muted);

            row.ConstantItem(120).AlignRight().Text(text =>
            {
                text.DefaultTextStyle(style => style.FontSize(7.5f).FontColor(Muted));
                text.Span("Page ");
                text.CurrentPageNumber();
                text.Span(" of ");
                text.TotalPages();
            });
        });
    }
}

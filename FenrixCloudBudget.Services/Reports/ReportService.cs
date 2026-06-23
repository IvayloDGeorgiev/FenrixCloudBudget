using System.Globalization;
using System.Text;
using FenrixCloudBudget.Data;
using FenrixCloudBudget.Services.Analytics;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace FenrixCloudBudget.Services.Reports;

/// <summary>What to report on — mirrors the dashboard's filters.</summary>
public record ReportRequest(string Provider, int? ClientId, DateOnly From, DateOnly To);

/// <summary>A computed report ready to render to CSV or PDF.</summary>
public sealed record ReportContext(
    string ClientName,
    string ProviderLabel,
    DateOnly From,
    DateOnly To,
    DateTimeOffset GeneratedUtc,
    DashboardData Data)
{
    public string PeriodLabel => $"{From:dd MMM yyyy} – {To:dd MMM yyyy}";
    public string SuggestedFileName(string extension)
    {
        var client = new string((ClientName ?? "workspace")
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray());
        return $"Fenrix-report_{client}_{From:yyyyMMdd}-{To:yyyyMMdd}.{extension}";
    }
}

/// <summary>
/// Builds an executive-summary budget report (the dashboard's key numbers) and renders it as a
/// spreadsheet-friendly CSV or a polished PDF. Reuses <see cref="DashboardAnalytics"/> so the
/// report always matches what's on screen.
/// </summary>
public sealed class ReportService
{
    private readonly DashboardAnalytics _analytics;
    private readonly IDbContextFactory<AppDbContext> _dbf;

    static ReportService()
    {
        // QuestPDF Community licence (free for individuals and companies under the revenue threshold).
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public ReportService(DashboardAnalytics analytics, IDbContextFactory<AppDbContext> dbf)
    {
        _analytics = analytics;
        _dbf = dbf;
    }

    public async Task<ReportContext> BuildAsync(ReportRequest request, CancellationToken ct = default)
    {
        var data = await _analytics.GetAsync(
            new DashboardQuery(request.Provider, request.ClientId, request.From, request.To), ct);

        var clientName = "All clients";
        if (request.ClientId is { } id)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            clientName = await db.Clients.Where(c => c.Id == id).Select(c => c.Name).FirstOrDefaultAsync(ct)
                         ?? "Client";
        }

        var providerLabel = request.Provider == "All" ? "All clouds" : request.Provider;
        return new ReportContext(clientName, providerLabel, request.From, request.To, DateTimeOffset.Now, data);
    }

    // ---------------------------------------------------------------- CSV

    public byte[] RenderCsv(ReportContext ctx)
    {
        var d = ctx.Data;
        var sb = new StringBuilder();

        void Row(params string[] cells) => sb.AppendLine(string.Join(",", cells.Select(Escape)));
        void Blank() => sb.AppendLine();

        Row("Cloud Budget Report");
        Row("Client", ctx.ClientName);
        Row("Provider", ctx.ProviderLabel);
        Row("Period", ctx.PeriodLabel);
        Row("Generated", ctx.GeneratedUtc.ToString("yyyy-MM-dd HH:mm"));
        Row("Data mode", d.DataMode);
        if (!string.IsNullOrWhiteSpace(d.LastSyncedLabel)) Row("Sync", d.LastSyncedLabel);
        Blank();

        Row("Key figures");
        Row("Metric", "Value");
        Row("Spend in range", Num(d.TotalSpend));
        Row("Month-to-date spend", Num(d.MonthToDateSpend));
        Row("Monthly budget", Num(d.MonthlyBudget));
        Row("Projected month-end", Num(d.ProjectedMonthEnd));
        Row("Budget runway", d.DaysUntilBudgetExhausted is { } dx ? (dx <= 0 ? "Over budget" : $"{dx} days") : "On track");
        Row("Month over month %", d.MoMChangePercent.ToString("0.#", CultureInfo.InvariantCulture));
        Row("Pace variance", Num(d.PaceVariance));
        Blank();

        Row("Budget health");
        Row("Status", "Projects");
        Row("On track", d.BudgetsOnTrack.ToString());
        Row("Approaching", d.BudgetsApproaching.ToString());
        Row("Over", d.BudgetsOver.ToString());
        Blank();

        Row("Spend by provider");
        Row("Provider", "Amount");
        foreach (var kv in d.ByProvider.OrderByDescending(x => x.Value))
            Row(kv.Key, Num((decimal)kv.Value));
        Blank();

        Row("Budget vs actual (by project)");
        Row("Project", "Budget", "Spend", "Used %");
        foreach (var b in d.Budgets)
            Row(b.Project, Num(b.Budget), Num(b.Spend), b.Percent.ToString());
        Blank();

        Row("Spend by project");
        Row("Project", "Amount");
        foreach (var p in d.ByProject)
            Row(p.Label, Num((decimal)p.Value));
        Blank();

        Row("Top cost drivers");
        Row("Service", "Amount");
        foreach (var kv in d.TopServices.OrderByDescending(x => x.Value))
            Row(kv.Key, Num((decimal)kv.Value));

        // UTF-8 BOM so Excel detects encoding (and currency glyphs) correctly.
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    private static string Num(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Escape(string? value)
    {
        value ??= "";
        return value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }

    // ---------------------------------------------------------------- PDF

    public byte[] RenderPdf(ReportContext ctx)
    {
        var d = ctx.Data;
        var accent = "#6C5CE7";
        var muted = "#6B7280";

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(t => t.FontSize(10).FontColor(Colors.Grey.Darken3));

                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Cloud Budget Report").FontSize(20).Bold().FontColor(accent);
                            c.Item().Text($"{ctx.ClientName}  ·  {ctx.ProviderLabel}").FontSize(11).FontColor(muted);
                        });
                        row.ConstantItem(150).AlignRight().Column(c =>
                        {
                            c.Item().AlignRight().Text("FenrixCloudBudget").SemiBold();
                            c.Item().AlignRight().Text(ctx.PeriodLabel).FontSize(9).FontColor(muted);
                            c.Item().AlignRight().Text($"Generated {ctx.GeneratedUtc:dd MMM yyyy HH:mm}").FontSize(8).FontColor(muted);
                        });
                    });
                    col.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                });

                page.Content().PaddingVertical(12).Column(col =>
                {
                    col.Spacing(16);

                    // Key figures as a 4-up band.
                    col.Item().Row(row =>
                    {
                        row.Spacing(10);
                        Kpi(row, "Spend in range", Money(d.TotalSpend), d.DataMode, accent);
                        Kpi(row, "Projected month-end", Money(d.ProjectedMonthEnd),
                            d.MonthlyBudget > 0 ? $"vs {Money(d.MonthlyBudget)} budget" : "no budget set", accent);
                        Kpi(row, "Budget runway",
                            d.DaysUntilBudgetExhausted is { } dx ? (dx <= 0 ? "Over" : $"{dx} days") : "On track",
                            $"{Money(d.MonthToDateSpend)} MTD", accent);
                        Kpi(row, "Month over month",
                            $"{(d.MoMChangePercent >= 0 ? "+" : "")}{d.MoMChangePercent.ToString("0.#", CultureInfo.CurrentCulture)}%",
                            $"prev {Money(d.PrevMonthSpend)}", accent);
                    });

                    // Budget health summary line.
                    col.Item().Text(text =>
                    {
                        text.Span("Budget health:  ").SemiBold();
                        text.Span($"{d.BudgetsOnTrack} on track").FontColor(Colors.Green.Darken1);
                        text.Span("    ");
                        text.Span($"{d.BudgetsApproaching} approaching").FontColor(Colors.Orange.Darken1);
                        text.Span("    ");
                        text.Span($"{d.BudgetsOver} over").FontColor(Colors.Red.Darken1);
                    });

                    if (d.ByProvider.Count > 0)
                        Section(col, "Spend by provider", t =>
                            TwoColTable(t, "Provider", "Amount",
                                d.ByProvider.OrderByDescending(x => x.Value)
                                    .Select(x => (x.Key, Money((decimal)x.Value)))));

                    if (d.Budgets.Count > 0)
                        Section(col, "Budget vs actual (by project)", t =>
                        {
                            t.Table(table =>
                            {
                                table.ColumnsDefinition(c => { c.RelativeColumn(3); c.RelativeColumn(2); c.RelativeColumn(2); c.RelativeColumn(1); });
                                HeaderCell(table, "Project"); HeaderCell(table, "Budget", true); HeaderCell(table, "Spend", true); HeaderCell(table, "Used", true);
                                foreach (var b in d.Budgets)
                                {
                                    BodyCell(table, b.Project);
                                    BodyCell(table, Money(b.Budget), true);
                                    BodyCell(table, Money(b.Spend), true);
                                    var color = b.Percent >= 100 ? Colors.Red.Darken1 : b.Percent >= 80 ? Colors.Orange.Darken1 : Colors.Green.Darken1;
                                    table.Cell().PaddingVertical(4).AlignRight().Text($"{b.Percent}%").FontColor(color);
                                }
                            });
                        });

                    if (d.TopServices.Count > 0)
                        Section(col, "Top cost drivers", t =>
                            TwoColTable(t, "Service", "Amount",
                                d.TopServices.OrderByDescending(x => x.Value)
                                    .Select(x => (x.Key, Money((decimal)x.Value)))));
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Generated by FenrixCloudBudget · fenrixsource.com").FontSize(8).FontColor(muted);
                });
            });
        }).GeneratePdf();

        static string Money(decimal v) => v.ToString("C0", CultureInfo.CurrentCulture);

        static void Kpi(RowDescriptor row, string label, string value, string foot, string accent)
            => row.RelativeItem().Background(Colors.Grey.Lighten4).Padding(10).Column(c =>
            {
                c.Item().Text(label).FontSize(8).FontColor(Colors.Grey.Darken1);
                c.Item().PaddingTop(3).Text(value).FontSize(14).Bold().FontColor(accent);
                c.Item().PaddingTop(2).Text(foot).FontSize(7).FontColor(Colors.Grey.Darken1);
            });

        static void Section(ColumnDescriptor col, string title, Action<IContainer> body)
            => col.Item().Column(c =>
            {
                c.Item().PaddingBottom(4).Text(title).FontSize(12).SemiBold();
                body(c.Item());
            });

        static void TwoColTable(IContainer container, string h1, string h2, IEnumerable<(string Label, string Value)> rows)
            => container.Table(table =>
            {
                table.ColumnsDefinition(c => { c.RelativeColumn(3); c.RelativeColumn(1); });
                HeaderCell(table, h1); HeaderCell(table, h2, true);
                foreach (var (label, value) in rows)
                {
                    BodyCell(table, label);
                    BodyCell(table, value, true);
                }
            });

        static void HeaderCell(TableDescriptor table, string text, bool right = false)
        {
            var cell = table.Cell().PaddingVertical(4).BorderBottom(1).BorderColor(Colors.Grey.Lighten2);
            (right ? cell.AlignRight() : cell.AlignLeft())
                .Text(text).FontSize(8).SemiBold().FontColor(Colors.Grey.Darken1);
        }

        static void BodyCell(TableDescriptor table, string text, bool right = false)
        {
            var cell = table.Cell().PaddingVertical(4);
            (right ? cell.AlignRight() : cell.AlignLeft()).Text(text);
        }
    }
}

using System.Globalization;
using SIQS.Contracts;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace QS.Presentation;

/// <summary>
/// Turns a <see cref="ProgressSnapshot"/> into a Spectre renderable: one row per phase with a status
/// glyph, name, and either a live progress bar (with elapsed and estimated remaining time) or the
/// completed summary. Pure rendering — it holds no state beyond the animation frame passed in.
/// </summary>
internal static class LiveViewRenderer
{
    private const int BarWidth = 22;
    private static readonly string[] Spinner = { "⣾", "⣽", "⣻", "⢿", "⡿", "⣟", "⣯", "⣷" };

    public static IRenderable Render(ProgressSnapshot snapshot, int frame)
    {
        var spinner = Spinner[frame % Spinner.Length];
        var grid = new Grid();
        grid.AddColumn(new GridColumn().Width(2).NoWrap());
        grid.AddColumn(new GridColumn().Width(15).NoWrap());
        grid.AddColumn(new GridColumn());

        foreach (var row in snapshot.Rows)
        {
            grid.AddRow(
                new Markup(Glyph(row.State, spinner)),
                new Markup(Label(row)),
                Detail(row));
        }

        if (snapshot.Notes.Count == 0)
        {
            return grid;
        }

        var rows = new List<IRenderable> { grid };
        rows.AddRange(snapshot.Notes.Select(note => new Markup("  " + note)));
        return new Rows(rows);
    }

    private static string Glyph(PhaseRunState state, string spinner) => state switch
    {
        PhaseRunState.Done => "[green]✔[/]",
        PhaseRunState.Active => $"[aqua]{spinner}[/]",
        _ => "[grey37]·[/]",
    };

    private static string Label(PhaseRowSnapshot row)
    {
        var name = Markup.Escape(PhaseText.DisplayName(row.Phase));
        return row.State switch
        {
            PhaseRunState.Done => name,
            PhaseRunState.Active => $"[bold]{name}[/]",
            _ => $"[grey37]{name}[/]",
        };
    }

    private static IRenderable Detail(PhaseRowSnapshot row) => row.State switch
    {
        PhaseRunState.Done => new Markup($"[grey]{Markup.Escape(row.Summary ?? string.Empty)}[/]"),
        PhaseRunState.Active when row.Fraction is { } fraction => new Markup(Bar(fraction, row)),
        PhaseRunState.Active => new Markup("[grey]working…[/]"),
        _ => new Markup(string.Empty),
    };

    private static string Bar(double fraction, PhaseRowSnapshot row)
    {
        var filled = (int)Math.Round(fraction * BarWidth, MidpointRounding.AwayFromZero);
        filled = Math.Clamp(filled, 0, BarWidth);
        var bar = $"[aqua]{new string('━', filled)}[/][grey37]{new string('─', BarWidth - filled)}[/]";
        var percent = ((int)Math.Round(fraction * 100.0)).ToString(CultureInfo.InvariantCulture).PadLeft(3);
        var timing = $"{FormatTime(row.Elapsed)} · ~{Remaining(fraction, row.Elapsed)} left";
        var detail = string.IsNullOrEmpty(row.Detail) ? string.Empty : $"  [grey37]{Markup.Escape(row.Detail)}[/]";
        return $"{bar} [white]{percent}%[/]  [grey]{timing}[/]{detail}";
    }

    private static string Remaining(double fraction, TimeSpan elapsed)
        => fraction <= 0.02
            ? "…"
            : FormatTime(TimeSpan.FromSeconds(elapsed.TotalSeconds * (1.0 - fraction) / fraction));

    private static string FormatTime(TimeSpan span)
        => span.TotalHours >= 1.0
            ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : span.TotalSeconds >= 60.0
                ? $"{span.Minutes}:{span.Seconds:00}"
                : $"{span.TotalSeconds:0}s";
}

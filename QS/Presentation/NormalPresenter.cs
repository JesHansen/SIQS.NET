using System.Globalization;
using System.Numerics;
using SIQS.Contracts;
using SIQS.Pipeline;
using Spectre.Console;

namespace QS.Presentation;

/// <summary>
/// The default rich presentation: a compact header, a live phase panel with progress bars and
/// time estimates for the long-running sieving and linear-algebra phases, and a styled result.
/// Falls back to plain per-phase lines when the terminal is non-interactive (e.g. redirected).
/// </summary>
internal sealed class NormalPresenter : IRunPresenter, IProgress<SiqsProgressEvent>
{
    private const int RefreshMs = 100;

    private readonly PhaseProgressModel _model = new();
    private readonly bool _interactive =
        AnsiConsole.Profile.Capabilities.Interactive && AnsiConsole.Profile.Capabilities.Ansi;

    public void ShowTarget(BigInteger target)
    {
        var digits = target.ToString(CultureInfo.InvariantCulture).Length;
        AnsiConsole.Write(new Rule("[aqua]quadratic sieve[/]").RuleStyle("grey37").LeftJustified());
        AnsiConsole.MarkupLine($"  [grey]N =[/] {target} [grey]({digits} digits)[/]");
        AnsiConsole.WriteLine();
    }

    public async Task<FactorizationJobResult> RunAsync(
        Func<IProgress<SiqsProgressEvent>, Task<FactorizationJobResult>> execute)
    {
        if (!_interactive)
        {
            return await execute(this);
        }

        FactorizationJobResult? result = null;
        await AnsiConsole.Live(LiveViewRenderer.Render(_model.Snapshot(), 0))
            .AutoClear(false)
            .Overflow(VerticalOverflow.Visible)
            .StartAsync(async ctx =>
            {
                var run = execute(this);
                using var finished = new ManualResetEventSlim(false);

                // Repaint from a dedicated OS thread rather than an async loop. The sieve saturates
                // the thread pool, which would starve await-continuations and visibly freeze the
                // display; a real thread with a timed wait keeps a steady cadence regardless.
                var renderer = new Thread(() =>
                {
                    var frame = 0;
                    while (!finished.Wait(RefreshMs))
                    {
                        ctx.UpdateTarget(LiveViewRenderer.Render(_model.Snapshot(), frame++));
                        ctx.Refresh();
                    }

                    ctx.UpdateTarget(LiveViewRenderer.Render(_model.Snapshot(), frame));
                    ctx.Refresh();
                })
                { IsBackground = true, Name = "qs-tui" };
                renderer.Start();

                try
                {
                    result = await run.ConfigureAwait(false);
                }
                finally
                {
                    finished.Set();
                    renderer.Join();
                }
            });

        return result!;
    }

    public void Report(SiqsProgressEvent value)
    {
        if (value.Phase == SiqsPhase.Pipeline)
        {
            if (value.Message == "job workspace created")
            {
                _model.MarkActive(SiqsPhase.FactorBase);
            }
            else if (value.Level == ProgressLevel.Warning)
            {
                _model.AddNote($"[yellow]![/] [grey]{Markup.Escape(value.Message)}[/]");
            }

            return;
        }

        if (value.Message == "phase completed")
        {
            var summary = PhaseText.Summarize(value.Phase, value.Counters);
            _model.Complete(value.Phase, summary);
            if (!_interactive && summary.Length > 0)
            {
                AnsiConsole.MarkupLine($"  [green]✔[/] [bold]{Markup.Escape(PhaseText.DisplayName(value.Phase))}[/]  [grey]{Markup.Escape(summary)}[/]");
            }

            return;
        }

        if (!_interactive)
        {
            return;
        }

        if (value.Phase == SiqsPhase.Sieving && value.Message == "sieving")
        {
            _model.ReportProgress(SiqsPhase.Sieving, SievingFraction(value), SievingDetail(value));
        }
        else if (value.Phase == SiqsPhase.LinearAlgebra)
        {
            _model.ReportProgress(SiqsPhase.LinearAlgebra, LinearAlgebraFraction(value), LinearAlgebraDetail(value));
        }
    }

    public void ShowOutcome(FactorizationCommandResult execution)
    {
        var result = execution.Result;
        AnsiConsole.WriteLine();

        if (execution.TrialSieve)
        {
            TrialSieveReporter.Write(result);
        }
        else if (result.Status is JobStatus.CompletedFactorFound or JobStatus.CompletedTrivialFactor)
        {
            var factors = string.Join(" [grey]×[/] ", result.Factors.Select(f => $"[aqua]{f}[/]"));
            AnsiConsole.MarkupLine("[bold green]✔ factored[/]");
            AnsiConsole.MarkupLine($"  [grey]{execution.Target} =[/]");
            AnsiConsole.MarkupLine($"  {factors}");
        }
        else if (result.Status == JobStatus.CompletedNoFactor)
        {
            AnsiConsole.MarkupLine($"[yellow]no non-trivial factor found[/] [grey]({result.AttemptedDependencies} dependencies attempted)[/]");
        }
        else if (result.Status == JobStatus.CompletedPrime)
        {
            AnsiConsole.MarkupLine($"[green]{execution.Target} is prime[/]");
        }
        else if (result.Status == JobStatus.CompletedProbablePrime)
        {
            AnsiConsole.MarkupLine($"[green]{execution.Target} is a Baillie-PSW probable prime[/]");
        }
        else if (result.Status == JobStatus.Canceled)
        {
            AnsiConsole.MarkupLine("[yellow]canceled[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]failed:[/] {Markup.Escape(result.ErrorSummary ?? "unknown error")}");
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(
            $"[grey37]job {Markup.Escape(result.JobId)} · {execution.Elapsed.TotalSeconds:F1}s · "
            + $"artifacts: {Markup.Escape(execution.ResolvedArtifactDirectory)}[/]");
    }

    private static double SievingFraction(SiqsProgressEvent e)
        => e.Counters.ContainsKey("trial_raw_target")
            ? Ratio(Get(e, "raw_relations"), Get(e, "trial_raw_target"))
            : Ratio(Get(e, "usable_relations"), Get(e, "relations_needed"));

    private static string SievingDetail(SiqsProgressEvent e)
        => e.Counters.ContainsKey("trial_raw_target")
            ? $"{Get(e, "raw_relations")}/{Get(e, "trial_raw_target")} raw"
            : $"{Get(e, "usable_relations")}/{Get(e, "relations_needed")} rel";

    private static double LinearAlgebraFraction(SiqsProgressEvent e)
        => Ratio(Get(e, "dimensions_solved"), Get(e, "target_dimensions"));

    private static string LinearAlgebraDetail(SiqsProgressEvent e)
        => $"run {Get(e, "run")} · {Get(e, "dimensions_solved")}/{Get(e, "target_dimensions")} dims";

    private static double Ratio(string numerator, string denominator)
        => double.TryParse(numerator, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
            && double.TryParse(denominator, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            && d > 0
            ? n / d
            : 0.0;

    private static string Get(SiqsProgressEvent e, string key) => e.Counters.GetValueOrDefault(key, "0");
}

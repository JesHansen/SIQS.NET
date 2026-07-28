using System.Numerics;
using SIQS.Contracts;
using SIQS.Pipeline;

namespace QS.Presentation;

/// <summary>
/// Owns everything the capstone tool shows the user for one run: the target header, live phase
/// progress, and the final outcome. Implementations pick the presentation style (rich TUI, verbose
/// debug log, or a single machine-readable line) while sharing the same execution flow.
/// </summary>
internal interface IRunPresenter
{
    /// <summary>Announces the number about to be factored.</summary>
    void ShowTarget(BigInteger target);

    /// <summary>
    /// Runs the pipeline, wiring progress reporting to this presenter. The delegate is invoked with
    /// the <see cref="IProgress{T}"/> the run should report to; a presenter may wrap the call in a
    /// live display.
    /// </summary>
    Task<FactorizationJobResult> RunAsync(
        Func<IProgress<SiqsProgressEvent>, Task<FactorizationJobResult>> execute);

    /// <summary>Reports the final result (factors, no-factor, cancellation, or failure).</summary>
    void ShowOutcome(FactorizationCommandResult execution);
}

using System.Text;
using QS;
using QS.Presentation;
using SIQS.Contracts;
using SIQS.Contracts.Cli;

const string Usage = "usage: qs <number> [options] or qs --resume <run-dir> [options]\n"
    + "  --quiet   print only the factor product (for pipelines)\n"
    + "  --debug   print the full per-phase diagnostic output";

// The default Windows console code page best-fit-maps the TUI glyphs (✔, ×, →, bars, spinner)
// to '?' before they reach the terminal. UTF-8 output (without a BOM, so redirected output stays
// clean) lets them through. Guarded because a detached/redirected process may have no console.
try { Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false); }
catch (IOException) { /* no console attached; nothing to configure */ }

try
{
    if (args.Any(argument => argument.Equals("--help", StringComparison.OrdinalIgnoreCase)
        || argument.Equals("-h", StringComparison.OrdinalIgnoreCase)))
    {
        Console.WriteLine(Usage);
        return 0;
    }

    var cli = CommandLine.Parse(args,
        CommandLineSyntax.FlagAware(allowPositional: true, "quiet", "debug", "continue-after-factor"));
    var presenter = RunPresenterFactory.Create(cli);

    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };

    var execution = await new FactorizationCommandHandler().ExecuteAsync(cli, presenter, cancellation.Token);
    presenter.ShowOutcome(execution);

    var result = execution.Result;
    return execution.TrialSieve ? 0
        : result.Status is JobStatus.CompletedFactorFound or JobStatus.CompletedTrivialFactor ? 0
        : result.Status is JobStatus.CompletedNoFactor or JobStatus.CompletedPrime ? 2 : 1;
}
catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException
    or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    Console.Error.WriteLine(Usage);
    return 1;
}

using QsSieve;
using SIQS.Contracts;
using SIQS.Contracts.Cli;

const string Usage = "usage: qs-sieve [--factor-base factor_base.txt] [--out-dir .] [sieving options]\n"
    + "  --factor-base <path>                 factor base written by qs-fb (default factor_base.txt)\n"
    + "  --out-dir <path>                     where to write raw relation batches (default .)\n"
    + "  --batch-size <n>                     relations per raw batch file (default 10000)\n"
    + "  --trial-sieve-percent <p>            stop after p% of the relation target, for timing runs\n"
    + "  --trial-relations-target <n>         stop after n raw relations (alternative to the above)\n"
    + "\n"
    + "Every sieving option accepted by qs is accepted here with the same meaning and the same\n"
    + "defaults; see docs/tuning.md for the full list.";

try
{
    if (CommandLine.IsHelpRequested(args))
    {
        Console.WriteLine(Usage);
        return 0;
    }

    var result = new SievingCommandHandler().Execute(
        CommandLine.Parse(args, CommandLineSyntax.Strict),
        new Progress<SiqsProgressEvent>(e => Console.Error.WriteLine($"  [sieving]     {e.Message}")));
    SievingReportFormatter.Write(result);
    return 0;
}
catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    Console.Error.WriteLine(Usage);
    return 1;
}

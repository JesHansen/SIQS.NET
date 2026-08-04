using QsSieve;
using SIQS.Contracts;
using SIQS.Contracts.Cli;

const string Usage = "usage: qs-sieve --factor-base factor_base.txt --out-dir . [sieving options]";

try
{
    if (args.Contains("--help") || args.Contains("-h"))
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

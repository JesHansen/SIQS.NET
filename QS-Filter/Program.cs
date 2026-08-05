using Filtering;
using QS_Filter;
using SIQS.Contracts;
using SIQS.Contracts.Cli;

const string Usage = "usage: qs-filter --factor-base factor_base.txt --relations relations_*.txt --partials partials_*.txt --out-dir . [--max-partials-per-prime N] [--max-cycle-length N] [--enable-two-merge true|false] [--two-merge-slack N]";

try
{
    if (CommandLine.IsHelpRequested(args))
    {
        Console.WriteLine(Usage);
        return 0;
    }

    var command = FilteringCommand.Parse(args);
    var progress = new Progress<SiqsProgressEvent>(e =>
    {
        Console.Error.WriteLine($"  [filtering]     {e.Message}");
        foreach (var (key, value) in e.Counters.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            Console.Error.WriteLine($"  {key}={value}");
        }
    });
    var execution = new FilteringCommandHandler().Execute(command, progress);
    foreach (var line in FilteringReportFormatter.Format(execution))
    {
        Console.WriteLine($"  {line}");
    }
    return 0;
}
catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    Console.Error.WriteLine(Usage);
    return 1;
}

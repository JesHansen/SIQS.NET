using Factorbase;
using QS_FB;
using SIQS.Contracts;
using SIQS.Contracts.Cli;
using SIQS.Contracts.Files;

const string Usage = "usage: qs-fb --n <TargetN> [--bound <bound>] [--multiplier <k>] [--out factor_base.txt]\n"
    + "  --n <TargetN>       the number whose factor base to build (required)\n"
    + "  --bound <bound>     smoothness bound; chosen from the size of N when omitted\n"
    + "  --multiplier <k>    Knuth-Schroeppel multiplier; chosen automatically when omitted\n"
    + "  --out <path>        where to write the factor base (default factor_base.txt)";

try
{
    if (CommandLine.IsHelpRequested(args))
    {
        Console.WriteLine(Usage);
        return 0;
    }

    var command = FactorBaseCommand.Parse(args);

    var progress = new Progress<SiqsProgressEvent>(e => Console.Error.WriteLine($"{Prefix("[factor base]")}{e.Message}"));
    var result = FactorBaseGenerator.Generate(new FactorBaseOptions(command.TargetN, command.Bound, command.Multiplier), progress);

    if (result.HasEarlyOutcome)
    {
        var factorsPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(command.OutputPath)) ?? ".", "factors.txt");
        File.WriteAllText(factorsPath, FactorsFile.Write(result.EarlyOutcome!));
        var row = result.EarlyOutcome!.Results[0];
        Console.WriteLine(row.Status switch
        {
            FactorizationStatus.InputPrime => $"{Prefix("[factor base]")}input is prime",
            _ => $"{Prefix("[factor base]")}trivial factor found ({row.Reason}): {row.Factor1} * {row.Factor2}",
        });
        Console.WriteLine($"{Prefix("[write]")}{factorsPath}");
        return 0;
    }

    var doc = result.FactorBase!;
    File.WriteAllText(command.OutputPath, FactorBaseFile.Write(doc));
    Console.WriteLine($"{Prefix("[factor base]")}primes={doc.Entries.Count}, multiplier={doc.Metadata.Multiplier}, bound={doc.Metadata.Bound}");
    Console.WriteLine($"{Prefix("[write]")}{command.OutputPath}");
    return 0;
}
catch (Exception ex) when (ex is FormatException or ArgumentException)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    Console.Error.WriteLine(Usage);
    return 1;
}

static string Prefix(string label) => $"  {label.PadRight(17)}";

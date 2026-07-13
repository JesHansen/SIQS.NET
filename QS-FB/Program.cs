using Factorbase;
using QS_FB;
using SIQS.Contracts;
using SIQS.Contracts.Files;

try
{
    var command = FactorBaseCommand.Parse(args);

    var progress = new Progress<SiqsProgressEvent>(e => Console.Error.WriteLine($"{Prefix("[factor base]")}{e.Message}"));
    var result = FactorBaseGenerator.Generate(new FactorBaseOptions(command.TargetN, command.Bound, command.Multiplier), progress);

    if (result.FoundEarlyFactor)
    {
        var factorsPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(command.OutputPath)) ?? ".", "factors.txt");
        File.WriteAllText(factorsPath, FactorsFile.Write(result.EarlyFactors!));
        var row = result.EarlyFactors!.Results[0];
        Console.WriteLine($"{Prefix("[factor base]")}trivial factor found ({row.Reason}): {row.Factor1} * {row.Factor2}");
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
    Console.Error.WriteLine("usage: qs-fb --n <TargetN> [--bound <bound>] [--multiplier <k>] [--out factor_base.txt]");
    return 1;
}

static string Prefix(string label) => $"  {label.PadRight(17)}";

using QsSqrt;
using SIQS.Contracts;
using SIQS.Contracts.Cli;
using SIQS.Contracts.Files;
using SquareRoot;

const string Usage =
    "usage: qs-sqrt [--factor-base factor_base.txt] [--relations relations_filtered.txt]\n"
    + "               [--dependencies dependencies.txt] [--out factors.txt] [--continue-after-factor]\n"
    + "  --factor-base <path>      factor base written by qs-fb (default factor_base.txt)\n"
    + "  --relations <path>        filtered relations written by qs-filter (default relations_filtered.txt)\n"
    + "  --dependencies <path>     null-space vectors written by qs-linalg (default dependencies.txt)\n"
    + "  --out <path>              where to write the factors (default factors.txt)\n"
    + "  --continue-after-factor   keep trying dependencies after the first non-trivial factor";

try
{
    if (CommandLine.IsHelpRequested(args))
    {
        Console.WriteLine(Usage);
        return 0;
    }

    var command = SquareRootCommand.Parse(args);

    var factorBase = FactorBaseFile.Parse(File.ReadAllText(command.FactorBasePath));
    var relations = FilteredRelationsFile.Parse(File.ReadAllText(command.RelationsPath));
    var dependencies = DependenciesFile.Parse(File.ReadAllText(command.DependenciesPath));

    // Metadata must agree across the input files.
    if (relations.ScaledN != factorBase.Metadata.ScaledN || dependencies.ScaledN != factorBase.Metadata.ScaledN)
    {
        throw new FormatException("Input files disagree on scaled_n.");
    }

    var progress = new Progress<SiqsProgressEvent>(e => Console.Error.WriteLine($"{Prefix("[square root]")}{e.Message}"));
    var result = SquareRootEngine.Run(factorBase, relations, dependencies, new SquareRootOptions(command.ContinueAfterFactor), progress);

    File.WriteAllText(command.OutputPath, FactorsFile.Write(result.Factors));

    if (result.Factor1 is { } f1 && result.Factor2 is { } f2)
    {
        Console.WriteLine($"{Prefix("[square root]")}factor found: {f1} * {f2} = {f1 * f2}");

        var row = result.Factors.Results.First(r => r.Status == FactorizationStatus.FactorFound);
        if (row.Factor1IsComposite == true || row.Factor2IsComposite == true)
        {
            var which = row.Factor1IsComposite == true && row.Factor2IsComposite == true
                ? "both factors are"
                : row.Factor1IsComposite == true ? "factor1 is" : "factor2 is";
            Console.WriteLine($"{Prefix("[square root]")}note: {which} composite; N has more than two prime factors, so at least one side needs further factoring.");
        }
    }
    else
    {
        Console.WriteLine($"{Prefix("[square root]")}no non-trivial factor found across the attempted dependencies");
    }

    Console.WriteLine($"{Prefix("[write]")}{command.OutputPath}");
    return 0;
}
catch (Exception ex) when (ex is FormatException or ArgumentException or FileNotFoundException)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    Console.Error.WriteLine(Usage);
    return 1;
}

static string Prefix(string label) => $"  {label.PadRight(17)}";

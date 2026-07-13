using QsSqrt;
using SIQS.Contracts;
using SIQS.Contracts.Files;
using SquareRoot;

try
{
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
    Console.Error.WriteLine("usage: qs-sqrt --factor-base factor_base.txt --relations relations_filtered.txt --dependencies dependencies.txt --out factors.txt [--continue-after-factor]");
    return 1;
}

static string Prefix(string label) => $"  {label.PadRight(17)}";

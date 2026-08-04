using SIQS.Contracts.Cli;

namespace QsSqrt;

/// <summary>
/// Validated command-line input for the square-root CLI.
/// </summary>
internal sealed record SquareRootCommand(
    string FactorBasePath,
    string RelationsPath,
    string DependenciesPath,
    string OutputPath,
    bool ContinueAfterFactor)
{
    public static SquareRootCommand Parse(string[] args)
    {
        var cli = CommandLine.Parse(args, CommandLineSyntax.FlagAware());
        return new SquareRootCommand(
            cli.GetOptional("factor-base") ?? "factor_base.txt",
            cli.GetOptional("relations") ?? "relations_filtered.txt",
            cli.GetOptional("dependencies") ?? "dependencies.txt",
            cli.GetOptional("out") ?? "factors.txt",
            cli.GetFlag("continue-after-factor"));
    }
}

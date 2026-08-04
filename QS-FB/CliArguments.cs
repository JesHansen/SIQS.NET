using System.Numerics;
using SIQS.Contracts.Cli;

namespace QS_FB;

/// <summary>Validated command-line input for the factor-base CLI.</summary>
internal sealed record FactorBaseCommand(BigInteger TargetN, long? Bound, BigInteger? Multiplier, string OutputPath)
{
    public static FactorBaseCommand Parse(string[] args)
    {
        var cli = CommandLine.Parse(args, CommandLineSyntax.Strict);
        return new FactorBaseCommand(
            cli.GetBigInteger("n") ?? throw new FormatException("Required option '--n' was not supplied."),
            cli.GetLong("bound"),
            cli.GetBigInteger("multiplier"),
            cli.GetOptional("out") ?? "factor_base.txt");
    }
}

namespace Sieving;

/// <summary>The cofactor-splitting strategy for two-large-prime residuals.</summary>
public enum CofactorSplitterKind
{
    /// <summary>SQUFOF only (no rho fallback). Sufficient when every residual fits in 64 bits.</summary>
    Squfof,

    /// <summary>
    /// SQUFOF first, falling back to Pollard's rho when SQUFOF does not split. Required when
    /// residuals may exceed 64 bits, because SQUFOF is 64-bit only and rho is the sole splitter
    /// that handles wider values.
    /// </summary>
    SqufofRho,
}

/// <summary>
/// Parses and formats <see cref="CofactorSplitterKind"/> at the CLI and persistence boundaries. Tokens
/// are lowercase (<c>squfof</c>, <c>squfof-rho</c>) and parsed case-insensitively.
/// </summary>
public static class CofactorSplitterKinds
{
    /// <summary>The default strategy when none is supplied and no bound is available to auto-select from.</summary>
    public const CofactorSplitterKind Default = CofactorSplitterKind.Squfof;

    /// <summary>
    /// Auto-selects the splitter from the two-large-prime bound. Every acceptable residual is at most
    /// <c>bound²</c>; that fits in a 64-bit word exactly when <c>bound ≤ uint.MaxValue</c> (since
    /// <c>(2³²)²</c> overflows a <see cref="ulong"/>). In that regime SQUFOF alone splits every
    /// residual it will ever see; otherwise the rho fallback is required for the &gt; 64-bit residuals.
    /// </summary>
    public static CofactorSplitterKind SelectFor(long largePrime2Bound)
        => largePrime2Bound <= uint.MaxValue
            ? CofactorSplitterKind.Squfof
            : CofactorSplitterKind.SqufofRho;

    public static bool TryParse(string? token, out CofactorSplitterKind kind)
    {
        switch (token?.Trim().ToLowerInvariant())
        {
            case "squfof":
                kind = CofactorSplitterKind.Squfof;
                return true;
            case "squfof-rho":
                kind = CofactorSplitterKind.SqufofRho;
                return true;
            default:
                kind = Default;
                return false;
        }
    }

    public static string ToToken(this CofactorSplitterKind kind) => kind switch
    {
        CofactorSplitterKind.Squfof => "squfof",
        CofactorSplitterKind.SqufofRho => "squfof-rho",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown cofactor splitter kind."),
    };
}

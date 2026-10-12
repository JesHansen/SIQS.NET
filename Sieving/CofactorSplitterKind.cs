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

    /// <summary>One managed micro-ECM curve followed by SQUFOF when ECM does not split.</summary>
    MicroEcmSqufof,

    /// <summary>
    /// Micro-ECM with a full baby-step/giant-step stage two, then SQUFOF, then Pollard's rho as the
    /// correctness-preserving fallback. Accepts exactly the <see cref="SqufofRho"/> pair set but is
    /// substantially faster on the larger residuals a wide two-large-prime policy produces
    /// (Experiment 37). This is the default whenever two-large-primes are enabled.
    /// </summary>
    MicroEcmStage2,
}

/// <summary>
/// Parses and formats <see cref="CofactorSplitterKind"/> at the CLI and persistence boundaries. Tokens
/// are lowercase and parsed case-insensitively.
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

    /// <summary>
    /// True when the strategy ends in Pollard's rho, the only splitter that handles residuals wider
    /// than 64 bits.
    /// </summary>
    public static bool SplitsWideResiduals(this CofactorSplitterKind kind)
        => kind is CofactorSplitterKind.SqufofRho or CofactorSplitterKind.MicroEcmStage2;

    /// <summary>
    /// The effective strategy when none is chosen explicitly: keeps the size-tuned
    /// <paramref name="preferred"/> default whenever it can split every residual the LP2 bound
    /// admits, and widens to <see cref="SelectFor"/> otherwise. An explicit choice bypasses this.
    /// </summary>
    public static CofactorSplitterKind AutoSelect(CofactorSplitterKind preferred, long largePrime2Bound)
        => preferred.SplitsWideResiduals() || SelectFor(largePrime2Bound) == CofactorSplitterKind.Squfof
            ? preferred
            : SelectFor(largePrime2Bound);

    /// <summary>
    /// Resolves an optional CLI/persisted token against the size-tuned default and the effective LP2
    /// bound. A supplied token must be valid and is authoritative.
    /// </summary>
    public static CofactorSplitterKind Resolve(string? token, CofactorSplitterKind preferred, long largePrime2Bound)
    {
        if (token is null)
        {
            return AutoSelect(preferred, largePrime2Bound);
        }

        return TryParse(token, out var kind)
            ? kind
            : throw new ArgumentException(
                $"Unknown cofactor splitter '{token}'. Expected squfof, squfof-rho, micro-ecm-squfof, or micro-ecm-stage2.",
                nameof(token));
    }

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
            case "micro-ecm-squfof":
                kind = CofactorSplitterKind.MicroEcmSqufof;
                return true;
            case "micro-ecm-stage2":
                kind = CofactorSplitterKind.MicroEcmStage2;
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
        CofactorSplitterKind.MicroEcmSqufof => "micro-ecm-squfof",
        CofactorSplitterKind.MicroEcmStage2 => "micro-ecm-stage2",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown cofactor splitter kind."),
    };
}

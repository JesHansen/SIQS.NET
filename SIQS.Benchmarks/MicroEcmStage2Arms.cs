using Sieving;

namespace SIQS.Benchmarks;

/// <summary>
/// Experiment 37 replay arms: micro-ECM with the real baby-step/giant-step stage two, both pure
/// (to see raw ECM yield/speed) and as the correctness-preserving hybrid "ECM then SQUFOF". Naming:
/// <c>ecm2-{B1}-{B2}-{curves}</c> for pure and a <c>-squfof</c> suffix for the hybrid.
/// </summary>
internal static class MicroEcmStage2Arms
{
    // The tuning sweep that led to the production (300, 12000, 10) point. The winner is a clear
    // replay winner across every C110 corpus at byte-identical yield; the neighbours document the
    // shape of the optimum (larger B2 costs more than the fallback it saves; too few curves loses
    // to slow squfof-rho fallbacks).
    private static readonly (int B1, int B2, int Curves)[] Configurations =
    [
        (300, 12_000, 10),   // production point
        (250, 12_000, 10),
        (300, 10_000, 8),
        (300, 15_000, 10),
        (300, 30_000, 8),
        (300, 30_000, 4),
        (300, 30_000, 2),
        (600, 30_000, 4),
        (1_000, 50_000, 4),
    ];

    public static void Register(Dictionary<string, Func<ulong, ulong>> arms)
    {
        foreach (var (b1, b2, curves) in Configurations)
        {
            var localB1 = b1;
            var localB2 = b2;
            var localCurves = curves;
            arms[$"ecm2-{b1}-{b2}-{curves}"] =
                value => MicroEcm64.TryFactorStage2(value, localB1, localB2, localCurves);
            // Hybrid: ECM first, then fall back to the full SQUFOF+rho path. Because the fallback
            // covers everything squfof-rho splits, this accepts exactly the squfof-rho pair set
            // (identical checksum) and can only be faster, never lower-yield.
            arms[$"ecm2-{b1}-{b2}-{curves}-squfof"] = value =>
            {
                var factor = MicroEcm64.TryFactorStage2(value, localB1, localB2, localCurves);
                if (factor > 1) return factor;
                factor = CofactorFactorizer.Squfof64(value);
                return factor > 1 ? factor : CofactorFactorizer.PollardRho64(value);
            };
        }
    }
}

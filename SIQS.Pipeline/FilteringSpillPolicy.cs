using System.Numerics;

namespace SIQS.Pipeline;

/// <summary>
/// Decides when the pipeline routes filtering through candidate payload spill. Spill is a steady time
/// tax (~10% at C80) that only pays for itself once peak filtering memory is the constraint. Measured
/// at C60 it saved nothing (candidate payloads were noise against the runtime baseline); at C80 it cut
/// ~15% of peak. Composites up to the C90s crack comfortably in memory, so the pipeline keeps them
/// fully in memory and turns spill on only from C100 up.
/// </summary>
internal static class FilteringSpillPolicy
{
    /// <summary>Composite decimal-digit count at or above which the pipeline spills candidate payloads.</summary>
    public const int SpillDigitThreshold = 100;

    public static bool ShouldSpill(BigInteger targetN)
        => BigInteger.Abs(targetN).ToString().Length >= SpillDigitThreshold;
}

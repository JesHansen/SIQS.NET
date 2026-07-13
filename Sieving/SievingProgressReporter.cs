using System.Globalization;
using SIQS.Contracts;

namespace Sieving;

/// <summary>Translates sieve counters into <see cref="SiqsProgressEvent"/> reports.</summary>
internal static class SievingProgressReporter
{
    /// <summary>Reports the lightweight shared counters emitted periodically during a run.</summary>
    public static void ReportShared(
        IProgress<SiqsProgressEvent>? progress,
        long polyCount,
        long fullCount,
        long partialCount,
        long approxUsable,
        int factorBaseCount,
        SievingParameters parameters)
    {
        var counterMap = new Dictionary<string, string>
        {
            ["polynomials"] = polyCount.ToString(),
            ["full_relations"] = fullCount.ToString(),
            ["partial_relations"] = partialCount.ToString(),
            ["raw_relations"] = (fullCount + partialCount).ToString(),
            ["usable_relations"] = approxUsable.ToString(),
            ["relations_needed"] = parameters.RelationTarget.ToString(),
            ["projected_matrix_rows"] = approxUsable.ToString(),
            ["projected_matrix_columns"] = (factorBaseCount + 1L).ToString(),
        };
        if (parameters.EnableTwoLargePrimes)
        {
            counterMap["large_prime2_bound"] = parameters.LargePrime2Bound.ToString();
            counterMap["large_prime2_threshold_bound"] = parameters.LargePrime2ThresholdBound.ToString();
        }

        if (parameters.TrialRawRelationTarget is { } trialTarget)
        {
            counterMap["trial_raw_target"] = trialTarget.ToString();
        }

        progress?.Report(new SiqsProgressEvent(
            DateTimeOffset.UtcNow, null, SiqsPhase.Sieving, ProgressLevel.Info, "sieving",
            Percent: null,
            Counters: counterMap,
            ArtifactPath: null));
    }

    /// <summary>Reports the full counter set; a null <paramref name="current"/> marks completion.</summary>
    public static void Report(IProgress<SiqsProgressEvent>? progress, SievingCounters counters, string? current)
    {
        var counterMap = new Dictionary<string, string>
        {
            ["polynomials"] = counters.Polynomials.ToString(),
            ["candidates"] = counters.Candidates.ToString(),
            ["blocks"] = counters.Blocks.ToString(),
            ["candidates_per_block"] = counters.Blocks > 0
                ? ((double)counters.Candidates / counters.Blocks).ToString("F3", CultureInfo.InvariantCulture)
                : "0",
            ["full_relations"] = counters.FullRelations.ToString(),
            ["partial_relations"] = counters.Partials.ToString(),
            ["one_large_prime_partials"] = counters.OneLargePrimePartials.ToString(),
            ["two_large_prime_partials"] = counters.TwoLargePrimePartials.ToString(),
            ["two_large_prime_split_attempts"] = counters.TwoLargePrimeSplitAttempts.ToString(),
            ["two_large_prime_split_successes"] = counters.TwoLargePrimeSplitSuccesses.ToString(),
            ["two_large_prime_residual_too_small"] = counters.TwoLargePrimeResidualTooSmall.ToString(),
            ["two_large_prime_residual_too_large"] = counters.TwoLargePrimeResidualTooLarge.ToString(),
            ["two_large_prime_residual_prime"] = counters.TwoLargePrimeResidualPrime.ToString(),
            ["two_large_prime_residual_small_factor"] = counters.TwoLargePrimeResidualSmallFactor.ToString(),
            ["two_large_prime_residual_bits_le32"] = counters.TwoLargePrimeResidualBitsLe32.ToString(),
            ["two_large_prime_residual_bits_le48"] = counters.TwoLargePrimeResidualBitsLe48.ToString(),
            ["two_large_prime_residual_bits_le64"] = counters.TwoLargePrimeResidualBitsLe64.ToString(),
            ["two_large_prime_residual_bits_gt64"] = counters.TwoLargePrimeResidualBitsGt64.ToString(),
            ["cofactor_squfof_attempts"] = counters.CofactorSqufofAttempts.ToString(),
            ["cofactor_squfof_successes"] = counters.CofactorSqufofSuccesses.ToString(),
            ["cofactor_rho_attempts"] = counters.CofactorRhoAttempts.ToString(),
            ["cofactor_rho_successes"] = counters.CofactorRhoSuccesses.ToString(),
            ["bucket_overflow_hits"] = counters.BucketOverflowHits.ToString(),
            ["bucket_slab_bytes_per_worker"] = counters.BucketSlabBytesPerWorker.ToString(),
            ["raw_relations"] = counters.RawRelations.ToString(),
            ["discarded"] = counters.Discarded.ToString(),
            ["usable_relations"] = counters.UsableRelations.ToString(),
            ["usable_partial_pairs"] = counters.UsablePartialPairs.ToString(),
            ["zero_parity_full_relations"] = counters.ZeroParityFullRelations.ToString(),
            ["zero_parity_partial_pairs"] = counters.ZeroParityPartialPairs.ToString(),
            ["relations_needed"] = counters.RelationTarget.ToString(),
            ["projected_matrix_rows"] = counters.ProjectedMatrixRows.ToString(),
            ["projected_matrix_columns"] = counters.ProjectedMatrixColumns.ToString(),
            ["cpu_setup_ms"] = counters.SetupCpuMs.ToString(),
            ["cpu_sieve_fill_ms"] = counters.SieveFillCpuMs.ToString(),
            ["cpu_scan_ms"] = counters.ScanCpuMs.ToString(),
            ["cpu_trial_div_ms"] = counters.TrialDivCpuMs.ToString(),
        };
        if (counters.TrialRawRelationTarget is { } trialTarget)
        {
            counterMap["trial_raw_target"] = trialTarget.ToString();
        }

        progress?.Report(new SiqsProgressEvent(
            DateTimeOffset.UtcNow, null, SiqsPhase.Sieving, ProgressLevel.Info,
            current is null ? "sieving complete" : "sieving",
            Percent: null,
            Counters: counterMap,
            ArtifactPath: null));
    }
}

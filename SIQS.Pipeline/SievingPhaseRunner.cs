using System.Globalization;
using SIQS.Contracts;
using SIQS.Contracts.Files;
using Sieving;

namespace SIQS.Pipeline;

internal sealed class SievingPhaseRunner
{
    public Task<PhaseResult> RunAsync(PhaseContext context) => Task.Run(() =>
    {
        var request = context.Request;
        var factorBase = FactorBaseFile.Parse(PhaseArtifactStore.Read(context, "factor_base.txt"));
        var parameters = SievingParameterResolver.Resolve(request, factorBase);
        var metadata = new RawRelationsMetadata(
            factorBase.Metadata.TargetN,
            factorBase.Metadata.Multiplier,
            factorBase.Metadata.ScaledN,
            factorBase.Metadata.Bound,
            parameters.LargePrimeBound,
            parameters.EnableTwoLargePrimes ? parameters.LargePrime2Bound : null);

        PhaseArtifactStore.QuarantineCorruptTailBatch(context.JobDirectory, "relations", context.Progress);
        PhaseArtifactStore.QuarantineCorruptTailBatch(context.JobDirectory, "partials", context.Progress);
        SieveCheckpointFile? checkpoint = null;
        SievingResumeState? resumeState = null;
        if (request.TrialSievePercent is null)
        {
            var fbData = FactorBaseData.From(factorBase);
            var aCandidateCount = PolynomialGenerator.SelectAPositions(fbData, parameters).Count;
            checkpoint = SieveCheckpointFile.OpenOrCreate(context.JobDirectory, factorBase.Metadata.TargetN, aCandidateCount);
            var existingRawFiles = PhaseArtifactStore.EnumerateRawBatchFiles(context.JobDirectory)
                .OrderBy(path => path, StringComparer.Ordinal).ToArray();
            resumeState = new SievingResumeState(
                checkpoint.CompletedAIndices,
                PhaseArtifactStore.ReadRawRelations(existingRawFiles, factorBase.Metadata),
                checkpoint.MarkCompleted);
        }

        var sink = new RawRelationBatchFileSink(context.JobDirectory, metadata, parameters.OutputBatchSize);
        SievingCounters counters;
        try
        {
            counters = SievingEngine.Sieve(factorBase, parameters, sink, context.Progress, context.CancellationToken, resumeState);
        }
        finally
        {
            checkpoint?.Dispose();
        }

        if (request.TrialSievePercent is null && counters.UsableRelations < parameters.RelationTarget)
        {
            throw new InvalidOperationException($"Polynomial supply exhausted before relation target: {counters.UsableRelations}/{parameters.RelationTarget} usable relations.");
        }

        var counterMap = new Dictionary<string, string>
        {
            ["polynomials"] = counters.Polynomials.ToString(),
            ["candidates"] = counters.Candidates.ToString(),
            ["blocks"] = counters.Blocks.ToString(),
            ["candidates_per_block"] = counters.Blocks > 0 ? ((double)counters.Candidates / counters.Blocks).ToString("F3", CultureInfo.InvariantCulture) : "0",
            ["discarded"] = counters.Discarded.ToString(),
            ["full_relations"] = counters.FullRelations.ToString(),
            ["partial_relations"] = counters.Partials.ToString(),
            ["one_large_prime_partials"] = counters.OneLargePrimePartials.ToString(),
            ["two_large_prime_partials"] = counters.TwoLargePrimePartials.ToString(),
            ["two_large_prime_split_attempts"] = counters.TwoLargePrimeSplitAttempts.ToString(),
            ["two_large_prime_split_successes"] = counters.TwoLargePrimeSplitSuccesses.ToString(),
            ["two_large_prime_residual_too_small"] = counters.TwoLargePrimeResidualTooSmall.ToString(),
            ["two_large_prime_residual_too_large"] = counters.TwoLargePrimeResidualTooLarge.ToString(),
            ["two_large_prime_residual_prime"] = counters.TwoLargePrimeResidualPrime.ToString(),
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
            ["usable_relations"] = counters.UsableRelations.ToString(),
            ["usable_partial_pairs"] = counters.UsablePartialPairs.ToString(),
            ["zero_parity_full_relations"] = counters.ZeroParityFullRelations.ToString(),
            ["zero_parity_partial_pairs"] = counters.ZeroParityPartialPairs.ToString(),
            ["relations_needed"] = counters.RelationTarget.ToString(),
            ["projected_matrix_rows"] = counters.ProjectedMatrixRows.ToString(),
            ["projected_matrix_columns"] = counters.ProjectedMatrixColumns.ToString(),
            ["polynomials_configured"] = parameters.PolynomialCount.ToString(),
            ["two_large_primes"] = parameters.EnableTwoLargePrimes ? "true" : "false",
            ["large_prime2_bound"] = parameters.LargePrime2Bound.ToString(),
            ["large_prime2_threshold_bound"] = parameters.LargePrime2ThresholdBound.ToString(),
            ["cofactor_splitter"] = parameters.CofactorSplitter.ToToken(),
            ["bucket_large_prime_cutoff"] = parameters.EffectiveBucketLargePrimeCutoff.ToString(),
            ["resieve_large_prime_cutoff"] = parameters.EffectiveResieveLargePrimeCutoff.ToString(),
            ["cpu_setup_ms"] = counters.SetupCpuMs.ToString(),
            ["cpu_sieve_fill_ms"] = counters.SieveFillCpuMs.ToString(),
            ["cpu_sieve_init_ms"] = counters.SieveInitCpuMs.ToString(),
            ["cpu_scan_ms"] = counters.ScanCpuMs.ToString(),
            ["cpu_poly_eval_ms"] = counters.PolyEvalCpuMs.ToString(),
            ["cpu_trial_div_ms"] = counters.TrialDivCpuMs.ToString(),
            ["cpu_td_pre_ms"] = counters.TrialDivPreCpuMs.ToString(),
            ["cpu_td_post_ms"] = counters.TrialDivPostCpuMs.ToString(),
            ["cpu_td_post_apos_ms"] = counters.TrialDivPostAPosCpuMs.ToString(),
            ["cpu_td_post_check_ms"] = counters.TrialDivPostCheckCpuMs.ToString(),
            ["cpu_td_post_parity_ms"] = counters.TrialDivPostParityCpuMs.ToString(),
        };
        if (counters.TrialRawRelationTarget is { } trialTarget)
        {
            counterMap["trial_raw_target"] = trialTarget.ToString();
            counterMap["trial_sieve_percent"] = request.TrialSievePercent!.Value.ToString("G", CultureInfo.InvariantCulture);
        }

        var artifacts = checkpoint is null
            ? sink.Artifacts
            : sink.Artifacts.Concat(new[] { SieveCheckpointFile.FileName }).Distinct(StringComparer.Ordinal).ToArray();
        return PhaseResult.Completed(SiqsPhase.Sieving, artifacts, counterMap);
    }, context.CancellationToken);
}

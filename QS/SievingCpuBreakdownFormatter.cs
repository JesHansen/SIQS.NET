using System.Text;
using SIQS.Contracts;

namespace QS;

/// <summary>Renders optional sieving CPU counters without coupling them to live progress handling.</summary>
internal static class SievingCpuBreakdownFormatter
{
    public static string Format(SiqsProgressEvent progress, Func<string, string> prefix)
    {
        if (!TryGetCounter(progress, "cpu_setup_ms", out var setup)
            || !TryGetCounter(progress, "cpu_sieve_fill_ms", out var fill)
            || !TryGetCounter(progress, "cpu_scan_ms", out var scan)
            || !TryGetCounter(progress, "cpu_trial_div_ms", out var trialDivision))
        {
            return string.Empty;
        }

        var total = setup + fill + scan + trialDivision;
        if (total == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine();
        builder.Append($"{prefix("[sieving cpu]")}setup {setup}ms ({Percent(setup, total)})");
        builder.Append($"  fill {fill}ms ({Percent(fill, total)})");
        builder.Append($"  scan {scan}ms ({Percent(scan, total)})");
        builder.Append($"  trial-div {trialDivision}ms ({Percent(trialDivision, total)})");
        builder.Append($"  [total cpu {total}ms]");

        AppendSieveFillBreakdown(builder, progress, fill, prefix);
        AppendScanBreakdown(builder, progress, scan, prefix);
        AppendTrialDivisionBreakdown(builder, progress, trialDivision, prefix);
        return builder.ToString().TrimEnd();
    }

    private static void AppendSieveFillBreakdown(StringBuilder builder, SiqsProgressEvent progress, long fill, Func<string, string> prefix)
    {
        if (TryGetCounter(progress, "cpu_sieve_init_ms", out var initialization) && fill > 0)
        {
            builder.AppendLine();
            builder.Append($"{prefix("[fill cpu]")}root-init {initialization}ms  block-fill {fill - initialization}ms");
        }
    }

    private static void AppendScanBreakdown(StringBuilder builder, SiqsProgressEvent progress, long scan, Func<string, string> prefix)
    {
        if (TryGetCounter(progress, "cpu_poly_eval_ms", out var polynomialEvaluation) && scan > 0)
        {
            builder.AppendLine();
            builder.Append($"{prefix("[scan cpu]")}filter {scan - polynomialEvaluation}ms  poly-eval {polynomialEvaluation}ms");
        }
    }

    private static void AppendTrialDivisionBreakdown(StringBuilder builder, SiqsProgressEvent progress, long trialDivision, Func<string, string> prefix)
    {
        if (!TryGetCounter(progress, "cpu_td_pre_ms", out var pre) || !TryGetCounter(progress, "cpu_td_post_ms", out var post))
        {
            return;
        }

        builder.AppendLine();
        builder.Append($"{prefix("[trial-div cpu]")}pre {pre}ms  loop {trialDivision - pre - post}ms  post {post}ms");
        if (TryGetCounter(progress, "cpu_td_post_apos_ms", out var aPosition)
            && TryGetCounter(progress, "cpu_td_post_check_ms", out var check)
            && TryGetCounter(progress, "cpu_td_post_parity_ms", out var parity))
        {
            builder.AppendLine();
            builder.Append($"{prefix("[post-loop cpu]")}apos {aPosition}ms  check(+primality) {check}ms  parity+ctor {parity}ms");
        }
    }

    private static bool TryGetCounter(SiqsProgressEvent progress, string key, out long value)
        => long.TryParse(progress.Counters.GetValueOrDefault(key, "?"), out value);

    private static string Percent(long value, long total) => $"{100.0 * value / total:F0}%";
}

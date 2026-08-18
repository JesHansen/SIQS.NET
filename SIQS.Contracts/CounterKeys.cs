namespace SIQS.Contracts;

/// <summary>
/// Progress- and phase-counter keys that are read back by name by a consumer, so producer and
/// consumer must agree on the exact string. These cross assembly boundaries (an engine writes
/// them; the pipeline, UI, or top-up planner reads them), which is exactly where a bare literal
/// can silently drift; the named constants exist so every producer and consumer references one
/// declaration.
/// </summary>
/// <remarks>
/// Write-only telemetry keys are deliberately not listed here: they have a single reference and
/// cannot drift, so a constant would add indirection without removing duplication. Use
/// <see cref="CounterFormat"/> to format and parse the values.
/// </remarks>
public static class CounterKeys
{
    /// <summary>Approximate usable-relation count emitted by sieving; read by the top-up planner.</summary>
    public const string UsableRelations = "usable_relations";

    /// <summary>Set when deterministic work proves the input prime; read to short-circuit.</summary>
    public const string InputIsPrime = "input_is_prime";

    /// <summary>Set when Baillie-PSW classifies a larger input as probable prime.</summary>
    public const string InputIsProbablePrime = "input_is_probable_prime";

    /// <summary>Number of dependencies the square-root phase attempted; read into the job result.</summary>
    public const string DependenciesAttempted = "dependencies_attempted";

    /// <summary>Wall-clock seconds for a phase or progress step; read by the UI timeline.</summary>
    public const string ElapsedSeconds = "elapsed_seconds";
}

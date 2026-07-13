namespace SIQS.Contracts;

/// <summary>
/// Canonical <c>format=</c> metadata strings written into each artifact file. Writers must emit
/// the matching constant; readers reject unknown formats. For v1, exact string matching is used.
/// </summary>
public static class FileFormats
{
    public const string FactorBaseV1 = "siqs-factor-base-v1";
    public const string RawRelationsV1 = "siqs-raw-relations-v1";
    public const string RawPartialsV1 = "siqs-raw-partials-v1";
    public const string RawRelationsV2 = "siqs-raw-relations-v2";
    public const string RawPartialsV2 = "siqs-raw-partials-v2";
    public const string FilteredRelationsV1 = "siqs-filtered-relations-v1";
    public const string FilteredRelationsV2 = "siqs-filtered-relations-v2";
    public const string FilteredMatrixV1 = "siqs-filtered-matrix-v1";
    public const string MatrixMetaV1 = "siqs-matrix-meta-v1";
    public const string DependenciesV1 = "siqs-dependencies-v1";
    public const string FactorsV1 = "siqs-factors-v1";
}

using System.Security.Cryptography;
using System.Text;

namespace SIQS.Contracts.Distributed;

/// <summary>
/// Shared constants and helpers for the distributed sieving wire protocol. The <see cref="Version"/>
/// is exchanged in the handshake so a client and server that disagree on the format fail fast rather
/// than exchanging relations they cannot agree on.
/// </summary>
public static class DistProtocol
{
    /// <summary>Bump on any change to the wire DTOs or the relation/parameter encoding.</summary>
    public const int Version = 1;

    /// <summary>
    /// A stable digest of every job input that must be identical on both sides for the sieve to
    /// agree (N, factor base inputs, and all relation-affecting sieving parameters, plus the protocol
    /// version). Deliberately excludes <see cref="SievingParameterSet.Parallelism"/>, a per-client
    /// performance knob that does not change the relation set.
    /// </summary>
    public static string ComputeParamHash(
        int protocolVersion, string n, long factorBaseBound, string multiplier, bool allowTinyTrialDivision, SievingParameterSet s)
    {
        var canonical = string.Join('|',
            protocolVersion, n, factorBaseBound, multiplier, allowTinyTrialDivision,
            s.SieveHalfInterval, s.PolynomialCount, s.RelationTarget, s.LargePrimeBound, s.ErrorMargin,
            s.OutputBatchSize, s.APrimeCount, s.APrimeWindowSize, s.SieveBlockSize, s.BucketLargePrimeCutoff,
            s.ResieveLargePrimeCutoff, s.TrialRawRelationTarget?.ToString() ?? "", s.EnableTwoLargePrimes,
            s.LargePrime2Bound, s.LargePrime2ThresholdBound, s.CofactorSplitter);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}

/// <summary>Client → server handshake: the client states its build version and the protocol it speaks.</summary>
public sealed record HelloRequest(string ClientVersion, int ProtocolVersion);

/// <summary>Server → client handshake result. When <see cref="Accepted"/> is false the client aborts.</summary>
public sealed record HelloResponse(bool Accepted, int ProtocolVersion, string? Reason);

/// <summary>
/// A transport-friendly mirror of the sieving engine's parameters (all primitives; the cofactor
/// splitter travels as its lowercase token). Mapped to/from the engine's <c>SievingParameters</c> by
/// extension methods in the Sieving assembly.
/// </summary>
public sealed record SievingParameterSet(
    long SieveHalfInterval,
    long PolynomialCount,
    int RelationTarget,
    long LargePrimeBound,
    int ErrorMargin,
    int OutputBatchSize,
    int APrimeCount,
    int APrimeWindowSize,
    int Parallelism,
    int SieveBlockSize,
    int BucketLargePrimeCutoff,
    int ResieveLargePrimeCutoff,
    int? TrialRawRelationTarget,
    bool EnableTwoLargePrimes,
    long LargePrime2Bound,
    long LargePrime2ThresholdBound,
    string CofactorSplitter);

/// <summary>
/// Everything a client needs to reproduce the job locally: the number, the factor base inputs, the
/// resolved sieving parameters, the size of the A-index space leases are carved from, and a digest
/// the client re-derives to confirm it agrees with the server. BigIntegers travel as decimal strings.
/// </summary>
public sealed record JobDescriptor(
    string JobId,
    string N,
    long FactorBaseBound,
    string Multiplier,
    bool AllowTinyTrialDivision,
    SievingParameterSet Sieving,
    int ACount,
    string ParamHash);

/// <summary>Server → client work lease covering the half-open A-index range [AStart, AEnd).</summary>
public sealed record LeaseResponse(string JobId, string LeaseId, int AStart, int AEnd, DateTimeOffset ExpiresUtc);

/// <summary>
/// Client → server upload of a completed lease: the sieved relations and partials serialized in the
/// existing raw-relations text format.
/// </summary>
public sealed record UploadRequest(string JobId, string LeaseId, string RelationsText, string PartialsText);

/// <summary>Server → client upload result after verification and dedup.</summary>
public sealed record UploadResponse(bool Accepted, int AcceptedCount, int RejectedCount, string? Reason);

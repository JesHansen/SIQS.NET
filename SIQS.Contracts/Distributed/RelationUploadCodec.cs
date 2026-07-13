using SIQS.Contracts.Files;

namespace SIQS.Contracts.Distributed;

/// <summary>
/// Serializes sieved relations into an <see cref="UploadRequest"/> and back, reusing the existing raw
/// relations text format. The client uses <see cref="ToUpload"/>; the server uses <see cref="Parse"/>.
/// </summary>
public static class RelationUploadCodec
{
    public static UploadRequest ToUpload(
        string jobId,
        string leaseId,
        RawRelationsMetadata metadata,
        IReadOnlyList<RawRelationRecord> fullRelations,
        IReadOnlyList<RawRelationRecord> partials)
    {
        var isV2 = metadata.LargePrime2Bound is not null;
        var relationsText = RawRelationsFile.Write(new RawRelationsDocument(
            isV2 ? FileFormats.RawRelationsV2 : FileFormats.RawRelationsV1, metadata, fullRelations));
        var partialsText = RawRelationsFile.Write(new RawRelationsDocument(
            isV2 ? FileFormats.RawPartialsV2 : FileFormats.RawPartialsV1, metadata, partials));
        return new UploadRequest(jobId, leaseId, relationsText, partialsText);
    }

    public static IReadOnlyList<RawRelationRecord> Parse(UploadRequest request)
    {
        var records = new List<RawRelationRecord>();
        if (!string.IsNullOrWhiteSpace(request.RelationsText))
        {
            records.AddRange(RawRelationsFile.Parse(request.RelationsText).Relations);
        }

        if (!string.IsNullOrWhiteSpace(request.PartialsText))
        {
            records.AddRange(RawRelationsFile.Parse(request.PartialsText).Relations);
        }

        return records;
    }
}

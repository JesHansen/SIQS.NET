using SIQS.Pipeline;
using SIQS.Contracts;
using SIQS.Contracts.Files;

namespace SIQS.Pipeline.Tests;

public class RawBatchFileTests
{
    [Theory]
    [InlineData("relations_0000.txt", "relations", true)]
    [InlineData("relations_9999.txt", "relations", true)]
    [InlineData("relations_10000.txt", "relations", true)]
    [InlineData("partials_10000.txt", "partials", true)]
    [InlineData("relations_00000.txt", "relations", false)]
    [InlineData("relations_filtered.txt", "relations", false)]
    [InlineData("partials_10000.txt", "relations", false)]
    public void Phase_artifact_store_recognizes_only_canonical_numbered_batches(
        string fileName,
        string prefix,
        bool expected)
    {
        Assert.Equal(expected, PhaseArtifactStore.IsNumberedBatch(fileName, prefix));
    }

    [Fact]
    public void Workspace_includes_five_digit_raw_batches_in_sieving_artifacts()
    {
        var directory = Directory.CreateTempSubdirectory("siqs-batch-files-").FullName;
        try
        {
            var relation = Path.Combine(directory, "relations_10000.txt");
            var partial = Path.Combine(directory, "partials_10000.txt");
            File.WriteAllText(relation, "");
            File.WriteAllText(partial, "");
            File.WriteAllText(Path.Combine(directory, "relations_filtered.txt"), "");

            var artifacts = new JobWorkspace(directory, "test")
                .ArtifactPathsForPhase(SIQS.Contracts.SiqsPhase.Sieving)
                .Select(Path.GetFileName)
                .ToArray();

            Assert.Contains("relations_10000.txt", artifacts);
            Assert.Contains("partials_10000.txt", artifacts);
            Assert.DoesNotContain("relations_filtered.txt", artifacts);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Resume_repairs_only_an_interrupted_final_row_in_the_tail_batch()
    {
        var directory = Directory.CreateTempSubdirectory("siqs-batch-files-").FullName;
        try
        {
            var metadata = new RawRelationsMetadata(77, 1, 77, 2, 128);
            var valid = new RawRelationRecord(
                "R000000", RelationKind.Full, "P000000",
                A: 1, B: 0, C: -77, X: 1, T: 1, Sign: 1,
                FactorExponents: new Dictionary<int, int>(),
                ParityColumns: Array.Empty<int>(),
                LargePrime: null);
            var path = Path.Combine(directory, "relations_0000.txt");
            File.WriteAllText(
                path,
                RawRelationsFile.Write(new RawRelationsDocument(
                    FileFormats.RawRelationsV1,
                    metadata,
                    new[] { valid })) + "R-interrupted,full\n");

            PhaseArtifactStore.QuarantineCorruptTailBatch(directory, "relations", progress: null);

            Assert.True(File.Exists(path));
            Assert.False(File.Exists(path + ".corrupt"));
            var repaired = RawRelationsFile.Parse(File.ReadAllText(path));
            Assert.Equal(new[] { "R000000" }, repaired.Relations.Select(r => r.RelationId));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

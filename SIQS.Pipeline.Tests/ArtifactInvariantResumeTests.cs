using SIQS.Contracts;
using SIQS.Contracts.Files;
using SIQS.Pipeline;

namespace SIQS.Pipeline.Tests;

public sealed class ArtifactInvariantResumeTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "siqs-invariant-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Mutated_factor_base_root_invalidates_factor_base_phase()
    {
        var run = await CompletedRun("factor-base");
        var path = Path.Combine(run.Directory, "factor_base.txt");
        var document = FactorBaseFile.Parse(File.ReadAllText(path));
        var entry = document.Entries[0];
        File.WriteAllText(path, FactorBaseFile.Write(document with
        {
            Entries = new[] { entry with { Prime = 3, Root1 = 0, Root2 = 0 } },
        }));

        Assert.Equal(0, ResumePoint(run));
    }

    [Fact]
    public async Task Mutated_matrix_relation_id_invalidates_filtering_phase()
    {
        var run = await CompletedRun("matrix");
        var path = Path.Combine(run.Directory, "filtered_matrix.txt");
        var matrix = FilteredMatrixFile.Parse(File.ReadAllText(path)).ToArray();
        matrix[0] = new SparseMatrixRowRecord(0, "F99999999", matrix[0].Columns);
        File.WriteAllText(path, FilteredMatrixFile.Write(matrix));

        Assert.Equal(2, ResumePoint(run));
    }

    [Fact]
    public async Task Mutated_dependency_relation_id_invalidates_linear_algebra_phase()
    {
        var run = await CompletedRun("dependency");
        var path = Path.Combine(run.Directory, "dependencies.txt");
        var document = DependenciesFile.Parse(File.ReadAllText(path));
        var dependency = document.Dependencies[0];
        File.WriteAllText(path, DependenciesFile.Write(new DependenciesDocument(
            document.TargetN,
            document.Multiplier,
            document.ScaledN,
            document.RowCount,
            document.ColumnCount,
            new[]
            {
                new DependencyRecord(
                    dependency.DependencyId,
                    dependency.RowIds,
                    dependency.RelationIds.Select((id, index) => index == 0 ? "F99999999" : id).ToArray()),
            })));

        Assert.Equal(3, ResumePoint(run));
    }

    [Fact]
    public async Task Mutated_factor_product_invalidates_square_root_phase()
    {
        var run = await CompletedRun("factors");
        var path = Path.Combine(run.Directory, "factors.txt");
        var document = FactorsFile.Parse(File.ReadAllText(path));
        var result = document.Results[0];
        File.WriteAllText(path, FactorsFile.Write(new FactorsDocument(
            document.TargetN,
            document.Multiplier,
            document.ScaledN,
            document.DependencyCount,
            new[] { result with { Factor1 = 5, Factor2 = 5 } })));

        Assert.Equal(4, ResumePoint(run));
    }

    private async Task<Run> CompletedRun(string name)
    {
        var directory = Path.Combine(_root, name);
        var request = new FactorizationRequest(91) { RunDirectory = directory };
        var pipeline = new SiqsPipeline(new FakePhaseExecutor());
        var result = await pipeline.RunAsync(request, null, CancellationToken.None);
        Assert.Equal(JobStatus.CompletedFactorFound, result.Status);
        return new Run(directory, pipeline.NormalizeAndValidate(request), pipeline.LoadJob(directory));
    }

    private static int ResumePoint(Run run)
        => new ResumePlanner().FindResumePoint(run.Directory, run.State, run.Request);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed record Run(string Directory, FactorizationRequest Request, JobState State);
}

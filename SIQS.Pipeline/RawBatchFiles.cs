using SIQS.Contracts.Files;

namespace SIQS.Pipeline;

/// <summary>Enumerates numbered raw sieve batch files.</summary>
internal static class RawBatchFiles
{
    public static IEnumerable<string> Enumerate(string directory)
        => Directory.EnumerateFiles(directory, "relations_*.txt")
            .Concat(Directory.EnumerateFiles(directory, "partials_*.txt"))
            .Where(path => IsNumberedRawBatch(Path.GetFileName(path)));

    private static bool IsNumberedRawBatch(string name)
        => RawBatchFileName.TryParse(name, "relations", out _) ||
           RawBatchFileName.TryParse(name, "partials", out _);
}

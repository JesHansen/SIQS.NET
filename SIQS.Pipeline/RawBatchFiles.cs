namespace SIQS.Pipeline;

/// <summary>Enumerates the numbered raw sieve batch files (<c>relations_NNNN.txt</c> / <c>partials_NNNN.txt</c>).</summary>
internal static class RawBatchFiles
{
    public static IEnumerable<string> Enumerate(string directory)
        => Directory.EnumerateFiles(directory, "relations_*.txt")
            .Concat(Directory.EnumerateFiles(directory, "partials_*.txt"))
            .Where(path => IsNumberedRawBatch(Path.GetFileName(path)));

    private static bool IsNumberedRawBatch(string name)
    {
        var prefixLength = name.StartsWith("relations_", StringComparison.Ordinal)
            ? "relations_".Length
            : name.StartsWith("partials_", StringComparison.Ordinal)
                ? "partials_".Length
                : -1;
        return prefixLength >= 0 &&
               name.EndsWith(".txt", StringComparison.Ordinal) &&
               name.Length == prefixLength + 4 + ".txt".Length &&
               name.AsSpan(prefixLength, 4).ToArray().All(char.IsDigit);
    }
}

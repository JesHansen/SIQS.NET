using System.Globalization;
using System.Numerics;
using SIQS.Contracts.Text;

namespace Sieving;

public sealed record SieveCheckpoint(BigInteger TargetN, int ACandidateCount, IReadOnlySet<int> CompletedAIndices);

/// <summary>Reads and appends <c>sieve_checkpoint.txt</c>.</summary>
public sealed class SieveCheckpointFile : IDisposable
{
    public const string FileName = "sieve_checkpoint.txt";
    public const string Format = "siqs-sieve-checkpoint-v1";

    private readonly string _path;
    private readonly object _gate = new();
    private readonly HashSet<int> _completed;
    private readonly StreamWriter _writer;

    private SieveCheckpointFile(string path, BigInteger targetN, int aCandidateCount, HashSet<int> completed)
    {
        _path = path;
        TargetN = targetN;
        ACandidateCount = aCandidateCount;
        _completed = completed;
        _writer = new StreamWriter(new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read))
        {
            AutoFlush = true,
        };
    }

    public BigInteger TargetN { get; }
    public int ACandidateCount { get; }
    public IReadOnlySet<int> CompletedAIndices => _completed;

    public static SieveCheckpointFile OpenOrCreate(string directory, BigInteger targetN, int aCandidateCount)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, FileName);
        if (!File.Exists(path))
        {
            using var writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
            writer.WriteLine(MetadataFormat.Comment("format", Format));
            writer.WriteLine(MetadataFormat.Comment("target_n", targetN.ToString(CultureInfo.InvariantCulture)));
            writer.WriteLine(MetadataFormat.Comment("a_candidate_count", aCandidateCount.ToString(CultureInfo.InvariantCulture)));
            writer.WriteLine("a_index");
        }

        var checkpoint = Parse(File.ReadAllText(path));
        if (checkpoint.TargetN != targetN || checkpoint.ACandidateCount != aCandidateCount)
        {
            throw new FormatException("sieve_checkpoint.txt metadata does not match this sieving run.");
        }

        return new SieveCheckpointFile(path, targetN, aCandidateCount, checkpoint.CompletedAIndices.ToHashSet());
    }

    public static SieveCheckpoint Parse(string text)
    {
        var lines = text.Split('\n');
        var meta = MetadataFormat.ParseAll(lines);
        if (!meta.TryGetValue("format", out var format) || format != Format)
        {
            throw new FormatException($"Unexpected sieve checkpoint format '{meta.GetValueOrDefault("format")}'.");
        }

        var targetN = BigInteger.Parse(Require(meta, "target_n"), CultureInfo.InvariantCulture);
        var aCandidateCount = int.Parse(Require(meta, "a_candidate_count"), CultureInfo.InvariantCulture);
        var completed = new HashSet<int>();
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || MetadataFormat.IsComment(trimmed) || trimmed == "a_index")
            {
                continue;
            }

            var index = int.Parse(trimmed, CultureInfo.InvariantCulture);
            if (index < 0 || index >= aCandidateCount)
            {
                throw new FormatException($"Checkpoint A-index {index} is outside 0..{aCandidateCount - 1}.");
            }

            completed.Add(index);
        }

        return new SieveCheckpoint(targetN, aCandidateCount, completed);
    }

    public void MarkCompleted(int aIndex)
    {
        lock (_gate)
        {
            if (!_completed.Add(aIndex))
            {
                return;
            }

            _writer.WriteLine(aIndex.ToString(CultureInfo.InvariantCulture));
        }
    }

    public void Dispose() => _writer.Dispose();

    private static string Require(IReadOnlyDictionary<string, string> meta, string key)
        => meta.TryGetValue(key, out var value)
            ? value
            : throw new FormatException($"sieve_checkpoint.txt is missing required metadata key '{key}'.");
}

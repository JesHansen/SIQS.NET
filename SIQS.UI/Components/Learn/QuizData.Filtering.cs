namespace SIQS.UI.Components.Learn;

public static partial class QuizData
{
    private static readonly IReadOnlyList<QuizQuestion> Filtering = new QuizQuestion[]
    {
        new("filtering-01", QuizTopic.Filtering, "A relation contains a prime that appears in no other relation. Filtering removes it because…", new[]
        {
            "that prime's exponent could never be made even by any combination",
            "large primes are inaccurate",
            "it speeds up sieving retroactively",
            "duplicate relations are illegal",
        }, "A lone odd exponent can never cancel, so the relation (a \"singleton\") is dead weight for the matrix."),
        new("filtering-02", QuizTopic.Filtering, "Two partial relations share the same large prime q. Combined, they…", new[]
        {
            "multiply into a relation whose q-exponent is even, usable like a full relation",
            "must both be discarded",
            "give a factor immediately",
            "double the matrix size",
        }, "q² divides the combined value, so q vanishes from the mod-2 vector — a full relation in disguise."),
        new("filtering-03", QuizTopic.Filtering, "Why are exact duplicate relations removed?", new[]
        {
            "Any dependency using both yields X ≡ Y — a guaranteed trivial congruence",
            "They overflow the matrix data type",
            "Duplicates indicate sieving errors",
            "They make gcd undefined",
        }, "A relation combined with its own copy contributes nothing new: both sides of the congruence match."),
        new("filtering-04", QuizTopic.Filtering, "Why must singleton removal be repeated until nothing changes?", new[]
        {
            "Deleting one relation can turn another prime into a new singleton",
            "The first pass is only approximate",
            "Relations regenerate between passes",
            "Once is always enough, repeating is a safety habit",
        }, "Removal cascades: each deleted row shrinks other primes' occurrence counts, exposing fresh singletons."),
        new("filtering-05", QuizTopic.Filtering, "How does filtering shrink the matrix?", new[]
        {
            "It removes useless rows and empties columns, leaving fewer relations over fewer active primes",
            "It compresses entries to 4 bits",
            "It transposes the matrix",
            "It converts GF(2) to GF(4)",
        }, "Fewer rows and columns mean dramatically less linear-algebra work — the whole point of the stage."),
        new("filtering-06", QuizTopic.Filtering, "The \"excess\" of a relation set is…", new[]
        {
            "the number of relations minus the number of distinct primes appearing in them",
            "the count of duplicate relations",
            "the largest exponent seen",
            "relations found beyond the sieve interval",
        }, "Positive excess is what guarantees dependencies exist; filtering must preserve enough of it."),
        new("filtering-07", QuizTopic.Filtering, "After combining two partials sharing large prime q, what happens to q's exponent?", new[]
        {
            "It becomes 2 — even — so q drops out of the mod-2 vector",
            "It becomes 0 by cancellation of signs",
            "It stays 1",
            "It becomes q²",
        }, "One q from each partial multiplies to q²; even exponents are invisible mod 2."),
        new("filtering-08", QuizTopic.Filtering, "When two partials are combined, what happens to their smooth parts?", new[]
        {
            "They multiply, so their exponent vectors add (mod 2)",
            "Only the larger smooth part is kept",
            "They are averaged",
            "They cancel out entirely",
        }, "The combined relation's vector is the XOR of the two originals plus the vanished large prime."),
        new("filtering-09", QuizTopic.Filtering, "Where does filtering sit in the pipeline?", new[]
        {
            "Between sieving and linear algebra",
            "Before the factor base is built",
            "After the square root",
            "In parallel with sieving, always",
        }, "It consumes raw relations and produces the trimmed matrix the solver will factor."),
        new("filtering-10", QuizTopic.Filtering, "In the worked example with three relations, how much filtering is needed?", new[]
        {
            "None — all three relations are used as-is",
            "The x = 0 relation must be removed as a singleton",
            "All partials must be combined first",
            "Duplicates must be merged",
        }, "The toy example is pre-cleaned: three full relations, no duplicates, no partials, no singletons."),
        new("filtering-11", QuizTopic.Filtering, "Why does a smaller filtered matrix matter so much?", new[]
        {
            "Linear-algebra cost grows much faster than linearly, so every removed row and column pays off",
            "Smaller matrices produce more factors",
            "Disk artifacts have a size limit",
            "It reduces sieving time on the next run",
        }, "Halving the dimension saves far more than half the solver time — filtering is cheap leverage."),
        new("filtering-12", QuizTopic.Filtering, "What happens if filtering leaves too few usable relations?", new[]
        {
            "Sieving must resume to collect more (a top-up round)",
            "The matrix is padded with zero rows",
            "The factor base is shrunk to fit",
            "Linear algebra runs anyway and fails silently",
        }, "You cannot fabricate excess; the pipeline goes back for more relations with an adjusted target."),
        new("filtering-13", QuizTopic.Filtering, "In this app, what does a \"top-up round\" record describe?", new[]
        {
            "A resumed sieving pass triggered by a relation deficit, with its new target",
            "A second multiplier attempt",
            "A retry of the square root",
            "The matrix being rebuilt after a crash",
        }, "The job state tracks each deficit, margin and new relation target so runs are reproducible."),
        new("filtering-14", QuizTopic.Filtering, "In this app, which artifacts does filtering produce?", new[]
        {
            "relations_filtered.txt and filtered_matrix.txt",
            "factor_base.txt and events.log",
            "factors.txt and dependencies.txt",
            "job.json only",
        }, "Filtering writes the cleaned relations plus the matrix hand-off for the linear-algebra stage."),
        new("filtering-15", QuizTopic.Filtering, "Why can't a singleton be rescued by combining it with other relations?", new[]
        {
            "Its unique prime appears in no other row, so its odd exponent survives every combination",
            "Combining is only allowed for partials",
            "Singletons have negative values",
            "The matrix format forbids it",
        }, "XOR with rows lacking that prime never flips its bit; the exponent stays odd forever."),
        new("filtering-16", QuizTopic.Filtering, "A dependency that combines a relation with an exact copy of itself yields…", new[]
        {
            "X ≡ Y (mod N) — the trivial congruence, useless for factoring",
            "a guaranteed factor",
            "a new relation",
            "X ≡ −Y, which is useful",
        }, "Both sides are built from identical ingredients, so the congruence carries no information."),
        new("filtering-17", QuizTopic.Filtering, "A prime appears in exactly two relations. A standard filtering move is to…", new[]
        {
            "merge those two relations and eliminate that prime's column entirely",
            "delete both relations",
            "move the prime to the factor base",
            "split the matrix at that column",
        }, "Replacing the pair with their product removes a column at the cost of one (denser) row — usually a good trade."),
        new("filtering-18", QuizTopic.Filtering, "What is the overall objective of the filtering stage?", new[]
        {
            "Minimize the matrix dimensions while preserving enough excess for dependencies",
            "Maximize the number of relations",
            "Verify the primality of every prime",
            "Sort relations by size",
        }, "Everything filtering does — dedupe, singleton removal, merging — serves smaller-but-still-solvable."),
        new("filtering-19", QuizTopic.Filtering, "What does the large-prime bound control?", new[]
        {
            "How big the single leftover prime of a partial relation may be",
            "The largest prime in the factor base",
            "The maximum matrix dimension",
            "The sieve interval length",
        }, "A larger bound keeps more partials (more combination chances) at the cost of storing more of them."),
        new("filtering-20", QuizTopic.Filtering, "Two partial relations have *different* large primes. Combining them gives…", new[]
        {
            "nothing useful — both large primes would still carry odd exponents",
            "a full relation",
            "a factor of N",
            "a singleton",
        }, "Only a shared large prime cancels; distinct ones both survive the multiplication mod 2."),
        new("filtering-21", QuizTopic.Filtering, "Which of these is NOT a filtering task?", new[]
        {
            "Computing dependencies of the matrix",
            "Removing duplicate relations",
            "Removing singletons",
            "Combining partials that share a large prime",
        }, "Dependencies are the linear-algebra stage's job; filtering only prepares its input."),
        new("filtering-22", QuizTopic.Filtering, "Five distinct relations involve only four distinct primes. What is guaranteed?", new[]
        {
            "At least one nontrivial dependency exists among the five vectors",
            "All five relations are duplicates",
            "N must be prime",
            "The excess is negative",
        }, "Five vectors in a 4-dimensional space over GF(2) must be linearly dependent."),
        new("filtering-23", QuizTopic.Filtering, "Over which structure is the filtered matrix that filtering hands to the solver?", new[]
        {
            "GF(2) — each entry is an exponent reduced mod 2",
            "The integers",
            "Floating-point reals",
            "GF(256)",
        }, "Only parity matters for building squares, so a single bit per entry suffices."),
        new("filtering-24", QuizTopic.Filtering, "Can filtering create brand-new relations?", new[]
        {
            "No — it only removes, merges or combines what sieving already found",
            "Yes, by extrapolating the polynomial",
            "Yes, by inverting the matrix",
            "Yes, whenever the excess is negative",
        }, "Filtering is conservative bookkeeping; new raw material only ever comes from sieving."),
        new("filtering-25", QuizTopic.Filtering, "Why can duplicate relations appear at all in a distributed sieve?", new[]
        {
            "An expired lease may be re-issued, so two clients can sieve and upload the same region",
            "The polynomial repeats every 2^64 values",
            "Uploads are applied twice by design",
            "They cannot; duplicates prove a bug",
        }, "Fault tolerance re-hands-out unfinished work; occasional double coverage is expected and filtered later."),
    };
}

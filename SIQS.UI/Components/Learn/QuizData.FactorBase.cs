namespace SIQS.UI.Components.Learn;

public static partial class QuizData
{
    private static readonly IReadOnlyList<QuizQuestion> FactorBase = new QuizQuestion[]
    {
        new("factorbase-01", QuizTopic.FactorBase, "Why does the factor base only keep primes p for which N is a quadratic residue mod p?", new[]
        {
            "For other primes, p can never divide x² − N",
            "They make the matrix symmetric",
            "Non-residue primes are too large",
            "It halves memory but is optional for correctness",
        }, "p | x² − N forces x² ≡ N (mod p), which is solvable only when N is a QR mod p."),
        new("factorbase-02", QuizTopic.FactorBase, "For N = 15347 and bound 30, the factor base is:", new[]
        {
            "{2, 17, 23, 29}",
            "{2, 3, 5, 7}",
            "{2, 13, 19, 29}",
            "all primes below 30",
        }, "Only primes with (N/p) = 1 qualify; for 15347 those up to 30 are 2, 17, 23 and 29."),
        new("factorbase-03", QuizTopic.FactorBase, "Which test decides whether an odd prime p joins the factor base?", new[]
        {
            "Whether the Legendre symbol (N/p) equals 1",
            "Whether p divides N − 1",
            "Whether p is congruent to 1 mod 4",
            "Whether p appears in the previous run's base",
        }, "(N/p) = 1 says N is a nonzero square mod p, exactly the solvability condition for x² ≡ N."),
        new("factorbase-04", QuizTopic.FactorBase, "What is the trade-off of choosing a larger factor-base bound?", new[]
        {
            "Smooth values become easier to find, but more relations are needed and the matrix grows",
            "Sieving slows down but linear algebra becomes free",
            "The polynomial degree increases",
            "gcd computations become harder",
        }, "More primes means more values qualify as smooth — and a wider matrix that demands more rows."),
        new("factorbase-05", QuizTopic.FactorBase, "Roughly how many relations must be collected, relative to the factor-base size?", new[]
        {
            "A few more than the number of factor-base primes",
            "Exactly N/2",
            "One per digit of N",
            "The square of the base size",
        }, "With more rows than columns over GF(2), dependencies are guaranteed; a small excess suffices."),
        new("factorbase-06", QuizTopic.FactorBase, "For an odd prime p with (N/p) = 1, how many solutions does x² ≡ N (mod p) have?", new[]
        {
            "Exactly two, of the form ±r",
            "Exactly one",
            "Exactly p − 1",
            "None",
        }, "A nonzero square mod an odd prime has precisely two square roots, giving two sieving progressions."),
        new("factorbase-07", QuizTopic.FactorBase, "Why does the prime 2 need special handling in the sieve setup?", new[]
        {
            "Mod 2 there is only one root, not the ± pair odd primes give",
            "2 never divides x² − N",
            "2 is not a prime",
            "The Legendre symbol is undefined only for p = 17",
        }, "The ±r structure needs an odd modulus; p = 2 contributes a single progression."),
        new("factorbase-08", QuizTopic.FactorBase, "What is the purpose of the small multiplier k sometimes applied to N?", new[]
        {
            "Making kN a quadratic residue for more small primes, so smooth values are more common",
            "Making N prime",
            "Doubling the sieve interval",
            "Reducing the number of relations needed",
        }, "The Knuth–Schroeppel multiplier trades slightly larger values for a friendlier factor base."),
        new("factorbase-09", QuizTopic.FactorBase, "What is the main cost of using a multiplier k > 1?", new[]
        {
            "The sieved values grow, since the polynomial now targets kN",
            "The factor base becomes empty",
            "gcd no longer works",
            "The matrix becomes dense",
        }, "You sieve kN instead of N, so y-values are larger and slightly less likely to be smooth — usually a worthwhile trade."),
        new("factorbase-10", QuizTopic.FactorBase, "Is 5 in the factor base of N = 15347?", new[]
        {
            "No — 15347 ≡ 2 (mod 5), and the squares mod 5 are only {0, 1, 4}",
            "Yes — every prime below 30 qualifies",
            "Yes — 5 divides 15347",
            "No — 5 is smaller than the multiplier",
        }, "2 is not a square mod 5, so 5 can never divide (x+124)² − 15347."),
        new("factorbase-11", QuizTopic.FactorBase, "Why does 17 qualify for the factor base of N = 15347?", new[]
        {
            "15347 ≡ 13 (mod 17) and 8² ≡ 13 (mod 17), so N is a QR mod 17",
            "17 divides 15347",
            "17 is congruent to 1 mod 4",
            "All primes ending in 7 qualify",
        }, "8² = 64 = 3·17 + 13, so 13 is a square mod 17 and x² ≡ N (mod 17) is solvable."),
        new("factorbase-12", QuizTopic.FactorBase, "Besides the prime itself, what does the sieve precompute per factor-base prime?", new[]
        {
            "Its rounded logarithm and the roots of x² ≡ N (mod p)",
            "Its primitive root and discriminant",
            "A full division table",
            "Nothing — primes are used raw",
        }, "The roots seed the arithmetic progressions and log p is the increment added at each hit."),
        new("factorbase-13", QuizTopic.FactorBase, "What if a candidate factor-base prime p actually divides N?", new[]
        {
            "p is itself a factor of N — the job is (partly) done immediately",
            "p is added twice to the base",
            "The sieve interval must double",
            "The Legendre symbol equals 1",
        }, "(N/p) = 0 signals p | N; a trial-division precheck harvests such lucky small factors up front."),
        new("factorbase-14", QuizTopic.FactorBase, "What goes wrong when the factor-base bound is far too small?", new[]
        {
            "Smooth values become so rare that sieving takes practically forever",
            "The matrix becomes too large",
            "gcd returns composite values",
            "Duplicate relations flood the filter",
        }, "Few primes means few values factor completely over the base — relations barely trickle in."),
        new("factorbase-15", QuizTopic.FactorBase, "What goes wrong when the factor-base bound is far too large?", new[]
        {
            "The relation target and matrix blow up, and linear algebra dominates the runtime",
            "Sieving stops finding any candidates",
            "The polynomial stops being quadratic",
            "Square roots mod p stop existing",
        }, "Every extra prime is an extra matrix column and an extra needed relation."),
        new("factorbase-16", QuizTopic.FactorBase, "How does the optimal factor-base bound behave as N grows?", new[]
        {
            "It grows — larger N warrants a larger (subexponentially sized) bound",
            "It shrinks toward 2",
            "It is always exactly 30",
            "It equals √N",
        }, "The classic L-function analysis balances smoothness probability against matrix cost; both scales rise with N."),
        new("factorbase-17", QuizTopic.FactorBase, "The Legendre symbol (a/p) = 1 means…", new[]
        {
            "a is a nonzero square modulo the odd prime p",
            "a divides p",
            "a is odd",
            "a is a primitive root mod p",
        }, "(a/p) is 1 for nonzero QRs, −1 for non-residues, 0 when p divides a."),
        new("factorbase-18", QuizTopic.FactorBase, "Euler's criterion evaluates (a/p) by computing…", new[]
        {
            "a^((p−1)/2) mod p, which is 1 exactly for quadratic residues",
            "a^p mod p",
            "gcd(a, p)",
            "the continued fraction of a/p",
        }, "Fermat's little theorem splits a^(p−1) ≡ 1 into ±1 halves; the +1 half is the residues."),
        new("factorbase-19", QuizTopic.FactorBase, "Roughly what fraction of candidate primes end up qualifying for the factor base?", new[]
        {
            "About half",
            "All of them",
            "About one in log N",
            "Exactly one quarter",
        }, "For a random N, each odd prime has (N/p) = 1 with probability about 1/2."),
        new("factorbase-20", QuizTopic.FactorBase, "In this app, which artifact does the factor-base stage produce?", new[]
        {
            "factor_base.txt",
            "dependencies.txt",
            "relations_0000.txt",
            "factors.txt",
        }, "Each pipeline stage writes its own artifact; the base of primes (with roots and logs) comes first."),
        new("factorbase-21", QuizTopic.FactorBase, "Which algorithm computes square roots modulo a prime, as needed for sieving roots?", new[]
        {
            "Tonelli–Shanks",
            "Euclid's algorithm",
            "Gram–Schmidt",
            "Miller–Rabin",
        }, "Tonelli–Shanks finds r with r² ≡ N (mod p); Euclid does gcds and Miller–Rabin tests primality."),
        new("factorbase-22", QuizTopic.FactorBase, "Why do the roots of x² ≡ N (mod p) matter to the sieve?", new[]
        {
            "They mark the starting offsets of the arithmetic progressions where p divides y(x)",
            "They are the factors of N",
            "They bound the sieve interval",
            "They determine the multiplier",
        }, "p | y(x) exactly when x lands in one of the two residue classes derived from the roots."),
        new("factorbase-23", QuizTopic.FactorBase, "The factor base {2, 17, 23, 29} has four primes. How long is each relation's exponent vector?", new[]
        {
            "4 entries — one per factor-base prime",
            "15347 entries",
            "2 entries",
            "8 entries — two per prime",
        }, "Each vector position records (mod 2) the exponent of one base prime in that relation."),
        new("factorbase-24", QuizTopic.FactorBase, "A prime with Legendre symbol (N/p) = −1 is…", new[]
        {
            "skipped: it can never divide any sieved value y(x)",
            "added to the base with a warning",
            "used only for negative x",
            "the large-prime bound",
        }, "Non-residue primes cannot solve x² ≡ N (mod p), so sieving with them would mark nothing."),
        new("factorbase-25", QuizTopic.FactorBase, "Why do sieve implementations often include −1 as an extra \"prime\" in the factor base?", new[]
        {
            "To track the sign of negative polynomial values as one more exponent mod 2",
            "Because −1 divides every integer",
            "To make the base size even",
            "To handle the prime 2",
        }, "For x below √N the polynomial goes negative; a sign bit in the vector lets those relations combine correctly."),
    };
}

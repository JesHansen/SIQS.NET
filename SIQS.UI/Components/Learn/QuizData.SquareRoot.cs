namespace SIQS.UI.Components.Learn;

public static partial class QuizData
{
    private static readonly IReadOnlyList<QuizQuestion> SquareRoot = new QuizQuestion[]
    {
        new("squareroot-01", QuizTopic.SquareRoot, "A dependency yields X ≡ Y (mod N). What next?", new[]
        {
            "Try another dependency — this one only gives the trivial factorization",
            "Restart sieving from scratch",
            "Increase the factor base",
            "Output N as prime",
        }, "X ≡ ±Y is the useless half of the outcomes; the next dependency is independent and free to try."),
        new("squareroot-02", QuizTopic.SquareRoot, "In the worked example, gcd(15347, 7331 − 1460) equals:", new[]
        {
            "103",
            "149",
            "1",
            "15347",
        }, "7331 − 1460 = 5871 = 3 · 19 · 103, and 103 is the factor of 15347 it shares."),
        new("squareroot-03", QuizTopic.SquareRoot, "How is X computed from a dependency, in the worked example?", new[]
        {
            "As the product of the (x + 124) values: 124 · 127 · 195 ≡ 1460 (mod 15347)",
            "As the sum 124 + 127 + 195",
            "As the largest of the three values",
            "As gcd(124, 127, 195)",
        }, "The left sides of the combined relations multiply together, reduced mod N as you go."),
        new("squareroot-04", QuizTopic.SquareRoot, "How is Y computed from a dependency?", new[]
        {
            "Halve each summed prime exponent and multiply the primes out, mod N",
            "Take the square root of N",
            "Multiply the x values",
            "It equals X by definition",
        }, "The dependency makes every exponent sum even, so the halved exponents give an exact integer square root."),
        new("squareroot-05", QuizTopic.SquareRoot, "Why is halving the summed exponents always possible at this stage?", new[]
        {
            "The dependency was chosen precisely so that every prime's total exponent is even",
            "Exponents are rounded down when odd",
            "The solver doubles every exponent first",
            "It is not always possible; odd cases are skipped",
        }, "Even sums are the defining property of a dependency — that's what the matrix stage guaranteed."),
        new("squareroot-06", QuizTopic.SquareRoot, "Which gcds can reveal a factor once X² ≡ Y² (mod N)?", new[]
        {
            "Both gcd(X − Y, N) and gcd(X + Y, N)",
            "Only gcd(X − Y, N)",
            "Only gcd(X · Y, N)",
            "gcd(X, Y)",
        }, "N divides (X−Y)(X+Y); each bracket can carry one of N's factors."),
        new("squareroot-07", QuizTopic.SquareRoot, "For N = p·q, the probability that a random dependency gives only trivial gcds is about…", new[]
        {
            "1/2",
            "0",
            "1/N",
            "3/4",
        }, "Of the four square-root pairings mod pq, two are X ≡ ±Y; the other two split N."),
        new("squareroot-08", QuizTopic.SquareRoot, "After gcd reveals 103, how is the factorization of 15347 completed?", new[]
        {
            "Divide: 15347 / 103 = 149, then primality-test both factors",
            "Run the whole sieve again on 103",
            "Take gcd(103, 149)",
            "Nothing — 103 alone is the answer",
        }, "The cofactor comes by division; primality tests confirm the job is finished."),
        new("squareroot-09", QuizTopic.SquareRoot, "gcd(X − Y, N) returns N itself. What happened?", new[]
        {
            "X ≡ Y (mod N), so X − Y ≡ 0 — a trivial dependency",
            "N is prime",
            "The relation set was corrupt",
            "Y was computed mod the wrong modulus",
        }, "gcd(0, N) = N; the congruence collapsed onto itself and carries no information."),
        new("squareroot-10", QuizTopic.SquareRoot, "X ≡ −Y (mod N) makes which gcd trivial?", new[]
        {
            "gcd(X + Y, N) = N, since X + Y ≡ 0",
            "gcd(X − Y, N) = N",
            "Both gcds equal 1",
            "Neither; −Y always works",
        }, "The mirrored case shifts the collapse to the plus side — equally useless, equally common."),
        new("squareroot-11", QuizTopic.SquareRoot, "Why are X and Y reduced mod N at every multiplication step?", new[]
        {
            "The unreduced products would have astronomically many digits",
            "The gcd requires inputs below N",
            "Reduction reveals the factors early",
            "It is only a stylistic convention",
        }, "Hundreds of relations multiplied without reduction would dwarf N itself; mod-N keeps every intermediate small."),
        new("squareroot-12", QuizTopic.SquareRoot, "Y is, by construction, a square root (mod N) of what?", new[]
        {
            "The product of the combined relations' smooth values y(x)",
            "N itself",
            "The product of the x values",
            "The factor-base primes' product",
        }, "X² ≡ ∏y(x) ≡ Y² (mod N) — both sides are square roots of the same product."),
        new("squareroot-13", QuizTopic.SquareRoot, "What final check is applied to the discovered factors?", new[]
        {
            "Multiply them back together and confirm the product is N",
            "Re-run linear algebra",
            "Check them against a prime table",
            "No check is possible",
        }, "One multiplication proves correctness end-to-end, whatever happened upstream."),
        new("squareroot-14", QuizTopic.SquareRoot, "Why does gcd(X − Y, N) work at all? Because N divides…", new[]
        {
            "(X − Y)(X + Y), so N's prime factors distribute across the two brackets",
            "X − Y directly",
            "both X and Y",
            "X² + Y²",
        }, "X² − Y² ≡ 0 (mod N); when the factors of N split between the brackets, each gcd catches one."),
        new("squareroot-15", QuizTopic.SquareRoot, "What congruence do the three combined example relations establish?", new[]
        {
            "1460² ≡ 7331² (mod 15347)",
            "1460 ≡ 7331 (mod 15347)",
            "124² ≡ 15347 (mod 29)",
            "22678² ≡ 15347 (mod 103)",
        }, "X = 1460 and Y ≡ 22678 ≡ 7331; their squares agree mod N, and X ≢ ±Y makes it count."),
        new("squareroot-16", QuizTopic.SquareRoot, "Across the three example relations, each of 2, 17, 23, 29 has total exponent 2. So Y equals…", new[]
        {
            "2 · 17 · 23 · 29 = 22678 (≡ 7331 mod 15347)",
            "2² · 17² · 23² · 29²",
            "2 + 17 + 23 + 29",
            "√15347 rounded to an integer",
        }, "Halving each exponent 2 → 1 gives one copy of each prime; their product is the integer square root."),
        new("squareroot-17", QuizTopic.SquareRoot, "Every available dependency produced only trivial gcds. What is the remedy?", new[]
        {
            "Sieve more relations and solve again for fresh dependencies",
            "Accept that N is prime",
            "Swap X and Y and retry",
            "Reduce the factor base",
        }, "Trivial streaks are bad luck (each is a coin flip); new independent dependencies need new material."),
        new("squareroot-18", QuizTopic.SquareRoot, "In this app, what does the \"continue square root after factor\" option do?", new[]
        {
            "Keeps processing further dependencies after the first factor, to split N more completely",
            "Restarts the pipeline from sieving",
            "Doubles the number of dependencies",
            "Re-verifies the first factor repeatedly",
        }, "More dependencies can peel off further factors when N has more than two prime factors."),
        new("squareroot-19", QuizTopic.SquareRoot, "In this app, which artifact records the final result?", new[]
        {
            "factors.txt",
            "dependencies.txt",
            "matrix_meta.txt",
            "partials_0000.txt",
        }, "The square-root stage writes the discovered factors; job.json records the overall status."),
        new("squareroot-20", QuizTopic.SquareRoot, "Why primality-test the factors that gcd produces?", new[]
        {
            "A gcd result can itself be composite and need further splitting",
            "gcd sometimes returns numbers larger than N",
            "Primality proves the gcd was computed correctly",
            "It's needed to update the factor base",
        }, "gcd guarantees a nontrivial divisor, not a prime one; composite divisors go back for more factoring."),
        new("squareroot-21", QuizTopic.SquareRoot, "How does the square-root stage's cost compare with sieving?", new[]
        {
            "Negligible — a few modular products and gcds against hours of sieving",
            "It dominates the total runtime",
            "Roughly equal",
            "It grows with the square of the interval",
        }, "Everything here is polynomial-time bookkeeping on numbers the size of N."),
        new("squareroot-22", QuizTopic.SquareRoot, "In the worked example, gcd(15347, 7331 + 1460) equals:", new[]
        {
            "149",
            "103",
            "1",
            "15347",
        }, "7331 + 1460 = 8791 = 59 · 149 — the plus-side gcd catches N's other prime factor."),
        new("squareroot-23", QuizTopic.SquareRoot, "Which statement about a dependency with X ≡ −Y (mod N) is correct?", new[]
        {
            "It is trivial: the minus-side gcd gives 1 and the plus-side gives N",
            "It always yields both factors at once",
            "It cannot occur for odd N",
            "It indicates a filtering bug",
        }, "−Y is a legitimate square root of Y²; landing on it is the other half of the bad-luck cases."),
        new("squareroot-24", QuizTopic.SquareRoot, "About how many dependencies must be tried, on average, before a factor appears?", new[]
        {
            "Around 2 — each independent try succeeds with probability about 1/2",
            "Around 100",
            "Exactly 64, always",
            "One; the first always works",
        }, "Geometric with p ≈ 1/2: expect two tries, and a handful of spares makes failure vanishingly unlikely."),
        new("squareroot-25", QuizTopic.SquareRoot, "The square-root stage receives its input from…", new[]
        {
            "the dependencies found by linear algebra, plus the relations they reference",
            "the raw sieve interval",
            "the factor base only",
            "the previous job's factors.txt",
        }, "Each dependency lists which relations to multiply; the stage replays them into X, Y and gcds."),
    };
}

namespace SIQS.UI.Components.Learn;

public static partial class QuizData
{
    private static readonly IReadOnlyList<QuizQuestion> Foundations = new QuizQuestion[]
    {
        new("foundations-01", QuizTopic.Foundations, "If X² ≡ Y² (mod N) with X ≢ ±Y (mod N), which quantity reveals a factor of N?", new[]
        {
            "gcd(X − Y, N)",
            "gcd(X + Y, X − Y)",
            "(X − Y) mod N",
            "N / (X − Y)",
        }, "N divides (X−Y)(X+Y) but neither bracket alone, so gcd(X−Y, N) is a nontrivial factor."),
        new("foundations-02", QuizTopic.Foundations, "Multiplying two primes is easy; what makes N = p·q hard to undo?", new[]
        {
            "No known classical algorithm factors general N in polynomial time",
            "p and q are always congruent to 3 mod 4",
            "N hides the primes with a secret key",
            "Division by candidate primes is impossible",
        }, "Factoring is easy to verify but (as far as anyone knows) classically hard to perform — the asymmetry RSA relies on."),
        new("foundations-03", QuizTopic.Foundations, "Fermat's factoring method writes N as which expression?", new[]
        {
            "X² − Y², which factors as (X − Y)(X + Y)",
            "X² + Y², which factors over the Gaussian integers",
            "2^X − Y, solved by discrete logarithm",
            "X·Y + 1, solved by continued fractions",
        }, "Every odd composite is a difference of squares; finding one gives the factors directly."),
        new("foundations-04", QuizTopic.Foundations, "When is Fermat's basic difference-of-squares method fast?", new[]
        {
            "When N's two factors are close to each other (both near √N)",
            "When N is even",
            "When N has many small factors",
            "When N is a perfect square plus one",
        }, "X starts at ⌈√N⌉ and climbs; the search is short only if a factorization sits near √N."),
        new("foundations-05", QuizTopic.Foundations, "Why is a congruence X² ≡ Y² (mod N) with X ≡ ±Y (mod N) useless for factoring?", new[]
        {
            "gcd(X − Y, N) then comes out as N or 1 — a trivial answer",
            "The congruence is arithmetically false",
            "It only happens when N is prime",
            "gcd cannot be computed when X and Y are congruent",
        }, "X ≡ Y makes X−Y ≡ 0 so the gcd is N; X ≡ −Y pushes the whole factorization into X+Y instead."),
        new("foundations-06", QuizTopic.Foundations, "For N = p·q, roughly what fraction of random congruences of squares yield a nontrivial factor?", new[]
        {
            "About half",
            "Essentially all of them",
            "About one in N",
            "Exactly one per factor base",
        }, "X² ≡ Y² allows four square-root pairings; two of them (X ≡ ±Y) are trivial, two split N."),
        new("foundations-07", QuizTopic.Foundations, "An integer is called B-smooth when…", new[]
        {
            "all of its prime factors are at most B",
            "it has at most B prime factors",
            "it is divisible by every prime up to B",
            "it is smaller than B²",
        }, "Smoothness bounds the largest prime factor; smooth values are the raw material of sieve algorithms."),
        new("foundations-08", QuizTopic.Foundations, "What was Kraitchik's key relaxation of Fermat's method?", new[]
        {
            "Combine many congruences x² ≡ small product (mod N) and multiply a subset into a square",
            "Search for X and Y simultaneously from both ends",
            "Replace squares with cubes",
            "Use only prime values of x",
        }, "One lucky X is rare; squares assembled from many easy relations are plentiful."),
        new("foundations-09", QuizTopic.Foundations, "Why does multiplying relations together help build a perfect square?", new[]
        {
            "Prime exponents add, so subsets can be chosen to make every exponent even",
            "Multiplication makes the numbers smaller",
            "Products are always quadratic residues",
            "It cancels the modulus",
        }, "A product is a square exactly when each prime appears an even number of times; addition of exponent vectors makes that a linear-algebra problem."),
        new("foundations-10", QuizTopic.Foundations, "A product of integers is a perfect square exactly when…", new[]
        {
            "every prime in its factorization appears with an even exponent",
            "it is even",
            "it has an odd number of divisors and is prime",
            "its last digit is 1, 4, 5, 6, or 9",
        }, "√(∏ pᵉ) is an integer iff every e is even — the criterion the whole matrix stage serves."),
        new("foundations-11", QuizTopic.Foundations, "The quadratic sieve's running time depends mainly on…", new[]
        {
            "the size of N itself, not the size of its smallest factor",
            "the size of N's smallest prime factor",
            "how close the factors are to each other",
            "the number of digits of the largest factor",
        }, "QS is a general-purpose method: unlike Pollard rho or ECM, it doesn't get faster when a factor happens to be small."),
        new("foundations-12", QuizTopic.Foundations, "Which algorithm displaced the quadratic sieve for very large numbers in the mid-1990s?", new[]
        {
            "The general number field sieve",
            "Pollard's rho method",
            "Trial division with wheel factorization",
            "Shor's algorithm on classical hardware",
        }, "GNFS is asymptotically faster and took over beyond roughly 100–110 digits; QS still wins below that."),
        new("foundations-13", QuizTopic.Foundations, "Roughly where is the crossover between quadratic sieve and number field sieve?", new[]
        {
            "Around 100–110 decimal digits",
            "Around 20 digits",
            "Around 500 digits",
            "There is none; QS is always faster",
        }, "Below ~100 digits QS's simplicity wins; above, GNFS's better asymptotics dominate."),
        new("foundations-14", QuizTopic.Foundations, "The 1994 factorization of RSA-129 decoded which famous message?", new[]
        {
            "THE MAGIC WORDS ARE SQUEAMISH OSSIFRAGE",
            "ATTACK AT DAWN",
            "HELLO WORLD",
            "THE EAGLE HAS LANDED",
        }, "The 129-digit challenge from Martin Gardner's 1977 column fell to a distributed QS effort with about 600 volunteers."),
        new("foundations-15", QuizTopic.Foundations, "Who invented the quadratic sieve, and when?", new[]
        {
            "Carl Pomerance, in 1981–82",
            "Pierre de Fermat, in the 1600s",
            "Peter Montgomery, in 1994",
            "Maurice Kraitchik, in 1970",
        }, "Pomerance replaced Dixon's random values with a sieved polynomial, creating the fastest general method of its era."),
        new("foundations-16", QuizTopic.Foundations, "What did Morrison and Brillhart achieve in 1970?", new[]
        {
            "Factored the Fermat number F₇ = 2¹²⁸ + 1 with the continued-fraction method",
            "Invented the quadratic sieve",
            "Broke RSA-129",
            "Proved factoring is NP-complete",
        }, "Their CFRAC factorization of the 39-digit F₇ established the relations-plus-linear-algebra template QS inherits."),
        new("foundations-17", QuizTopic.Foundations, "What is Dixon's random-squares method known for?", new[]
        {
            "The first rigorous runtime analysis in the relations-and-linear-algebra family",
            "Being the fastest method in practice",
            "Requiring no smooth numbers",
            "Working only on Fermat numbers",
        }, "Dixon's method is slower in practice but its randomness made honest proofs possible."),
        new("foundations-18", QuizTopic.Foundations, "Why is a claimed factorization of N trivial to verify?", new[]
        {
            "Multiply the factors and compare with N",
            "Rerun the entire sieve",
            "Check N against a table of primes",
            "Verification is as hard as factoring",
        }, "One multiplication settles it — the easy direction of the asymmetry."),
        new("foundations-19", QuizTopic.Foundations, "In this app's worked example, N = 15347 factors as…", new[]
        {
            "103 × 149",
            "101 × 151",
            "113 × 137",
            "3 × 5119",
        }, "103 · 149 = 15347; the walkthrough recovers these via three combined relations."),
        new("foundations-20", QuizTopic.Foundations, "What is the correct order of the pipeline stages?", new[]
        {
            "Factor base → sieving → filtering → linear algebra → square root",
            "Sieving → factor base → square root → filtering → linear algebra",
            "Linear algebra → sieving → factor base → square root → filtering",
            "Filtering → linear algebra → sieving → factor base → square root",
        }, "Build the primes, harvest relations, clean them, find dependencies, then extract factors."),
        new("foundations-21", QuizTopic.Foundations, "In QS, a \"relation\" is…", new[]
        {
            "a congruence x² ≡ (product of factor-base primes) (mod N)",
            "a pair of primes that multiply to N",
            "an entry in the gcd table",
            "a polynomial with integer roots",
        }, "Each relation records one smooth polynomial value together with its prime exponents."),
        new("foundations-22", QuizTopic.Foundations, "Why is the algorithm called the *quadratic* sieve?", new[]
        {
            "The sieved values come from a quadratic polynomial such as (x+m)² − N",
            "It runs in quadratic time",
            "It only factors squares",
            "The matrix is square",
        }, "The polynomial's quadratic form is what makes divisibility fall into arithmetic progressions."),
        new("foundations-23", QuizTopic.Foundations, "How expensive is the gcd computation used to extract factors?", new[]
        {
            "Cheap — Euclid's algorithm runs in polynomial time",
            "Exponential in the size of N",
            "As costly as the sieving stage",
            "It requires the factorization of both arguments",
        }, "Euclid's algorithm needs only O(log N) divisions; the hard work is building the congruence, not the gcd."),
        new("foundations-24", QuizTopic.Foundations, "Compute gcd(21, 15347).", new[]
        {
            "1",
            "3",
            "7",
            "21",
        }, "15347 = 730·21 + 17, then gcd(21, 17) = gcd(17, 4) = gcd(4, 1) = 1; 15347 has no factor of 3 or 7."),
        new("foundations-25", QuizTopic.Foundations, "Why is N tested for primality before running the sieve?", new[]
        {
            "A prime N has no nontrivial factor, so the whole pipeline would run for nothing",
            "Primes make the matrix singular",
            "The sieve crashes on primes",
            "Primality testing finds the factors directly",
        }, "Fast primality tests (unlike factoring!) settle the question first; QS assumes a composite input."),
    };
}

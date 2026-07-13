namespace SIQS.UI.Components.Learn;

public static partial class QuizData
{
    private static readonly IReadOnlyList<QuizQuestion> Sieving = new QuizQuestion[]
    {
        new("sieving-01", QuizTopic.Sieving, "What does the sieve add at positions where prime p divides y(x)?", new[]
        {
            "approximately log p",
            "p itself",
            "1/p",
            "the exponent of p",
        }, "Summed logs approximate log of the smooth part, letting one comparison replace repeated division."),
        new("sieving-02", QuizTopic.Sieving, "Why is sieving faster than trial-dividing every y(x)?", new[]
        {
            "Divisible positions form arithmetic progressions, so each prime touches only its own multiples",
            "It skips negative values",
            "It uses floating point",
            "It factors only prime x",
        }, "Like Eratosthenes, each prime strides through the array; most cells are never touched by most primes."),
        new("sieving-03", QuizTopic.Sieving, "In the worked example, what is y(0) = (0+124)² − 15347?", new[]
        {
            "29",
            "124",
            "782",
            "15347",
        }, "124² = 15376 and 15376 − 15347 = 29 — a factor-base prime, so x = 0 is instantly smooth."),
        new("sieving-04", QuizTopic.Sieving, "Why accumulate logarithms rather than dividing at every position?", new[]
        {
            "Adding a precomputed log is far cheaper; exact division is saved for the few candidates",
            "Logarithms are exact and division is not",
            "Divisions overflow 64-bit integers",
            "Logs make the values smooth",
        }, "The sieve is an inexpensive filter; only threshold-crossing cells get the expensive exact treatment."),
        new("sieving-05", QuizTopic.Sieving, "How many arithmetic progressions does each odd factor-base prime contribute?", new[]
        {
            "Two — one for each square root of N mod p",
            "One",
            "p − 1",
            "Four",
        }, "The two roots ±r mod p each seed a progression of positions where p divides y(x)."),
        new("sieving-06", QuizTopic.Sieving, "After sieving an interval, which positions become candidate relations?", new[]
        {
            "Those whose accumulated log-sum comes close to log y(x)",
            "Those with the smallest y(x)",
            "Every odd position",
            "Those never touched by any prime",
        }, "A near-complete log-sum means the value is (probably) fully smooth; exact factoring then confirms."),
        new("sieving-07", QuizTopic.Sieving, "Why does the threshold comparison include an error margin?", new[]
        {
            "Rounded logs, skipped prime powers and tiny primes make the sum slightly inexact",
            "Because y(x) is unknown during sieving",
            "To exclude negative values",
            "To keep the matrix sparse",
        }, "The sieve trades exactness for speed; slack in the threshold catches smooth values the shortcuts underestimate."),
        new("sieving-08", QuizTopic.Sieving, "How do production sieves account for values divisible by p² or higher powers?", new[]
        {
            "They also sieve prime-power progressions (or absorb the deficit into the error margin)",
            "They discard such values",
            "Higher powers cannot occur",
            "They multiply the threshold by p",
        }, "y(2) = 529 = 23² in the worked example shows why: a single log 23 undercounts its true weight."),
        new("sieving-09", QuizTopic.Sieving, "What happens to a position that crosses the sieve threshold?", new[]
        {
            "Its y(x) is trial-divided over the factor base to confirm smoothness and record exponents",
            "It is immediately written to factors.txt",
            "It becomes a new factor-base prime",
            "The interval restarts from there",
        }, "The sieve only nominates candidates; exact division builds the actual exponent vector (or rejects a false positive)."),
        new("sieving-10", QuizTopic.Sieving, "Why do smooth values become rarer as x moves away from 0?", new[]
        {
            "y(x) grows quadratically, and larger numbers are less likely to be smooth",
            "The sieve loses precision",
            "The factor base shrinks with x",
            "Rounding errors accumulate",
        }, "Smoothness probability falls sharply with size — the core reason a single polynomial exhausts its usefulness."),
        new("sieving-11", QuizTopic.Sieving, "What problem do multiple polynomials (MPQS) solve?", new[]
        {
            "They keep sieved values small by switching polynomials instead of stretching one interval",
            "They remove the need for a factor base",
            "They parallelize the gcd",
            "They eliminate partial relations",
        }, "Many short, small-valued intervals beat one long interval whose values have grown huge."),
        new("sieving-12", QuizTopic.Sieving, "In SIQS, polynomials take the form y = (Ax + B)² − N. What must B satisfy?", new[]
        {
            "B² ≡ N (mod A)",
            "B = A + 1",
            "B divides N",
            "B is prime",
        }, "With B² ≡ N (mod A), A divides (Ax+B)² − N, so the value actually sieved is the much smaller quotient ((Ax+B)² − N)/A."),
        new("sieving-13", QuizTopic.Sieving, "How is the leading coefficient A chosen in SIQS?", new[]
        {
            "As a product of several factor-base primes",
            "As a random prime larger than N",
            "Always equal to 2",
            "As the largest prime below √N",
        }, "Composing A from s base primes yields many valid Bs and keeps A near its optimal size."),
        new("sieving-14", QuizTopic.Sieving, "With A built from s distinct primes, how many essentially different B values exist?", new[]
        {
            "2^(s−1)",
            "s",
            "s²",
            "One",
        }, "Each prime contributes a ± choice of root mod that prime; halving for the global sign leaves 2^(s−1) polynomials."),
        new("sieving-15", QuizTopic.Sieving, "What does the \"self-initializing\" in SIQS refer to?", new[]
        {
            "Switching to the next B is cheap — root updates are precomputed deltas, not fresh computations",
            "The sieve chooses its own factor base",
            "No parameters need to be supplied",
            "The first polynomial initializes the matrix",
        }, "Gray-code stepping through the B values updates all sieving roots with one addition per prime."),
        new("sieving-16", QuizTopic.Sieving, "What is a partial relation?", new[]
        {
            "A value that is smooth except for one leftover prime below the large-prime bound",
            "A relation missing its x value",
            "A relation found in only half the interval",
            "An exponent vector with an odd length",
        }, "Nearly-smooth values are too plentiful to waste; the single large prime is tracked for later pairing."),
        new("sieving-17", QuizTopic.Sieving, "Why are partial relations worth keeping?", new[]
        {
            "Two partials sharing the same large prime combine into the equivalent of a full relation",
            "They are faster to verify",
            "Each is worth two full relations",
            "The matrix requires them",
        }, "Multiplying the pair makes the shared prime's exponent even, so it drops out of the mod-2 vector."),
        new("sieving-18", QuizTopic.Sieving, "Which phenomenon makes large-prime matches common once many partials accumulate?", new[]
        {
            "The birthday paradox",
            "The prime number theorem",
            "Quadratic reciprocity",
            "Benford's law",
        }, "Collisions among random large primes appear far sooner than intuition suggests, just like shared birthdays."),
        new("sieving-19", QuizTopic.Sieving, "Why do sieve implementations process the interval in fixed-size blocks?", new[]
        {
            "So the active array fits in CPU cache, keeping the memory accesses fast",
            "Because polynomials are only valid per block",
            "To bound the size of y(x)",
            "Blocks are required for correctness",
        }, "Sieving is memory-bound; cache-sized blocks are one of the biggest practical speedups."),
        new("sieving-20", QuizTopic.Sieving, "The relation y(3) = 782 = 2 · 17 · 23. Its exponent vector mod 2 over (2, 17, 23, 29) is:", new[]
        {
            "(1, 1, 1, 0)",
            "(1, 1, 1, 1)",
            "(0, 0, 0, 1)",
            "(2, 17, 23, 0)",
        }, "Each of 2, 17, 23 appears once (odd), 29 not at all — record 1, 1, 1, 0."),
        new("sieving-21", QuizTopic.Sieving, "y(2) = 529 = 23², so its exponent vector mod 2 is (0,0,0,0). What does such a relation give?", new[]
        {
            "A congruence of squares by itself — here 126² ≡ 23² (mod N) — no matrix step needed",
            "Nothing; it must be discarded",
            "A duplicate of x = 0",
            "Proof that N is prime",
        }, "An all-even vector is already a square on both sides; in the toy example gcd(126−23, N) = 103 immediately."),
        new("sieving-22", QuizTopic.Sieving, "In this app's distributed mode, which pipeline stage is farmed out to volunteer machines?", new[]
        {
            "Sieving",
            "Linear algebra",
            "The square root",
            "Filtering",
        }, "Sieving dominates the runtime and splits into independent work units, so volunteers lease intervals while the server does the rest."),
        new("sieving-23", QuizTopic.Sieving, "Why does sieving parallelize so well?", new[]
        {
            "Different polynomials and intervals are completely independent of one another",
            "The matrix can be split by rows",
            "gcd is associative",
            "It doesn't — only filtering parallelizes",
        }, "No communication is needed until relations are collected — near-perfect scaling across cores and machines."),
        new("sieving-24", QuizTopic.Sieving, "When does the sieving stage stop?", new[]
        {
            "When the number of usable relations reaches the target (factor-base size plus a margin)",
            "After exactly one interval",
            "When y(x) becomes negative",
            "When the first factor is found",
        }, "Enough excess rows guarantee matrix dependencies; sieving beyond the target is wasted work."),
        new("sieving-25", QuizTopic.Sieving, "For large N, which pipeline stage usually consumes the most total time?", new[]
        {
            "Sieving",
            "The factor-base construction",
            "The square root",
            "Writing artifacts to disk",
        }, "Relation hunting dwarfs everything else — which is why it gets multiple polynomials, caches, and volunteers."),
    };
}

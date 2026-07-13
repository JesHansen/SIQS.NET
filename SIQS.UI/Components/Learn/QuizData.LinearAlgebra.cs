namespace SIQS.UI.Components.Learn;

public static partial class QuizData
{
    private static readonly IReadOnlyList<QuizQuestion> LinearAlgebra = new QuizQuestion[]
    {
        new("linalg-01", QuizTopic.LinearAlgebra, "Exponent vectors are reduced mod 2 because…", new[]
        {
            "a product is a perfect square exactly when every prime exponent is even",
            "GF(2) avoids overflow",
            "the matrix must be binary to be sparse",
            "odd exponents indicate sieving errors",
        }, "Only parity decides squareness, so all the solver needs is each exponent's last bit."),
        new("linalg-02", QuizTopic.LinearAlgebra, "Why collect a few more relations than factor-base primes?", new[]
        {
            "More rows than columns guarantees linear dependencies exist",
            "spares are needed for checksums",
            "the sieve discards half",
            "gcd needs at least two candidates",
        }, "Vectors exceeding the space's dimension must be dependent — excess rows are guaranteed dependencies."),
        new("linalg-03", QuizTopic.LinearAlgebra, "What are the entries of the matrix handed to the solver?", new[]
        {
            "The exponents of each factor-base prime in each relation, reduced mod 2",
            "The raw y(x) values",
            "The gcds of relation pairs",
            "The sieve log-sums",
        }, "Row = relation, column = prime, entry = exponent parity."),
        new("linalg-04", QuizTopic.LinearAlgebra, "In this context, a \"dependency\" is…", new[]
        {
            "a subset of rows whose XOR is the zero vector",
            "a row containing only ones",
            "two rows sharing a prime",
            "a column of even weight",
        }, "Zero XOR means all exponents sum to even — the subset's product is a perfect square."),
        new("linalg-05", QuizTopic.LinearAlgebra, "A vector in the nullspace of the (transposed) matrix corresponds to…", new[]
        {
            "a selection of relations whose combined exponents are all even",
            "a factor of N",
            "a new factor-base prime",
            "an error in sieving",
        }, "Each nullspace vector is a recipe: multiply exactly the relations it marks."),
        new("linalg-06", QuizTopic.LinearAlgebra, "Why use iterative sparse solvers instead of Gaussian elimination on big jobs?", new[]
        {
            "Elimination fills the sparse matrix in and its time/memory costs explode; sparse methods don't",
            "Gaussian elimination is numerically unstable mod 2",
            "Sparse solvers find more factors",
            "Elimination cannot run on binary data",
        }, "Fill-in destroys sparsity; Block Lanczos/Wiedemann work via matrix-vector products and stay lean."),
        new("linalg-07", QuizTopic.LinearAlgebra, "Which sparse solver does this app use for the matrix stage?", new[]
        {
            "Block Lanczos",
            "Simplex",
            "Strassen multiplication",
            "Newton's method",
        }, "Block Lanczos over GF(2) is the classic choice for factoring matrices, processing 64 vectors per pass."),
        new("linalg-08", QuizTopic.LinearAlgebra, "Each dependency found by the solver yields…", new[]
        {
            "one candidate congruence of squares to try in the square-root stage",
            "one guaranteed prime factor",
            "one new relation",
            "a smaller factor base",
        }, "The dependency defines X and Y; whether gcd(X−Y, N) is nontrivial is decided afterwards."),
        new("linalg-09", QuizTopic.LinearAlgebra, "Why does the solver return several dependencies rather than just one?", new[]
        {
            "Each has roughly a 1/2 chance of giving only a trivial factor, so spares are needed",
            "More dependencies shrink the matrix",
            "One dependency per factor is required",
            "The first is always trivial by convention",
        }, "With, say, 64 independent dependencies, the chance all fail is astronomically small."),
        new("linalg-10", QuizTopic.LinearAlgebra, "v₁ = (0,0,0,1), v₂ = (1,1,1,0), v₃ = (1,1,1,1). Which subset is a dependency?", new[]
        {
            "{v₁, v₂, v₃} — their XOR is (0,0,0,0)",
            "{v₁, v₂}",
            "{v₂, v₃}",
            "{v₁, v₃}",
        }, "Adding all three clears every coordinate — exactly the combination the worked example uses."),
        new("linalg-11", QuizTopic.LinearAlgebra, "With the same vectors, what is v₂ XOR v₃?", new[]
        {
            "(0,0,0,1) — not zero, so {v₂, v₃} is not a dependency",
            "(0,0,0,0)",
            "(1,1,1,1)",
            "(1,0,0,1)",
        }, "They differ only in the last coordinate, which survives; 29's exponent would stay odd."),
        new("linalg-12", QuizTopic.LinearAlgebra, "Addition in GF(2) is the same as which bit operation?", new[]
        {
            "XOR",
            "AND",
            "OR",
            "NOT",
        }, "1+1 = 0 with no carry — addition and subtraction coincide, and both are XOR."),
        new("linalg-13", QuizTopic.LinearAlgebra, "Roughly how many independent dependencies should the solver find?", new[]
        {
            "About the excess: number of rows minus the rank",
            "Exactly one",
            "One per prime",
            "As many as there are rows",
        }, "Every row beyond the rank adds one dimension to the nullspace."),
        new("linalg-14", QuizTopic.LinearAlgebra, "In the convention used here, each relation contributes…", new[]
        {
            "one row of the matrix",
            "one column of the matrix",
            "one diagonal entry",
            "one full block",
        }, "Rows are relations, columns are primes; a dependency picks a set of rows."),
        new("linalg-15", QuizTopic.LinearAlgebra, "What makes *block* methods efficient on real hardware?", new[]
        {
            "They advance 64 vectors at once, one per bit of a machine word",
            "They block cache misses",
            "They split N into blocks",
            "They round small entries to zero",
        }, "A single word-wide XOR processes 64 GF(2) operations — near-free parallelism inside each instruction."),
        new("linalg-16", QuizTopic.LinearAlgebra, "Why is there no rounding error in this linear algebra?", new[]
        {
            "GF(2) arithmetic is exact — every value is a bit",
            "The solver compensates errors adaptively",
            "Errors cancel out over many iterations",
            "There is rounding error; it's just tolerated",
        }, "Unlike floating-point Lanczos, the GF(2) version is purely combinatorial."),
        new("linalg-17", QuizTopic.LinearAlgebra, "The asymptotic cost of Gaussian elimination on an n×n matrix is about…", new[]
        {
            "n³",
            "n log n",
            "n",
            "2ⁿ",
        }, "Cubic cost plus dense storage is what makes elimination hopeless at factoring scale."),
        new("linalg-18", QuizTopic.LinearAlgebra, "A relation whose matrix row is all zeros means…", new[]
        {
            "its value was already a perfect square — a congruence of squares by itself",
            "the relation is corrupt",
            "the factor base is too small",
            "N has been factored",
        }, "All exponents even (like y(2) = 23²) needs no combination at all."),
        new("linalg-19", QuizTopic.LinearAlgebra, "In this app, which artifact does the linear-algebra stage produce?", new[]
        {
            "dependencies.txt",
            "filtered_matrix.txt",
            "factor_base.txt",
            "relations_0000.txt",
        }, "The solver writes the dependency sets; the square-root stage reads them back."),
        new("linalg-20", QuizTopic.LinearAlgebra, "The matrix turns out to have full rank and no nontrivial nullspace. What now?", new[]
        {
            "Collect more relations — with more rows a dependency becomes guaranteed",
            "Declare N prime",
            "Rerun the solver with a new seed until one appears",
            "Transpose the matrix and retry",
        }, "No dependency means not enough excess; the fix is more sieving, not more solving."),
        new("linalg-21", QuizTopic.LinearAlgebra, "What is the rank over GF(2) of {(0,0,0,1), (1,1,1,0), (1,1,1,1)}?", new[]
        {
            "2 — the third vector is the XOR of the first two",
            "3",
            "1",
            "4",
        }, "v₃ = v₁ + v₂, so only two of the three are independent."),
        new("linalg-22", QuizTopic.LinearAlgebra, "Why are factoring matrices naturally sparse?", new[]
        {
            "A smooth value involves only a handful of primes, so each row has few 1s",
            "Filtering deletes most entries",
            "Sparsity is enforced by the file format",
            "They are not sparse",
        }, "A typical relation touches maybe a dozen primes out of thousands of columns."),
        new("linalg-23", QuizTopic.LinearAlgebra, "What dominates the cost of one Lanczos iteration?", new[]
        {
            "A sparse matrix–vector product — proportional to the number of nonzero entries",
            "A full matrix inversion",
            "Sorting the rows",
            "Computing gcds",
        }, "The whole method is built from repeated cheap products; total cost ≈ dimension × nonzeros per row."),
        new("linalg-24", QuizTopic.LinearAlgebra, "After filtering, a column contains no 1s at all. What does that mean?", new[]
        {
            "That prime no longer appears with odd exponent anywhere — the column can be dropped",
            "The matrix is singular and unusable",
            "That prime divides N",
            "Sieving must restart",
        }, "Empty columns are dead dimensions; removing them shrinks the problem for free."),
        new("linalg-25", QuizTopic.LinearAlgebra, "The solver hands its dependencies to which pipeline stage?", new[]
        {
            "The square root, which turns each into an X, Y pair and takes gcds",
            "Filtering, for a second pass",
            "Sieving, to guide the next interval",
            "The factor base, to add primes",
        }, "Linear algebra finds the recipes; the square-root stage cooks them into factors."),
    };
}

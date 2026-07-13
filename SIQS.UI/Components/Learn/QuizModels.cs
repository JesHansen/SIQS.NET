namespace SIQS.UI.Components.Learn;

public enum QuizTopic
{
    Foundations,
    FactorBase,
    Sieving,
    Filtering,
    LinearAlgebra,
    SquareRoot,
}

public sealed record QuizQuestion(
    string Id,
    QuizTopic Topic,
    string Prompt,
    IReadOnlyList<string> Choices,
    string Explanation);

public sealed record QuizQuestionView(
    QuizQuestion Question,
    IReadOnlyList<string> Choices,
    int CorrectIndex);

public sealed record QuizRound(IReadOnlyList<QuizQuestionView> Questions);

public static partial class QuizData
{
    private static readonly Lazy<IReadOnlyList<QuizQuestion>> AllQuestions = new(() => new[]
        { Foundations, FactorBase, Sieving, Filtering, LinearAlgebra, SquareRoot }
        .SelectMany(questions => questions)
        .ToArray());

    public static IReadOnlyList<QuizQuestion> All => AllQuestions.Value;
}

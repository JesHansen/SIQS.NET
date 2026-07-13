namespace SIQS.UI.Components.Learn;

/// <summary>Creates balanced, shuffled ten-question quiz rounds from the fixed question bank.</summary>
public static class QuizSampler
{
    public static QuizRound CreateRound(IReadOnlyList<QuizQuestion> all, Random random)
    {
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(random);

        var onePerTopic = Enum.GetValues<QuizTopic>()
            .Select(topic => all.Where(question => question.Topic == topic).OrderBy(_ => random.Next()).First())
            .ToList();
        var selectedIds = onePerTopic.Select(question => question.Id).ToHashSet(StringComparer.Ordinal);
        var remaining = all.Where(question => selectedIds.Add(question.Id)).OrderBy(_ => random.Next()).Take(4);
        var questions = onePerTopic.Concat(remaining).OrderBy(_ => random.Next()).Select(question => View(question, random)).ToArray();
        return new QuizRound(questions);
    }

    private static QuizQuestionView View(QuizQuestion question, Random random)
    {
        var choices = question.Choices
            .Select((choice, index) => new Choice(choice, index == 0))
            .OrderBy(_ => random.Next())
            .ToArray();
        var correctIndex = Array.FindIndex(choices, choice => choice.IsCorrect);
        return new QuizQuestionView(question, choices.Select(choice => choice.Text).ToArray(), correctIndex);
    }

    private sealed record Choice(string Text, bool IsCorrect);
}

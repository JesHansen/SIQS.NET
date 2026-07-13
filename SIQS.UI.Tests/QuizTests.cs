using SIQS.UI.Components.Learn;

namespace SIQS.UI.Tests;

public sealed class QuizTests
{
    [Fact]
    public void Data_has_the_complete_balanced_question_bank()
    {
        Assert.Equal(150, QuizData.All.Count);
        Assert.Equal(150, QuizData.All.Select(question => question.Id).Distinct(StringComparer.Ordinal).Count());

        foreach (var topic in Enum.GetValues<QuizTopic>())
        {
            Assert.Equal(25, QuizData.All.Count(question => question.Topic == topic));
        }

        Assert.All(QuizData.All, question =>
        {
            Assert.Equal(4, question.Choices.Count);
            Assert.All(question.Choices, choice => Assert.False(string.IsNullOrWhiteSpace(choice)));
            Assert.False(string.IsNullOrWhiteSpace(question.Explanation));
        });
    }

    [Fact]
    public void Sampler_creates_a_balanced_shuffled_round()
    {
        var round = QuizSampler.CreateRound(QuizData.All, new Random(12345));

        Assert.Equal(10, round.Questions.Count);
        Assert.Equal(10, round.Questions.Select(question => question.Question.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(Enum.GetValues<QuizTopic>(), topic => Assert.Contains(round.Questions, question => question.Question.Topic == topic));
        Assert.All(round.Questions, question => Assert.Equal(question.Question.Choices[0], question.Choices[question.CorrectIndex]));
    }

    [Fact]
    public void Different_seeds_create_different_rounds()
    {
        var first = QuizSampler.CreateRound(QuizData.All, new Random(11)).Questions.Select(question => question.Question.Id);
        var second = QuizSampler.CreateRound(QuizData.All, new Random(12)).Questions.Select(question => question.Question.Id);

        Assert.NotEqual(first, second);
    }
}

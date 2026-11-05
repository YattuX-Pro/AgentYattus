using AgentYattus.Contracts;

namespace AgentYattus.Evals;

/// <summary>La notation et le calcul de coût, vérifiés sans appeler aucun modèle.</summary>
public sealed class ScoringTests
{
    [Fact]
    public void Cost_uses_the_public_price_per_million_tokens()
    {
        Assert.Equal(0.51m, Pricing.Cost("claude-haiku-5-5", 100_000, 1_000_000));
        Assert.Equal(12m, Pricing.Cost("claude-sonnet-5-5", 1_000_000, 1_000_000));
        Assert.Equal(0.0007m, Pricing.Cost("claude-haiku-5-5", 2_000, 1_000));
    }

    [Fact]
    public void Haiku_costs_more_beyond_100k_input_tokens_in_one_request()
    {
        Assert.Equal(0.01m, Pricing.Cost("claude-haiku-5-5", 100_000, 0));
        Assert.Equal(0.0500005m, Pricing.Cost("claude-haiku-5-5", 100_001, 0));
    }

    [Fact]
    public void A_mission_succeeds_only_if_it_cites_everything_expected()
    {
        var question = new EvalCase("q", "?", MissionKind.Question, ["GetTotals", ".Result"]);

        Assert.True(question.Grade(MissionStatus.Completed, "GetTotals appelle .Result", null).Success);
        Assert.Equal("réponse sans : .Result", question.Grade(MissionStatus.Completed, "GetTotals", null).Reason);
        Assert.False(question.Grade(MissionStatus.Failed, "GetTotals .Result", null).Success);
    }

    [Fact]
    public void A_review_also_needs_a_report_with_the_expected_findings()
    {
        var review = new EvalCase("r", "Revue", MissionKind.Review, ["N+1"], ["OrderRepository.cs"]);

        Assert.Equal("REPORT.md non écrit", review.Grade(MissionStatus.Completed, "N+1", null).Reason);
        Assert.False(review.Grade(MissionStatus.Completed, "N+1", "# Rapport").Success);
        Assert.True(review.Grade(MissionStatus.Completed, "N+1", "Voir OrderRepository.cs:9").Success);
    }

    [Fact]
    public void The_cases_file_is_valid()
    {
        var cases = EvalCase.Load();

        Assert.NotEmpty(cases);
        Assert.All(cases, c => Assert.NotEmpty(c.MustMention));
        Assert.Contains(cases, c => c.Kind == MissionKind.Review && c.ReportMustMention is { Count: > 0 });
    }
}

using Threadline.Core;

namespace Threadline.Core.Tests;

public sealed class ThreadlineAgentRoutingTests
{
    private readonly AgentIntentClassifier _classifier = new();

    [Theory]
    [InlineData("What does this dashboard show?")]
    [InlineData("Summarize the context I have open.")]
    [InlineData("Why would this query be slow?")]
    [InlineData("Explain the difference between a CTE and a temp table.")]
    public void Conversational_requests_stay_direct(string request)
    {
        var decision = _classifier.Decide(request);
        Assert.Equal(ThreadlineAgentRoute.Direct, decision.Route);
    }

    [Theory]
    [InlineData("Fix this code and run the tests.")]
    [InlineData("Research the vendor and write a report.")]
    [InlineData("Install the package, configure it, then test it.")]
    [InlineData("Commit this change and push the branch.")]
    [InlineData("Mission: inspect the repository and implement the fix.")]
    public void Operational_requests_become_missions(string request)
    {
        var decision = _classifier.Decide(request);
        Assert.Equal(ThreadlineAgentRoute.Mission, decision.Route);
    }

    [Fact]
    public void Explicit_direct_override_wins()
    {
        var decision = _classifier.Decide(
            "Fix this code and run the tests.",
            ThreadlineAgentRoutePreference.Direct);

        Assert.Equal(ThreadlineAgentRoute.Direct, decision.Route);
        Assert.Contains("explicitly", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Explicit_mission_override_wins()
    {
        var decision = _classifier.Decide(
            "What does this dashboard show?",
            ThreadlineAgentRoutePreference.Mission);

        Assert.Equal(ThreadlineAgentRoute.Mission, decision.Route);
        Assert.Contains("explicitly", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }
}

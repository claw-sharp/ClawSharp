using ClawSharp.Core;

namespace ClawSharp.UnitTests;

public sealed class AgentRoutingResolverTests
{
    [Fact]
    public void ResolveRoutedModel_Prefers_Agent_Name_Then_Subagent_Type_Then_Default()
    {
        var settings = new ClawSharpSettings
        {
            AgentModels = new Dictionary<string, AgentModelConnection>(StringComparer.Ordinal)
            {
                ["gpt-4o"] = new() { BaseUrl = "https://api.openai.com/v1", ApiKey = "sk-openai" },
                ["deepseek-chat"] = new() { BaseUrl = "https://api.deepseek.com/v1", ApiKey = "sk-deepseek" }
            },
            AgentRouting = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["default"] = "gpt-4o",
                ["Explore"] = "deepseek-chat",
                ["team_lead"] = "gpt-4o"
            }
        };

        Assert.Equal("gpt-4o", AgentRoutingResolver.ResolveRoutedModel(settings, "team-lead", "Explore"));
        Assert.Equal("deepseek-chat", AgentRoutingResolver.ResolveRoutedModel(settings, null, "Explore"));
        Assert.Equal("gpt-4o", AgentRoutingResolver.ResolveRoutedModel(settings, null, "UnknownAgent"));
    }
}

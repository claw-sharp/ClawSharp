// TS origin: meetsAvailabilityRequirement in commands.ts
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

/// <summary>
/// Service to determine command availability based on current auth and env state.
/// TS origin: meetsAvailabilityRequirement in commands.ts
/// </summary>
public sealed class CommandAvailabilityService(IAuthService _authService)
    : ICommandAvailabilityService
{
    public bool IsAvailable(CommandDescriptor descriptor)
    {
        var availability = descriptor.Availability;
        if (availability is null || availability.Count == 0)
        {
            return true;
        }

        foreach (var a in availability)
        {
            switch (a)
            {
                case CommandAvailability.Universal:
                    return true;

                case CommandAvailability.ClaudeAi:
                    if (_authService.IsClaudeAISubscriber())
                    {
                        return true;
                    }
                    break;

                case CommandAvailability.Console:
                    // Console API key user = direct 1P API customer (not 3P, not claude.ai).
                    // Excludes 3rd-party providers (Bedrock/Vertex/Foundry) who don't set ANTHROPIC_BASE_URL
                    // and gateway users who proxy through a custom base URL.
                    if (!_authService.IsClaudeAISubscriber() &&
                        !_authService.IsUsing3PServices() &&
                        _authService.IsFirstPartyAnthropicBaseUrl())
                    {
                        return true;
                    }
                    break;
            }
        }

        return false;
    }
}

namespace ClawSharp.Core;

public sealed record AutoModeGateState(
    bool IsAutoModeAvailable,
    bool UseAutoModeDuringPlan);

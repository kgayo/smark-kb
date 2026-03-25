namespace SmartKb.Contracts.Models;

/// <summary>
/// Response type constants for chat orchestration structured output.
/// </summary>
public static class ChatResponseType
{
    public const string FinalAnswer = "final_answer";
    public const string NextStepsOnly = "next_steps_only";
    public const string Escalate = "escalate";

    /// <summary>All valid response type values.</summary>
    public static readonly string[] AllValues = [FinalAnswer, NextStepsOnly, Escalate];
}

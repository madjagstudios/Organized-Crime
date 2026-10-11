namespace OrganizedCrime.Model;

public sealed record Chapter3DesiredMessage(string CorrelationId, string Text);

public sealed record Chapter3DecisionOption(string Label, Chapter3PresentationCommand Command);

/// <summary>
/// Nell's one reply decision: the subject text as <see cref="Prompt"/>, the
/// correlation of the scope that text belongs to (null when no scope fits the
/// buttons), and the short <see cref="RepromptText"/> for re-showing buttons
/// whose subject she already delivered.
/// </summary>
public sealed record Chapter3DesiredDecision(
    string Id,
    string Prompt,
    IReadOnlyList<Chapter3DecisionOption> Options,
    string? PromptCorrelationId,
    string RepromptText);

public sealed record Chapter3PresentationPlan(
    IReadOnlyList<Chapter3DesiredMessage> Messages,
    Chapter3DesiredDecision? Decision,
    Release1DesiredQuest? Quest = null)
{
    public static Chapter3PresentationPlan Empty =>
        new(Array.Empty<Chapter3DesiredMessage>(), null);
}

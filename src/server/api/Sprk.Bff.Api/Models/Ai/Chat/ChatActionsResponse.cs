namespace Sprk.Bff.Api.Models.Ai.Chat;

/// <summary>
/// Response body for GET /api/ai/chat/actions.
///
/// Returns available actions grouped by <see cref="ActionCategory"/> for the SprkChat
/// action menu. Actions are filtered based on the active playbook's declared capabilities.
/// </summary>
/// <param name="Actions">Flat list of available actions, each tagged with its category.</param>
/// <param name="Categories">Ordered list of categories that have at least one action.</param>
public sealed record ChatActionsResponse(
    ChatAction[] Actions,
    ActionCategory[] Categories);

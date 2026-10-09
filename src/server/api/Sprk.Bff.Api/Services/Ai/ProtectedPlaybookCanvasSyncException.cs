namespace Sprk.Bff.Api.Services.Ai;

/// <summary>Why a playbook's nodes must not be replaced from a canvas layout.</summary>
public enum ProtectedPlaybookReason
{
    /// <summary><c>sprk_issystemplaybook</c> is true.</summary>
    SystemFlag,

    /// <summary><c>sprk_playbooktype</c> is 2 (Notification); the system flag may be null on these rows.</summary>
    NotificationType,

    /// <summary>At least one node has no <c>__canvasNodeId</c>: the node was written by a repo deploy script, not the Designer.</summary>
    RepoDeployedNodes,

    /// <summary>The playbook or its nodes could not be read, so protection cannot be ruled out (fail closed).</summary>
    Unverifiable
}

/// <summary>
/// Thrown by <see cref="INodeService"/> when a canvas-to-node sync would replace the nodes of a
/// repo-deployed system playbook (D-97 / PB-08). The sync deletes every node without
/// <c>__canvasNodeId</c> and recreates stubs, which destroys such a playbook.
/// </summary>
public sealed class ProtectedPlaybookCanvasSyncException : InvalidOperationException
{
    public ProtectedPlaybookCanvasSyncException(Guid playbookId, string? playbookName, ProtectedPlaybookReason reason)
        : base(BuildMessage(playbookId, playbookName, reason))
    {
        PlaybookId = playbookId;
        PlaybookName = playbookName;
        Reason = reason;
    }

    public Guid PlaybookId { get; }
    public string? PlaybookName { get; }
    public ProtectedPlaybookReason Reason { get; }

    private static string BuildMessage(Guid playbookId, string? playbookName, ProtectedPlaybookReason reason)
    {
        var label = string.IsNullOrWhiteSpace(playbookName) ? playbookId.ToString() : $"'{playbookName}' ({playbookId})";
        return reason switch
        {
            ProtectedPlaybookReason.SystemFlag =>
                $"Playbook {label} is a system playbook deployed from the repository and is read-only in the Playbook Designer. Change it in the repo and redeploy.",
            ProtectedPlaybookReason.NotificationType =>
                $"Playbook {label} is a notification playbook deployed from the repository and is read-only in the Playbook Designer. Change it in the repo and redeploy.",
            ProtectedPlaybookReason.RepoDeployedNodes =>
                $"Playbook {label} has nodes deployed from the repository (no Designer canvas id) and is read-only in the Playbook Designer. Change it in the repo and redeploy.",
            _ =>
                $"Playbook {label} could not be verified as safe to overwrite (its record or nodes could not be read), so the canvas save was refused. Retry later."
        };
    }
}

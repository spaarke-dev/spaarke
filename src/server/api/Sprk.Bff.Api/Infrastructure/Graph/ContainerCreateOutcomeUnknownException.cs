namespace Sprk.Bff.Api.Infrastructure.Graph;

/// <summary>
/// The SPE container create POST was sent but did not come back with an answer that settles whether Graph created the
/// container: it timed out, the connection failed, Graph answered with a server error (5xx or 408) (#1755), or Graph
/// answered with success but named no container (#1755 round 2). A container may exist that nothing names. A
/// <see cref="ContainerLeftActiveException"/> names the container it left active (a step after the create failed and
/// so did its compensating delete — #1755 round 3). Any other failure of
/// <see cref="ContainerOperations.CreateContainerAsync"/> means no container was left active.
/// </summary>
public sealed class ContainerCreateOutcomeUnknownException : Exception
{
    public ContainerCreateOutcomeUnknownException(string displayName, Exception innerException)
        : base($"The create of SPE container '{displayName}' was sent, but its outcome is unknown: a container may have " +
               "been created.", innerException)
    {
        DisplayName = displayName;
    }

    /// <summary>The display name the possible container was created with: the operator's way to find it.</summary>
    public string DisplayName { get; }
}

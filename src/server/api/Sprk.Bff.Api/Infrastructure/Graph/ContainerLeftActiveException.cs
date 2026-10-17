namespace Sprk.Bff.Api.Infrastructure.Graph;

/// <summary>
/// The SPE container was created and its id is known, but a step after the create failed — the business-unit bind or
/// the ownership marker — and the compensating delete failed too (#1755 round 3). The container was not removed and
/// nothing names it: no record, and no admin route until it is bound and marked. (When the activate itself failed, Graph
/// may still remove the inactive container after 24 hours; nothing here relies on that.) Unlike
/// <see cref="ContainerCreateOutcomeUnknownException"/>, which may or may not have left a container, this one names it.
/// </summary>
/// <remarks>
/// An <see cref="InvalidOperationException"/>, as the plain one it replaces was, with the same message: a caller that
/// catches that type (the SPE admin create, which shares <see cref="SpeContainerOwnershipGuard.MarkOwnedAsync"/>) still
/// catches it. A caller that reports the failure catches this type to say a container exists and give its id.
/// </remarks>
public sealed class ContainerLeftActiveException : InvalidOperationException
{
    public ContainerLeftActiveException(string containerId, string message, Exception innerException)
        : base(message, innerException)
    {
        ContainerId = containerId;
    }

    /// <summary>The container that was created and could not be removed: the operator's way to find it.</summary>
    public string ContainerId { get; }
}

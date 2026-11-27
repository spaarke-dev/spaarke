namespace Sprk.Bff.Api.Infrastructure.Dataverse;

/// <summary>
/// ISS-010 part C: a decorator over the caller's own <see cref="IDataverseUserClient"/> that only adds or answers reads (the decision
/// commit's access-window view). <see cref="CallerMetadataReadMemo.For"/> shares the request's table-metadata store with such a view
/// ONLY when <see cref="Inner"/> is the <see cref="DataverseUserClient"/> itself, so a view over any other client (an app-only or
/// impersonating one) keeps the per-create memo and its answers are never kept under the caller's key.
/// </summary>
internal interface ICallerClientView
{
    /// <summary>The client this view wraps.</summary>
    IDataverseUserClient Inner { get; }
}

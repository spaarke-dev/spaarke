namespace Sprk.Bff.Api.Infrastructure.Dataverse;

/// <summary>
/// The secure record that owns a SharePoint Embedded container, as returned by
/// <see cref="RecordContainerResolver.ResolveOwningRecordAsync"/>.
///
/// <para>This is the authorization SUBJECT for the container-keyed routes — task 073
/// (<c>PUT /api/containers/{containerId}/files/{*path}</c>) and task 078
/// (<c>GET /api/v1/containers/{containerId}/documents</c>). Those routes take a container id off the route
/// and had no record to authorize against; this is that record.</para>
///
/// <para><b>Note there is no <c>IRecordContainerResolver</c>.</b> ADR-010 requires services to be concrete
/// unless a seam is genuinely required, and the ArchTests 1:1-interface ratchet
/// (<c>ADR010_DITests.knownOneToOneCeiling</c>) is at its audited ceiling — a 1:1 interface here would
/// consume the last of its headroom and make the next interface added anywhere in the BFF assembly fail the
/// build blaming an unrelated project. No seam is needed: the tests substitute the resolver's
/// DEPENDENCIES (<see cref="ISecurableEntityRegistry"/>, <c>IGenericEntityService</c>) and exercise the real
/// decision logic, which is higher-fidelity than mocking the decision itself. Consumers should depend on the
/// concrete <see cref="RecordContainerResolver"/> and do the same.</para>
/// </summary>
/// <param name="EntityLogicalName">The owning record's entity logical name.</param>
/// <param name="RecordId">The owning record's id.</param>
public sealed record OwningSecureRecord(string EntityLogicalName, Guid RecordId);

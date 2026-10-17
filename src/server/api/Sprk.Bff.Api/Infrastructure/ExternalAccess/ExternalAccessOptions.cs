// spaarke-SPA-external-access-platform-r3 task 011 — the per-customer external-access switches (spec FR-06, FR-19;
// ADR-028 Amendment A8, owner-approved 2026-10-10).
//
// Owner decision 2026-10-10: Legal Front Door intake is workforce-only by default; a per-customer setting lets CIAM
// partners submit and read their OWN service requests. Default OFF — with it off, a CIAM caller's behaviour is exactly
// what it was before this class existed (no legal-front-door module, 0 service-request rows).
//
// Bound from the existing "ExternalAccess" section, which already carries PortalUrl, Reconciliation:WritesEnabled and
// AssignedAccess:JobRevokeOnChangeEnabled (read by key elsewhere); this class binds only the keys it declares.

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// Options bound from <c>ExternalAccess</c>: per-customer switches for the external-access module host.
/// </summary>
public sealed class ExternalAccessOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "ExternalAccess";

    /// <summary>
    /// When <c>true</c>, CIAM partner contacts may use the Service Requests module for their OWN requests (spec FR-06):
    /// the <c>service-requests</c> module's accessible set includes the CIAM contact's own requests
    /// (<c>sprk_requestedby</c> = the caller's contact), and a CIAM caller's default Tier-1 modules add
    /// <c>legal-front-door</c> (FR-19). Default <c>false</c> — absent means off. App Service form:
    /// <c>ExternalAccess__PartnerServiceRequestsEnabled</c>.
    /// </summary>
    public bool PartnerServiceRequestsEnabled { get; set; }

    /// <summary>
    /// When <c>true</c>, a CIAM partner may ALSO submit the Invention Submission intake type (owner decision 2026-10-12:
    /// Invention Submission is available to workforce users - staff and workforce contacts - and to CIAM partners only
    /// behind this flag). Default <c>false</c> - absent means off. It narrows nothing for workforce callers, and it is
    /// INERT while <see cref="PartnerServiceRequestsEnabled"/> is off (a partner is then not entitled to the Legal Front
    /// Door at all). Read through <see cref="ModuleEntitlementResolver.IsIntakeTypeAvailable"/> by the submit route and
    /// <c>/me/entitlements</c>, so the two cannot drift. App Service form:
    /// <c>ExternalAccess__PartnerInventionIntakeEnabled</c>. Bound and validated with the options above: a non-boolean
    /// value, including empty, fails startup.
    /// </summary>
    public bool PartnerInventionIntakeEnabled { get; set; }

    /// <summary>
    /// ADR-028 A9 (r3 task 047): the business unit whose DEFAULT team owns every contact the registration route
    /// (<c>POST /api/v1/external/registration</c>) creates. Absent means no registration create: the route answers 500
    /// <c>registration_owner_unconfigured</c> and creates nothing (binding an existing contact needs no owner). App Service
    /// form: <c>ExternalAccess__CustomerBusinessUnitId</c>. A value that is not a GUID fails startup.
    /// </summary>
    public Guid? CustomerBusinessUnitId { get; set; }

    /// <summary>
    /// FR-20 (unified-access-control-r2 task 036): whether Dataverse decides an internal user's record access on the
    /// SPA/Teams plane. See <see cref="ImpersonatedRootSetsOptions"/>.
    /// </summary>
    public ImpersonatedRootSetsOptions ImpersonatedRootSets { get; set; } = new();
}

/// <summary>
/// Options bound from <c>ExternalAccess:ImpersonatedRootSets</c> (FR-20, unified-access-control-r2 task 036; ADR-034
/// Amendment A2, accepted by owner round 75).
/// </summary>
/// <remarks>
/// <para><b>Off (the default; absent means off).</b> A systemuser's accessible projects, matters and work assignments come
/// from the ADR-034 membership walk, each at the membership term's Read|Write|Create, exactly as before this option
/// existed. Transitional only: it keeps the membership walk's business-unit over-grant.</para>
/// <para><b>On.</b> For the systemuser plane, Dataverse's own answer replaces the membership rules for ACCESS: the root ids
/// come from an impersonated read as the caller (<see cref="IImpersonatedRootSetSource"/>) and confer Read only; every write
/// route on <c>/api/v1/external</c> confirms the write against the caller's own rights on the target record
/// (<see cref="CallerRecordAccessProbe"/>). The contact-grants union, the deny list and the Restricted veto apply in both
/// states. A failed impersonated read fails the composition (the request is refused); it never falls back to membership.
/// </para>
/// <para>Do not turn it on in an environment until tasks 142 and 143 are deployed there and the task-034 canary has passed
/// with a strictly smaller set (owner round 59). App Service form: <c>ExternalAccess__ImpersonatedRootSets__Enabled</c>. A
/// value that is not a boolean, including empty, fails startup.</para>
/// </remarks>
public sealed class ImpersonatedRootSetsOptions
{
    /// <summary>When <c>true</c>, Dataverse decides a systemuser's access on this plane. Default <c>false</c>.</summary>
    public bool Enabled { get; set; }
}

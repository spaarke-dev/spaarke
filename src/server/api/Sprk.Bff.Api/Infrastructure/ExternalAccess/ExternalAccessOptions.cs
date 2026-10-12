// spaarke-SPA-external-access-platform-r3 task 011 — the per-customer external-access switches (spec FR-06, FR-19;
// ADR-028 Amendment A7, owner-approved 2026-10-10).
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
}

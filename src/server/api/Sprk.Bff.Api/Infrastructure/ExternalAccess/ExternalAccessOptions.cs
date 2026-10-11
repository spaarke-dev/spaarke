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
}

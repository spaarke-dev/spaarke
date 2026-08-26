using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Bounds on the sharing links minted by <c>POST /api/documents/{documentId}/share-link</c>
/// (unified-access-control-r2 task 072, spec FR-01 · Wave 1).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this type exists.</b> The route previously called <c>CreateSharingLinkAsUserAsync(linkType:
/// "view", scope: "anonymous", expiration: null)</c> — a NON-EXPIRING, anyone-with-the-URL credential,
/// minted with no per-document authorization. A minted SPE URL is not revocable through Dataverse: once
/// it exists, removing the caller's access to the document does not invalidate it. So the link's LIFETIME
/// is the only revocation mechanism this route has, which makes it a security bound rather than a
/// preference — and that is why the ceilings below are <see cref="RangeAttribute"/>-validated instead of
/// merely defaulted. An operator can shorten a lifetime; an operator cannot configure an unbounded one.
/// </para>
/// <para>
/// <b>Why anonymous links still exist.</b> They are the reason the feature was built
/// (<c>email-communication-solution-r5</c> R2 item 12): the email composer's "Link" attachments must open
/// for recipients OUTSIDE the tenant, and an <c>organization</c>-scoped link cannot do that. Task 072
/// therefore did NOT delete the capability — it removed it as the silent DEFAULT. Anonymous is now an
/// explicit per-call request, authorized against <c>AccessRights.Share</c> on the document, capped by
/// <see cref="AnonymousMaxLifetimeDays"/> (shorter than the organization cap), and logged at Warning with
/// the caller's identity. <see cref="AnonymousLinksEnabled"/> is the tenant-wide off switch.
/// </para>
/// <para>
/// <b>Why <see cref="AnonymousLinksEnabled"/> defaults to <c>true</c>.</b> Defaulting it off would
/// silently break external recipients on the next deploy until an operator noticed — a support incident
/// caused by a security task, which task 072's escalation trigger explicitly warns against. The safety
/// gained by this task comes from the per-document <c>share</c> gate and the lifetime cap, both of which
/// apply unconditionally; the switch is here so a tenant with a no-external-sharing policy can enforce it
/// in Spaarke rather than relying on the SharePoint tenant setting (which is a separate control and can
/// be changed by a different admin).
/// </para>
/// </remarks>
public class ShareLinkOptions
{
    public const string SectionName = "Documents:ShareLinks";

    /// <summary>
    /// Lifetime of an <c>organization</c>-scoped link, in days (default 14).
    /// </summary>
    /// <remarks>
    /// Ceiling of 90 is deliberate and structural: a minted SPE URL survives Dataverse revocation, so an
    /// operator must not be able to configure a link that effectively never expires. Shortening is always
    /// allowed.
    /// </remarks>
    [Range(1, 90, ErrorMessage = "Documents:ShareLinks:MaxLifetimeDays must be between 1 and 90 days — a "
        + "minted SPE link survives Dataverse revocation, so its lifetime is the only revocation this "
        + "route has.")]
    public int MaxLifetimeDays { get; set; } = 14;

    /// <summary>
    /// Lifetime of an <c>anonymous</c> (anyone-with-the-link) link, in days (default 7).
    /// </summary>
    /// <remarks>
    /// Capped harder than <see cref="MaxLifetimeDays"/> because the audience is unbounded and
    /// unauthenticated: nobody can enumerate who holds the URL, so time is the only containment. Seven
    /// days comfortably covers the emailed-link use case this exists for.
    /// </remarks>
    [Range(1, 30, ErrorMessage = "Documents:ShareLinks:AnonymousMaxLifetimeDays must be between 1 and 30 "
        + "days. An anonymous link's audience is unbounded, so time is the only containment.")]
    public int AnonymousMaxLifetimeDays { get; set; } = 7;

    /// <summary>
    /// Tenant-wide switch for anonymous links (default <c>true</c>). When <c>false</c>, a request that
    /// asks for external reach is refused (403) rather than silently downgraded to an
    /// <c>organization</c>-scoped link the external recipient could not open.
    /// </summary>
    /// <remarks>
    /// Refusing rather than downgrading is the deliberate choice: a silent downgrade produces a link that
    /// looks fine to the sender and is dead on arrival for the recipient, which is the failure mode that
    /// is hardest to diagnose from a support ticket.
    /// </remarks>
    public bool AnonymousLinksEnabled { get; set; } = true;
}

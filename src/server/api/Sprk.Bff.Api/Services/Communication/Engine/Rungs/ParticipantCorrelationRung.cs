using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Engine.Rungs;

/// <summary>
/// Rung 2 — participant correlation (medium-confidence deterministic). Who is on the message tells us
/// which matter/project/organization it relates to:
/// <list type="bullet">
///   <item>the SENDER's email → a contact (regarding person) and that contact's record memberships
///   (matter contact / project team / counsel) via the <c>sprk_userentityassociation</c> junction;</item>
///   <item>each RECIPIENT's (to/cc) email → their memberships (a recipient who is on Matter X is a
///   signal the message relates to Matter X);</item>
///   <item>the SENDER's email DOMAIN → <c>sprk_organization</c> (legal entity) and/or OOB
///   <c>account</c> (vendor/payment) — DISTINCT targets, each to its own lookup (DEC-3/FR-04).</item>
/// </list>
/// Emits <see cref="RungMatch"/> in the 0.60–0.85 confidence band with provenance citing the
/// participant + rule. Runs symmetrically inbound + outbound (reads only the envelope). Task 015 maps
/// confidence → status (rung-2 matches land in the <c>Suggested</c> tier unless reinforced).
/// </summary>
/// <remarks>
/// The org target is <c>sprk_organization</c> via <c>sprk_regardingorganization</c> on every path —
/// NO path writes <c>account</c> into the org association (the account association is a separate,
/// intentional lookup). Membership resolution depends on the R3 membership junction being populated;
/// when empty the rung gracefully yields only contact/org/account matches.
/// </remarks>
public sealed class ParticipantCorrelationRung : IAssociationRung
{
    private readonly ICommunicationDataverseService _communicationService;

    private const double PersonConfidence = 0.70;
    private const double SenderMembershipConfidence = 0.80;
    private const double RecipientMembershipConfidence = 0.70;
    private const double DomainConfidence = 0.65;

    /// <summary>
    /// Cap on recipients correlated per message. Bounds the serial Dataverse round-trips on the
    /// capture path (a large distribution list would otherwise issue ~2 queries per recipient). The
    /// sender is always processed; recipients beyond this cap are skipped — membership signals are
    /// additive, so tail recipients rarely change the outcome.
    /// </summary>
    private const int MaxRecipientsForCorrelation = 25;

    /// <summary>Common email providers to skip for domain-based organization/account matching.</summary>
    private static readonly HashSet<string> CommonEmailProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        "gmail.com", "outlook.com", "hotmail.com", "yahoo.com",
        "live.com", "msn.com", "icloud.com", "aol.com",
        "protonmail.com", "mail.com", "ymail.com", "googlemail.com"
    };

    public ParticipantCorrelationRung(ICommunicationDataverseService communicationService)
    {
        _communicationService = communicationService;
    }

    public RungKind Kind => RungKind.ParticipantCorrelation;
    public int Order => 2;

    public async Task<IReadOnlyList<RungMatch>> EvaluateAsync(
        NormalizedMessage message, AssociationContext context, CancellationToken ct)
    {
        // Dedup matches by (regarding field, target id); keep the highest confidence seen.
        var best = new Dictionary<(string Field, Guid Id), RungMatch>();

        void Offer(RungMatch match)
        {
            var key = (match.RegardingFieldName, match.Target.Id);
            if (!best.TryGetValue(key, out var existing) || match.Confidence > existing.Confidence)
                best[key] = match;
        }

        var sender = message.From;

        // ── Sender: person + memberships + domain org/account ─────────────────────
        if (!string.IsNullOrWhiteSpace(sender))
        {
            var contact = await _communicationService.QueryContactByEmailAsync(sender, ct);
            if (contact is not null)
            {
                Offer(new RungMatch
                {
                    RegardingFieldName = "sprk_regardingperson",
                    Target = new EntityReference("contact", contact.Id),
                    Confidence = PersonConfidence,
                    Provenance = $"participant:sender:{sender}→contact",
                    Rung = RungKind.ParticipantCorrelation,
                });

                foreach (var m in await ResolveMembershipsAsync(contact.Id, sender, "sender", SenderMembershipConfidence, ct))
                    Offer(m);
            }

            foreach (var m in await ResolveSenderDomainAsync(sender, ct))
                Offer(m);
        }

        // ── Recipients (to + cc): memberships only ────────────────────────────────
        var recipients = message.To.Concat(message.Cc)
            .Where(r => !string.IsNullOrWhiteSpace(r) && !string.Equals(r, sender, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxRecipientsForCorrelation);

        foreach (var recipient in recipients)
        {
            var contact = await _communicationService.QueryContactByEmailAsync(recipient, ct);
            if (contact is null)
                continue;

            foreach (var m in await ResolveMembershipsAsync(contact.Id, recipient, "recipient", RecipientMembershipConfidence, ct))
                Offer(m);
        }

        return best.Values.ToArray();
    }

    /// <summary>
    /// Resolves a contact's record memberships from the junction and maps each to a regarding match.
    /// </summary>
    private async Task<IEnumerable<RungMatch>> ResolveMembershipsAsync(
        Guid contactId, string email, string role, double confidence, CancellationToken ct)
    {
        var memberships = await _communicationService.QueryContactMembershipsAsync(contactId, ct);
        var matches = new List<RungMatch>();
        if (memberships is null)
            return matches;

        foreach (var membership in memberships)
        {
            var entityName = membership.GetAttributeValue<string>("sprk_entitylogicalname");
            var recordId = membership.GetAttributeValue<string>("sprk_entityrecordid");
            var memberRole = membership.GetAttributeValue<string>("sprk_role");

            if (string.IsNullOrWhiteSpace(entityName) || !Guid.TryParse(recordId, out var targetId))
                continue;

            var field = RegardingFieldMap.FieldFor(entityName);
            if (field is null)
                continue;

            matches.Add(new RungMatch
            {
                RegardingFieldName = field,
                Target = new EntityReference(entityName, targetId),
                Confidence = confidence,
                Provenance = $"participant:{role}:{email}→member:{entityName}"
                             + (string.IsNullOrWhiteSpace(memberRole) ? "" : $":{memberRole}"),
                Rung = RungKind.ParticipantCorrelation,
            });
        }

        return matches;
    }

    /// <summary>
    /// Resolves the sender domain to sprk_organization AND/OR account, each to its OWN lookup (DEC-3/FR-04).
    /// </summary>
    private async Task<IEnumerable<RungMatch>> ResolveSenderDomainAsync(string senderEmail, CancellationToken ct)
    {
        var domain = ExtractDomain(senderEmail);
        if (string.IsNullOrEmpty(domain) || CommonEmailProviders.Contains(domain))
            return Array.Empty<RungMatch>();

        var matches = new List<RungMatch>();

        var organization = await _communicationService.QueryOrganizationByDomainAsync(domain, ct);
        if (organization is not null)
        {
            matches.Add(new RungMatch
            {
                RegardingFieldName = "sprk_regardingorganization",
                Target = new EntityReference("sprk_organization", organization.Id),
                Confidence = DomainConfidence,
                Provenance = $"participant:sender-domain:{domain}→organization",
                Rung = RungKind.ParticipantCorrelation,
            });
        }

        var account = await _communicationService.QueryAccountByDomainAsync(domain, ct);
        if (account is not null)
        {
            matches.Add(new RungMatch
            {
                RegardingFieldName = "sprk_regardingaccount",
                Target = new EntityReference("account", account.Id),
                Confidence = DomainConfidence,
                Provenance = $"participant:sender-domain:{domain}→account",
                Rung = RungKind.ParticipantCorrelation,
            });
        }

        return matches;
    }

    private static string? ExtractDomain(string email)
    {
        var atIndex = email.IndexOf('@');
        return atIndex > 0 ? email[(atIndex + 1)..].ToLowerInvariant() : null;
    }
}

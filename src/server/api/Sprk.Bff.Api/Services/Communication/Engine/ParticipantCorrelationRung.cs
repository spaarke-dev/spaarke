using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Engine;

/// <summary>
/// Rung 2 — participant correlation. Matches the sender to a contact (by email) and the sender domain
/// to an organization (legal entity) and/or an account (vendor/payment), each written to its OWN
/// regarding lookup.
/// </summary>
/// <remarks>
/// Task-011 adapter wrapping the current <c>TryResolveBySenderAsync</c> logic, envelope-only (reads
/// <see cref="NormalizedMessage.From"/>). <c>sprk_organization</c> and OOB <c>account</c> are DISTINCT
/// targets — never mixed (task-004 fix). Common email providers are skipped for domain matching.
/// Task 013 replaces this adapter with the real rung 2 (adds recipient-side correlation + direction
/// awareness).
/// </remarks>
public sealed class ParticipantCorrelationRung : IAssociationRung
{
    private readonly ICommunicationDataverseService _communicationService;

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
        var senderEmail = message.From;
        if (string.IsNullOrEmpty(senderEmail))
            return Array.Empty<RungMatch>();

        var matches = new List<RungMatch>();

        // Contact match by email → sprk_regardingperson.
        var contact = await _communicationService.QueryContactByEmailAsync(senderEmail, ct);
        if (contact is not null)
        {
            matches.Add(new RungMatch
            {
                RegardingFieldName = "sprk_regardingperson",
                Target = new EntityReference("contact", contact.Id),
                Confidence = 1.0,
                Provenance = "sender:email→contact",
                Rung = RungKind.ParticipantCorrelation,
            });
        }

        // Organization + account by sender domain (skip common providers). sprk_organization (legal
        // entity) and OOB account (vendor/payment) each go to their OWN lookup, never mixed (task 004).
        var domain = ExtractDomain(senderEmail);
        if (!string.IsNullOrEmpty(domain) && !CommonEmailProviders.Contains(domain))
        {
            var organization = await _communicationService.QueryOrganizationByDomainAsync(domain, ct);
            if (organization is not null)
            {
                matches.Add(new RungMatch
                {
                    RegardingFieldName = "sprk_regardingorganization",
                    Target = new EntityReference("sprk_organization", organization.Id),
                    Confidence = 1.0,
                    Provenance = "sender:domain→organization",
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
                    Confidence = 1.0,
                    Provenance = "sender:domain→account",
                    Rung = RungKind.ParticipantCorrelation,
                });
            }
        }

        return matches;
    }

    private static string? ExtractDomain(string email)
    {
        var atIndex = email.IndexOf('@');
        return atIndex > 0 ? email[(atIndex + 1)..].ToLowerInvariant() : null;
    }
}

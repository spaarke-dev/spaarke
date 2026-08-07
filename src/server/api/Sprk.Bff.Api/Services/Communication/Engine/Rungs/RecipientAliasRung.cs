using System.Text.RegularExpressions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Engine.Rungs;

/// <summary>
/// Rung 0 (tier) — recipient-alias (FR-A2). Parses every recipient field (To/Cc/<b>Bcc</b>) for a
/// per-record intake address — the <c>matter-{ref}@</c> scheme — and resolves each to its
/// <c>sprk_matter</c>, emitting an explicit-reference-tier <see cref="RungMatch"/>
/// (<see cref="RungKind.RecipientAlias"/>, confidence 1.0, auto-file-eligible per
/// <see cref="AssociationStatusMapper"/>).
/// </summary>
/// <remarks>
/// <para>
/// A per-record alias is a deliberate, unambiguous routing instruction, so it is as authoritative as a
/// caller-supplied regarding — external systems (client billing, e-filing) file to a matter by addressing
/// <c>matter-12345@</c>. Delivery is via a targeted Exchange mail-flow rule, most commonly by <b>Bcc</b>,
/// so the rung reads the envelope's <see cref="NormalizedMessage.Bcc"/> (mapped once at the Graph boundary,
/// never a second Graph round-trip — ADR-045) and Bcc-only delivery MUST associate deterministically.
/// </para>
/// <para>
/// Best-effort/non-fatal (NFR-04): a throw is treated by the engine as a non-match. Multiple DISTINCT
/// matter aliases across the recipients emit multiple matches — the mapper then surfaces the conflict as
/// <c>Ambiguous</c> (never guesses); the same matter aliased in several fields collapses to one match.
/// </para>
/// <para>
/// MVP scope is the <c>matter-{ref}@</c> scheme only. Extending to other core types
/// (<c>project-{ref}@</c>, <c>invoice-{ref}@</c>, …) needs a record-type→address-scheme catalog that no
/// config supplies today; per the task escalation contract that is a deliberate follow-up, not a
/// hardcoded per-tenant convention.
/// </para>
/// </remarks>
public sealed class RecipientAliasRung : IAssociationRung
{
    private readonly ICommunicationDataverseService _communicationService;

    /// <summary>
    /// A per-record matter intake address: local part <c>matter-{ref}</c> immediately before the <c>@</c>.
    /// The reference token is everything between <c>matter-</c> and <c>@</c> (≥1 non-<c>@</c>/whitespace
    /// char), so <c>matter-@…</c> (empty ref) and <c>notmatter-1@…</c> (no <c>matter-</c> prefix) do NOT
    /// match. Case-insensitive; the domain is irrelevant (any intake domain the mail-flow rule targets).
    /// </summary>
    private static readonly Regex MatterAliasPattern =
        new(@"^matter-(?<ref>[^@\s]+)@", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    public RecipientAliasRung(ICommunicationDataverseService communicationService)
    {
        _communicationService = communicationService;
    }

    // A per-record alias IS an explicit reference (rung-0 tier); Order 0 co-runs it with the other
    // deterministic rung-0 signals (their matches reinforce in the mapper). Kind is distinct so the alias
    // signal is isolated + independently testable.
    public RungKind Kind => RungKind.RecipientAlias;
    public int Order => 0;

    public async Task<IReadOnlyList<RungMatch>> EvaluateAsync(
        NormalizedMessage message, AssociationContext context, CancellationToken ct)
    {
        // Distinct matter targets across ALL recipient fields → one match each (a matter aliased in both To
        // and Bcc collapses to one; two DIFFERENT matters surface as a conflict the mapper resolves).
        var byMatter = new Dictionary<Guid, RungMatch>();

        foreach (var recipient in message.To.Concat(message.Cc).Concat(message.Bcc))
        {
            if (string.IsNullOrWhiteSpace(recipient))
                continue;

            var referenceNumber = ExtractMatterReference(recipient);
            if (referenceNumber is null)
                continue;

            var matter = await ResolveMatterAsync(referenceNumber, ct);
            if (matter is null || byMatter.ContainsKey(matter.Id))
                continue;

            var field = RegardingFieldMap.FieldFor("sprk_matter");
            if (field is null)
                continue; // defensive — sprk_matter is always mapped

            byMatter[matter.Id] = new RungMatch
            {
                RegardingFieldName = field,
                Target = new EntityReference("sprk_matter", matter.Id),
                // A per-record intake address is a deliberate routing instruction — treated as certain,
                // matching a caller-supplied regarding. Clears the auto-file threshold for a core matter.
                Confidence = 1.0,
                Provenance = $"recipient-alias:matter-{referenceNumber}@→matter",
                Rung = RungKind.RecipientAlias,
            };
        }

        return byMatter.Values.ToArray();
    }

    /// <summary>
    /// Extracts the matter reference token from a <c>matter-{ref}@…</c> recipient address; null when the
    /// address is not a matter intake alias. Non-fatal on a pathological input (regex timeout → treat as
    /// non-match).
    /// </summary>
    private static string? ExtractMatterReference(string recipient)
    {
        try
        {
            var match = MatterAliasPattern.Match(recipient.Trim());
            if (!match.Success)
                return null;

            var reference = match.Groups["ref"].Value;
            return string.IsNullOrWhiteSpace(reference) ? null : reference;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves the alias reference to a matter. Mirrors <see cref="ExplicitReferenceRung"/>: tries the bare
    /// reference then the <c>MAT-{ref}</c> form, tolerating both storage conventions for the matter number.
    /// </summary>
    private async Task<Entity?> ResolveMatterAsync(string referenceNumber, CancellationToken ct) =>
        await _communicationService.QueryMatterByReferenceNumberAsync(referenceNumber, ct)
        ?? await _communicationService.QueryMatterByReferenceNumberAsync($"MAT-{referenceNumber}", ct);
}

using System.Text.RegularExpressions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Engine.Rungs;

/// <summary>
/// Rung 0 — explicit reference (highest determinism). Two authoritative sources:
/// <list type="number">
///   <item>a <b>caller-supplied regarding</b> (<see cref="AssociationContext.CallerSuppliedRegarding"/>)
///   — the outbound send request's associations or the add-in save-pane selection; the caller told us
///   exactly what this communication is about, so it is treated as certain across all supported targets; and</item>
///   <item>an <b>explicit identifier in the subject line</b> — a matter reference number resolved to a
///   <c>sprk_matter</c>.</item>
/// </list>
/// Runs symmetrically for inbound and outbound (it reads only the envelope + context). Emits
/// <see cref="RungMatch"/> with confidence + provenance; task 015 maps confidence → status.
/// </summary>
/// <remarks>
/// Scope note (task 012): subject-token RESOLUTION is implemented for matter (the target with a
/// reference-number convention + an existing query). Invoice/service-request subject tokens are not yet
/// detected or resolved — that needs regex patterns + dedicated query methods + confirmed number-field
/// names on <see cref="ICommunicationDataverseService"/> (follow-up). Caller-supplied regarding already
/// covers all supported targets, so target coverage holds via source (1).
/// </remarks>
public sealed class ExplicitReferenceRung : IAssociationRung
{
    private readonly ICommunicationDataverseService _communicationService;

    /// <summary>Regex patterns for extracting matter reference numbers from the subject.</summary>
    private static readonly Regex[] MatterReferencePatterns =
    [
        new(@"\bMAT-(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1)),
        new(@"\bMatter\s*#(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1)),
        new(@"\bSPRK-(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1)),
        new(@"\[MATTER:(\d+)\]", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1)),
    ];

    public ExplicitReferenceRung(ICommunicationDataverseService communicationService)
    {
        _communicationService = communicationService;
    }

    public RungKind Kind => RungKind.ExplicitReference;
    public int Order => 0;

    public async Task<IReadOnlyList<RungMatch>> EvaluateAsync(
        NormalizedMessage message, AssociationContext context, CancellationToken ct)
    {
        var matches = new List<RungMatch>();

        // Source 1: caller-supplied regarding — authoritative, across all 8 targets.
        foreach (var assoc in context.CallerSuppliedRegarding)
        {
            var field = RegardingFieldMap.FieldFor(assoc.EntityType);
            if (field is null)
                continue; // unmapped entity type — skip rather than write an unknown lookup

            matches.Add(new RungMatch
            {
                RegardingFieldName = field,
                Target = new EntityReference(assoc.EntityType, assoc.EntityId) { Name = assoc.EntityName },
                Confidence = 1.0,
                Provenance = $"explicit:caller-supplied:{assoc.EntityType}",
                Rung = RungKind.ExplicitReference,
            });
        }

        // A caller-supplied regarding is authoritative — do not second-guess it with subject parsing.
        if (matches.Count > 0)
            return matches;

        // Source 2: explicit matter reference in the subject line.
        var matterMatch = await TryResolveSubjectMatterAsync(message.Subject, ct);
        if (matterMatch is not null)
            matches.Add(matterMatch);

        return matches;
    }

    private async Task<RungMatch?> TryResolveSubjectMatterAsync(string? subject, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(subject))
            return null;

        foreach (var pattern in MatterReferencePatterns)
        {
            try
            {
                var match = pattern.Match(subject);
                if (!match.Success)
                    continue;

                var referenceNumber = match.Groups[1].Value;
                if (string.IsNullOrWhiteSpace(referenceNumber))
                    continue;

                var matter = await _communicationService.QueryMatterByReferenceNumberAsync(referenceNumber, ct)
                             ?? await _communicationService.QueryMatterByReferenceNumberAsync($"MAT-{referenceNumber}", ct);

                if (matter is not null)
                {
                    return new RungMatch
                    {
                        RegardingFieldName = "sprk_regardingmatter",
                        // A resolved subject token is strong but heuristic — below a caller-supplied
                        // regarding (1.0). Task 015's confidence→status ladder weights the sources.
                        Confidence = 0.9,
                        Target = new EntityReference("sprk_matter", matter.Id),
                        Provenance = $"explicit:subject-token→matter:{referenceNumber}",
                        Rung = RungKind.ExplicitReference,
                    };
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // Skip this pattern on timeout (defensive; preserves pre-011 per-pattern guard).
            }
        }

        return null;
    }
}

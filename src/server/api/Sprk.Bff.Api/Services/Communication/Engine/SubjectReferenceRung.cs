using System.Text.RegularExpressions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Engine;

/// <summary>
/// Explicit-reference rung (subject line) — extracts a matter reference number from the subject via
/// regex and resolves it to <c>sprk_matter</c>.
/// </summary>
/// <remarks>
/// Task-011 adapter wrapping the current <c>TryResolveBySubjectPatternAsync</c> logic, envelope-only
/// (reads <see cref="NormalizedMessage.Subject"/>). Runs LAST in the deterministic cascade
/// (<see cref="Order"/> = 3) to preserve the pre-011 precedence (thread → participant → subject),
/// even though its <see cref="RungKind.ExplicitReference"/> kind is numerically 0. Task 012/014
/// supply the real explicit-reference + structural rungs.
/// </remarks>
public sealed class SubjectReferenceRung : IAssociationRung
{
    private readonly ICommunicationDataverseService _communicationService;

    /// <summary>Regex patterns for extracting matter reference numbers from email subjects.</summary>
    private static readonly Regex[] MatterReferencePatterns =
    [
        new(@"\bMAT-(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1)),
        new(@"\bMatter\s*#(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1)),
        new(@"\bSPRK-(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1)),
        new(@"\[MATTER:(\d+)\]", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1)),
    ];

    public SubjectReferenceRung(ICommunicationDataverseService communicationService)
    {
        _communicationService = communicationService;
    }

    public RungKind Kind => RungKind.ExplicitReference;
    public int Order => 3;

    public async Task<IReadOnlyList<RungMatch>> EvaluateAsync(
        NormalizedMessage message, AssociationContext context, CancellationToken ct)
    {
        var subject = message.Subject;
        if (string.IsNullOrEmpty(subject))
            return Array.Empty<RungMatch>();

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

                // Try the raw number, then with the MAT- prefix (preserves pre-011 behavior).
                var matter = await _communicationService.QueryMatterByReferenceNumberAsync(referenceNumber, ct)
                             ?? await _communicationService.QueryMatterByReferenceNumberAsync($"MAT-{referenceNumber}", ct);

                if (matter is not null)
                {
                    return
                    [
                        new RungMatch
                        {
                            RegardingFieldName = "sprk_regardingmatter",
                            Target = new EntityReference("sprk_matter", matter.Id),
                            Confidence = 1.0,
                            Provenance = $"subject:pattern→matter:{referenceNumber}",
                            Rung = RungKind.ExplicitReference,
                        }
                    ];
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // Skip this pattern on timeout (preserves pre-011 per-pattern guard).
            }
        }

        return Array.Empty<RungMatch>();
    }
}

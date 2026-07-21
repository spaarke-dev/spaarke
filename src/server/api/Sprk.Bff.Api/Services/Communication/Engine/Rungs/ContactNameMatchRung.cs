using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Engine.Rungs;

/// <summary>
/// Rung 3.6 — <b>deterministic contact-name match</b> (email-r4 UAT R2 B1, owner spec 2026-07-20). Closes the
/// gap that let a contact NAMED in the subject/body ("Working on this file will be Eyal Iffergan and Sara Chen")
/// never surface: today contacts match only when they are the sender/recipient
/// (<see cref="ParticipantCorrelationRung"/> / rung 2).
/// </summary>
/// <remarks>
/// <para>
/// <b>Extract then verify.</b> The rung extracts candidate PERSON-NAME phrases (a run of ≥2 Title-Case tokens)
/// from the normalized envelope (subject + body + attachment text) with a bounded regex, then makes the decision
/// DETERMINISTICALLY: a phrase matches only if it EXACTLY equals the <c>fullname</c> of an existing
/// <c>contact</c> (resolved via <see cref="ICommunicationDataverseService.QueryContactsByFullNameAsync"/>).
/// Contacts are NOT in the <c>spaarke-records-index</c>, so this is a Dataverse query — not a RecordSearch call.
/// </para>
/// <para>
/// <b>Suggest-only, never auto-file.</b> Each matched contact is emitted as a <c>sprk_regardingperson</c> match
/// at a Suggested-band confidence (below the 0.85 auto-file threshold), tiered by location
/// (subject &gt; body &gt; attachment). This is doubly non-auto-filing: <c>sprk_regardingperson</c> is a mapper
/// FALLBACK field (excluded from auto-file), and — like <see cref="RecordNameMatch"/> — this rung is NOT in the
/// mapper's auto-file-eligible set. So emitting these matches cannot change auto-file behavior; it only surfaces
/// the named contacts for the reviewer to confirm.
/// </para>
/// <para>
/// <b>Precision guards</b> (common-name false positives are the main hazard). A candidate must be a run of ≥
/// <see cref="ContactNameMatchOptions.MinNameTokens"/> Title-Case tokens (a single common name matches nothing),
/// clear <see cref="ContactNameMatchOptions.MinNameLength"/>, AND resolve to an existing contact by EXACT
/// full-name lookup (a name matching no contact matches nothing). Bias is to precision over recall.
/// </para>
/// <para>
/// Best-effort/non-fatal (NFR-06): a regex timeout yields no phrases for that section; a throw is treated by the
/// resolver as a non-match. Registered as a concrete <see cref="IAssociationRung"/> singleton in
/// <c>CommunicationModule</c> (ADR-010), self-gated by <c>Communication:ContactNameMatch:Enabled</c>.
/// </para>
/// </remarks>
public sealed class ContactNameMatchRung : IAssociationRung
{
    private readonly ICommunicationDataverseService _communicationService;
    private readonly IOptions<ContactNameMatchOptions> _options;
    private readonly ILogger<ContactNameMatchRung> _logger;

    /// <summary>The single regarding field this rung ever writes — a mapper FALLBACK field (never auto-files).</summary>
    private const string RegardingField = "sprk_regardingperson";

    /// <summary>
    /// A run of 2+ Title-Case tokens (a candidate person-name phrase generator). A token is an uppercase letter
    /// followed by 1+ lowercase letters, with optional internal apostrophe/hyphen segments (O'Brien, Jean-Paul).
    /// Requiring Title-Case (not ALL-CAPS) is itself a precision guard — shouted headers ("PLEASE REVIEW") and
    /// signature blocks do not yield name runs. Precision ultimately comes from the EXACT contact lookup, not
    /// this pattern; an over-match simply resolves to no contact and emits nothing. Bounded 1s timeout (NFR-06).
    /// </summary>
    private static readonly Regex NameRunPattern =
        new(@"\p{Lu}[\p{Ll}]+(?:['’\-]\p{Lu}?[\p{Ll}]+)*(?:[ \t]+\p{Lu}[\p{Ll}]+(?:['’\-]\p{Lu}?[\p{Ll}]+)*)+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public ContactNameMatchRung(
        ICommunicationDataverseService communicationService,
        IOptions<ContactNameMatchOptions> options,
        ILogger<ContactNameMatchRung> logger)
    {
        _communicationService = communicationService;
        _options = options;
        _logger = logger;
    }

    public RungKind Kind => RungKind.ContactNameMatch;

    // Sorts evaluation sequence WITHIN the deterministic partition; runs alongside the other name/structure
    // detectors. The deterministic-vs-AI partition keys off Kind, not Order.
    public int Order => 3;

    public async Task<IReadOnlyList<RungMatch>> EvaluateAsync(
        NormalizedMessage message, AssociationContext context, CancellationToken ct)
    {
        var opts = _options.Value;

        if (!opts.Enabled)
        {
            _logger.LogDebug("Rung 3.6 (contact-name match) skipped — disabled via Communication:ContactNameMatch:Enabled.");
            return Array.Empty<RungMatch>();
        }

        // Candidate phrase → strongest location it appeared in (subject > body > attachment). First location
        // scanned wins so a phrase in the subject keeps its subject-band confidence even if it also appears in
        // the body/attachment. Bounded by MaxCandidates to cap the serial Dataverse round-trips.
        var phraseLocation = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Collect(string? text, string location)
        {
            if (phraseLocation.Count >= opts.MaxCandidates)
                return;
            foreach (var phrase in ExtractCandidatePhrases(text, opts))
            {
                if (!phraseLocation.ContainsKey(phrase))
                    phraseLocation[phrase] = location;
                if (phraseLocation.Count >= opts.MaxCandidates)
                    return;
            }
        }

        Collect(message.Subject, "subject");
        Collect(message.BodyText, "body");
        Collect(message.AttachmentText, "attachment");

        if (phraseLocation.Count == 0)
            return Array.Empty<RungMatch>();

        var startTs = Stopwatch.GetTimestamp();

        // Resolve each distinct phrase by EXACT full-name lookup; dedup matches by contact id keeping the
        // highest-confidence (i.e. best-location) appearance.
        var best = new Dictionary<Guid, RungMatch>();
        foreach (var (phrase, location) in phraseLocation)
        {
            var contacts = await _communicationService.QueryContactsByFullNameAsync(phrase, ct);
            if (contacts is null || contacts.Count == 0)
                continue; // name resolves to no contact → precision guard: emit nothing

            var confidence = LocationConfidence(location, opts);

            foreach (var contact in contacts)
            {
                if (contact.Id == Guid.Empty)
                    continue;

                var fullName = contact.GetAttributeValue<string>("fullname");
                // Defensive precision: only accept an EXACT (case-insensitive) full-name match. The query
                // already filters on fullname eq, but this guards against any partial/normalized surprise.
                if (!string.IsNullOrWhiteSpace(fullName)
                    && !string.Equals(fullName.Trim(), phrase, StringComparison.OrdinalIgnoreCase))
                    continue;

                var displayName = string.IsNullOrWhiteSpace(fullName) ? phrase : fullName;
                var provenance =
                    $"contact-name-match:where={location}:matched=fullname:" +
                    $"name=\"{displayName}\":reason=\"name in {location}\"";

                var match = new RungMatch
                {
                    RegardingFieldName = RegardingField,
                    Target = new EntityReference("contact", contact.Id) { Name = displayName },
                    Confidence = confidence,
                    Provenance = provenance,
                    Rung = RungKind.ContactNameMatch,
                };

                if (!best.TryGetValue(contact.Id, out var existing) || match.Confidence > existing.Confidence)
                    best[contact.Id] = match;
            }
        }

        var matches = best.Values
            .OrderByDescending(m => m.Confidence)
            .Take(opts.MaxCandidates)
            .ToArray();

        var elapsed = Stopwatch.GetElapsedTime(startTs);
        _logger.LogInformation(
            "Rung 3.6 (contact-name match) fired | Phrases: {PhraseCount}, Matches: {MatchCount}, ElapsedMs: {ElapsedMs}",
            phraseLocation.Count, matches.Length, (long)elapsed.TotalMilliseconds);

        return matches;
    }

    /// <summary>
    /// Extracts distinct candidate full-name phrases from one location's text: for every run of Title-Case
    /// tokens, all contiguous token windows of length <see cref="ContactNameMatchOptions.MinNameTokens"/> ..
    /// <see cref="ContactNameMatchOptions.MaxNameTokens"/> (so "Sara Chen" is caught whether standalone or
    /// embedded in a longer capitalized run). A phrase shorter than
    /// <see cref="ContactNameMatchOptions.MinNameLength"/> (alphanumeric-collapsed) is dropped. Bounded by
    /// <see cref="ContactNameMatchOptions.MaxCandidates"/>. Best-effort (NFR-06): a regex timeout yields no
    /// phrases for this section.
    /// </summary>
    private static IReadOnlyList<string> ExtractCandidatePhrases(string? text, ContactNameMatchOptions opts)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<string>();

        var scan = text.Length > opts.MaxTextChars ? text[..opts.MaxTextChars] : text;

        var phrases = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (Match run in NameRunPattern.Matches(scan))
            {
                var tokens = run.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                var maxLen = Math.Min(tokens.Length, opts.MaxNameTokens);

                for (var len = opts.MinNameTokens; len <= maxLen; len++)
                {
                    for (var start = 0; start + len <= tokens.Length; start++)
                    {
                        var phrase = string.Join(' ', tokens.Skip(start).Take(len));
                        if (CollapseAlphanumeric(phrase).Length < opts.MinNameLength)
                            continue;
                        if (seen.Add(phrase))
                            phrases.Add(phrase);
                        if (phrases.Count >= opts.MaxCandidates)
                            return phrases;
                    }
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // Defensive: treat a pathological input as yielding no phrases for this section (NFR-06).
        }

        return phrases;
    }

    private static double LocationConfidence(string location, ContactNameMatchOptions opts) => location switch
    {
        "subject" => opts.SubjectNameConfidence,
        "body" => opts.BodyNameConfidence,
        _ => opts.AttachmentNameConfidence,
    };

    /// <summary>Lowercases and removes ALL non-alphanumeric characters (for length gating).</summary>
    private static string CollapseAlphanumeric(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }
}

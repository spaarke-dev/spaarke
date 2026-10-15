using System.Globalization;
using System.Text.Json;
using Sprk.Bff.Api.Infrastructure.Dataverse;

namespace Sprk.Bff.Api.Services.Signals.Actions;

/// <summary>sprk_signal.sprk_lane (live option values).</summary>
public enum SignalLane
{
    Decide = 100000000,
    Do = 100000001,
}

/// <summary>
/// One policy version's standing metric (spec section 7, H-7): <c>acted / (acted + dismissed)</c> over the window.
/// <see cref="Rate"/> is null, and <see cref="TooFewToJudge"/> true, below <see cref="PolicyActionRateService.MinimumJudged"/>.
/// </summary>
public sealed record PolicyActionRate(string PolicyCode, Guid? PolicyVersionId, SignalLane Lane, int Acted, int Dismissed)
{
    public int Judged => Acted + Dismissed;

    public bool TooFewToJudge => Judged < PolicyActionRateService.MinimumJudged;

    public double? Rate => TooFewToJudge ? null : (double)Acted / Judged;
}

/// <summary>
/// Task 071 (spec section 7, H-7, decision D-111): action rate per policy VERSION over the last 90 days, read from
/// <c>sprk_signal.sprk_resolutiontype</c> (<c>Acted</c> 100000000, <c>Dismissed</c> 100000001) and NEVER from Decision
/// Records. A Do-lane Signal is acted on with no gate and no Decision Record, so counting Decision Records would report
/// about 0 percent on every Do rule and make a noisy policy and a perfect one look identical. The one rule is used for both
/// lanes: a confirmed Decide Signal also closes <c>Acted</c>.
/// </summary>
/// <remarks>
/// Only Signals RESOLVED (<c>sprk_resolvedon</c>) in the window and closed Acted or Dismissed are counted: Superseded,
/// Policy Retired, Condition Cleared and open Signals are not in the denominator. Runs as the caller, so a user's rate
/// covers the Signals they may read; a server-side aggregate, so there is no paging to truncate.
/// </remarks>
public sealed class PolicyActionRateService(IDataverseUserClient user, TimeProvider clock, ILogger<PolicyActionRateService> logger)
{
    internal const int Acted = 100000000;
    internal const int Dismissed = 100000001;

    /// <summary>The window, in days, ending now (<c>sprk_resolvedon</c>).</summary>
    public const int WindowDays = 90;

    /// <summary>Fewer acted + dismissed Signals than this and the rate is "too few to judge".</summary>
    public const int MinimumJudged = 5;

    public async Task<IReadOnlyList<PolicyActionRate>> GetAsync(CancellationToken ct)
    {
        var since = clock.GetUtcNow().UtcDateTime.AddDays(-WindowDays);
        var response = await user.GetAsync($"sprk_signals?fetchXml={Uri.EscapeDataString(BuildFetch(since))}", ct).ConfigureAwait(false);
        var rows = InquiryDispositionService.ReadRows(response);

        var parsed = new List<(string Code, Guid? Version, SignalLane Lane, int Resolution, int Count)>();
        foreach (var r in rows)
        {
            var code = r.TryGetProperty("policy", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            if (string.IsNullOrEmpty(code) || !r.TryGetProperty("lane", out var l) || l.ValueKind != JsonValueKind.Number
                || !Enum.IsDefined(typeof(SignalLane), l.GetInt32())
                || !r.TryGetProperty("resolution", out var x) || x.ValueKind != JsonValueKind.Number)
            {
                // A Signal with no lane (or no policy code) cannot be attributed to a policy: skipped, never thrown.
                logger.LogWarning("Action rate: a Signal group with no policy code, lane or resolution was skipped | Count: {Count}",
                    r.TryGetProperty("n", out var n) && n.ValueKind == JsonValueKind.Number ? n.GetInt32() : -1);
                continue;
            }

            parsed.Add((code, InquiryDispositionService.AsGuid(r, "version"), (SignalLane)l.GetInt32(), x.GetInt32(),
                InquiryDispositionService.ReadInt(r, "n")));
        }

        return parsed
            .GroupBy(r => (r.Code, r.Version, r.Lane))
            .Select(g => new PolicyActionRate(
                g.Key.Code, g.Key.Version, g.Key.Lane,
                g.Where(r => r.Resolution == Acted).Sum(r => r.Count),
                g.Where(r => r.Resolution == Dismissed).Sum(r => r.Count)))
            .OrderBy(r => r.PolicyCode, StringComparer.Ordinal)
            .ThenBy(r => r.PolicyVersionId)
            .ThenBy(r => r.Lane)
            .ToList();
    }

    /// <summary>Signals acted on or dismissed since <paramref name="since"/>, counted per policy code, version, lane and resolution.</summary>
    internal static string BuildFetch(DateTime since) =>
        "<fetch aggregate=\"true\"><entity name=\"sprk_signal\">"
        + "<attribute name=\"sprk_signalid\" alias=\"n\" aggregate=\"count\"/>"
        + "<attribute name=\"sprk_policycode\" alias=\"policy\" groupby=\"true\"/>"
        + "<attribute name=\"sprk_policyversion\" alias=\"version\" groupby=\"true\"/>"
        + "<attribute name=\"sprk_lane\" alias=\"lane\" groupby=\"true\"/>"
        + "<attribute name=\"sprk_resolutiontype\" alias=\"resolution\" groupby=\"true\"/>"
        + "<filter>"
        + $"<condition attribute=\"sprk_resolvedon\" operator=\"ge\" value=\"{since.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)}\"/>"
        + $"<condition attribute=\"sprk_resolutiontype\" operator=\"in\"><value>{Acted}</value><value>{Dismissed}</value></condition>"
        + "</filter></entity></fetch>";
}

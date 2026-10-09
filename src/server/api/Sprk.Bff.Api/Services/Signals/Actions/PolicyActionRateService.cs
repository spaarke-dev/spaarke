using System.Text.Json;
using Sprk.Bff.Api.Infrastructure.Dataverse;

namespace Sprk.Bff.Api.Services.Signals.Actions;

/// <summary>sprk_signal.sprk_lane (live option values).</summary>
public enum SignalLane
{
    Decide = 100000000,
    Do = 100000001,
}

/// <summary>One policy's standing metric: <c>acted / surfaced</c> (spec section 7). <see cref="Rate"/> is null when nothing was surfaced.</summary>
public sealed record PolicyActionRate(string PolicyCode, SignalLane Lane, int Surfaced, int Acted)
{
    public double? Rate => Surfaced == 0 ? null : (double)Acted / Surfaced;
}

/// <summary>
/// Task 071 (spec section 7, FR-37 note): action rate per policy, read from <c>sprk_signal.sprk_resolutiontype</c>
/// (<c>Acted</c> = 100000000) and NEVER from Decision Records. A Do-lane Signal is acted on with no gate and no Decision
/// Record, so counting Decision Records would report about 0 percent on every Do rule and make a noisy policy and a
/// perfect one look identical. The one rule is used for both lanes: a confirmed Decide Signal also closes <c>Acted</c>.
/// </summary>
/// <remarks>
/// <b>Surfaced</b> is every Signal of the policy, open or closed, whatever its resolution (the denominator the spec
/// names); <b>acted</b> is those resolved <c>Acted</c>. Runs as the caller, so a user's rate covers the Signals they may
/// read; a server-side aggregate, so there is no paging to truncate.
/// </remarks>
public sealed class PolicyActionRateService(IDataverseUserClient user)
{
    internal const int Acted = 100000000;

    public async Task<IReadOnlyList<PolicyActionRate>> GetAsync(CancellationToken ct)
    {
        var response = await user.GetAsync($"sprk_signals?fetchXml={Uri.EscapeDataString(BuildFetch())}", ct).ConfigureAwait(false);
        var rows = InquiryDispositionService.ReadRows(response);

        return rows
            .Select(r => (Code: r.TryGetProperty("policy", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null,
                          Lane: InquiryDispositionService.ReadInt(r, "lane"),
                          Resolution: r.TryGetProperty("resolution", out var x) && x.ValueKind == JsonValueKind.Number ? x.GetInt32() : (int?)null,
                          Count: InquiryDispositionService.ReadInt(r, "n")))
            .Where(r => !string.IsNullOrEmpty(r.Code) && Enum.IsDefined((SignalLane)r.Lane))
            .GroupBy(r => (r.Code!, Lane: (SignalLane)r.Lane))
            .Select(g => new PolicyActionRate(g.Key.Item1, g.Key.Lane, g.Sum(r => r.Count), g.Where(r => r.Resolution == Acted).Sum(r => r.Count)))
            .OrderBy(r => r.PolicyCode, StringComparer.Ordinal)
            .ThenBy(r => r.Lane)
            .ToList();
    }

    /// <summary>Signals counted per policy code, lane and resolution; open Signals group under a null resolution.</summary>
    internal static string BuildFetch() =>
        "<fetch aggregate=\"true\"><entity name=\"sprk_signal\">"
        + "<attribute name=\"sprk_signalid\" alias=\"n\" aggregate=\"count\"/>"
        + "<attribute name=\"sprk_policycode\" alias=\"policy\" groupby=\"true\"/>"
        + "<attribute name=\"sprk_lane\" alias=\"lane\" groupby=\"true\"/>"
        + "<attribute name=\"sprk_resolutiontype\" alias=\"resolution\" groupby=\"true\"/>"
        + "</entity></fetch>";
}

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// #3091 (DI-172, ontology task 217): <see cref="SignalClosureService.PoliciesNotInForceWithStoredDetailAsync"/>, the read that names the
/// not-in-force Ontology policies whose Signals still carry stored detail. Its filters ARE the performance fix (the per-policy sweep would
/// redact the same Signals without them), so these pin them: rows the filters must keep out would push the read past one page.
/// </summary>
public partial class SignalReevaluationJobTests
{
    private static string EvidenceWithSourceOnly() =>
        $"[{{\"kind\":\"communication\",\"ref\":\"sprk_communication:{Lower(CommunicationId)}\",\"tier\":\"Fact\",\"source\":\"partner@firm.example\"}}]";

    [Fact]
    public async Task StoredDetailRead_NamesOnlyNotInForceOntologyPoliciesWithDetail_InOnePageOfEachRead()
    {
        var h = new Harness();
        var off = AddOffPolicies(h, 3); // [0] detail, [1] only already-redacted Signals, [2] detail only on a Signal with no policy lookup
        SeedEvidenceSignal(h, off[0].Code, off[0].Id, StoredEvidenceQuotingTheWitness());
        for (var i = 1; i <= 5; i++) SeedEvidenceSignal(h, off[1].Code, off[1].Id, EvidenceAlreadyRedacted(), episode: i);
        SeedEvidenceSignal(h, off[2].Code, policyId: null, StoredEvidenceQuotingTheWitness());
        h.Sysadmin.AddPolicy("POL-RETIRED", SignalWriter.LaneDecide, enabled: true, PathBBody, PathBTemplate);
        h.Sysadmin.SetPolicyState("POL-RETIRED", enabled: true, statecode: 1);
        SeedEvidenceSignal(h, "POL-RETIRED", h.Sysadmin.PolicyIdOf("POL-RETIRED"), EvidenceWithSourceOnly());

        // Noise the filters must keep out of the joined read: detail on an in-force Ontology policy (state) and on an Off Billing policy
        // (domain); and a Signal with no policy whose key names no not-in-force policy (read, never named).
        h.Sysadmin.AddPolicy("POL-LIVE", SignalWriter.LaneDecide, enabled: true, PathBBody, PathBTemplate);
        for (var i = 1; i <= 10; i++) SeedEvidenceSignal(h, "POL-LIVE", h.Sysadmin.PolicyIdOf("POL-LIVE"), StoredEvidenceQuotingTheWitness(), episode: i);
        h.Sysadmin.AddPolicy("POL-BILL-OFF", SignalWriter.LaneDecide, enabled: false, PathBBody, PathBTemplate);
        SetDomain(h, "POL-BILL-OFF", RuleDomains.PolicyDomainBilling);
        for (var i = 1; i <= 3; i++) SeedEvidenceSignal(h, "POL-BILL-OFF", h.Sysadmin.PolicyIdOf("POL-BILL-OFF"), StoredEvidenceQuotingTheWitness(), episode: i);
        SeedEvidenceSignal(h, "POL-GONE", policyId: null, StoredEvidenceQuotingTheWitness());
        // And Signals with no policy whose detail is already cleared (the stored-detail filter keeps them out of the second read).
        for (var i = 2; i <= 4; i++) SeedEvidenceSignal(h, "POL-GONE", policyId: null, EvidenceAlreadyRedacted(), episode: i);

        var policies = await h.Closure.PoliciesNotInForceAsync(default);
        h.Sysadmin.PageSizeCap = 2; // each read's qualifying rows: joined [0] + Retired; no policy [2] + POL-GONE

        var read = await h.Closure.PoliciesNotInForceWithStoredDetailAsync(policies, default);

        read.IsComplete.Should().BeTrue();
        read.Codes.Should().BeEquivalentTo(new[] { off[0].Code, "POL-RETIRED", off[2].Code });
        h.Sysadmin.StoredDetailReads.Should().Equal(new[] { "joined", "no policy" },
            "only Signals with detail, of Off or Retired Ontology policies or of none, are read, so each read fits one page of its own count");
    }

    [Fact]
    public async Task StoredDetailRead_PastTheBoundOrOnAFault_IsIncomplete_AndLogged()
    {
        var h = new Harness();
        var off = AddOffPolicies(h, 3);
        foreach (var p in off) SeedEvidenceSignal(h, p.Code, p.Id, StoredEvidenceQuotingTheWitness());
        var policies = await h.Closure.PoliciesNotInForceAsync(default);
        h.Sysadmin.PageSizeCap = 1;

        var bounded = await h.Closure.PoliciesNotInForceWithStoredDetailAsync(policies, default, maxPages: 2);

        bounded.IsComplete.Should().BeFalse("three Signals do not fit two pages of one");
        h.Log.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("More than 2 pages"));

        h.Sysadmin.PageSizeCap = null;
        h.Sysadmin.FaultOn = q => q.EntityName == "sprk_signal" && q.LinkEntities.Count == 0 ? new TimeoutException("Dataverse timed out.") : null;

        var faulted = await h.Closure.PoliciesNotInForceWithStoredDetailAsync(policies, default);

        faulted.IsComplete.Should().BeFalse();
        h.Log.Should().Contain(e => e.Level == LogLevel.Warning && e.Exception is TimeoutException);
    }

    [Fact]
    public async Task StoredDetailRead_NoNotInForcePolicy_ReadsNothing()
    {
        var h = new Harness();
        var read = await h.Closure.PoliciesNotInForceWithStoredDetailAsync(new Dictionary<Guid, string>(), default);

        read.IsComplete.Should().BeTrue();
        read.Codes.Should().BeEmpty();
        h.Sysadmin.StoredDetailReads.Should().BeEmpty();
    }
}

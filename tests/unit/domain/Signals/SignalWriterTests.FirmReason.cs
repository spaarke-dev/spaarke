using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// ebilling-r1 task 130e-1 (DEF-060, design 3.3 (a)(b)(c), uac-r2 C3 and O1): <see cref="SignalWriteRequest.FirmReason"/> rides the one
/// create as <c>sprk_firmreason</c> and nothing else. It is never on an update (the dedupe refresh, a Resolved row), never stamped on the
/// row as a creator, and is not part of the dedupe key or the sentence. A direct caller's invalid text is refused before any Dataverse call.
/// </summary>
public partial class SignalWriterTests
{
    private const string FirmText = "Travel is billed at half the agreed rate.";

    private static string MatterDedupeKey(SignalWriteRequest request) =>
        SignalWriter.BuildDedupeKey(request.PolicyCode, "sprk_matter", MatterId.ToString("D").ToLowerInvariant(), 1);

    private static SignalWriter FirmReasonWriter(FakeOrganizationService org, DateTimeOffset at) =>
        Build(org, new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId), new FakeTimeProvider(at));

    [Fact(DisplayName = "130e-1 must-fire: a request with a firm reason creates the Signal with sprk_firmreason, in the one create call")]
    public async Task FirmReason_IsWrittenOnTheCreate()
    {
        var org = new FakeOrganizationService();

        var result = await FirmReasonWriter(org, FirstRun).WriteAsync(MatterSubjectRequest() with { FirmReason = FirmText });

        result.Created.Should().BeTrue();
        org.CreateCallCount.Should().Be(1);
        var created = org.CreatedPayloads.Should().ContainSingle().Subject;
        created.GetAttributeValue<string>("sprk_firmreason").Should().Be(FirmText);
        created.Attributes.Keys.Should().NotContain("createdonbehalfby", "the firm reason is written as the writer identity, never on behalf of anyone");
        org.UpdateCalls.Should().BeEmpty("the firm reason never needs a second round-trip");
    }

    [Theory(DisplayName = "130e-1 must-not-fire: a request without a firm reason (null, empty or blank) sends no sprk_firmreason key at all")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task NoFirmReason_SendsNoKey(string? firmReason)
    {
        var org = new FakeOrganizationService();

        await FirmReasonWriter(org, FirstRun).WriteAsync(MatterSubjectRequest() with { FirmReason = firmReason });

        org.CreatedPayloads.Should().ContainSingle().Which.Attributes.Keys.Should().NotContain("sprk_firmreason");
    }

    [Fact(DisplayName = "130e-1 O1: the firm reason is not in the dedupe key and does not change the sentence, headline or fact snapshot")]
    public async Task FirmReason_IsNotInTheKey_NorTheSentence()
    {
        var without = new FakeOrganizationService();
        var with = new FakeOrganizationService();

        await FirmReasonWriter(without, FirstRun).WriteAsync(MatterSubjectRequest());
        await FirmReasonWriter(with, FirstRun).WriteAsync(MatterSubjectRequest() with { FirmReason = FirmText });

        var a = without.CreatedPayloads.Single();
        var b = with.CreatedPayloads.Single();
        foreach (var column in new[] { "sprk_dedupekey", "sprk_sentence", "sprk_name", "sprk_factsnapshot", "sprk_evidencerefs" })
        {
            b.GetAttributeValue<string>(column).Should().Be(a.GetAttributeValue<string>(column), column);
        }

        b.Attributes.Keys.Except(a.Attributes.Keys).Should().Equal(["sprk_firmreason"], "the firm reason adds exactly one column and changes no other");
        b.GetAttributeValue<string>("sprk_sentence").Should().NotContain(FirmText);
    }

    [Theory(DisplayName = "130e-1 3.3(c) + O1: the dedupe refresh of an open or a Resolved Signal sends exactly sprk_lastevaluated, never the firm reason or the version")]
    [InlineData(100000000)] // Open
    [InlineData(100000002)] // Resolved
    public async Task Refresh_NeverSendsTheFirmReason(int status)
    {
        var org = new FakeOrganizationService();
        var request = MatterSubjectRequest() with { FirmReason = FirmText };
        org.SeedExisting(MatterDedupeKey(request), new Entity("sprk_signal")
        {
            ["sprk_corerecordid"] = MatterId.ToString("D"),
            ["sprk_signalstatus"] = new OptionSetValue(status),
            ["ownerid"] = new EntityReference("team", DefaultTeamId),
        });
        org.ThrowDuplicateOnNextCreate = true;

        var result = await FirmReasonWriter(org, SecondRun).WriteAsync(request);

        result.Created.Should().BeFalse();
        org.UpdateCalls.Should().ContainSingle().Which.Fields.Keys.Should().BeEquivalentTo(["sprk_lastevaluated"],
            "the exact refresh set: a firm reason is written on create only");
    }

    [Fact(DisplayName = "130e-1 O1: a refresh under a newer policy version with a different description leaves the stored text and the cited version unchanged")]
    public async Task Refresh_UnderANewerVersion_KeepsTheStoredTextAndVersion()
    {
        var org = new FakeOrganizationService();
        var newerVersion = Guid.Parse("130e0000-0000-4000-8000-0000000000f2");
        var request = MatterSubjectRequest() with { PolicyVersionId = newerVersion, FirmReason = "A newer description." };
        var existingId = org.SeedExisting(MatterDedupeKey(request), new Entity("sprk_signal")
        {
            ["sprk_corerecordid"] = MatterId.ToString("D"),
            ["sprk_signalstatus"] = new OptionSetValue(100000000),
            ["ownerid"] = new EntityReference("team", DefaultTeamId),
            ["sprk_firmreason"] = FirmText,
            ["sprk_policyversion"] = new EntityReference("sprk_policyversion", PolicyVersionId),
        });
        org.ThrowDuplicateOnNextCreate = true;

        var result = await FirmReasonWriter(org, SecondRun).WriteAsync(request);

        result.SignalId.Should().Be(existingId);
        org.Rows[existingId].GetAttributeValue<string>("sprk_firmreason").Should().Be(FirmText);
        org.Rows[existingId].GetAttributeValue<EntityReference>("sprk_policyversion").Id.Should().Be(PolicyVersionId);
        org.UpdateCalls.Should().ContainSingle().Which.Fields.Keys.Should().NotContain(["sprk_firmreason", "sprk_policyversion"]);
    }

    [Theory(DisplayName = "130e-1 C3: a direct caller's firm reason that fails the rules is refused fail-closed (firm_reason_invalid): nothing reaches Dataverse")]
    [InlineData("over")]
    [InlineData("{")]
    [InlineData("}")]
    [InlineData("<")]
    [InlineData(">")]
    [InlineData("\u0007")]
    [InlineData("\u200B")]
    [InlineData("\u2028")]
    [InlineData("\u2029")]
    public async Task InvalidFirmReason_IsRefused_BeforeAnyDataverseCall(string sample)
    {
        var text = sample == "over" ? new string('x', FirmDescriptionRules.MaxRunes + 1) : $"Rate{sample} too high.";
        var org = new FakeOrganizationService();
        var request = MatterSubjectRequest() with { FirmReason = text };

        SignalWriter.PreflightRefusal(request).Should().Be(SignalWriteRefusal.FirmReasonInvalid);
        var act = async () => await FirmReasonWriter(org, FirstRun).WriteAsync(request);

        var thrown = (await act.Should().ThrowAsync<ArgumentException>()).Which;
        thrown.Message.Should().NotContain(text, "the refusal never echoes the text");
        org.CreateCallCount.Should().Be(0);
        org.UpdateCalls.Should().BeEmpty();
        org.RetrievedColumns.Should().BeEmpty("the refusal precedes the first Dataverse call, so no partial write is possible");
        org.Rows.Should().BeEmpty();
    }

    [Fact(DisplayName = "130e-1 C3 must-not-fire: a valid firm reason at the 200-rune bound passes the preflight")]
    public void ValidFirmReason_AtTheBound_PassesThePreflight()
    {
        var atBound = string.Concat(Enumerable.Repeat("\U0001F600", FirmDescriptionRules.MaxRunes));

        SignalWriter.PreflightRefusal(MatterSubjectRequest() with { FirmReason = atBound }).Should().BeNull();
        SignalWriter.PreflightRefusal(MatterSubjectRequest() with { FirmReason = FirmText }).Should().BeNull();
    }

    [Fact(DisplayName = "130e-1 Q2: the evidence the writer stores is read back by the projection's reason-code reader (writer and reader agree on the shape)")]
    public async Task WrittenEvidence_IsReadBackByTheProjectionsReasonCodeReader()
    {
        var org = new FakeOrganizationService();
        var request = MatterSubjectRequest() with
        {
            EvidenceRefs =
            [
                new SignalEvidenceRef(BillingFindingSignalMapper.ReasonEvidenceKind, BillingReasonCodes.RateAboveApproved, "Fact") { Text = "Reason text." },
                new SignalEvidenceRef("snapshot", "sprk_matter:x", "Fact"),
            ],
        };

        await FirmReasonWriter(org, FirstRun).WriteAsync(request);

        var stored = org.CreatedPayloads.Single().GetAttributeValue<string>("sprk_evidencerefs");
        FirmFindingsProjection.ReasonCodesOf(stored).Should().BeEquivalentTo([BillingReasonCodes.RateAboveApproved]);
    }
}

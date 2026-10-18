using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.BillingEvaluationKit;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers.BillingHandlerKit;

namespace Sprk.Bff.Api.Tests.Domain.Signals.Billing;

/// <summary>
/// ebilling-r1 task 072: <see cref="BillingFindingSignalMapper"/>. Every mapped request passes the writer's template and fact checks
/// (<see cref="SignalWriter.RenderSentence"/>), whatever tokens a finding lacks; an invoice-level Duplicate with the default template
/// is written; a withheld Duplicate (D-112) puts no withheld value into any field of the written Signal (071 fix round 2).
/// </summary>
public class BillingFindingSignalMapperTests
{
    private static readonly Guid InvoiceRowId = Guid.Parse("07200000-0000-4000-8000-0000000000e1");
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);

    private static BillingSignalSubjects SubjectsFor(BillingEvaluationRequest request) => new(
        InvoiceRowId,
        "INV-100",
        request.Lines.ToDictionary(l => l.Index, l => Id("line-row:" + l.Index)));

    private static IEnumerable<BillingSignalMapping> MapAll(BillingEvaluationRequest request, BillingEvaluationResult result) =>
        result.Findings.Select(f => BillingFindingSignalMapper.Map(f, result.RulesByVersion[f.PolicyVersionId], SubjectsFor(request)));

    public static TheoryData<string> TemplateChoices => new() { "blank", "default wording" };

    [Theory]
    [MemberData(nameof(TemplateChoices))]
    public void EveryFindingOfEveryType_MapsToARequestTheWriterAccepts(string templates)
    {
        var (request, loaded) = EverythingWrong(20);
        loaded = loaded with
        {
            Rules = loaded.Rules.Select(r => r with
            {
                MessageTemplate = templates == "blank" ? null : BillingRuleDraftTranslator.Wording(Enum.Parse<RuleType>(r.RuleType!)).Template,
            }).ToList(),
        };
        var result = Evaluator().Evaluate(request, loaded);
        result.Findings.Select(f => f.RuleType).Distinct().Should().HaveCount(11);

        foreach (var (finding, mapping) in result.Findings.Zip(MapAll(request, result)))
        {
            var sent = mapping.Request;
            var render = () => SignalWriter.RenderSentence(sent.MessageTemplate, sent.FactValues);
            render.Should().NotThrow($"{finding.RuleType} {finding.ReasonCode} must render");
            sent.FactValues.Keys.Should().BeSubsetOf(BillingRuleCatalogue.TemplateFactFields(finding.RuleType));
            sent.ShortHeadline.Should().NotBeNullOrWhiteSpace();
            sent.ShortHeadline.Length.Should().BeLessThanOrEqualTo(200);
            sent.PolicyCode.Length.Should().BeLessThanOrEqualTo(50);
            sent.Lane.Should().Be(SignalWriter.LaneDecide, "a policy with no lane is a Decide item");
            sent.PolicyVersionId.Should().Be(finding.PolicyVersionId);
            if (finding.Subject.Kind == BillingSubjectKind.Invoice)
            {
                sent.SubjectEntityLogicalName.Should().Be("sprk_invoice");
                sent.SubjectId.Should().Be(InvoiceRowId);
            }
            else
            {
                sent.SubjectEntityLogicalName.Should().Be("sprk_billingevent");
                sent.SubjectId.Should().Be(Id("line-row:" + finding.Subject.LineIndex));
            }
        }
    }

    [Fact]
    public void TheVersionsTemplate_IsUsed_WhenEveryTokenHasAValue()
    {
        var rule = Version("CD-INCREMENT", RuleType.TimeIncrement, """{"type":"TimeIncrement","incrementHours":0.5}""",
            template: "{{line.hours}} hours is not a multiple of {{rule.incrementHours}}.");
        var request = Request(Fee(0) with { Hours = 0.3m });
        var result = Evaluator().Evaluate(request, Loaded(rule));

        var mapping = MapAll(request, result).Should().ContainSingle().Subject;

        mapping.MessageSource.Should().Be(BillingMessageSource.PolicyVersion);
        SignalWriter.RenderSentence(mapping.Request.MessageTemplate, mapping.Request.FactValues)
            .Should().Be("0.3 hours is not a multiple of 0.5.");
    }

    [Fact]
    public void ATemplateNamingAValueTheFindingLacks_FallsBack_SoTheFindingIsNeverRefused()
    {
        // No billed timekeeper name: the ApprovedTimekeeper finding has no line.timekeeperName, which both the version's template and
        // the default wording name.
        var rule = Version("CD-TIMEKEEPER", RuleType.ApprovedTimekeeper,
            template: "{{line.timekeeperName}} is not approved on {{line.date}}.");
        var request = Request(Fee(0) with { TimekeeperName = null, Timekeeper = Unmatched(Sprk.Bff.Api.Services.Finance.Resolution.TimekeeperUnmatchedReason.NameMissing) });
        var result = Evaluator().Evaluate(request, Loaded(rule));

        var mapping = MapAll(request, result).Should().ContainSingle().Subject;

        mapping.MessageSource.Should().Be(BillingMessageSource.HeadlineOnly);
        SignalWriter.RenderSentence(mapping.Request.MessageTemplate, mapping.Request.FactValues).Should().Be("Timekeeper not approved.");
    }

    [Fact]
    public void AFindingOnALineWithNoRow_IsRefused()
    {
        var (request, loaded) = EverythingWrong(3);
        var result = Evaluator().Evaluate(request, loaded);
        var lineFinding = result.Findings.First(f => f.Subject.Kind == BillingSubjectKind.Line);

        var act = () => BillingFindingSignalMapper.Map(lineFinding, result.RulesByVersion[lineFinding.PolicyVersionId],
            new BillingSignalSubjects(InvoiceRowId, "INV-100", new Dictionary<int, Guid>()));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AFindingMappedWithAnotherVersion_IsRefused()
    {
        var (request, loaded) = EverythingWrong(3);
        var result = Evaluator().Evaluate(request, loaded);
        var finding = result.Findings[0];
        var other = result.RulesByVersion.Values.First(v => v.PolicyVersionId != finding.PolicyVersionId);

        var act = () => BillingFindingSignalMapper.Map(finding, other, SubjectsFor(request));

        act.Should().Throw<ArgumentException>();
    }

    // ── Through the real writer ──────────────────────────────────────────────────────────────────────────────────

    private static (SignalWriter Writer, SignalWriterTests.FakeOrganizationService Rows) Writer(BillingSignalSubjects subjects)
    {
        var sysadmin = new SignalWriterTests.FakeSysadminClient()
            .WithMatterBusinessUnit(SignalWriterTests.MatterId, SignalWriterTests.BusinessUnitId)
            .Stub(new Entity("sprk_invoice", subjects.InvoiceId) { ["sprk_matter"] = new EntityReference("sprk_matter", SignalWriterTests.MatterId) });
        foreach (var lineId in subjects.LineIds.Values)
        {
            sysadmin.Stub(new Entity("sprk_billingevent", lineId) { ["sprk_matter"] = new EntityReference("sprk_matter", SignalWriterTests.MatterId) });
        }

        var org = new SignalWriterTests.FakeOrganizationService();
        return (SignalWriterTests.Build(org, sysadmin, new FakeTimeProvider(Now)), org);
    }

    [Fact]
    public async Task AnInvoiceLevelDuplicate_WithTheDefaultTemplate_IsWritten_071FixRound2()
    {
        // The default Duplicate template names line tokens an invoice-level finding cannot have; the writer would refuse it.
        var rule = Version("CD-DUPLICATE", RuleType.Duplicate, """{"type":"Duplicate","matchFields":["firmInvoiceNumber"]}""",
            template: BillingRuleDraftTranslator.Wording(RuleType.Duplicate).Template);
        var request = Request(Fee(0));
        var loaded = Loaded(rule) with { PriorSubmissions = [Prior("INV-100.1", "INV-100", 1)] };
        var result = Evaluator().Evaluate(request, loaded);
        var subjects = SubjectsFor(request);
        var mapping = BillingFindingSignalMapper.Map(result.Findings.Should().ContainSingle().Subject, rule, subjects);
        var (writer, org) = Writer(subjects);

        var written = await writer.WriteAsync(mapping.Request);

        mapping.MessageSource.Should().Be(BillingMessageSource.InvoiceLevelDuplicate);
        var row = org.Rows[written.SignalId];
        row.GetAttributeValue<string>("sprk_sentence").Should().Be("Invoice INV-100 duplicates INV-100.1.");
        row.GetAttributeValue<EntityReference>("sprk_regardinginvoice").Id.Should().Be(InvoiceRowId);
    }

    [Fact]
    public async Task AWithheldDuplicate_StoresNoTrackingNumber_InAnyFieldOfTheWrittenSignal_D112()
    {
        const string hiddenTrackingNumber = "INV-100.1";
        var rule = Version("CD-DUPLICATE", RuleType.Duplicate,
            template: BillingRuleDraftTranslator.Wording(RuleType.Duplicate).Template);
        var request = Request(Fee(0));
        var acrossTheWall = Prior(hiddenTrackingNumber, "INV-100", 1, BillingPriorStatus.Active,
            new BillingPriorLine { Date = LineDate, TimekeeperId = Matched().TimekeeperId, Hours = 1.0m }) with
        {
            SecureRoots = Roots(Id("secure-matter")),
        };
        var loaded = Loaded(rule) with { PriorSubmissions = [acrossTheWall] }; // the invoice is ordinary: the prior is hidden
        var result = Evaluator().Evaluate(request, loaded);
        result.Findings.Should().HaveCount(2).And.OnlyContain(f => f.DetailWithheld, "one invoice-level and one line-level match, both hidden");
        var subjects = SubjectsFor(request);
        var (writer, org) = Writer(subjects);

        foreach (var finding in result.Findings)
        {
            var mapping = BillingFindingSignalMapper.Map(finding, rule, subjects);
            mapping.MessageSource.Should().Be(BillingMessageSource.WithheldDuplicate);
            mapping.Request.MessageTemplate.Should().Be(BillingRuleCatalogue.WithheldDuplicateTemplate);
            mapping.Request.FactValues.Should().BeEmpty();

            var written = await writer.WriteAsync(mapping.Request);

            var row = org.Rows[written.SignalId];
            row.GetAttributeValue<string>("sprk_sentence").Should().Be(BillingRuleCatalogue.WithheldDuplicateTemplate);
            foreach (var (column, value) in row.Attributes)
            {
                var text = value switch
                {
                    EntityReference reference => $"{reference.LogicalName} {reference.Id} {reference.Name}",
                    null => string.Empty,
                    _ => value.ToString() ?? string.Empty,
                };
                text.Should().NotContain(hiddenTrackingNumber, $"column {column} must not carry the withheld tracking number");
                text.Should().NotContain("100.1", $"column {column} must not carry any part of it");
            }
        }
    }
}

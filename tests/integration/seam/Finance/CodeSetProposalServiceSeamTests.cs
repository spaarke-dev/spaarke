using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Coding;
using Sprk.Bff.Api.Services.Finance.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Finance;

/// <summary>
/// Seam for the AI code-set mapping proposals (ebilling-r1 task 048, FR-22): the production <see cref="CodeSetProposalService"/> runs
/// against the in-memory Dataverse that evaluates the queries it sends (<see cref="FakeCodingReferenceData"/>). Only the embedding call
/// is doubled, with fixed vectors on four axes (litigation, IP, bankruptcy, counseling), so a ranking is a property of the vectors
/// and the service's query logic, not of a model.
/// </summary>
public sealed class CodeSetProposalServiceSeamTests
{
    private const string DefaultSetColumn = "sprk_defaultbillingcodeset";

    private readonly FakeCodingReferenceData _data = new();
    private readonly Mock<IInvoiceAi> _ai = new();
    private readonly Dictionary<string, float[]> _vectors = new(StringComparer.Ordinal);
    private readonly List<string> _embedded = [];

    private readonly Guid _litigation;
    private readonly Guid _bankruptcy;
    private readonly Guid _ip;
    private readonly Guid _activity;

    public CodeSetProposalServiceSeamTests()
    {
        _ai.Setup(a => a.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string text, string? _, int? _, CancellationToken _) =>
            {
                // A code set is embedded as its profile, whose first line is "{name} ({body})"; a reference row as its name.
                _embedded.Add(text);
                return _vectors.TryGetValue(text.Split('\n')[0], out var v)
                    ? new ReadOnlyMemory<float>(v)
                    : throw new InvalidOperationException($"The test has no vector for '{text}'.");
            });

        // Task 054: the AI review step is unavailable here (no Binding yet), so every proposal is the embedding ranking, unreviewed.
        // CodeSetMatchingSeamTests covers the review step itself.
        _ai.Setup(a => a.MatchCodeSetAsync(It.IsAny<InvoiceCodeSetMatchRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvoiceAiException(InvoiceAiException.BindingMissing, ConsumerTypes.InvoiceCodeSetMatch, "no binding"));

        // Usable sets: each has Task codes. Activity has none (it holds only Activity codes), so it can never be proposed.
        _litigation = SetWithTaskCode("UTBMS Litigation", "ABA", "L110", [1f, 0f, 0f, 0f]);
        _bankruptcy = SetWithTaskCode("UTBMS Bankruptcy", "ABA", "B110", [0f, 0f, 1f, 0f]);
        _ip = SetWithTaskCode("UTBMS IP Patent Prosecution", "LOC", "P110", [0f, 1f, 0f, 0f]);
        _activity = _data.AddSet("UTBMS Activity", body: "ABA");
        _data.AddCode(_activity, "A101", BillingCodeKind.Activity, "Plan and prepare for");
        _vectors["UTBMS Activity (ABA)"] = [0.5f, 0.5f, 0.5f, 0.5f];
    }

    private CodeSetProposalService Sut() => new(_data.Entities.Object, _data.Records.Object, _ai.Object, NullLogger<CodeSetProposalService>.Instance);

    private Guid SetWithTaskCode(string name, string body, string code, float[] vector)
    {
        var id = _data.AddSet(name, body: body);
        _data.AddCode(id, code, BillingCodeKind.Task, "A task");
        _vectors[$"{name} ({body})"] = vector;
        return id;
    }

    private Guid MatterType(string name, float[] vector, Guid? mapped = null, bool active = true)
    {
        _vectors[name] = vector;
        return _data.AddMatterType(mapped, name, active);
    }

    private Guid PracticeArea(string name, float[] vector, Guid? mapped = null, bool active = true)
    {
        _vectors[name] = vector;
        return _data.AddPracticeArea(mapped, name, active);
    }

    // ── Proposals ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Propose_RanksEveryUnmappedRow_BestSetWithScoreRunnerUpAndRationale()
    {
        var litigationType = MatterType("Litigation", [0.9f, 0.1f, 0f, 0f]);
        var bankruptcyType = MatterType("Bankruptcy", [0.1f, 0f, 0.9f, 0f]);
        var patents = PracticeArea("Intellectual Property Patents", [0.1f, 0.9f, 0f, 0f]);

        var proposals = (await Sut().ProposeAsync()).Proposals;

        proposals.Select(p => (p.Table, p.ReferenceId)).Should().Equal(
            ("mattertype", bankruptcyType), ("mattertype", litigationType), ("practicearea", patents));

        var litigation = proposals.Single(p => p.ReferenceId == litigationType);
        litigation.Proposed!.CodeSetId.Should().Be(_litigation);
        litigation.Proposed.Name.Should().Be("UTBMS Litigation");
        litigation.Proposed.Score.Should().BeApproximately(0.994m, 0.001m);
        litigation.RunnerUp!.CodeSetId.Should().Be(_ip);
        litigation.RunnerUp.Score.Should().BeLessThan(litigation.Proposed.Score);
        litigation.Rationale.Should().Contain("'Litigation'").And.Contain("'UTBMS Litigation'").And.Contain("runner-up");

        proposals.Single(p => p.ReferenceId == bankruptcyType).Proposed!.CodeSetId.Should().Be(_bankruptcy);
        proposals.Single(p => p.ReferenceId == patents).Proposed!.CodeSetId.Should().Be(_ip);
    }

    [Fact]
    public async Task Propose_ExcludesMappedAndInactiveRows_AndWritesNothing()
    {
        var unmapped = MatterType("Litigation", [1f, 0f, 0f, 0f]);
        MatterType("Bankruptcy", [0f, 0f, 1f, 0f], mapped: _bankruptcy);
        MatterType("Retired type", [1f, 0f, 0f, 0f], active: false);
        PracticeArea("Appellate", [1f, 0f, 0f, 0f], mapped: _litigation);

        var proposals = (await Sut().ProposeAsync()).Proposals;

        proposals.Should().ContainSingle().Which.ReferenceId.Should().Be(unmapped);
        _data.Updates.Should().BeEmpty("proposing is read-only");
        _data.Entities.Verify(
            e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Propose_NeverOffersASetWithoutActiveTaskCodes_OrAnInactiveSet()
    {
        var inactive = _data.AddSet("UTBMS Trademark", active: false, body: "LOC");
        _data.AddCode(inactive, "T100", BillingCodeKind.Task, "A task");
        _vectors["UTBMS Trademark (LOC)"] = [0f, 0f, 0f, 1f];
        var onlyInactiveCodes = _data.AddSet("UTBMS Counseling", body: "ABA");
        _data.AddCode(onlyInactiveCodes, "C100", BillingCodeKind.Task, "A task", active: false);
        _vectors["UTBMS Counseling (ABA)"] = [0f, 0f, 0f, 1f];
        // Closest of all to the row below, but the Activity set has no Task codes and the others are inactive.
        MatterType("Trademark and counseling", [0.3f, 0f, 0f, 1f]);

        var proposals = (await Sut().ProposeAsync()).Proposals;

        var proposed = proposals.Single();
        new[] { proposed.Proposed!.CodeSetId, proposed.RunnerUp!.CodeSetId }
            .Should().NotContain(new[] { _activity, inactive, onlyInactiveCodes });
        _embedded.Should().NotContain(t => t.StartsWith("UTBMS Activity (ABA)", StringComparison.Ordinal) || t.StartsWith("UTBMS Trademark (LOC)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Propose_WithNoUsableCodeSet_ReturnsTheRowWithNoProposal()
    {
        var data = new FakeCodingReferenceData();
        var empty = data.AddSet("UTBMS Activity");
        data.AddCode(empty, "A101", BillingCodeKind.Activity);
        var row = data.AddMatterType(null, "Litigation");
        var sut = new CodeSetProposalService(data.Entities.Object, data.Records.Object, _ai.Object, NullLogger<CodeSetProposalService>.Instance);

        var proposal = (await sut.ProposeAsync()).Proposals.Single();

        proposal.ReferenceId.Should().Be(row);
        proposal.Proposed.Should().BeNull();
        proposal.RunnerUp.Should().BeNull();
        proposal.Outcome.Should().Be(CodeSetMatchOutcome.NoneFits, "with no usable set nothing can be proposed (D-93)");
        proposal.Rationale.Should().Contain("No active code set");
        _embedded.Should().BeEmpty("there was nothing to compare against");
    }

    [Fact]
    public async Task Propose_EmbedsEachCodeSetOncePerInstance_NotOncePerRow()
    {
        MatterType("Litigation", [1f, 0f, 0f, 0f]);
        MatterType("Bankruptcy", [0f, 0f, 1f, 0f]);
        PracticeArea("Appellate", [1f, 0f, 0f, 0f]);
        var sut = Sut();

        await sut.ProposeAsync();
        await sut.ProposeAsync();

        _embedded.Count(t => t.StartsWith("UTBMS Litigation (ABA)\n", StringComparison.Ordinal)).Should().Be(1);
        _embedded.Count(t => t.StartsWith("UTBMS Bankruptcy (ABA)\n", StringComparison.Ordinal)).Should().Be(1);
        _embedded.Count(t => t == "Litigation").Should().Be(2, "a row is embedded each time it is proposed for");
    }

    [Fact]
    public async Task Propose_WhenTheAiIsOff_PropagatesTheFeatureDisabledException()
    {
        MatterType("Litigation", [1f, 0f, 0f, 0f]);
        _ai.Setup(a => a.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FeatureDisabledException("ai.invoice.disabled", "AI is off"));

        var act = () => Sut().ProposeAsync();

        await act.Should().ThrowAsync<FeatureDisabledException>();
        _data.Updates.Should().BeEmpty();
    }

    // ── Confirm ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Confirm_WritesExactlyOneLookupOnExactlyThatRow()
    {
        var target = MatterType("Litigation", [1f, 0f, 0f, 0f]);
        var other = MatterType("Bankruptcy", [0f, 0f, 1f, 0f]);
        var area = PracticeArea("Appellate", [1f, 0f, 0f, 0f]);
        var admin = Guid.NewGuid();

        var outcome = await Sut().ConfirmAsync(CodeSetReferenceKind.MatterType, target, _litigation, admin);

        outcome.Should().Be(CodeSetConfirmOutcome.Confirmed);
        var update = _data.Updates.Should().ContainSingle().Subject;
        update.Entity.Should().Be("sprk_mattertype_ref");
        update.Id.Should().Be(target);
        update.Fields.Should().ContainSingle().Which.Key.Should().Be(DefaultSetColumn);
        update.Fields[DefaultSetColumn].Should().BeEquivalentTo(new EntityReference("sprk_billingcodeset", _litigation));
        _data.Find("sprk_mattertype_ref", target)!.GetAttributeValue<EntityReference>(DefaultSetColumn).Id.Should().Be(_litigation);
        _data.Find("sprk_mattertype_ref", other)!.Contains(DefaultSetColumn).Should().BeFalse();
        _data.Find("sprk_practicearea_ref", area)!.Contains(DefaultSetColumn).Should().BeFalse();
    }

    [Fact]
    public async Task Confirm_ForAPracticeArea_WritesThePracticeAreaTable()
    {
        var area = PracticeArea("Intellectual Property Patents", [0f, 1f, 0f, 0f]);

        var outcome = await Sut().ConfirmAsync(CodeSetReferenceKind.PracticeArea, area, _ip, null);

        outcome.Should().Be(CodeSetConfirmOutcome.Confirmed);
        _data.Updates.Single().Entity.Should().Be("sprk_practicearea_ref");
    }

    [Fact]
    public async Task Confirm_AfterTheRowGainedAMappingSinceTheProposal_Returns409Outcome_AndWritesNothing()
    {
        var row = MatterType("Litigation", [1f, 0f, 0f, 0f]);
        var sut = Sut();
        (await sut.ProposeAsync()).Proposals.Should().ContainSingle(p => p.ReferenceId == row);

        // Another admin confirms first.
        _data.Apply("sprk_mattertype_ref", row, new Dictionary<string, object> { [DefaultSetColumn] = new EntityReference("sprk_billingcodeset", _bankruptcy) });

        var outcome = await sut.ConfirmAsync(CodeSetReferenceKind.MatterType, row, _litigation, null);

        outcome.Should().Be(CodeSetConfirmOutcome.AlreadyMapped);
        _data.Updates.Should().BeEmpty("the other admin's mapping is not overwritten");
        _data.Find("sprk_mattertype_ref", row)!.GetAttributeValue<EntityReference>(DefaultSetColumn).Id.Should().Be(_bankruptcy);
    }

    [Fact]
    public async Task Confirm_WhenAnotherAdminMapsTheRowBetweenTheReReadAndTheWrite_Returns409Outcome_AndKeepsTheOtherMapping()
    {
        // The 048 lost update (fixed in task 054): the re-read saw the row unmapped, another admin's mapping landed before the
        // write, and an unconditional write replaced it. The write is now conditional on the version the re-read saw.
        var row = MatterType("Litigation", [1f, 0f, 0f, 0f]);
        var versionRead = (long)_data.Find("sprk_mattertype_ref", row)!["versionnumber"];
        var raced = false;
        _data.AfterQuery = query =>
        {
            if (!raced && query.EntityName == "sprk_mattertype_ref")
            {
                raced = true;
                _data.Apply("sprk_mattertype_ref", row, new Dictionary<string, object> { [DefaultSetColumn] = new EntityReference("sprk_billingcodeset", _bankruptcy) });
            }
        };

        var outcome = await Sut().ConfirmAsync(CodeSetReferenceKind.MatterType, row, _litigation, null);

        raced.Should().BeTrue("the concurrent mapping landed after the confirm's re-read");
        outcome.Should().Be(CodeSetConfirmOutcome.AlreadyMapped);
        _data.Find("sprk_mattertype_ref", row)!.GetAttributeValue<EntityReference>(DefaultSetColumn).Id.Should().Be(
            _bankruptcy, "the other admin's mapping is not overwritten");
        _data.Updates.Should().BeEmpty();
        _data.ConditionalWrites.Should().ContainSingle().Which.Should().Be(("sprk_mattertype_ref", row, versionRead),
            "the write was conditional on the version the re-read saw, and Dataverse refused it");
    }

    [Fact]
    public async Task Confirm_WhenTheRowKeepsChangingButStaysUnmapped_ReturnsConcurrentChange_NotAlreadyMapped_AfterTheAttemptLimit()
    {
        // Task 054 fix-round-1 K4: every refused write was reported as AlreadyMapped ("already has a default code set") although the
        // row was still unmapped. Each re-read here is followed by an unrelated edit, so every conditional write meets a newer version.
        var row = MatterType("Litigation", [1f, 0f, 0f, 0f]);
        _data.AfterQuery = query =>
        {
            if (query.EntityName == "sprk_mattertype_ref" && query.TopCount == 1)
            {
                _data.Apply("sprk_mattertype_ref", row, new Dictionary<string, object> { ["sprk_mattertypename"] = "Litigation" });
            }
        };

        var outcome = await Sut().ConfirmAsync(CodeSetReferenceKind.MatterType, row, _litigation, null);

        outcome.Should().Be(CodeSetConfirmOutcome.ConcurrentChange);
        _data.ConditionalWrites.Should().HaveCount(CodeSetProposalService.MaxConfirmAttempts);
        _data.Updates.Should().BeEmpty();
        _data.Find("sprk_mattertype_ref", row)!.Contains(DefaultSetColumn).Should().BeFalse("nothing was written and nobody mapped it");
    }

    [Fact]
    public async Task Confirm_UnknownOrInactiveOrWrongTableReference_IsNotFound_AndWritesNothing()
    {
        var matterType = MatterType("Litigation", [1f, 0f, 0f, 0f]);
        var retired = MatterType("Retired", [1f, 0f, 0f, 0f], active: false);
        var sut = Sut();

        (await sut.ConfirmAsync(CodeSetReferenceKind.MatterType, Guid.NewGuid(), _litigation, null)).Should().Be(CodeSetConfirmOutcome.ReferenceNotFound);
        (await sut.ConfirmAsync(CodeSetReferenceKind.MatterType, retired, _litigation, null)).Should().Be(CodeSetConfirmOutcome.ReferenceNotFound);
        (await sut.ConfirmAsync(CodeSetReferenceKind.PracticeArea, matterType, _litigation, null)).Should().Be(CodeSetConfirmOutcome.ReferenceNotFound);

        _data.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task Confirm_UnknownInactiveOrTasklessCodeSet_IsNotFound_AndWritesNothing()
    {
        var row = MatterType("Litigation", [1f, 0f, 0f, 0f]);
        var inactive = _data.AddSet("UTBMS Trademark", active: false);
        _data.AddCode(inactive, "T100", BillingCodeKind.Task);
        var sut = Sut();

        foreach (var codeSetId in new[] { Guid.NewGuid(), Guid.Empty, inactive, _activity })
        {
            (await sut.ConfirmAsync(CodeSetReferenceKind.MatterType, row, codeSetId, null))
                .Should().Be(CodeSetConfirmOutcome.CodeSetNotFound, because: codeSetId.ToString());
        }

        _data.Updates.Should().BeEmpty();
    }

    // ── FR-22 step 4: the suggestion hook ───────────────────────────────────────

    [Fact]
    public async Task Suggest_WhenBothMatterTypeAndPracticeAreaAreUnmapped_ReturnsTheMatterTypeProposal_AndPersistsNothing()
    {
        var matterType = MatterType("Bankruptcy", [0f, 0f, 1f, 0f]);
        var area = PracticeArea("Appellate", [1f, 0f, 0f, 0f]);

        var suggested = await Sut().SuggestCodeSetAsync(Context(matterType, area));

        suggested.Should().Be(_bankruptcy, "the matter type is asked first");
        _data.Updates.Should().BeEmpty("a per-line suggestion is never a mapping");
    }

    [Fact]
    public async Task Suggest_WithAnUnmappedMatterTypeAndNoPracticeArea_OrTheReverse_ProposesForTheOneThatIsSet()
    {
        var matterType = MatterType("Bankruptcy", [0f, 0f, 1f, 0f]);
        var area = PracticeArea("Intellectual Property Patents", [0f, 1f, 0f, 0f]);
        var sut = Sut();

        (await sut.SuggestCodeSetAsync(Context(matterType, null))).Should().Be(_bankruptcy);
        (await sut.SuggestCodeSetAsync(Context(null, area))).Should().Be(_ip);
    }

    [Fact]
    public async Task Suggest_WhenEitherRecordLookupIsMapped_ReturnsNull_WithoutCallingTheAi()
    {
        var mappedType = MatterType("Litigation", [1f, 0f, 0f, 0f], mapped: _litigation);
        var unmappedType = MatterType("Bankruptcy", [0f, 0f, 1f, 0f]);
        var mappedArea = PracticeArea("Appellate", [1f, 0f, 0f, 0f], mapped: _litigation);
        var unmappedArea = PracticeArea("Intellectual Property Patents", [0f, 1f, 0f, 0f]);
        var sut = Sut();

        (await sut.SuggestCodeSetAsync(Context(mappedType, unmappedArea))).Should().BeNull();
        (await sut.SuggestCodeSetAsync(Context(unmappedType, mappedArea))).Should().BeNull();
        (await sut.SuggestCodeSetAsync(Context(mappedType, mappedArea))).Should().BeNull();
        _embedded.Should().BeEmpty();
    }

    [Fact]
    public async Task Suggest_WithNoMatterTypeAndNoPracticeArea_ReturnsNull()
    {
        (await Sut().SuggestCodeSetAsync(Context(null, null))).Should().BeNull();
        _embedded.Should().BeEmpty();
    }

    [Fact]
    public async Task Suggest_WhenTheReferenceRowNoLongerExists_ReturnsNull()
    {
        (await Sut().SuggestCodeSetAsync(Context(Guid.NewGuid(), null))).Should().BeNull();
    }

    [Fact]
    public async Task Resolver_UsesTheSuggestion_FlaggedForReview_AndTheMappingStaysEmpty()
    {
        var matterType = MatterType("Bankruptcy", [0f, 0f, 1f, 0f]);
        var area = PracticeArea("Appellate", [1f, 0f, 0f, 0f]);
        var core = _data.AddCoreRecord("sprk_matter", matterTypeId: matterType, practiceAreaId: area);
        var settings = new Mock<IEbillingSettingsReader>();
        settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new EbillingSettings());
        var resolver = new CodeSetResolver(_data.Entities.Object, settings.Object, Sut(), NullLogger<CodeSetResolver>.Instance);

        var resolved = await resolver.ResolveAsync(core, [new InvoiceLineToCode { LineKey = "1", LineType = InvoiceLineType.Fee, Narrative = "Filed a proof of claim" }]);

        resolved.Source.Should().Be(CodeSetSource.AiSuggestion);
        resolved.CodeSetId.Should().Be(_bankruptcy);
        resolved.NeedsReview.Should().BeTrue();
        _data.Updates.Should().BeEmpty();
        _data.Find("sprk_mattertype_ref", matterType)!.Contains(DefaultSetColumn).Should().BeFalse();
    }

    private static CodeSetSuggestionContext Context(Guid? matterTypeId, Guid? practiceAreaId) =>
        new(new CodingCoreRecord("sprk_matter", Guid.NewGuid()), matterTypeId, practiceAreaId, []);
}

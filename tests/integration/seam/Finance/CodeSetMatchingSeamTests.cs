using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Coding;
using Sprk.Bff.Api.Services.Finance.Models;
using Sprk.Bff.Api.Tests.Seam.ContentUnderstanding;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Finance;

/// <summary>
/// ADR-043 vertical-slice seam for code-set matching (ebilling-r1 task 054, FR-22, D-93). <see cref="CodeSetProposalService"/> runs with
/// the PRODUCTION <see cref="InvoiceAi"/>, <see cref="ActionResolver"/>, <see cref="ActionRunner"/> and <see cref="PromptSchemaRenderer"/>,
/// loaded with the REAL authored Action (<c>infra/dataverse/actions/invoice-codeset-match.action.json</c> as the runtime stores it, plus
/// its output-schema mirror). Only the Dataverse boundary (<see cref="FakeCodingReferenceData"/>), the Binding and Action-row reads and
/// the model (<see cref="IOpenAiClient"/>: embeddings with fixed vectors, completions answered by the test) are doubled.
/// </summary>
/// <remarks>
/// <b>Anti-stub.</b> What the model "sees" is read back out of the rendered prompt's <c>## Input</c> section and the fake model answers
/// from THAT, so a service that sent the wrong candidates or examples fails here. The fake model is also made to misbehave (an id that
/// is not a candidate, a set name instead of an id, an empty answer, a failed call): the guard and the fallback are the controls under
/// test. The live model's accuracy is <see cref="CodeSetMatchingLiveTests"/>' job.
/// </remarks>
public sealed class CodeSetMatchingSeamTests
{
    private const string DefaultSetColumn = "sprk_defaultbillingcodeset";
    private static readonly Guid ActionId = Guid.Parse("54545454-5454-5454-5454-545454545454");

    // Embedding axes: litigation, patents, bankruptcy, counseling, project (transactional), trademark.
    private readonly Harness _h = new();

    // ── The authored Action, end to end ────────────────────────────────────────

    [Fact]
    public async Task Propose_ThroughTheAuthoredAction_SendsTheRowCandidateProfilesAndExamples_AndProposesTheReviewedChoice()
    {
        var commercial = _h.MatterType("Commercial", [0.1f, 0f, 0.2f, 0f, 0.6f, 0.4f]);
        _h.PracticeArea("Appellate", [1f, 0f, 0f, 0f, 0f, 0f], mapped: _h.Litigation);
        _h.Respond = input => Answer(input.GetProperty("candidates").EnumerateArray()
            .Single(c => c.GetProperty("name").GetString() == "UTBMS Project").GetProperty("codeSetId").GetString()!, 0.86, "Commercial matters are contract and deal work.");

        var list = await _h.Sut.ProposeAsync();

        // What reached the model.
        var prompt = _h.Prompts.Should().ContainSingle().Subject;
        prompt.Should().Contain("legal operations specialist", "the authored role is rendered").And.Contain("## Input");
        var input = AuthoredActionMirror.InputSection(prompt);
        input.GetProperty("reference").GetProperty("type").GetString().Should().Be("MatterType");
        input.GetProperty("reference").GetProperty("name").GetString().Should().Be("Commercial");
        var candidates = input.GetProperty("candidates").EnumerateArray().ToList();
        candidates.Select(c => c.GetProperty("name").GetString()).Should().BeEquivalentTo(
            ["UTBMS Litigation", "UTBMS IP Patent Prosecution", "UTBMS Bankruptcy", "UTBMS Counseling", "UTBMS Project", "UTBMS Trademark"],
            "with six usable sets every one is a candidate; Activity and Expense (no Task codes) never are");
        candidates[0].GetProperty("name").GetString().Should().Be("UTBMS Project", "candidates are sent best-ranked first");
        var project = candidates[0];
        project.GetProperty("codeSetId").GetString().Should().Be(_h.Project.ToString());
        project.GetProperty("body").GetString().Should().Be("ABA");
        project.GetProperty("description").GetString().Should().Contain("transactional");
        project.GetProperty("matchHints").GetString().Should().Contain("mergers and acquisitions");
        project.GetProperty("taskCodes").EnumerateArray().Select(t => t.GetString()).Should().Equal("P100 Project Administration", "P210 Corporate Review");
        var example = input.GetProperty("examples").EnumerateArray().Should().ContainSingle().Subject;
        example.GetProperty("referenceType").GetString().Should().Be("PracticeArea");
        example.GetProperty("referenceName").GetString().Should().Be("Appellate");
        example.GetProperty("codeSetName").GetString().Should().Be("UTBMS Litigation", "a confirmed mapping is an example");
        _h.Temperatures.Should().Equal(0.0f);
        _h.Deployments.Should().Equal(["fast-deployment"], "the Action declares the Fast tier");
        JsonDocument.Parse(_h.Schemas[0]).RootElement.GetProperty("required").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo(["codeSetId", "confidence", "reason"]);

        // What came back.
        var proposal = list.Proposals.Should().ContainSingle().Subject;
        proposal.ReferenceId.Should().Be(commercial);
        proposal.Outcome.Should().Be(CodeSetMatchOutcome.Proposed);
        proposal.Reviewed.Should().BeTrue();
        proposal.Proposed!.CodeSetId.Should().Be(_h.Project);
        proposal.Proposed.Score.Should().BeGreaterThan(0m);
        proposal.RunnerUp!.CodeSetId.Should().NotBe(_h.Project);
        proposal.Closest.Should().BeNull();
        proposal.Confidence.Should().Be(0.86m);
        proposal.Rationale.Should().Be("Commercial matters are contract and deal work.");
        list.Coverage.MatterTypes.Should().Be(new CodeSetCoverageCount(Active: 1, Mapped: 0, Proposed: 1, NoneFits: 0, Unreviewed: 0, Failed: 0));
        list.Coverage.PracticeAreas.Should().Be(new CodeSetCoverageCount(Active: 1, Mapped: 1, Proposed: 0, NoneFits: 0, Unreviewed: 0, Failed: 0));
        _h.Data.Updates.Should().BeEmpty("proposing writes nothing (FR-22)");
        _h.Data.ConditionalWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task Propose_WhenTheActionAnswersNone_TheRowIsNoneFits_WithNoConfirmableProposal_AndTheClosestSetAsContextOnly()
    {
        var catering = _h.PracticeArea("Office Catering", [0.1f, 0f, 0f, 0.2f, 0.3f, 0f]);
        _h.Respond = _ => Answer("none", 0.9, "Catering is not legal work billed by outside counsel.");

        var list = await _h.Sut.ProposeAsync();

        var row = list.Proposals.Should().ContainSingle().Subject;
        row.ReferenceId.Should().Be(catering);
        row.Outcome.Should().Be(CodeSetMatchOutcome.NoneFits);
        row.Proposed.Should().BeNull("a none-fits row never carries a proposal an admin could confirm by mistake (D-93)");
        row.RunnerUp.Should().BeNull();
        row.Closest!.CodeSetId.Should().Be(_h.Project, "the most similar set is named as context");
        row.Reviewed.Should().BeTrue();
        row.Confidence.Should().Be(0.9m);
        row.Rationale.Should().Contain("Catering is not legal work").And.Contain("by hand").And.Contain("add a code set");
        list.Coverage.PracticeAreas.Should().Be(new CodeSetCoverageCount(Active: 1, Mapped: 0, Proposed: 0, NoneFits: 1, Unreviewed: 0, Failed: 0));
    }

    public static TheoryData<string> OutOfCandidateAnswers => new() { "activity-set-id", "unknown-id", "set-name", "empty" };

    [Theory]
    [MemberData(nameof(OutOfCandidateAnswers))]
    public async Task Propose_AnAnswerThatIsNotACandidateId_IsRefused_AndTheRowFallsBackToTheEmbeddingRanking_Unreviewed(string answer)
    {
        var employment = _h.MatterType("Employment", [0.3f, 0f, 0f, 0.9f, 0.1f, 0f]);
        _h.Respond = _ => answer switch
        {
            "activity-set-id" => Answer(_h.Activity.ToString(), 0.99, "Activity codes fit everything."),
            "unknown-id" => Answer(Guid.NewGuid().ToString(), 0.99, "Invented."),
            "set-name" => Answer("UTBMS Counseling", 0.99, "Named, not an id."),
            _ => Answer("", 0.99, "Empty."),
        };

        var list = await _h.Sut.ProposeAsync();

        var row = list.Proposals.Should().ContainSingle().Subject;
        row.ReferenceId.Should().Be(employment);
        row.Outcome.Should().Be(CodeSetMatchOutcome.Proposed, "a refused answer never becomes a fabricated none-fits");
        row.Reviewed.Should().BeFalse();
        row.Confidence.Should().BeNull();
        row.Proposed!.CodeSetId.Should().Be(_h.Counseling, "the embedding ranking's best set");
        row.Proposed.CodeSetId.Should().NotBe(_h.Activity);
        row.Rationale.Should().StartWith("Not reviewed by AI").And.Contain("low confidence");
        list.Coverage.MatterTypes.Unreviewed.Should().Be(1);
    }

    public static TheoryData<string> ActionFailures => new() { "no-binding", "completion-throws", "linear-consumers-off" };

    [Theory]
    [MemberData(nameof(ActionFailures))]
    public async Task Propose_WhenTheActionFailsOrIsUnavailable_EveryRowIsStillListed_AsAnUnreviewedEmbeddingProposal(string failure)
    {
        _h.MatterType("Litigation", [1f, 0f, 0f, 0f, 0f, 0f]);
        _h.PracticeArea("Intellectual Property Patents", [0f, 1f, 0f, 0f, 0f, 0.4f]);
        switch (failure)
        {
            case "no-binding":
                _h.Routing.Setup(r => r.ResolveActionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IRoutingContext?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((Guid?)null);
                break;
            case "completion-throws":
                _h.Respond = _ => throw new HttpRequestException("model down");
                break;
            default:
                _h.Respond = _ => throw new FeatureDisabledException("ai.linear-consumers.disabled", "linear consumers off");
                break;
        }

        var list = await _h.Sut.ProposeAsync();

        list.Proposals.Should().HaveCount(2, "the review step never fails the list");
        list.Proposals.Should().OnlyContain(p => p.Outcome == CodeSetMatchOutcome.Proposed && !p.Reviewed && p.Confidence == null);
        list.Proposals.Select(p => p.Proposed!.CodeSetId).Should().Equal(_h.Litigation, _h.Patent);
        list.Coverage.MatterTypes.Unreviewed.Should().Be(1);
        list.Coverage.PracticeAreas.Unreviewed.Should().Be(1);
    }

    [Theory]
    [InlineData("top-candidate")]
    [InlineData("fifth-ranked")]
    public async Task Propose_WithMoreUsableSetsThanTheLimit_SendsOnlyTheTopRankedCandidates_AndAcceptsOnlyThose(string answer)
    {
        _h.TaskSet("UTBMS Extra One", [0f, 0f, 0f, 0f, 0f, 0.1f]);
        _h.TaskSet("UTBMS Extra Two", [0f, 0f, 0f, 0f, 0f, 0.05f]);
        _h.MatterType("Litigation", [1f, 0.2f, 0.1f, 0.05f, 0f, 0f]);

        // Ranked for this row: Litigation, Counseling, IP Patent Prosecution, Bankruptcy | Trademark (5th), Project, the two extras.
        // Trademark is a usable set of this environment, so a guard that checked "any usable set" would accept it (fix-round-1 F3).
        _h.Respond = input => answer == "top-candidate"
            ? Answer(input.GetProperty("candidates")[0].GetProperty("codeSetId").GetString()!, 0.9, "Disputes.")
            : Answer(_h.Trademark.ToString(), 0.9, "Trademark work.");

        var list = await _h.Sut.ProposeAsync();

        AuthoredActionMirror.InputSection(_h.Prompts.Single()).GetProperty("candidates").EnumerateArray()
            .Select(c => c.GetProperty("name").GetString())
            .Should().Equal(
                ["UTBMS Litigation", "UTBMS Counseling", "UTBMS IP Patent Prosecution", "UTBMS Bankruptcy"],
                $"eight usable sets exceed {CodeSetProposalService.AllCandidatesUpTo}, so only the top {CodeSetProposalService.TopCandidates} by similarity go");
        var row = list.Proposals.Should().ContainSingle().Subject;
        row.Proposed!.CodeSetId.Should().Be(_h.Litigation);
        if (answer == "top-candidate")
        {
            row.Reviewed.Should().BeTrue();
        }
        else
        {
            row.Reviewed.Should().BeFalse("the 5th-ranked set is usable but was not one of THIS request's candidates, so the answer is refused");
            row.Confidence.Should().BeNull();
            row.Rationale.Should().StartWith("Not reviewed by AI");
        }
    }

    [Fact]
    public async Task Propose_EmbedsEachSetsProfile_FromItsDescriptionHintsAndActiveTaskCodes_NotJustItsName()
    {
        _h.MatterType("Commercial", [0f, 0f, 0f, 0f, 1f, 0f]);
        _h.Respond = input => Answer(input.GetProperty("candidates")[0].GetProperty("codeSetId").GetString()!, 0.9, "Deals.");

        await _h.Sut.ProposeAsync();

        var project = _h.ProfileEmbeddings().Should().ContainSingle(t => t.StartsWith("UTBMS Project (ABA)\n", StringComparison.Ordinal)).Subject;
        var lines = project.Split('\n');
        lines.Should().Contain("Task codes for transactional and project work.", "the admin-editable description is part of the profile");
        lines.Should().Contain("Fits: " + "Fits commercial transactions, mergers and acquisitions, banking and finance.");
        lines.Should().Contain("Task codes: P100 Project Administration; P210 Corporate Review", "active Task codes only, in code order");
        project.Should().NotContain("P900", "an inactive code is not part of what the set covers");
    }

    [Fact]
    public async Task Propose_SendsAtMostTheExampleLimit_AndNeverAMappingToAnUnusableSet()
    {
        var retired = _h.Data.AddSet("UTBMS Retired", active: false, body: "ABA");
        _h.Data.AddCode(retired, "R100", BillingCodeKind.Task, "Retired");
        _h.Data.AddMatterType(retired, "A mapped to a retired set");
        _h.Data.AddMatterType(_h.Activity, "B mapped to the activity set");
        for (var i = 0; i < 12; i++)
        {
            _h.PracticeArea($"Mapped {i:00}", [1f, 0f, 0f, 0f, 0f, 0f], mapped: _h.Litigation);
        }

        _h.MatterType("Commercial", [0f, 0f, 0f, 0f, 1f, 0f]);
        _h.Respond = input => Answer(input.GetProperty("candidates")[0].GetProperty("codeSetId").GetString()!, 0.9, "Deals.");

        await _h.Sut.ProposeAsync();

        var examples = AuthoredActionMirror.InputSection(_h.Prompts.Single()).GetProperty("examples").EnumerateArray().ToList();
        examples.Should().HaveCount(CodeSetProposalService.MaxExamples);
        examples.Select(e => e.GetProperty("codeSetName").GetString()).Should().OnlyContain(n => n == "UTBMS Litigation",
            "mappings to an inactive set or a set without Task codes would teach the model the wrong thing");
    }

    [Fact]
    public async Task Propose_SendsTheConfirmedMappingsMostSimilarToTheRow_NotTheFirstByName()
    {
        // Ten mappings that sort first by name but are about something else, and one, last by name, that is about the row's kind of work.
        for (var i = 0; i < CodeSetProposalService.MaxExamples; i++)
        {
            _h.PracticeArea($"A{i:00} Disputes", [1f, 0f, 0f, 0f, 0f, 0f], mapped: _h.Litigation);
        }

        _h.PracticeArea("Zz Corporate Deals", [0f, 0f, 0f, 0f, 1f, 0.1f], mapped: _h.Project);
        _h.MatterType("Commercial", [0f, 0f, 0.05f, 0f, 1f, 0f]);
        _h.Respond = input => Answer(input.GetProperty("candidates")[0].GetProperty("codeSetId").GetString()!, 0.9, "Deals.");

        await _h.Sut.ProposeAsync();

        var examples = AuthoredActionMirror.InputSection(_h.Prompts.Single()).GetProperty("examples").EnumerateArray().ToList();
        examples.Should().HaveCount(CodeSetProposalService.MaxExamples);
        examples[0].GetProperty("referenceName").GetString().Should().Be("Zz Corporate Deals", "the most similar precedent comes first");
        examples[0].GetProperty("codeSetName").GetString().Should().Be("UTBMS Project");
    }

    // ── Parallel matching, isolation, logging (task 054 fix-round-1) ────────────

    [Fact]
    public async Task Propose_MatchesSeveralRowsAtOnce_ButNeverMoreThanTheParallelismLimit()
    {
        for (var i = 1; i <= 9; i++)
        {
            _h.MatterType($"Row {i}", [1f, 0f, 0f, 0f, 0f, 0f]);
        }

        var calls = 0;
        var inFlight = 0;
        var maxInFlight = 0;
        _h.RespondAsync = async input =>
        {
            var answer = Answer(input.GetProperty("candidates")[0].GetProperty("codeSetId").GetString()!, 0.9, "Disputes.");
            if (Interlocked.Increment(ref calls) == 1)
            {
                return answer; // the probe row is matched alone
            }

            // Hold every call open long enough that all rows the implementation lets start are in flight together: sequential gives 1,
            // bounded gives the limit, unbounded gives all eight.
            InterlockedMax(ref maxInFlight, Interlocked.Increment(ref inFlight));
            await Task.Delay(250);
            Interlocked.Decrement(ref inFlight);
            return answer;
        };

        var list = await _h.Sut.ProposeAsync();

        CodeSetProposalService.MatchParallelism.Should().Be(4, "the comment on the constant explains why four");
        maxInFlight.Should().Be(4, "rows are matched in parallel (more than one at a time), bounded by the limit");
        list.Proposals.Should().HaveCount(9).And.OnlyContain(p => p.Reviewed);
    }

    [Fact]
    public async Task Propose_KeepsTheRowOrder_WhenLaterRowsFinishFirst()
    {
        string[] names = ["Row A", "Row B", "Row C", "Row D", "Row E", "Row F"];
        foreach (var name in names)
        {
            _h.MatterType(name, [1f, 0f, 0f, 0f, 0f, 0f]);
        }

        _h.RespondAsync = async input =>
        {
            var index = Array.IndexOf(names, input.GetProperty("reference").GetProperty("name").GetString());
            await Task.Delay((names.Length - index) * 40); // the later the row, the sooner it finishes
            return Answer(input.GetProperty("candidates")[0].GetProperty("codeSetId").GetString()!, 0.9, "Disputes.");
        };

        var list = await _h.Sut.ProposeAsync();

        list.Proposals.Select(p => p.ReferenceName).Should().Equal(names);
    }

    [Fact]
    public async Task Propose_WhenOneRowCannotBeMatched_ListsItAsFailed_AndTheOtherRowsAreUnaffected()
    {
        _h.MatterType("Alpha Litigation", [1f, 0f, 0f, 0f, 0f, 0f]);
        var broken = _h.Data.AddMatterType(null, "Broken Row"); // no vector: embedding its name throws
        _h.MatterType("Gamma Bankruptcy", [0f, 0f, 1f, 0f, 0f, 0f]);
        _h.Respond = input => Answer(input.GetProperty("candidates")[0].GetProperty("codeSetId").GetString()!, 0.9, "Fits.");

        var list = await _h.Sut.ProposeAsync();

        list.Proposals.Select(p => p.ReferenceName).Should().Equal("Alpha Litigation", "Broken Row", "Gamma Bankruptcy");
        var failed = list.Proposals[1];
        failed.ReferenceId.Should().Be(broken);
        failed.Outcome.Should().Be(CodeSetMatchOutcome.Failed);
        failed.Proposed.Should().BeNull("a failed row carries nothing that could be confirmed");
        failed.Reviewed.Should().BeFalse();
        failed.Rationale.Should().Contain("by hand");
        list.Proposals[0].Should().Match<CodeSetProposal>(p => p.Outcome == CodeSetMatchOutcome.Proposed && p.Reviewed && p.Proposed!.CodeSetId == _h.Litigation);
        list.Proposals[2].Should().Match<CodeSetProposal>(p => p.Outcome == CodeSetMatchOutcome.Proposed && p.Reviewed && p.Proposed!.CodeSetId == _h.Bankruptcy);
        list.Coverage.MatterTypes.Should().Be(new CodeSetCoverageCount(Active: 3, Mapped: 0, Proposed: 2, NoneFits: 0, Unreviewed: 0, Failed: 1));
    }

    [Fact]
    public async Task Propose_WhenTheRequestIsCancelled_Throws_InsteadOfListingRowsAsFailed()
    {
        for (var i = 1; i <= 9; i++)
        {
            _h.MatterType($"Row {i}", [1f, 0f, 0f, 0f, 0f, 0f]);
        }

        using var cts = new CancellationTokenSource();
        var calls = 0;
        _h.Respond = input =>
        {
            if (Interlocked.Increment(ref calls) == 2)
            {
                cts.Cancel();
            }

            return Answer(input.GetProperty("candidates")[0].GetProperty("codeSetId").GetString()!, 0.9, "Disputes.");
        };

        var act = () => _h.Sut.ProposeAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Propose_WhenTheBindingIsMissing_ResolvesItOnce_AndLogsOneWarningForTheRequest_NotOnePerRow()
    {
        _h.MatterType("Litigation", [1f, 0f, 0f, 0f, 0f, 0f]);
        _h.MatterType("Bankruptcy", [0f, 0f, 1f, 0f, 0f, 0f]);
        _h.PracticeArea("Intellectual Property Patents", [0f, 1f, 0f, 0f, 0f, 0.4f]);
        _h.Routing.Setup(r => r.ResolveActionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IRoutingContext?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        var list = await _h.Sut.ProposeAsync();

        list.Proposals.Should().HaveCount(3).And.OnlyContain(p => p.Outcome == CodeSetMatchOutcome.Proposed && !p.Reviewed);
        list.Proposals.Select(p => p.Proposed!.CodeSetId).Should().Equal(_h.Bankruptcy, _h.Litigation, _h.Patent);
        _h.Routing.Verify(
            r => r.ResolveActionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IRoutingContext?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once(),
            "the first row finds the Action unavailable; the others do not ask again");
        var warning = _h.Log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning).Subject;
        warning.Message.Should().Contain("3 row(s)").And.Contain(InvoiceAiException.BindingMissing);
    }

    // ── Set-profile embedding cache (task 054 fix-round-1 K6) ───────────────────

    [Fact]
    public async Task Propose_ReusesCachedSetProfileAndNameEmbeddings_InTheNextRequest()
    {
        var cache = new InMemoryEmbeddingCache();
        _h.MatterType("Commercial", [0f, 0f, 0f, 0f, 1f, 0f]);
        _h.PracticeArea("Appellate", [1f, 0f, 0f, 0f, 0f, 0f], mapped: _h.Litigation);
        _h.Respond = input => Answer(input.GetProperty("candidates")[0].GetProperty("codeSetId").GetString()!, 0.9, "Deals.");

        var first = await _h.NewSut(cache).ProposeAsync();
        _h.ProfileEmbeddings().Should().HaveCount(6, "the first request embeds every usable set's profile");
        _h.EmbeddedTexts.Should().Contain(["Commercial", "Appellate"], "and the row's and the example's names");
        cache.Count.Should().Be(8);

        _h.EmbeddedTexts.Clear();
        var second = await _h.NewSut(cache).ProposeAsync();

        _h.EmbeddedTexts.Should().BeEmpty("a new request (new scope) reads the six profiles and both names from the cache");
        second.Proposals.Single().Proposed.Should().Be(first.Proposals.Single().Proposed, "the cached vectors rank exactly as the fresh ones");
        AuthoredActionMirror.InputSection(_h.Prompts[1]).GetProperty("examples")[0].GetProperty("referenceName").GetString().Should().Be("Appellate");
    }

    [Fact]
    public async Task Propose_ReEmbedsOnlyTheSetWhoseTaskCodeTextOrRowChanged()
    {
        var cache = new InMemoryEmbeddingCache();
        _h.MatterType("Commercial", [0f, 0f, 0f, 0f, 1f, 0f]);
        _h.Respond = input => Answer(input.GetProperty("candidates")[0].GetProperty("codeSetId").GetString()!, 0.9, "Deals.");
        await _h.NewSut(cache).ProposeAsync();

        // A code is edited; its set row (and so the set's modifiedon) is not touched.
        _h.Data.EditCode(_h.Project, "P100", "Project Management");
        _h.EmbeddedTexts.Clear();
        await _h.NewSut(cache).ProposeAsync();

        _h.ProfileEmbeddings().Should().ContainSingle().Which.Should()
            .StartWith("UTBMS Project (ABA)\n").And.Contain("P100 Project Management", "the edited Task-code text invalidates that set only");

        // The set row itself is saved (modifiedon moves) with the same text.
        _h.Data.Apply("sprk_billingcodeset", _h.Litigation, new Dictionary<string, object> { ["sprk_name"] = "UTBMS Litigation" });
        _h.EmbeddedTexts.Clear();
        await _h.NewSut(cache).ProposeAsync();

        _h.ProfileEmbeddings().Should().ContainSingle().Which.Should().StartWith("UTBMS Litigation (ABA)\n", "a newer modifiedon invalidates that set only");
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while ((seen = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, seen) != seen)
        {
        }
    }

    // ── FR-22 step 4: the per-line hook uses the same matcher ───────────────────

    [Fact]
    public async Task Suggest_WhenTheMatterTypeFitsNone_TriesThePracticeArea_AndWhenBothFitNone_ReturnsNoSuggestion()
    {
        var catering = _h.MatterType("Office Catering", [0f, 0f, 0f, 0.2f, 0.3f, 0f]);
        var patents = _h.PracticeArea("Intellectual Property Patents", [0f, 1f, 0f, 0f, 0f, 0.4f]);
        var vending = _h.PracticeArea("Vending Machines", [0f, 0f, 0f, 0.1f, 0.2f, 0f]);
        _h.Respond = input => input.GetProperty("reference").GetProperty("name").GetString() == "Intellectual Property Patents"
            ? Answer(_h.Patent.ToString(), 0.95, "Patent prosecution.")
            : Answer("none", 0.9, "Not legal work.");

        (await _h.Sut.SuggestCodeSetAsync(Context(catering, patents))).Should().Be(_h.Patent);
        (await _h.Sut.SuggestCodeSetAsync(Context(catering, vending))).Should().BeNull("a none-fits answer gives no suggestion (FR-22, D-93)");
        _h.Data.Updates.Should().BeEmpty("a per-line suggestion is never persisted as a mapping");
        _h.Data.ConditionalWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task Resolver_OnANoneFitsRecord_FallsToTheSettingsDefault_NotToAnAiSuggestion_AndWritesNothing()
    {
        var catering = _h.MatterType("Office Catering", [0f, 0f, 0f, 0.2f, 0.3f, 0f]);
        var core = _h.Data.AddCoreRecord("sprk_matter", matterTypeId: catering);
        _h.Respond = _ => Answer("none", 0.9, "Not legal work.");
        var settings = new Mock<IEbillingSettingsReader>();
        settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new EbillingSettings());
        var resolver = new CodeSetResolver(_h.Data.Entities.Object, settings.Object, _h.Sut, NullLogger<CodeSetResolver>.Instance);

        var resolved = await resolver.ResolveAsync(core, [new InvoiceLineToCode { LineKey = "1", LineType = InvoiceLineType.Fee, Narrative = "Ordered sandwiches" }]);

        resolved.Source.Should().Be(CodeSetSource.SettingsDefault);
        resolved.CodeSetId.Should().Be(_h.Litigation, "the settings fallback is the set named UTBMS Litigation");
        _h.Data.Updates.Should().BeEmpty();
        _h.Data.Find("sprk_mattertype_ref", catering)!.Contains(DefaultSetColumn).Should().BeFalse();
    }

    [Fact]
    public async Task Resolver_OnAMatchedRecord_UsesTheReviewedSuggestion_FlaggedForReview()
    {
        var commercial = _h.MatterType("Commercial", [0.1f, 0f, 0.2f, 0f, 0.6f, 0.4f]);
        var core = _h.Data.AddCoreRecord("sprk_matter", matterTypeId: commercial);
        _h.Respond = _ => Answer(_h.Project.ToString(), 0.85, "Deals.");
        var settings = new Mock<IEbillingSettingsReader>();
        settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new EbillingSettings());
        var resolver = new CodeSetResolver(_h.Data.Entities.Object, settings.Object, _h.Sut, NullLogger<CodeSetResolver>.Instance);

        var resolved = await resolver.ResolveAsync(core, [new InvoiceLineToCode { LineKey = "1", LineType = InvoiceLineType.Fee, Narrative = "Negotiated the supply agreement" }]);

        resolved.Source.Should().Be(CodeSetSource.AiSuggestion);
        resolved.NeedsReview.Should().BeTrue();
        resolved.CodeSetId.Should().Be(_h.Project);
        _h.Data.Updates.Should().BeEmpty();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static string Answer(string codeSetId, double confidence, string reason) =>
        JsonSerializer.Serialize(new { codeSetId, confidence, reason });

    private static CodeSetSuggestionContext Context(Guid? matterTypeId, Guid? practiceAreaId) =>
        new(new CodingCoreRecord("sprk_matter", Guid.NewGuid()), matterTypeId, practiceAreaId, []);

    /// <summary>The service's own log, captured (thread-safe: rows are matched in parallel).</summary>
    private sealed class CapturingLogger : ILogger<CodeSetProposalService>
    {
        public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Enqueue((logLevel, formatter(state, exception)));
    }

    /// <summary>An in-memory <see cref="IEmbeddingCache"/> with the production key derivation (SHA-256 of the content).</summary>
    private sealed class InMemoryEmbeddingCache : IEmbeddingCache
    {
        private readonly ConcurrentDictionary<string, float[]> _entries = new(StringComparer.Ordinal);

        public int Count => _entries.Count;

        public Task<ReadOnlyMemory<float>?> GetEmbeddingAsync(string contentHash, CancellationToken cancellationToken = default) =>
            Task.FromResult(_entries.TryGetValue(contentHash, out var vector) ? new ReadOnlyMemory<float>(vector) : (ReadOnlyMemory<float>?)null);

        public Task SetEmbeddingAsync(string contentHash, ReadOnlyMemory<float> embedding, CancellationToken cancellationToken = default)
        {
            _entries[contentHash] = embedding.ToArray();
            return Task.CompletedTask;
        }

        public string ComputeContentHash(string content) =>
            Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

        public Task<ReadOnlyMemory<float>?> GetEmbeddingForContentAsync(string content, CancellationToken cancellationToken = default) =>
            GetEmbeddingAsync(ComputeContentHash(content), cancellationToken);

        public Task SetEmbeddingForContentAsync(string content, ReadOnlyMemory<float> embedding, CancellationToken cancellationToken = default) =>
            SetEmbeddingAsync(ComputeContentHash(content), embedding, cancellationToken);
    }

    private sealed class Harness
    {
        private readonly Dictionary<string, float[]> _vectors = new(StringComparer.Ordinal);
        private readonly object _sync = new();
        private readonly InvoiceAi _ai;

        public Harness()
        {
            Litigation = TaskSet("UTBMS Litigation", [1f, 0f, 0f, 0f, 0f, 0f], "L110 Fact Investigation/Development");
            Patent = TaskSet("UTBMS IP Patent Prosecution", [0f, 1f, 0f, 0f, 0f, 0.3f], "110101 Become Familiar with Invention/Idea", body: "LOC");
            Bankruptcy = TaskSet("UTBMS Bankruptcy", [0f, 0f, 1f, 0f, 0.2f, 0f], "B110 Case Administration");
            Counseling = TaskSet("UTBMS Counseling", [0.2f, 0f, 0f, 1f, 0f, 0f], "C100 Fact Gathering");
            Project = AddProject();
            Trademark = TaskSet("UTBMS Trademark", [0f, 0.3f, 0f, 0f, 0f, 1f], "TR110 Fact Investigation and Development", body: "LOC");
            Activity = Data.AddSet("UTBMS Activity", body: "ABA");
            Data.AddCode(Activity, "A101", BillingCodeKind.Activity, "Plan and prepare for");
            _vectors["UTBMS Activity (ABA)"] = [1f, 1f, 1f, 1f, 1f, 1f];
            var expense = Data.AddSet("UTBMS Expense", body: "ABA");
            Data.AddCode(expense, "E101", BillingCodeKind.Expense, "Copying");
            _vectors["UTBMS Expense (ABA)"] = [1f, 1f, 1f, 1f, 1f, 1f];

            Model.Setup(m => m.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string text, string? _, int? _, CancellationToken _) =>
                {
                    lock (_sync)
                    {
                        EmbeddedTexts.Add(text);
                        return new ReadOnlyMemory<float>(_vectors.TryGetValue(text.Split('\n')[0], out var v)
                            ? v
                            : throw new InvalidOperationException($"The test has no vector for '{text}'."));
                    }
                });
            Routing.Setup(r => r.ResolveActionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IRoutingContext?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ActionId);
            Catalog.Setup(s => s.GetActionAsync(ActionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AnalysisAction
                {
                    Id = ActionId,
                    Name = "Invoice Code Set Match",
                    SystemPrompt = AuthoredActionMirror.SystemPrompt(ConsumerTypes.InvoiceCodeSetMatch),
                    OutputSchemaJson = AuthoredActionMirror.OutputSchema(ConsumerTypes.InvoiceCodeSetMatch),
                    Temperature = 0.0m,
                    ModelTier = AiModelTier.Fast,
                });
            Model.Setup(o => o.GetStructuredCompletionRawAsync(
                    It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(), It.IsAny<string?>(),
                    It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()))
                .Returns((string prompt, BinaryData schema, string _, string? deployment, int? _, float? temperature, CancellationToken _) =>
                {
                    lock (_sync)
                    {
                        Prompts.Add(prompt);
                        Schemas.Add(schema);
                        Temperatures.Add(temperature);
                        Deployments.Add(deployment);
                    }

                    try
                    {
                        var input = AuthoredActionMirror.InputSection(prompt);
                        return RespondAsync is { } respondAsync ? respondAsync(input) : Task.FromResult(Respond(input));
                    }
                    catch (Exception ex)
                    {
                        return Task.FromException<string>(ex);
                    }
                });

            var runner = new ActionRunner(
                Model.Object,
                new PromptSchemaRenderer(NullLogger<PromptSchemaRenderer>.Instance),
                NullLogger<ActionRunner>.Instance,
                Options.Create(new DocumentIntelligenceOptions { FastModel = "fast-deployment", StandardModel = "standard-deployment" }));
            _ai = new InvoiceAi(
                new Mock<IPlaybookService>(MockBehavior.Strict).Object,
                Model.Object,
                new ActionResolver(Routing.Object, Catalog.Object, NullLogger<ActionResolver>.Instance),
                runner,
                ContentUnderstandingTestHost.Unconfigured(),
                NullLogger<InvoiceAi>.Instance);
            Sut = NewSut();
        }

        /// <summary>A fresh instance (a new request scope) over the same data and model, optionally with a cross-request embedding cache.</summary>
        public CodeSetProposalService NewSut(IEmbeddingCache? cache = null) =>
            new(Data.Entities.Object, Data.Records.Object, _ai, Log, cache);

        public CapturingLogger Log { get; } = new();

        /// <summary>Every text sent to the embedding model, in order.</summary>
        public List<string> EmbeddedTexts { get; } = [];

        /// <summary>The set-profile texts embedded so far (a profile always ends with its Task-codes line).</summary>
        public List<string> ProfileEmbeddings()
        {
            lock (_sync)
            {
                return EmbeddedTexts.Where(t => t.Contains("\nTask codes: ", StringComparison.Ordinal)).ToList();
            }
        }

        public void Vector(string text, float[] vector) => _vectors[text] = vector;

        private Guid AddProject()
        {
            var id = Data.AddSet(
                "UTBMS Project", body: "ABA",
                description: "Task codes for transactional and project work.",
                matchHints: "Fits commercial transactions, mergers and acquisitions, banking and finance.");
            Data.AddCode(id, "P210", BillingCodeKind.Task, "Corporate Review");
            Data.AddCode(id, "P100", BillingCodeKind.Task, "Project Administration");
            Data.AddCode(id, "P900", BillingCodeKind.Task, "Retired code", active: false);
            _vectors["UTBMS Project (ABA)"] = [0f, 0f, 0.1f, 0f, 1f, 0f];
            return id;
        }

        public FakeCodingReferenceData Data { get; } = new();

        public Guid Litigation { get; }

        public Guid Patent { get; }

        public Guid Bankruptcy { get; }

        public Guid Counseling { get; }

        public Guid Project { get; }

        public Guid Trademark { get; }

        public Guid Activity { get; }

        public Mock<IConsumerRoutingService> Routing { get; } = new();

        public Mock<IScopeResolverService> Catalog { get; } = new();

        public Mock<IOpenAiClient> Model { get; } = new();

        public CodeSetProposalService Sut { get; }

        public List<string> Prompts { get; } = [];

        public List<BinaryData> Schemas { get; } = [];

        public List<float?> Temperatures { get; } = [];

        public List<string?> Deployments { get; } = [];

        /// <summary>The fake model: given the parsed <c>## Input</c>, returns the JSON output (or throws).</summary>
        public Func<JsonElement, string> Respond { get; set; } = _ => throw new InvalidOperationException("The test set no answer.");

        /// <summary>An asynchronous fake model (takes precedence over <see cref="Respond"/>): lets a test hold calls open.</summary>
        public Func<JsonElement, Task<string>>? RespondAsync { get; set; }

        public Guid TaskSet(string name, float[] vector, string code = "X100 A task", string body = "ABA")
        {
            var id = Data.AddSet(name, body: body, description: $"Work covered by {name}.", matchHints: $"Fits {name} matters.");
            var space = code.IndexOf(' ');
            Data.AddCode(id, code[..space], BillingCodeKind.Task, code[(space + 1)..]);
            _vectors[$"{name} ({body})"] = vector;
            return id;
        }

        public Guid MatterType(string name, float[] vector, Guid? mapped = null)
        {
            _vectors[name] = vector;
            return Data.AddMatterType(mapped, name);
        }

        public Guid PracticeArea(string name, float[] vector, Guid? mapped = null)
        {
            _vectors[name] = vector;
            return Data.AddPracticeArea(mapped, name);
        }
    }
}

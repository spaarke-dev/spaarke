using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Resolution;

/// <summary>
/// <see cref="TimekeeperMatcher"/> (ebilling-r1 task 046, FR-23): exact, normalized, then AI-assisted matching of a fee line's timekeeper
/// to the firm's roster.
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): a wrong match attributes a billed hour to the wrong person, and an unapproved timekeeper billed under an
/// approved person's name is exactly what the ApprovedTimekeeper rule (task 071) exists to catch. The tests pin the failure direction:
/// when in doubt, unmatched. The embedding service is the <see cref="IInvoiceAi"/> double; its vectors are built so each cosine
/// similarity is the value the test names.
/// </remarks>
public sealed class TimekeeperMatcherTests
{
    private static readonly Guid Firm = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherFirm = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateOnly Day = new(2026, 6, 15);
    private const decimal Threshold = 0.90m;

    private readonly Mock<IInvoiceAi> _ai = new();
    private readonly Dictionary<string, float[]> _vectors = new(StringComparer.Ordinal);
    private readonly List<string> _embedded = [];

    public TimekeeperMatcherTests()
    {
        _ai.Setup(a => a.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns<string, string?, int?, CancellationToken>((text, _, _, _) =>
            {
                _embedded.Add(text);
                return Task.FromResult(new ReadOnlyMemory<float>(_vectors.TryGetValue(text, out var v) ? v : [0f, 0f, 0f, 1f]));
            });
    }

    private TimekeeperMatcher CreateMatcher() => new(_ai.Object, Mock.Of<ILogger<TimekeeperMatcher>>());

    private static RosterEntry Person(
        string name,
        Guid? id = null,
        Guid? firm = null,
        bool active = true,
        int status = (int)TimekeeperStatus.Approved,
        DateOnly? from = null,
        DateOnly? to = null,
        int? classification = 1) =>
        new(id ?? Guid.NewGuid(), firm ?? Firm, name, active, status, from, to, classification);

    private async Task<TimekeeperResolution> MatchOne(
        string? billed,
        IReadOnlyList<RosterEntry> roster,
        DateOnly? date = null,
        IReadOnlyList<string>? summary = null,
        decimal threshold = Threshold,
        CancellationToken ct = default)
    {
        var results = await CreateMatcher().MatchAsync(
            Firm, roster, summary ?? [], [new TimekeeperMatchRequest(billed, date ?? Day)], threshold, ct);
        return results.Should().ContainSingle().Subject;
    }

    /// <summary>A vector whose cosine similarity with <c>[1, 0, 0, 0]</c> is <paramref name="similarity"/>, leaning on the given axis.</summary>
    private static float[] Vec(double similarity, int axis = 1)
    {
        var v = new float[4];
        v[0] = (float)similarity;
        v[axis] = (float)Math.Sqrt(1 - similarity * similarity);
        return v;
    }

    private void GivenNameVector(string canonicalName) => _vectors[canonicalName] = [1f, 0f, 0f, 0f];

    // ── Stage 1: exact ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Exact_AnApprovedTimekeeperOfTheFirm_IsMatched_WithItsClassification()
    {
        var jane = Person("Jane Doe", classification: 7);

        var result = await MatchOne("Jane Doe", [jane, Person("John Roe")]);

        result.IsMatched.Should().BeTrue();
        result.TimekeeperId.Should().Be(jane.Id);
        result.Stage.Should().Be(TimekeeperMatchStage.Exact);
        result.MatchConfidence.Should().Be(1m);
        result.Classification.Should().Be(7);
        result.Reason.Should().Be(TimekeeperUnmatchedReason.None);
    }

    [Theory]
    [InlineData("JANE DOE")]
    [InlineData("jane doe")]
    [InlineData("  Jane   Doe  ")]
    public async Task Exact_IgnoresCaseAndSurroundingAndRepeatedWhitespace(string billed)
    {
        var jane = Person("Jane Doe");

        var result = await MatchOne(billed, [jane]);

        result.TimekeeperId.Should().Be(jane.Id);
        result.Stage.Should().Be(TimekeeperMatchStage.Exact);
    }

    // ── Stage 2: normalized ──────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Doe, Jane")]
    [InlineData("DOE,JANE")]
    [InlineData("Dr. Jane Doe")]
    [InlineData("Ms Jane Doe")]
    [InlineData("Jane Doe, Esq.")]
    [InlineData("Jane Doe Esquire")]
    [InlineData("Jane A. Doe")]
    [InlineData("Doe, Jane A.")]
    [InlineData("Jane Anne Doe")]
    public async Task Normalized_FoldsOrderHonorificsPostNominalsAndMiddleNames(string billed)
    {
        var jane = Person("Jane Doe");

        var result = await MatchOne(billed, [jane]);

        result.TimekeeperId.Should().Be(jane.Id, billed);
        result.Stage.Should().Be(TimekeeperMatchStage.Normalized);
        result.MatchConfidence.Should().Be(1m);
    }

    [Theory]
    [InlineData("José García", "Jose Garcia")]
    [InlineData("Jose Garcia", "José García")]
    [InlineData("Mary-Anne O'Brien", "Mary Anne OBrien")]
    [InlineData("Mary Anne O’Brien", "Mary-Anne O'Brien")]
    [InlineData("Jane A Doe", "Jane Anne Doe")]
    [InlineData("John Smith, Jr.", "John Smith Jr")]
    [InlineData("Smith, John, Jr.", "John Smith Jr")]
    public async Task Normalized_FoldsAccentsPunctuationAndHyphens(string billed, string rosterName)
    {
        var person = Person(rosterName);

        var result = await MatchOne(billed, [person]);

        result.TimekeeperId.Should().Be(person.Id, billed);
        result.Stage.Should().BeOneOf(TimekeeperMatchStage.Exact, TimekeeperMatchStage.Normalized);
    }

    [Fact]
    public async Task Normalized_AGenerationalSuffixTellsTwoPeopleApart()
    {
        var senior = Person("John Smith Sr");
        var junior = Person("John Smith Jr");

        var result = await MatchOne("Smith, John Jr.", [senior, junior]);

        result.TimekeeperId.Should().Be(junior.Id);
    }

    [Fact]
    public async Task Normalized_ASuffixOnOneSideOnly_IsNotTheSamePerson_AndTheModelIsNotAskedToBlurIt()
    {
        // "john smith jr" and "john smith" are nearly identical vectors; the guard keeps the model from being asked at all.
        GivenNameVector("john smith jr");
        _vectors["john smith"] = Vec(0.99);

        var result = await MatchOne("John Smith Jr", [Person("John Smith")]);

        result.IsMatched.Should().BeFalse();
        result.Reason.Should().Be(TimekeeperUnmatchedReason.NotOnRoster);
        _embedded.Should().BeEmpty();
    }

    [Fact]
    public async Task Normalized_TwoMiddleNamesThatDisagree_AreTwoPeople_AndTheModelIsNotAskedToBlurThem()
    {
        GivenNameVector("john b smith");
        _vectors["john a smith"] = Vec(0.99);

        var result = await MatchOne("John B Smith", [Person("John A Smith")]);

        result.IsMatched.Should().BeFalse();
        result.Reason.Should().Be(TimekeeperUnmatchedReason.NotOnRoster);
        _embedded.Should().BeEmpty();
    }

    [Fact]
    public async Task Ai_AMiddleNameOnlyOneSidePrints_DoesNotBlockACandidate()
    {
        var jonathan = Person("Jonathan Smith");
        GivenNameVector("jon a smith");
        _vectors["jonathan smith"] = Vec(0.96);

        var result = await MatchOne("Jon A Smith", [jonathan]);

        result.TimekeeperId.Should().Be(jonathan.Id);
        result.Stage.Should().Be(TimekeeperMatchStage.AiAssisted);
    }

    [Fact]
    public async Task Normalized_AnInitialIsNeverGuessedFromTheRoster()
    {
        // "J. Doe" fits Jane Doe and Jack Doe equally; picking either would be a guess.
        var result = await MatchOne("J. Doe", [Person("Jane Doe"), Person("Jack Doe")]);

        result.IsMatched.Should().BeFalse();
        result.Reason.Should().Be(TimekeeperUnmatchedReason.NameIncomplete);
        _embedded.Should().BeEmpty();
    }

    [Fact]
    public async Task Normalized_AnInitialDoesNotMatch_EvenWhenOnlyOneRosterPersonFits()
    {
        // A roster that holds Hannah Rosenthal must not absorb a Hector Rosenthal who is not on it.
        var result = await MatchOne("H. Rosenthal", [Person("Hannah Rosenthal")]);

        result.IsMatched.Should().BeFalse();
        result.Reason.Should().Be(TimekeeperUnmatchedReason.NameIncomplete);
    }

    [Fact]
    public async Task Ambiguous_TwoApprovedPeopleWithTheSameName_AreNotResolvedToEither()
    {
        var result = await MatchOne("Jane Doe", [Person("Jane Doe"), Person("Jane Doe")]);

        result.IsMatched.Should().BeFalse();
        result.Reason.Should().Be(TimekeeperUnmatchedReason.Ambiguous);
    }

    [Fact]
    public async Task Ambiguous_TwoApprovedPeopleWhoNormalizeToTheSameName_AreNotResolvedToEither()
    {
        var result = await MatchOne("Doe, Jane", [Person("Jane Doe"), Person("Dr. Jane Doe")]);

        result.IsMatched.Should().BeFalse();
        result.Reason.Should().Be(TimekeeperUnmatchedReason.Ambiguous);
    }

    // ── Initials expanded through the invoice's own timekeeper summary ───────────────────────────────────────────

    [Theory]
    [InlineData("DL")]
    [InlineData("D.L.")]
    [InlineData("D. L.")]
    [InlineData("Lockhart, D.")]
    [InlineData("D. Lockhart")]
    public async Task Summary_InitialsAreExpandedWhenExactlyOneSummaryPersonFits(string billed)
    {
        var david = Person("David Lockhart");

        var result = await MatchOne(billed, [david, Person("Dana Lopez")], summary: ["David Lockhart", "Maria Soto"]);

        result.TimekeeperId.Should().Be(david.Id, billed);
        result.Stage.Should().BeOneOf(TimekeeperMatchStage.Exact, TimekeeperMatchStage.Normalized);
    }

    [Fact]
    public async Task Summary_InitialsThatFitTwoSummaryPeople_AreAmbiguous_AndNeitherIsPicked()
    {
        var result = await MatchOne(
            "DL",
            [Person("David Lockhart"), Person("Dana Lopez")],
            summary: ["David Lockhart", "Dana Lopez"]);

        result.IsMatched.Should().BeFalse();
        result.Reason.Should().Be(TimekeeperUnmatchedReason.Ambiguous);
    }

    [Fact]
    public async Task Summary_TheSamePersonListedTwice_IsOnePerson()
    {
        var david = Person("David Lockhart");

        var result = await MatchOne("DL", [david], summary: ["David Lockhart", "Lockhart, David"]);

        result.TimekeeperId.Should().Be(david.Id);
    }

    [Fact]
    public async Task Summary_InitialsThatNoSummaryPersonFits_StayUnmatched_NotGuessedFromTheRoster()
    {
        var result = await MatchOne("DL", [Person("David Lockhart")], summary: ["Maria Soto"]);

        result.IsMatched.Should().BeFalse();
        result.Reason.Should().Be(TimekeeperUnmatchedReason.NameIncomplete);
    }

    [Fact]
    public async Task Summary_WithNoSummaryAtAll_InitialsAreIncomplete_AndCostNoModelCall()
    {
        var result = await MatchOne("DL", [Person("David Lockhart")]);

        result.Reason.Should().Be(TimekeeperUnmatchedReason.NameIncomplete);
        _ai.Verify(a => a.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Summary_ExpandsToAPersonWhoIsNotOnTheRoster_ThenTheLineIsNotOnRoster()
    {
        var result = await MatchOne("DL", [Person("Maria Soto")], summary: ["David Lockhart"]);

        result.IsMatched.Should().BeFalse();
        result.Reason.Should().Be(TimekeeperUnmatchedReason.NotOnRoster);
    }

    // ── Firm, status and date: who may be matched at all ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AnotherFirmsTimekeeper_IsNeverMatched_AndNeverEmbedded()
    {
        var result = await MatchOne("Jane Doe", [Person("Jane Doe", firm: OtherFirm)]);

        result.IsMatched.Should().BeFalse();
        result.Reason.Should().Be(TimekeeperUnmatchedReason.NotOnRoster);
        _embedded.Should().BeEmpty();
    }

    [Fact]
    public async Task ATimekeeperWithNoFirm_IsNeverMatched()
    {
        var result = await MatchOne("Jane Doe", [Person("Jane Doe") with { FirmId = null }]);

        result.IsMatched.Should().BeFalse();
    }

    [Theory]
    [InlineData((int)TimekeeperStatus.Pending)]
    [InlineData((int)TimekeeperStatus.Inactive)]
    [InlineData(999)]
    public async Task ATimekeeperWhoIsNotApproved_IsNeverMatched_AndTheReasonSaysSo(int status)
    {
        var result = await MatchOne("Jane Doe", [Person("Jane Doe", status: status)]);

        result.IsMatched.Should().BeFalse();
        result.Reason.Should().Be(TimekeeperUnmatchedReason.NotApprovedOnLineDate);
    }

    [Fact]
    public async Task ADeactivatedRow_IsNeverMatched_EvenIfItsStatusIsApproved()
    {
        var result = await MatchOne("Jane Doe", [Person("Jane Doe", active: false)]);

        result.IsMatched.Should().BeFalse();
        result.Reason.Should().Be(TimekeeperUnmatchedReason.NotApprovedOnLineDate);
    }

    [Fact]
    public async Task ARowWithNoStatus_IsNeverMatched()
    {
        var result = await MatchOne("Jane Doe", [Person("Jane Doe") with { StatusCode = null }]);

        result.IsMatched.Should().BeFalse();
    }

    [Fact]
    public async Task EffectiveFrom_IsInclusive_AndTheDayBeforeIsNotApproved()
    {
        var jane = Person("Jane Doe", from: Day);

        (await MatchOne("Jane Doe", [jane], Day)).TimekeeperId.Should().Be(jane.Id);

        var before = await MatchOne("Jane Doe", [jane], Day.AddDays(-1));
        before.IsMatched.Should().BeFalse();
        before.Reason.Should().Be(TimekeeperUnmatchedReason.NotApprovedOnLineDate);
    }

    [Fact]
    public async Task EffectiveTo_IsInclusive_AndTheDayAfterIsNotApproved()
    {
        var jane = Person("Jane Doe", to: Day);

        (await MatchOne("Jane Doe", [jane], Day)).TimekeeperId.Should().Be(jane.Id);

        var after = await MatchOne("Jane Doe", [jane], Day.AddDays(1));
        after.IsMatched.Should().BeFalse();
        after.Reason.Should().Be(TimekeeperUnmatchedReason.NotApprovedOnLineDate);
    }

    [Fact]
    public async Task TheSamePerson_IsMatchedOrNot_AccordingToTheDateOfEachLine()
    {
        var jane = Person("Jane Doe", from: new DateOnly(2026, 3, 1), to: new DateOnly(2026, 4, 30));
        var requests = new[]
        {
            new TimekeeperMatchRequest("Jane Doe", new DateOnly(2026, 2, 28)),
            new TimekeeperMatchRequest("Jane Doe", new DateOnly(2026, 3, 1)),
            new TimekeeperMatchRequest("Jane Doe", new DateOnly(2026, 4, 30)),
            new TimekeeperMatchRequest("Jane Doe", new DateOnly(2026, 5, 1)),
        };

        var results = await CreateMatcher().MatchAsync(Firm, [jane], [], requests, Threshold);

        results.Select(r => r.IsMatched).Should().Equal(false, true, true, false);
    }

    [Fact]
    public async Task ARosterPersonWhoIsNotApproved_IsNotReplacedByAnApprovedLookAlike_ThroughTheModel()
    {
        GivenNameVector("jonathan smith");
        _vectors["jonathan smyth"] = Vec(0.97);
        var pending = Person("Jonathan Smith", status: (int)TimekeeperStatus.Pending);
        var approved = Person("Jonathan Smyth");

        var result = await MatchOne("Jonathan Smith", [pending, approved]);

        result.IsMatched.Should().BeFalse();
        result.Reason.Should().Be(TimekeeperUnmatchedReason.NotApprovedOnLineDate);
        _embedded.Should().BeEmpty();
    }

    [Fact]
    public async Task ALineWithNoName_OrNoDate_IsUnmatchedWithItsOwnReason()
    {
        var jane = Person("Jane Doe");

        (await MatchOne(null, [jane])).Reason.Should().Be(TimekeeperUnmatchedReason.NameMissing);
        (await MatchOne("   ", [jane])).Reason.Should().Be(TimekeeperUnmatchedReason.NameMissing);

        var results = await CreateMatcher().MatchAsync(Firm, [jane], [], [new TimekeeperMatchRequest("Jane Doe", null)], Threshold);
        results.Single().Reason.Should().Be(TimekeeperUnmatchedReason.LineDateMissing);
    }

    [Fact]
    public async Task AnEmptyRoster_LeavesEveryLineUnmatched_WithoutAModelCall()
    {
        var result = await MatchOne("Jane Doe", []);

        result.Reason.Should().Be(TimekeeperUnmatchedReason.NotOnRoster);
        _embedded.Should().BeEmpty();
    }

    // ── Stage 3: AI-assisted ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Ai_ASimilarNameAtOrAboveTheThreshold_IsMatched_WithTheSimilarityAsItsConfidence()
    {
        var jonathan = Person("Jonathan Smith");
        GivenNameVector("jon smith");
        _vectors["jonathan smith"] = Vec(0.95);

        var result = await MatchOne("Jon Smith", [jonathan]);

        result.TimekeeperId.Should().Be(jonathan.Id);
        result.Stage.Should().Be(TimekeeperMatchStage.AiAssisted);
        result.MatchConfidence.Should().BeApproximately(0.95m, 0.0001m);
        result.Classification.Should().Be(jonathan.Classification);
    }

    [Fact]
    public async Task Ai_TheThreshold_IsInclusive()
    {
        // [1,0,0,0] against [3,4,0,0] is exactly 0.6.
        var jonathan = Person("Jonathan Smith");
        GivenNameVector("jon smith");
        _vectors["jonathan smith"] = [3f, 4f, 0f, 0f];

        (await MatchOne("Jon Smith", [jonathan], threshold: 0.6m)).Stage.Should().Be(TimekeeperMatchStage.AiAssisted);

        var above = await MatchOne("Jon Smith", [jonathan], threshold: 0.61m);
        above.IsMatched.Should().BeFalse();
        above.Reason.Should().Be(TimekeeperUnmatchedReason.AiBelowThreshold);
    }

    [Fact]
    public async Task Ai_ABestCandidateBelowTheThreshold_IsNotMapped_ToAnotherPerson()
    {
        GivenNameVector("jon smith");
        _vectors["jonathan smith"] = Vec(0.85);

        var result = await MatchOne("Jon Smith", [Person("Jonathan Smith")]);

        result.IsMatched.Should().BeFalse();
        result.TimekeeperId.Should().BeNull();
        result.Reason.Should().Be(TimekeeperUnmatchedReason.AiBelowThreshold);
        result.MatchConfidence.Should().Be(0m);
    }

    [Fact]
    public async Task Ai_TwoCandidatesTooCloseTogether_AreNotResolvedToEither()
    {
        GivenNameVector("jon smith");
        _vectors["jonathan smith"] = Vec(0.95, axis: 1);
        _vectors["johnny smith"] = Vec(0.92, axis: 2);

        var result = await MatchOne("Jon Smith", [Person("Jonathan Smith"), Person("Johnny Smith")]);

        result.IsMatched.Should().BeFalse();
        result.Reason.Should().Be(TimekeeperUnmatchedReason.AiNotClearlyAhead);
    }

    [Fact]
    public async Task Ai_ACandidateClearlyAheadOfTheSecond_IsMatched()
    {
        var jonathan = Person("Jonathan Smith");
        GivenNameVector("jon smith");
        _vectors["jonathan smith"] = Vec(0.97, axis: 1);
        _vectors["johnny smith"] = Vec(0.90, axis: 2);

        var result = await MatchOne("Jon Smith", [Person("Johnny Smith"), jonathan]);

        result.TimekeeperId.Should().Be(jonathan.Id);
        result.Stage.Should().Be(TimekeeperMatchStage.AiAssisted);
    }

    [Fact]
    public async Task Ai_TheLeadMustBeAtLeastTheMinimum()
    {
        var jonathan = Person("Jonathan Smith");
        GivenNameVector("jon smith");
        _vectors["jonathan smith"] = Vec(0.97, axis: 1);

        // A second candidate just inside the minimum lead leaves the line unmatched; just outside, it is matched.
        _vectors["johnny smith"] = Vec(0.97 - (double)TimekeeperMatcher.MinimumLead + 0.005, axis: 2);
        (await MatchOne("Jon Smith", [jonathan, Person("Johnny Smith")])).Reason
            .Should().Be(TimekeeperUnmatchedReason.AiNotClearlyAhead);

        _vectors["johnny smith"] = Vec(0.97 - (double)TimekeeperMatcher.MinimumLead - 0.005, axis: 2);
        (await MatchOne("Jon Smith", [jonathan, Person("Johnny Smith")])).TimekeeperId.Should().Be(jonathan.Id);
    }

    [Fact]
    public async Task Ai_OnlyApprovedCandidatesOfTheFirm_AreEmbeddedAndCompared()
    {
        GivenNameVector("jon smith");
        _vectors["jonathan smith"] = Vec(0.99);
        var otherFirmsJonathan = Person("Jonathan Smith", firm: OtherFirm);
        var inactiveJohnny = Person("Johnny Smith", status: (int)TimekeeperStatus.Inactive);
        var jonas = Person("Jonas Smith");
        _vectors["jonas smith"] = Vec(0.93, axis: 2);

        var result = await MatchOne("Jon Smith", [otherFirmsJonathan, inactiveJohnny, jonas]);

        result.TimekeeperId.Should().Be(jonas.Id);
        _embedded.Should().NotContain("jonathan smith").And.NotContain("johnny smith");
    }

    [Fact]
    public async Task Ai_CandidatesAreBlockedByFamilyName_SoOnlyPlausiblePeopleCostAModelCall()
    {
        GivenNameVector("jon smith");
        _vectors["jonathan smith"] = Vec(0.95);

        var result = await MatchOne(
            "Jon Smith",
            [Person("Jonathan Smith"), Person("Maria Gonzalez"), Person("Peter Wojciechowski"), Person("Ann Smithson")]);

        result.Stage.Should().Be(TimekeeperMatchStage.AiAssisted);
        _embedded.Should().BeEquivalentTo(["jon smith", "jonathan smith"]);
    }

    [Fact]
    public async Task Ai_WithNoPlausibleCandidate_IsNotOnRoster_WithoutAModelCall()
    {
        var result = await MatchOne("Jon Smith", [Person("Maria Gonzalez"), Person("Peter Wojciechowski")]);

        result.Reason.Should().Be(TimekeeperUnmatchedReason.NotOnRoster);
        _embedded.Should().BeEmpty();
    }

    [Fact]
    public async Task Ai_IsMemoized_AcrossLinesOfTheSameInvoice()
    {
        GivenNameVector("jon smith");
        _vectors["jonathan smith"] = Vec(0.95);
        var jonathan = Person("Jonathan Smith");
        var requests = Enumerable.Repeat(new TimekeeperMatchRequest("Jon Smith", Day), 25)
            .Concat(Enumerable.Repeat(new TimekeeperMatchRequest("JON SMITH", Day.AddDays(1)), 25))
            .ToList();

        var results = await CreateMatcher().MatchAsync(Firm, [jonathan], [], requests, Threshold);

        results.Should().HaveCount(50).And.OnlyContain(r => r.TimekeeperId == jonathan.Id);
        _embedded.Should().BeEquivalentTo(["jon smith", "jonathan smith"], "each distinct text is embedded once per invoice");
    }

    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(HttpRequestException))]
    [InlineData(typeof(TimeoutException))]
    public async Task Ai_WhenTheModelCallFails_TheLineIsUnmatched_AndResolutionDoesNotFail(Type exceptionType)
    {
        _ai.Setup(a => a.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync((Exception)Activator.CreateInstance(exceptionType, "boom")!);

        var result = await MatchOne("Jon Smith", [Person("Jonathan Smith")]);

        result.IsMatched.Should().BeFalse();
        result.Reason.Should().Be(TimekeeperUnmatchedReason.AiUnavailable);
    }

    [Fact]
    public async Task Ai_WhenAiIsOff_TheNullFacadeThrowsFeatureDisabled_AndTheLineIsUnmatched()
    {
        var matcher = new TimekeeperMatcher(new NullInvoiceAi(Mock.Of<ILogger<NullInvoiceAi>>()), Mock.Of<ILogger<TimekeeperMatcher>>());

        var results = await matcher.MatchAsync(
            Firm, [Person("Jonathan Smith")], [], [new TimekeeperMatchRequest("Jon Smith", Day)], Threshold);

        results.Single().Reason.Should().Be(TimekeeperUnmatchedReason.AiUnavailable);
        results.Single().IsMatched.Should().BeFalse();
    }

    [Fact]
    public async Task Ai_AnExactMatch_StillWorksWhenAiIsOff()
    {
        var matcher = new TimekeeperMatcher(new NullInvoiceAi(Mock.Of<ILogger<NullInvoiceAi>>()), Mock.Of<ILogger<TimekeeperMatcher>>());
        var jane = Person("Jane Doe");

        var results = await matcher.MatchAsync(
            Firm, [jane], [], [new TimekeeperMatchRequest("Doe, Jane", Day), new TimekeeperMatchRequest("Jane Doe", Day)], Threshold);

        results.Select(r => r.TimekeeperId).Should().Equal(jane.Id, jane.Id);
    }

    [Fact]
    public async Task Ai_ATimeoutThatIsNotTheCallersCancellation_IsUnavailable_NotACancellation()
    {
        _ai.Setup(a => a.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("http timeout"));

        var result = await MatchOne("Jon Smith", [Person("Jonathan Smith")]);

        result.Reason.Should().Be(TimekeeperUnmatchedReason.AiUnavailable);
    }

    [Fact]
    public async Task Ai_TheCallersCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        _ai.Setup(a => a.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns<string, string?, int?, CancellationToken>((_, _, _, token) =>
            {
                cts.Cancel();
                throw new OperationCanceledException(token);
            });

        var act = () => MatchOne("Jon Smith", [Person("Jonathan Smith")], ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ACancelledToken_StopsBeforeAnyLineIsResolved()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => MatchOne("Jane Doe", [Person("Jane Doe")], ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ABrokenEmbedding_NeverLooksSimilar()
    {
        GivenNameVector("jon smith");
        _vectors["jonathan smith"] = [1f, 0f]; // different length

        var result = await MatchOne("Jon Smith", [Person("Jonathan Smith")]);

        result.IsMatched.Should().BeFalse();
        result.Reason.Should().Be(TimekeeperUnmatchedReason.AiBelowThreshold);
    }

    // ── Shape of the result ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OneResultPerRequest_InRequestOrder()
    {
        var jane = Person("Jane Doe");
        var john = Person("John Roe");
        var requests = new[]
        {
            new TimekeeperMatchRequest("John Roe", Day),
            new TimekeeperMatchRequest(null, Day),
            new TimekeeperMatchRequest("Jane Doe", Day),
            new TimekeeperMatchRequest("Nobody Here", Day),
        };

        var results = await CreateMatcher().MatchAsync(Firm, [jane, john], [], requests, Threshold);

        results.Select(r => r.TimekeeperId).Should().Equal(john.Id, null, jane.Id, null);
    }

    [Fact]
    public async Task Match_IsDeterministic_WhateverTheRosterOrder()
    {
        GivenNameVector("jon smith");
        _vectors["jonathan smith"] = Vec(0.97, axis: 1);
        _vectors["jonas smith"] = Vec(0.90, axis: 2);
        var a = Person("Jonathan Smith", id: Guid.Parse("00000000-0000-0000-0000-00000000000a"));
        var b = Person("Jonas Smith", id: Guid.Parse("00000000-0000-0000-0000-00000000000b"));

        var forward = await MatchOne("Jon Smith", [a, b]);
        var reversed = await MatchOne("Jon Smith", [b, a]);

        forward.TimekeeperId.Should().Be(a.Id);
        forward.Should().BeEquivalentTo(reversed);
    }
}

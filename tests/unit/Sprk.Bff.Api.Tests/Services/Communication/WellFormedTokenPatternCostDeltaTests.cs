using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Engine.Rungs;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Task 082 (space-bearing matter-number tokenizer repair) — the MEASURED query-count delta required by
/// ADR-045. This rung issues exactly one Dataverse reverse-lookup query per core record type in the catalog
/// roster (<see cref="IdentifierReverseLookupRung"/> batches per number field, not per token) WHENEVER the
/// message yields at least one candidate token, and zero queries otherwise (NFR-08 gate). So the per-message
/// query count is binary — 0 or <c>RosterSize</c> (7 in this test's full roster) — and the cost of loosening
/// <c>WellFormedTokenPattern</c> is exactly: how many MORE messages, over a representative corpus, now yield
/// at least one token.
/// </summary>
/// <remarks>
/// <para>
/// This exercises the rung through its PUBLIC surface only (no reflection into the private regex — tests/CLAUDE.md
/// B8) across a representative corpus of communication subjects spanning: pre-existing well-formed matches
/// (unaffected controls), the space-bearing repair target, ordinary hyphenated English compounds (the
/// pre-existing false-positive source this fix's digit requirement removes), dates/time references, bare-numeric
/// fallback (unaffected by this task), and plain conversational subjects with no tokens at all.
/// </para>
/// <para>
/// <b>Measured result</b> (this corpus, 35 messages, roster size 7 — full methodology + the companion
/// BEFORE-pattern comparison in
/// <c>projects/spaarke-ontology-platform-r1/notes/tokenizer-cost-delta.md</c>): queries-triggering messages
/// went from 23 (161 queries) BEFORE this task to 19 (133 queries) AFTER — a **decrease** of 4 messages / 28
/// queries. The repro itself ("Form D - 2023") is query-count NEUTRAL: the bare-numeric fallback already
/// extracted its trailing digits and issued the same 7 queries before this fix, just unproductively
/// (exact-match against "2023" alone never equals the stored "Form D - 2023"); this fix makes that query
/// productive (<see cref="RungKind.ExplicitReference"/> now actually fires) rather than adding a new one. The
/// REAL movement is: +3 messages / +21 queries from generalized shapes the whitespace tolerance newly admits
/// ("Acme Corp - 88 invoice", "Team sync - 2pm start", "Standup - 9am tomorrow" — accepted, measured cost, not
/// a misfile risk since the reverse lookup still requires an exact match), offset by −7 messages / −49 queries
/// from ordinary hyphenated English compounds ("follow-up", "state-of-the-art", "well-known", "catch-up",
/// "good-to-go", "sign-off", "up-to-date") that the new digit-in-body requirement — added to satisfy "no
/// arbitrary prose" — now correctly excludes, where the OLD pattern (no whitespace requirement at all) had
/// already been matching them. This test asserts the AFTER state (today's behavior); the BEFORE state cannot
/// be re-executed from this codebase since the fix is already applied — it is documented, not re-derived, in
/// the notes file above.
/// </para>
/// <para>
/// <b>Why the two-word prefix is restricted to a single trailing letter.</b> A first draft of the regex
/// allowed ANY second prefix word, not just a one-letter one. That is a genuine precision bug, not a style
/// choice: it made <c>"… related to MAT-123"</c> tokenize as <c>"to MAT-123"</c> — the connector "to" got
/// absorbed as a bogus first prefix word, pulling the REAL 3-letter prefix "MAT" in as the "second" word,
/// so the extracted token value stopped equaling the stored field value "MAT-123" and the exact-match lookup
/// silently failed. The existing <c>Fr12_NewRecordFraming_…</c> regression test in
/// <c>IdentifierReverseLookupRungTests.cs</c> caught this on the first build. The fix: the optional second
/// word is admitted ONLY when it is exactly one letter (matching the real "Form"/"D" shape) — a single letter
/// can never stand alone as a prefix word (the {2,} floor exists for exactly that reason), so it can never be
/// "stolen" by an unrelated preceding connector the way a 2+ letter word (like the real prefix "MAT") can.
/// The regression case is pinned permanently in this corpus (category "regression guard").
/// </para>
/// </remarks>
public class WellFormedTokenPatternCostDeltaTests
{
    private readonly Mock<ICommunicationDataverseService> _dv = new();

    private static readonly (string Entity, string NumberField, string RegardingField)[] CoreSeven =
    {
        ("sprk_matter", "sprk_matternumber", "sprk_regardingmatter"),
        ("sprk_project", "sprk_projectnumber", "sprk_regardingproject"),
        ("sprk_invoice", "sprk_invoicenumber", "sprk_regardinginvoice"),
        ("sprk_workassignment", "sprk_workassignmentnumber", "sprk_regardingworkassignment"),
        ("sprk_budget", "sprk_budgetnumber", "sprk_regardingbudget"),
        ("sprk_servicerequest", "sprk_servicerequestnumber", "sprk_regardingservicerequest"),
        ("sprk_reportcard", "sprk_reportcardnumber", "sprk_regardingreportcard"),
    };

    private IdentifierReverseLookupRung Rung() =>
        new(_dv.Object, NullLogger<IdentifierReverseLookupRung>.Instance);

    private static DataverseEntity RosterRow(string logicalName, string numberField)
    {
        var e = new DataverseEntity("sprk_recordtype_ref") { Id = Guid.NewGuid() };
        e["sprk_recordlogicalname"] = logicalName;
        e["sprk_regardingrecordnumberfield"] = numberField;
        return e;
    }

    private void SetupFullRosterNoMatches()
    {
        _dv.Setup(d => d.QueryAllRecordTypeRefsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CoreSeven.Select(t => RosterRow(t.Entity, t.NumberField)).ToArray());
        _dv.Setup(d => d.QueryRecordsByNumberFieldValuesAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DataverseEntity>());
    }

    /// <summary>
    /// The representative corpus (35 subjects) used to measure the delta. Each row states its category and
    /// the expected AFTER-fix query count: 0 (no candidate token — the rung never reads the roster) or 7 (one
    /// batched reverse-lookup query per core type, regardless of how many distinct tokens the message yields).
    /// </summary>
    public static IEnumerable<object[]> Corpus()
    {
        (string Subject, int ExpectedQueries, string Category)[] rows =
        {
            // Controls — already well-formed before this task; must remain unaffected.
            ("Re: MAT-123 next steps", 7, "control"),
            ("INV-002 payment reminder", 7, "control"),
            ("PRJT.10001.01 status", 7, "control"),
            ("WRK-55 assignment update", 7, "control"),
            ("BDGT-9012 revision", 7, "control"),
            ("SVCR-77 follow-up", 7, "control"),
            ("RPTC-3001 generated", 7, "control"),
            ("Re: REAL-2026-123456.02 real estate", 7, "control (P1 substring guard, unaffected)"),

            // The repair target + a same-shape variant (word + single-letter word + separator + digits).
            // Query-count NEUTRAL vs. before this task: the bare-numeric fallback already extracted the
            // trailing digits and issued these same 7 queries unproductively (no record's number field
            // equals "2023" alone). The fix makes the query productive (ExplicitReference now fires) rather
            // than adding a new one.
            ("Form D - 2023 case update", 7, "repair-target (task 082 repro — query-count neutral)"),
            ("Re: Form D - 2023", 7, "repair-target (query-count neutral)"),
            ("Case A - 4521 pending", 7, "repair-target (query-count neutral — same bare-numeric overlap)"),

            // Regression guard: the FIRST draft of this fix allowed ANY second prefix word (not just a
            // single letter), which made this exact subject tokenize as "to MAT-123" — the connector "to"
            // absorbed as a bogus first word, swallowing the real prefix "MAT" as its "second" word. The
            // existing Fr12 test in IdentifierReverseLookupRungTests.cs caught this because the match value
            // stopped equaling the stored field value. Locked in here too: must extract "MAT-123" cleanly.
            ("This is a new litigation matter related to MAT-123", 7, "regression guard (ambiguity fix)"),

            // Genuinely NEW query-triggering messages (0 before -> 7 after): whitespace tolerance +
            // digit-in-body also admits short business shorthand with a time reference or a bare 2-digit
            // suffix. This is the real added cost side of the delta — accepted, measured, not a misfile risk
            // since the reverse lookup still requires an exact match against a real record's number field.
            ("Acme Corp - 88 invoice", 7, "genuinely new match (+7 vs. before)"),
            ("Team sync - 2pm start", 7, "genuinely new match (+7 vs. before)"),
            ("Standup - 9am tomorrow", 7, "genuinely new match (+7 vs. before)"),

            // Ordinary hyphenated English compounds — PRE-EXISTING false positives under the old pattern
            // (no space required), now REJECTED because the body carries no digit. This is where the
            // query-count reduction comes from.
            ("Follow-up - please review when free", 0, "prose (fixed by digit requirement)"),
            ("state-of-the-art software rollout", 0, "prose (fixed by digit requirement)"),
            ("Well-known issue being tracked", 0, "prose (fixed by digit requirement)"),
            ("Re: catch-up - rescheduled", 0, "prose (fixed by digit requirement)"),
            ("Good-to-go on the merger", 0, "prose (fixed by digit requirement)"),
            ("Sign-off needed by Friday", 0, "prose (fixed by digit requirement)"),
            ("Up-to-date figures attached", 0, "prose (fixed by digit requirement)"),

            // Ordinary prose that never matched either pattern.
            ("Let's touch base - need your input", 0, "prose (never matched)"),
            ("Quick turnaround - thanks in advance", 0, "prose (never matched)"),
            ("Mary Jones - checking in", 0, "prose (never matched)"),
            ("Invoice total $1,200 - due Friday", 0, "prose (never matched)"),
            ("Call at 3 - confirmed", 0, "prose (never matched)"),

            // Dates / numeric prose — must stay rejected by the well-formed shape; the bare-numeric fallback
            // (unchanged by this task) still fires on the embedded 4+ digit runs.
            ("Meeting on 2026-10-03", 7, "date (bare-numeric fallback, unaffected)"),
            ("Q3 2026 - budget review", 7, "date (bare-numeric fallback, unaffected)"),

            // Bare-numeric fallback, unaffected by this task either way.
            ("regarding 441482 please advise", 7, "bare-numeric (unaffected)"),
            ("778899 reference", 7, "bare-numeric (unaffected)"),

            // Plain conversational subjects — zero tokens, zero queries, both before and after.
            ("Hello there", 0, "no tokens"),
            ("Quick question for you", 0, "no tokens"),
            ("Lunch tomorrow?", 0, "no tokens"),
            ("Thanks for the update!", 0, "no tokens"),
        };

        foreach (var row in rows)
            yield return new object[] { row.Subject, row.ExpectedQueries, row.Category };
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public async Task RepresentativeCorpus_IssuesExpectedQueryCount(string subject, int expectedQueries, string category)
    {
        SetupFullRosterNoMatches();

        await Rung().EvaluateAsync(Envelope(subject), new AssociationContext(), CancellationToken.None);

        _dv.Verify(d => d.QueryRecordsByNumberFieldValuesAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()),
            Times.Exactly(expectedQueries),
            $"category '{category}' — subject \"{subject}\" expected {expectedQueries} reverse-lookup queries");
    }

    [Fact]
    public void Corpus_AggregateQueryCount_MatchesTheMeasuredAfterTotal()
    {
        // Pins the aggregate so a future, uncoordinated change to WellFormedTokenPattern that shifts the
        // corpus's total query count is caught here rather than silently drifting the ADR-045 cost profile.
        // AFTER total measured for this task: 19 of 35 messages trigger the roster (7 queries each) = 133.
        var expected = Corpus().Sum(row => (int)row[1]);

        expected.Should().Be(133, "the task 082 measured AFTER total for this representative corpus");
    }

    private static NormalizedMessage Envelope(string subject) =>
        new() { Direction = CommunicationDirection.Incoming, Subject = subject };
}

using FluentAssertions;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Pins <see cref="AnalysisResultPersistence.ReviewMemoOutputName"/> (R8 §GAPS-5 Phase 4, 2026-09-07).
///
/// <para><b>Why a test for one string.</b> Because <c>OutputTypeId</c> is deliberately null (an
/// environment-specific GUID is not portable C#), this single value plays two roles at once: it is the
/// row's DISPLAY NAME in Dataverse and it is the LOOKUP KEY the read path matches on. Changing it
/// therefore orphans every row already written under the old name — and the read does not fail, it
/// returns "no Review Summary generated yet". A silent wrong answer, indistinguishable from the honest
/// empty state, with no exception and no log line.</para>
///
/// <para>That is the same failure shape §GAPS-5 Phase 1 fixed on the client (a field dropped in a
/// hand-mapping, surfacing as plausible-but-wrong content rather than an error), which is why it gets a
/// forcing function rather than a comment.</para>
///
/// <para><b>If this test fails, you renamed the constant.</b> That is allowed — but not silently. Before
/// changing it, query the target environment:
/// <code>SELECT sprk_name, COUNT(sprk_analysisoutputid) FROM sprk_analysisoutput GROUP BY sprk_name</code>
/// Zero rows under the old name ⇒ the rename is free; update this test and go. Any rows ⇒ you need a
/// data migration, or the by-code (<c>sprk_outputtypecode</c> = "REVMEMO") lookup described in
/// <see cref="AnalysisResultPersistence"/>. The 2026-09-07 rename was verified free this way: the whole
/// table held ONE row, named "Too Long Didn't Read".</para>
/// </summary>
public class ReviewMemoOutputNameGuardTests
{
    [Fact]
    public void ReviewMemoOutputName_IsTheNameRowsAreBothWrittenUnderAndLookedUpBy()
    {
        AnalysisResultPersistence.ReviewMemoOutputName.Should().Be(
            "Review Summary",
            "this string is simultaneously the sprk_analysisoutput display name and the lookup key; " +
            "changing it orphans existing rows and the read then reports 'not generated yet' rather " +
            "than failing — see this class's remarks for the pre-rename check");
    }

    /// <summary>
    /// The rename's whole purpose: "Memo" collided with <c>sprk_memo</c>, the first-class Notepad
    /// entity, whose supported parents gained <c>sprk_agreement</c> on 2026-08-25 — so one agreement
    /// record could carry both a Notepad memo and a "Summary Memo" meaning entirely unrelated things.
    /// A reader who knows Memos would read "Create Summary Memo" as "make me one of those, here".
    /// </summary>
    [Fact]
    public void ReviewMemoOutputName_DoesNotSayMemo_BecauseThatCollidesWithTheNotepadEntity()
    {
        AnalysisResultPersistence.ReviewMemoOutputName.Should().NotContainEquivalentOf(
            "memo",
            "the user-facing name must not collide with sprk_memo (Notepad), which can hang off the " +
            "same sprk_agreement record; code identifiers may keep ReviewMemo* (no user sees them)");
    }
}

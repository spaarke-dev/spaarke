using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Files;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Sprk.Bff.Api.Services.Signals;
using Xunit;
using static Sprk.Bff.Api.Tests.Services.Finance.RuleSetServiceTests;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 116 fix round 1: a replaced source file makes a drafted Procedure Version draftable again (owner ruling 2026-11-10,
/// "reset and allow redraft"); the rule set read shows a not-converted list only under its finished run's marker; a drafted version is
/// decided before its source file; and a run whose source file was replaced while it worked writes no marker and is retried.
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): the guarantees the version-file route and the drafting job rely on. Reuses the 076 harness (a fake store that
/// evaluates the real queries and models <c>createdby</c>, <c>createdon</c> and <c>modifiedon</c>).
/// </remarks>
public sealed class RuleSetServiceRedraftTests
{
    private static readonly Guid CallerOid = Guid.NewGuid();
    private const string Q1 = "Time must be billed in tenth-hour increments.";
    private const string Q2 = "No timekeeper may bill more than ten hours a day.";
    private const string Q3 = "Travel time is billed at half rate.";

    private static IReadOnlyList<NotConvertedClause> None => Array.Empty<NotConvertedClause>();

    private static DraftedRule Increment(string quote = Q1) =>
        Drafted("TimeIncrement", """{"type":"TimeIncrement","incrementHours":0.1}""", "Flag", quote, "Section 4.2");

    private static DraftedRule Cap(int hours, string quote = Q2) =>
        Drafted("DailyHoursCap", $$"""{"type":"DailyHoursCap","maxHoursPerDay":{{hours}}}""", "Flag", quote, "Section 5");

    private static DraftedRule Drafted(string type, string body, string consequence, string quote, string? location)
    {
        var (headline, template) = BillingRuleDraftTranslator.Wording(Enum.Parse<RuleType>(type));
        return new DraftedRule(type, body, template, headline, consequence, quote, location);
    }

    private static Entity Version(Harness h, Guid id) => h.Row(ProcedureEntity.VersionEntity, id);

    private static (Guid Policy, Guid Version) Find(Harness h, string quote)
    {
        var version = h.Rows["sprk_policyversion"].Single(v => v.GetAttributeValue<string>("sprk_clausereference") == quote);
        return (version.GetAttributeValue<EntityReference>("sprk_policy").Id, version.Id);
    }

    /// <summary>The replace as the file store does it: a new Servable source file, then the earlier one Removed.</summary>
    private static Guid ReplaceFile(Harness h, Guid version, Guid earlier)
    {
        var replacement = h.SourceFile(version, StoredFileStatus.Servable, "guidelines-v2.pdf");
        h.Row(StoredFileSchema.Entity, earlier)[StoredFileSchema.Status] = new OptionSetValue((int)StoredFileStatus.Removed);
        return replacement;
    }

    // ── A4: the rule set read ────────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "116 fix 1 (A4) must-fire: a not-converted list without the drafted marker Yes is not shown (null)")]
    [InlineData(false)]
    [InlineData(null)]
    public async Task Get_AListWithoutTheMarker_IsNull(bool? marker)
    {
        var h = new Harness();
        var d = h.DraftVersion(drafted: marker, notConvertedList: """[{"text":"Staff sensibly.","reason":"Nothing to check."}]""");

        var detail = await h.Service().GetAsync(d.RuleSet, CancellationToken.None);

        detail.Kind.Should().Be(RuleSetOutcomeKind.Done);
        detail.Value!.NotConverted.Should().BeNull();
    }

    [Fact(DisplayName = "116 fix 1 (A4) must-not-fire: a not-converted list under the drafted marker Yes is shown")]
    public async Task Get_AListUnderTheMarker_IsReturned()
    {
        var h = new Harness();
        var d = h.DraftVersion(drafted: true, notConvertedList: """[{"text":"Staff sensibly.","reason":"Nothing to check."}]""");

        var detail = await h.Service().GetAsync(d.RuleSet, CancellationToken.None);

        detail.Value!.NotConverted.Should().NotBeNull();
        detail.Value.NotConverted!.ToJsonString().Should().Contain("Staff sensibly.");
    }

    // ── K4: already drafted before the source file ───────────────────────────────────────────────

    [Theory(DisplayName = "116 fix 1 (K4): a drafted version with no single Servable source file is already drafted (409 at the route), not 422")]
    [InlineData(0)]
    [InlineData(2)]
    public async Task DraftSource_ADraftedVersion_IsAlreadyDrafted_WhateverItsFiles(int servable)
    {
        var h = new Harness();
        var d = h.DraftVersion(drafted: true, servableFiles: servable);

        var found = await h.Service().GetDraftSourceAsync(d.Version, CancellationToken.None);

        found.Kind.Should().Be(RuleSetOutcomeKind.Done);
        found.Value!.AlreadyDrafted.Should().BeTrue();
    }

    // ── A2: the reset ────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "116 fix 1 (A2): resetting a drafted Draft writes the marker No FIRST, then clears the list")]
    public async Task Reset_ADraftedDraft_MarkerNoFirst_ThenTheListCleared()
    {
        var h = new Harness();
        var d = h.DraftVersion(drafted: true, notConvertedList: "[]");
        h.Writes.Clear();

        var draft = await ProcedureService.ResetDraftedStateIfDraftAsync(h.Entities.Object, d.Version, NullLogger.Instance, CancellationToken.None);

        draft.Should().BeTrue();
        h.Writes.Select(w => (w.Entity, string.Join(",", w.Fields.Keys))).Should().Equal(
            (ProcedureEntity.VersionEntity, RuleSetService.VersionDraftedColumn),
            (ProcedureEntity.VersionEntity, RuleSetService.VersionNotConvertedListColumn));
        Version(h, d.Version).GetAttributeValue<bool?>(RuleSetService.VersionDraftedColumn).Should().BeFalse();
        Version(h, d.Version).Contains(RuleSetService.VersionNotConvertedListColumn).Should().BeFalse();
    }

    [Theory(DisplayName = "116 fix 1 (A2) must-not-fire: a never-drafted Draft is not written; a version that is not a Draft is refused and not written")]
    [InlineData("never drafted", true)]
    [InlineData("marker No", true)]
    [InlineData("published", false)]
    [InlineData("no status", false)]
    public async Task Reset_WritesNothing_UnlessDrafted(string scenario, bool expectedDraft)
    {
        var h = new Harness();
        var d = scenario switch
        {
            "never drafted" => h.DraftVersion(),
            "marker No" => h.DraftVersion(drafted: false),
            "published" => h.DraftVersion(versionStatus: ProcedureEntity.VersionStatusActive, drafted: true),
            _ => h.DraftVersion(versionStatus: null, drafted: true),
        };
        h.Writes.Clear();

        var draft = await ProcedureService.ResetDraftedStateIfDraftAsync(h.Entities.Object, d.Version, NullLogger.Instance, CancellationToken.None);

        draft.Should().Be(expectedDraft);
        h.Writes.Should().BeEmpty();
    }

    [Fact(DisplayName = "116 fix 1 (A2): draft, replace the file, draft again: the redraft runs from the new file and leaves no duplicate drafts")]
    public async Task Redraft_AfterAReplacedFile_DraftsFromTheNewFile_WithNoDuplicates()
    {
        var h = new Harness();
        var d = h.DraftVersion();
        var service = h.Service();
        (await service.AddDraftsAsync(d.Version, d.File, new[] { Increment(), Cap(10), Cap(8, Q3) }, None, CallerOid, CancellationToken.None))
            .Kind.Should().Be(RuleSetOutcomeKind.Done);
        Version(h, d.Version).GetAttributeValue<bool?>(RuleSetService.VersionDraftedColumn).Should().BeTrue();

        // A reviewer edits one draft and accepts another; the third stays untouched.
        var (edited, editedVersion) = Find(h, Q1);
        h.ConcurrentWrite("sprk_policyversion", editedVersion, new Dictionary<string, object> { ["sprk_shortheadline"] = "Edited by an admin" });
        var (accepted, acceptedVersion) = Find(h, Q2);
        h.ConcurrentWrite("sprk_policyversion", acceptedVersion, new Dictionary<string, object> { ["sprk_proposalstatus"] = new OptionSetValue((int)ProposalState.Accepted) });
        // 081's copy-forward under this version: a published rule's new Draft working version. It is never removed.
        var (published, publishedV1) = h.Rule(d.RuleSet, code: $"BRD-{d.RuleSet:N}-077", current: true);
        h.SetCreatedBy(published, h.Identity);
        h.SetCreatedBy(publishedV1, h.Identity);
        var copy = h.AddVersion(published, 2, ProposalState.Proposed);
        h.SetCreatedBy(copy, h.Identity);
        h.Row("sprk_policyversion", copy)[RuleSetService.ProcedureVersionColumn] = new EntityReference(ProcedureEntity.VersionEntity, d.Version);
        h.Row("sprk_policyversion", copy)[RuleSetService.PublishStateColumn] = new OptionSetValue(RuleSetService.PublishStateDraft);

        // The version-file route: reset, then the replace.
        (await ProcedureService.ResetDraftedStateIfDraftAsync(h.Entities.Object, d.Version, NullLogger.Instance, CancellationToken.None)).Should().BeTrue();
        var replacement = ReplaceFile(h, d.Version, d.File);

        var found = await service.GetDraftSourceAsync(d.Version, CancellationToken.None);
        found.Value!.AlreadyDrafted.Should().BeFalse("the reset makes the version draftable again");
        found.Value.FileId.Should().Be(replacement, "the redraft reads the new file");

        var compensated = await service.RemoveUntouchedDraftsAsync(d.Version, replacement, CancellationToken.None);
        compensated.Value.Should().Be(new DraftsCompensated(Removed: 3, Kept: 1));
        var redraft = await service.AddDraftsAsync(d.Version, replacement, new[] { Increment(), Cap(12) }, None, CallerOid, CancellationToken.None);

        redraft.Kind.Should().Be(RuleSetOutcomeKind.Done);
        redraft.Value!.Stored.Should().Be(2);
        h.Rows["sprk_policy"].Select(p => p.Id).Should().Contain(published).And.NotContain(new[] { edited, accepted });
        h.Rows["sprk_policy"].Should().HaveCount(3, "the new run's two drafts and the copy-forward; nothing of the earlier file's run");
        h.Rows["sprk_policyversion"].Select(v => v.Id).Should().Contain(new[] { copy, publishedV1 });
        Version(h, d.Version).GetAttributeValue<bool?>(RuleSetService.VersionDraftedColumn).Should().BeTrue();
    }

    [Fact(DisplayName = "116 fix 1 (A2) must-not-fire: a retry on the SAME file keeps a draft a reviewer worked on (only an earlier file's drafts are superseded)")]
    public async Task Retry_OnTheSameFile_KeepsAReviewedDraft()
    {
        var h = new Harness();
        var d = h.DraftVersion();
        var service = h.Service();
        await service.AddDraftsAsync(d.Version, d.File, new[] { Increment(), Cap(10) }, None, CallerOid, CancellationToken.None);
        Version(h, d.Version).Attributes.Remove(RuleSetService.VersionDraftedColumn); // a run that died before its marker
        var (edited, editedVersion) = Find(h, Q1);
        h.ConcurrentWrite("sprk_policyversion", editedVersion, new Dictionary<string, object> { ["sprk_shortheadline"] = "Edited by an admin" });

        var compensated = await service.RemoveUntouchedDraftsAsync(d.Version, d.File, CancellationToken.None);

        compensated.Value.Should().Be(new DraftsCompensated(Removed: 1, Kept: 1));
        h.Rows["sprk_policy"].Select(p => p.Id).Should().Equal(edited);
    }

    // ── A2: the race ─────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "116 fix 1 (A2 race) must-fire: the file replaced before the list is written: no list, no marker, this run's drafts removed, 503 (retried)")]
    public async Task AddDrafts_TheFileReplacedBeforeTheList_WritesNoMarker_AndIsRetried()
    {
        var h = new Harness();
        var d = h.DraftVersion();
        ReplaceFile(h, d.Version, d.File);

        var outcome = await h.Service().AddDraftsAsync(d.Version, d.File, new[] { Increment(), Cap(10) }, None, CallerOid, CancellationToken.None);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Refused);
        outcome.StatusCode.Should().Be(503);
        outcome.ErrorCode.Should().Be(RuleSetService.CodeSourceChanged);
        Version(h, d.Version).Contains(RuleSetService.VersionDraftedColumn).Should().BeFalse();
        Version(h, d.Version).Contains(RuleSetService.VersionNotConvertedListColumn).Should().BeFalse();
        h.Rows["sprk_policy"].Should().BeEmpty("this run's untouched drafts are removed");
    }

    [Fact(DisplayName = "116 fix 1 (A2 race) must-fire: the file replaced between the check and the marker: the marker is taken back (No, then the list), drafts removed, 503")]
    public async Task AddDrafts_TheFileReplacedAfterTheCheck_TakesTheMarkerBack()
    {
        var h = new Harness();
        var d = h.DraftVersion();
        var storedFileReads = 0;
        h.AfterQuery = q =>
        {
            if (q.EntityName == StoredFileSchema.Entity && ++storedFileReads == 1)
            {
                ReplaceFile(h, d.Version, d.File); // lands right after the run's check
            }
        };

        var outcome = await h.Service().AddDraftsAsync(d.Version, d.File, new[] { Increment() }, None, CallerOid, CancellationToken.None);

        outcome.ErrorCode.Should().Be(RuleSetService.CodeSourceChanged);
        outcome.StatusCode.Should().Be(503);
        Version(h, d.Version).GetAttributeValue<bool?>(RuleSetService.VersionDraftedColumn).Should().NotBe(true);
        Version(h, d.Version).Contains(RuleSetService.VersionNotConvertedListColumn).Should().BeFalse();
        h.Writes.Where(w => w.Entity == ProcedureEntity.VersionEntity).Select(w => (w.Fields.Keys.Single(), w.Fields.Values.Single())).Should().Equal(
            (RuleSetService.VersionNotConvertedListColumn, (object)"[]"),
            (RuleSetService.VersionDraftedColumn, true),
            (RuleSetService.VersionDraftedColumn, false),
            (RuleSetService.VersionNotConvertedListColumn, DBNull.Value));
        h.Rows["sprk_policy"].Should().BeEmpty();
    }

    [Fact(DisplayName = "116 fix 1 (A2 race) must-not-fire: the file unchanged throughout: the list and the marker are written")]
    public async Task AddDrafts_TheFileUnchanged_WritesTheMarker()
    {
        var h = new Harness();
        var d = h.DraftVersion();

        var outcome = await h.Service().AddDraftsAsync(d.Version, d.File, new[] { Increment() }, None, CallerOid, CancellationToken.None);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        Version(h, d.Version).GetAttributeValue<bool?>(RuleSetService.VersionDraftedColumn).Should().BeTrue();
    }
}

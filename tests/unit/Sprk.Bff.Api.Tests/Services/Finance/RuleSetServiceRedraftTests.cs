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

    [Fact(DisplayName = "116 fix 2 (K): the first reset of a drafted Draft writes ONLY the marker No; the list stays (hidden under No)")]
    public async Task Reset_ADraftedDraft_WritesOnlyTheMarkerNo()
    {
        var h = new Harness();
        var d = h.DraftVersion(drafted: true, notConvertedList: "[]");
        h.Writes.Clear();

        var draft = await ProcedureService.ResetDraftedStateIfDraftAsync(h.Entities.Object, d.Version, NullLogger.Instance, CancellationToken.None);

        draft.Should().BeTrue();
        h.Writes.Select(w => (w.Entity, string.Join(",", w.Fields.Keys))).Should().Equal(
            (ProcedureEntity.VersionEntity, RuleSetService.VersionDraftedColumn));
        Version(h, d.Version).GetAttributeValue<bool?>(RuleSetService.VersionDraftedColumn).Should().BeFalse();
        Version(h, d.Version).GetAttributeValue<string>(RuleSetService.VersionNotConvertedListColumn).Should().Be("[]");
    }

    [Fact(DisplayName = "116 fix 2: the re-check once the new file is Servable writes the marker No FIRST (a run marked it meanwhile), then clears the list")]
    public async Task Recheck_ADraftedDraft_MarkerNoFirst_ThenTheListCleared()
    {
        var h = new Harness();
        var d = h.DraftVersion(drafted: true, notConvertedList: "[]");
        h.Writes.Clear();

        var draft = await ProcedureService.RecheckDraftAfterNewFileAsync(h.Entities.Object, d.Version, NullLogger.Instance, CancellationToken.None);

        draft.Should().BeTrue();
        h.Writes.Select(w => (w.Entity, string.Join(",", w.Fields.Keys))).Should().Equal(
            (ProcedureEntity.VersionEntity, RuleSetService.VersionDraftedColumn),
            (ProcedureEntity.VersionEntity, RuleSetService.VersionNotConvertedListColumn));
        Version(h, d.Version).GetAttributeValue<bool?>(RuleSetService.VersionDraftedColumn).Should().BeFalse();
        Version(h, d.Version).GetAttributeValue<string>(RuleSetService.VersionNotConvertedListColumn).Should().Be(RuleSetService.ClearedNotConverted);
    }

    [Fact(DisplayName = "116 fix 2 (K) / fix 3 must-fire: a version published between the first reset and the re-check keeps its list visible to the rule set read; the re-check refuses and writes nothing")]
    public async Task Recheck_AVersionPublishedAfterTheFirstReset_KeepsItsList()
    {
        var h = new Harness();
        const string List = """[{"text":"Staff sensibly.","reason":"Nothing to check."}]""";
        var d = h.DraftVersion(drafted: true);
        Version(h, d.Version)[RuleSetService.VersionNotConvertedListColumn] = RuleSetService.StoredNotConvertedOf(d.File, List);

        (await ProcedureService.ResetDraftedStateIfDraftAsync(h.Entities.Object, d.Version, NullLogger.Instance, CancellationToken.None)).Should().BeTrue();
        Version(h, d.Version)[ProcedureEntity.VersionStatusColumn] = new OptionSetValue(ProcedureEntity.VersionStatusActive); // a publish lands
        h.Writes.Clear();
        var stillDraft = await ProcedureService.RecheckDraftAfterNewFileAsync(h.Entities.Object, d.Version, NullLogger.Instance, CancellationToken.None);

        stillDraft.Should().BeFalse();
        h.Writes.Should().BeEmpty();
        var shown = (await h.Service().GetAsync(d.RuleSet, CancellationToken.None)).Value!.NotConverted;
        shown.Should().NotBeNull("the published version's list survives and is shown");
        shown!.ToJsonString().Should().Contain("Staff sensibly.");
    }

    // ── Fix round 3 (item 2): the first reset writes the marker only for a list that names no file ──

    [Fact(DisplayName = "116 fix 3 (item 2) must-fire: the first reset of a version whose list is an envelope writes nothing")]
    public async Task Reset_AnEnvelopeList_WritesNothing()
    {
        var h = new Harness();
        var d = h.DraftVersion();
        (await h.Service().AddDraftsAsync(d.Version, d.File, new[] { Increment() }, None, CallerOid, CancellationToken.None)).Kind.Should().Be(RuleSetOutcomeKind.Done);
        h.Writes.Clear();

        var draft = await ProcedureService.ResetDraftedStateIfDraftAsync(h.Entities.Object, d.Version, NullLogger.Instance, CancellationToken.None);

        draft.Should().BeTrue();
        h.Writes.Should().BeEmpty();
        Version(h, d.Version).GetAttributeValue<bool?>(RuleSetService.VersionDraftedColumn).Should().BeTrue();
    }

    [Fact(DisplayName = "116 fix 3 (item 2, scenario A) must-fire: drafted from F, the upload of G starts, a publish lands, the re-check refuses and G is withdrawn: the published version is still drafted and its list is shown")]
    public async Task ScenarioA_APublishDuringTheUpload_LeavesTheVersionDrafted_AndItsListShown()
    {
        var h = new Harness();
        var d = h.DraftVersion();
        var service = h.Service();
        (await service.AddDraftsAsync(d.Version, d.File, new[] { Increment() }, new[] { new NotConvertedClause("Staff sensibly.", null, "Nothing to check.") },
            CallerOid, CancellationToken.None)).Kind.Should().Be(RuleSetOutcomeKind.Done);

        // The version-file route: the first reset, then G staged and made Servable, a publish lands, the re-check refuses, G withdrawn.
        (await ProcedureService.ResetDraftedStateIfDraftAsync(h.Entities.Object, d.Version, NullLogger.Instance, CancellationToken.None)).Should().BeTrue();
        var g = h.SourceFile(d.Version, StoredFileStatus.Servable, "guidelines-v2.pdf");
        Version(h, d.Version)[ProcedureEntity.VersionStatusColumn] = new OptionSetValue(ProcedureEntity.VersionStatusActive);
        (await ProcedureService.RecheckDraftAfterNewFileAsync(h.Entities.Object, d.Version, NullLogger.Instance, CancellationToken.None)).Should().BeFalse();
        h.Row(StoredFileSchema.Entity, g)[StoredFileSchema.Status] = new OptionSetValue((int)StoredFileStatus.Removed);

        Version(h, d.Version).GetAttributeValue<bool?>(RuleSetService.VersionDraftedColumn).Should().BeTrue();
        var shown = (await service.GetAsync(d.RuleSet, CancellationToken.None)).Value!.NotConverted;
        shown.Should().NotBeNull("F is still the one Servable file, so the version is drafted and its list is shown");
        shown!.ToJsonString().Should().Contain("Staff sensibly.");
    }

    [Fact(DisplayName = "116 fix 3 (item 2, scenario B) must-fire: the upload is refused after the first reset (an owner it cannot resolve, a stage that fails): the version is still drafted and its list is shown")]
    public async Task ScenarioB_AnUploadRefusedAfterTheReset_LeavesTheVersionDrafted_AndItsListShown()
    {
        var h = new Harness();
        var d = h.DraftVersion();
        var service = h.Service();
        (await service.AddDraftsAsync(d.Version, d.File, new[] { Increment() }, new[] { new NotConvertedClause("Staff sensibly.", null, "Nothing to check.") },
            CallerOid, CancellationToken.None)).Kind.Should().Be(RuleSetOutcomeKind.Done);

        (await ProcedureService.ResetDraftedStateIfDraftAsync(h.Entities.Object, d.Version, NullLogger.Instance, CancellationToken.None)).Should().BeTrue();
        // The store refuses the upload here: no new file, no re-check.

        (await service.GetDraftSourceAsync(d.Version, CancellationToken.None)).Value!.AlreadyDrafted.Should().BeTrue("the file did not change");
        var shown = (await service.GetAsync(d.RuleSet, CancellationToken.None)).Value!.NotConverted;
        shown.Should().NotBeNull();
        shown!.ToJsonString().Should().Contain("Staff sensibly.");
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
        var recheck = await ProcedureService.RecheckDraftAfterNewFileAsync(h.Entities.Object, d.Version, NullLogger.Instance, CancellationToken.None);

        draft.Should().Be(expectedDraft);
        recheck.Should().Be(expectedDraft);
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

    [Fact(DisplayName = "116 fix 1 (A2 race) / fix 3 must-fire: the file replaced between the check and the marker: the marker is taken back (No, then the cleared list), drafts removed, 503")]
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
        StoredList(h, d.Version).Should().Be(RuleSetService.ClearedNotConverted, "fix round 3: the take-back writes the cleared envelope, never an empty list");
        h.Writes.Where(w => w.Entity == ProcedureEntity.VersionEntity).Select(w => (w.Fields.Keys.Single(), w.Fields.Values.Single())).Should().Equal(
            (RuleSetService.VersionNotConvertedListColumn, (object)RuleSetService.StoredNotConvertedOf(d.File, "[]")),
            (RuleSetService.VersionDraftedColumn, true),
            (RuleSetService.VersionDraftedColumn, false),
            (RuleSetService.VersionNotConvertedListColumn, RuleSetService.ClearedNotConverted));
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

    // ── Fix round 2 (F2): "drafted" is the marker Yes AND the list's drafted-from file is the version's one Servable source file ──

    private static string StoredList(Harness h, Guid version) => Version(h, version).GetAttributeValue<string>(RuleSetService.VersionNotConvertedListColumn);

    /// <summary>After a fault point: the version reads as not drafted with the NEW file, and a retry drafts that file and is marked for it.</summary>
    private static async Task TheRetryDraftsTheNewFile(Harness h, RuleSetService service, Guid version, Guid replacement)
    {
        var found = await service.GetDraftSourceAsync(version, CancellationToken.None);
        found.Kind.Should().Be(RuleSetOutcomeKind.Done);
        found.Value!.AlreadyDrafted.Should().BeFalse("a Yes for a replaced file is stale: the version can be drafted again (/draft does not answer 409)");
        found.Value.FileId.Should().Be(replacement);

        (await service.RemoveUntouchedDraftsAsync(version, replacement, CancellationToken.None)).Kind.Should().Be(RuleSetOutcomeKind.Done);
        var retry = await service.AddDraftsAsync(version, replacement, new[] { Cap(12) }, None, CallerOid, CancellationToken.None);

        retry.Kind.Should().Be(RuleSetOutcomeKind.Done);
        Version(h, version).GetAttributeValue<bool?>(RuleSetService.VersionDraftedColumn).Should().BeTrue();
        RuleSetService.ParseStoredNotConverted(StoredList(h, version)).DraftedFromFileId.Should().Be(replacement);
        h.Rows["sprk_policy"].Should().ContainSingle("the earlier file's drafts are cleared; only the retry's rule remains");
        (await service.GetDraftSourceAsync(version, CancellationToken.None)).Value!.AlreadyDrafted.Should().BeTrue();
    }

    [Fact(DisplayName = "116 fix 2 (F2) must-fire: the re-read after the marker faults: handled as changed (marker taken back, 503 source_changed); the new file is drafted on retry")]
    public async Task FaultPoint_TheCheckAfterTheMarkerThrows_IsChanged_AndTheRetryDraftsTheNewFile()
    {
        var h = new Harness();
        var d = h.DraftVersion();
        var service = h.Service();
        var replacement = Guid.Empty;
        var storedFileReads = 0;
        h.AfterQuery = q =>
        {
            if (q.EntityName == StoredFileSchema.Entity && ++storedFileReads == 2)
            {
                h.AfterQuery = null;
                replacement = ReplaceFile(h, d.Version, d.File); // the upload lands; the run's re-read then faults
                throw new InvalidOperationException("simulated Dataverse 503");
            }
        };

        var outcome = await service.AddDraftsAsync(d.Version, d.File, new[] { Increment() }, None, CallerOid, CancellationToken.None);

        outcome.StatusCode.Should().Be(503);
        outcome.ErrorCode.Should().Be(RuleSetService.CodeSourceChanged);
        Version(h, d.Version).GetAttributeValue<bool?>(RuleSetService.VersionDraftedColumn).Should().NotBe(true, "the marker is taken back");
        await TheRetryDraftsTheNewFile(h, service, d.Version, replacement);
    }

    [Fact(DisplayName = "116 fix 2 (F2) must-fire: the take-back write fails (swallowed): the Yes stays for the OLD file, reads as not drafted, and the retry drafts the new file")]
    public async Task FaultPoint_TheTakeBackFails_TheStaleYesIsNotDrafted_AndTheRetryDraftsTheNewFile()
    {
        var h = new Harness();
        var d = h.DraftVersion();
        var service = h.Service();
        var replacement = Guid.Empty;
        var storedFileReads = 0;
        h.AfterQuery = q =>
        {
            if (q.EntityName != StoredFileSchema.Entity)
            {
                return;
            }

            if (++storedFileReads == 1)
            {
                replacement = ReplaceFile(h, d.Version, d.File); // lands right after the run's first check
            }
            else if (storedFileReads == 2)
            {
                h.AfterQuery = null;
                h.FailUpdateFor = e => e == ProcedureEntity.VersionEntity; // the re-read after the marker saw it; the take-back's writes fail
            }
        };

        var outcome = await service.AddDraftsAsync(d.Version, d.File, new[] { Increment() }, None, CallerOid, CancellationToken.None);
        h.FailUpdateFor = null;

        outcome.ErrorCode.Should().Be(RuleSetService.CodeSourceChanged);
        Version(h, d.Version).GetAttributeValue<bool?>(RuleSetService.VersionDraftedColumn).Should().BeTrue("the take-back did not land");
        RuleSetService.ParseStoredNotConverted(StoredList(h, d.Version)).DraftedFromFileId.Should().Be(d.File);
        await TheRetryDraftsTheNewFile(h, service, d.Version, replacement);
    }

    [Fact(DisplayName = "116 fix 2 (F2) must-fire: the host stops after the marker and the upload's re-check saw No: the stale Yes is not drafted, and the retry drafts the new file")]
    public async Task FaultPoint_StoppedAfterTheMarker_TheStaleYesIsNotDrafted_AndTheRetryDraftsTheNewFile()
    {
        var h = new Harness();
        var d = h.DraftVersion();
        var service = h.Service();
        (await service.AddDraftsAsync(d.Version, d.File, new[] { Increment() }, None, CallerOid, CancellationToken.None)).Kind.Should().Be(RuleSetOutcomeKind.Done);

        // The upload flipped its new file Servable while the run had passed its first check; its resets saw No and wrote nothing; the run's
        // marker landed and the host stopped before the re-read.
        var replacement = ReplaceFile(h, d.Version, d.File);

        (await service.GetAsync(d.RuleSet, CancellationToken.None)).Value!.NotConverted.Should().BeNull("the rule set read hides a stale Yes's list");
        await TheRetryDraftsTheNewFile(h, service, d.Version, replacement);
    }

    [Fact(DisplayName = "116 fix 2 (F2) must-not-fire: a list with no file id (the earlier bare-list shape) under Yes is drafted as before, whatever the files are now")]
    public async Task Legacy_ABareListUnderYes_IsDraftedAsBefore()
    {
        var h = new Harness();
        var d = h.DraftVersion(drafted: true, notConvertedList: """[{"text":"Staff sensibly.","reason":"Nothing to check."}]""");
        ReplaceFile(h, d.Version, d.File);
        var service = h.Service();

        (await service.GetDraftSourceAsync(d.Version, CancellationToken.None)).Value!.AlreadyDrafted.Should().BeTrue();
        (await service.AddDraftsAsync(d.Version, d.File, new[] { Increment() }, None, CallerOid, CancellationToken.None)).ErrorCode
            .Should().Be(RuleSetService.CodeAlreadyDrafted);
        (await service.GetAsync(d.RuleSet, CancellationToken.None)).Value!.NotConverted!.ToJsonString().Should().Contain("Staff sensibly.");
    }

    [Fact(DisplayName = "116 fix 2 (F2): the run writes the list as an envelope naming its file; the rule set read returns the bare list (client contract unchanged)")]
    public async Task AddDrafts_WritesTheEnvelope_AndTheReadReturnsTheBareList()
    {
        var h = new Harness();
        var d = h.DraftVersion();

        await h.Service().AddDraftsAsync(d.Version, d.File, new[] { Increment() }, new[] { new NotConvertedClause("Staff sensibly.", null, "Nothing to check.") },
            CallerOid, CancellationToken.None);

        var stored = RuleSetService.ParseStoredNotConverted(StoredList(h, d.Version));
        stored.IsEnvelope.Should().BeTrue();
        stored.DraftedFromFileId.Should().Be(d.File);
        var shown = (await h.Service().GetAsync(d.RuleSet, CancellationToken.None)).Value!.NotConverted!;
        shown.Should().BeOfType<System.Text.Json.Nodes.JsonArray>("the client reads the list, never the envelope");
        shown.AsArray().Single()!["text"]!.GetValue<string>().Should().Be("Staff sensibly.");
    }

    [Fact(DisplayName = "116 fix 2 (F2) must-fire: an envelope that names no file (the re-check's clear) under Yes is not drafted")]
    public async Task ClearedEnvelopeUnderYes_IsNotDrafted()
    {
        var h = new Harness();
        var d = h.DraftVersion(drafted: true, notConvertedList: RuleSetService.ClearedNotConverted);

        (await h.Service().GetDraftSourceAsync(d.Version, CancellationToken.None)).Value!.AlreadyDrafted.Should().BeFalse();
        (await h.Service().GetAsync(d.RuleSet, CancellationToken.None)).Value!.NotConverted.Should().BeNull();
    }

    [Fact(DisplayName = "116 fix 2 must-fire: the marker is written on CancellationToken.None: a run cancelled after its list is written still marks the version")]
    public async Task AddDrafts_CancelledAfterTheList_StillWritesTheMarker()
    {
        var h = new Harness();
        var d = h.DraftVersion();
        using var cts = new CancellationTokenSource();
        h.FailUpdateFor = e =>
        {
            if (e == ProcedureEntity.VersionEntity)
            {
                cts.Cancel(); // the request goes away while the list is written
            }

            return false;
        };

        var outcome = await h.Service().AddDraftsAsync(d.Version, d.File, new[] { Increment() }, None, CallerOid, cts.Token);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        Version(h, d.Version).GetAttributeValue<bool?>(RuleSetService.VersionDraftedColumn).Should().BeTrue();
    }

    // ── Fix round 3 (item 4): the take-back writes the cleared envelope ──

    private static void Servable(Harness h, Guid servable, Guid removed)
    {
        h.Row(StoredFileSchema.Entity, servable)[StoredFileSchema.Status] = new OptionSetValue((int)StoredFileStatus.Servable);
        h.Row(StoredFileSchema.Entity, removed)[StoredFileSchema.Status] = new OptionSetValue((int)StoredFileStatus.Removed);
    }

    [Fact(DisplayName = "116 fix 3 (item 4) must-fire: overlapping runs (one writes its list, the other's take-back writes, then the first writes Yes): the version is not drafted, for its file or a later one")]
    public async Task OverlappingRuns_ATakeBackBetweenAListAndItsMarker_IsNotDraftedForAnotherFile()
    {
        var h = new Harness();
        var d = h.DraftVersion();
        var service = h.Service();
        var earlier = d.File;
        var current = h.SourceFile(d.Version, StoredFileStatus.Removed, "guidelines-v2.pdf");
        Servable(h, current, earlier); // run 1 drafts from the current file

        var versionUpdates = 0;
        RuleSetOutcome<DraftsAdded>? run2 = null;
        h.FailUpdateFor = e =>
        {
            if (e != ProcedureEntity.VersionEntity || ++versionUpdates != 2)
            {
                return false;
            }

            // Run 1 has written its list and is about to write its marker. Run 2 (drafting from the earlier file) runs now: its first check
            // sees its file, then the current one is back, so after its marker it takes the marker back.
            h.FailUpdateFor = null;
            Servable(h, earlier, current);
            var storedFileReads = 0;
            h.AfterQuery = q =>
            {
                if (q.EntityName == StoredFileSchema.Entity && ++storedFileReads == 1)
                {
                    h.AfterQuery = null;
                    Servable(h, current, earlier);
                }
            };
            run2 = service.AddDraftsAsync(d.Version, earlier, new[] { Cap(10) }, None, CallerOid, CancellationToken.None).GetAwaiter().GetResult();
            return false; // run 1's marker now lands
        };

        var run1 = await service.AddDraftsAsync(d.Version, current, new[] { Increment() }, None, CallerOid, CancellationToken.None);

        run2!.ErrorCode.Should().Be(RuleSetService.CodeSourceChanged, "run 2 took its marker back");
        run1.Kind.Should().Be(RuleSetOutcomeKind.Done);
        Version(h, d.Version).GetAttributeValue<bool?>(RuleSetService.VersionDraftedColumn).Should().BeTrue("run 1's marker landed last");
        var stored = RuleSetService.ParseStoredNotConverted(StoredList(h, d.Version));
        stored.IsEnvelope.Should().BeTrue("the take-back wrote the cleared envelope, not an empty list");
        stored.DraftedFromFileId.Should().BeNull();
        (await service.GetDraftSourceAsync(d.Version, CancellationToken.None)).Value!.AlreadyDrafted.Should().BeFalse("a Yes over a list that names no file is not drafted");

        // A later file whose upload re-check never landed: still not drafted, so it can be drafted.
        var later = h.SourceFile(d.Version, StoredFileStatus.Servable, "guidelines-v3.pdf");
        h.Row(StoredFileSchema.Entity, current)[StoredFileSchema.Status] = new OptionSetValue((int)StoredFileStatus.Removed);
        var found = await service.GetDraftSourceAsync(d.Version, CancellationToken.None);
        found.Value!.AlreadyDrafted.Should().BeFalse("the version must not read as drafted for a file no run drafted");
        found.Value.FileId.Should().Be(later);
    }
}

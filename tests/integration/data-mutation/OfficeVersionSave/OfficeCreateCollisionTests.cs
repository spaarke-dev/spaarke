using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Tests.Api.Office;
using Sprk.Bff.Api.Tests.Shared.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.OfficeVersionSave;

/// <summary>
/// D1 (spaarkeai-word-add-in-r1 task 025, owner re-scope 2026-09-17): a same-name Document CREATE used to land
/// on the SAME SPE item as an existing document (path-keyed <c>ConflictBehavior.Replace</c>), silently
/// overwriting its bytes, and then fail to create a SECOND <c>sprk_document</c> row on
/// <c>sprk_graphitemid_uk</c> — so the FIRST document's content was replaced by a save that itself errored.
/// This is the create-path residual task 047 left open (its own note §4: "Document creates: not changed...
/// Task 025's refuse-before-upload design removes both").
/// </summary>
/// <remarks>
/// <para><b>The fix.</b> The create path now passes <c>ConflictBehavior.Fail</c> by default: Graph refuses the
/// PUT atomically on a name collision — no bytes move, the existing item and its owning document are provably
/// untouched. The refusal is a typed, distinguishable <c>OFFICE_020</c> ProblemDetails (409), never a generic
/// 500 and never a success with silently different content (ADR-019). Two explicit retries are offered,
/// mirroring the shipped OBO two-option dialog WITHOUT a second collision-detection mechanism: "Keep both"
/// (<c>Document.AllowRename</c> — the server passes <c>ConflictBehavior.Rename</c>, Graph mints a non-colliding
/// name) and "Save as new version" (resubmit through the ALREADY-SHIPPED FR-11 version-save path using the
/// <c>existingDocumentId</c> this task's refusal resolves and returns). A collision that resolves to NO owning
/// document (an orphan left by an earlier attempt that uploaded successfully but failed before its Dataverse row
/// was written) is reclaimed automatically rather than refused — see
/// <see cref="OfficeSaveSpineIdempotencyTests.RetryAfterACreateSaveThatThrewMidway_WithTheSameKey_StartsANewAttempt"/>,
/// which pins this: without the reclaim, that scenario would be a PERMANENT lockout, not merely task 039's
/// transient-failure retry.</para>
/// <para>Module-boundary doubles only (ADR-038 §4): the real route, filters, <c>OfficeService</c>,
/// <c>OfficeStorageUploader</c>, <c>OfficeDocumentPersistence</c> over <see cref="OfficeVersionSaveWorld"/> — no
/// second in-memory model.</para>
/// </remarks>
[Trait("status", "repaired")]
public class OfficeCreateCollisionTests
{
    private const string SaveContainer = "b!test-office-save-drive";

    // FR-02 (task 014): REAL minimal .docx bytes — a bare PK signature now classifies CORRUPT, so every save
    // here would be refused with OFFICE_021 instead of reaching the OFFICE_020 collision this class is about.
    // The two refusals must stay distinguishable, which is why 014 took the next free code rather than 020.
    private static readonly byte[] B = MinimalDocx.Create("draft B");
    private static readonly byte[] A = MinimalDocx.Create("draft A");

    private static readonly SaveEntityReference Target = new() { EntityType = "matter", EntityId = Guid.NewGuid() };

    private static SaveRequest CreateSave(string fileName, byte[] bytes, bool allowRename = false) => new()
    {
        ContentType = SaveContentType.Document,
        TargetEntity = Target,
        Document = new DocumentMetadata { FileName = fileName, ContentBase64 = Convert.ToBase64String(bytes), AllowRename = allowRename },
    };

    private static SaveRequest VersionSaveOf(Guid existingDocumentId, byte[] bytes, string fileName = "Brief.docx") => new()
    {
        ContentType = SaveContentType.Document,
        TargetEntity = Target,
        Document = new DocumentMetadata
        {
            FileName = fileName,
            ContentBase64 = Convert.ToBase64String(bytes),
            IsNewVersion = true,
            ExistingDocumentId = existingDocumentId,
        },
    };

    private static async Task<SaveResponse> BodyOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<SaveResponse>())!;

    /// <summary>The immediate 202 <see cref="SaveResponse"/> carries no <c>Artifact</c> — it is written onto
    /// the JOB record (<c>OfficeService.DocumentResult</c>), not the initial response. Mirrors the established
    /// <c>JobOf</c> idiom in <c>OfficeImmutableSaveFileSafetyTests</c>: poll the job status to read the created
    /// document's id back. Takes the ALREADY-READ <see cref="SaveResponse"/> body (not the raw
    /// <see cref="HttpResponseMessage"/>) because an <c>HttpContent</c> stream can only be read once.</summary>
    private static async Task<Guid> CreatedDocumentIdAsync(HttpClient client, SaveResponse saved)
    {
        var jobResponse = await client.GetAsync($"/api/office/jobs/{saved.JobId}");
        jobResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var job = (await jobResponse.Content.ReadFromJsonAsync<JobStatusResponse>())!;
        job.Status.Should().Be(JobStatus.Completed, "the create's job completes synchronously in this fixture");
        return job.Result!.Artifact!.Id;
    }

    /// <summary>The string-valued top-level properties of a ProblemDetails error body (errorCode, fileName,
    /// existingDocumentId — Guid serializes as a string). Mirrors the established <c>ReadProblemAsync</c> idiom
    /// in <c>OfficeQuickCreateContractTests</c> / <c>OfficeSaveSpineIdempotencyTests</c>.</summary>
    private static async Task<Dictionary<string, string?>> ReadProblemAsync(HttpResponseMessage response)
    {
        using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        return document.RootElement.EnumerateObject()
            .Where(property => property.Value.ValueKind == JsonValueKind.String)
            .ToDictionary(property => property.Name, property => property.Value.GetString());
    }

    private static OfficeVersionSaveWorld.SpeItem TheItemNamed(OfficeVersionSaveWorld world, string fileName) =>
        world.SpeItems.Values.Should().ContainSingle(i => i.DriveId == SaveContainer && i.Name == fileName).Subject;

    // ── (a) The existing file's bytes and version are provably unchanged across a colliding save ──────────

    [Fact]
    public async Task ColliderCreateSave_UnderAnExistingDocumentsName_RefusesBeforeAnyBytesMove_LeavingItsFileAndVersionUnchanged()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();

        var first = await client.PostAsJsonAsync("/api/office/save", CreateSave("Brief.docx", B));
        first.StatusCode.Should().Be(HttpStatusCode.Accepted, "precondition: the first save establishes the document");
        var firstDocumentId = await CreatedDocumentIdAsync(client, await BodyOf(first));

        var collision = await client.PostAsJsonAsync("/api/office/save", CreateSave("Brief.docx", A));

        collision.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "a same-name create is refused before any bytes move, never a success with different content (ADR-019)");
        var problem = await ReadProblemAsync(collision);
        problem.Should().ContainKey("errorCode").WhoseValue.Should().Be("OFFICE_020");
        problem.Should().ContainKey("fileName").WhoseValue.Should().Be("Brief.docx");
        problem.Should().ContainKey("existingDocumentId").WhoseValue.Should().Be(firstDocumentId.ToString("D"),
            "the pane needs this id to offer \"Save as new version\" as a direct retry");

        // The existing file's bytes AND version are provably unchanged.
        var item = TheItemNamed(world, "Brief.docx");
        item.Versions.Should().ContainSingle("the collision never added a version, let alone replaced the content");
        MinimalDocx.ReadBodyText(item.Versions[0])
            .Should().Be("draft B", "the existing item still holds exactly what the first save wrote");
        world.UploadSmallCalls.Should().Be(1, "the colliding attempt never reached a second successful upload");
        world.CollisionRefusals.Should().Be(1);
    }

    // ── (b) create B -> A -> B never corrupts or loses B (task 047's addendum) ─────────────────────────────

    [Fact]
    public async Task ThreeCreatesOfBThenAThenB_UnderTheSameNameAndTarget_NeverCorruptsOrLosesB()
    {
        // Task 047 §4 / this task's addendum: before the fix, the second create (A) silently overwrote the
        // first document's (B's) file in place (Replace, path-keyed) and then failed inserting a second row on
        // sprk_graphitemid_uk — so B's OWN file held A while the create that caused it errored. The third
        // create (B again) then found the first save's Completed job under its own (content-aware) key and was
        // answered "Duplicate" — which, after the corruption, was a LIE: SPE held A, not B. Task 025 removes the
        // corruption at its source: the second create (A) now refuses outright, so SPE never stops holding B,
        // and the third create's "Duplicate" answer is correct because it is true.
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();

        var createB = await client.PostAsJsonAsync("/api/office/save", CreateSave("Brief.docx", B));
        createB.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var createBBody = await BodyOf(createB);
        var documentId = await CreatedDocumentIdAsync(client, createBBody);

        var createA = await client.PostAsJsonAsync("/api/office/save", CreateSave("Brief.docx", A));
        createA.StatusCode.Should().Be(HttpStatusCode.Conflict, "A collides with B's name — refused, not written over B");
        (await ReadProblemAsync(createA)).Should().ContainKey("existingDocumentId").WhoseValue.Should().Be(documentId.ToString("D"));

        var createBAgain = await client.PostAsJsonAsync("/api/office/save", CreateSave("Brief.docx", B));

        createBAgain.StatusCode.Should().Be(HttpStatusCode.OK, "the document already truthfully holds these exact bytes");
        var thirdBody = await BodyOf(createBAgain);
        thirdBody.Duplicate.Should().BeTrue();
        thirdBody.JobId.Should().Be(createBBody.JobId, "answered with the save that actually wrote these bytes");

        // The end state: exactly ONE document, ONE SPE item, ONE version — B, never A, never a second row.
        world.Documents.Should().HaveCount(1, "A's create never inserted a second row (the sprk_graphitemid_uk defect is moot: it never got that far)");
        var item = TheItemNamed(world, "Brief.docx");
        item.Versions.Should().ContainSingle("neither the refused A nor the truthful duplicate B wrote anything new");
        MinimalDocx.ReadBodyText(item.Versions[0])
            .Should().Be("draft B", "SPE ends exactly where the user left it — holding B, never silently corrupted to A");
        world.Jobs.Should().HaveCount(2, "B's job (Completed) and A's job (Failed) — the third save's Duplicate answer creates no new job");
    }

    // ── (c) dismissing the choice writes nothing and creates no sprk_document row ──────────────────────────

    [Fact]
    public async Task CollisionChoice_WhenTheUserDismissesItInsteadOfRetrying_HasWrittenNothingAndCreatedNoDocumentRow()
    {
        // "Dismissing" is a client-only act (the pane simply does not send another request) — there is nothing
        // server-side to undo. This test pins that the refusal ITSELF is already the complete, safe end state:
        // no bytes, no row, no lingering half-written anything for a dismiss to have to clean up.
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();

        (await client.PostAsJsonAsync("/api/office/save", CreateSave("Brief.docx", B)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        var beforeDismiss = (
            DocumentCreates: world.DocumentCreates,
            Documents: world.Documents.Count,
            Uploads: world.UploadSmallCalls,
            Finalizations: world.FinalizationPayloads.Count);

        var collision = await client.PostAsJsonAsync("/api/office/save", CreateSave("Brief.docx", A));
        collision.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Simulating "dismiss": the test sends nothing further. The invariant is what the refusal alone did —
        // compared against the state AFTER the first (legitimate) save, so that save's own finalization job
        // is not mistaken for something the collision produced.
        world.DocumentCreates.Should().Be(beforeDismiss.DocumentCreates, "the collision created no sprk_document row");
        world.Documents.Should().HaveCount(beforeDismiss.Documents, "still exactly the one document from the first save");
        world.UploadSmallCalls.Should().Be(beforeDismiss.Uploads, "the collision performed no successful upload");
        world.DeletedItemIds.Should().BeEmpty("nothing was uploaded for a dismiss to need cleaning up");
        world.FinalizationPayloads.Should().HaveCount(beforeDismiss.Finalizations, "no NEW finalization job was queued for the refused save");
    }

    // ── The two-option retries: both mirror the shipped semantics without a second write mechanism ─────────

    [Fact]
    public async Task CollisionRetry_KeepBothWithAllowRename_UploadsUnderAGraphChosenName_AndLeavesTheOriginalUntouched()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();

        (await client.PostAsJsonAsync("/api/office/save", CreateSave("Brief.docx", B)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await client.PostAsJsonAsync("/api/office/save", CreateSave("Brief.docx", A)))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "precondition: the plain retry still collides");

        var keepBoth = await client.PostAsJsonAsync("/api/office/save", CreateSave("Brief.docx", A, allowRename: true));

        keepBoth.StatusCode.Should().Be(HttpStatusCode.Accepted, "\"Keep both\" asks the server to rename instead of refusing");
        world.Documents.Should().HaveCount(2, "a genuinely new row for the renamed upload");
        var renamedItem = world.SpeItems.Values.Should().ContainSingle(i => i.DriveId == SaveContainer && i.Name != "Brief.docx").Subject;
        renamedItem.Name.Should().StartWith("Brief (", "Graph's own auto-rename shape, mirrored by the fixture");
        MinimalDocx.ReadBodyText(renamedItem.Versions.Should().ContainSingle().Subject).Should().Be("draft A");

        // The Dataverse row stores the file's ACTUAL (renamed) name, not the one that was merely requested.
        var renamedDocument = world.Documents.Values.Single(d => d.ItemId == renamedItem.Id);
        renamedDocument.FileName.Should().Be(renamedItem.Name);

        // The original is provably untouched throughout.
        var original = TheItemNamed(world, "Brief.docx");
        MinimalDocx.ReadBodyText(original.Versions.Should().ContainSingle().Subject).Should().Be("draft B");
    }

    [Fact]
    public async Task CollisionRetry_SaveAsNewVersionOfTheResolvedExistingDocument_WritesANewVersion_NeverASecondRow()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();

        var created = await client.PostAsJsonAsync("/api/office/save", CreateSave("Brief.docx", B));
        created.StatusCode.Should().Be(HttpStatusCode.Accepted, "precondition");
        var documentId = await CreatedDocumentIdAsync(client, await BodyOf(created));
        var collision = await client.PostAsJsonAsync("/api/office/save", CreateSave("Brief.docx", A));
        collision.StatusCode.Should().Be(HttpStatusCode.Conflict, "precondition");
        var resolvedId = Guid.Parse((await ReadProblemAsync(collision))["existingDocumentId"]!);
        resolvedId.Should().Be(documentId, "precondition: the refusal resolved the correct existing document");

        // The version-save authorization filter (OfficeVersionSaveAuthorizationFilter) checks the caller's
        // rights on the target document — a right the create path itself never records in this fixture (only
        // the SeedDocument test helper does). Granted explicitly here, mirroring
        // OfficeSaveAsNewDocumentLinkGraduateTests' own "the caller holds the same rights on their new
        // document" precedent — this is a TEST-FIXTURE fact about the caller's real Dataverse access, not
        // something task 025's server code grants.
        world.AccessByDocumentId[resolvedId] = AccessRights.Read | AccessRights.Write;

        // "Save as new version": the pane resubmits through the ALREADY-SHIPPED FR-11 version-save path.
        var saveAsVersion = await client.PostAsJsonAsync("/api/office/save", VersionSaveOf(resolvedId, A));

        saveAsVersion.StatusCode.Should().Be(HttpStatusCode.Accepted);
        world.Documents.Should().HaveCount(1, "a version save never creates a second row — the invariant this retry depends on");
        var item = TheItemNamed(world, "Brief.docx");
        item.Versions.Should().HaveCount(2, "the version write is item-keyed (ReplaceFileContentAsUserAsync), not a second path-keyed upload");
        MinimalDocx.ReadBodyText(item.Versions[^1])
            .Should().Be("draft A", "the version save's content is what the user actually chose to keep");
        world.ReplaceCalls.Should().Be(1, "the version write went through the item-keyed path, never a second create upload");
    }
}

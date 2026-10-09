using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Xunit;
using World = TestRecordContainerResolver.DocumentPointerWorld;

namespace Sprk.Bff.Api.Tests.DataMutation.DocumentContainer;

/// <summary>
/// Batch-4 integration — round 26 item 3 and round 46 item 2: Make Secure moves the record's EXISTING files into its own
/// container through the ONE <see cref="Sprk.Bff.Api.Services.Documents.DocumentContainerRelocator"/>, and 147's
/// <see cref="SecureChildReconciliationJob"/> settles the moves a Make Secure left unmade.
/// </summary>
/// <remarks>
/// Driven through the REAL provisioning endpoint (with the REAL reconciler over task 148's in-memory world) and the REAL
/// relocator over task 166's document-pointer world, joined by the same document ids: the child pass decides which
/// documents end isolated, and the relocator moves exactly those. KEEP path: <c>tests/integration/data-mutation/**</c>.
/// </remarks>
public class MakeSecureFileRelocationTests : IClassFixture<ProvisionProjectTestFixture>
{
    private const string ProvisionRoute = "/api/v1/external-access/provision-project";
    private const string RecordContainer = ProvisionProjectTestFixture.ProvisionedContainerId;
    private const string SharedContainer = DocumentContainerRelocatorTests.CustomerAContainer;
    private const string Item = DocumentContainerRelocatorTests.Item;

    private readonly ProvisionProjectTestFixture _fixture;

    public MakeSecureFileRelocationTests(ProvisionProjectTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    /// <summary>
    /// The relocator's world: the project secure in its OWN container (the one provisioning creates) and one document of it
    /// whose file still sits in the business unit's shared container.
    /// </summary>
    private static World FilesOf(Guid projectId, Guid documentId)
    {
        var files = DocumentContainerRelocatorTests.Environment();
        files.SecureClaims[RecordContainer] = ("sprk_project", projectId);
        files.Rows[("sprk_project", projectId)] = new Entity("sprk_project", projectId)
        {
            ["sprk_issecure"] = true,
            ["sprk_containerid"] = RecordContainer,
            ["owningbusinessunit"] = new EntityReference("businessunit", DocumentContainerRelocatorTests.CustomerA),
        };
        var document = World.Document(documentId, null, DocumentContainerRelocatorTests.CustomerA);
        document["sprk_project"] = new EntityReference("sprk_project", projectId);
        document["sprk_graphdriveid"] = SharedContainer;
        document["sprk_graphitemid"] = Item;
        files.Rows[("sprk_document", documentId)] = document;
        files.Items[(SharedContainer, Item)] = new SpeItemCreator(
            "memo.docx", DocumentContainerRelocatorTests.Creator, null, 10, "h");
        return files;
    }

    /// <summary>A project about to be made secure, with one document filed under it (ordinary-team owned).</summary>
    private (Guid Project, Guid Document) SeedProjectWithOneDocument()
    {
        var project = Guid.NewGuid();
        var document = Guid.NewGuid();
        _fixture.SeedProject(project, isSecure: true);
        _fixture.UseChildWorldForRoots();
        _fixture.ChildWorld.OrdinaryChild("sprk_document", document, ("sprk_project", "sprk_project", project));
        return (project, document);
    }

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static string? PointerOf(World files, Guid documentId)
        => files.Updates.Where(u => u.Id == documentId && u.Fields.ContainsKey("sprk_graphdriveid"))
            .Select(u => u.Fields["sprk_graphdriveid"] as string)
            .LastOrDefault();

    [Fact(DisplayName = "Round 26 item 3: Make Secure moves the record's existing file into its own container, and says so")]
    public async Task MakeSecure_MovesTheRecordsExistingFile_IntoItsOwnContainer()
    {
        var (project, document) = SeedProjectWithOneDocument();
        var files = FilesOf(project, document);
        var rig = new DocumentContainerRelocatorTests.Rig(files);
        _fixture.RelocatorOverride = () => rig.Relocator;

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(ProvisionRoute, new { projectId = project });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await JsonOf(response);
        body.GetProperty("files").GetProperty("moved").GetInt32().Should().Be(1);
        body.GetProperty("files").GetProperty("incomplete").GetInt32().Should().Be(0);
        PointerOf(files, document).Should().Be(RecordContainer, "the document now points at the record's own container");
        rig.Steps.Should().Contain($"delete {SharedContainer}/{Item} (after re-point)", "the source goes once the copy is verified");
    }

    [Fact(DisplayName = "Round 26 item 3: a move that fails is files_incomplete with counts; the record stays provisioned, and a repeat call completes it")]
    public async Task MakeSecure_WhenAFileCannotBeMoved_IsFilesIncomplete_AndARepeatCallCompletesIt()
    {
        var (project, document) = SeedProjectWithOneDocument();
        var files = FilesOf(project, document);
        // The copy does not verify (its size differs from the source): the copy is deleted, the row and source untouched.
        var failing = new DocumentContainerRelocatorTests.Rig(files, copyFacts: f => f with { Size = f.Size + 1 });
        _fixture.RelocatorOverride = () => failing.Relocator;
        var client = _fixture.CreateAuthenticatedClient();

        var first = await client.PostAsJsonAsync(ProvisionRoute, new { projectId = project });

        first.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await JsonOf(first);
        problem.GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonFilesIncomplete);
        problem.GetProperty("filesIncomplete").GetInt32().Should().Be(1);
        problem.GetProperty("filesMoved").GetInt32().Should().Be(0);
        problem.GetProperty("incompleteDocuments")[0].GetGuid().Should().Be(document);
        _fixture.OwningTeamOf(project).Should().Be(ProvisionProjectTestFixture.SecureOwnerTeamId, "nothing done is undone");
        _fixture.ContainerIdOf(project).Should().Be(RecordContainer, "the record stays provisioned with its own container");
        PointerOf(files, document).Should().BeNull("the row still names its source");

        // The re-entry branch (the record is provisioned) relocates again.
        var working = new DocumentContainerRelocatorTests.Rig(files);
        _fixture.RelocatorOverride = () => working.Relocator;

        var second = await client.PostAsJsonAsync(ProvisionRoute, new { projectId = project });

        second.StatusCode.Should().Be(HttpStatusCode.OK, await second.Content.ReadAsStringAsync());
        var completed = await JsonOf(second);
        completed.GetProperty("childrenOnly").GetBoolean().Should().BeTrue("the record was provisioned by the first call");
        completed.GetProperty("files").GetProperty("moved").GetInt32().Should().Be(1);
        PointerOf(files, document).Should().Be(RecordContainer);
        _fixture.CreatedContainerDisplayNames.Should().ContainSingle("no second container is created");
    }

    [Fact(DisplayName = "Round 26 item 3: a row whose item id is a document id (the archive bug) is reported, never moved, and does not block")]
    public async Task MakeSecure_AnArchiveRowNamingADocumentId_IsReportedUnresolvable_AndDoesNotBlock()
    {
        var (project, document) = SeedProjectWithOneDocument();
        var files = FilesOf(project, document);
        files.Rows[("sprk_document", document)]["sprk_graphdriveid"] = DocumentContainerRelocatorTests.ArchiveContainer;
        files.Rows[("sprk_document", document)]["sprk_graphitemid"] = Guid.NewGuid().ToString("D");
        var rig = new DocumentContainerRelocatorTests.Rig(files);
        _fixture.RelocatorOverride = () => rig.Relocator;

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(ProvisionRoute, new { projectId = project });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await JsonOf(response);
        body.GetProperty("files").GetProperty("unresolvable").GetInt32().Should().Be(1);
        body.GetProperty("files").GetProperty("unresolvableDocuments")[0].GetGuid().Should().Be(document);
        files.Updates.Should().NotContain(u => u.Id == document, "it is never re-pointed");
        rig.Steps.Should().BeEmpty("nothing is downloaded, uploaded or deleted for it");
    }

    // ── Round 46 item 2: the scheduled backstop ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The job over task 148's world (the document is isolated, its file in a business unit's shared container — a move a
    /// files_incomplete left unmade, which no ledger records) with the REAL relocator over task 166's world.
    /// </summary>
    private sealed class BackstopHarness
    {
        private readonly IConfigurationRoot _configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        private readonly SecureChildReconciliationJob _job;

        public BackstopHarness(SecureChildShareWorld world, DocumentContainerRelocatorTests.Rig rig)
        {
            var services = new ServiceCollection();
            services.AddSingleton(SecureChildShareWorld.EntitiesOver(() => world).Object);
            services.AddScoped(_ => SecureChildShareWorld.ReconcilerOver(
                () => world, new FakeRecordShareTable(), null!));
            services.AddScoped(_ => SecureChildShareWorld.SynchronizerOver(() => world, new FakeRecordShareTable()));
            services.AddScoped(_ => rig.Relocator);
            services.AddSingleton(_ => SecureChildShareWorld.CoreAncestorsOver(() => world)); // task 173: as the host registers it
            var provider = services.BuildServiceProvider();
            foreach (var (key, value) in SecureChildShareWorld.Configuration().AsEnumerable())
                _configuration[key] = value;
            _job = new SecureChildReconciliationJob(
                provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, _configuration,
                NullLogger<SecureChildReconciliationJob>.Instance);
        }

        public async Task<(JobRunResult Result, JsonElement Relocations)> RunAsync(string? maxRelocations = null)
        {
            _configuration[SecureChildReconciliationJob.MaxRelocationsPerRunConfigKey] = maxRelocations;
            var result = await _job.ExecuteAsync(
                new JobRunContext(Guid.NewGuid(), "test", JobRunTrigger.Scheduled, new Dictionary<string, object>()),
                CancellationToken.None);
            using var doc = JsonDocument.Parse(result.ResultJson!);
            return (result, doc.RootElement.GetProperty("makeSecureRelocations").Clone());
        }
    }

    /// <summary>148's world: <paramref name="documents"/> isolated, their files in the shared business-unit container.</summary>
    private static SecureChildShareWorld StrandedWorld(params Guid[] documents)
    {
        var world = SecureChildShareWorld.Standard();
        world.Add("businessunit", DocumentContainerRelocatorTests.CustomerA, ("sprk_containerid", SharedContainer));
        foreach (var document in documents)
            world.Add("sprk_document", document,
                ("owningteam", new EntityReference("team", world.OwnerTeam)), ("sprk_graphdriveid", SharedContainer));
        return world;
    }

    [Fact(DisplayName = "Round 46 item 2: the reconciliation job settles a Make Secure move that a files_incomplete left unmade, in write mode")]
    public async Task TheJob_SettlesAStrandedIsolatedFile_InWriteMode()
    {
        var project = Guid.NewGuid();
        var document = Guid.NewGuid();
        var files = FilesOf(project, document);
        var rig = new DocumentContainerRelocatorTests.Rig(files);
        var harness = new BackstopHarness(StrandedWorld(document), rig);

        var (result, relocations) = await harness.RunAsync();

        relocations.GetProperty("mode").GetString().Should().Be(SecureChildReconciliationJob.ModeWrite);
        relocations.GetProperty("stranded").GetInt32().Should().Be(1);
        relocations.GetProperty("moved").GetInt32().Should().Be(1);
        relocations.GetProperty("incomplete").GetInt32().Should().Be(0);
        PointerOf(files, document).Should().Be(RecordContainer, "the file is in the record's own container now");
        result.Success.Should().BeTrue(result.ErrorMessage);
    }

    [Fact(DisplayName = "Round 46 item 2: a move the job cannot finish is reported and the run is not a success; it is retried next run")]
    public async Task TheJob_WhenAMoveFails_ReportsIt_AndTheRunIsNotASuccess()
    {
        var project = Guid.NewGuid();
        var document = Guid.NewGuid();
        var files = FilesOf(project, document);
        var rig = new DocumentContainerRelocatorTests.Rig(files, copyFacts: f => f with { Size = f.Size + 1 });
        var harness = new BackstopHarness(StrandedWorld(document), rig);

        var (result, relocations) = await harness.RunAsync();

        relocations.GetProperty("incomplete").GetInt32().Should().Be(1);
        relocations.GetProperty("incompleteDocuments")[0].GetGuid().Should().Be(document);
        result.Success.Should().BeFalse("a Make Secure file still owed is not a finished run");
        result.ErrorMessage.Should().Contain("Make Secure file relocation");
    }

    [Fact(DisplayName = "Round 46 item 2: the backstop moves at most its per-run cap, and the next run continues after it")]
    public async Task TheJob_MovesAtMostItsCapPerRun_AndTheNextRunContinues()
    {
        var project = Guid.NewGuid();
        var first = Guid.Parse("00000000-0000-4000-8000-0000000c0001");
        var second = Guid.Parse("00000000-0000-4000-8000-0000000c0002");
        var files = FilesOf(project, first);
        var other = World.Document(second, null, DocumentContainerRelocatorTests.CustomerA);
        other["sprk_project"] = new EntityReference("sprk_project", project);
        other["sprk_graphdriveid"] = SharedContainer;
        other["sprk_graphitemid"] = "01SECONDITEM";
        files.Rows[("sprk_document", second)] = other;
        files.Items[(SharedContainer, "01SECONDITEM")] = new SpeItemCreator(
            "second.docx", DocumentContainerRelocatorTests.Creator, null, 12, "h2");
        var world = StrandedWorld(first, second);
        var harness = new BackstopHarness(world, new DocumentContainerRelocatorTests.Rig(files));

        var (_, run1) = await harness.RunAsync(maxRelocations: "1");
        // The moved document now points at the record's own container (the job's world follows the re-point).
        world.Add("sprk_document", first,
            ("owningteam", new EntityReference("team", world.OwnerTeam)), ("sprk_graphdriveid", RecordContainer));
        var (_, run2) = await harness.RunAsync(maxRelocations: "1");

        run1.GetProperty("examined").GetInt32().Should().Be(1);
        run1.GetProperty("passComplete").GetBoolean().Should().BeFalse("one of two was taken");
        run2.GetProperty("examined").GetInt32().Should().Be(1);
        PointerOf(files, first).Should().Be(RecordContainer);
        PointerOf(files, second).Should().Be(RecordContainer, "the next run continued after the first");
    }
}

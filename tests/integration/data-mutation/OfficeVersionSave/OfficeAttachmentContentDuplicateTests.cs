using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.Api.Office;
using Sprk.Bff.Api.Tests.Services.Ai.Handlers;
using Sprk.Bff.Api.Tests.Shared.Office;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.OfficeVersionSave;

/// <summary>
/// spaarkeai-word-add-in-r1 task 143 (owner A1): an Attachment save whose bytes are identical to an existing Spaarke document
/// answers <c>contentDuplicate: true</c>, and names that document (<c>existingDocument</c>: id + <c>sprk_documentname</c>) only
/// when the caller holds Read on it AND the external document narrowing shows it. Without Read, a hidden document, or an access
/// check that faults, the identity is withheld and "already in Spaarke" stays. Nothing is created, written or re-filed.
/// </summary>
/// <remarks>
/// Module-boundary doubles only (ADR-038 §4): the real route, filters, <c>OfficeService</c>, <c>OfficeDocumentPersistence</c>
/// and <c>ContentDedupDetector</c> over <see cref="OfficeVersionSaveWorld"/>; the narrowing's caller and app-only reads are
/// stated (<see cref="StatedNarrowingCaller"/>, the REAL predicate behind the real seam), as the collision narrowing tests do.
/// </remarks>
[Trait("status", "task-143-word-add-in-r1")]
public class OfficeAttachmentContentDuplicateTests
{
    private const string ExistingDrive = "b!doc-drive";
    private const string ExistingName = "Exhibit A — Signed Lease";

    private static readonly byte[] ExhibitBytes = { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37 };
    private static readonly byte[] OtherBytes = { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34 };

    /// <summary>An attachment saved on its own (the pane's attachments-only loop), to a fresh record.</summary>
    private static SaveRequest AttachmentSave(byte[] bytes) => new()
    {
        ContentType = SaveContentType.Attachment,
        TargetEntity = new SaveEntityReference { EntityType = "matter", EntityId = Guid.NewGuid() },
        Attachment = new AttachmentMetadata
        {
            AttachmentId = $"att-{Guid.NewGuid():N}",
            FileName = "Exhibit A - received.pdf",
            ContentBase64 = Convert.ToBase64String(bytes),
        },
    };

    /// <summary>The Office host, with the caller and the narrowing's app-only world stated.</summary>
    private sealed class Host : IDisposable
    {
        private readonly Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> _factory;

        public Host()
        {
            _factory = new OfficeVersionSaveTestWebAppFactory(World).WithWebHostBuilder(builder =>
                builder.ConfigureTestServices(services => services.AddStatedNarrowingCaller(() => Mode, () => AppOnly)));
            Client = _factory.CreateClient();
            Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-caller-token");
        }

        public OfficeVersionSaveWorld World { get; } = new();

        public AppOnlyDocumentWorld AppOnly { get; } = new();

        public NarrowingCallerMode Mode { get; set; } = NarrowingCallerMode.Internal;

        public HttpClient Client { get; }

        /// <summary>The existing document holding the exhibit's bytes, with a display name.</summary>
        public Guid SeedExisting(AccessRights rights = AccessRights.Read | AccessRights.Write, string? documentName = ExistingName) =>
            World.SeedDocument(ExistingDrive, "Exhibit A.pdf", ExhibitBytes, rights: rights, documentName: documentName).DocumentId;

        public void Dispose()
        {
            Client.Dispose();
            _factory.Dispose();
        }
    }

    private static async Task<JsonElement> SaveAsync(Host host, SaveRequest request)
    {
        var response = await host.Client.PostAsJsonAsync("/api/office/save", request);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return document.RootElement.Clone();
    }

    private static void ShouldBeAlreadyInSpaarkeWithoutIdentity(JsonElement body)
    {
        body.GetProperty("success").GetBoolean().Should().BeTrue();
        body.GetProperty("contentDuplicate").GetBoolean().Should().BeTrue("the pane still says \"Already in Spaarke\"");
        body.TryGetProperty("existingDocument", out _).Should().BeFalse("the existing document's name and id are withheld");
    }

    private static void ShouldHaveChangedNothing(OfficeVersionSaveWorld world, Guid existingId)
    {
        world.DocumentCreates.Should().Be(0, "no second document is created");
        world.DocumentUpdates.Should().NotContain(u => u.DocumentId == existingId.ToString("D"), "the existing document is not written");
        world.GenericUpdates.Should().NotContain(u => u.Id == existingId, "nor re-filed");
        world.FinalizationPayloads.Should().BeEmpty("a suppressed duplicate is not finalized");
    }

    // ── Must fire ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Task 143 A1: a content-duplicate attachment save names the existing document to a caller who may read it")]
    public async Task ContentDuplicate_CallerWithRead_InternalCaller_NamesTheExistingDocument()
    {
        using var host = new Host();
        var existingId = host.SeedExisting();

        var body = await SaveAsync(host, AttachmentSave(ExhibitBytes));

        body.GetProperty("contentDuplicate").GetBoolean().Should().BeTrue();
        var existing = body.GetProperty("existingDocument");
        existing.GetProperty("id").GetGuid().Should().Be(existingId);
        existing.GetProperty("name").GetString().Should().Be(ExistingName, "the display name (sprk_documentname), not the file name");
        host.World.AccessChecks.Should().Contain(existingId.ToString("D"), "the caller's Read was asked");
        host.AppOnly.Calls.Should().Be(0, "an internal caller is not narrowed");
        ShouldHaveChangedNothing(host.World, existingId);
    }

    [Fact(DisplayName = "Task 143 A1: an external caller with Read on a document the narrowing shows is told its name")]
    public async Task ContentDuplicate_ExternalCaller_VisibleDocument_NamesIt()
    {
        using var host = new Host();
        var existingId = host.SeedExisting();
        host.AppOnly.Document(existingId);
        host.Mode = NarrowingCallerMode.External;

        var body = await SaveAsync(host, AttachmentSave(ExhibitBytes));

        body.GetProperty("existingDocument").GetProperty("id").GetGuid().Should().Be(existingId);
        host.AppOnly.Calls.Should().Be(1, "THE narrowing decided");
    }

    [Fact(DisplayName = "Task 143 A1: without Read on the existing document, its identity is withheld; \"already in Spaarke\" stays")]
    public async Task ContentDuplicate_CallerWithoutRead_WithholdsTheIdentity()
    {
        using var host = new Host();
        var existingId = host.SeedExisting(rights: AccessRights.None);

        var body = await SaveAsync(host, AttachmentSave(ExhibitBytes));

        ShouldBeAlreadyInSpaarkeWithoutIdentity(body);
        host.World.AccessChecks.Should().Contain(existingId.ToString("D"),
            "withheld because the gate evaluated the caller's rights and found no Read, not because nothing asked");
        ShouldHaveChangedNothing(host.World, existingId);
    }

    [Theory(DisplayName = "Task 143 A1: a document the external narrowing hides is withheld exactly like an unreadable one")]
    [MemberData(nameof(HiddenDocumentCases.All), MemberType = typeof(HiddenDocumentCases))]
    public async Task ContentDuplicate_ExternalCaller_HiddenDocument_WithholdsTheIdentity(string @case)
    {
        using var host = new Host();
        var existingId = host.SeedExisting();
        host.Mode = HiddenDocumentCases.Arrange(host.AppOnly, existingId, @case);

        var body = await SaveAsync(host, AttachmentSave(ExhibitBytes));

        ShouldBeAlreadyInSpaarkeWithoutIdentity(body);
        host.World.AccessChecks.Should().Contain(existingId.ToString("D"), "Read is asked first");
        host.AppOnly.Calls.Should().Be(1, "then THE narrowing decides (the caller may Read it)");
        ShouldHaveChangedNothing(host.World, existingId);
    }

    [Fact(DisplayName = "Task 143 A1: an access check that throws withholds the identity (fail closed)")]
    public async Task ContentDuplicate_AccessCheckThrows_WithholdsTheIdentity()
    {
        using var host = new Host();
        var existingId = host.SeedExisting();
        host.World.FaultingAccessChecks.Add(existingId);

        var body = await SaveAsync(host, AttachmentSave(ExhibitBytes));

        ShouldBeAlreadyInSpaarkeWithoutIdentity(body);
        host.World.AccessChecks.Should().Contain(existingId.ToString("D"));
        ShouldHaveChangedNothing(host.World, existingId);
    }

    [Fact(DisplayName = "Task 143 A1: when the existing document's name cannot be read, the answer is the bare \"already in Spaarke\"")]
    public async Task ContentDuplicate_ExistingDocumentHasNoName_SaysAlreadyInSpaarkeOnly()
    {
        using var host = new Host();
        var existingId = host.SeedExisting(documentName: null);

        var body = await SaveAsync(host, AttachmentSave(ExhibitBytes));

        ShouldBeAlreadyInSpaarkeWithoutIdentity(body);
        ShouldHaveChangedNothing(host.World, existingId);
    }

    // ── Must not fire ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Task 143 A1: must not fire — an attachment save that is not a duplicate carries neither field and asks no access check")]
    public async Task NotADuplicate_CarriesNoContentDuplicateFields()
    {
        using var host = new Host();
        host.SeedExisting();

        var body = await SaveAsync(host, AttachmentSave(OtherBytes));

        body.TryGetProperty("contentDuplicate", out _).Should().BeFalse();
        body.TryGetProperty("existingDocument", out _).Should().BeFalse();
        host.World.DocumentCreates.Should().Be(1, "a new attachment is saved as before");
        host.World.AccessChecks.Should().BeEmpty("no identity to gate");
    }
}

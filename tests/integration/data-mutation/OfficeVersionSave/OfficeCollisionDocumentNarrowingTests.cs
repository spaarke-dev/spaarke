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
/// unified-access-control-r2 task 188 round 4c (F1): a save-from-Office NAME COLLISION with a document THE external
/// document narrowing hides. Scenario: a systemuser flagged <c>sprk_isexternal</c> (a B2B guest) with Read and Write on the
/// colliding document through a share on matter M. Before: the refusal named the document, returned its id and offered
/// "Save as new version" (<c>canSaveAsVersion=true</c>). After: the refusal is the one a caller without Read gets — no
/// name, no id, no version retry — and nothing is written; the version retry itself is refused by
/// <c>OfficeVersionSaveAuthorizationFilter</c> (<c>DocumentAuthorizationFilter</c>, which narrows).
/// </summary>
/// <remarks>The real route, filters and Office pipeline over <see cref="OfficeVersionSaveWorld"/>; the caller and the
/// app-only reads of the narrowing are stated (<see cref="StatedNarrowingCaller"/>, the REAL predicate behind the real
/// seam).</remarks>
public class OfficeCollisionDocumentNarrowingTests
{
    private const string SaveContainer = "b!test-office-save-drive";
    private const string FileName = "Brief.docx";
    private const string DocumentName = "Project Falcon — Termination Letter";

    private static readonly byte[] Existing = MinimalDocx.Create("existing");
    private static readonly byte[] Incoming = MinimalDocx.Create("incoming");

    private static readonly SaveEntityReference Target = new() { EntityType = "matter", EntityId = Guid.NewGuid() };

    private static SaveRequest CreateSave() => new()
    {
        ContentType = SaveContentType.Document,
        TargetEntity = Target,
        Document = new DocumentMetadata { FileName = FileName, ContentBase64 = Convert.ToBase64String(Incoming) },
    };

    /// <summary>The Office host with the caller and the narrowing's app-only world stated.</summary>
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

        /// <summary>The colliding document: Read and Write for the caller, filed to the save's target.</summary>
        public (Guid Id, string ItemId) SeedCollidingDocument() =>
            World.SeedDocument(SaveContainer, FileName, Existing, documentName: DocumentName, matterId: Target.EntityId);

        public void Dispose()
        {
            Client.Dispose();
            _factory.Dispose();
        }
    }

    private static async Task<Dictionary<string, string?>> ReadProblemAsync(HttpResponseMessage response)
    {
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return document.RootElement.EnumerateObject()
            .Where(p => p.Value.ValueKind is JsonValueKind.String or JsonValueKind.True or JsonValueKind.False)
            .ToDictionary(
                p => p.Name,
                p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.GetBoolean() ? "true" : "false");
    }

    [Theory(DisplayName = "Task 188 r4c (F1): a collision with a document the narrowing hides is the no-Read refusal — no name, no id, no version retry — and writes nothing")]
    [MemberData(nameof(HiddenDocumentCases.All), MemberType = typeof(HiddenDocumentCases))]
    public async Task Collision_ExternalCaller_HiddenDocument_WithholdsItsIdentity_AndWritesNothing(string @case)
    {
        using var host = new Host();
        var (existingId, itemId) = host.SeedCollidingDocument();
        host.Mode = HiddenDocumentCases.Arrange(host.AppOnly, existingId, @case);

        var collision = await host.Client.PostAsJsonAsync("/api/office/save", CreateSave());

        collision.StatusCode.Should().Be(HttpStatusCode.Conflict, "the refusal itself is unchanged — only what it reveals");
        var problem = await ReadProblemAsync(collision);
        problem.Should().ContainKey("fileName").WhoseValue.Should().Be(FileName);
        problem.Should().NotContainKey("existingDocumentName");
        problem.Should().NotContainKey("existingDocumentId");
        problem.Should().NotContainKey("canSaveAsVersion");
        host.World.AccessChecks.Should().Contain(existingId.ToString("D"), "the caller's Read is asked first");
        host.AppOnly.Calls.Should().Be(1, "then THE narrowing decides (the caller may Read it)");
        host.World.SpeItems[itemId].Versions.Should().ContainSingle().Which.Should().Equal(Existing);
        host.World.DocumentCreates.Should().Be(0);
    }

    [Fact(DisplayName = "Task 188 r4c (F1): the version retry over a hidden colliding document is refused by the version-save filter and writes nothing")]
    public async Task VersionRetry_ExternalCaller_HiddenDocument_IsRefused_AndWritesNothing()
    {
        using var host = new Host();
        var (existingId, itemId) = host.SeedCollidingDocument();
        host.Mode = HiddenDocumentCases.Arrange(host.AppOnly, existingId, HiddenDocumentCases.OwnRestricted);

        var retry = await host.Client.PostAsJsonAsync("/api/office/save", OfficeVersionSaveWorld.VersionSave(existingId, Incoming));

        retry.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.World.ShouldHaveWrittenNothing();
        host.World.SpeItems[itemId].Versions.Should().ContainSingle();
    }

    [Fact(DisplayName = "Task 188 r4c (F1): must not fire — an internal caller's collision names the document and offers the version retry; no narrowing call, no app-only read")]
    public async Task Collision_InternalCaller_IsNotNarrowed()
    {
        using var host = new Host();
        var (existingId, _) = host.SeedCollidingDocument();
        HiddenDocumentCases.Arrange(host.AppOnly, existingId, HiddenDocumentCases.OwnRestricted);
        host.Mode = NarrowingCallerMode.Internal;

        var problem = await ReadProblemAsync(await host.Client.PostAsJsonAsync("/api/office/save", CreateSave()));

        problem.Should().ContainKey("existingDocumentId").WhoseValue.Should().Be(existingId.ToString("D"));
        problem.Should().ContainKey("existingDocumentName").WhoseValue.Should().Be(DocumentName);
        problem.Should().ContainKey("canSaveAsVersion").WhoseValue.Should().Be("true");
        host.AppOnly.Calls.Should().Be(0);
        host.AppOnly.AllReads.Should().BeEmpty();
    }

    [Fact(DisplayName = "Task 188 r4c (F1): must not fire — an external caller's collision with a document the narrowing shows names it")]
    public async Task Collision_ExternalCaller_VisibleDocument_NamesIt()
    {
        using var host = new Host();
        var (existingId, _) = host.SeedCollidingDocument();
        host.AppOnly.Document(existingId);
        host.Mode = NarrowingCallerMode.External;

        var problem = await ReadProblemAsync(await host.Client.PostAsJsonAsync("/api/office/save", CreateSave()));

        problem.Should().ContainKey("existingDocumentId").WhoseValue.Should().Be(existingId.ToString("D"));
        problem.Should().ContainKey("existingDocumentName").WhoseValue.Should().Be(DocumentName);
        problem.Should().ContainKey("canSaveAsVersion").WhoseValue.Should().Be("true");
        host.AppOnly.Calls.Should().Be(1);
    }
}

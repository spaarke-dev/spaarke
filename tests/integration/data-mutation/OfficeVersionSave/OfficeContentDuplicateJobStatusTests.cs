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
/// spaarkeai-word-add-in-r1 task 143 (owner A1): a save whose content resolved to an existing document through content de-dup
/// records that document in its job (<c>result.artifact</c>, phase <c>DeduplicatedToExisting</c>). The job-status read
/// (<c>GET /api/office/jobs/{id}</c>) and the stream (<c>/stream</c>, its <c>job-complete</c> event) name it only to a caller
/// who holds Read on it AND whom the external document narrowing shows it to — the rule the save's own answer obeys. Anyone
/// else, or an access check that faults, gets the job's status and phase with no artifact. Any other job is unchanged and asks
/// no access check.
/// </summary>
/// <remarks>
/// Module-boundary doubles only (ADR-038 §4): the real routes, filters, <c>OfficeService</c>, <c>OfficeJobStatusService</c>,
/// <c>OfficeDocumentPersistence</c> and <c>ContentDedupDetector</c> over <see cref="OfficeVersionSaveWorld"/>; the narrowing's
/// caller and app-only reads are stated (<see cref="StatedNarrowingCaller"/>), as <c>OfficeAttachmentContentDuplicateTests</c>
/// does. The caller saves as an internal user with Read; what it may see is then changed, so only the job read decides.
/// </remarks>
[Trait("status", "task-143-word-add-in-r1")]
public class OfficeContentDuplicateJobStatusTests
{
    private const string ExistingDrive = "b!doc-drive";

    private static readonly byte[] ExhibitBytes = { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37 };
    private static readonly byte[] OtherBytes = { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34 };

    /// <summary>An email save whose <c>.eml</c> the server builds from this metadata.</summary>
    private static SaveRequest EmailSave() => new()
    {
        ContentType = SaveContentType.Email,
        TargetEntity = new SaveEntityReference { EntityType = "matter", EntityId = Guid.NewGuid() },
        Email = new EmailMetadata
        {
            Subject = "Re: Exhibit",
            SenderEmail = "counsel@test.com",
            SentDate = new DateTimeOffset(2026, 9, 14, 9, 30, 0, TimeSpan.Zero),
            InternetMessageId = $"<exhibit-{Guid.NewGuid():N}@test.com>",
            Body = "Exhibit A attached.",
            IsNameSystemDerived = true,
        },
    };

    /// <summary>An attachment saved on its own (the pane's attachments-only loop).</summary>
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

    /// <summary>The <c>.eml</c> bytes the server builds for <paramref name="request"/>, so a seeded file holds exactly them.</summary>
    private static byte[] EmlBytesOf(SaveRequest request)
    {
        using var eml = Sprk.Bff.Api.Services.Office.OfficeEmailEnricher.BuildEmlFromMetadata(request.Email!);
        using var copy = new MemoryStream();
        eml.CopyTo(copy);
        return copy.ToArray();
    }

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

        public void Dispose()
        {
            Client.Dispose();
            _factory.Dispose();
        }
    }

    public enum ReadPath { Poll, Stream }

    public enum DuplicateSave { Email, Attachment }

    /// <summary>
    /// Saves a byte-identical duplicate of a seeded existing document as <paramref name="kind"/>, then clears the access-check
    /// record, so a test sees only what the job read asks. Returns the job id and the existing document.
    /// </summary>
    private static async Task<(Guid JobId, Guid ExistingId)> SaveDuplicateAsync(Host host, DuplicateSave kind)
    {
        var request = kind == DuplicateSave.Email ? EmailSave() : AttachmentSave(ExhibitBytes);
        var bytes = kind == DuplicateSave.Email ? EmlBytesOf(request) : ExhibitBytes;
        var existingId = host.World.SeedDocument(ExistingDrive, "Existing copy.bin", bytes, documentName: "Existing copy").DocumentId;

        var jobId = await SaveAsync(host, request);
        host.World.DocumentCreates.Should().Be(0, "precondition: the save resolved to the existing document");
        host.World.AccessChecks.Clear();
        return (jobId, existingId);
    }

    private static async Task<Guid> SaveAsync(Host host, SaveRequest request)
    {
        var response = await host.Client.PostAsJsonAsync("/api/office/save", request);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var saved = (await response.Content.ReadFromJsonAsync<SaveResponse>())!;
        return saved.JobId!.Value;
    }

    /// <summary>What the caller is told the job resolved to, on <paramref name="path"/>: the artifact's id (or null), and the
    /// job's status as that path states it.</summary>
    private static async Task<(Guid? DocumentId, string Status, string? WebUrl)> ReadJobAsync(Host host, Guid jobId, ReadPath path)
    {
        if (path == ReadPath.Poll)
        {
            var response = await host.Client.GetAsync($"/api/office/jobs/{jobId}");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            var root = body.RootElement;
            Guid? id = null;
            string? webUrl = null;
            if (root.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object
                && result.TryGetProperty("artifact", out var artifact) && artifact.ValueKind == JsonValueKind.Object)
            {
                id = artifact.GetProperty("id").GetGuid();
                webUrl = artifact.TryGetProperty("webUrl", out var url) && url.ValueKind == JsonValueKind.String ? url.GetString() : null;
            }

            return (id, root.GetProperty("status").GetString()!, webUrl);
        }

        var text = await StreamTextAsync(host, jobId);
        var complete = text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .SingleOrDefault(frame => frame.StartsWith("event: job-complete\n", StringComparison.Ordinal));
        complete.Should().NotBeNull($"the stream ends on the job's outcome; it sent:\n{text}");
        var data = complete!.Split('\n').Single(line => line.StartsWith("data: ", StringComparison.Ordinal))["data: ".Length..];
        using var payload = JsonDocument.Parse(data);
        var p = payload.RootElement;
        Guid? documentId = p.TryGetProperty("documentId", out var doc) && doc.ValueKind == JsonValueKind.String ? doc.GetGuid() : null;
        string? documentUrl = p.TryGetProperty("documentUrl", out var du) && du.ValueKind == JsonValueKind.String ? du.GetString() : null;
        return (documentId, p.GetProperty("status").GetString()!, documentUrl);
    }

    /// <summary>The whole stream: it ends by itself after the job's outcome, because the job is already terminal.</summary>
    private static async Task<string> StreamTextAsync(Host host, Guid jobId)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var stream = await host.Client.GetAsync(
            $"/api/office/jobs/{jobId}/stream", HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        stream.StatusCode.Should().Be(HttpStatusCode.OK);
        return await stream.Content.ReadAsStringAsync(deadline.Token);
    }

    /// <summary>The job's phase as the status read states it (the stream carries it in its progress snapshot).</summary>
    private static async Task<string?> PhaseOfAsync(Host host, Guid jobId)
    {
        using var body = await JsonDocument.ParseAsync(
            await (await host.Client.GetAsync($"/api/office/jobs/{jobId}")).Content.ReadAsStreamAsync());
        return body.RootElement.GetProperty("currentPhase").GetString();
    }

    // ── Must fire ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "Task 143 A1: a content-duplicate job names the existing document to a caller with Read whom nothing hides it from")]
    [InlineData(ReadPath.Poll, DuplicateSave.Email)]
    [InlineData(ReadPath.Poll, DuplicateSave.Attachment)]
    [InlineData(ReadPath.Stream, DuplicateSave.Email)]
    [InlineData(ReadPath.Stream, DuplicateSave.Attachment)]
    public async Task ContentDuplicateJob_CallerWithRead_InternalCaller_NamesTheExistingDocument(ReadPath path, DuplicateSave kind)
    {
        using var host = new Host();
        var (jobId, existingId) = await SaveDuplicateAsync(host, kind);

        var (documentId, status, webUrl) = await ReadJobAsync(host, jobId, path);

        documentId.Should().Be(existingId, "exactly today's answer for a caller who may see the document");
        webUrl.Should().BeNull("the duplicate's job never carried a file URL");
        status.Should().Be("Completed");
        host.World.AccessChecks.Should().Contain(existingId.ToString("D"), "the caller's Read was asked");
        host.AppOnly.Calls.Should().Be(0, "an internal caller is not narrowed");
    }

    [Theory(DisplayName = "Task 143 A1: an external caller with Read on a document the narrowing shows is told which document the job resolved to")]
    [InlineData(ReadPath.Poll)]
    [InlineData(ReadPath.Stream)]
    public async Task ContentDuplicateJob_ExternalCaller_VisibleDocument_NamesIt(ReadPath path)
    {
        using var host = new Host();
        var (jobId, existingId) = await SaveDuplicateAsync(host, DuplicateSave.Email);
        host.AppOnly.Document(existingId);
        host.Mode = NarrowingCallerMode.External;

        var (documentId, _, _) = await ReadJobAsync(host, jobId, path);

        documentId.Should().Be(existingId);
        host.AppOnly.Calls.Should().BeGreaterThan(0, "THE narrowing decided");
    }

    [Theory(DisplayName = "Task 143 A1: without Read on the existing document, the job's artifact is withheld; the job still reads Completed")]
    [InlineData(ReadPath.Poll, DuplicateSave.Email)]
    [InlineData(ReadPath.Poll, DuplicateSave.Attachment)]
    [InlineData(ReadPath.Stream, DuplicateSave.Email)]
    [InlineData(ReadPath.Stream, DuplicateSave.Attachment)]
    public async Task ContentDuplicateJob_CallerWithoutRead_WithholdsTheArtifact(ReadPath path, DuplicateSave kind)
    {
        using var host = new Host();
        var (jobId, existingId) = await SaveDuplicateAsync(host, kind);
        host.World.AccessByDocumentId[existingId] = AccessRights.None;

        var (documentId, status, _) = await ReadJobAsync(host, jobId, path);

        documentId.Should().BeNull("the existing document is named only to a caller who may see it");
        status.Should().Be("Completed", "the job's own outcome stays");
        host.World.AccessChecks.Should().Contain(existingId.ToString("D"),
            "withheld because the gate found no Read, not because nothing asked");
        (await PhaseOfAsync(host, jobId)).Should().Be(
            Sprk.Bff.Api.Services.Office.OfficeService.ContentDuplicatePhase, "the pane still learns the item is already in Spaarke");
        if (path == ReadPath.Stream)
        {
            // The pane's stream handler relies on this order: the snapshot names the phase, then job-complete has no document.
            var text = await StreamTextAsync(host, jobId);
            var phase = text.IndexOf(
                $"\"currentPhase\":\"{Sprk.Bff.Api.Services.Office.OfficeService.ContentDuplicatePhase}\"", StringComparison.Ordinal);
            phase.Should().BeGreaterThan(-1, "the stream's snapshot reports the phase");
            phase.Should().BeLessThan(text.IndexOf("event: job-complete", StringComparison.Ordinal));
        }
    }

    [Theory(DisplayName = "Task 143 A1: a document the external narrowing hides is withheld from the job status exactly like an unreadable one")]
    [MemberData(nameof(HiddenOnBothPaths))]
    public async Task ContentDuplicateJob_ExternalCaller_HiddenDocument_WithholdsTheArtifact(string @case, ReadPath path)
    {
        using var host = new Host();
        var (jobId, existingId) = await SaveDuplicateAsync(host, DuplicateSave.Email);
        host.Mode = HiddenDocumentCases.Arrange(host.AppOnly, existingId, @case);

        var (documentId, status, _) = await ReadJobAsync(host, jobId, path);

        documentId.Should().BeNull();
        status.Should().Be("Completed");
        host.World.AccessChecks.Should().Contain(existingId.ToString("D"), "Read is asked first");
        host.AppOnly.Calls.Should().BeGreaterThan(0, "then THE narrowing decides (the caller may Read it)");
    }

    public static TheoryData<string, ReadPath> HiddenOnBothPaths()
    {
        var data = new TheoryData<string, ReadPath>();
        foreach (var row in HiddenDocumentCases.All)
        {
            data.Add((string)row[0], ReadPath.Poll);
            data.Add((string)row[0], ReadPath.Stream);
        }

        return data;
    }

    [Theory(DisplayName = "Task 143 A1: an access check that throws withholds the job's artifact (fail closed)")]
    [InlineData(ReadPath.Poll)]
    [InlineData(ReadPath.Stream)]
    public async Task ContentDuplicateJob_AccessCheckThrows_WithholdsTheArtifact(ReadPath path)
    {
        using var host = new Host();
        var (jobId, existingId) = await SaveDuplicateAsync(host, DuplicateSave.Email);
        host.World.FaultingAccessChecks.Add(existingId);

        var (documentId, status, _) = await ReadJobAsync(host, jobId, path);

        documentId.Should().BeNull();
        status.Should().Be("Completed");
        host.World.AccessChecks.Should().Contain(existingId.ToString("D"));
    }

    // ── Must not fire ──────────────────────────────────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "Task 143 A1: must not fire — a job that created its document names it as before and asks no access check")]
    [InlineData(ReadPath.Poll)]
    [InlineData(ReadPath.Stream)]
    public async Task NotADuplicateJob_NamesTheCreatedDocument_WithoutAnAccessCheck(ReadPath path)
    {
        using var host = new Host();
        var seededId = host.World.SeedDocument(ExistingDrive, "Existing copy.bin", ExhibitBytes).DocumentId;
        var jobId = await SaveAsync(host, AttachmentSave(OtherBytes));
        host.World.DocumentCreates.Should().Be(1, "precondition: a new document was created");
        var createdId = host.World.Documents.Keys.Single(id => id != seededId);
        host.World.AccessChecks.Clear();
        host.World.AccessByDocumentId[createdId] = AccessRights.None; // were the gate to engage, it would withhold

        var (documentId, status, _) = await ReadJobAsync(host, jobId, path);

        documentId.Should().Be(createdId, "a job that is not a content duplicate is unchanged");
        status.Should().Be("Completed");
        host.World.AccessChecks.Should().BeEmpty("no existing document's identity to gate");
        host.AppOnly.Calls.Should().Be(0);
    }
}

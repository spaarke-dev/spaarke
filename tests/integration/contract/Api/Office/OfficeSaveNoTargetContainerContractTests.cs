using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Models.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Office;

/// <summary>
/// spaarkeai-word-add-in-r1 task 065 — finding <b>F4</b>: <c>POST /api/office/save</c> treats the
/// association as optional, so a request with no <c>TargetEntity</c> passes <c>EntityAccessFilter</c>
/// untouched (<c>EntityAccessFilter.cs</c>: an absent target calls <c>next()</c>) and the content lands
/// in a container no per-record check ever guarded. Upload and row-create are app-only, and
/// <c>TriggerAiProcessing</c> / <c>RagIndex</c> both default <see langword="true"/>.
/// </summary>
/// <remarks>
/// <para><b>What this file proves, and what it deliberately does NOT.</b> §1 is the REPRODUCE-FIRST
/// evidence for F4: it demonstrates, on shipped code, that a no-target save is accepted, that no
/// resource-authorization question is asked anywhere in the path, and that the bytes land in the
/// tenant-wide <c>EmailProcessing:DefaultContainerId</c> with AI processing requested. Those tests
/// stay GREEN after task 065's change, because <b>task 065 did not close F4</b> — closing it means
/// requiring <c>TargetEntity</c>, which the task ESCALATED rather than shipped
/// (<c>notes/065-require-target-entity.md</c> §"Escalation"). They are the harness whoever lands the
/// requirement will invert.</para>
///
/// <para>§2 is what task 065 DID ship: the record-less branch no longer resolves the tenant-wide
/// default FIRST. It asks <c>RecordContainerResolver.ResolveForActingUserAsync</c> — task 076's
/// owner-sanctioned answer for content created before its owning record exists — and uses the
/// configured default only when that cannot answer. That is a NARROWING of where unassociated content
/// lands (per-business-unit rather than per-tenant), not an authorization gate.</para>
///
/// <para><b>Non-regression is the point of the pair.</b> Almost every save the spine's test corpus
/// posts carries no target, and neither does any Word ribbon quick-save, any FR-11 version save, or
/// any pane save with no "Related to" selected. A change to this branch that fails closed on an
/// unresolvable caller would take all of that with it — so the configured-default fallback is
/// retained, and it is pinned by §1's container assertion, which runs under the base fixture's own
/// caller <c>oid</c> (<c>"test-user-oid"</c> — not a GUID, therefore not a Dataverse user).</para>
///
/// <para>ADR-038: KEEP path <c>tests/integration/contract/**</c>. The doubles are module boundaries only
/// — <see cref="CallerRecordAccessProbe"/> (its <c>virtual</c> method is the designated seam),
/// <see cref="SpeFileStore"/> (the ADR-007 facade) and <see cref="IDataverseService"/>. No
/// <c>Mock&lt;HttpMessageHandler&gt;</c>, no DI-registration assertion, no ctor null-check.</para>
/// </remarks>
[Trait("Category", "OfficeSaveNoTargetContainer")]
public class OfficeSaveNoTargetContainerContractTests
{
    /// <summary>The tenant-wide default the base fixture configures (<c>EmailProcessing:DefaultContainerId</c>).</summary>
    private const string TenantDefaultContainer = "b!test-office-save-drive";

    // ─────────────────────────────────────────────────────────────────────────────
    // §1 REPRODUCE-FIRST — F4, on shipped code. These stay green after task 065.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// THE reproduction. A save naming no target is ACCEPTED, and no resource-authorization question is
    /// asked about anything, by anyone, anywhere in the path.
    /// </summary>
    /// <remarks>
    /// The probe is the single primitive behind every per-record check on this route
    /// (<c>EntityAccessFilter</c>, <c>TodoSourceAccessFilter</c>, <c>QuickCreateSourceAccessFilter</c>),
    /// so "the probe was never consulted" is the direct statement of "no resource authorization ran" —
    /// not a proxy for it. <c>OfficeVersionSaveAuthorizationFilter</c> is the one other resource gate on
    /// this route and it is scoped to version saves; this body is not one, asserted by the absence of
    /// <c>document.existingDocumentId</c>.
    /// </remarks>
    [Fact]
    public async Task PostOfficeSave_WithNoTargetEntity_IsAccepted_AndNoResourceAuthorizationRuns()
    {
        using var factory = new NoTargetSaveFactory(NoTargetSaveFactory.CallerOid.NonResolvable);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/office/save", DocumentSaveWithNoTarget());

        response.StatusCode.Should().BeOneOf(
            [HttpStatusCode.Accepted, HttpStatusCode.OK],
            "F4's premise: association is optional, so the request is accepted rather than refused");

        factory.ProbedRecords.Should().BeEmpty(
            "this is the finding. CallerRecordAccessProbe is the one primitive every per-record check on "
            + "this route goes through, so an empty recording means NO resource authorization ran at all — "
            + "not 'a weaker check ran'. The upload and the sprk_document create that follow are both "
            + "app-only");
    }

    /// <summary>
    /// The unauthorized bytes land in the tenant-wide default container — and this assertion is ALSO
    /// the non-regression pin for task 065's narrowing, because this caller's <c>oid</c> is not a GUID
    /// and therefore cannot resolve to a Dataverse user.
    /// </summary>
    /// <remarks>
    /// This is the case the existing save-spine suites are overwhelmingly in. If the record-less branch
    /// is ever changed to THROW when the acting user cannot be resolved, this test goes red first and
    /// names the reason.
    /// </remarks>
    [Fact]
    public async Task PostOfficeSave_WithNoTargetEntity_AndAnUnresolvableCaller_StillLandsInTheConfiguredDefaultContainer()
    {
        using var factory = new NoTargetSaveFactory(NoTargetSaveFactory.CallerOid.NonResolvable);
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/office/save", DocumentSaveWithNoTarget());

        factory.UploadedToContainers.Should().Contain(TenantDefaultContainer,
            "with no record to derive from and a caller who resolves to no Dataverse user, the configured "
            + "tenant default is the only remaining answer — and it must stay the answer, because the "
            + "entire save-spine corpus and every Word ribbon quick-save are in exactly this shape");
    }

    /// <summary>
    /// The content that reached an unauthorized container is then profiled and RAG-indexed, because both
    /// flags default true and nothing on the no-target path reconsiders them.
    /// </summary>
    /// <remarks>
    /// The indexing itself is the background finalization worker's, so what is asserted here is the
    /// instruction that reaches it: the <c>ProcessingJob</c> payload the save writes. Asserting the
    /// model defaults as well keeps the claim honest in both halves — a caller who says nothing gets
    /// AI processing, and the job row records that it was requested.
    /// </remarks>
    [Fact]
    public async Task PostOfficeSave_WithNoTargetEntity_RequestsAiProcessingAndRagIndexingByDefault()
    {
        new SaveRequest { ContentType = SaveContentType.Document }.TriggerAiProcessing
            .Should().BeTrue("SaveRequest.TriggerAiProcessing defaults true");
        new AiProcessingOptionsRequest().RagIndex
            .Should().BeTrue("AiProcessingOptionsRequest.RagIndex defaults true");

        using var factory = new NoTargetSaveFactory(NoTargetSaveFactory.CallerOid.NonResolvable);
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/office/save", DocumentSaveWithNoTarget());

        factory.JobPayloads.Should().Contain(p => p.Contains("\"TriggerAiProcessing\":true"),
            "the ProcessingJob payload is what the finalization worker reads, so this is the instruction "
            + "that actually carries AI processing + RAG indexing forward for content no per-record check "
            + "ever guarded");

        factory.JobPayloads.Should().Contain(
            p => p.Contains("\"TargetEntity\":null") && p.Contains($"\"ContainerId\":\"{TenantDefaultContainer}\""),
            "F4 in one row: a Dataverse ProcessingJob that records, in its own payload, that unassociated "
            + "content was routed to the tenant-wide container and queued for AI processing");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // §2 WHAT TASK 065 SHIPPED — the record-less branch narrows before it defaults.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A resolvable caller's record-less save lands in THEIR business unit's container, not the
    /// tenant-wide default.
    /// </summary>
    /// <remarks>
    /// The resolver is real and <c>sealed</c>; only its Dataverse boundary is arranged, through the
    /// shared <c>TestActingUserBusinessUnit</c> whose matchers pin the actual derivation
    /// (<c>systemuser</c> filtered on <c>azureactivedirectoryobjectid</c> → that user's
    /// <c>businessunit</c> → <c>sprk_containerid</c>). A production regression that looked the user up by
    /// a different column would stop matching and fail this test rather than pass it.
    /// </remarks>
    [Fact]
    public async Task PostOfficeSave_WithNoTargetEntity_AndAResolvableCaller_LandsInThatCallersBusinessUnitContainer()
    {
        using var factory = new NoTargetSaveFactory(NoTargetSaveFactory.CallerOid.Resolvable);
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/office/save", DocumentSaveWithNoTarget());

        factory.UploadedToContainers.Should().Contain(TestActingUserBusinessUnit.ContainerId,
            "task 076's ResolveForActingUserAsync is the owner-sanctioned answer for content created "
            + "before its owning record exists — the Word ribbon quick-save's exact shape");
        factory.UploadedToContainers.Should().NotContain(TenantDefaultContainer,
            "the tenant-wide default is the LAST resort, not the first answer; reaching it when the "
            + "acting user's business unit has a container stamped is the behaviour task 065 removed");
    }

    /// <summary>
    /// A resolvable caller whose business unit has NO container stamped still saves — via the configured
    /// default.
    /// </summary>
    /// <remarks>
    /// An unstamped business unit is a legitimate and common state (3 of 6 live units, verified
    /// 2026-08-27 by unified-access-control-r2), so it must not be an outage. This is the second of the
    /// two fallback routes, and it is distinct from the unresolvable-caller route above: that one throws
    /// inside the resolver, this one returns an empty decision.
    /// </remarks>
    [Fact]
    public async Task PostOfficeSave_WithNoTargetEntity_WhenTheCallersBusinessUnitHasNoContainer_FallsBackToTheConfiguredDefault()
    {
        using var factory = new NoTargetSaveFactory(NoTargetSaveFactory.CallerOid.ResolvableWithNoContainer);
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/office/save", DocumentSaveWithNoTarget());

        factory.UploadedToContainers.Should().Contain(TenantDefaultContainer,
            "a business unit with no sprk_containerid is a configuration state, not an error — refusing "
            + "here would make an unstamped business unit a save outage for everyone in it");
    }

    /// <summary>
    /// The narrowing must not reach a save that HAS a target: that container comes from the record the
    /// caller was authorized against, and nothing else may displace it.
    /// </summary>
    /// <remarks>
    /// This is the assertion that stops task 065's change from becoming the isolation failure
    /// <c>RecordContainerResolver.ResolveForRecordAsync</c>'s own remarks warn about — users sit in the
    /// Operations subtree while secure records are owned in Secure Projects, so acting-user resolution
    /// applied to a RECORD would write a secure record's content into the general Operations container.
    /// The narrowing is confined to the branch where no record exists, and this pins that confinement.
    /// </remarks>
    [Fact]
    public async Task PostOfficeSave_WithATargetEntity_DoesNotConsultTheActingUsersBusinessUnit()
    {
        var matterId = Guid.Parse("6f6f6f6f-0000-0000-0000-00000000000b");
        using var factory = new NoTargetSaveFactory(NoTargetSaveFactory.CallerOid.Resolvable);
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/office/save", DocumentSaveTargeting("matter", matterId));

        factory.ProbedRecords.Should().Contain(("sprk_matters", matterId),
            "a named target is still authorized against its own collection — the association gate is "
            + "untouched by this task");
        factory.UploadedToContainers.Should().NotContain(TestActingUserBusinessUnit.ContainerId,
            "when a record exists the container follows the RECORD. Letting the uploader's business unit "
            + "win here is exactly the isolation failure unified-access-control-r2 closed");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // §3 account / contact — the two target types sprk_document cannot be filed to.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>account</c> and <c>contact</c> are accepted association targets that <c>sprk_document</c> has
    /// no lookup column for. Their DEFINED behaviour, pinned here: they are authorized like any other
    /// target, and their container comes from that record — they do NOT fall into the record-less
    /// branch.
    /// </summary>
    /// <remarks>
    /// <para>The association itself is dropped at persistence (<c>DocumentAssociationMap</c> has no
    /// column to write), which <c>OfficeEndpoints.ValidateSaveRequest</c> records as a standing owner
    /// decision — accept and log loudly, rather than silently reject a user-visible flow. Task 065 does
    /// NOT change that decision; it pins the half that was undefined, which is where the bytes go.</para>
    /// <para>Why it matters that this is stated rather than assumed: a reading of F4 that "account and
    /// contact have no lookup column, so they behave like a no-target save" would be wrong in both
    /// halves — they ARE gated (the probe is consulted) and they do NOT reach the tenant default. The
    /// test exists because the wrong reading is the plausible one.</para>
    /// </remarks>
    [Theory]
    [InlineData("account", "accounts")]
    [InlineData("contact", "contacts")]
    public async Task PostOfficeSave_TargetingAccountOrContact_IsStillGated_AndDoesNotTakeTheRecordLessBranch(
        string entityType, string expectedEntitySet)
    {
        var recordId = Guid.NewGuid();
        using var factory = new NoTargetSaveFactory(NoTargetSaveFactory.CallerOid.Resolvable);
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/office/save", DocumentSaveTargeting(entityType, recordId));

        factory.ProbedRecords.Should().Contain((expectedEntitySet, recordId),
            "a document that cannot be ASSOCIATED to the record can still be FILED against it, so the "
            + "AppendTo gate is the right one and must not be skipped for these two types");
        factory.UploadedToContainers.Should().NotContain(TestActingUserBusinessUnit.ContainerId,
            "these carry a record, so ResolveForRecordAsync answers and the record-less branch is never "
            + "reached — the failure this assertion exists to catch is them falling through to it "
            + "'because there is no lookup column anyway'");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Bodies
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The Word ribbon quick-save's body, shape-for-shape: <c>buildDocumentSaveRequest</c>
    /// (<c>quickSaveHelpers.ts</c>) sends contentType Document, AI options, an idempotency key and
    /// <b>no</b> <c>targetEntity</c> and no <c>existingDocumentId</c>.
    /// </summary>
    private static object DocumentSaveWithNoTarget() => new
    {
        contentType = 2, // SaveContentType.Document
        document = new
        {
            fileName = "quick-save.docx",
            title = "quick-save",
            contentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            contentBase64 = MinimalDocxBase64
        },
        idempotencyKey = Guid.NewGuid().ToString("N")
    };

    private static object DocumentSaveTargeting(string entityType, Guid entityId) => new
    {
        contentType = 2,
        document = new
        {
            fileName = "filed.docx",
            title = "filed",
            contentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            contentBase64 = MinimalDocxBase64
        },
        targetEntity = new { entityType, entityId },
        idempotencyKey = Guid.NewGuid().ToString("N")
    };

    /// <summary>
    /// An empty ZIP central directory — the smallest byte sequence the save path's content sniffing
    /// accepts as a <c>.docx</c>. A bare "PK" signature classifies CORRUPT once document identity is
    /// stamped into the uploaded bytes (the trap <c>OfficeVersionSaveContractTests</c> records).
    /// </summary>
    private static readonly string MinimalDocxBase64 = Convert.ToBase64String(
        Encoding.ASCII.GetBytes("PK\u0005\u0006").Concat(new byte[18]).ToArray());

    // ─────────────────────────────────────────────────────────────────────────────
    // Fixture
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <see cref="OfficeTestWebAppFactory"/> plus the three observations this file needs — which SPE
    /// container each upload went to, which records were authorization-probed, and what the
    /// <c>ProcessingJob</c> payload said — and a caller identity the test chooses.
    /// </summary>
    /// <remarks>
    /// Derived rather than re-declared (CLAUDE.md §11): every configuration key, DI substitution and
    /// Graph/ServiceBus double the save path needs already exists on the base factory, and duplicating
    /// ~200 lines of it to add three recorders is how fixtures drift apart. What is overridden here is
    /// only what the base factory has no way to expose: the auth handler's <c>oid</c>, and three
    /// boundary doubles it constructs privately.
    /// </remarks>
    private sealed class NoTargetSaveFactory : OfficeTestWebAppFactory
    {
        /// <summary>Which caller identity the host authenticates as, and how it resolves.</summary>
        internal enum CallerOid
        {
            /// <summary>The base fixture's own <c>"test-user-oid"</c> — not a GUID, so no Dataverse user.</summary>
            NonResolvable,

            /// <summary><see cref="TestSessionOwner.Oid"/>, arranged to a business unit WITH a container.</summary>
            Resolvable,

            /// <summary><see cref="TestSessionOwner.Oid"/>, arranged to a business unit with NO container.</summary>
            ResolvableWithNoContainer
        }

        private readonly CallerOid _caller;

        public NoTargetSaveFactory(CallerOid caller) => _caller = caller;

        /// <summary>The SPE container id of every upload the save path performed.</summary>
        public ConcurrentBag<string> UploadedToContainers { get; } = new();

        /// <summary>Every (entitySet, recordId) any per-record authorization check asked about.</summary>
        public ConcurrentBag<(string EntitySet, Guid RecordId)> ProbedRecords { get; } = new();

        /// <summary>The serialized payload of every <c>ProcessingJob</c> the save created.</summary>
        public ConcurrentBag<string> JobPayloads { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureTestServices(services =>
            {
                // ── caller identity ───────────────────────────────────────────────────────────────
                // The base fixture's handler issues oid "test-user-oid", which cannot be a Dataverse
                // user. Registering a second scheme and repointing the defaults is the only way to
                // change it without editing the shared fixture; this ConfigureTestServices runs AFTER
                // the base's, so this PostConfigure wins.
                if (_caller != CallerOid.NonResolvable)
                {
                    services.AddAuthentication()
                        .AddScheme<AuthenticationSchemeOptions, GuidOidAuthHandler>(GuidOidScheme, _ => { });
                    services.PostConfigure<Microsoft.AspNetCore.Authentication.AuthenticationOptions>(o =>
                    {
                        o.DefaultAuthenticateScheme = GuidOidScheme;
                        o.DefaultChallengeScheme = GuidOidScheme;
                    });
                }

                // ── Dataverse boundary ────────────────────────────────────────────────────────────
                // Re-declared rather than reached into: the base factory builds its Mock<IDataverseService>
                // inside its own closure. The two setups below are the base's, verbatim in effect; the
                // third records the job payload and the acting-user arrangement is layered on top.
                var dataverse = new Mock<IDataverseService>();
                dataverse.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
                dataverse
                    .Setup(d => d.CreateDocumentAsync(
                        It.IsAny<CreateDocumentRequest>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(() => Guid.NewGuid().ToString());
                dataverse
                    .Setup(d => d.CreateProcessingJobAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((object job, CancellationToken _) =>
                    {
                        // The job's own Payload member, DECODED. OfficeService serializes the request
                        // into it as a nested JSON string, so serializing the job once leaves every
                        // inner quote escaped — asserting against that outer form would be asserting
                        // against an encoding artefact rather than against the payload.
                        using var document = System.Text.Json.JsonDocument.Parse(
                            System.Text.Json.JsonSerializer.Serialize(job));
                        JobPayloads.Add(
                            document.RootElement.TryGetProperty("Payload", out var payload)
                                ? payload.GetString() ?? string.Empty
                                : string.Empty);
                        return Guid.NewGuid();
                    });

                if (_caller == CallerOid.Resolvable)
                {
                    TestActingUserBusinessUnit.Arrange(dataverse);
                }
                else if (_caller == CallerOid.ResolvableWithNoContainer)
                {
                    TestActingUserBusinessUnit.ArrangeWithNoContainer(dataverse);
                }

                services.RemoveAll<IDataverseService>();
                services.AddSingleton(dataverse.Object);

                // ── SPE boundary ──────────────────────────────────────────────────────────────────
                // The ADR-007 facade, doubled exactly as the base factory doubles it, with the drive id
                // recorded. The drive id IS the container on this path: OfficeStorageUploader passes
                // OfficeService's derived container straight through.
                var graphClientFactory = Mock.Of<IGraphClientFactory>();
                var speFileStore = new Mock<SpeFileStore>(
                    MockBehavior.Loose,
                    new ContainerOperations(graphClientFactory, Mock.Of<ILogger<ContainerOperations>>()),
                    new DriveItemOperations(graphClientFactory, Mock.Of<ILogger<DriveItemOperations>>()),
                    new UploadSessionManager(
                        graphClientFactory,
                        Mock.Of<IHttpClientFactory>(),
                        Mock.Of<ILogger<UploadSessionManager>>()),
                    new UserOperations(graphClientFactory, Mock.Of<ILogger<UserOperations>>()),
                    null!);

                speFileStore
                    .Setup(s => s.UploadSmallAsync(
                        It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(),
                        It.IsAny<ConflictBehavior>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((string driveId, string path, Stream _, ConflictBehavior _, CancellationToken _) =>
                    {
                        UploadedToContainers.Add(driveId);
                        return Uploaded(path);
                    });
                speFileStore
                    .Setup(s => s.UploadSmallAsync(
                        It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((string driveId, string path, Stream _, CancellationToken _) =>
                    {
                        UploadedToContainers.Add(driveId);
                        return Uploaded(path);
                    });

                services.RemoveAll<SpeFileStore>();
                services.AddScoped(_ => speFileStore.Object);

                // ── authorization boundary ────────────────────────────────────────────────────────
                // Permissive AND recording. Permissive so a denial never masquerades as "the gate did
                // not run"; recording so "no resource authorization ran" is an observation rather than
                // an inference from a status code.
                services.RemoveAll<CallerRecordAccessProbe>();
                services.AddScoped<CallerRecordAccessProbe>(_ => new RecordingProbe(ProbedRecords));
            });
        }

        private const string GuidOidScheme = "TestGuidOid";

        private static FileHandleDto? Uploaded(string path) => new(
            Id: $"item-{Guid.NewGuid():N}",
            Name: path,
            ParentId: null,
            Size: 3,
            CreatedDateTime: DateTimeOffset.UtcNow,
            LastModifiedDateTime: DateTimeOffset.UtcNow,
            ETag: null,
            IsFolder: false,
            WebUrl: "https://contoso.sharepoint.com/sites/test/Shared%20Documents/quick-save.docx");

        /// <summary>The base fixture's handler with one claim changed: a GUID <c>oid</c>.</summary>
        private sealed class GuidOidAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
        {
            public GuidOidAuthHandler(
                Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
                ILoggerFactory logger,
                System.Text.Encodings.Web.UrlEncoder encoder)
                : base(options, logger, encoder) { }

            protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            {
                var identity = new ClaimsIdentity(
                    [
                        new Claim("oid", TestSessionOwner.Oid),
                        new Claim(ClaimTypes.NameIdentifier, TestSessionOwner.Oid),
                        new Claim(ClaimTypes.Email, "test@example.com"),
                        new Claim("tid", "test-tenant-id")
                    ],
                    GuidOidScheme);

                return Task.FromResult(AuthenticateResult.Success(
                    new AuthenticationTicket(new ClaimsPrincipal(identity), GuidOidScheme)));
            }
        }

        /// <summary>
        /// <see cref="CallerRecordAccessProbe"/>'s designated test seam — its <c>virtual</c>
        /// <c>GetCallerRightsAsync</c> (ADR-038 §4) — answering "all rights" and recording the question.
        /// </summary>
        private sealed class RecordingProbe : CallerRecordAccessProbe
        {
            private readonly ConcurrentBag<(string, Guid)> _probed;

            public RecordingProbe(ConcurrentBag<(string, Guid)> probed)
                : base(new HttpClient(),
                       new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
                       NullLogger<CallerRecordAccessProbe>.Instance)
                => _probed = probed;

            public override Task<AccessRights> GetCallerRightsAsync(
                string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
            {
                _probed.Add((entitySet, recordId));
                return Task.FromResult(
                    AccessRights.Read | AccessRights.Write | AccessRights.AppendTo | AccessRights.Append);
            }
        }
    }
}

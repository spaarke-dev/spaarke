// TESTS ONLY (spaarkeai-mcp-bff-api-r2 task 023). The module-boundary world the two document tools run against, shared by
// DocumentToolsContractTests (over the MCP route) and DocumentToolsNotFoundTests (the handlers called directly):
//   - IDocumentDataverseService (the Dataverse document reads) as a Moq double over a small in-memory set of rows;
//   - Spaarke.Core.Auth.IAuthorizationService (the per-document Read decision) as a double that allows exactly the documents it is told to,
//     and only for a request that carries a bearer token (the real service denies without one);
//   - CallerRecordAccessProbe (the caller's Read on the matter) as the shipped type with its rights question answered from a table;
//   - SpeFileStore (the app-only SPE facade) as a recording double that answers the same fixed values the REST frozen-bytes tests pin;
//   - RecordContainerResolver (the pointer check) as the shared test resolver; the external-narrowing seam as a scripted delegate.
// Nothing here is an HttpMessageHandler or IServiceClient mock (ADR-038).

using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Identity;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using IAuthorizationService = Spaarke.Core.Auth.IAuthorizationService;

namespace Sprk.Bff.Api.Tests.Api.Mcp.Documents;

internal sealed class DocumentToolsWorld
{
    public static readonly Guid CallerOid = Guid.Parse("66666666-7777-4888-9999-aaaaaaaaaaaa");
    public static readonly Guid MatterId = Guid.Parse("23000000-0000-4000-8000-000000000001");
    public static readonly Guid OtherMatterId = Guid.Parse("23000000-0000-4000-8000-000000000002");
    /// <summary>A matter that exists but the caller holds no Read on.</summary>
    public static readonly Guid RestrictedMatterId = Guid.Parse("23000000-0000-4000-8000-000000000003");
    public static readonly Guid MissingMatterId = Guid.Parse("23000000-0000-4000-8000-0000000000ee");

    /// <summary>A document the caller may read.</summary>
    public const string ReadableDocumentId = "23000000-0000-4000-8000-0000000000a1";

    /// <summary>A second document the caller may read, older than <see cref="ReadableDocumentId"/>.</summary>
    public const string SecondReadableDocumentId = "23000000-0000-4000-8000-0000000000a2";

    /// <summary>A document that exists but the caller may not read.</summary>
    public const string ForbiddenDocumentId = "23000000-0000-4000-8000-0000000000f0";

    /// <summary>An id no row carries.</summary>
    public const string MissingDocumentId = "23000000-0000-4000-8000-0000000000ee";

    /// <summary>A readable document with no file attached (no SPE pointer).</summary>
    public const string NoFileDocumentId = "23000000-0000-4000-8000-0000000000b1";

    public const string TraceId = "trace-023";

    public static readonly DateTime Newest = new(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>The bytes the REST frozen tests pin for the same fixed SPE answers (FileAccessLinkRoutesFrozenBytesTests).</summary>
    public const string ExpectedPreviewUrl = "https://example.invalid/embed.aspx?token=redacted&nb=true";
    public const string ExpectedWebUrl = "https://contoso.sharepoint.com/contentstorage/x/_layouts/15/Doc.aspx?sourcedoc=1";
    public const string ExpectedDesktopUrl = "ms-word:" + ExpectedWebUrl;

    public Dictionary<string, DocumentEntity> Rows { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Documents filed under a matter, in no particular order.</summary>
    public Dictionary<Guid, List<DocumentEntity>> ByMatter { get; } = new();

    /// <summary>Document ids <see cref="Authorization"/> allows.</summary>
    public HashSet<string> Readable { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Matters the caller holds Read on.</summary>
    public HashSet<Guid> ReadableMatters { get; } = [];

    /// <summary>Document ids the external document narrowing hides (used only when <see cref="CallerIsExternal"/>).</summary>
    public HashSet<Guid> HiddenByNarrowing { get; } = [];

    public bool CallerIsExternal { get; set; }

    /// <summary>When true the narrowing delegate is not registered, so an external caller's narrowing faults (it must fail closed).</summary>
    public bool NarrowingUnavailable { get; set; }

    public Mock<IDocumentDataverseService> Documents { get; } = new();

    public RecordingAuthorization Authorization { get; } = new();

    public RightsProbe Probe { get; }

    public ConcurrentQueue<string> SpeCalls { get; } = new();

    public DocumentToolsWorld()
    {
        Probe = new RightsProbe(this);
        Authorization.Readable = Readable;

        Add(Document(ReadableDocumentId, "Master services agreement.docx", 120_345, Newest), MatterId, readable: true);
        Add(Document(SecondReadableDocumentId, "Disclosure schedule.pdf", 2_048, Newest.AddDays(-3)), MatterId, readable: true);
        Add(Document(ForbiddenDocumentId, "Settlement memo.docx", 9_999, Newest.AddDays(-1)), RestrictedMatterId, readable: false);
        var noFile = Document(NoFileDocumentId, "Scan placeholder", null, Newest.AddDays(-9));
        noFile.HasFile = false;
        noFile.GraphDriveId = null;
        noFile.GraphItemId = null;
        noFile.FileName = null;
        Add(noFile, OtherMatterId, readable: true);
        ReadableMatters.Add(MatterId);
        ReadableMatters.Add(OtherMatterId);

        Documents
            .Setup(d => d.GetDocumentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) => Rows.GetValueOrDefault(id));
        Documents
            .Setup(d => d.GetDocumentsByMatterAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid matter, Guid? _, CancellationToken _) =>
                (IEnumerable<DocumentEntity>)(ByMatter.TryGetValue(matter, out var rows) ? rows.ToList() : []));
    }

    public static DocumentEntity Document(string id, string name, long? size, DateTime modified) =>
        new()
        {
            Id = id,
            Name = name,
            FileName = name.Contains('.') ? name : null,
            FileSize = size,
            MimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            GraphDriveId = $"b!drive-{id}",
            GraphItemId = $"item-{id}",
            HasFile = true,
            ModifiedOn = modified,
            Description = "SECRET-DESCRIPTION",
            Summary = "SECRET-SUMMARY",
        };

    public void Add(DocumentEntity document, Guid matter, bool readable)
    {
        document.MatterId = matter.ToString("D");
        document.MatterName = "Acme v. Roadrunner";
        Rows[document.Id] = document;
        if (!ByMatter.TryGetValue(matter, out var list))
        {
            ByMatter[matter] = list = [];
        }

        list.Add(document);
        if (readable)
        {
            Readable.Add(document.Id);
        }
    }

    /// <summary>The services both hosts register: the doubles above plus the real tools' own dependencies.</summary>
    public void Register(IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(Documents.Object);
        services.AddSingleton<IAuthorizationService>(Authorization);
        services.AddSingleton<CallerRecordAccessProbe>(Probe);
        services.AddSingleton<SpeFileStore>(_ => new RecordingStore(SpeCalls));
        services.AddScoped(_ => TestRecordContainerResolver.ForBusinessUnitContainers(c => c.StartsWith("b!drive-", StringComparison.Ordinal)));

        var callers = new Mock<ICallerSystemUserResolver>();
        callers.Setup(c => c.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Resolved(Guid.NewGuid().ToString("D")));
        var identity = new Mock<ISystemUserIdentityResolver>();
        identity.Setup(i => i.IsExternalAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => CallerIsExternal);
        services.AddSingleton(callers.Object);
        services.AddSingleton(identity.Object);

        if (!NarrowingUnavailable)
        {
            services.AddSingleton<InternalRouteDocumentNarrowing>((ids, _) => Task.FromResult(Narrowed(ids)));
        }
    }

    private DocumentNarrowingResult Narrowed(IReadOnlyCollection<Guid> ids) =>
        new(
            ids.ToDictionary(
                id => new DocumentNarrowingRow(id, DocumentPermission.NotNarrowed, Guid.Empty, Guid.Empty),
                id => HiddenByNarrowing.Contains(id) ? DocumentNarrowingReason.OwnRestricted : DocumentNarrowingReason.Visible),
            faulted: false);

    /// <summary>A request as the MCP route makes it for the caller: authenticated with an <c>oid</c>, carrying a bearer token.</summary>
    public static DefaultHttpContext RequestFor(IServiceProvider services, Guid? oid = null, bool authenticated = true)
    {
        var http = new DefaultHttpContext { RequestServices = services, TraceIdentifier = TraceId };
        http.Request.Headers.Authorization = "Bearer opaque-test-token";
        if (authenticated)
        {
            http.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("oid", (oid ?? CallerOid).ToString("D"))], authenticationType: "Test"));
        }

        return http;
    }

    /// <summary>Allows exactly <see cref="Readable"/>, and only with a bearer token (the real service denies without one). Records every ask.</summary>
    internal sealed class RecordingAuthorization : IAuthorizationService
    {
        public HashSet<string> Readable { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public ConcurrentQueue<string> Asked { get; } = new();

        public Task<AuthorizationResult> AuthorizeAsync(AuthorizationContext context, CancellationToken ct = default)
        {
            Asked.Enqueue($"{context.Operation}:{context.ResourceId}:{context.UserId}");
            var allowed = context.Operation == "read" && !string.IsNullOrEmpty(context.UserAccessToken) && Readable.Contains(context.ResourceId);
            return Task.FromResult(new AuthorizationResult
            {
                IsAllowed = allowed,
                ReasonCode = allowed ? "sdap.access.allow.test" : "sdap.access.deny.insufficient_rights",
                RuleName = "TestRule",
            });
        }
    }

    /// <summary>The shipped probe, with only its rights question answered from <see cref="ReadableMatters"/> (Read, else none).</summary>
    internal sealed class RightsProbe : CallerRecordAccessProbe
    {
        private readonly DocumentToolsWorld _world;

        public RightsProbe(DocumentToolsWorld world)
            : base(
                new HttpClient(),
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AzureAd:TenantId"] = "00000000-0000-0000-0000-0000000000aa",
                    ["AzureAd:ClientId"] = "00000000-0000-0000-0000-0000000000bb",
                    ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
                }).Build(),
                NullLogger<CallerRecordAccessProbe>.Instance,
                new OrderedCredentialClientProvider(
                    Options.Create(new CredentialSelectionOptions { Order = ["ManagedIdentityFederated"] }),
                    new ConfigurationBuilder().Build(),
                    NullLogger<OrderedCredentialClientProvider>.Instance))
        {
            _world = world;
        }

        public ConcurrentQueue<Guid> Asked { get; } = new();

        public override Task<AccessRights> GetCallerRightsAsync(string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
        {
            Asked.Enqueue(recordId);
            return Task.FromResult(
                !string.IsNullOrEmpty(callerBearerToken) && entitySet == "sprk_matters" && _world.ReadableMatters.Contains(recordId)
                    ? AccessRights.Read
                    : AccessRights.None);
        }
    }

    /// <summary>The app-only SPE facade, answering the fixed values the REST frozen-bytes tests pin and recording each call.</summary>
    private sealed class RecordingStore : SpeFileStore
    {
        private readonly ConcurrentQueue<string> _calls;

        public RecordingStore(ConcurrentQueue<string> calls)
            : this(calls, Mock.Of<IGraphClientFactory>())
        {
        }

        private RecordingStore(ConcurrentQueue<string> calls, IGraphClientFactory factory)
            : this(calls, factory, TestSpeOwnership.AllowAll(factory))
        {
        }

        private RecordingStore(ConcurrentQueue<string> calls, IGraphClientFactory factory, SpeContainerOwnershipGuard guard)
            : base(
                new ContainerOperations(factory, guard, NullLogger<ContainerOperations>.Instance),
                new DriveItemOperations(factory, guard, NullLogger<DriveItemOperations>.Instance),
                new UploadSessionManager(factory, guard, Mock.Of<IHttpClientFactory>(), NullLogger<UploadSessionManager>.Instance),
                new UserOperations(factory, NullLogger<UserOperations>.Instance))
        {
            _calls = calls;
        }

        public override Task<string?> GetEmbedPreviewUrlAsync(
            string driveId, string itemId, IDictionary<string, object>? additionalData = null, CancellationToken ct = default)
        {
            _calls.Enqueue($"preview:{driveId}/{itemId}");
            return Task.FromResult<string?>("https://example.invalid/embed.aspx?token=redacted");
        }

        public override Task<SpeDriveItemSummary?> GetDriveItemAsync(
            string driveId, string itemId, IEnumerable<string>? selectFields = null, CancellationToken ct = default)
        {
            _calls.Enqueue($"driveitem:{driveId}/{itemId}");
            return Task.FromResult<SpeDriveItemSummary?>(new SpeDriveItemSummary(
                itemId, "Brief.docx", 3, ExpectedWebUrl, null,
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document", null, null, null));
        }
    }
}

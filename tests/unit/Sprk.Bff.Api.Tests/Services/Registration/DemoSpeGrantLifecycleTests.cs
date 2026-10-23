using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Graph;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Abstractions.Store;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Registration;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.IdentityBinding.IdentityBindingTestKit;
using Membership = Sprk.Bff.Api.Infrastructure.ExternalAccess.SpeContainerMembershipService;

namespace Sprk.Bff.Api.Tests.Services.Registration;

/// <summary>
/// #1363 (unified-access-control-r2, task 171 follow-up) — the demo self-registration SPE grant across its whole life:
/// Step 8 grants → the BFF's standing pass runs → demo expiry revokes → the standing pass runs again.
/// </summary>
/// <remarks>
/// <para>The defect: Step 8 recorded the grant as a STANDING marker keyed by the systemuserid it had just created in the
/// DEMO environment's Dataverse. The standing pass resolves standing holders in the BFF's own Dataverse, so on a container
/// it syncs that marker could never be resolved — kept and counted unknown on every run. And expiry deleted the permission
/// directly (first page only, no <c>Prefer</c> header), leaving the marker behind.</para>
/// <para>Mocking boundary (ADR-038 §4): the marked-grant primitives of <see cref="Membership"/> are virtual; they are
/// replaced by an in-memory container that keeps roles and markers the way the primitives' contract states, so the
/// SEQUENCE is observable. The legacy (pre-task-171) delete goes through the real Graph SDK over a recording
/// <see cref="IRequestAdapter"/> (the <c>SpeContainerPagingTests</c> convention — the SDK's own seam, not a transport mock).</para>
/// </remarks>
public class DemoSpeGrantLifecycleTests
{
    private const string DemoContainer = "b!demo-container";
    private const string DemoUpn = "jane.doe@demo.spaarke.com";
    private static readonly Guid EntraObjectId = Guid.Parse("13630000-0000-4000-8000-0000000000e1");
    private static readonly Guid BffUnit = Guid.Parse("13630000-0000-4000-8000-0000000000b1");

    private readonly InMemoryContainer _container = new();
    private readonly RecordingAdapter _graph = new();
    private readonly Mock<IGenericEntityService> _bffDataverse = new();

    public DemoSpeGrantLifecycleTests()
    {
        _container.ObjectIdsByUpn[DemoUpn] = EntraObjectId;

        // The BFF's own Dataverse: the demo container is ALSO stamped on one of its business units (the worst case of
        // #1363 — the container is one the standing pass syncs), and the demo user does not exist there.
        _bffDataverse.Setup(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) => new EntityCollection(q.EntityName == "businessunit"
                ? [new Entity("businessunit", BffUnit) { ["sprk_containerid"] = DemoContainer }]
                : [])
            { MoreRecords = false });
        _bffDataverse.Setup(d => d.RetrieveAsync("systemuser", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Entity>(null!)); // not a user of this Dataverse
    }

    private static string DemoKey => Membership.MarkerKey(Membership.DemoWriterMarkerPrefix, EntraObjectId);

    // ── Step 8 ─────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>MUTATION: recording the grant under <see cref="Membership.StandingWriterMarkerPrefix"/> (task 171's shape).</summary>
    [Fact(DisplayName = "#1363: Step 8 records a DEMO marker keyed by the Entra object id — never a standing marker")]
    public async Task Provision_RecordsADemoMarker_KeyedByTheEntraObjectId()
    {
        await Provisioning().GrantSpeContainerAccessAsync(DemoContainer, EntraObjectId.ToString(), DemoUpn, CancellationToken.None);

        _container.Markers.Keys.Should().Equal(DemoKey);
        _container.Roles.Should().ContainSingle(r => r.UserPrincipalName == DemoUpn && r.IsPlainWriter);
    }

    [Fact(DisplayName = "#1363: Step 8 with an Entra id that is not a GUID grants nothing and fails the step")]
    public async Task Provision_WithANonGuidEntraId_GrantsNothing()
    {
        var act = () => Provisioning().GrantSpeContainerAccessAsync(DemoContainer, "not-a-guid", DemoUpn, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _container.Roles.Should().BeEmpty();
        _container.Markers.Should().BeEmpty();
    }

    // ── The whole life ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// MUTATION (either half): a standing marker makes both standing passes count the demo user UNKNOWN (the holder is not
    /// in the BFF's Dataverse); expiry's direct delete leaves the marker behind.
    /// </summary>
    [Fact(DisplayName = "#1363: provision → standing pass → expire → standing pass: never unknown, no re-grant, no orphan marker")]
    public async Task Provision_Expire_StandingPass_LeavesNoGrantAndNoMarker()
    {
        await Provisioning().GrantSpeContainerAccessAsync(DemoContainer, EntraObjectId.ToString(), DemoUpn, CancellationToken.None);

        var whileProvisioned = await Sync().SyncStandingWritersAsync(CancellationToken.None);
        whileProvisioned.Unknown.Should().Be(0, "the standing pass never counts a demo grant it does not own");
        whileProvisioned.Removed.Should().Be(0);
        whileProvisioned.Failed.Should().Be(0);
        _container.Roles.Should().ContainSingle("the standing pass leaves the demo grant alone while the demo runs");

        await Expiration().RevokeSpeContainerAccessAsync(DemoContainer, EntraObjectId.ToString(), DemoUpn, CancellationToken.None);

        _container.Roles.Should().BeEmpty("expiry removes the grant");
        _container.Markers.Should().BeEmpty("expiry clears the marker with the grant — none is orphaned");
        _container.Removals.Should().Equal(DemoKey);

        var afterExpiry = await Sync().SyncStandingWritersAsync(CancellationToken.None);
        afterExpiry.Granted.Should().Be(0, "an expired demo user is not re-granted");
        afterExpiry.Unknown.Should().Be(0);
        afterExpiry.MarkersCleared.Should().Be(0);
        _container.Roles.Should().BeEmpty();
        _container.Markers.Should().BeEmpty();
    }

    // ── Expiry ─────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>MUTATION: matching only the demo key strands every grant task 171 recorded under a standing key.</summary>
    [Fact(DisplayName = "#1363: expiry also removes a task-171-era STANDING marker that names the user's permission, and clears it")]
    public async Task Expire_RemovesATask171EraStandingMarker_ThatNamesTheUsersPermission()
    {
        var demoEnvironmentSystemUserId = Guid.Parse("13630000-0000-4000-8000-0000000000d5");
        var legacyKey = Membership.MarkerKey(Membership.StandingWriterMarkerPrefix, demoEnvironmentSystemUserId);
        _container.Roles.Add(new("perm-171", ["writer"], DemoUpn, EntraObjectId.ToString()));
        _container.Markers[legacyKey] = "perm-171";
        var otherKey = Membership.MarkerKey(Membership.StandingWriterMarkerPrefix, Guid.NewGuid());
        _container.Roles.Add(new("perm-other", ["writer"], "other@contoso.example", Guid.NewGuid().ToString()));
        _container.Markers[otherKey] = "perm-other";

        await Expiration().RevokeSpeContainerAccessAsync(DemoContainer, EntraObjectId.ToString(), DemoUpn, CancellationToken.None);

        _container.Removals.Should().Equal(legacyKey);
        _container.Markers.Keys.Should().Equal(new[] { otherKey }, "another user's standing grant is untouched");
        _container.Roles.Select(r => r.PermissionId).Should().Equal("perm-other");
    }

    /// <summary>MUTATION: treating a failed removal as done (or swallowing it) hides a grant that may still stand.</summary>
    [Fact(DisplayName = "#1363: a removal that cannot be proven (partial role list) throws and KEEPS the marker")]
    public async Task Expire_UnprovenRemoval_ThrowsAndKeepsTheMarker()
    {
        _container.Markers[DemoKey] = "perm-unseen";
        _container.RolesComplete = false;

        var act = () => Expiration().RevokeSpeContainerAccessAsync(DemoContainer, EntraObjectId.ToString(), DemoUpn, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _container.Markers.Should().ContainKey(DemoKey, "the grant stays recorded, never orphaned");
    }

    [Fact(DisplayName = "#1363: a container read that fails throws and changes nothing")]
    public async Task Expire_ReadFault_ThrowsAndChangesNothing()
    {
        _container.Roles.Add(new("perm-demo", ["writer"], DemoUpn, EntraObjectId.ToString()));
        _container.Markers[DemoKey] = "perm-demo";
        _container.ReadFault = new HttpRequestException("Graph unavailable");

        var act = () => Expiration().RevokeSpeContainerAccessAsync(DemoContainer, EntraObjectId.ToString(), DemoUpn, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
        _container.Markers.Should().ContainKey(DemoKey);
        _container.Roles.Should().ContainSingle();
        _graph.Deletes.Should().BeEmpty();
    }

    /// <summary>
    /// #1830 (task 192) must-not-fire, MUTATION: mapping Graph's "does not resolve" 400 to null inside the membership
    /// service's reads — expiry would read "the container does not exist; nothing to revoke" and report a clean expiry.
    /// Only the JIT removal sweep classifies that 400; here it must stay a fault.
    /// </summary>
    [Fact(DisplayName = "#1830: a Graph 400 'Invalid hostname for this tenancy' on the container read still THROWS at expiry — never 'nothing to revoke'")]
    public async Task Expire_UnresolvableContainer_StillThrows()
    {
        var graph = new SpeContainerGraphStub { ContainerReadError = SpeContainerGraphStub.InvalidHostname() };
        var membership = new Membership(
            TestSpeOwnership.AllowAll(Mock.Of<IGraphClientFactory>(f => f.ForApp() == graph.Client)), NullLogger<Membership>.Instance);

        var act = () => Expiration(membership).RevokeSpeContainerAccessAsync(DemoContainer, EntraObjectId.ToString(), DemoUpn, CancellationToken.None);

        await act.Should().ThrowAsync<Microsoft.Graph.Models.ODataErrors.ODataError>();
        graph.Requests.Should().ContainSingle().Which.Should().StartWith("GET ");
    }

    /// <summary>MUTATION: reading "not in a partial list" as "holds nothing" reports a clean expiry while access may remain.</summary>
    [Fact(DisplayName = "#1363: no marker and no role in a PARTIAL role list throws — the absence is unproven")]
    public async Task Expire_NoMarkerAndNoRole_InAPartialList_Throws()
    {
        _container.RolesComplete = false;

        var act = () => Expiration().RevokeSpeContainerAccessAsync(DemoContainer, EntraObjectId.ToString(), DemoUpn, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact(DisplayName = "#1363: no marker and no role in a COMPLETE role list is a benign absence")]
    public async Task Expire_NoMarkerAndNoRole_InACompleteList_DoesNothing()
    {
        await Expiration().RevokeSpeContainerAccessAsync(DemoContainer, EntraObjectId.ToString(), DemoUpn, CancellationToken.None);

        _container.Removals.Should().BeEmpty();
        _graph.Deletes.Should().BeEmpty();
    }

    /// <summary>
    /// MUTATION: dropping the unmarked fallback leaves every pre-task-171 demo grant in place at expiry; dropping the
    /// <c>Prefer</c> header also removes the user's item-level permissions.
    /// </summary>
    [Fact(DisplayName = "#1363: an UNMARKED plain writer role (granted before task 171) is deleted container-scoped")]
    public async Task Expire_UnmarkedPlainWriter_IsDeletedWithThePreferHeader()
    {
        _container.Roles.Add(new("perm-pre171", ["writer"], DemoUpn, EntraObjectId.ToString()));

        await Expiration().RevokeSpeContainerAccessAsync(DemoContainer, EntraObjectId.ToString(), DemoUpn, CancellationToken.None);

        var delete = _graph.Deletes.Should().ContainSingle().Subject;
        delete.Url.Should().EndWith("/permissions/perm-pre171");
        delete.Prefer.Should().Be(Membership.OnlyContainerScopedPrefer);
        _container.Removals.Should().BeEmpty("there is no marker to clear");
    }

    /// <summary>
    /// MUTATION: matching the role by object id only (<c>IsFor(upn: null, oid)</c>). Graph keys a container grant by UPN
    /// and may return one with no object id; such a task-171-era grant would stay standing, with its marker, forever.
    /// </summary>
    [Fact(DisplayName = "#1363: a task-171 STANDING-marked grant that carries only the UPN (no object id) is still removed")]
    public async Task Expire_Task171StandingGrant_KeyedByUpnOnly_IsRemoved()
    {
        var legacyKey = Membership.MarkerKey(Membership.StandingWriterMarkerPrefix, Guid.Parse("13630000-0000-4000-8000-0000000000d6"));
        _container.Roles.Add(new("perm-171-upn", ["writer"], DemoUpn, null));
        _container.Markers[legacyKey] = "perm-171-upn";

        await Expiration().RevokeSpeContainerAccessAsync(DemoContainer, EntraObjectId.ToString(), DemoUpn, CancellationToken.None);

        _container.Removals.Should().Equal(legacyKey);
        _container.Markers.Should().BeEmpty();
    }

    /// <summary>MUTATION: as above, for an unmarked pre-task-171 plain writer that carries only the UPN.</summary>
    [Fact(DisplayName = "#1363: an UNMARKED pre-171 plain writer that carries only the UPN (no object id) is still deleted")]
    public async Task Expire_UnmarkedPlainWriter_KeyedByUpnOnly_IsDeleted()
    {
        _container.Roles.Add(new("perm-pre171-upn", ["writer"], DemoUpn.ToUpperInvariant(), null));

        await Expiration().RevokeSpeContainerAccessAsync(DemoContainer, EntraObjectId.ToString(), DemoUpn, CancellationToken.None);

        _graph.Deletes.Should().ContainSingle().Which.Url.Should().EndWith("/permissions/perm-pre171-upn");
    }

    [Fact(DisplayName = "#1363: an unmarked role that is not a plain writer (never made by Step 8) is left in place")]
    public async Task Expire_UnmarkedOwnerRole_IsLeftInPlace()
    {
        _container.Roles.Add(new("perm-owner", ["owner"], DemoUpn, EntraObjectId.ToString()));

        await Expiration().RevokeSpeContainerAccessAsync(DemoContainer, EntraObjectId.ToString(), DemoUpn, CancellationToken.None);

        _graph.Deletes.Should().BeEmpty();
        _container.Removals.Should().BeEmpty();
    }

    // ── The marker is reserved ─────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "#1363: a demo marker is a grant marker (reserved in the SPE admin editor) and is not a standing or JIT key")]
    public void DemoMarker_IsReserved_AndParsedByNeitherSyncPass()
    {
        Membership.IsGrantMarkerName(DemoKey).Should().BeTrue();
        Membership.TryParseMarkerKey(DemoKey, Membership.StandingWriterMarkerPrefix, out _).Should().BeFalse();
        Membership.TryParseMarkerKey(DemoKey, Membership.JitWriterMarkerPrefix, out _).Should().BeFalse();
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────────

    private DemoProvisioningService Provisioning()
    {
        var options = Options.Create(new DemoProvisioningOptions { DemoUsersGroupId = "group-1" });
        return new DemoProvisioningService(
            new GraphUserService(Mock.Of<IGraphClientFactory>(), new PasswordGenerator(), options, NullLogger<GraphUserService>.Instance),
            RegistrationDataverse(),
            new RegistrationEmailService(null!, Options.Create(new CommunicationOptions()), NullLogger<RegistrationEmailService>.Instance),
            new PasswordGenerator(),
            _container,
            options,
            NullLogger<DemoProvisioningService>.Instance);
    }

    private DemoExpirationJob Expiration(Membership? membership = null)
    {
        var options = Options.Create(new DemoProvisioningOptions { DemoUsersGroupId = "group-1" });
        var factory = new Mock<IGraphClientFactory>();
        factory.Setup(f => f.ForApp()).Returns(new GraphServiceClient(_graph));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DATAVERSE_URL"] = "https://admin.crm.dynamics.com" })
            .Build();
        return new DemoExpirationJob(
            new GraphUserService(Mock.Of<IGraphClientFactory>(), new PasswordGenerator(), options, NullLogger<GraphUserService>.Instance),
            RegistrationDataverse(),
            new RegistrationEmailService(null!, Options.Create(new CommunicationOptions()), NullLogger<RegistrationEmailService>.Instance),
            TestSpeOwnership.AllowAll(factory.Object),
            membership ?? _container,
            new DataverseEnvironmentService(
                configuration,
                new StaticTokenCredential(),
                Mock.Of<IHttpClientFactory>(f => f.CreateClient(It.IsAny<string>()) == new HttpClient()),
                NullLogger<DataverseEnvironmentService>.Instance),
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            options,
            NullLogger<DemoExpirationJob>.Instance);
    }

    private SpeContainerMembershipSync Sync()
    {
        var registry = new Mock<ISecurableEntityRegistry>();
        registry.Setup(r => r.GetSecurableEntitiesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new HashSet<string>());
        return new SpeContainerMembershipSync(
            _bffDataverse.Object, _container, registry.Object, Mock.Of<IDataverseRecordShareService>(),
            NullLogger<SpeContainerMembershipSync>.Instance);
    }

    /// <summary>Required by both constructors; no test here reaches Dataverse through it.</summary>
    private static RegistrationDataverseService RegistrationDataverse()
        => new(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["DATAVERSE_URL"] = "https://admin.crm.dynamics.com" })
                .Build(),
            new TrackingIdGenerator(),
            new StaticTokenCredential(),
            Mock.Of<IHttpClientFactory>(f => f.CreateClient(It.IsAny<string>()) == new HttpClient()),
            NullLogger<RegistrationDataverseService>.Instance,
            new ContactIdentityBinderFactory(
                Mock.Of<IHttpClientFactory>(), NullLoggerFactory.Instance, Tenants(CustomerTenant), new FakeTimeProvider(Now)),
            new NullMembershipCacheInvalidator(NullLogger<NullMembershipCacheInvalidator>.Instance));

    /// <summary>
    /// One container's roles and grant markers, kept the way the marked-grant primitives' contract states
    /// (<see cref="Membership.GrantMarkedWriterAsync"/>: 409 when a role is held, else a writer role plus its marker;
    /// <see cref="Membership.RemoveMarkedGrantAsync"/>: an unseen permission in a partial list is Failed, a plain writer is
    /// deleted, and the marker is cleared).
    /// </summary>
    internal sealed class InMemoryContainer()
        : Membership(TestSpeOwnership.AllowAll(Mock.Of<IGraphClientFactory>()), NullLogger<Membership>.Instance)
    {
        private int _next;

        public List<ContainerUserRole> Roles { get; } = [];
        public Dictionary<string, string> Markers { get; } = new(StringComparer.Ordinal);
        public bool RolesComplete { get; set; } = true;
        public Exception? ReadFault { get; set; }
        public Dictionary<string, Guid> ObjectIdsByUpn { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Removals { get; } = [];

        public override Task<ContainerAccess?> ReadAccessAsync(string containerId, CancellationToken ct = default)
        {
            if (ReadFault is not null)
                throw ReadFault;
            return Task.FromResult<ContainerAccess?>(
                new ContainerAccess(Roles.ToList(), RolesComplete, new Dictionary<string, string>(Markers, StringComparer.Ordinal)));
        }

        public override Task<IReadOnlyDictionary<string, string>?> ReadMarkersAsync(string containerId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, string>?>(new Dictionary<string, string>(Markers, StringComparer.Ordinal));

        public override Task<MarkedGrantOutcome> GrantMarkedWriterAsync(
            string containerId, string markerPrefix, Guid systemUserId, string userPrincipalName, CancellationToken ct = default)
        {
            if (Roles.Any(r => string.Equals(r.UserPrincipalName, userPrincipalName, StringComparison.OrdinalIgnoreCase)))
                return Task.FromResult(MarkedGrantOutcome.AlreadyHeld);

            var permissionId = $"perm-{++_next}";
            Roles.Add(new ContainerUserRole(
                permissionId, ["writer"], userPrincipalName,
                ObjectIdsByUpn.TryGetValue(userPrincipalName, out var oid) ? oid.ToString() : null));
            Markers[MarkerKey(markerPrefix, systemUserId)] = permissionId;
            return Task.FromResult(MarkedGrantOutcome.Granted);
        }

        public override Task<MarkedRemovalOutcome> RemoveMarkedGrantAsync(
            string containerId, string markerKey, string permissionId, ContainerAccess access, CancellationToken ct = default)
        {
            var current = access.Roles.FirstOrDefault(r => r.PermissionId == permissionId);
            if (current is null && !access.RolesComplete)
                return Task.FromResult(MarkedRemovalOutcome.Failed);

            Removals.Add(markerKey);
            var outcome = MarkedRemovalOutcome.MarkerCleared;
            if (current is not null && current.IsPlainWriter)
            {
                Roles.RemoveAll(r => r.PermissionId == permissionId);
                outcome = MarkedRemovalOutcome.Removed;
            }

            Markers.Remove(markerKey);
            return Task.FromResult(outcome);
        }
    }

    /// <summary>Records each DELETE the real Graph SDK sends (URL and <c>Prefer</c> header); nothing else is on this path.</summary>
    private sealed class RecordingAdapter : IRequestAdapter
    {
        public List<(string Url, string? Prefer)> Deletes { get; } = [];

        public ISerializationWriterFactory SerializationWriterFactory => throw new NotSupportedException();

        public string? BaseUrl { get; set; } = "https://graph.microsoft.com/v1.0";

        public void EnableBackingStore(IBackingStoreFactory backingStoreFactory) { }

        public Task SendNoContentAsync(
            RequestInformation requestInfo,
            Dictionary<string, ParsableFactory<IParsable>>? errorMapping = default,
            CancellationToken cancellationToken = default)
        {
            requestInfo.HttpMethod.Should().Be(Method.DELETE);
            Deletes.Add((requestInfo.URI.ToString(),
                requestInfo.Headers.TryGetValue("Prefer", out var prefer) ? string.Join(",", prefer) : null));
            return Task.CompletedTask;
        }

        public Task<ModelType?> SendAsync<ModelType>(
            RequestInformation requestInfo, ParsableFactory<ModelType> factory,
            Dictionary<string, ParsableFactory<IParsable>>? errorMapping = default, CancellationToken cancellationToken = default)
            where ModelType : IParsable
            => throw new NotSupportedException("Only the permission DELETE reaches Graph directly on this path.");

        public Task<IEnumerable<ModelType>?> SendCollectionAsync<ModelType>(
            RequestInformation requestInfo, ParsableFactory<ModelType> factory,
            Dictionary<string, ParsableFactory<IParsable>>? errorMapping = default, CancellationToken cancellationToken = default)
            where ModelType : IParsable
            => throw new NotSupportedException();

        public Task<ModelType?> SendPrimitiveAsync<ModelType>(
            RequestInformation requestInfo,
            Dictionary<string, ParsableFactory<IParsable>>? errorMapping = default, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IEnumerable<ModelType>?> SendPrimitiveCollectionAsync<ModelType>(
            RequestInformation requestInfo,
            Dictionary<string, ParsableFactory<IParsable>>? errorMapping = default, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<NativeResponseType?> ConvertToNativeRequestAsync<NativeResponseType>(
            RequestInformation requestInfo, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StaticTokenCredential : TokenCredential
    {
        private static AccessToken Token => new("test-token-not-a-credential", DateTimeOffset.UtcNow.AddHours(1));

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => Token;

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(Token);
    }
}

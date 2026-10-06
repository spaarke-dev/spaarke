using System.Text.RegularExpressions;
using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.SpeAdmin;
using Sprk.Bff.Api.Tests.Contract.SpeAdmin;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.SpeAdmin;

/// <summary>
/// unified-access-control-r2 task 165 — the bulk delete / bulk permissions background job acts on a container only
/// when it is of the config's container type AND bound to a business unit the caller reaches (sweep findings #44,
/// #72; owner round 20 item 2: the bulk routes authorize PER CONTAINER).
/// </summary>
/// <remarks>
/// <para>
/// The tenant-scope filter confines the CONFIG a bulk request names to the caller's business units; the container ids
/// are still caller-chosen, the job runs app-only with no caller context, and one container type serves several
/// customers (Model 1). Each item is now read with the config's Graph client — its type AND its business-unit stamp —
/// and written only when the caller's scope captured at acceptance reaches it. Every refusal — another type, a type
/// Graph does not report, another customer's unit, unbound or malformed (for EVERY caller, root included — owner round
/// 35 item 2), not found, a read fault — carries ONE error text and sends no write.
/// </para>
/// <para>
/// Driven through the two per-item methods with a REAL <see cref="SpeAdminGraphService"/> and a real
/// <c>GraphServiceClient</c> against the WireMock Graph fake (<see cref="GraphWireMockFixture"/>), so "no write was
/// sent" is the absence of a recorded DELETE / permissions POST — not a mock expectation. ADR-038 §2 path #1; no
/// <c>Mock&lt;HttpMessageHandler&gt;</c>.
/// </para>
/// </remarks>
public sealed class BulkOperationContainerTypeTests : IDisposable
{
    private const string ConfigType = "aaaaaaaa-1111-2222-3333-444444444444";
    private const string OtherType = "bbbbbbbb-1111-2222-3333-444444444444";
    private const string ContainersPath = "/storage/fileStorage/containers";

    private static readonly Guid Root = Guid.Parse("10000000-0000-0000-0000-000000000000");
    private static readonly Guid UnitA = Guid.Parse("1a000000-0000-0000-0000-000000000000");
    private static readonly Guid UnitASub = Guid.Parse("1a100000-0000-0000-0000-000000000000");
    private static readonly Guid UnitB = Guid.Parse("1b000000-0000-0000-0000-000000000000");

    /// <summary>A leaf administrator of Unit A (reaches A and its child).</summary>
    private static readonly SpeAdminCallerScope LeafA = new(UnitA, IsPlatformOperator: false, new HashSet<Guid> { UnitA, UnitASub });

    /// <summary>A root-unit administrator (reaches every unit).</summary>
    private static readonly SpeAdminCallerScope RootAdmin = new(Root, IsPlatformOperator: true, new HashSet<Guid> { Root, UnitA, UnitASub, UnitB });

    private readonly GraphWireMockFixture _graph = new();
    private readonly BulkOperationService _sut = new(CreateGraphService(), NullLogger<BulkOperationService>.Instance);
    private readonly Guid _operationId = Guid.NewGuid();

    private static readonly SpeAdminGraphService.ContainerTypeConfig Config = new(
        ConfigId: Guid.Parse("ca000000-0000-0000-0000-00000000000a"),
        ContainerTypeId: ConfigType,
        ClientId: "client",
        TenantId: "tenant",
        SecretKeyVaultName: "secret");

    public void Dispose() => _graph.Dispose();

    // ── (i) another container type ───────────────────────────────────────────

    [Fact]
    public async Task Delete_OfAContainerOfAnotherType_IsRefused_AndNoDeleteIsSent()
    {
        StubContainer("c-other", OtherType, UnitA);

        var error = await Delete("c-other", LeafA);

        error!.ErrorMessage.Should().Be(BulkOperationService.ContainerNotInScopeError);
        Writes("DELETE", "c-other").Should().BeEmpty();
    }

    [Fact]
    public async Task Grant_OnAContainerOfAnotherType_IsRefused_AndNoPermissionIsPosted()
    {
        StubContainer("c-other", OtherType, UnitA);

        var error = await Grant("c-other", LeafA);

        error!.ErrorMessage.Should().Be(BulkOperationService.ContainerNotInScopeError);
        Writes("POST", "c-other").Should().BeEmpty();
    }

    // ── (ii) another customer's container of the SAME type — owner round 20's defect ──

    [Fact]
    public async Task Delete_OfAnotherCustomersContainerOfTheSameType_IsRefused_AndNoDeleteIsSent()
    {
        StubContainer("c-b", ConfigType, UnitB);

        var error = await Delete("c-b", LeafA);

        error!.ErrorMessage.Should().Be(BulkOperationService.ContainerNotInScopeError,
            "a leaf admin of Unit A must not soft-delete Unit B's container just because it shares the container type");
        Writes("DELETE", "c-b").Should().BeEmpty();
    }

    [Fact]
    public async Task Grant_OnAnotherCustomersContainerOfTheSameType_IsRefused_AndNoPermissionIsPosted()
    {
        StubContainer("c-b", ConfigType, UnitB);

        var error = await Grant("c-b", LeafA);

        error!.ErrorMessage.Should().Be(BulkOperationService.ContainerNotInScopeError);
        Writes("POST", "c-b").Should().BeEmpty();
    }

    // ── (iii) an unbound container: NO caller (round 35 item 2); a malformed stamp reads as unbound ──

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    public async Task Delete_OfAnUnboundOrMalformedContainer_ByALeafAdmin_IsRefused(string? rawStamp)
    {
        StubContainerRaw("c-unbound", ConfigType, rawStamp);

        var error = await Delete("c-unbound", LeafA);

        error!.ErrorMessage.Should().Be(BulkOperationService.ContainerNotInScopeError);
        Writes("DELETE", "c-unbound").Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    public async Task Delete_OfAnUnboundOrMalformedContainer_ByARootAdmin_IsRefused_AndNoDeleteIsSent(string? rawStamp)
    {
        // Owner round 35 item 2 (amends round 20 item 2): an unbound container is reached by NO admin route — under Model
        // 1 a root admin of any environment whose config names a shared type would otherwise reach other customers'.
        StubContainerRaw("c-unbound", ConfigType, rawStamp);
        _graph.StubDelete($"{ContainersPath}/c-unbound");

        var error = await Delete("c-unbound", RootAdmin);

        error!.ErrorMessage.Should().Be(BulkOperationService.ContainerNotInScopeError);
        Writes("DELETE", "c-unbound").Should().BeEmpty();
    }

    [Fact]
    public async Task Grant_OnAnUnboundContainer_ByARootAdmin_IsRefused_AndNoPermissionIsPosted()
    {
        StubContainerRaw("c-unbound", ConfigType, rawStamp: null);

        var error = await Grant("c-unbound", RootAdmin);

        error!.ErrorMessage.Should().Be(BulkOperationService.ContainerNotInScopeError);
        Writes("POST", "c-unbound").Should().BeEmpty();
    }

    // ── (iii-b) a container whose type Graph does not REPORT (D10: the job takes no type on trust) ──

    [Fact]
    public async Task Delete_OfAContainerWhoseTypeGraphDoesNotReport_IsRefused_EvenInTheCallersOwnUnit()
    {
        // Bound to the caller's own unit, so ONLY the "type must be reported and equal" clause can refuse it: the bulk
        // job acts app-only with no per-request filter, and an unreported type is never taken to be the config's.
        StubContainerWithoutType("c-untyped", UnitA);
        _graph.StubDelete($"{ContainersPath}/c-untyped");

        var error = await Delete("c-untyped", LeafA);

        error!.ErrorMessage.Should().Be(BulkOperationService.ContainerNotInScopeError);
        Writes("DELETE", "c-untyped").Should().BeEmpty();
    }

    [Fact]
    public async Task Grant_OnAContainerWhoseTypeGraphDoesNotReport_IsRefused_EvenInTheCallersOwnUnit()
    {
        StubContainerWithoutType("c-untyped", UnitA);

        var error = await Grant("c-untyped", LeafA);

        error!.ErrorMessage.Should().Be(BulkOperationService.ContainerNotInScopeError);
        Writes("POST", "c-untyped").Should().BeEmpty();
    }

    // ── (iv) not found, read fault — the same text, no write ─────────────────

    [Theory]
    [InlineData(404)]
    [InlineData(500)]
    public async Task Delete_WhenTheContainerCannotBeRead_IsRefusedWithTheSameText_AndNoDeleteIsSent(int status)
    {
        _graph.StubGet($"{ContainersPath}/c-unread", """{"error":{"code":"x","message":"x"}}""", status);

        var error = await Delete("c-unread", RootAdmin);

        error!.ErrorMessage.Should().Be(BulkOperationService.ContainerNotInScopeError);
        Writes("DELETE", "c-unread").Should().BeEmpty();
    }

    [Theory]
    [InlineData(404)]
    [InlineData(500)]
    public async Task Grant_WhenTheContainerCannotBeRead_IsRefusedWithTheSameText_AndNoPermissionIsPosted(int status)
    {
        _graph.StubGet($"{ContainersPath}/c-unread", """{"error":{"code":"x","message":"x"}}""", status);

        var error = await Grant("c-unread", RootAdmin);

        error!.ErrorMessage.Should().Be(BulkOperationService.ContainerNotInScopeError);
        Writes("POST", "c-unread").Should().BeEmpty();
    }

    // ── (v) the caller's own subtree, the config's own type — the write is sent ──

    [Fact]
    public async Task Delete_OfAContainerInTheCallersSubtree_OfTheConfigsType_DifferingOnlyInCase_IsSent()
    {
        StubContainer("c-same", ConfigType.ToUpperInvariant(), UnitASub);
        _graph.StubDelete($"{ContainersPath}/c-same");

        var error = await Delete("c-same", LeafA);

        error.Should().BeNull();
        Writes("DELETE", "c-same").Should().ContainSingle();
    }

    [Fact]
    public async Task Grant_OnAContainerOfTheCallersOwnUnit_OfTheConfigsType_IsSent()
    {
        StubContainer("c-same", ConfigType, UnitA);
        _graph.StubPost(
            $"{ContainersPath}/c-same/permissions",
            """{"id":"p1","roles":["owner"],"grantedToV2":{"user":{"id":"u-1","displayName":"U"}}}""",
            201);

        var error = await Grant("c-same", LeafA);

        error.Should().BeNull();
        Writes("POST", "c-same").Should().ContainSingle();
    }

    [Fact]
    public async Task TheBindingRead_AsksForTheContainersTypeAndCustomProperties()
    {
        StubContainer("c-same", ConfigType, UnitA);
        _graph.StubDelete($"{ContainersPath}/c-same");

        await Delete("c-same", LeafA);

        _graph.SelectFieldsFor($"{ContainersPath}/c-same").Should().BeEquivalentTo(
            new[] { "id", "containerTypeId", "customProperties" },
            "Graph returns customProperties only when the single-container GET selects it");
    }

    // ── the job loops reach the Graph writes ONLY through the per-item methods ─

    [Fact]
    public void TheTwoGraphWrites_HaveExactlyOneCallSiteEach_InsideThePerItemMethods()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "server", "api", "Sprk.Bff.Api", "Services", "SpeAdmin", "BulkOperationService.cs"));
        var code = Regex.Replace(source, @"//[^\n]*|/\*.*?\*/", string.Empty, RegexOptions.Singleline);

        foreach (var (write, method) in new[]
                 {
                     ("SoftDeleteContainerAsync(", "DeleteContainerInScopeAsync("),
                     ("GrantContainerPermissionAsync(", "GrantOnContainerInScopeAsync("),
                 })
        {
            var callSites = Regex.Matches(code, Regex.Escape("." + write)).Select(m => m.Index).ToList();
            callSites.Should().ContainSingle("{0} must be reachable only through {1}", write, method);

            var methodStart = code.IndexOf("internal async Task<BulkOperationItemError?> " + method, StringComparison.Ordinal);
            methodStart.Should().BePositive();
            var nextMember = code.IndexOf("    internal ", methodStart + 1, StringComparison.Ordinal);
            var methodEnd = nextMember < 0 ? code.Length : nextMember;
            callSites[0].Should().BeInRange(methodStart, methodEnd, "{0} must sit inside {1}", write, method);
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private Task<Sprk.Bff.Api.Models.SpeAdmin.BulkOperationItemError?> Delete(string containerId, SpeAdminCallerScope scope) =>
        _sut.DeleteContainerInScopeAsync(
            _graph.CreateGraphClient(), Config, scope, containerId, _operationId, CancellationToken.None);

    private Task<Sprk.Bff.Api.Models.SpeAdmin.BulkOperationItemError?> Grant(string containerId, SpeAdminCallerScope scope) =>
        _sut.GrantOnContainerInScopeAsync(
            _graph.CreateGraphClient(), Config, scope, containerId, "u-1", null, "owner", _operationId, CancellationToken.None);

    private void StubContainer(string id, string containerTypeId, Guid stampedUnit) =>
        StubContainerRaw(id, containerTypeId, stampedUnit.ToString("D"));

    /// <summary>A single-container GET answer, carrying the stamp as Graph does (or no stamp at all).</summary>
    private void StubContainerRaw(string id, string containerTypeId, string? rawStamp)
    {
        var customProperties = rawStamp is null
            ? "{}"
            : "{\"" + SpeContainerBusinessUnitStamp.PropertyName + "\":{\"value\":\"" + rawStamp + "\",\"isSearchable\":false}}";

        _graph.StubGet(
            $"{ContainersPath}/{id}",
            $$"""{"id":"{{id}}","displayName":"Container {{id}}","containerTypeId":"{{containerTypeId}}","customProperties":""" + customProperties + "}");
    }

    /// <summary>A single-container GET answer that carries a stamp but NO <c>containerTypeId</c>.</summary>
    private void StubContainerWithoutType(string id, Guid stampedUnit) =>
        _graph.StubGet(
            $"{ContainersPath}/{id}",
            "{\"id\":\"" + id + "\",\"displayName\":\"Container " + id + "\",\"customProperties\":{\"" +
            SpeContainerBusinessUnitStamp.PropertyName + "\":{\"value\":\"" + stampedUnit.ToString("D") + "\",\"isSearchable\":false}}}");

    private IReadOnlyList<RecordedGraphRequest> Writes(string method, string containerId) =>
        _graph.RequestsFor($"{ContainersPath}/{containerId}")
            .Where(r => string.Equals(r.Method, method, StringComparison.OrdinalIgnoreCase))
            .ToList();

    /// <summary>A real service with unusable credentials — the same construction the archival contract tests use.</summary>
    private static SpeAdminGraphService CreateGraphService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://unused.invalid" })
            .Build();

        var service = new SpeAdminGraphService(
            dataverseClient: new DataverseWebApiClient(
                configuration, NullLogger<DataverseWebApiClient>.Instance, new UnusableCredential()),
            configuration: configuration,
            logger: NullLogger<SpeAdminGraphService>.Instance);
        return service;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
            dir = dir.Parent;

        return dir!.FullName;
    }

    private sealed class UnusedHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException(
            $"The per-item methods take the Graph client as a parameter; building '{name}' means an unexpected path.");
    }

    private sealed class UnusableCredential : Azure.Core.TokenCredential
    {
        public override Azure.Core.AccessToken GetToken(Azure.Core.TokenRequestContext r, CancellationToken c)
            => throw new InvalidOperationException("No credential may be used by these tests.");

        public override ValueTask<Azure.Core.AccessToken> GetTokenAsync(Azure.Core.TokenRequestContext r, CancellationToken c)
            => throw new InvalidOperationException("No credential may be used by these tests.");
    }
}

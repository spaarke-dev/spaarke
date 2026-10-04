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
/// unified-access-control-r2 task 165 — the bulk delete / bulk permissions background job acts on a
/// container only when it is of the config's container type (sweep findings #44, #72).
/// </summary>
/// <remarks>
/// <para>
/// The tenant-scope filter confines the CONFIG a bulk request names to the caller's business units; the
/// container ids are still caller-chosen, and the job runs app-only with no caller context. Each item is now
/// read with the config's Graph client and written only if its <c>containerTypeId</c> equals the config's.
/// Every refusal — another type, not found, a read fault — carries ONE error text and sends no write.
/// </para>
/// <para>
/// Driven through the two per-item methods with a REAL <see cref="SpeAdminGraphService"/> and a real
/// <c>GraphServiceClient</c> against the WireMock Graph fake (<see cref="GraphWireMockFixture"/>), so "no
/// write was sent" is the absence of a recorded DELETE / permissions POST — not a mock expectation.
/// ADR-038 §2 path #1; no <c>Mock&lt;HttpMessageHandler&gt;</c>.
/// </para>
/// </remarks>
public sealed class BulkOperationContainerTypeTests : IDisposable
{
    private const string ConfigType = "aaaaaaaa-1111-2222-3333-444444444444";
    private const string OtherType = "bbbbbbbb-1111-2222-3333-444444444444";
    private const string ContainersPath = "/storage/fileStorage/containers";

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
        StubContainer("c-other", OtherType);

        var error = await _sut.DeleteContainerOfConfigTypeAsync(
            _graph.CreateGraphClient(), Config, "c-other", _operationId, CancellationToken.None);

        error!.ErrorMessage.Should().Be(BulkOperationService.ContainerNotOfConfigTypeError);
        Writes("DELETE", "c-other").Should().BeEmpty();
    }

    [Fact]
    public async Task Grant_OnAContainerOfAnotherType_IsRefused_AndNoPermissionIsPosted()
    {
        StubContainer("c-other", OtherType);

        var error = await Grant("c-other");

        error!.ErrorMessage.Should().Be(BulkOperationService.ContainerNotOfConfigTypeError);
        Writes("POST", "c-other").Should().BeEmpty();
    }

    // ── (ii) not found, (iii) read fault — the same text, no write ───────────

    [Theory]
    [InlineData(404)]
    [InlineData(500)]
    public async Task Delete_WhenTheContainerCannotBeRead_IsRefusedWithTheSameText_AndNoDeleteIsSent(int status)
    {
        _graph.StubGet($"{ContainersPath}/c-unread", """{"error":{"code":"x","message":"x"}}""", status);

        var error = await _sut.DeleteContainerOfConfigTypeAsync(
            _graph.CreateGraphClient(), Config, "c-unread", _operationId, CancellationToken.None);

        error!.ErrorMessage.Should().Be(BulkOperationService.ContainerNotOfConfigTypeError);
        Writes("DELETE", "c-unread").Should().BeEmpty();
    }

    [Theory]
    [InlineData(404)]
    [InlineData(500)]
    public async Task Grant_WhenTheContainerCannotBeRead_IsRefusedWithTheSameText_AndNoPermissionIsPosted(int status)
    {
        _graph.StubGet($"{ContainersPath}/c-unread", """{"error":{"code":"x","message":"x"}}""", status);

        var error = await Grant("c-unread");

        error!.ErrorMessage.Should().Be(BulkOperationService.ContainerNotOfConfigTypeError);
        Writes("POST", "c-unread").Should().BeEmpty();
    }

    // ── (iv) the config's own type — the write is sent as today ──────────────

    [Fact]
    public async Task Delete_OfAContainerOfTheConfigsType_DifferingOnlyInCase_IsSent()
    {
        StubContainer("c-same", ConfigType.ToUpperInvariant());
        _graph.StubDelete($"{ContainersPath}/c-same");

        var error = await _sut.DeleteContainerOfConfigTypeAsync(
            _graph.CreateGraphClient(), Config, "c-same", _operationId, CancellationToken.None);

        error.Should().BeNull();
        Writes("DELETE", "c-same").Should().ContainSingle();
    }

    [Fact]
    public async Task Grant_OnAContainerOfTheConfigsType_DifferingOnlyInCase_IsSent()
    {
        StubContainer("c-same", ConfigType.ToUpperInvariant());
        _graph.StubPost(
            $"{ContainersPath}/c-same/permissions",
            """{"id":"p1","roles":["owner"],"grantedToV2":{"user":{"id":"u-1","displayName":"U"}}}""",
            201);

        var error = await Grant("c-same");

        error.Should().BeNull();
        Writes("POST", "c-same").Should().ContainSingle();
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
                     ("SoftDeleteContainerAsync(", "DeleteContainerOfConfigTypeAsync("),
                     ("GrantContainerPermissionAsync(", "GrantOnContainerOfConfigTypeAsync("),
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

    private Task<Sprk.Bff.Api.Models.SpeAdmin.BulkOperationItemError?> Grant(string containerId) =>
        _sut.GrantOnContainerOfConfigTypeAsync(
            _graph.CreateGraphClient(), Config, containerId, "u-1", null, "owner", _operationId, CancellationToken.None);

    private void StubContainer(string id, string containerTypeId) =>
        _graph.StubGet(
            $"{ContainersPath}/{id}",
            $$"""{"id":"{{id}}","displayName":"Container {{id}}","containerTypeId":"{{containerTypeId}}","status":"active"}""");

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
            httpClientFactory: new UnusedHttpClientFactory(),
            secretClient: new SecretClient(new Uri("https://unused.invalid/"), new UnusableCredential()),
            dataverseClient: new DataverseWebApiClient(
                configuration, NullLogger<DataverseWebApiClient>.Instance, new UnusableCredential()),
            configuration: configuration,
            logger: NullLogger<SpeAdminGraphService>.Instance,
            tokenProvider: null);
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

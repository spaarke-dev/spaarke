using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.Integration.Workspace;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// Test host for the <c>/api/v1/external-access</c> management group with a SUBSTITUTED
/// <see cref="CallerRecordAccessProbe"/>, so the delegation rule (task 008, FR-07) can be exercised
/// in both directions offline.
/// </summary>
/// <remarks>
/// <para><b>Why substitution is mandatory here, not a convenience.</b> The real probe performs an OBO
/// exchange and two Dataverse calls; offline all three fail and it returns
/// <see cref="AccessRights.None"/> — correctly, by design. Every request would then 403, and a test
/// asserting 403 would pass just as well against a filter that denied unconditionally, or against no
/// filter at all if some other layer happened to reject. That is the vacuous-pass failure mode this
/// project has hit repeatedly. A double that CAN answer "yes" is what makes the negatives mean
/// something: the positive and the negative differ only in the caller's rights.</para>
///
/// <para><b>How a test states the caller's rights.</b> Through the bearer token, which is exactly what
/// the real probe consumes — <c>Bearer rights=ReadAccess,WriteAccess</c>. Encoding the answer in the
/// credential keeps the fixture immutable and shareable across a class (no mutable "current rights"
/// field to bleed between tests) and keeps the stub honest: it is still a function of the caller's
/// token, as the production type is. <see cref="ThrowingRightsToken"/> makes the probe throw, for the
/// fail-closed-on-error path.</para>
/// </remarks>
public sealed class DelegationRuleTestFixture : WorkspaceTestFixture
{
    /// <summary>A bearer token that makes the stub probe throw — exercises the error path.</summary>
    public const string ThrowingRightsToken = "rights=THROW";

    private const string GrantEntitySet = "sprk_externalrecordaccesses";

    /// <summary>
    /// Every (entitySet, recordId) the filter asked about. Tests assert the filter aimed the check at
    /// the right RECORD, not merely that it denied — for <c>/revoke</c> the target is the grant row's
    /// root, which is not the id in the request body.
    /// </summary>
    public ConcurrentBag<(string EntitySet, Guid RecordId)> ProbedTargets { get; } = new();

    /// <summary>Grant rows the stubbed <see cref="DataverseWebApiClient"/> can retrieve by id.</summary>
    private readonly ConcurrentDictionary<Guid, ExternalGrantRow> _grantRows = new();

    /// <summary>Seeds a grant row so <c>/revoke</c>'s target resolution can read it.</summary>
    public void SeedGrantRow(Guid accessRecordId, Guid contactId, Guid rootId, ExternalGrantRootType rootType)
    {
        var row = new ExternalGrantRow
        {
            Id = accessRecordId,
            ContactId = contactId,
            AccessLevel = 1,
            StateCode = 0
        };

        switch (rootType)
        {
            case ExternalGrantRootType.Project: row.ProjectId = rootId; break;
            case ExternalGrantRootType.Matter: row.MatterId = rootId; break;
            case ExternalGrantRootType.WorkAssignment: row.WorkAssignmentId = rootId; break;
        }

        _grantRows[accessRecordId] = row;
    }

    /// <summary>
    /// Adds the CIAM + portal keys the invite routes' singletons need at CONSTRUCTION time.
    /// </summary>
    /// <remarks>
    /// Not cosmetic, and worth stating plainly: Minimal API resolves a handler's DI arguments BEFORE
    /// the endpoint-filter pipeline runs. <c>CiamUserProvisioningService</c>'s constructor throws
    /// without <c>Ciam:Domain</c>, so <c>/invite</c> and <c>/invite-and-grant</c> answered 500 before
    /// the delegation filter was ever invoked — which would have made a 403 assertion on those two
    /// routes untestable, and (worse) a PASSING 403-free assertion look like evidence of anything.
    /// Values mirror <c>ExternalAccessContractTests</c>, the proven set for this endpoint group.
    /// </remarks>
    protected override Microsoft.Extensions.Hosting.IHost CreateHost(Microsoft.Extensions.Hosting.IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ciam:Instance"] = "https://spaarketest.ciamlogin.com",
            ["Ciam:TenantId"] = "00000000-0000-0000-0000-0000000000c1",
            ["Ciam:ClientId"] = "ciam-api-client-id",
            ["Ciam:Audience"] = "api://ciam-api-client-id",
            ["Ciam:Domain"] = "spaarketest.onmicrosoft.com",
            ["Ciam:GraphProvisioner:ClientId"] = "ciam-graph-provisioner-id",
            ["Ciam:GraphProvisioner:CertificateName"] = "ciam-graph-cert",
            ["ExternalAccess:PortalUrl"] = "https://external.spaarke.test"
        }));

        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(new StubCallerRecordAccessProbe(ProbedTargets));
            services.AddSingleton<CallerRecordAccessProbe>(sp => sp.GetRequiredService<StubCallerRecordAccessProbe>());

            // The filter reads the grant row to resolve /revoke's target. Substituting the client's
            // virtual seam (ADR-038 §4) rather than mocking transport (ban B1) keeps the production
            // RetrieveRowAsync + DeriveKey path under test.
            var clientMock = new Mock<DataverseWebApiClient>(
                ClientConfig(), NullLogger<DataverseWebApiClient>.Instance) { CallBase = false };

            clientMock
                .Setup(c => c.RetrieveAsync<ExternalGrantRow>(
                    GrantEntitySet, It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, Guid id, string? _, CancellationToken _) =>
                    _grantRows.TryGetValue(id, out var row) ? row : null);

            services.AddSingleton(clientMock.Object);
        });
    }

    /// <summary>Config sufficient for the real <see cref="DataverseWebApiClient"/> constructor (Moq invokes it).</summary>
    private static IConfiguration ClientConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
            ["API_APP_ID"] = "00000000-0000-0000-0000-0000000000aa",
            ["API_CLIENT_SECRET"] = "test-secret",
            ["TENANT_ID"] = "00000000-0000-0000-0000-0000000000bb"
        }).Build();

    /// <summary>
    /// An authenticated client whose caller holds exactly <paramref name="dataverseRights"/> on every
    /// record — expressed in Dataverse's own wire vocabulary
    /// (<c>"ReadAccess"</c>, <c>"ReadAccess,WriteAccess"</c>, …) so the test states what Dataverse
    /// would say, not what the C# enum is called.
    /// </summary>
    public HttpClient CreateClientWithRights(string dataverseRights)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", $"rights={dataverseRights}");
        return client;
    }

    /// <summary>
    /// An authenticated client that carries a NON-bearer Authorization header. It passes
    /// authentication (the fake handler accepts any non-empty header) but yields no bearer token, so
    /// the delegation check has no credential to evaluate as.
    /// </summary>
    public HttpClient CreateClientWithoutBearerToken()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", "dGVzdDp0ZXN0");
        return client;
    }

    /// <summary>
    /// Reports the rights encoded in the caller's bearer token, and records what it was asked about.
    /// </summary>
    private sealed class StubCallerRecordAccessProbe : CallerRecordAccessProbe
    {
        private readonly ConcurrentBag<(string, Guid)> _probed;

        public StubCallerRecordAccessProbe(ConcurrentBag<(string, Guid)> probed)
            : base(new HttpClient(),
                   new ConfigurationBuilder().Build(),
                   NullLogger<CallerRecordAccessProbe>.Instance)
        {
            _probed = probed;
        }

        public override Task<AccessRights> GetCallerRightsAsync(
            string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
        {
            _probed.Add((entitySet, recordId));

            if (callerBearerToken == ThrowingRightsToken)
                throw new InvalidOperationException("Simulated Dataverse failure during the delegation check.");

            var rights = callerBearerToken?.StartsWith("rights=", StringComparison.Ordinal) == true
                ? callerBearerToken["rights=".Length..]
                : null;

            // The real probe parses Dataverse's rights string with this same mapper — using it here
            // keeps the double honest about the one translation that decides the outcome.
            return Task.FromResult(DataverseAccessRightsMapper.FromAccessRightsString(rights));
        }
    }
}

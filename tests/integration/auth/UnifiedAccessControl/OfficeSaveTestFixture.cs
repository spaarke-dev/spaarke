using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.Integration.Workspace;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// Test host for <c>POST /api/office/save</c>'s entity-association gate, with a substituted
/// <see cref="CallerRecordAccessProbe"/> so the caller's rights on the TARGET ENTITY can be stated.
/// </summary>
/// <remarks>
/// The rights are carried on the bearer token (<c>Bearer rights=ReadAccess,AppendToAccess</c>) — the
/// same convention as <see cref="DelegationRuleTestFixture"/>, and for the same reason: it keeps the
/// fixture immutable across a test class while leaving the double a function of the credential, as the
/// production type is. Without a substituted probe every case would deny offline and the negative
/// assertions would be vacuous.
/// </remarks>
public sealed class OfficeSaveTestFixture : WorkspaceTestFixture
{
    /// <summary>Every (entitySet, recordId) the association gate asked about.</summary>
    public ConcurrentBag<(string EntitySet, Guid RecordId)> ProbedTargets { get; } = new();

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            // Every test in this class shares one caller identity, and the save route is capped at
            // 10 requests/minute/user — enough cases here to trip it and produce 429s that look like
            // authorization failures.
            ["OfficeRateLimit:Enabled"] = "false"
        }));

        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(new RecordingCallerRecordAccessProbe(ProbedTargets));
            services.AddSingleton<CallerRecordAccessProbe>(
                sp => sp.GetRequiredService<RecordingCallerRecordAccessProbe>());
        });
    }

    /// <summary>
    /// An authenticated Office caller holding exactly <paramref name="dataverseRights"/> on every
    /// record, stated in Dataverse's own wire vocabulary.
    /// </summary>
    public HttpClient CreateClientWithRights(string dataverseRights)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", $"rights={dataverseRights}");
        return client;
    }

    private sealed class RecordingCallerRecordAccessProbe : CallerRecordAccessProbe
    {
        private readonly ConcurrentBag<(string, Guid)> _probed;

        public RecordingCallerRecordAccessProbe(ConcurrentBag<(string, Guid)> probed)
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

            var rights = callerBearerToken?.StartsWith("rights=", StringComparison.Ordinal) == true
                ? callerBearerToken["rights=".Length..]
                : null;

            return Task.FromResult(DataverseAccessRightsMapper.FromAccessRightsString(rights));
        }
    }
}

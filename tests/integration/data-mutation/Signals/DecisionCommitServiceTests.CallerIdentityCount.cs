using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.Signals;

/// <summary>
/// Ontology task 170 (D-196): one decision commit asks Dataverse who the caller is ONCE. The REAL commit service runs against
/// the SHIPPED <see cref="CallerRecordAccessProbe"/> with only its wire steps answered in memory and counted (the OBO exchange,
/// <c>WhoAmI()</c>, <c>RetrievePrincipalAccess</c>), inside a request set the way the host sets it. Every rights question is
/// still asked: access results are not remembered.
/// </summary>
public sealed partial class DecisionCommitServiceTests
{
    [Fact]
    [Trait("status", "new")]
    public async Task T170_OneCommit_AsksWhoAmIOnce_AndAsksEveryRightsQuestion()
    {
        var rig = new Rig();
        var probe = new WireCountingProbe(Caller);
        rig.ProbeOverride = probe;
        var http = Http();
        new HttpContextAccessor().HttpContext = http;

        var result = await rig.CommitAsync(PathBReview(), http: http);

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        rig.Record.GetAttributeValue<Microsoft.Xrm.Sdk.EntityReference>("sprk_confirmedby").Id.Should().Be(Caller,
            "the remembered answer is the caller's own systemuserid");
        probe.CallerQuestions.Should().BeGreaterThan(1, "the commit asks the probe about the caller more than once");
        probe.WhoAmIs.Should().Be(1, "who the caller is is asked once per request");
        probe.RightsAsked.Should().BeGreaterThan(0).And.Be(probe.RightsQuestions,
            "every rights question still reaches Dataverse (FR-51 is unchanged)");
        probe.PrincipalsAskedAbout.Should().OnlyContain(p => p == Caller);
    }

    /// <summary>The shipped probe with its wire steps answered in memory and counted; every record is granted.</summary>
    private sealed class WireCountingProbe : CallerRecordAccessProbe
    {
        private readonly Guid _caller;
        private int _whoAmIs;
        private int _rightsAsked;
        private int _callerQuestions;
        private int _rightsQuestions;

        public WireCountingProbe(Guid caller)
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
                    Options.Create(new CredentialSelectionOptions { Order = new List<string> { "ManagedIdentityFederated" } }),
                    new ConfigurationBuilder().Build(),
                    NullLogger<OrderedCredentialClientProvider>.Instance))
        {
            _caller = caller;
        }

        public int WhoAmIs => Volatile.Read(ref _whoAmIs);

        public int RightsAsked => Volatile.Read(ref _rightsAsked);

        /// <summary>How many times the commit asked the probe anything that needs the caller's identity.</summary>
        public int CallerQuestions => Volatile.Read(ref _callerQuestions);

        public int RightsQuestions => Volatile.Read(ref _rightsQuestions);

        public ConcurrentBag<Guid> PrincipalsAskedAbout { get; } = new();

        public override Task<Guid?> GetCallerSystemUserIdAsync(string? callerBearerToken, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _callerQuestions);
            return base.GetCallerSystemUserIdAsync(callerBearerToken, ct);
        }

        public override Task<AccessRights> GetCallerRightsAsync(string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _callerQuestions);
            Interlocked.Increment(ref _rightsQuestions);
            return base.GetCallerRightsAsync(callerBearerToken, entitySet, recordId, ct);
        }

        protected override Task<string?> ExchangeForDataverseTokenAsync(string callerBearerToken, string context, CancellationToken ct) =>
            Task.FromResult<string?>($"{callerBearerToken}-dv");

        protected override Task<(HttpStatusCode Status, string Body)> SendWhoAmIRequestAsync(string dataverseToken, CancellationToken ct)
        {
            Interlocked.Increment(ref _whoAmIs);
            return Task.FromResult((HttpStatusCode.OK, JsonSerializer.Serialize(new { UserId = _caller.ToString("D") })));
        }

        protected override Task<(HttpStatusCode Status, string Body)> SendPrincipalAccessRequestAsync(
            string url, string dataverseToken, CancellationToken ct)
        {
            Interlocked.Increment(ref _rightsAsked);
            PrincipalsAskedAbout.Add(Guid.Parse(url[(url.IndexOf("systemusers(", StringComparison.Ordinal) + "systemusers(".Length)..][..36]));
            return Task.FromResult((HttpStatusCode.OK,
                "{\"AccessRights\":\"ReadAccess, WriteAccess, AppendAccess, AppendToAccess, CreateAccess, DeleteAccess, ShareAccess, AssignAccess\"}"));
        }
    }
}

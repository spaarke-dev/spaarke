using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Contracts.Provisioning;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Platform;

/// <summary>
/// <c>POST /api/platform/secure-record-isolation-census</c> (customer-provisioning task 260, ISS-014) through the REAL
/// Program — real route, real <c>KeylessProofAuthorizationFilter</c>, real census code (the one the scheduled job runs).
/// Only the Dataverse seam is substituted: it answers the census reads the way the SDK does.
/// </summary>
/// <remarks>
/// Pinned: the route admits exactly the keyless-proof caller (no token 401; a role-less, delegated or foreign-audience
/// token 403); the body is the job's result and nothing else (status, verdict, findings{verdict,message}); a census that
/// cannot be read is <c>error</c> with no exception text; and the route writes nothing.
/// </remarks>
[Trait("category", "authorization")]
public sealed class SecureRecordIsolationCensusEndpointContractTests : IClassFixture<KeylessProofHost>
{
    private static readonly string Route = KeylessProofContract.SecureRecordIsolationCensus.Route;

    private readonly KeylessProofHost _host;

    public SecureRecordIsolationCensusEndpointContractTests(KeylessProofHost host)
    {
        _host = host;
        // A directory with no Secure Record unit — the census grades it INERT (a deterministic, non-passing answer).
        _host.Dataverse
            .Setup(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression query, CancellationToken _) => InertDirectory(query));
    }

    [Fact]
    public async Task Census_WithoutAToken_Is401()
    {
        var response = await _host.CreateClient().PostAsync(Route, null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Census_AnApplicationTokenWithoutTheRole_Is403()
    {
        // SystemAdmin is the role the admin job routes accept — it does NOT open this route.
        var response = await _host.Caller(roles: "SystemAdmin").PostAsync(Route, null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("reasonCode").GetString().Should().Be("sdap.access.deny.keyless_proof_role");
    }

    [Fact]
    public async Task Census_AUserTokenCarryingTheRole_Is403()
    {
        var response = await _host.Caller(roles: KeylessProofContract.AppRoleValue, scope: "user_impersonation")
            .PostAsync(Route, null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Census_ATokenForAnotherAudience_Is403()
    {
        var response = await _host.Caller(roles: KeylessProofContract.AppRoleValue, audience: "api://copilot-plugin")
            .PostAsync(Route, null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Census_TheL2ApplicationWithTheRole_Is200_WithTheJobsResultShapeAndNothingElse()
    {
        var response = await _host.Caller(roles: KeylessProofContract.AppRoleValue, idtyp: "app").PostAsync(Route, null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        root.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(new[] { "status", "verdict", "findings" });
        root.GetProperty("status").GetString().Should().Be(KeylessProofContract.SecureRecordIsolationCensus.Inert,
            "no Secure Record unit: nothing was asserted, which is never isolated");
        root.GetProperty("verdict").GetString().Should().Be(nameof(SecureBuVerdict.SecureBusinessUnitNotFound));
        var finding = root.GetProperty("findings").EnumerateArray().Should().ContainSingle().Subject;
        finding.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(new[] { "verdict", "message" });
    }

    [Fact]
    public async Task Census_ThatCannotBeRead_IsStatusError_WithNoExceptionText()
    {
        _host.Dataverse
            .Setup(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("secret-connection-detail-260"));

        var response = await _host.Caller(roles: KeylessProofContract.AppRoleValue).PostAsync(Route, null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync();
        text.Should().NotContain("secret-connection-detail-260");
        using var body = JsonDocument.Parse(text);
        body.RootElement.GetProperty("status").GetString().Should().Be(KeylessProofContract.SecureRecordIsolationCensus.Error);
        body.RootElement.GetProperty("findings").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Census_WritesNothing_OnlyReads()
    {
        _host.Dataverse.Invocations.Clear();

        await _host.Caller(roles: KeylessProofContract.AppRoleValue).PostAsync(Route, null);

        _host.Dataverse.Invocations.Select(i => i.Method.Name).Distinct()
            .Should().OnlyContain(name => name == "RetrieveMultipleAsync" || name == "TestConnectionAsync");
        _host.Dataverse.Invocations.Should().Contain(i => i.Method.Name == "RetrieveMultipleAsync", "the census ran");
    }

    /// <summary>
    /// The root unit only (no Secure Record unit), the three guarded privileges, System Administrator holding them —
    /// with the SDK's attribute types. Every other read is empty.
    /// </summary>
    private static EntityCollection InertDirectory(QueryExpression query)
    {
        var root = Guid.Parse("00000000-0000-0000-0000-000000000260");
        var sysAdmin = Guid.Parse("00000000-0000-0000-0000-000000000261");
        var privileges = SecureBuRoleDepthAssertion.GuardedPrivileges
            .Select((name, i) => (Id: Guid.Parse($"00000000-0000-0000-0000-00000000027{i}"), Name: name))
            .ToArray();

        var rows = query.EntityName switch
        {
            "businessunit" => new List<Entity>
            {
                new("businessunit", root) { ["businessunitid"] = root, ["name"] = "Spaarke" },
            },
            "privilege" => privileges
                .Select(p => new Entity("privilege", p.Id) { ["privilegeid"] = p.Id, ["name"] = p.Name })
                .ToList(),
            "roleprivileges" => privileges
                .Select(p => new Entity("roleprivileges", Guid.NewGuid())
                {
                    ["roleid"] = sysAdmin, ["privilegeid"] = p.Id, ["privilegedepthmask"] = (int)PrivilegeDepth.Global,
                })
                .ToList(),
            "role" => new List<Entity>
            {
                new("role", sysAdmin)
                {
                    ["roleid"] = sysAdmin, ["name"] = "System Administrator",
                    ["businessunitid"] = new EntityReference("businessunit", root),
                },
            },
            _ => new List<Entity>(),
        };

        return new EntityCollection(rows) { MoreRecords = false };
    }
}

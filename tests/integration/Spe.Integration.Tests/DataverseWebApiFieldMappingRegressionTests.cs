using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Xunit;

namespace Spe.Integration.Tests;

/// <summary>
/// Regression (DEF-2, RED-4 B, 2026-08-16) — <see cref="DataverseWebApiService.GetEntitySetNameAsync"/> must
/// resolve a real Dataverse <c>EntitySetName</c> rather than <c>throw new NotImplementedException</c>.
/// </summary>
/// <remarks>
/// Root cause: <see cref="DataverseWebApiService"/> is the runtime binding for
/// <see cref="IFieldMappingDataverseService"/> (GraphModule DI). Three LIVE field-mapping methods —
/// <c>RetrieveRecordFieldsAsync</c>, <c>QueryChildRecordIdsAsync</c>, and <c>UpdateRecordFieldsAsync</c> — call
/// <c>GetEntitySetNameAsync</c> as their first operation. While that method was a throwing stub, every
/// field-mapping read / child-query / write routed to this impl threw <c>NotImplementedException</c> before
/// issuing its request (surfaced as an HTTP 500 in the compose cold-session UAT; the compose caller was patched
/// narrowly, the stub was left throwing). The fix implements the method against the <c>EntityDefinitions</c>
/// metadata endpoint (mirroring <c>GetEntityObjectTypeCodeAsync</c>), cached, failing loud on error.
///
/// See <c>docs/architecture/DATAVERSE-ACCESS-LAYER-ROUTING.md</c> trap #2 and
/// <c>projects/code-quality-and-assurance-r3/notes/defer-issues.md</c> (DEF-2).
///
/// LIVE-gated (ADR-038 "real test tenant > emulator"): this exercises the real Dataverse Web API metadata
/// endpoint, so it requires real environment config (env <c>Dataverse__ServiceUrl</c> + <c>Dataverse__ClientSecret</c>
/// + <c>TENANT_ID</c> + <c>API_APP_ID</c>, e.g. pointing at spaarke-dev). It SKIPS when unconfigured or when
/// pointed at the placeholder test host — matching the rest of the field-mapping integration suite, which is
/// all <see cref="SkippableFactAttribute"/>-gated. Run on-demand against spaarke-dev to verify the fix
/// behaviorally.
/// </remarks>
public class DataverseWebApiFieldMappingRegressionTests
{
    private static IConfiguration BuildConfig() =>
        new ConfigurationBuilder().AddEnvironmentVariables().Build();

    private static bool IsLiveConfigured(IConfiguration cfg)
    {
        var url = cfg["Dataverse:ServiceUrl"];
        return !string.IsNullOrEmpty(url)
            && !url.Contains("test.crm.dynamics.com", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(cfg["Dataverse:ClientSecret"])
            && !string.IsNullOrEmpty(cfg["TENANT_ID"])
            && !string.IsNullOrEmpty(cfg["API_APP_ID"]);
    }

    [SkippableFact]
    [Trait("Category", "FieldMapping")]
    [Trait("Category", "Regression")]
    public async Task GetEntitySetNameAsync_ForFieldMappingProfileEntity_ResolvesSetName_NotNotImplemented()
    {
        var cfg = BuildConfig();
        Skip.IfNot(IsLiveConfigured(cfg),
            "Live Dataverse not configured — set Dataverse__ServiceUrl + Dataverse__ClientSecret + TENANT_ID + " +
            "API_APP_ID (e.g. spaarke-dev) to run this DEF-2 regression against a real environment.");

        using var http = new HttpClient();
        var sut = new DataverseWebApiService(http, cfg, NullLogger<DataverseWebApiService>.Instance);

        // Act — the exact call that previously threw NotImplementedException on the WebApi impl.
        var setName = await sut.GetEntitySetNameAsync("sprk_fieldmappingprofile");

        // Assert — a real plural set name is resolved (DEF-2 fixed). Exact pluralization is not asserted to
        // avoid brittleness; the point is it resolves rather than throwing.
        setName.Should().NotBeNullOrEmpty();
        setName.Should().StartWith("sprk_fieldmappingprofile");
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Dataverse.FieldMapping;
using Sprk.Bff.Api.Tests.Api.Office;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using EntityReference = Microsoft.Xrm.Sdk.EntityReference;

namespace Sprk.Bff.Api.Tests.Auth.Office;

/// <summary>
/// #2188 (field-mapping-server-write-path-r1 DI-004) — <c>POST /api/office/quickcreate/matter</c> never copies a source column
/// the CALLER cannot read. The source is read app-only (after the record-level Read check in
/// <c>QuickCreateSourceAccessFilter</c>, unchanged), then masked through one caller read of the same row
/// (<c>MappingSourceCallerMask</c>) before any field-mapping rule sees it.
/// </summary>
/// <remarks>
/// The real pipeline runs end to end (<see cref="OfficeQuickCreateTestWebAppFactory"/>); the caller's own Dataverse is
/// <see cref="MappingSourceCallerReadDouble"/>, which masks a field-secured column as null the way Dataverse does.
/// </remarks>
public class OfficeQuickCreateSourceFieldSecurityTests
{
    private const string Route = "/api/office/quickcreate/matter";

    private static readonly Guid OwnerId = Guid.Parse("7d0e8a41-3b5c-4f2e-9a10-5c2b7e4f1a02");
    private static readonly Guid MatterTypeId = Guid.Parse("b2c4e6a8-1d3f-4a5b-8c7d-9e0f1a2b3c02");
    private static readonly Guid SourceProjectId = Guid.Parse("1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b2188");
    private static readonly Guid AccountId = Guid.Parse("9f8e7d6c-5b4a-4c3d-8e2f-1a0b9c8d7e02");
    private static readonly Guid BusinessUnitId = Guid.Parse("cb15f587-baa0-f111-aaac-000d3a992188");

    /// <summary>The service's step-8 warning when the owning team's business unit cannot be read (the harness scripts it).</summary>
    private const string BusinessUnitDefaultsWarning = "Business-unit search defaults could not be read";

    private const string Secret = "TOP-SECRET-FEE-ARRANGEMENT";

    /// <summary>Copy a readable text column, copy a lookup, copy the secured column, and template the secured column.</summary>
    private static readonly FieldMappingRuleEntity[] Profile =
    {
        Rule("copy-ref", "sprk_clientreference", 0, "sprk_clientreference", 0, mappingType: 0, order: 1),
        Rule("copy-client", "sprk_client", 1, "sprk_client", 1, mappingType: 0, order: 2),
        Rule("copy-secret", "sprk_feearrangement", 0, "sprk_matterdescription", 6, mappingType: 0, order: 3),
        Rule("template-secret", "", 0, "sprk_matternotes", 6, mappingType: 3, order: 4,
            expression: "Fee: {sprk_feearrangement}"),
    };

    private static Entity Source(bool withSecret = true, bool withClient = true)
    {
        var source = new Entity("sprk_project", SourceProjectId) { ["sprk_clientreference"] = "ACME-REF" };
        if (withClient)
        {
            source["sprk_client"] = new EntityReference("account", AccountId);
        }

        if (withSecret)
        {
            source["sprk_feearrangement"] = Secret;
        }

        return source;
    }

    // ── must fire ───────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_Matter_AFieldSecuredSourceColumnTheCallerCannotRead_IsNotCopied_AndWarnsExactlyAsAnEmptyColumn()
    {
        var secured = await CreateAsync(Source(withSecret: true), caller => caller.Secured.Add("sprk_feearrangement"));
        var empty = await CreateAsync(Source(withSecret: false));

        secured.Status.Should().Be(HttpStatusCode.Created);
        secured.Matter!.Attributes.Values.OfType<string>().Should().NotContain(v => v.Contains(Secret),
            "a column the caller cannot read is never copied, by Copy or by Template");
        secured.Matter.Contains("sprk_matterdescription").Should().BeFalse();
        secured.Matter.GetAttributeValue<string>("sprk_matternotes").Should().Be("Fee: ");
        secured.Matter.GetAttributeValue<string>("sprk_clientreference").Should().Be("ACME-REF", "readable columns still copy");

        secured.Warnings.Should().Equal(empty.Warnings,
            "a masked column must be indistinguishable from an empty one: same texts, same count, same order");
        secured.Warnings.Should().ContainSingle(w => w.Contains("{sprk_feearrangement}") && w.Contains("left blank"))
            .And.NotContain(w => w.Contains(BusinessUnitDefaultsWarning), "the harness answers the owning team's business unit");
        secured.Matter.Attributes.Should().BeEquivalentTo(empty.Matter!.Attributes);
    }

    [Fact]
    public async Task Post_Matter_ALookupTheCallerCannotRead_IsNotCopied_AndWarnsExactlyAsAnEmptyLookup()
    {
        var secured = await CreateAsync(Source(), caller => caller.Secured.Add("sprk_client"));
        var empty = await CreateAsync(Source(withClient: false));

        secured.Status.Should().Be(HttpStatusCode.Created);
        secured.Matter!.Contains("sprk_client").Should().BeFalse();
        secured.Warnings.Should().Equal(empty.Warnings);
        secured.Warnings.Should().NotContain(w => w.Contains(BusinessUnitDefaultsWarning), "the harness answers the owning team's business unit");
    }

    public static TheoryData<string> CallerReadFailures() => new() { "403", "404", "500", "0", "fault", "timeout" };

    [Theory]
    [MemberData(nameof(CallerReadFailures))]
    public async Task Post_Matter_WhenTheCallerReadDoesNotAnswer_CopiesNothing_AndGivesTheOneSourceLevelWarning(string failure)
    {
        var result = await CreateAsync(Source(), caller =>
        {
            switch (failure)
            {
                case "fault": caller.Throw = new HttpRequestException("connection reset"); break;
                case "timeout": caller.Throw = new TaskCanceledException("HttpClient.Timeout elapsed"); break;
                default: caller.FailWithStatus = int.Parse(failure, System.Globalization.CultureInfo.InvariantCulture); break;
            }
        });
        var appReadFailed = await CreateAsync(source: null);

        result.Status.Should().Be(HttpStatusCode.Created, "the create proceeds without the mapped values");
        result.Matter!.Contains("sprk_clientreference").Should().BeFalse("never a fallback to the app-read values");
        result.Matter.Contains("sprk_client").Should().BeFalse();
        result.Matter.Contains("sprk_matterdescription").Should().BeFalse();
        result.Matter.Contains("sprk_matternotes").Should().BeFalse();
        // FR-04 (G7, signed off 2026-10-22): the service's one "source unavailable" text, and nothing mapped — not even a Default rule.
        result.Warnings.Should().ContainSingle(w => w == MappingSourceReadProbe.SourceUnavailableWarning)
            .And.NotContain(w => w.Contains(BusinessUnitDefaultsWarning), "the harness answers the owning team's business unit");
        result.Warnings.Should().Equal(appReadFailed.Warnings,
            "a source the caller read could not use reads exactly like one the app read could not (missing, denied or faulted)");
        result.AppSourceReads.Should().Be(1, "the source is read app-only once; a failed caller read is never retried app-only");
    }

    // ── must not fire ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_Matter_ReadableScalarAndLookupColumns_AreCopied_ThroughOneCallerRead()
    {
        var result = await CreateAsync(Source());

        result.Status.Should().Be(HttpStatusCode.Created);
        result.Matter!.GetAttributeValue<string>("sprk_clientreference").Should().Be("ACME-REF");
        result.Matter.GetAttributeValue<EntityReference>("sprk_client").Should().BeEquivalentTo(new EntityReference("account", AccountId));
        result.Matter.GetAttributeValue<string>("sprk_matterdescription").Should().Be(Secret, "a column the caller CAN read copies");
        result.Matter.GetAttributeValue<string>("sprk_matternotes").Should().Be($"Fee: {Secret}");
        result.CallerReads.Should().ContainSingle().Which.Should().Be($"sprk_projects({SourceProjectId:D})");
    }

    // ── harness ─────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record Outcome(
        HttpStatusCode Status, Entity? Matter, IReadOnlyList<string> Warnings, int AppSourceReads, IReadOnlyList<string> CallerReads);

    /// <param name="source">The app-only read's answer; <see langword="null"/> makes the app read fault.</param>
    /// <param name="arrangeCaller">Shapes the caller's own read (default: reads everything the app read returned).</param>
    private static async Task<Outcome> CreateAsync(Entity? source, Action<MappingSourceCallerReadDouble>? arrangeCaller = null)
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        factory.CallerResolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Resolved(OwnerId.ToString("D")));
        factory.CallerDataverse.WithRow("sprk_mattertype_ref", MatterTypeId); // the type, checked as the caller (task 018b)
        // The owning team -> business unit read (task 015), so step 8's "Business-unit search defaults" warning is not on
        // every outcome and a warning-list assertion compares only what the mapping said.
        factory.Entities
            .Setup(e => e.RetrieveAsync("team", RecordOwnershipResolverDouble.DefaultTeamId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("team", RecordOwnershipResolverDouble.DefaultTeamId)
            {
                ["businessunitid"] = new EntityReference("businessunit", BusinessUnitId),
            });
        factory.Entities
            .Setup(e => e.RetrieveAsync("businessunit", BusinessUnitId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("businessunit", BusinessUnitId));
        factory.AccessProbe
            .Setup(p => p.GetCallerRightsAsync(It.IsAny<string>(), "sprk_projects", SourceProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccessRights.Read);
        factory.FieldMappings
            .Setup(m => m.GetFieldMappingProfileWithRulesAsync("sprk_project", "sprk_matter", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FieldMappingProfileEntity
            {
                Id = Guid.NewGuid(), Name = "Project to Matter", SourceEntity = "sprk_project", TargetEntity = "sprk_matter",
                IsActive = true, Rules = Profile.ToList(),
            });

        var sourceRead = factory.Entities
            .Setup(e => e.RetrieveAsync("sprk_project", SourceProjectId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()));
        if (source is null)
        {
            sourceRead.ThrowsAsync(new InvalidOperationException("Dataverse unavailable"));
        }
        else
        {
            sourceRead.ReturnsAsync(source);
        }

        // The creation service's step-11 write (task 018b), and the number read-back of whatever id it picked.
        var created = factory.CaptureRecordCreationWrite();
        factory.Entities
            .Setup(e => e.RetrieveAsync("sprk_matter", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid id, string[] _, CancellationToken _) =>
                new Entity("sprk_matter", id) { ["sprk_matternumber"] = "MAT-002188" });

        arrangeCaller?.Invoke(factory.CallerRead);

        var response = await factory.CreateClient().PostAsJsonAsync(Route, new QuickCreateRequest
        {
            Name = "Acme Fee Review",
            MatterTypeId = MatterTypeId,
            SourceEntityType = "sprk_project",
            SourceRecordId = SourceProjectId,
        });

        var body = response.StatusCode == HttpStatusCode.Created
            ? await response.Content.ReadFromJsonAsync<QuickCreateResponse>()
            : null;
        var appSourceReads = factory.Entities.Invocations.Count(i =>
            i.Method.Name == nameof(IGenericEntityService.RetrieveAsync) && Equals(i.Arguments[0], "sprk_project"));

        return new Outcome(response.StatusCode, created.Entity, body?.Warnings ?? Array.Empty<string>(), appSourceReads,
            factory.CallerRead.Paths.ToList());
    }

    private static FieldMappingRuleEntity Rule(
        string name, string sourceField, int sourceType, string targetField, int targetType,
        int mappingType, int order, string? defaultValue = null, string? expression = null) => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            SourceField = sourceField,
            SourceFieldType = sourceType,
            TargetField = targetField,
            TargetFieldType = targetType,
            MappingType = mappingType,
            ExecutionOrder = order,
            DefaultValue = defaultValue,
            Expression = expression,
            IsActive = true,
        };
}

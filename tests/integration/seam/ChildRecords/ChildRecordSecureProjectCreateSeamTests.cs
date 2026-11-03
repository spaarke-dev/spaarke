using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;
using Xunit;
using static Sprk.Bff.Api.Tests.DataMutation.ExternalAccess.SecureRootInheritanceTests;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// field-mapping-server-write-path-r1 task 020 (spec FR-15): <c>POST /api/v1/child-records/sprk_project</c> — a project filed
/// under a SECURE matter by its ADR-024 pair is created INTO isolation by the service's secure-root plan, exactly as a work
/// assignment is (<c>SecureRootInheritanceWriterTests.ChildRecordRoute.cs</c>, the pattern copied): owned by the named
/// Secure Record Owners team in the create itself, then completed in the request — flag, own container, the creator's share.
/// Under an ordinary matter it stays ordinary; a caller walled off the secure matter, or without AppendTo on it, is refused
/// with nothing written. Driven through the REAL handler, the host's REAL <see cref="SecureRootFilingGate"/> and
/// provisioning's fixture. The user's own "Secure Project" choice on a project that inherits nothing stays on
/// <c>/external-access/provision-project</c> (unified-access-control-r2) and is not this route's.
/// </summary>
public partial class SecureRootInheritanceWriterTests
{
    /// <summary>A project filed under <paramref name="matter"/> by its pair, through the route (inferred mapping: no profile).</summary>
    private Task<IResult> CreateProjectThroughTheRoute(
        Guid matter, IFieldMappingDataverseService appOnly, SecureChildOwnershipAiToolTests.ScriptedUserClient? user = null)
    {
        var matterType = RecordTypeRef(_fixture, "sprk_matter");
        var payload = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["sprk_projectname"] = "Phase 1",
            [SecureRootInheritance.PairIdColumn] = matter.ToString("D"),
            [SecureRootInheritance.PairTypeColumn + "@odata.bind"] = $"/sprk_recordtype_refs({matterType:D})",
        });

        // The I-1 stamp's own reads: the catalog row the pair names, and the matter (a root: it stamps itself).
        var stamps = new StampWorld().RecordType(matterType, "sprk_matter").Row("sprk_matter", matter);

        // Step 7 reads the pair's record type app-only from the catalog, through the service's own reader
        // (SecureRootInheritance.PairParentAsync).
        var serviceReads = RouteRecordCreation.Entities();
        serviceReads
            .Setup(e => e.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_recordtype_ref"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity>
            {
                new("sprk_recordtype_ref", matterType) { ["sprk_recordlogicalname"] = "sprk_matter" },
            }));

        using var scope = _fixture.Services.CreateScope();
        return ChildRecordEndpoints.CreateAsync(
            "sprk_project", payload, user ?? Scripted(),
            RouteRecordCreation.Over(
                new RecordOwnershipResolver(
                    SecureChildShareWorld.EntitiesOver(() => _fixture.ChildWorld).Object, SecureChildShareWorld.Configuration(),
                    NullLogger<RecordOwnershipResolver>.Instance),
                appOnly,
                stamps.Restamper,
                scope.ServiceProvider.GetRequiredService<SecureChildShareSynchronizer>(),
                _gate,
                new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
                entities: serviceReads.Object),
            new DefaultHttpContext(), NullLogger<Program>.Instance, CancellationToken.None,
            callerAccess: new CallerRecordAccessProbe(
                new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance));
    }

    /// <summary>The app-only create into the fixture, and no field-mapping profile for matter → project (mapping is a no-op).</summary>
    private Mock<IFieldMappingDataverseService> ProjectCreatesIntoTheWorld()
    {
        var appOnly = AppCreatesIntoTheWorld();
        appOnly
            .Setup(s => s.GetFieldMappingProfileWithRulesAsync("sprk_matter", "sprk_project", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((FieldMappingProfileEntity?)null);
        return appOnly;
    }

    [Fact]
    public async Task ChildRecordRoute_AProjectUnderASecureMatter_IsCreatedIntoIsolation_AndComesOutSecure()
    {
        var (secure, _) = Matters();

        var result = await CreateProjectThroughTheRoute(secure, ProjectCreatesIntoTheWorld().Object);

        RouteStatus(result).Should().Be(StatusCodes.Status201Created, RouteDetail(result));
        var (table, created, fields) = _writes.Should().ContainSingle().Subject;
        table.Should().Be("sprk_project");
        BoundId(fields, "ownerid@odata.bind").Should().Be(SecureTeam, "created owned by the named team — never the caller's unit first");
        fields["sprk_issecure"].Should().Be(true, "flagged in the create itself");
        BoundId(fields, "sprk_CreatedByPerson@odata.bind").Should().Be(Creator);
        RouteBody(result).GetProperty("id").GetGuid().Should().Be(created);
        RouteBody(result).TryGetProperty("warnings", out _).Should().BeFalse("securing it was completed in the request");
        ShouldBeSecure(created, "a project created under a secure matter through the route");
    }

    [Fact]
    public async Task ChildRecordRoute_AProjectUnderAnOrdinaryMatter_StaysOrdinary()
    {
        var (_, ordinary) = Matters();

        var result = await CreateProjectThroughTheRoute(ordinary, ProjectCreatesIntoTheWorld().Object);

        RouteStatus(result).Should().Be(StatusCodes.Status201Created, RouteDetail(result));
        var (_, created, fields) = _writes.Should().ContainSingle().Subject;
        fields.Should().NotContainKey("sprk_issecure");
        ShouldNotBeSecured(created, "filed under an ordinary matter");
    }

    [Fact]
    public async Task ChildRecordRoute_AProjectUnderASecureMatter_WhenTheCallerIsOnItsNoAccessList_Is403_AndNothingIsCreated()
    {
        var (secure, _) = Matters();
        _fixture.NoAccessList.DenySystemUserOnRecord(Creator, secure);

        var result = await CreateProjectThroughTheRoute(secure, ProjectCreatesIntoTheWorld().Object);

        RouteStatus(result).Should().Be(StatusCodes.Status403Forbidden);
        RouteReason(result).Should().Be(ProvisionProjectEndpoint.ReasonCreatorNoAccess);
        _writes.Should().BeEmpty();
    }

    /// <summary>
    /// The uniform refusal: a caller without AppendTo on the secure matter the pair names gets the route's not-found — the same
    /// status, code and words as a matter that does not exist — and nothing is created.
    /// </summary>
    [Fact]
    public async Task ChildRecordRoute_AProjectUnderASecureMatterTheCallerCannotAppendTo_IsTheUniformNotFound_AsAMissingMatter()
    {
        var (secure, _) = Matters();
        var denied = Scripted();
        denied.NoAppendTo.Add(secure);
        var nowhere = Guid.NewGuid();
        var missing = Scripted();
        missing.Missing.Add(nowhere); // the caller's AppendTo question on it answers 404: it does not exist

        var refused = await CreateProjectThroughTheRoute(secure, ProjectCreatesIntoTheWorld().Object, denied);
        var absent = await CreateProjectThroughTheRoute(nowhere, ProjectCreatesIntoTheWorld().Object, missing);

        RouteStatus(refused).Should().Be(StatusCodes.Status404NotFound);
        RouteReason(refused).Should().Be(ChildRecordEndpoints.NotFoundCode);
        RouteStatus(absent).Should().Be(RouteStatus(refused));
        RouteReason(absent).Should().Be(RouteReason(refused));
        RouteDetail(absent).Should().Be(RouteDetail(refused), "one answer whether the matter is secure, unappendable or missing");
        RouteDetail(refused).Should().NotContain(secure.ToString("D"));
        _writes.Should().BeEmpty();
    }
}

using System.Net;
using System.Net.Http;
using System.Text;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// #2253 fix round 1 (F1-a): every Spaarke.Dataverse builder that pastes a table, entity-set, column or navigation name into
/// a Web API URL (or an <c>@odata.id</c>) refuses a name that is not a bare identifier
/// (<see cref="DataverseIdentifier"/>) before it builds the URL or sends anything. The reviewed attack:
/// <c>sprk_externalrecordaccess')?$select=EntitySetName#</c> made <c>EntityDefinitions(LogicalName='{name}')</c> answer for
/// <c>sprk_externalrecordaccess</c>. Must fire: that name and its relatives on each builder, nothing sent. Must not fire: a
/// real logical name, a lookup's <c>_{column}_value</c>; the metadata GET goes out with exactly the name it was given.
/// </summary>
public class DataverseIdentifierUrlGuardTests
{
    private const string Crafted = "sprk_externalrecordaccess')?$select=EntitySetName#";

    public static TheoryData<string?> NotTableNames() => new()
    {
        Crafted,
        "sprk_matter'",
        "sprk_matter#",
        "sprk_matter?x=1",
        "sprk_matter/x",
        "sprk_matter(1)",
        "sprk matter",
        " sprk_matter",
        "sprk_matter\n",
        "_sprk_matter",
        "1sprk_matter",
        "sprk_mätter",
        "sprk_matter%27",
        "",
        null,
    };

    // ── The helper ──────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(NotTableNames))]
    public void ANameThatIsNotAnIdentifier_IsNoTableName(string? name)
    {
        DataverseIdentifier.IsTableName(name).Should().BeFalse();
        var act = () => DataverseIdentifier.RequireTableName(name, "name");
        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("name");
    }

    [Theory]
    [InlineData("sprk_matter")]
    [InlineData("sprk_externalrecordaccesses")]
    [InlineData("SPRK_Matter")]
    [InlineData("account")]
    [InlineData("sprk_recordtype_refs")]
    [InlineData("x2")]
    public void AnIdentifier_IsATableName(string name)
    {
        DataverseIdentifier.IsTableName(name).Should().BeTrue();
        DataverseIdentifier.RequireTableName(name, "name").Should().Be(name);
    }

    [Theory]
    [InlineData("_sprk_matter_value", true)]
    [InlineData("sprk_Matter", true)]
    [InlineData("sprk_matter@odata.bind", false)]
    [InlineData("sprk_matter,ownerid", false)]
    [InlineData("sprk_matter)", false)]
    [InlineData("", false)]
    public void AColumnName_MayStartWithAnUnderscore_AndIsOtherwiseAnIdentifier(string name, bool expected) =>
        DataverseIdentifier.IsColumnName(name).Should().Be(expected);

    // ── The builders (nothing is sent) ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(NotTableNames))]
    public async Task TheMetadataRead_RefusesANameThatIsNotAnIdentifier_AndSendsNothing(string? name)
    {
        var handler = new RecordingHandler(_ => Json("{\"EntitySetName\":\"sprk_externalrecordaccesses\"}"));
        var sut = new OfflineService(handler);

        await FluentActions.Awaiting(() => sut.GetEntitySetNameAsync(name!)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => sut.GetEntityObjectTypeCodeAsync(name!)).Should().ThrowAsync<ArgumentException>();

        handler.Requests.Should().BeEmpty("the name is refused before a URL is built");
    }

    [Fact]
    public async Task TheGenericUpdate_ACraftedTable_IsRefused_AndNothingIsSent()
    {
        var handler = new RecordingHandler(_ => Json("{\"EntitySetName\":\"sprk_externalrecordaccesses\"}"));
        var sut = new OfflineService(handler);
        var fields = new Dictionary<string, object?> { ["sprk_accesslevel"] = 3 };

        await FluentActions.Awaiting(() => sut.UpdateRecordFieldsAsync(Crafted, Guid.NewGuid(), fields))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => sut.UpdateExistingRecordFieldsAsync(Crafted, Guid.NewGuid(), fields))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => sut.UpdateRecordFieldsAsync(Crafted, Guid.NewGuid(), fields, default, Guid.NewGuid()))
            .Should().ThrowAsync<ArgumentException>("an impersonated update skips the BFF refusal, so the URL builder decides");

        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task TheReadsAndTheShareWrites_RefuseACraftedName_AndSendNothing()
    {
        var handler = new RecordingHandler(r => r.RequestUri!.AbsolutePath.Contains("EntityDefinitions", StringComparison.Ordinal)
            ? Json("{\"EntitySetName\":\"sprk_matters\"}")
            : Json("{\"value\":[]}"));
        var sut = new OfflineService(handler);
        var id = Guid.NewGuid();

        await FluentActions.Awaiting(() => sut.RetrieveRecordFieldsAsync("sprk_matter", id, ["sprk_name", "x)?$expand=y#"]))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => sut.QueryChildRecordIdsAsync("sprk_event", "sprk_matter_value eq 1 or x", id))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => sut.RetrieveMultipleAppOnlyAsync(Crafted, null))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => sut.GetPrincipalAccessAsync("sprk_matter' or objecttypecode ne 'x", id))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => sut.GetPrincipalAccessForRecordsOrThrowAsync("sprk_matter'", [id]))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => sut.GrantAccessAsync("sprk_matters)/x#", id, DataversePrincipalRef.User(id), "ReadAccess"))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => sut.RevokeAccessAsync("sprk_matters)/x#", id, DataversePrincipalRef.User(id)))
            .Should().ThrowAsync<ArgumentException>();

        // RetrieveRecordFields / QueryChildRecordIds resolve the set first (metadata), then refuse the column: nothing else.
        handler.Requests.Should().OnlyContain(r => r.AbsolutePath.Contains("EntityDefinitions", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheClient_RefusesACraftedSet_BeforeItBuildsAUrl()
    {
        using var client = new DataverseWebApiClient(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
            }).Build(),
            NullLogger<DataverseWebApiClient>.Instance,
            new StaticTokenCredential());
        var id = Guid.NewGuid();

        await FluentActions.Awaiting(() => client.RetrieveAsync<object>(Crafted, id)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => client.CreateAsync(Crafted, new { })).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => client.UpdateAsync(Crafted, id, new { })).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => client.UpdateIfMatchAsync(Crafted, id, new { }, "W/\"1\""))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => client.DeleteAsync(Crafted, id)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => client.AssociateAsync("sprk_matters(1)/x/$ref", id, Crafted))
            .Should().ThrowAsync<ArgumentException>();
    }

    // ── Must not fire ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ARealLogicalName_IsAskedAbout_AsItWasGiven()
    {
        var handler = new RecordingHandler(_ => Json("{\"EntitySetName\":\"sprk_externalrecordaccesses\"}"));
        var sut = new OfflineService(handler);

        (await sut.GetEntitySetNameAsync("sprk_externalrecordaccess")).Should().Be("sprk_externalrecordaccesses");

        handler.Requests.Should().ContainSingle().Which.PathAndQuery.Should().Be(
            "/api/data/v9.2/EntityDefinitions(LogicalName='sprk_externalrecordaccess')?$select=EntitySetName");
    }

    [Fact]
    public async Task ALookupColumn_IsSelected()
    {
        var handler = new RecordingHandler(r => r.RequestUri!.AbsolutePath.Contains("EntityDefinitions", StringComparison.Ordinal)
            ? Json("{\"EntitySetName\":\"sprk_matters\"}")
            : Json("{\"sprk_name\":\"m\"}"));
        var sut = new OfflineService(handler);

        await sut.RetrieveRecordFieldsAsync("sprk_matter", Guid.Empty, ["sprk_name", "_ownerid_value"]);

        handler.Requests.Should().Contain(r => r.Query.Contains("$select=sprk_name,_ownerid_value", StringComparison.Ordinal));
    }

    // ── #2253 fix round 3 (F1): the multi-row read accepts one table's metadata collection ─────────────────────

    private static readonly Guid Caller = Guid.Parse("22530003-0000-4000-8000-000000000001");

    [Fact]
    public async Task ThePushAttributeRead_ThroughTheRealService_SendsOneRequest_ForTheMetadataPath()
    {
        var handler = new RecordingHandler(_ => Json("{\"value\":[]}"));
        var sut = new OfflineService(handler);

        await sut.RetrieveMultipleImpersonatedAsync(
            Sprk.Bff.Api.Api.FieldMappings.FieldMappingPushLookups.AttributeMetadataPath("sprk_matter"),
            Sprk.Bff.Api.Api.FieldMappings.FieldMappingPushLookups.AttributeMetadataQuery,
            Caller);

        handler.Requests.Should().ContainSingle().Which.PathAndQuery.Should().Be(
            "/api/data/v9.2/EntityDefinitions(LogicalName='sprk_matter')/Attributes"
            + "?$select=LogicalName,AttributeType,IsValidODataAttribute");
    }

    [Fact]
    public async Task ThePushParentLookupRead_ThroughTheRealService_SendsOneRequest_ForTheMetadataPath()
    {
        var handler = new RecordingHandler(_ => Json("{\"value\":[]}"));
        var sut = new OfflineService(handler);

        await sut.RetrieveMultipleImpersonatedAsync(
            Sprk.Bff.Api.Api.FieldMappings.FieldMappingEndpoints.ParentLookupMetadataPath("sprk_event"),
            Sprk.Bff.Api.Api.FieldMappings.FieldMappingEndpoints.ParentLookupMetadataQuery,
            Caller);

        handler.Requests.Should().ContainSingle().Which.PathAndQuery.Should().Be(
            "/api/data/v9.2/EntityDefinitions(LogicalName='sprk_event')/ManyToOneRelationships"
            + "?$select=ReferencingAttribute,ReferencedEntity,ReferencingEntityNavigationPropertyName");
    }

    [Theory]
    [InlineData("sprk_communications")]
    [InlineData("EntityDefinitions(LogicalName='sprk_matter')/Attributes")]
    [InlineData("EntityDefinitions(LogicalName='sprk_matter')/ManyToOneRelationships")]
    [InlineData("EntityDefinitions(LogicalName='sprk_matter')/OneToManyRelationships")]
    [InlineData("EntityDefinitions(LogicalName='sprk_matter')/ManyToManyRelationships")]
    [InlineData("EntityDefinitions(LogicalName='account')/Keys")]
    public async Task TheMultiRowRead_ABareSetOrAMetadataCollection_IsSentAsGiven(string path)
    {
        DataverseIdentifier.IsCollectionPath(path).Should().BeTrue();

        var handler = new RecordingHandler(_ => Json("{\"value\":[]}"));
        var sut = new OfflineService(handler);

        await sut.RetrieveMultipleImpersonatedAsync(path, "$top=1", Caller);
        await sut.RetrieveMultipleAppOnlyAsync(path, null);

        handler.Requests.Select(r => r.PathAndQuery).Should().Equal(
            $"/api/data/v9.2/{path}?$top=1",
            $"/api/data/v9.2/{path}");
    }

    public static TheoryData<string?> NotCollectionPaths() => new()
    {
        "EntityDefinitions(LogicalName='x')?$select=y#')/Attributes",
        "EntityDefinitions(LogicalName='sprk_matter')/Attributes?$select=x",
        "EntityDefinitions(LogicalName='sprk_matter')/Attributes/x",
        "EntityDefinitions(LogicalName='sprk_matter')/Attributes#",
        "EntityDefinitions(LogicalName='sprk_matter')/attributes",
        "EntityDefinitions(LogicalName='sprk_matter')/Privileges",
        "EntityDefinitions(LogicalName='sprk_matter')/",
        "EntityDefinitions(LogicalName='sprk_matter')",
        "EntityDefinitions(LogicalName='sprk matter')/Attributes",
        "EntityDefinitions(LogicalName='')/Attributes",
        "EntityDefinitions(LogicalName='_x')/Attributes",
        "EntityDefinitions(LogicalName='sprk_matter')/Attributes\n",
        "entitydefinitions(LogicalName='sprk_matter')/Attributes",
        "EntityDefinitions(MetadataId=00000000-0000-0000-0000-000000000000)/Attributes",
        "RelationshipDefinitions(SchemaName='x')",
        "systemusers(00000000-0000-0000-0000-000000000000)/systemuserroles_association",
        "sprk_matters(00000000-0000-0000-0000-000000000000)",
        "/sprk_matters",
        Crafted,
        "",
        null,
    };

    [Theory]
    [MemberData(nameof(NotCollectionPaths))]
    public async Task TheMultiRowRead_ACraftedPath_IsRefused_AndNothingIsSent(string? path)
    {
        DataverseIdentifier.IsCollectionPath(path).Should().BeFalse();
        FluentActions.Invoking(() => DataverseIdentifier.RequireCollectionPath(path, "path"))
            .Should().Throw<ArgumentException>().Which.ParamName.Should().Be("path");

        var handler = new RecordingHandler(_ => Json("{\"value\":[]}"));
        var sut = new OfflineService(handler);

        await FluentActions.Awaiting(() => sut.RetrieveMultipleImpersonatedAsync(path!, null, Caller))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => sut.RetrieveMultipleAppOnlyAsync(path!, null))
            .Should().ThrowAsync<ArgumentException>();

        handler.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("x')?$select=y#")]
    [InlineData("sprk_matter')/Attributes?x=")]
    [InlineData("_sprk_matter")]
    [InlineData("1sprk_matter")]
    [InlineData("sprk_matter\n")]
    public void ThePushMetadataPaths_RefuseACraftedTable_WhereTheyAreBuilt(string table)
    {
        FluentActions.Invoking(() => Sprk.Bff.Api.Api.FieldMappings.FieldMappingPushLookups.AttributeMetadataPath(table))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => Sprk.Bff.Api.Api.FieldMappings.FieldMappingEndpoints.ParentLookupMetadataPath(table))
            .Should().Throw<ArgumentException>();
    }

    // ── #2253 fix round 3 (K): a lookup name with a trailing newline (`$` matched before it; `\z` does not) ─────

    [Fact]
    public async Task TheExternalInvoiceLookupRead_ALookupNameWithATrailingNewline_IsRefused_AndNothingIsSent()
    {
        var handler = new RecordingHandler(_ => Json("{\"_sprk_matter_value\":null}"));
        var sut = new Sprk.Bff.Api.Infrastructure.ExternalAccess.ExternalDataService(
            new HttpClient(handler),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
            }).Build(),
            new StaticTokenCredential(),
            NullLogger<Sprk.Bff.Api.Infrastructure.ExternalAccess.ExternalDataService>.Instance);

        await FluentActions.Awaiting(() => sut.GetInvoiceLookupsAsync(Guid.NewGuid(), ["sprk_matter\n"]))
            .Should().ThrowAsync<ArgumentException>();

        handler.Requests.Should().BeEmpty();
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>The production service, built through its protected test constructor with a static token.</summary>
    private sealed class OfflineService(RecordingHandler handler) : DataverseWebApiService(
        new HttpClient(handler),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
        }).Build(),
        NullLogger<DataverseWebApiService>.Instance,
        new Sprk.Bff.Api.Services.Ai.Membership.NullMembershipCacheInvalidator(
            NullLogger<Sprk.Bff.Api.Services.Ai.Membership.NullMembershipCacheInvalidator>.Instance),
        confidentialClients: null,
        credential: new StaticTokenCredential());

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(respond(request));
        }
    }

    private sealed class StaticTokenCredential : TokenCredential
    {
        private static readonly AccessToken Token = new("test-token", DateTimeOffset.MaxValue);

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => Token;

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(Token);
    }
}

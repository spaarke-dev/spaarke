using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Workspace;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Workspace;

/// <summary>
/// Unit tests for <see cref="TodoRegardingBuilder"/>.
/// </summary>
/// <remarks>
/// Verifies ADR-024 invariants on the server-side helper:
/// <list type="bullet">
///   <item>Specific lookup + 4 resolver fields populated atomically</item>
///   <item>Multi-lookup guard: throws when a regarding lookup is already set</item>
///   <item>Unsupported parent entity rejected</item>
///   <item>sprk_recordtype_ref missing → resolver type field left unset (non-fatal)</item>
///   <item>No hard-coded org URL or tenant id (URL is relative)</item>
/// </list>
/// </remarks>
[Trait("status", "repaired")]
public class TodoRegardingBuilderTests
{
    private readonly Mock<ICommunicationDataverseService> _commServiceMock;
    private readonly Mock<ILogger<TodoRegardingBuilder>> _loggerMock;

    public TodoRegardingBuilderTests()
    {
        _commServiceMock = new Mock<ICommunicationDataverseService>(MockBehavior.Loose);
        _loggerMock = new Mock<ILogger<TodoRegardingBuilder>>();

        // Default: sprk_recordtype_ref not found
        _commServiceMock
            .Setup(c => c.QueryRecordTypeRefAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity?)null);
    }

    private TodoRegardingBuilder CreateBuilder(Entity? recordTypeRef = null)
    {
        if (recordTypeRef is not null)
        {
            _commServiceMock
                .Setup(c => c.QueryRecordTypeRefAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(recordTypeRef);
        }

        return new TodoRegardingBuilder(_commServiceMock.Object, _loggerMock.Object);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Constructor
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullCommService_Throws()
    {
        var act = () => new TodoRegardingBuilder(null!, _loggerMock.Object);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("communicationService");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new TodoRegardingBuilder(_commServiceMock.Object, null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Argument validation
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApplyResolverFieldsAsync_NullEntity_Throws()
    {
        var builder = CreateBuilder();
        var act = async () => await builder.ApplyResolverFieldsAsync(
            null!, "sprk_matter", Guid.NewGuid(), "Acme", CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("todoEntity");
    }

    [Fact]
    public async Task ApplyResolverFieldsAsync_WrongEntity_Throws()
    {
        var builder = CreateBuilder();
        var entity = new Entity("sprk_event"); // NOT sprk_todo
        var act = async () => await builder.ApplyResolverFieldsAsync(
            entity, "sprk_matter", Guid.NewGuid(), "Acme", CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("todoEntity")
            .WithMessage("*sprk_todo*");
    }

    [Fact]
    public async Task ApplyResolverFieldsAsync_EmptyGuid_Throws()
    {
        var builder = CreateBuilder();
        var entity = new Entity("sprk_todo");
        var act = async () => await builder.ApplyResolverFieldsAsync(
            entity, "sprk_matter", Guid.Empty, "Acme", CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("regardingId");
    }

    [Fact]
    public async Task ApplyResolverFieldsAsync_UnsupportedEntity_Throws()
    {
        var builder = CreateBuilder();
        var entity = new Entity("sprk_todo");
        var act = async () => await builder.ApplyResolverFieldsAsync(
            entity, "sprk_unknownentity", Guid.NewGuid(), "Anything", CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("regardingEntityName")
            .WithMessage("*not a supported sprk_todo regarding parent*");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Typical case — all four resolver fields populated atomically
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApplyResolverFieldsAsync_TypicalCase_PopulatesAllFourResolverFieldsAndSpecificLookup()
    {
        // Arrange
        var recordTypeId = Guid.NewGuid();
        var recordTypeRef = new Entity("sprk_recordtype_ref")
        {
            Id = recordTypeId,
            ["sprk_recorddisplayname"] = "Matter"
        };
        var builder = CreateBuilder(recordTypeRef: recordTypeRef);

        var matterId = Guid.NewGuid();
        var entity = new Entity("sprk_todo")
        {
            ["sprk_name"] = "Budget Alert: Acme"
        };

        // Act
        await builder.ApplyResolverFieldsAsync(
            entity, "sprk_matter", matterId, "Acme Litigation", CancellationToken.None);

        // Assert — specific lookup
        var lookupRef = entity["sprk_regardingmatter"].Should().BeOfType<EntityReference>().Subject;
        lookupRef.LogicalName.Should().Be("sprk_matter");
        lookupRef.Id.Should().Be(matterId);
        lookupRef.Name.Should().Be("Acme Litigation");

        // Assert — all four resolver fields
        var cleanId = matterId.ToString("D").ToLowerInvariant();
        entity["sprk_regardingrecordid"].Should().Be(cleanId);
        entity["sprk_regardingrecordname"].Should().Be("Acme Litigation");
        entity["sprk_regardingrecordurl"].Should().Be(
            $"/main.aspx?pagetype=entityrecord&etn=sprk_matter&id={cleanId}");

        var typeRef = entity["sprk_regardingrecordtype"].Should().BeOfType<EntityReference>().Subject;
        typeRef.LogicalName.Should().Be("sprk_recordtype_ref");
        typeRef.Id.Should().Be(recordTypeId);
        typeRef.Name.Should().Be("Matter");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Multi-lookup guard (ADR-024)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApplyResolverFieldsAsync_WhenAnotherRegardingAlreadySet_Throws()
    {
        // Arrange — pre-populate sprk_regardingproject; attempt to set sprk_regardingmatter
        var builder = CreateBuilder();
        var entity = new Entity("sprk_todo")
        {
            ["sprk_regardingproject"] = new EntityReference("sprk_project", Guid.NewGuid())
        };

        // Act
        var act = async () => await builder.ApplyResolverFieldsAsync(
            entity, "sprk_matter", Guid.NewGuid(), "Acme", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*at most one specific regarding lookup*");
    }

    [Fact]
    public async Task ApplyResolverFieldsAsync_WhenSameRegardingAlreadySet_AlsoThrows()
    {
        // Arrange — same specific lookup already populated; the rule is "exactly one fresh write"
        var builder = CreateBuilder();
        var entity = new Entity("sprk_todo")
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", Guid.NewGuid())
        };

        // Act
        var act = async () => await builder.ApplyResolverFieldsAsync(
            entity, "sprk_matter", Guid.NewGuid(), "Acme", CancellationToken.None);

        // Assert — multi-write into the same lookup is still rejected to keep callers honest
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // sprk_recordtype_ref not found (non-fatal)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApplyResolverFieldsAsync_WhenRecordTypeRefMissing_PopulatesOtherFieldsAnyway()
    {
        // Arrange — default mock returns null for QueryRecordTypeRefAsync
        var builder = CreateBuilder();
        var matterId = Guid.NewGuid();
        var entity = new Entity("sprk_todo");

        // Act
        await builder.ApplyResolverFieldsAsync(
            entity, "sprk_matter", matterId, "Acme", CancellationToken.None);

        // Assert — specific lookup + 3 of 4 resolver fields populated; type left unset
        entity.Attributes.Should().ContainKey("sprk_regardingmatter");
        entity.Attributes.Should().ContainKey("sprk_regardingrecordid");
        entity.Attributes.Should().ContainKey("sprk_regardingrecordname");
        entity.Attributes.Should().ContainKey("sprk_regardingrecordurl");
        entity.Attributes.Should().NotContainKey("sprk_regardingrecordtype",
            "missing sprk_recordtype_ref is non-fatal — the type lookup is left unset");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // URL portability — no hard-coded org URL or tenant id
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApplyResolverFieldsAsync_RecordUrlIsRelative_NoHardCodedOrgUrlOrTenantId()
    {
        // Arrange
        var builder = CreateBuilder();
        var entity = new Entity("sprk_todo");
        var id = Guid.NewGuid();

        // Act
        await builder.ApplyResolverFieldsAsync(entity, "sprk_matter", id, "Acme", CancellationToken.None);

        // Assert — URL starts with /main.aspx (relative); no scheme, no hostname, no tenant guid
        var url = entity["sprk_regardingrecordurl"].Should().BeOfType<string>().Subject;
        url.Should().StartWith("/main.aspx");
        url.Should().NotContain("https://", "no scheme — relative URL only");
        url.Should().NotContain(".dynamics.com", "no hostname embedded");
        url.Should().NotContain(".crm.dynamics.com", "no Dataverse host embedded");
        url.Should().NotMatchRegex(@"\?.*tenantid=", "no tenant id parameter");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // All 11 supported regarding parents
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("sprk_matter", "sprk_regardingmatter")]
    [InlineData("sprk_project", "sprk_regardingproject")]
    [InlineData("sprk_event", "sprk_regardingevent")]
    [InlineData("sprk_communication", "sprk_regardingcommunication")]
    [InlineData("sprk_workassignment", "sprk_regardingworkassignment")]
    [InlineData("sprk_invoice", "sprk_regardinginvoice")]
    [InlineData("sprk_budget", "sprk_regardingbudget")]
    [InlineData("sprk_analysis", "sprk_regardinganalysis")]
    [InlineData("sprk_organization", "sprk_regardingorganization")]
    [InlineData("contact", "sprk_regardingcontact")]
    [InlineData("sprk_document", "sprk_regardingdocument")]
    public async Task ApplyResolverFieldsAsync_All11SupportedParents_MapToCorrectSpecificLookup(
        string parentEntityName, string expectedLookupAttribute)
    {
        // Arrange
        var builder = CreateBuilder();
        var entity = new Entity("sprk_todo");
        var parentId = Guid.NewGuid();

        // Act
        await builder.ApplyResolverFieldsAsync(
            entity, parentEntityName, parentId, "Display", CancellationToken.None);

        // Assert — correct specific lookup is populated
        entity.Attributes.Should().ContainKey(expectedLookupAttribute);
        var lookupRef = entity[expectedLookupAttribute].Should().BeOfType<EntityReference>().Subject;
        lookupRef.LogicalName.Should().Be(parentEntityName);
        lookupRef.Id.Should().Be(parentId);
    }
}

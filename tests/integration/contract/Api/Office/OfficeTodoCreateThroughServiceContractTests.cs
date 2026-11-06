using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Dataverse.FieldMapping;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using EntityReference = Microsoft.Xrm.Sdk.EntityReference;

namespace Sprk.Bff.Api.Tests.Api.Office;

/// <summary>
/// field-mapping-server-write-path-r1 task 030a (spec FR-18, NFR-07): <c>POST /api/office/todo</c> creates through the ONE server
/// create (<c>RecordCreationService</c>) as the caller, and its refusals keep the route's existing codes.
/// </summary>
/// <remarks>
/// <para>What is pinned: one app-only create through the service and none through <c>IGenericEntityService.CreateAsync</c>; the
/// caller identified by WhoAmI under their own token; the owner from the resolver; field mapping from the matter the To Do is
/// filed under, fill-only. The refusals, each with nothing written: a record the request names that the caller may not
/// AppendTo, or that no longer exists, is the route filter's ONE uniform body (403 OFFICE_009,
/// <c>source_record_inaccessible</c>), byte for byte, so "denied" and "not found" stay indistinguishable; no owner is 403
/// OFFICE_022; an I-1 stamp that cannot be derived is the route's generic 403 OFFICE_010.</para>
/// <para>The host is <see cref="TodoRegardingTestWebAppFactory"/>, whose caller's Dataverse and app-only create are
/// <see cref="OfficeTodoRecordCreationHost"/>'s.</para>
/// </remarks>
public class OfficeTodoCreateThroughServiceContractTests
{
    private const string Route = "/api/office/todo";

    [Fact]
    public async Task Post_Todo_UnderAMatter_IsOneAppOnlyCreateThroughTheUnifiedCreate_AsTheCaller()
    {
        using var factory = new TodoRegardingTestWebAppFactory();
        var matterId = Guid.NewGuid();

        var response = await factory.CreateClient().PostAsJsonAsync(Route, new CreateTodoRequest
        {
            Name = "Review red-lines",
            RegardingEntityType = "Matter",
            RegardingRecordId = matterId,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var (table, id, _) = factory.RecordCreation.Capture.Attempts.Should().ContainSingle(
            "the service makes ONE app-only create").Subject;
        table.Should().Be("sprk_todo");
        (await response.Content.ReadFromJsonAsync<CreateTodoResponse>())!.TodoId.Should().Be(id);
        factory.GenericCreates.Should().BeEmpty("the To Do is no longer created through IGenericEntityService.CreateAsync");
        factory.RecordCreation.CallerDataverse.Requests.Should().Contain("WhoAmI()",
            "the caller is identified under their own token (ADR-028)");

        var created = factory.CreatedEntities.Should().ContainSingle().Subject;
        created.GetAttributeValue<EntityReference>("sprk_regardingmatter")!.Id.Should().Be(matterId);
        created.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(
            new EntityReference("team", RecordOwnershipResolverDouble.DefaultTeamId));
    }

    [Fact]
    public async Task Post_Todo_UnderAMatterWithAProfile_TakesTheMattersValue_FillOnly_ReadAsTheCaller()
    {
        using var factory = new TodoRegardingTestWebAppFactory();
        var matterId = Guid.NewGuid();
        var matter = new Entity("sprk_matter", matterId)
        {
            ["sprk_matterdescription"] = "From the matter",
            ["sprk_mattername"] = "Acme v. Beta",
        };
        factory.SourceRows[("sprk_matter", matterId)] = matter;
        factory.RecordCreation.CallerReadableRows[matterId] = matter;
        factory.RecordCreation.FieldMappings
            .Setup(m => m.GetFieldMappingProfileWithRulesAsync("sprk_matter", "sprk_todo", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FieldMappingProfileEntity
            {
                Name = "Matter to To Do",
                SourceEntity = "sprk_matter",
                TargetEntity = "sprk_todo",
                IsActive = true,
                Rules =
                [
                    Rule("sprk_matterdescription", "sprk_description", CreateTimeFieldMapping.FieldTypeMemo, order: 0),
                    Rule("sprk_mattername", "sprk_name", CreateTimeFieldMapping.FieldTypeText, order: 1),
                ],
            });

        var response = await factory.CreateClient().PostAsJsonAsync(Route, new CreateTodoRequest
        {
            Name = "What the user typed",
            RegardingEntityType = "Matter",
            RegardingRecordId = matterId,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = factory.CreatedEntities.Should().ContainSingle().Subject;
        created.GetAttributeValue<string>("sprk_description").Should().Be("From the matter",
            "FR-18: the Matter to To Do profile fills the column the user left empty");
        created.GetAttributeValue<string>("sprk_name").Should().Be("What the user typed", "FR-02: fill-only, the user's value wins");
    }

    [Fact]
    public async Task Post_Todo_ARegardingRecordTheCallerMayNotAppendTo_IsTheFiltersUniformRefusal_AndNothingIsCreated()
    {
        var matterId = Guid.NewGuid();
        var (status, body, factory) = await PostAsync(world => world.NoAppendTo.Add(("sprk_matter", matterId)), new CreateTodoRequest
        {
            Name = "Not mine to file under",
            RegardingEntityType = "Matter",
            RegardingRecordId = matterId,
        });

        using (factory)
        {
            status.Should().Be(HttpStatusCode.Forbidden);
            body.Should().Be(await FilterDenialBodyAsync(), "the service's refusal is the route filter's ONE body, byte for byte");
            AssertNothingCreated(factory);
        }
    }

    [Fact]
    public async Task Post_Todo_ARegardingRecordThatNoLongerExists_IsRefusedExactlyAsADeniedOne()
    {
        var matterId = Guid.NewGuid();
        var (status, body, factory) = await PostAsync(world => world.Missing.Add(("sprk_matter", matterId)), new CreateTodoRequest
        {
            Name = "Filed under a deleted matter",
            RegardingEntityType = "Matter",
            RegardingRecordId = matterId,
        });

        using (factory)
        {
            status.Should().Be(HttpStatusCode.Forbidden);
            body.Should().Be(await FilterDenialBodyAsync(), "a caller must not learn whether the record exists");
            AssertNothingCreated(factory);
        }
    }

    [Theory]
    [InlineData("sprk_document")]
    [InlineData("sprk_communication")]
    [InlineData("contact")]
    public async Task Post_Todo_ACarrierOrAssigneeTheCallerMayNotAppendTo_IsTheFiltersUniformRefusal_AndNothingIsCreated(string table)
    {
        var recordId = Guid.NewGuid();
        var request = new CreateTodoRequest
        {
            Name = "Names a record the caller may not use",
            DocumentId = table == "sprk_document" ? recordId : null,
            CommunicationId = table == "sprk_communication" ? recordId : null,
            AssignedToContactId = table == "contact" ? recordId : null,
        };

        var (status, body, factory) = await PostAsync(world => world.NoAppendTo.Add((table, recordId)), request);

        using (factory)
        {
            status.Should().Be(HttpStatusCode.Forbidden);
            body.Should().Be(await FilterDenialBodyAsync());
            AssertNothingCreated(factory);
        }
    }

    [Fact]
    public async Task Post_Todo_WhenNoOwnerResolves_IsOffice022_AndNothingIsCreated()
    {
        using var factory = new TodoRegardingTestWebAppFactory();
        factory.Ownership.TeamId = null;

        var response = await factory.CreateClient().PostAsJsonAsync(Route, new CreateTodoRequest
        {
            Name = "Nobody owns this",
            RegardingEntityType = "Matter",
            RegardingRecordId = Guid.NewGuid(),
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("OFFICE_022");
        json.RootElement.GetProperty("detail").GetString().Should().Be(
            "The To Do could not be assigned an owner from the item it is filed to, so it was not created. "
            + "Check that the item still exists and that you can open it.");
        AssertNothingCreated(factory);
    }

    [Fact]
    public async Task Post_Todo_WhenTheStampCannotBeDerived_IsTheGenericOffice010_AndNothingIsCreated()
    {
        // Invoice is child-class: its stamp is read app-only, and this host's read of an invoice faults.
        using var factory = new TodoRegardingTestWebAppFactory();

        var response = await factory.CreateClient().PostAsJsonAsync(Route, new CreateTodoRequest
        {
            Name = "Stamp unavailable",
            RegardingEntityType = "Invoice",
            RegardingRecordId = Guid.NewGuid(),
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("OFFICE_010");
        AssertNothingCreated(factory);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────────────────────────────

    private static async Task<(HttpStatusCode Status, string Body, TodoRegardingTestWebAppFactory Factory)> PostAsync(
        Action<OfficeCallerDataverseDouble> arrange, CreateTodoRequest request)
    {
        var factory = new TodoRegardingTestWebAppFactory();
        arrange(factory.RecordCreation.CallerDataverse);
        var response = await factory.CreateClient().PostAsJsonAsync(Route, request);
        return (response.StatusCode, await NormalizeAsync(response), factory);
    }

    /// <summary>The route filter's own refusal of a record the caller cannot read (the filter denies; the service is not reached).</summary>
    private static async Task<string> FilterDenialBodyAsync()
    {
        using var factory = new TodoSourceAccessTestWebAppFactory();
        var response = await factory.CreateClient().PostAsJsonAsync(Route, new CreateTodoRequest
        {
            Name = "Unreadable",
            RegardingEntityType = "Matter",
            RegardingRecordId = Guid.NewGuid(),
        });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await NormalizeAsync(response);
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("OFFICE_009");
        json.RootElement.GetProperty("reasonCode").GetString().Should().Be("source_record_inaccessible");
        return body;
    }

    private static void AssertNothingCreated(TodoRegardingTestWebAppFactory factory)
    {
        factory.RecordCreation.Capture.Attempts.Should().BeEmpty("a refused create writes nothing");
        factory.GenericCreates.Should().BeEmpty();
    }

    /// <summary>The body with the per-request correlation id blanked (it is not record-derived).</summary>
    private static async Task<string> NormalizeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return Regex.Replace(body, "\"correlationId\"\\s*:\\s*\"[^\"]*\"", "\"correlationId\":\"<normalized>\"");
    }

    private static FieldMappingRuleEntity Rule(string source, string target, int targetType, int order) => new()
    {
        Name = $"{source} to {target}",
        SourceField = source,
        TargetField = target,
        TargetFieldType = targetType,
        MappingType = CreateTimeFieldMapping.MappingTypeCopy,
        ExecutionOrder = order,
        IsActive = true,
    };
}

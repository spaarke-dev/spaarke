using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// Contract tests for the External Access to-do DTOs introduced by smart-todo-decoupling-r3
/// task 007 (FR-29). Locks the JSON property names and the round-trip serialize/deserialize
/// shape consumed by the external-spa (task 008).
///
/// Replaces the former <c>ExternalEventDto</c> + <c>sprk_todoflag</c> contract. See
/// <c>projects/smart-todo-decoupling-r3/notes/external-access-contract-change.md</c> for the
/// breaking-contract migration guide.
///
/// ADR-024: The DTOs expose the four resolver fields + the project-specific lookup
/// (<c>_sprk_regardingproject_value</c>) — NOT a polymorphic <c>regardingobjectid</c>.
/// </summary>
[Trait("status", "repaired")]
public class ExternalTodoDtoTests
{
    // =========================================================================
    // ExternalTodoDto — JSON contract
    // =========================================================================

    [Fact]
    public void ExternalTodoDto_RoundTripsThroughJson()
    {
        // Arrange
        var todo = new ExternalTodoDto
        {
            SprkTodoid = "11111111-1111-1111-1111-111111111111",
            SprkName = "Review draft motion",
            SprkNotes = "Check section 4 carefully",
            SprkDuedate = "2026-07-15",
            SprkPriorityscore = 80,
            SprkEffortscore = 30,
            SprkTodocolumn = 100000000, // Today
            SprkTodopinned = true,
            Statecode = 0,
            Statuscode = 1, // Open
            Createdon = "2026-06-07T10:00:00Z",
            SprkRegardingprojectValue = "22222222-2222-2222-2222-222222222222",
            SprkRegardingrecordid = "22222222-2222-2222-2222-222222222222",
            SprkRegardingrecordname = "Acme Acquisition",
            SprkRegardingrecordurl = "/main.aspx?pagetype=entityrecord&etn=sprk_project&id=22222222-2222-2222-2222-222222222222"
        };

        // Act — round trip
        var json = JsonSerializer.Serialize(todo);
        var deserialized = JsonSerializer.Deserialize<ExternalTodoDto>(json);

        // Assert — the JSON contains the EXACT property names the external-spa consumes
        json.Should().Contain("\"sprk_todoid\":");
        json.Should().Contain("\"sprk_name\":");
        json.Should().Contain("\"sprk_notes\":");
        json.Should().Contain("\"sprk_duedate\":");
        json.Should().Contain("\"sprk_priorityscore\":");
        json.Should().Contain("\"sprk_effortscore\":");
        json.Should().Contain("\"sprk_todocolumn\":");
        json.Should().Contain("\"sprk_todopinned\":");
        json.Should().Contain("\"statecode\":");
        json.Should().Contain("\"statuscode\":");
        json.Should().Contain("\"createdon\":");
        json.Should().Contain("\"_sprk_regardingproject_value\":");
        json.Should().Contain("\"sprk_regardingrecordid\":");
        json.Should().Contain("\"sprk_regardingrecordname\":");
        json.Should().Contain("\"sprk_regardingrecordurl\":");

        // Assert — round-trip equality
        deserialized.Should().NotBeNull();
        deserialized!.SprkTodoid.Should().Be(todo.SprkTodoid);
        deserialized.SprkName.Should().Be(todo.SprkName);
        deserialized.SprkNotes.Should().Be(todo.SprkNotes);
        deserialized.SprkDuedate.Should().Be(todo.SprkDuedate);
        deserialized.SprkPriorityscore.Should().Be(todo.SprkPriorityscore);
        deserialized.SprkEffortscore.Should().Be(todo.SprkEffortscore);
        deserialized.SprkTodocolumn.Should().Be(todo.SprkTodocolumn);
        deserialized.SprkTodopinned.Should().Be(todo.SprkTodopinned);
        deserialized.Statecode.Should().Be(todo.Statecode);
        deserialized.Statuscode.Should().Be(todo.Statuscode);
        deserialized.SprkRegardingprojectValue.Should().Be(todo.SprkRegardingprojectValue);
        deserialized.SprkRegardingrecordid.Should().Be(todo.SprkRegardingrecordid);
        deserialized.SprkRegardingrecordname.Should().Be(todo.SprkRegardingrecordname);
        deserialized.SprkRegardingrecordurl.Should().Be(todo.SprkRegardingrecordurl);
    }

    [Fact]
    public void ExternalTodoDto_DoesNotExposePolymorphicRegardingLookup()
    {
        // ADR-024: the DTO MUST NOT carry a polymorphic regardingobjectid — the resolver
        // fields + specific lookup are the only exposed regarding shape.
        var props = typeof(ExternalTodoDto).GetProperties()
            .Select(p => p.Name)
            .ToList();

        props.Should().NotContain(p =>
            p.Equals("Regardingobjectid", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("RegardingObjectId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExternalTodoDto_DoesNotExposeLegacyTodoflag()
    {
        // FR-29: zero sprk_todoflag references remain in the External Access surface.
        var props = typeof(ExternalTodoDto).GetProperties().Select(p => p.Name).ToList();
        props.Should().NotContain(p =>
            p.Contains("Todoflag", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExternalTodoDto_AllResolverFieldsPresent()
    {
        // ADR-024 contract: four resolver fields exposed.
        var props = typeof(ExternalTodoDto).GetProperties().Select(p => p.Name).ToList();
        props.Should().Contain("SprkRegardingrecordid");
        props.Should().Contain("SprkRegardingrecordname");
        props.Should().Contain("SprkRegardingrecordurl");
        // sprk_regardingrecordtype is the type lookup; it's exposed via _sprk_regardingproject_value
        // for project-scoped surface (single lookup) plus the resolver id/name/url.
        // The complete record-type lookup (sprk_recordtype_ref) is NOT exposed to external
        // callers — they query/write to the project-specific lookup only.
        props.Should().Contain("SprkRegardingprojectValue");
    }

    // =========================================================================
    // CreateExternalTodoRequest — JSON contract
    // =========================================================================

    [Fact]
    public void CreateExternalTodoRequest_RoundTripsThroughJson()
    {
        var request = new CreateExternalTodoRequest
        {
            SprkName = "Draft response brief",
            SprkNotes = "Reference exhibits 4-7",
            SprkDuedate = "2026-07-20",
            SprkPriorityscore = 70,
            SprkEffortscore = 40,
            SprkTodocolumn = 100000001, // Tomorrow
            SprkTodopinned = false,
        };

        var json = JsonSerializer.Serialize(request);
        var deserialized = JsonSerializer.Deserialize<CreateExternalTodoRequest>(json);

        json.Should().Contain("\"sprk_name\":");
        json.Should().Contain("\"sprk_notes\":");
        json.Should().Contain("\"sprk_duedate\":");
        json.Should().Contain("\"sprk_priorityscore\":");
        json.Should().Contain("\"sprk_effortscore\":");
        json.Should().Contain("\"sprk_todocolumn\":");
        json.Should().Contain("\"sprk_todopinned\":");
        json.Should().NotContain("sprk_todoflag");

        deserialized.Should().NotBeNull();
        deserialized!.SprkName.Should().Be(request.SprkName);
        deserialized.SprkNotes.Should().Be(request.SprkNotes);
        deserialized.SprkDuedate.Should().Be(request.SprkDuedate);
        deserialized.SprkPriorityscore.Should().Be(request.SprkPriorityscore);
        deserialized.SprkEffortscore.Should().Be(request.SprkEffortscore);
        deserialized.SprkTodocolumn.Should().Be(request.SprkTodocolumn);
        deserialized.SprkTodopinned.Should().Be(request.SprkTodopinned);
    }

    [Fact]
    public void CreateExternalTodoRequest_NameOnly_IsMinimalValidShape()
    {
        // The handler validates SprkName as required; other fields are optional.
        var request = new CreateExternalTodoRequest { SprkName = "Quick task" };

        var json = JsonSerializer.Serialize(request);
        json.Should().Contain("\"sprk_name\":\"Quick task\"");
        json.Should().NotContain("sprk_todoflag",
            "FR-29: zero sprk_todoflag references in the External Access surface");
    }

    [Fact]
    public void CreateExternalTodoRequest_DoesNotExposeRegardingShape()
    {
        // Project context comes from the route ({id}), not from the request body —
        // prevents external callers from regarding-ing a to-do to an arbitrary project.
        var props = typeof(CreateExternalTodoRequest).GetProperties().Select(p => p.Name).ToList();
        props.Should().NotContain(p => p.StartsWith("Sprk_regarding", StringComparison.OrdinalIgnoreCase));
        props.Should().NotContain(p => p.Equals("SprkRegardingprojectValue", StringComparison.OrdinalIgnoreCase));
    }

    // =========================================================================
    // UpdateExternalTodoRequest — JSON contract
    // =========================================================================

    [Fact]
    public void UpdateExternalTodoRequest_RoundTripsThroughJson()
    {
        var request = new UpdateExternalTodoRequest
        {
            SprkName = "Updated name",
            Statuscode = 659490001, // In Progress
        };

        var json = JsonSerializer.Serialize(request);
        var deserialized = JsonSerializer.Deserialize<UpdateExternalTodoRequest>(json);

        json.Should().Contain("\"sprk_name\":");
        json.Should().Contain("\"statuscode\":");
        json.Should().NotContain("sprk_todoflag");

        deserialized.Should().NotBeNull();
        deserialized!.SprkName.Should().Be(request.SprkName);
        deserialized.Statuscode.Should().Be(request.Statuscode);
    }

    [Fact]
    public void UpdateExternalTodoRequest_AllFieldsNullable()
    {
        // PATCH semantics: every field is optional; only provided fields are updated.
        var empty = new UpdateExternalTodoRequest();

        empty.SprkName.Should().BeNull();
        empty.SprkNotes.Should().BeNull();
        empty.SprkDuedate.Should().BeNull();
        empty.SprkPriorityscore.Should().BeNull();
        empty.SprkEffortscore.Should().BeNull();
        empty.SprkTodocolumn.Should().BeNull();
        empty.SprkTodopinned.Should().BeNull();
        empty.Statuscode.Should().BeNull();
    }

    [Theory]
    [InlineData(1, "Open")]
    [InlineData(659490001, "In Progress")]
    [InlineData(2, "Completed")]
    [InlineData(659490002, "Dismissed")]
    public void UpdateExternalTodoRequest_Statuscode_AcceptsAllFourValues(int statuscode, string label)
    {
        // Per entity-schema.md, sprk_todo has exactly four statuscode values mapped to Graph
        // todoTask.status. These tests document the contract; the handler does no further
        // validation — Dataverse rejects unknown statuscode values.
        var request = new UpdateExternalTodoRequest { Statuscode = statuscode };
        request.Statuscode.Should().Be(statuscode, $"the {label} status value must be accepted");
    }
}

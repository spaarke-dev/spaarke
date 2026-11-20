using FluentAssertions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Regression;

/// <summary>
/// Issue #2892: the external event create bound the event type as <c>sprk_eventtype_ref@odata.bind</c>. An <c>@odata.bind</c> key
/// is the navigation property name and is case-sensitive; on <c>sprk_event</c> it is <c>sprk_EventType_Ref</c> (verified against
/// relationship metadata in 9b32c1c8ab; the internal create in <c>DataverseWebApiService.CreateEventAsync</c> writes the same key).
/// Dataverse refused the lowercase key, so every typed create from the R4 Add event form answered 500 (owner E2E, 2026-11-17).
/// </summary>
public class Issue2892_ExternalEventTypeBindKeyTests
{
    private static readonly Guid Matter = Guid.Parse("28920000-0000-4000-8000-000000000001");
    private static readonly Guid OwningTeam = Guid.Parse("28920000-0000-4000-8000-0000000000aa");

    [Fact]
    public void BuildEventCreatePayload_WithAnEventTypeId_BindsTheCaseSensitiveNavigationProperty()
    {
        var request = new CreateExternalEventRequest { SprkName = "x", EventTypeId = EventTypeRef.Task };

        var payload = ExternalDataService.BuildEventCreatePayload(
            ExternalDataService.TodoRootKind.Matter, Matter, request, OwningTeam);

        payload.Should().ContainKey("sprk_EventType_Ref@odata.bind")
            .WhoseValue.Should().Be($"/sprk_eventtype_refs({EventTypeRef.Task})");
        payload.Keys.Should().NotContain(
            k => k.StartsWith("sprk_eventtype_ref", StringComparison.Ordinal),
            "Dataverse refuses the lowercase attribute name as a bind key");
    }

    [Fact]
    public void BuildEventCreatePayload_WithoutAType_WritesNoEventTypeBind()
    {
        // Must-not-fire pair: the legacy untyped body binds no type at all, in either case.
        var request = new CreateExternalEventRequest { SprkName = "x" };

        var payload = ExternalDataService.BuildEventCreatePayload(
            ExternalDataService.TodoRootKind.Matter, Matter, request, OwningTeam);

        payload.Keys.Should().NotContain(
            k => k.StartsWith("sprk_eventtype_ref", StringComparison.OrdinalIgnoreCase));
    }
}

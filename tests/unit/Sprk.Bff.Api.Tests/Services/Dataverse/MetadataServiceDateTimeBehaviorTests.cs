// spaarke-ontology-platform-r1 task 098 (2026-10-05) — the BFF metadata the shared DataGrid reads carries a DateTime
// column's BEHAVIOUR. sprk_event's six date columns are Behavior DateOnly: the Web API returns "yyyy-MM-dd", which the
// grid must render as that calendar day. Format = DateOnly alone cannot tell it so — a UserLocal column with Date Only
// format still stores an instant — and the grid treats a value as date-only only when the metadata says DateOnly.

using FluentAssertions;
using Microsoft.Xrm.Sdk.Metadata;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Dataverse;

[Trait("status", "task-098-ontology-r1")]
public class MetadataServiceDateTimeBehaviorTests
{
    [Theory]
    [InlineData("DateOnly")]
    [InlineData("UserLocal")]
    [InlineData("TimeZoneIndependent")]
    public void ProjectAttribute_DateTime_CarriesItsBehaviour(string behaviour)
    {
        var attr = new DateTimeAttributeMetadata(DateTimeFormat.DateOnly)
        {
            LogicalName = "sprk_duedate",
            DateTimeBehavior = behaviour, // implicit string conversion (as in Microsoft's own sample)
        };

        var dto = MetadataService.ProjectAttribute(attr);

        dto.Format.Should().Be("DateOnly");
        dto.DateTimeBehavior.Should().Be(behaviour);
    }

    [Fact]
    public void ProjectAttribute_NonDateTime_HasNoBehaviour()
    {
        MetadataService.ProjectAttribute(new StringAttributeMetadata { LogicalName = "sprk_eventname" })
            .DateTimeBehavior.Should().BeNull();
    }
}

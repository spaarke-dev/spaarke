// field-mapping-server-write-path-r1 task 072 — the entity's display name rides the cached metadata DTO as ONE label string
// (the Family endpoint's recordType: "Matter"). Beside MetadataServiceDateTimeBehaviorTests, which pins the attribute projection.

using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Dataverse;

public class MetadataServiceDisplayNameTests
{
    [Fact]
    public void ProjectToDto_CarriesTheUserLocalizedDisplayName()
    {
        var entity = new EntityMetadata
        {
            LogicalName = "sprk_matter",
            DisplayName = new Label(new LocalizedLabel("Matter", 1033), Array.Empty<LocalizedLabel>()),
        };

        MetadataService.ProjectToDto(entity).DisplayName.Should().Be("Matter");
    }

    [Fact]
    public void ProjectToDto_WithNoUserLocalizedLabel_FallsBackToTheFirstLocalizedLabel()
    {
        var entity = new EntityMetadata
        {
            LogicalName = "sprk_matter",
            DisplayName = new Label { LocalizedLabels = { new LocalizedLabel("Affaire", 1036), new LocalizedLabel("Matter", 1033) } },
        };

        MetadataService.ProjectToDto(entity).DisplayName.Should().Be("Affaire");
    }

    [Fact]
    public void ProjectToDto_WithNoDisplayName_IsNull()
    {
        MetadataService.ProjectToDto(new EntityMetadata { LogicalName = "sprk_matter" }).DisplayName.Should().BeNull();
        MetadataService.ProjectToDto(new EntityMetadata { LogicalName = "sprk_matter", DisplayName = new Label() }).DisplayName.Should().BeNull();
    }
}

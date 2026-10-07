// Regression test for GitHub #1038 (found by spaarkeai-word-add-in-r1 task 080's code review, 2026-09-30).
//
// POST /api/office/save accepts only FRIENDLY association types ("matter", "project", …), but
// OfficeService.ResolveContainerAsync passed that spelling straight to RecordContainerResolver, whose securable
// registry is keyed on LOGICAL names. "project" was therefore never securable: a save filed to a SECURE project
// skipped the secure branch and landed in the tenant-wide default container — irreversibly, because SPE
// permissions are additive-only. No test saw it because every Office test host reported nothing as securable.

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Tests.Api.Office;
using Sprk.Bff.Api.Tests.Shared.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Regression;

public class Issue1038_OfficeSaveSecureRecordContainerTests
{
    private const string DefaultContainer = "b!test-office-save-drive";
    private const string SecureProjectContainer = "b!secure-project-own-drive";

    [Fact]
    public async Task Save_FiledToASecureProjectByItsFriendlyTypeName_LandsInTheProjectsOwnContainer_NotTheDefault()
    {
        var world = new OfficeVersionSaveWorld();
        var secureProjectId = Guid.NewGuid();
        // Registered under the LOGICAL name, as the production registry holds it.
        world.SecureRecords[("sprk_project", secureProjectId)] = SecureProjectContainer;
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);

        var response = await factory.CreateClient().PostAsJsonAsync("/api/office/save", new SaveRequest
        {
            ContentType = SaveContentType.Document,
            // The ONLY spelling the endpoint accepts — "sprk_project" is rejected with OFFICE_006.
            TargetEntity = new SaveEntityReference { EntityType = "project", EntityId = secureProjectId },
            Document = new DocumentMetadata
            {
                FileName = "Privileged.docx",
                ContentBase64 = Convert.ToBase64String(MinimalDocx.Create("privileged")),
            },
        });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var stored = world.SpeItems.Values.Should().ContainSingle(i => i.Name == "Privileged.docx").Subject;
        stored.DriveId.Should().Be(SecureProjectContainer,
            "a secure record's content belongs in its own container; the shared default is readable far more widely");
        stored.DriveId.Should().NotBe(DefaultContainer);
    }
}

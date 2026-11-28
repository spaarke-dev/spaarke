using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Tests.Shared.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Office;

/// <summary>
/// spaarke-intake-pipeline-r1 FR-06b (task 011): <c>sprk_processingjob</c> now holds two kinds of record, an Office save
/// and an intake pipeline run (<c>sprk_jobtype</c> = Intake run). The Office job-status read must never return an intake
/// run, even to the user who started the request that the run belongs to.
/// </summary>
/// <remarks>
/// The two clones below are byte-identical copies of a row the real save wrote, so each carries the caller as its creator
/// and would pass <c>JobOwnershipFilter</c>. The only difference is <c>sprk_jobtype</c>. The Office clone is the control:
/// it proves the arrangement would serve the row, so the 404 for the intake clone is the job-type rule and nothing else.
/// </remarks>
public class OfficeJobStatusIntakeRunContractTests
{
    private static readonly byte[] Docx = MinimalDocx.Create("intake run isolation");

    [Fact]
    public async Task JobStatus_ForAnIntakeRunTheCallerCreated_ReturnsNotFound_WhileTheSameCallersOfficeJobIsServed()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();

        var saved = await client.PostAsJsonAsync("/api/office/save", OfficeVersionSaveWorld.NewDocumentSave("Brief.docx", Docx));
        saved.StatusCode.Should().Be(HttpStatusCode.Accepted, await saved.Content.ReadAsStringAsync());
        var saveResponse = (await saved.Content.ReadFromJsonAsync<SaveResponse>())!;

        var officeJobId = world.CloneJobRowAsAnotherProcessWouldSeeIt(saveResponse.JobId!.Value);
        var intakeRunId = world.CloneJobRowAsAnotherProcessWouldSeeIt(saveResponse.JobId!.Value);
        world.JobRows[intakeRunId]["JobType"] = (int)ProcessingJobType.IntakeRun;

        var office = await client.GetAsync($"/api/office/jobs/{officeJobId}");
        var intake = await client.GetAsync($"/api/office/jobs/{intakeRunId}");

        office.StatusCode.Should().Be(HttpStatusCode.OK, "the control: this caller owns the cloned Office job");
        intake.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "an intake run is not an Office job, whoever created it (FR-06b)");
    }
}

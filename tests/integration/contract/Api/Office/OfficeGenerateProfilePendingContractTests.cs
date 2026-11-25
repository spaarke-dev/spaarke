using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Office;

/// <summary>
/// <c>POST /api/office/documents/{documentId}/generate-profile</c> records <c>sprk_filesummarystatus</c> = Pending BEFORE
/// it answers 202 (spaarkeai-word-add-in-r1 task 137, #1090).
/// </summary>
/// <remarks>
/// <para>
/// <b>The defect.</b> The pane re-reads the document as soon as it has the 202. The queued job writes its own status only
/// once it starts, so that first read returned the status from before the click (Failed, say) and the pane showed it as the
/// new result. The status now has one owner, the server: it reads Pending the moment the 202 is out.
/// </para>
/// <para>
/// <b>Fixture.</b> <see cref="OfficeGenerateProfilePendingWebAppFactory"/> is the Generate Profile fixture with the document
/// service replaced by one in-memory status column, so a test reads back what the route left there — the same read the pane
/// makes. Each status write records how many jobs were on the queue when it landed, which pins the ORDER (Pending first, then
/// the submit) and not only the end state.
/// </para>
/// </remarks>
public class OfficeGenerateProfilePendingContractTests : IClassFixture<OfficeGenerateProfilePendingWebAppFactory>
{
    private static readonly Guid DocumentId = Guid.Parse("3f6d9a2c-1b4e-4c7a-9d5f-8a2b1c3d4e5f");
    private static readonly string Route = $"/api/office/documents/{DocumentId}/generate-profile";

    private const int Pending = 100000001;
    private const int None = 100000000;
    private const int Failed = 100000004;

    private readonly OfficeGenerateProfilePendingWebAppFactory _factory;

    public OfficeGenerateProfilePendingContractTests(OfficeGenerateProfilePendingWebAppFactory factory)
    {
        _factory = factory;
        _factory.Reset();
        _factory.ResetStatus();
    }

    private HttpClient AuthorizedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-caller-token");
        return client;
    }

    [Fact]
    public async Task Post_GenerateProfile_OnAFailedDocument_LeavesItPending_SoTheReadStraightAfterThe202IsNotTheOldFailure()
    {
        _factory.GrantWrite(DocumentId);
        _factory.SetStatus(Failed);

        var response = await AuthorizedClient().PostAsync(Route, content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        _factory.Status.Should().Be(Pending, "the pane's first read after the 202 must not return the status from before the click");
        _factory.QueuedJobs.Should().ContainSingle();
    }

    [Fact]
    public async Task Post_GenerateProfile_WritesPendingExactlyOnce_AndBeforeTheJobIsSubmitted()
    {
        _factory.GrantWrite(DocumentId);
        _factory.SetStatus(Failed);

        await AuthorizedClient().PostAsync(Route, content: null);

        var write = _factory.StatusWrites.Should().ContainSingle().Subject;
        write.DocumentId.Should().Be(DocumentId.ToString());
        write.Status.Should().Be(Pending);
        write.JobsQueuedAtTheTime.Should().Be(0,
            "Pending goes in before the submit: a job that fails fast writes its terminal status within a second, and a later Pending would overwrite it");
    }

    [Fact]
    public async Task Post_GenerateProfile_WhenThePendingWriteFails_StillQueuesTheJobAndAnswers202()
    {
        _factory.GrantWrite(DocumentId);
        _factory.SetStatus(Failed);
        _factory.FailStatusWrites();

        var response = await AuthorizedClient().PostAsync(Route, content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, "the profile is still wanted; the pane's cap ends its wait");
        _factory.QueuedJobs.Should().ContainSingle();
    }

    [Fact]
    public async Task Post_GenerateProfile_WhenTheJobQueueRefuses_PutsTheEarlierStatusBack_NotAStuckPending()
    {
        _factory.GrantWrite(DocumentId);
        _factory.SetStatus(Failed);
        _factory.RefuseNextSubmit();

        var response = await AuthorizedClient().PostAsync(Route, content: null);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        _factory.QueuedJobs.Should().BeEmpty();
        _factory.Status.Should().Be(Failed, "nothing was queued, so nothing would ever move the status on from Pending");
    }

    [Fact]
    public async Task Post_GenerateProfile_WhenTheJobQueueRefusesAnUnprofiledDocument_PutsItBackToNone()
    {
        _factory.GrantWrite(DocumentId);
        _factory.SetStatus(null);
        _factory.RefuseNextSubmit();

        var response = await AuthorizedClient().PostAsync(Route, content: null);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        _factory.Status.Should().Be(None, "a column that was never set reads as None");
    }

    [Fact]
    public async Task Post_GenerateProfile_WhenTheCallerLacksWrite_TouchesNoStatus()
    {
        _factory.GrantReadOnly(DocumentId);
        _factory.SetStatus(Failed);

        var response = await AuthorizedClient().PostAsync(Route, content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _factory.StatusWrites.Should().BeEmpty("a refused request must not change the document");
        _factory.Status.Should().Be(Failed);
    }
}

/// <summary>
/// The Generate Profile fixture with <see cref="IDocumentDataverseService"/> replaced by one in-memory
/// <c>sprk_filesummarystatus</c> column for <c>DocumentId</c>.
/// </summary>
public sealed class OfficeGenerateProfilePendingWebAppFactory : OfficeGenerateProfileTestWebAppFactory
{
    private readonly List<StatusWrite> _writes = new();
    private bool _failWrites;

    /// <summary>The column's current value (the pane's <c>summaryStatus</c>).</summary>
    public int? Status { get; private set; }

    /// <summary>Every status write the route made, in order.</summary>
    public IReadOnlyList<StatusWrite> StatusWrites => _writes;

    public void SetStatus(int? status) => Status = status;

    /// <summary>The next status writes throw, as a throttled Dataverse does.</summary>
    public void FailStatusWrites() => _failWrites = true;

    public void ResetStatus()
    {
        _writes.Clear();
        _failWrites = false;
        Status = null;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            var documents = new Mock<IDocumentDataverseService>(MockBehavior.Loose);
            documents
                .Setup(d => d.GetDocumentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string id, CancellationToken _) =>
                    new DocumentEntity { Id = id, Name = "doc.docx", SummaryStatus = Status });
            documents
                .Setup(d => d.UpdateDocumentAsync(It.IsAny<string>(), It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>()))
                .Returns((string id, UpdateDocumentRequest request, CancellationToken _) =>
                {
                    if (_failWrites)
                    {
                        throw new InvalidOperationException("Dataverse is throttling.");
                    }

                    if (request.SummaryStatus is { } status)
                    {
                        _writes.Add(new StatusWrite(id, status, QueuedJobs.Count));
                        Status = status;
                    }

                    return Task.CompletedTask;
                });
            services.RemoveAll<IDocumentDataverseService>();
            services.AddSingleton(documents.Object);
        });
    }

    /// <summary>One status write: the document, the value, and how many jobs were queued when it landed.</summary>
    public sealed record StatusWrite(string DocumentId, int Status, int JobsQueuedAtTheTime);
}

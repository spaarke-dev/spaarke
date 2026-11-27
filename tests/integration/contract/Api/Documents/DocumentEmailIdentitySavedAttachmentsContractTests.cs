using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Tests.Services.Documents;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Documents;

/// <summary>
/// spaarkeai-word-add-in-r1 task 141: <c>POST /api/documents/resolve-email-identity</c> also answers
/// <c>savedAttachmentNames</c> — the names of the saved <c>.eml</c>'s children the CALLER may read — so the pane can mark
/// each attachment saved or not.
/// </summary>
/// <remarks>
/// The full pipeline (routing, the resolution filter, <c>DocumentAuthorizationFilter("read")</c> on the <c>.eml</c>,
/// authentication, JSON) with module-boundary doubles: the Dataverse document service (the child read) and the access data
/// source (each child's check, ADR-008 A1). Reuses <see cref="DocumentIdentityTestWebAppFactory"/> as-is (CLAUDE.md §11).
/// </remarks>
[Trait("status", "task-141-word-add-in-r1")]
public class DocumentEmailIdentitySavedAttachmentsContractTests
{
    private const string Route = "/api/documents/resolve-email-identity";
    private const string MessageId = "<CAF0-141-identity@mail.example.com>";

    [Fact]
    public async Task ResolveEmail_ListsTheNamesOfTheSavedChildren_TheCallerMayRead()
    {
        var emlId = Guid.NewGuid();
        var readable = Child("Settlement terms.pdf", fileName: "Settlement terms.pdf");
        var renamed = Child("Exhibit A", fileName: "Exhibit A.pdf");
        var rights = new Dictionary<Guid, AccessRights>
        {
            [emlId] = AccessRights.Read,
            [Guid.Parse(readable.Id)] = AccessRights.Read,
            [Guid.Parse(renamed.Id)] = AccessRights.Read | AccessRights.Write,
        };

        using var host = Host(emlId, rights, children: new[] { readable, renamed });
        var body = await Resolve(host);

        body.GetProperty("resolved").GetBoolean().Should().BeTrue();
        Names(body).Should().BeEquivalentTo(new[] { "Settlement terms.pdf", "Exhibit A", "Exhibit A.pdf" },
            "a child's display name and file name both count, by the planner's one name rule");
    }

    [Fact]
    public async Task ResolveEmail_LeavesOutAChildTheCallerMayNotRead()
    {
        var emlId = Guid.NewGuid();
        var readable = Child("Settlement terms.pdf");
        var hidden = Child("Privileged memo.pdf");
        var rights = new Dictionary<Guid, AccessRights>
        {
            [emlId] = AccessRights.Read,
            [Guid.Parse(readable.Id)] = AccessRights.Read,
            [Guid.Parse(hidden.Id)] = AccessRights.None,
        };

        using var host = Host(emlId, rights, children: new[] { readable, hidden });
        var response = await Client(host).PostAsJsonAsync(Route, new { internetMessageId = MessageId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContain("Privileged memo", "a child the caller may not read is not named to them");
        Names(JsonDocument.Parse(raw).RootElement).Should().Equal("Settlement terms.pdf");
    }

    [Fact]
    public async Task ResolveEmail_WithNoSavedChildren_AnswersAnEmptyList()
    {
        var emlId = Guid.NewGuid();

        using var host = Host(emlId, new() { [emlId] = AccessRights.Read }, children: Array.Empty<DocumentEntity>());
        var body = await Resolve(host);

        body.TryGetProperty("savedAttachmentNames", out var names).Should().BeTrue("an empty list means \"none saved\"");
        names.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task ResolveEmail_WhenTheChildReadFails_OmitsTheField_AndStillAnswersTheIdentity()
    {
        var emlId = Guid.NewGuid();

        using var host = Host(emlId, new() { [emlId] = AccessRights.Read }, children: null);
        var body = await Resolve(host);

        body.GetProperty("resolved").GetBoolean().Should().BeTrue("the identity never fails on the attachments read");
        body.GetProperty("documentId").GetString().Should().Be(emlId.ToString("D"));
        body.TryGetProperty("savedAttachmentNames", out _).Should().BeFalse("nothing can be said, so the pane behaves as before");
    }

    [Fact]
    public async Task ResolveEmail_WhenAChildsCheckFaults_OmitsTheWholeField()
    {
        // The access source throws; the real AuthorizationService answers that as a deny with the system-failure reason code
        // (it does not throw), which is the shape the handler sees in production.
        var emlId = Guid.NewGuid();
        var readable = Child("Settlement terms.pdf");
        var faulting = Child("Faulting check.pdf");
        var rights = new Dictionary<Guid, AccessRights>
        {
            [emlId] = AccessRights.Read,
            [Guid.Parse(readable.Id)] = AccessRights.Read,
        };

        using var host = Host(emlId, rights, children: new[] { readable, faulting }, faultFor: Guid.Parse(faulting.Id));
        var response = await Client(host).PostAsJsonAsync(Route, new { internetMessageId = MessageId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContain("Faulting check", "the check fails closed");
        var body = JsonDocument.Parse(raw).RootElement;
        body.GetProperty("resolved").GetBoolean().Should().BeTrue("the identity never fails on the attachments check");
        body.TryGetProperty("savedAttachmentNames", out _).Should().BeFalse(
            "a check that did not decide cannot say the child is unsaved, so nothing is said");
    }

    [Fact]
    public void SavedNames_AreTheNamesTheAddPlannerTreatsAsAlreadySaved()
    {
        var child = Child("Exhibit A", fileName: "exhibit a.PDF");

        EmailAttachmentAddPlanner.SavedNamesOf(child).Should().Equal(new[] { "Exhibit A", "exhibit a.PDF" });
        foreach (var name in new[] { "EXHIBIT A", "Exhibit A.pdf" })
        {
            EmailAttachmentAddPlanner.IsAlreadySaved(child, name).Should().BeTrue(
                "the pane's saved mark and the server's skip must agree, case-insensitively");
            EmailAttachmentAddPlanner.SavedNamesOf(child).Should().Contain(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        }

        EmailAttachmentAddPlanner.IsAlreadySaved(child, "Exhibit B.pdf").Should().BeFalse();
        EmailAttachmentAddPlanner.SavedNamesOf(Child("", fileName: null)).Should().BeEmpty("a blank name never matches");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────────────────────

    private static DocumentEntity Child(string name, string? fileName = null) => new()
    {
        Id = Guid.NewGuid().ToString("D"),
        Name = name,
        FileName = fileName,
    };

    /// <param name="children">The children read's answer; <see langword="null"/> = the read throws.</param>
    /// <param name="faultFor">A child whose access-source read throws (the authorization service answers a system-failure deny).</param>
    private static WebApplicationFactory<Program> Host(
        Guid emlId, Dictionary<Guid, AccessRights> rights, DocumentEntity[]? children, Guid? faultFor = null)
    {
        var entityService = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entityService
            .Setup(e => e.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_document"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity> { DocumentEmailIdentityResolutionTests.Row(emlId) }));

        var accessSource = new Mock<IAccessDataSource>();
        accessSource
            .Setup(a => a.GetUserAccessAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns((string user, string resourceId, string? _, CancellationToken _) =>
            {
                var id = Guid.Parse(resourceId);
                if (id == faultFor)
                    throw new InvalidOperationException("access check fault");
                return Task.FromResult(new AccessSnapshot
                {
                    UserId = user,
                    ResourceId = resourceId,
                    AccessRights = rights.TryGetValue(id, out var r) ? r : AccessRights.None,
                });
            });

        var dataverse = new Mock<IDataverseService>();
        var read = dataverse.Setup(d => d.GetDocumentsByParentAsync(emlId, It.IsAny<CancellationToken>()));
        if (children is null)
            read.ThrowsAsync(new HttpRequestException("Dataverse unavailable"));
        else
            read.ReturnsAsync(children);

        var factory = new DocumentIdentityTestWebAppFactory(
            // Placeholder: this route never reaches SharePoint Embedded.
            DocumentUrlIdentityResolutionTests.Spe(DocumentUrlIdentityResolutionTests.ResolvedFor("unused-drive", "unused-item")),
            entityService,
            accessSource);
        return factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(dataverse.Object);
        }));
    }

    private static HttpClient Client(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        return client;
    }

    private static async Task<JsonElement> Resolve(WebApplicationFactory<Program> host)
    {
        var response = await Client(host).PostAsJsonAsync(Route, new { internetMessageId = MessageId });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static List<string> Names(JsonElement body) =>
        body.GetProperty("savedAttachmentNames").EnumerateArray().Select(e => e.GetString()!).ToList();
}

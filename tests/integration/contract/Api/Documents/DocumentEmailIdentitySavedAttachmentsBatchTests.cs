using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
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
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Identity;
using Sprk.Bff.Api.Tests.Services.Documents;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Documents;

/// <summary>
/// spaarkeai-word-add-in-r1 task 141 review (F6): <c>resolve-email-identity</c> checks the saved <c>.eml</c>'s children in ONE
/// <c>AuthorizeDocumentsAsync</c> call first (it allows only when every child is allowed), and one child at a time only when
/// that call denies, so a child the caller may not read is still left out.
/// </summary>
/// <remarks>
/// The same full pipeline and doubles as <c>DocumentEmailIdentitySavedAttachmentsContractTests</c>, plus counting doubles of the
/// caller's internal/external lookup (<see cref="ISystemUserIdentityResolver.IsExternalAsync"/>), which every
/// <c>AuthorizeDocumentsAsync</c> call that allows makes once: the count is the number of those calls.
/// </remarks>
[Trait("status", "task-141-word-add-in-r1")]
public class DocumentEmailIdentitySavedAttachmentsBatchTests
{
    private const string Route = "/api/documents/resolve-email-identity";
    private const string MessageId = "<CAF0-141-batch@mail.example.com>";

    [Fact]
    public async Task EveryChildReadable_IsCheckedInOneCall_HoweverManyChildrenThereAre()
    {
        var oneChild = await Run(childCount: 1);
        var fiveChildren = await Run(childCount: 5);

        fiveChildren.Names.Should().HaveCount(5);
        fiveChildren.ExternalLookups.Should().Be(oneChild.ExternalLookups,
            "the children are checked in one call, not one call per child");
    }

    [Fact]
    public async Task AChildTheCallerMayNotRead_FallsBackToOneCheckPerChild_AndIsLeftOut()
    {
        var result = await Run(childCount: 3, deniedChild: 1);

        result.Names.Should().Equal("Attachment 0.pdf", "Attachment 2.pdf");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record Outcome(List<string> Names, int ExternalLookups);

    private static async Task<Outcome> Run(int childCount, int? deniedChild = null)
    {
        var emlId = Guid.NewGuid();
        var children = Enumerable.Range(0, childCount)
            .Select(i => new DocumentEntity { Id = Guid.NewGuid().ToString("D"), Name = $"Attachment {i}.pdf" })
            .ToArray();
        var rights = new Dictionary<Guid, AccessRights> { [emlId] = AccessRights.Read };
        for (var i = 0; i < children.Length; i++)
            rights[Guid.Parse(children[i].Id)] = i == deniedChild ? AccessRights.None : AccessRights.Read;

        var externalLookups = 0;
        var identity = new Mock<ISystemUserIdentityResolver>();
        identity
            .Setup(r => r.IsExternalAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback(() => Interlocked.Increment(ref externalLookups))
            .ReturnsAsync(false);
        var caller = new Mock<ICallerSystemUserResolver>();
        caller
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Resolved(Guid.Parse("14114114-0141-4141-8141-0000000005e1").ToString("D")));

        using var host = Host(emlId, rights, children, services =>
        {
            services.RemoveAll<ISystemUserIdentityResolver>();
            services.AddSingleton(identity.Object);
            services.RemoveAll<ICallerSystemUserResolver>();
            services.AddSingleton(caller.Object);
        });
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        var response = await client.PostAsJsonAsync(Route, new { internetMessageId = MessageId });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var names = body.GetProperty("savedAttachmentNames").EnumerateArray().Select(e => e.GetString()!).ToList();
        return new Outcome(names, externalLookups);
    }

    private static WebApplicationFactory<Program> Host(
        Guid emlId, Dictionary<Guid, AccessRights> rights, DocumentEntity[] children, Action<IServiceCollection> configure)
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
            .Returns((string user, string resourceId, string? _, CancellationToken _) => Task.FromResult(new AccessSnapshot
            {
                UserId = user,
                ResourceId = resourceId,
                AccessRights = rights.TryGetValue(Guid.Parse(resourceId), out var r) ? r : AccessRights.None,
            }));

        var dataverse = new Mock<IDataverseService>();
        dataverse.Setup(d => d.GetDocumentsByParentAsync(emlId, It.IsAny<CancellationToken>())).ReturnsAsync(children);

        var factory = new DocumentIdentityTestWebAppFactory(
            // Placeholder: this route never reaches SharePoint Embedded.
            DocumentUrlIdentityResolutionTests.Spe(DocumentUrlIdentityResolutionTests.ResolvedFor("unused-drive", "unused-item")),
            entityService,
            accessSource);
        return factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(dataverse.Object);
            configure(services);
        }));
    }
}

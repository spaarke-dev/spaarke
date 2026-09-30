using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using FluentAssertions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;
using EntityReference = Microsoft.Xrm.Sdk.EntityReference;

namespace Sprk.Bff.Api.Tests.Api.Office;

/// <summary>
/// spaarkeai-word-add-in-r1 task 064 — the per-resource authorization contract for
/// <c>POST /api/office/todo</c> (Fable review finding <b>F3</b>).
/// </summary>
/// <remarks>
/// <para><b>What was wrong.</b> The route accepted FOUR caller-supplied record ids —
/// <see cref="CreateTodoRequest.RegardingRecordId"/>, <see cref="CreateTodoRequest.DocumentId"/>,
/// <see cref="CreateTodoRequest.CommunicationId"/>, <see cref="CreateTodoRequest.AssignedToContactId"/> —
/// and wrote every one of them onto a caller-owned <c>sprk_todo</c> app-only, with no check that the caller
/// could read any of them. Its 403-vs-201 split (a child-class regarding whose core-ancestor read finds no
/// row fails closed to 403; one that finds a row returns 201) additionally confirmed whether an arbitrary
/// GUID existed.</para>
///
/// <para><b>Two properties are asserted here, and they are not the same property.</b> (1) the WRITE gap —
/// an unreadable source id must not reach the handler; (2) the ORACLE — a refusal for a record that does
/// not exist and a refusal for a record the caller may not read must be indistinguishable. Closing (1)
/// while leaving (2) open only half-fixes the finding.</para>
///
/// <para><b>Doubles are module boundaries only</b> (ADR-038 §4): <see cref="CallerRecordAccessProbe"/>
/// (its <c>virtual</c> method is the sanctioned seam — it stands in for Dataverse
/// <c>RetrievePrincipalAccess</c>, modelling its deny-by-default posture) and <see cref="IDataverseService"/>
/// (the app-only write/read). Everything between is shipped code: the real route, the real filter chain,
/// the real <c>OfficeService</c>, the real <see cref="CoreAncestorResolver"/>. No
/// <c>Mock&lt;HttpMessageHandler&gt;</c>, no DI-registration assertion, no ctor null-check (ADR-038 bans
/// B1/B16/B17).</para>
///
/// <para>A SEPARATE factory from the sibling <c>OfficeTodoRegardingContractTests</c> per the same boundary
/// that file records: it subclasses the shared, public, override-able <see cref="OfficeTestWebAppFactory"/>
/// rather than modifying it.</para>
/// </remarks>
public class OfficeTodoSourceAuthorizationContractTests
{
    private static readonly Guid MatterId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DocumentId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid CommunicationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ContactId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    // ---------------------------------------------------------------------------------------------
    // 1-4. The write gap: EVERY ONE of the four caller-supplied ids is gated. Three of four is a gap.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Post_Todo_WhenCallerCannotReadTheDocumentCarrier_IsRefused_AndCreatesNoRow()
    {
        // Arrange — Word's shape: a readable Matter, but a sprk_document the caller has no rights on.
        // This is finding F3's headline case: a To Do hung off a document the caller cannot read, which
        // FR-26 inheritance then surfaces to that document's members.
        using var factory = new TodoSourceAccessTestWebAppFactory();
        factory.GrantRead("sprk_matters", MatterId);
        // sprk_documents(DocumentId) deliberately NOT granted.
        using var client = factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/office/todo", new CreateTodoRequest
        {
            Name = "Review the sealed NDA",
            RegardingEntityType = "Matter",
            RegardingRecordId = MatterId,
            DocumentId = DocumentId,
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a To Do must not be written against a document the caller cannot read");
        factory.CreatedEntities.Should().BeEmpty("the refusal happens before the handler, so no row is written");
    }

    [Fact]
    public async Task Post_Todo_WhenCallerCannotReadTheCommunicationCarrier_IsRefused_AndCreatesNoRow()
    {
        // Arrange — Outlook's shape: the communication carrier is the counterpart of the document carrier
        // and was added by the same task (035); gating one and not the other would be the gap.
        using var factory = new TodoSourceAccessTestWebAppFactory();
        factory.GrantRead("sprk_matters", MatterId);
        using var client = factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/office/todo", new CreateTodoRequest
        {
            Name = "Follow up with counsel",
            RegardingEntityType = "Matter",
            RegardingRecordId = MatterId,
            CommunicationId = CommunicationId,
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        factory.CreatedEntities.Should().BeEmpty();
    }

    [Fact]
    public async Task Post_Todo_WhenCallerCannotReadTheRegardingRecord_IsRefused_AndCreatesNoRow()
    {
        // Arrange — the regarding record is the id the core-ancestor stamp is derived from, so it is also
        // the id whose parent matter/project would otherwise be copied onto a row the caller owns.
        using var factory = new TodoSourceAccessTestWebAppFactory();
        // Nothing granted at all.
        using var client = factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/office/todo", new CreateTodoRequest
        {
            Name = "Should never be created",
            RegardingEntityType = "Matter",
            RegardingRecordId = MatterId,
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        factory.CreatedEntities.Should().BeEmpty();
    }

    [Fact]
    public async Task Post_Todo_WhenCallerCannotReadTheAssigneeContact_IsRefused_AndCreatesNoRow()
    {
        // Arrange — the fourth id. Easiest one to forget, because it is an assignment rather than a
        // regarding; it is still a caller-supplied record id written onto the row.
        using var factory = new TodoSourceAccessTestWebAppFactory();
        factory.GrantRead("sprk_matters", MatterId);
        // contacts(ContactId) deliberately NOT granted.
        using var client = factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/office/todo", new CreateTodoRequest
        {
            Name = "Assign to someone I cannot see",
            RegardingEntityType = "Matter",
            RegardingRecordId = MatterId,
            AssignedToContactId = ContactId,
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        factory.CreatedEntities.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------------------------
    // 5. THE ORACLE. Denied and not-found must be indistinguishable.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Post_Todo_DeniedRecordAndNonExistentRecord_ProduceIndistinguishableResponses()
    {
        // Arrange — the two requests differ in EXACTLY ONE respect: whether the invoice row exists.
        // Invoice is CHILD-class (CoreAncestorResolver.ChildRecordEntities), so before the gate the
        // existing row produced a successful stamp (201 Created) and the absent row produced a failed
        // stamp (403) — which is the record-existence oracle, stated as a test.
        var existingInvoiceId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var absentInvoiceId = Guid.Parse("66666666-6666-6666-6666-666666666666");

        using var factory = new TodoSourceAccessTestWebAppFactory();
        factory.RowExists("sprk_invoice", existingInvoiceId); // app-only-readable; caller granted NOTHING
        using var client = factory.CreateClient();

        // Act
        var deniedResponse = await client.PostAsJsonAsync("/api/office/todo", new CreateTodoRequest
        {
            Name = "Probe",
            RegardingEntityType = "Invoice",
            RegardingRecordId = existingInvoiceId,
        });
        var notFoundResponse = await client.PostAsJsonAsync("/api/office/todo", new CreateTodoRequest
        {
            Name = "Probe",
            RegardingEntityType = "Invoice",
            RegardingRecordId = absentInvoiceId,
        });

        // Assert
        deniedResponse.StatusCode.Should().Be(
            notFoundResponse.StatusCode,
            "a caller must not learn from the status code whether the record exists");

        var deniedBody = await NormalizeAsync(deniedResponse);
        var notFoundBody = await NormalizeAsync(notFoundResponse);
        deniedBody.Should().Be(
            notFoundBody,
            "a caller must not learn from the response body whether the record exists "
            + "(correlationId is normalized out — it varies per request by design and is not record-derived)");

        factory.CreatedEntities.Should().BeEmpty("neither request may write a row");
    }

    // ---------------------------------------------------------------------------------------------
    // 6-7. The happy path is unchanged.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Post_Todo_WhenEverySourceIsReadable_StillCreatesTheTodo_WithResolverFieldsStamped()
    {
        // Arrange — the Word pane's real request shape, with the caller holding Read on all four ids.
        using var factory = new TodoSourceAccessTestWebAppFactory();
        factory.GrantRead("sprk_matters", MatterId);
        factory.GrantRead("sprk_documents", DocumentId);
        factory.GrantRead("contacts", ContactId);
        using var client = factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/office/todo", new CreateTodoRequest
        {
            Name = "Review NDA red-lines",
            RegardingEntityType = "Matter",
            RegardingRecordId = MatterId,
            RegardingRecordName = "Acme Corp — NDA",
            DocumentId = DocumentId,
            AssignedToContactId = ContactId,
        });

        // Assert — 201, and the ADR-024 resolver fields + FR-26 core-ancestor stamp still written.
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        factory.CreatedEntities.Should().HaveCount(1);
        var entity = factory.CreatedEntities[0];
        entity.GetAttributeValue<EntityReference>("sprk_regardingmatter")!.Id.Should().Be(MatterId);
        entity.GetAttributeValue<EntityReference>("sprk_regardingdocument")!.Id.Should().Be(DocumentId);
        entity.GetAttributeValue<EntityReference>("sprk_assignedto")!.Id.Should().Be(ContactId);
        entity.GetAttributeValue<string>("sprk_regardingrecordid").Should().Be(MatterId.ToString());
        entity.GetAttributeValue<string>("sprk_regardingrecordname").Should().Be("Acme Corp — NDA");
    }

    [Fact]
    public async Task Post_Todo_WithNoSourceIdsAtAll_StillCreates_AndProbesNothing()
    {
        // Arrange — a standalone To Do names no record, so the service reads nothing and there is nothing
        // to authorize. The gate must not turn "no source" into a refusal (the same fail-open-where-
        // nothing-is-read posture QuickCreateSourceAccessFilter takes).
        using var factory = new TodoSourceAccessTestWebAppFactory();
        using var client = factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/office/todo", new CreateTodoRequest
        {
            Name = "Standalone reminder",
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        factory.Probed.Should().BeEmpty("no record was named, so no access question was asked");
    }

    // ---------------------------------------------------------------------------------------------
    // 8. All four, in one request — the "three of four is a gap" criterion, asserted directly.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Post_Todo_ProbesAllFourCallerSuppliedIds_NotASubset()
    {
        // Arrange
        using var factory = new TodoSourceAccessTestWebAppFactory();
        factory.GrantRead("sprk_invoices", MatterId); // reused id, invoice regarding
        factory.GrantRead("sprk_documents", DocumentId);
        factory.GrantRead("sprk_communications", CommunicationId);
        factory.GrantRead("contacts", ContactId);
        factory.RowExists("sprk_invoice", MatterId);
        using var client = factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/office/todo", new CreateTodoRequest
        {
            Name = "Everything at once",
            RegardingEntityType = "Invoice",
            RegardingRecordId = MatterId,
            DocumentId = DocumentId,
            CommunicationId = CommunicationId,
            AssignedToContactId = ContactId,
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        factory.Probed.Should().BeEquivalentTo(new[]
        {
            ("sprk_invoices", MatterId),
            ("sprk_documents", DocumentId),
            ("sprk_communications", CommunicationId),
            ("contacts", ContactId),
        }, "every caller-supplied record id on this route is gated — four of four");
    }

    /// <summary>
    /// The response body with the per-request correlation id blanked. Everything else — status, title,
    /// detail, errorCode, reasonCode — must match byte for byte between a denial and a not-found.
    /// </summary>
    private static async Task<string> NormalizeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return Regex.Replace(body, "\"correlationId\"\\s*:\\s*\"[^\"]*\"", "\"correlationId\":\"<normalized>\"");
    }
}

/// <summary>
/// Layers a grant-table <see cref="CallerRecordAccessProbe"/> and an entity-capturing
/// <see cref="IDataverseService"/> over the shared <see cref="OfficeTestWebAppFactory"/>.
/// </summary>
public sealed class TodoSourceAccessTestWebAppFactory : OfficeTestWebAppFactory
{
    private readonly HashSet<(string EntitySet, Guid Id)> _readable = new();
    private readonly HashSet<(string LogicalName, Guid Id)> _existingRows = new();

    /// <summary>Every <c>IGenericEntityService.CreateAsync</c> call, in order — the proof a row was (not) written.</summary>
    public List<Entity> CreatedEntities { get; } = new();

    /// <summary>Every access question the filter asked, in order.</summary>
    public List<(string EntitySet, Guid Id)> Probed { get; } = new();

    /// <summary>The caller holds Read on this record.</summary>
    public void GrantRead(string entitySet, Guid id) => _readable.Add((entitySet, id));

    /// <summary>The row exists as far as an APP-ONLY read is concerned (independent of caller rights).</summary>
    public void RowExists(string logicalName, Guid id) => _existingRows.Add((logicalName, id));

    /// <summary>Columns the in-memory metadata probe reports — covers every core-ancestor lookup these tests touch.</summary>
    private static readonly IReadOnlySet<string> ProbedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "sprk_regardingmatter",
        "sprk_regardingproject",
        "sprk_regardingworkassignment",
        "sprk_regardingservicerequest",
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            var dataverseMock = new Mock<IDataverseService>();
            dataverseMock.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            dataverseMock
                .Setup(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Entity entity, CancellationToken _) =>
                {
                    var id = Guid.NewGuid();
                    entity.Id = id;
                    CreatedEntities.Add(entity);
                    return id;
                });
            // The APP-ONLY core-ancestor read: a row that exists carries a matter stamp; one that does not
            // returns null, which CoreAncestorResolver fails closed on. That divergence IS the oracle the
            // gate has to render unobservable.
            dataverseMock
                .Setup(d => d.RetrieveAsync(
                    It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string logicalName, Guid id, string[] _, CancellationToken __) =>
                {
                    if (!_existingRows.Contains((logicalName, id)))
                    {
                        return null!;
                    }

                    var row = new Entity(logicalName, id);
                    row["sprk_regardingmatter"] = new EntityReference(
                        "sprk_matter", Guid.Parse("99999999-9999-9999-9999-999999999999"));
                    return row;
                });
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(dataverseMock.Object);

            services.RemoveAll<CoreAncestorResolver>();
            services.AddSingleton(sp => new CoreAncestorResolver(
                sp.GetRequiredService<IGenericEntityService>(),
                (string _, CancellationToken _) => Task.FromResult(ProbedColumns),
                sp.GetRequiredService<ILogger<CoreAncestorResolver>>()));

            // The access decision, modelled on Dataverse's own: deny by default, and "no such record" is
            // reported exactly the same way as "you may not see it" (see CallerRecordAccessProbe's remarks).
            var probe = new GrantTableProbe(_readable, Probed);
            services.RemoveAll<CallerRecordAccessProbe>();
            services.AddSingleton<CallerRecordAccessProbe>(probe);
        });
    }

    private sealed class GrantTableProbe : CallerRecordAccessProbe
    {
        private readonly HashSet<(string, Guid)> _readable;
        private readonly List<(string, Guid)> _probed;

        public GrantTableProbe(HashSet<(string, Guid)> readable, List<(string, Guid)> probed)
            : base(new HttpClient(),
                   new ConfigurationBuilder().Build(),
                   NullLogger<CallerRecordAccessProbe>.Instance)
        {
            _readable = readable;
            _probed = probed;
        }

        public override Task<Spaarke.Dataverse.AccessRights> GetCallerRightsAsync(
            string? callerBearerToken,
            string entitySet,
            Guid recordId,
            CancellationToken ct = default)
        {
            lock (_probed)
            {
                _probed.Add((entitySet, recordId));
            }

            return Task.FromResult(_readable.Contains((entitySet, recordId))
                ? Spaarke.Dataverse.AccessRights.Read
                : Spaarke.Dataverse.AccessRights.None);
        }
    }
}

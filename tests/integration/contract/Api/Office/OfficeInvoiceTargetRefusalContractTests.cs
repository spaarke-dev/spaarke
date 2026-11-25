using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Tests.Services.Compose;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Office;

/// <summary>
/// HTTP contract of ebilling-r1 task 096 (owner L, FR-17 / FR-20): an invoice is created and filed onto only by submission, so the
/// two Office server legs that touched one are refused with a stable 400 <c>OFFICE_023</c> BEFORE anything happens.
/// </summary>
/// <remarks>
/// <para><b>Two routes.</b> <c>POST /api/office/save</c> whose <c>TargetEntity</c> names an invoice (any casing, either spelling), and
/// <c>POST /api/office/quickcreate/{entityType}</c> whose type parses to Invoice (any casing, plus the numeric and comma-joined
/// spellings <c>Enum.TryParse</c> also accepts). Both answer ProblemDetails with <c>errorCode</c> and <c>correlationId</c> (ADR-019).</para>
/// <para><b>"Nothing happened" is asserted at every boundary the real pipeline reaches</b>, not inferred from the status code: the
/// Dataverse composite (job row, document create, every read), the caller-rights probe (the filter's read), the SPE upload facade and
/// the Service Bus sender (queued work) for the save; the generic entity service, the field-mapping service, the caller resolver, the
/// rights probe and the Create-privilege question for the quick-create. A 400 alone would pass if the write ran first.</para>
/// <para><b>Controls (must-not-fire).</b> A save to a matter still answers 202 and does touch the job row; a quick-create of a matter
/// still reaches the privilege question (403 when it is not held, not this refusal) — so the empty-boundary assertions are not vacuous.
/// Quick-create of Matter and Project is otherwise covered, unchanged, by <see cref="OfficeQuickCreateContractTests"/> and
/// <c>OfficeQuickCreateFieldsContractTests</c>.</para>
/// </remarks>
public class OfficeInvoiceTargetRefusalContractTests
{
    private const string RefusalCode = "OFFICE_023";

    // ── POST /api/office/save ─────────────────────────────────────────────────────────────────────────────

    public static TheoryData<string> InvoiceSpellings => new()
    {
        "invoice", "Invoice", "INVOICE", " invoice ", "sprk_invoice", "SPRK_Invoice",
    };

    [Theory]
    [MemberData(nameof(InvoiceSpellings))]
    public async Task Post_OfficeSave_WithAnInvoiceTarget_Returns400Office023_AndNothingIsReadOrWritten(string entityType)
    {
        using var factory = new InvoiceSaveRefusalWebAppFactory();

        var response = await factory.CreateClient().PostAsJsonAsync("/api/office/save", EmailSave(entityType));

        await AssertRefusedAsync(response);
        factory.Touches().Should().BeEmpty(
            "the refusal comes before the rights read, the job row, the email capture, the upload and the queue");
    }

    [Fact]
    public async Task Post_OfficeSave_WithAnInvoiceTarget_ForADocument_Returns400Office023_AndNothingIsReadOrWritten()
    {
        using var factory = new InvoiceSaveRefusalWebAppFactory();

        var response = await factory.CreateClient().PostAsJsonAsync("/api/office/save", new SaveRequest
        {
            ContentType = SaveContentType.Document,
            Document = new DocumentMetadata { FileName = "Agreement.docx", ContentBase64 = Convert.ToBase64String(new byte[] { 1, 2, 3 }) },
            TargetEntity = new SaveEntityReference { EntityType = "invoice", EntityId = Guid.NewGuid() },
        });

        await AssertRefusedAsync(response);
        factory.Touches().Should().BeEmpty();
    }

    [Theory]
    [InlineData("matter")]
    [InlineData("project")]
    [InlineData("workassignment")]
    public async Task Post_OfficeSave_MustNotFire_ToAnotherTarget_IsStillAccepted_AndTheJobRowIsWritten(string entityType)
    {
        using var factory = new InvoiceSaveRefusalWebAppFactory();

        var response = await factory.CreateClient().PostAsJsonAsync("/api/office/save", EmailSave(entityType));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, "only an invoice target is retired");
        factory.Touches().Should().Contain(t => t.StartsWith("dataverse:CreateProcessingJobAsync", StringComparison.Ordinal),
            "the control: the same harness records the job row of an accepted save, so the empty list above is not vacuous");
        factory.Probe.Invocations.Should().NotBeEmpty("the control: an accepted save asks the caller's rights first");
    }

    // ── POST /api/office/quickcreate/{entityType} ─────────────────────────────────────────────────────────

    /// <summary>Every spelling that parses to the Invoice enum value (<c>Enum.TryParse</c>, ignore case): the name, the number, a comma join.</summary>
    public static TheoryData<string> InvoiceRouteSpellings => new()
    {
        "invoice", "Invoice", "INVOICE", "2", "matter,invoice",
    };

    [Theory]
    [MemberData(nameof(InvoiceRouteSpellings))]
    public async Task Post_QuickCreate_OfAnInvoice_Returns400Office023_AndNothingIsReadOrWritten(string entityType)
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        var contactId = Guid.NewGuid();

        // The richest request the retired leg took: a name, a description, an Assigned To contact and a record context. The filter
        // would read the source and the contact, and ask the invoice Create privilege (held by default); none of it may happen.
        var response = await factory.CreateClient().PostAsJsonAsync(
            $"/api/office/quickcreate/{entityType}",
            new QuickCreateRequest
            {
                Name = "INV-2026-0100",
                Description = "Outside counsel, September",
                AssignedToContactId = contactId,
                SourceEntityType = "sprk_matter",
                SourceRecordId = Guid.NewGuid(),
            });

        await AssertRefusedAsync(response);
        factory.HeldPrivileges.Should().Contain("prvCreatesprk_Invoice", "the caller holds the privilege, so the refusal is not a rights answer");
        factory.PrivilegeQuestions.Should().BeEmpty("no Create-privilege question is asked for an invoice");
        factory.AccessProbe.Invocations.Should().BeEmpty("no source Read, no contact Read and no privilege query: refused before any read");
        factory.CallerResolver.Invocations.Should().BeEmpty("the handler was never reached");
        factory.Entities.Invocations.Should().BeEmpty("no Dataverse read or write through the generic entity service");
        factory.FieldMappings.Invocations.Should().BeEmpty("no creation-service write");
    }

    [Fact]
    public async Task Post_QuickCreate_OfAnInvoice_WithNoName_StillAnswersTheRefusal_NotAFieldValidation()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();

        var response = await factory.CreateClient().PostAsJsonAsync("/api/office/quickcreate/invoice", new QuickCreateRequest());

        await AssertRefusedAsync(response);
    }

    [Fact]
    public async Task Post_QuickCreate_MustNotFire_ForAMatter_StillAsksItsCreatePrivilege()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        factory.HeldPrivileges.Remove("prvCreatesprk_Matter");

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/office/quickcreate/matter", new QuickCreateRequest { Name = "No create privilege" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "a matter is refused by its own privilege, not by the invoice refusal");
        factory.PrivilegeQuestions.Should().Equal("prvCreatesprk_Matter");
    }

    [Fact]
    public void CreatePrivilegeFor_AnInvoice_AsksNoPrivilege_BecauseTheRouteNeverCreatesOne()
    {
        QuickCreateSourceAccessFilter.CreatePrivilegeFor(QuickCreateEntityType.Invoice).Should().BeNull();
        QuickCreateSourceAccessFilter.CreatePrivilegeFor(QuickCreateEntityType.Matter).Should().Be("prvCreatesprk_Matter");
        QuickCreateSourceAccessFilter.CreatePrivilegeFor(QuickCreateEntityType.Project).Should().Be("prvCreatesprk_Project");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────────────────

    private static SaveRequest EmailSave(string entityType) => new()
    {
        ContentType = SaveContentType.Email,
        Email = new EmailMetadata { Subject = "Invoice for September", SenderEmail = "billing@firm.test", SenderName = "Firm Billing" },
        TargetEntity = new SaveEntityReference { EntityType = entityType, EntityId = Guid.NewGuid() },
    };

    /// <summary>The ADR-019 shape of the refusal: 400, ProblemDetails, the stable code, a correlation id, and the way out.</summary>
    private static async Task AssertRefusedAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = problem.RootElement;
        root.GetProperty("errorCode").GetString().Should().Be(RefusalCode);
        root.GetProperty("correlationId").GetString().Should().NotBeNullOrWhiteSpace();
        root.GetProperty("status").GetInt32().Should().Be(400);
        root.GetProperty("type").GetString().Should().Be("https://spaarke.com/errors/office/office_023");
        root.GetProperty("detail").GetString().Should().Contain("Submit Invoice", "the answer says where to go");
    }

    /// <summary>
    /// The Office host with every boundary the save reaches substituted by a recording double. Layered over
    /// <see cref="OfficeTestWebAppFactory"/>, whose own doubles it replaces (registered later, so they win).
    /// </summary>
    private sealed class InvoiceSaveRefusalWebAppFactory : OfficeTestWebAppFactory
    {
        public Mock<IDataverseService> Dataverse { get; } = new(MockBehavior.Loose);

        /// <summary>The caller-rights probe <c>EntityAccessFilter</c> reads through (its designated virtual seam).</summary>
        public Mock<CallerRecordAccessProbe> Probe { get; } = ComposeServiceCollaborators.Probe();

        private readonly Mock<ServiceBusSender> _sender = new();
        private readonly Mock<SpeFileStore> _spe;

        public InvoiceSaveRefusalWebAppFactory()
        {
            var graph = Mock.Of<IGraphClientFactory>();
            _spe = new Mock<SpeFileStore>(
                MockBehavior.Loose,
                new ContainerOperations(graph, TestSpeOwnership.AllowAll(graph), Mock.Of<ILogger<ContainerOperations>>()),
                new DriveItemOperations(graph, TestSpeOwnership.AllowAll(graph), Mock.Of<ILogger<DriveItemOperations>>()),
                new UploadSessionManager(graph, TestSpeOwnership.AllowAll(graph), Mock.Of<IHttpClientFactory>(), Mock.Of<ILogger<UploadSessionManager>>()),
                new UserOperations(graph, Mock.Of<ILogger<UserOperations>>()),
                null!);

            // The same answers the shared host's Dataverse double gives, so an ACCEPTED save (the control) runs to its job row.
            Dataverse.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            Dataverse.Setup(d => d.CreateDocumentAsync(It.IsAny<CreateDocumentRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Guid.NewGuid().ToString());
            Dataverse.Setup(d => d.CreateProcessingJobAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Guid.NewGuid());
            Dataverse.Setup(d => d.RetrieveAsync(
                    It.IsAny<string>(), It.IsAny<Guid>(),
                    It.Is<string[]>(c => c.Contains("owningbusinessunit") && !c.Contains("sprk_issecure")),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((string entity, Guid id, string[] _, CancellationToken _) => new Microsoft.Xrm.Sdk.Entity(entity, id));
            var uploaded = new Func<string, FileHandleDto>(path => new FileHandleDto(
                Id: $"item-{Guid.NewGuid():N}", Name: path, ParentId: null, Size: 3,
                CreatedDateTime: DateTimeOffset.UtcNow, LastModifiedDateTime: DateTimeOffset.UtcNow, ETag: null, IsFolder: false,
                WebUrl: "https://contoso.sharepoint.com/sites/test/Shared%20Documents/test.eml"));
            _spe.Setup(s => s.UploadSmallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string path, Stream _, CancellationToken _) => (FileHandleDto?)uploaded(path));
            _spe.Setup(s => s.UploadSmallAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<ConflictBehavior>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string path, Stream _, ConflictBehavior _, CancellationToken _) => (FileHandleDto?)uploaded(path));
            _sender.Setup(s => s.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        }

        /// <summary>One line per call that reached a boundary: Dataverse (any member), the rights probe, an SPE upload, a queued message.</summary>
        public List<string> Touches()
        {
            var touches = new List<string>();
            touches.AddRange(Dataverse.Invocations.Select(i => $"dataverse:{i.Method.Name}"));
            touches.AddRange(Probe.Invocations.Select(i => $"probe:{i.Method.Name}"));
            touches.AddRange(_spe.Invocations.Select(i => $"spe:{i.Method.Name}"));
            touches.AddRange(_sender.Invocations.Select(i => $"servicebus:{i.Method.Name}"));
            return touches;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDataverseService>();
                services.AddSingleton(Dataverse.Object);

                services.RemoveAll<CallerRecordAccessProbe>();
                services.AddScoped(_ => Probe.Object);

                services.RemoveAll<SpeFileStore>();
                services.AddScoped(_ => _spe.Object);

                var client = new Mock<ServiceBusClient>();
                client.Setup(c => c.CreateSender(It.IsAny<string>())).Returns(_sender.Object);
                services.RemoveAll<OfficeJobQueue>();
                services.AddScoped(sp => new OfficeJobQueue(
                    client.Object, Options.Create(new ServiceBusOptions()), sp.GetRequiredService<ILogger<OfficeJobQueue>>()));
            });
        }
    }
}

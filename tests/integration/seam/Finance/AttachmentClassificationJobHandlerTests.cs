using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Sprk.Bff.Api.Services.RecordMatching;
using Sprk.Bff.Api.Telemetry;
using Sprk.Bff.Api.Tests.Seam.ContentUnderstanding;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Finance;

/// <summary>
/// Seam test of the attachment classification pipeline after its migration to the <c>invoice-classify</c> Action
/// (ebilling-r1 task 042, FR-46). The REAL <see cref="AttachmentClassificationJobHandler"/> runs over the REAL
/// <see cref="InvoiceAi"/> facade, <see cref="ActionResolver"/>, <see cref="ActionRunner"/>,
/// <see cref="PromptSchemaRenderer"/>, <see cref="TextExtractorService"/> and <see cref="RecordContainerResolver"/>.
/// Only the data boundaries are doubled: the Binding table (<see cref="IConsumerRoutingService"/>), the Action row
/// (<see cref="IScopeResolverService"/>), the LLM (<see cref="IOpenAiClient"/>), SharePoint Embedded, the document
/// row and the idempotency store.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this proves.</b> The extracted attachment text reaches the model as the Action's <c>documentText</c>
/// operand; the classification, confidence and hints the model returns land in the Dataverse write
/// (<c>sprk_classification</c>, <c>sprk_classificationconfidence</c>, <c>sprk_invoicehintsjson</c>) unchanged; and a
/// missing Binding, a failed completion, an invalid output and a disabled AI stack each end in a defined job outcome
/// with NO classification written and NO prompt supplied by the handler (the facade's playbook and client are strict
/// mocks with no setups).
/// </para>
/// <para>
/// <b>Retry policy under test.</b> The facade's reason codes classify where a failure happened, not whether a retry
/// helps. <c>binding_missing</c> (which a transient routing-table read also produces, platform issue #1936) and
/// <c>completion_failed</c> are retried while attempts remain, then poisoned; <c>output_invalid</c> and the AI kill
/// switch are poisoned at once.
/// </para>
/// </remarks>
public sealed class AttachmentClassificationJobHandlerTests
{
    private const string DriveId = "b!drive-1";
    private const string ItemId = "item-1";
    private static readonly Guid DocumentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ActionId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private const string JpsPrompt =
        """{"$schema":"https://spaarke.com/schemas/prompt/v1","instruction":{"role":"You classify attachments.","task":"Classify the document."},"output":{"fields":[{"name":"classification","type":"string"}],"structuredOutput":true}}""";

    private const string InvoiceText = "INVOICE INV-7 from Acme LLP for professional services rendered in September. Total due 1250.50 USD.";

    private const string InvoiceOutput =
        """{"classification":"InvoiceCandidate","confidence":0.92,"hints":{"vendorName":"Acme LLP","invoiceNumber":"INV-7","invoiceDate":"2026-09-30","totalAmount":1250.50,"currency":"USD","matterReference":null},"reasoning":"Has an invoice header."}""";

    // ── Success: the facade result reaches the Dataverse write ───────────────────────────────────

    [Fact]
    public async Task ProcessAsync_WhenTheActionClassifiesAnInvoice_WritesTheClassificationConfidenceAndHints()
    {
        var h = new Harness(InvoiceText, InvoiceOutput);

        var outcome = await h.Sut.ProcessAsync(h.Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        h.Routing.Verify(r => r.ResolveActionAsync("invoice-classify", It.IsAny<string?>(), It.IsAny<IRoutingContext?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        h.Prompt.Should().Contain("## Input").And.Contain(InvoiceText, "the extracted text is the Action's documentText operand");

        h.Written.Should().NotBeNull();
        h.Written!["sprk_classification"].Should().Be("InvoiceCandidate");
        h.Written["sprk_classificationconfidence"].Should().Be(0.92m);
        h.Written["sprk_invoicereviewstatus"].Should().Be(100000000, "an invoice candidate goes to review");

        using var hints = JsonDocument.Parse((string)h.Written["sprk_invoicehintsjson"]!);
        hints.RootElement.GetProperty("vendorName").GetString().Should().Be("Acme LLP");
        hints.RootElement.GetProperty("invoiceNumber").GetString().Should().Be("INV-7");
        hints.RootElement.GetProperty("invoiceDate").GetString().Should().Be("2026-09-30");
        hints.RootElement.GetProperty("totalAmount").GetDecimal().Should().Be(1250.50m);
        hints.RootElement.GetProperty("currency").GetString().Should().Be("USD");
        hints.RootElement.TryGetProperty("matterReference", out _).Should().BeFalse("a null hint is not serialised");

        h.Idempotency.Verify(i => i.MarkEventAsProcessedAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()), Times.Once);
        h.Idempotency.Verify(i => i.ReleaseProcessingLockAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WhenTheActionSaysNotInvoice_WritesNotInvoiceWithoutReviewStatusAndWithoutHints()
    {
        var h = new Harness("Master services agreement between the parties.", """{"classification":"NotInvoice","confidence":0.97,"hints":null,"reasoning":"A contract."}""");

        var outcome = await h.Sut.ProcessAsync(h.Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        h.Written!["sprk_classification"].Should().Be("NotInvoice");
        h.Written["sprk_classificationconfidence"].Should().Be(0.97m);
        h.Written["sprk_invoicehintsjson"].Should().BeNull();
        h.Written.Should().NotContainKey("sprk_invoicereviewstatus");
    }

    // ── Fail closed (FR-46): no Binding, no prompt, no classification ─────────────────────────────

    [Fact]
    public async Task ProcessAsync_WhenTheBindingCannotBeResolved_RetriesWhileAttemptsRemainWithoutCallingTheModelOrWriting()
    {
        var h = new Harness(InvoiceText, InvoiceOutput);
        h.Routing.Setup(r => r.ResolveActionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IRoutingContext?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        var outcome = await h.Sut.ProcessAsync(h.Job(attempt: 1, maxAttempts: 3), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Failed, "binding_missing may be a transient routing read (#1936): retry");
        outcome.ErrorMessage.Should().Be(InvoiceAiException.BindingMissing);
        h.Prompt.Should().BeNull("with no Binding there is no prompt, and the handler supplies none");
        h.Written.Should().BeNull("no classification is written without an Action result");
        h.Idempotency.Verify(i => i.MarkEventAsProcessedAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()), Times.Never,
            "the document stays reclassifiable");
        h.Idempotency.Verify(i => i.ReleaseProcessingLockAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WhenTheBindingStaysUnresolvedOnTheLastAttempt_Poisons()
    {
        var h = new Harness(InvoiceText, InvoiceOutput);
        h.Routing.Setup(r => r.ResolveActionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IRoutingContext?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        var outcome = await h.Sut.ProcessAsync(h.Job(attempt: 3, maxAttempts: 3), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        outcome.ErrorMessage.Should().Be(InvoiceAiException.BindingMissing);
        h.Written.Should().BeNull();
    }

    [Fact]
    public async Task ProcessAsync_WhenTheCompletionFails_RetriesWhileAttemptsRemainThenPoisons()
    {
        var h = new Harness(InvoiceText, InvoiceOutput);
        h.Model.Setup(o => o.GetStructuredCompletionRawAsync(
                It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("model unavailable"));

        var first = await h.Sut.ProcessAsync(h.Job(attempt: 1, maxAttempts: 3), CancellationToken.None);
        var last = await h.Sut.ProcessAsync(h.Job(attempt: 3, maxAttempts: 3), CancellationToken.None);

        first.Status.Should().Be(JobStatus.Failed);
        first.ErrorMessage.Should().Be(InvoiceAiException.CompletionFailed);
        last.Status.Should().Be(JobStatus.Poisoned);
        last.ErrorMessage.Should().Be(InvoiceAiException.CompletionFailed);
        h.Written.Should().BeNull();
    }

    [Fact]
    public async Task ProcessAsync_WhenTheOutputDoesNotParse_PoisonsAtOnceAndWritesNothing()
    {
        var h = new Harness(InvoiceText, """{"classification":"NotInvoice","reasoning":"no confidence"}""");

        var outcome = await h.Sut.ProcessAsync(h.Job(attempt: 1, maxAttempts: 3), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned, "an answer outside the contract is not retried");
        outcome.ErrorMessage.Should().Be(InvoiceAiException.OutputInvalid);
        h.Written.Should().BeNull();
    }

    [Fact]
    public async Task ProcessAsync_WhenTheAiStackIsDisabled_PoisonsWithoutAnUnhandledExceptionOrAWrite()
    {
        var h = new Harness(InvoiceText, InvoiceOutput, new NullInvoiceAi(NullLogger<NullInvoiceAi>.Instance));

        var outcome = await h.Sut.ProcessAsync(h.Job(attempt: 1, maxAttempts: 3), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        outcome.ErrorMessage.Should().Contain("ai.invoice.disabled");
        h.Written.Should().BeNull();
        h.Idempotency.Verify(i => i.MarkEventAsProcessedAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_WhenNoTextCanBeExtracted_WritesUnknownWithoutCallingTheAction()
    {
        var h = new Harness("   ", InvoiceOutput);

        var outcome = await h.Sut.ProcessAsync(h.Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        h.Written!["sprk_classification"].Should().Be("Unknown");
        h.Written["sprk_classificationconfidence"].Should().Be(0m);
        h.Routing.Verify(r => r.ResolveActionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IRoutingContext?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        h.Prompt.Should().BeNull();
    }

    // ── Fixture ──────────────────────────────────────────────────────────────────────────────────

    private sealed class Harness
    {
        public Harness(string fileText, string modelOutput, IInvoiceAi? invoiceAi = null)
        {
            Routing.Setup(r => r.ResolveActionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IRoutingContext?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ActionId);
            Catalog.Setup(s => s.GetActionAsync(ActionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AnalysisAction
                {
                    Id = ActionId,
                    Name = "Invoice Classify",
                    SystemPrompt = JpsPrompt,
                    OutputSchemaJson = """{"type":"object"}""",
                    Temperature = 0.0m,
                });
            Model.Setup(o => o.GetStructuredCompletionRawAsync(
                    It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(), It.IsAny<string?>(),
                    It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()))
                .Callback((string prompt, BinaryData _, string _, string? _, int? _, float? _, CancellationToken _) => Prompt = prompt)
                .ReturnsAsync(modelOutput);

            var ai = invoiceAi ?? new InvoiceAi(
                new Mock<IPlaybookService>(MockBehavior.Strict).Object,
                new Mock<IOpenAiClient>(MockBehavior.Strict).Object,
                new ActionResolver(Routing.Object, Catalog.Object, NullLogger<ActionResolver>.Instance),
                new ActionRunner(Model.Object, new PromptSchemaRenderer(NullLogger<PromptSchemaRenderer>.Instance), NullLogger<ActionRunner>.Instance),
                ContentUnderstandingTestHost.Unconfigured(),
                NullLogger<InvoiceAi>.Instance);

            Spe.Setup(s => s.GetFileMetadataAsync(DriveId, ItemId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FileHandleDto(ItemId, "attachment.txt", null, fileText.Length, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, false, null));
            Spe.Setup(s => s.DownloadFileAsync(DriveId, ItemId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new MemoryStream(Encoding.UTF8.GetBytes(fileText)));
            Spe.Setup(s => s.GetItemCreatorAsync(DriveId, ItemId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SpeItemCreator("attachment.txt", null, null));

            Idempotency.Setup(i => i.IsEventProcessedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
            Idempotency.Setup(i => i.TryAcquireProcessingLockAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

            Documents.Setup(d => d.UpdateDocumentFieldsAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()))
                .Callback((string _, Dictionary<string, object?> fields, CancellationToken _) => Written = fields)
                .Returns(Task.CompletedTask);

            RecordMatch.Setup(m => m.MatchAsync(It.IsAny<RecordMatchRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RecordMatchResponse());

            // The REAL pointer check, strict rule: an unfiled document's derived container is
            // EmailProcessing:DefaultContainerId, and the item exists in it (Spe.GetItemCreatorAsync above).
            var entities = new Mock<IGenericEntityService>();
            entities.Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string name, Guid id, string[] _, CancellationToken _) => new Entity(name, id));
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [RecordContainerResolver.StrictDerivedContainerKey] = "true",
                    [RecordContainerResolver.UnfiledDefaultContainerKey] = DriveId,
                })
                .Build();
            var containerResolver = new RecordContainerResolver(
                new Mock<ISecurableEntityRegistry>().Object,
                entities.Object,
                NullLogger<RecordContainerResolver>.Instance,
                restampQueue: null,
                communicationOptions: null,
                speFiles: Spe.Object,
                configuration: configuration);

            var textExtractor = new TextExtractorService(
                Options.Create(new DocumentIntelligenceOptions()),
                NullLogger<TextExtractorService>.Instance,
                new TenantCache(
                    new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
                    NullLogger<TenantCache>.Instance));

            Sut = new AttachmentClassificationJobHandler(
                ai,
                Spe.Object,
                textExtractor,
                Documents.Object,
                Idempotency.Object,
                RecordMatch.Object,
                new FinanceTelemetry(),
                NullLogger<AttachmentClassificationJobHandler>.Instance,
                containerResolver);
        }

        /// <summary>The Binding table (sprk_playbookconsumer) boundary.</summary>
        public Mock<IConsumerRoutingService> Routing { get; } = new();

        /// <summary>The Action row (sprk_analysisaction) boundary.</summary>
        public Mock<IScopeResolverService> Catalog { get; } = new();

        /// <summary>The LLM boundary the production ActionRunner calls.</summary>
        public Mock<IOpenAiClient> Model { get; } = new();

        public Mock<ISpeFileOperations> Spe { get; } = new();

        public Mock<IIdempotencyService> Idempotency { get; } = new();

        public Mock<IDocumentDataverseService> Documents { get; } = new();

        public Mock<IRecordMatchService> RecordMatch { get; } = new();

        public AttachmentClassificationJobHandler Sut { get; }

        /// <summary>The prompt the model received, or null when the model was never called.</summary>
        public string? Prompt { get; private set; }

        /// <summary>The fields written to the document row, or null when nothing was written.</summary>
        public Dictionary<string, object?>? Written { get; private set; }

        public JobContract Job(int attempt = 1, int maxAttempts = 3) => new()
        {
            JobType = AttachmentClassificationJobHandler.JobTypeName,
            SubjectId = DocumentId.ToString(),
            Attempt = attempt,
            MaxAttempts = maxAttempts,
            Payload = JsonDocument.Parse($$"""{"documentId":"{{DocumentId}}","driveId":"{{DriveId}}","itemId":"{{ItemId}}"}"""),
        };
    }
}

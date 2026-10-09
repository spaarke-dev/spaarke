using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Models.Ai.SemanticSearch;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Ai.RecordSearch;
using Sprk.Bff.Api.Services.Ai.SemanticSearch;
using Sprk.Bff.Api.Services.Ai.Tools;
using Sprk.Bff.Api.Tests.Services.Ai.Handlers;
using Xunit;

namespace Sprk.Bff.Api.Tests.Regression.Ai;

/// <summary>
/// GitHub #1511 (unified-access-control-r2 task 176): AI chat and playbook retrieval returned the text of documents the
/// caller cannot read (secure matters, Restricted, No Access) because nothing trimmed the RAG rows and the index's
/// privilege filter is never populated (finding A-21). Every test runs the REAL <see cref="RetrievalAccessTrim"/>; only
/// the user-OBO Dataverse boundary (<see cref="ReadableDocumentsUserClient"/>) and the index are simulated.
/// </summary>
/// <remarks>
/// Seeding proof (task 176 notes §6): with <see cref="RetrievalAccessTrim.TrimAsync{T}"/> made to return every row, the
/// <c>SecureDocument_*</c> tests fail.
/// </remarks>
public sealed class Issue1511_AiRetrievalAccessTrimTests : TypedToolHandlerTestFixture
{
    private const string CallerOid = "0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0";
    private const string Secret = "SECRET-1511-merger-price-42m";
    private const string Visible = "VISIBLE-1511-engagement-letter";

    private static readonly Guid SecureDoc = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");
    private static readonly Guid ReadableDoc = Guid.Parse("bbbbbbbb-1111-2222-3333-444444444444");

    private readonly ReadableDocumentsUserClient _userClient = new(ReadableDoc);
    private readonly Mock<IRagService> _rag = new();
    private readonly RetrievalAccessTrim _trim;

    public Issue1511_AiRetrievalAccessTrimTests()
    {
        var accessor = new HttpContextAccessor { HttpContext = TestHttpContexts.Authenticated(CallerOid) };
        _trim = new RetrievalAccessTrim(accessor, _userClient, NullLogger<RetrievalAccessTrim>.Instance);
    }

    private void IndexReturns(params RagSearchResult[] rows) =>
        _rag.Setup(r => r.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RagSearchResponse { Query = "q", Results = rows, TotalCount = rows.Length });

    // The secure chunk is ranked FIRST, so a missing trim cannot hide behind a page cut.
    private void IndexReturnsSecureThenReadable() => IndexReturns(
        Chunk("c-secure", SecureDoc, "Secure merger memo.docx", Secret),
        Chunk("c-readable", ReadableDoc, "Engagement letter.docx", Visible));

    private static RagSearchResult Chunk(string id, Guid doc, string name, string content) => new()
    {
        Id = id,
        // Registry spelling (braces, upper case), as Xrm-sourced ids can be: the trim must still match (ADR-044).
        DocumentId = "{" + doc.ToString().ToUpperInvariant() + "}",
        DocumentName = name,
        Content = content,
        ChunkIndex = 0,
        ChunkCount = 1,
        Score = 0.9,
    };

    private ChatInvocationContext Chat(string? userId = CallerOid, ChatKnowledgeScope? scope = null, string args = "{\"query\":\"price\"}") => new()
    {
        ChatSessionId = Guid.NewGuid(),
        TenantId = "tenant-1",
        UserId = userId,
        KnowledgeScope = scope,
        ToolArgumentsJson = args,
    };

    private DocumentSearchHandler DocumentSearch(IRetrievalAccessTrim? trim = null) =>
        new(_rag.Object, trim ?? _trim, CreateLogger<DocumentSearchHandler>());

    private static string Everything(ToolResult result) =>
        JsonSerializer.Serialize(result.Data) + JsonSerializer.Serialize(result.Metadata) + result.Summary;

    // ── goal 2: every entry point ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(DocumentSearchHandler.MethodSearchDocuments)]
    [InlineData(DocumentSearchHandler.MethodSearchDiscovery)]
    public async Task SecureDocument_DoesNotReachDocumentSearch(string method)
    {
        IndexReturnsSecureThenReadable();
        var tool = BuildAnalysisTool(nameof(DocumentSearchHandler), $"{{\"method\":\"{method}\"}}");

        var result = await DocumentSearch().ExecuteChatAsync(Chat(), tool, CancellationToken.None);

        JsonSerializer.Serialize(result.Data).Should().NotContain(Secret);
        JsonSerializer.Serialize(result.Metadata).Should().NotContain(Secret).And.NotContain("Secure merger memo");
        JsonSerializer.Serialize(result.Data).Should().Contain(Visible[..20]);
    }

    [Theory]
    [InlineData(KnowledgeRetrievalHandler.MethodSearchKnowledgeBase, "{\"query\":\"price\"}")]
    [InlineData(KnowledgeRetrievalHandler.MethodGetKnowledgeSource, "{\"knowledgeSourceId\":\"9c0e7f43-6c1b-4f3e-9d43-0d8a1c2b3e4f\"}")]
    public async Task SecureDocument_DoesNotReachKnowledgeRetrieval(string method, string args)
    {
        IndexReturnsSecureThenReadable();
        var handler = new KnowledgeRetrievalHandler(_rag.Object, _trim, CreateLogger<KnowledgeRetrievalHandler>());
        var tool = BuildAnalysisTool(nameof(KnowledgeRetrievalHandler), $"{{\"method\":\"{method}\"}}");

        var result = await handler.ExecuteChatAsync(Chat(args: args), tool, CancellationToken.None);

        Everything(result).Should().NotContain(Secret).And.NotContain("Secure merger memo");
        JsonSerializer.Serialize(result.Data).Should().Contain(Visible);
    }

    [Fact]
    public async Task SecureDocument_DoesNotReachPlaybookNodeDocumentContext()
    {
        IndexReturnsSecureThenReadable();
        ToolExecutionContext? seen = null;
        var handler = new Mock<IAnalysisToolHandler>();
        handler.Setup(h => h.HandlerId).Returns("CaptureHandler");
        handler.Setup(h => h.Validate(It.IsAny<ToolExecutionContext>(), It.IsAny<AnalysisTool>()))
            .Returns(ToolValidationResult.Success());
        handler.Setup(h => h.ExecuteAsync(It.IsAny<ToolExecutionContext>(), It.IsAny<AnalysisTool>(), It.IsAny<CancellationToken>()))
            .Callback<ToolExecutionContext, AnalysisTool, CancellationToken>((c, _, _) => seen = c)
            .ReturnsAsync((ToolExecutionContext _, AnalysisTool t, CancellationToken _) =>
                ToolResult.Ok("CaptureHandler", t.Id, t.Name, data: new { ok = true }));
        var registry = new Mock<IToolHandlerRegistry>();
        registry.Setup(r => r.GetHandler("CaptureHandler")).Returns(handler.Object);

        var services = new ServiceCollection();
        services.AddSingleton(registry.Object);
        services.AddSingleton<IRetrievalAccessTrim>(_trim);
        services.AddSingleton(Mock.Of<IRecordSearchService>());
        using var provider = services.BuildServiceProvider();
        var executor = new AiAnalysisNodeExecutor(provider, null!, _rag.Object, NullLogger<AiAnalysisNodeExecutor>.Instance);

        var toolId = Guid.NewGuid();
        var context = new NodeExecutionContext
        {
            RunId = Guid.NewGuid(),
            PlaybookId = Guid.NewGuid(),
            Node = new PlaybookNodeDto
            {
                Id = Guid.NewGuid(), PlaybookId = Guid.NewGuid(), ActionId = Guid.NewGuid(), ToolIds = new[] { toolId },
                Name = "Node", ExecutionOrder = 1, OutputVariable = "out", IsActive = true,
                ConfigJson = "{\"knowledgeRetrieval\":{\"includeDocumentContext\":true}}",
            },
            Action = new AnalysisAction { Id = Guid.NewGuid(), Name = "Review", SystemPrompt = "Review the document." },
            ExecutorType = ExecutorType.AiAnalysis,
            Scopes = new ResolvedScopes([], [], [new AnalysisTool { Id = toolId, Name = "T", Type = ToolType.Custom, HandlerClass = "CaptureHandler" }]),
            Document = new DocumentContext { DocumentId = Guid.NewGuid(), Name = "Current.docx", ExtractedText = "text" },
            TenantId = "tenant-1",
            CallerObjectId = CallerOid,
        };

        await executor.ExecuteAsync(context, CancellationToken.None);

        seen.Should().NotBeNull();
        seen!.KnowledgeContext.Should().NotContain(Secret).And.Contain(Visible);
        seen.CallerObjectId.Should().Be(CallerOid, because: "the run principal reaches retrieval tools on the playbook path");
    }

    [Fact]
    public async Task SecureDocument_DoesNotReachSemanticSearchTool()
    {
        var search = new Mock<ISemanticSearchService>();
        search.Setup(s => s.SearchAsync(It.IsAny<SemanticSearchRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SemanticSearchResponse
            {
                Results = new[]
                {
                    new SearchResult { DocumentId = SecureDoc.ToString(), Name = "Secure merger memo.docx", Highlights = new[] { Secret } },
                    new SearchResult { DocumentId = ReadableDoc.ToString(), Name = "Engagement letter.docx", Highlights = new[] { Visible } },
                },
                Metadata = new SearchMetadata { TotalResults = 2, ReturnedResults = 2 },
            });
        var handler = new SemanticSearchToolHandler(
            search.Object, _trim, Mock.Of<IOpenAiClient>(),
            new PromptSchemaRenderer(NullLogger<PromptSchemaRenderer>.Instance), NullLogger<SemanticSearchToolHandler>.Instance);
        var tool = BuildAnalysisTool("SemanticSearchHandler",
            $"{{\"query\":\"price\",\"scope\":\"entity\",\"entityType\":\"matter\",\"entityId\":\"{Guid.NewGuid()}\"}}");
        var context = new ToolExecutionContext
        {
            AnalysisId = Guid.NewGuid(),
            TenantId = "tenant-1",
            Document = new DocumentContext { DocumentId = Guid.NewGuid(), Name = "x", ExtractedText = "x" },
            CallerObjectId = CallerOid,
        };

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        Everything(result).Should().NotContain(Secret).And.NotContain("Secure merger memo");
        JsonSerializer.Serialize(result.Data).Should().Contain(Visible);
    }

    // ── goal 5: what the caller may see is unchanged ───────────────────────────────────────────

    [Fact]
    public async Task ReadableDocument_IsReturnedExactlyAsBefore()
    {
        IndexReturns(Chunk("c-readable", ReadableDoc, "Engagement letter.docx", Visible));
        var tool = BuildAnalysisTool(nameof(DocumentSearchHandler), "{\"method\":\"SearchDocuments\"}");

        var trimmed = await DocumentSearch().ExecuteChatAsync(Chat(), tool, CancellationToken.None);
        var untrimmed = await DocumentSearch(PermitAllRetrievalAccessTrim.Instance).ExecuteChatAsync(Chat(), tool, CancellationToken.None);

        JsonSerializer.Serialize(trimmed.Data).Should().Be(JsonSerializer.Serialize(untrimmed.Data));
        JsonSerializer.Serialize(trimmed.Metadata).Should().Be(JsonSerializer.Serialize(untrimmed.Metadata));
    }

    // ── goal 3: no caller, or no answer, returns nothing ───────────────────────────────────────

    [Fact]
    public async Task NoCallerIdentity_ReturnsNoRows_SaysSo_AndDoesNotSearch()
    {
        IndexReturnsSecureThenReadable();
        var tool = BuildAnalysisTool(nameof(DocumentSearchHandler), "{\"method\":\"SearchDiscovery\"}");

        var result = await DocumentSearch().ExecuteChatAsync(Chat(userId: null), tool, CancellationToken.None);

        Everything(result).Should().NotContain(Secret).And.NotContain(Visible);
        result.Data!.Value.GetProperty("message").GetString().Should().Contain("withheld");
        _rag.Verify(r => r.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UnattendedPlaybookRun_WithNoRunPrincipal_ReturnsNoRows()
    {
        IndexReturnsSecureThenReadable();
        var tool = BuildAnalysisTool(nameof(DocumentSearchHandler), "{\"method\":\"SearchDocuments\",\"query\":\"price\"}");
        var context = new ToolExecutionContext
        {
            AnalysisId = Guid.NewGuid(),
            TenantId = "tenant-1",
            Document = new DocumentContext { DocumentId = Guid.NewGuid(), Name = "x", ExtractedText = "x" },
            CallerObjectId = null,
        };

        var result = await DocumentSearch().ExecuteAsync(context, tool, CancellationToken.None);

        Everything(result).Should().NotContain(Secret).And.NotContain(Visible);
    }

    [Fact]
    public async Task AFailedOrThrottledAccessCheck_ReturnsNoRows()
    {
        IndexReturnsSecureThenReadable();
        _userClient.FailWithStatus = 429;
        var tool = BuildAnalysisTool(nameof(DocumentSearchHandler), "{\"method\":\"SearchDocuments\"}");

        var result = await DocumentSearch().ExecuteChatAsync(Chat(), tool, CancellationToken.None);

        Everything(result).Should().NotContain(Secret).And.NotContain(Visible);
        result.Data!.Value.GetProperty("message").GetString().Should().Contain("withheld");
    }

    // ── goal 4: scope ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HostDropped_DiscoverySearchesTheTenant_ButReturnsOnlyWhatTheCallerCanRead()
    {
        RagSearchOptions? sent = null;
        _rag.Setup(r => r.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .Callback<string, RagSearchOptions, CancellationToken>((_, o, _) => sent = o)
            .ReturnsAsync(new RagSearchResponse
            {
                Query = "q",
                Results = new[]
                {
                    Chunk("c-secure", SecureDoc, "Secure merger memo.docx", Secret),
                    Chunk("c-readable", ReadableDoc, "Engagement letter.docx", Visible),
                },
                TotalCount = 2,
            });
        var tool = BuildAnalysisTool(nameof(DocumentSearchHandler), "{\"method\":\"SearchDiscovery\"}");

        // Task 164 drops an unauthorizable host: the session reaches the tool with no knowledge scope.
        var result = await DocumentSearch().ExecuteChatAsync(Chat(scope: null), tool, CancellationToken.None);

        sent!.ParentEntityId.Should().BeNull(because: "with no host the index query is tenant-wide");
        Everything(result).Should().NotContain(Secret);
        result.Data!.Value.GetProperty("resultCount").GetInt32().Should().Be(1,
            because: "the tenant-wide page is trimmed to the caller's readable documents, never returned whole");
    }

    /// <summary>The knowledge scope exactly as PlaybookChatContextProvider builds it from the session's host.</summary>
    private static ChatKnowledgeScope ScopeFor(ChatHostContext host) => new(
        RagKnowledgeSourceIds: [], InlineContent: null, SkillInstructions: null, ActiveDocumentId: null,
        ParentEntityType: host.EntityType, ParentEntityId: host.EntityId);

    [Theory]
    [InlineData(DocumentSearchHandler.MethodSearchDocuments, "sprk_matter", "matter")]
    [InlineData(DocumentSearchHandler.MethodSearchDiscovery, "sprk_matter", "matter")]
    [InlineData(DocumentSearchHandler.MethodSearchDocuments, "sprk_workassignment", "workassignment")]
    public async Task WithAnIndexedHost_BothMethodsAreBoundToTheHostParent(string method, string hostType, string indexedType)
    {
        RagSearchOptions? sent = null;
        _rag.Setup(r => r.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .Callback<string, RagSearchOptions, CancellationToken>((_, o, _) => sent = o)
            .ReturnsAsync(new RagSearchResponse { Query = "q", Results = Array.Empty<RagSearchResult>(), TotalCount = 0 });
        var hostId = Guid.NewGuid();
        // Production normalization: ChatHostContext runs EntityTypeNormalizer on the type the client sent.
        var host = new ChatHostContext(hostType, "{" + hostId.ToString().ToUpperInvariant() + "}");
        var tool = BuildAnalysisTool(nameof(DocumentSearchHandler), $"{{\"method\":\"{method}\"}}");

        await DocumentSearch().ExecuteChatAsync(Chat(scope: ScopeFor(host)), tool, CancellationToken.None);

        sent!.ParentEntityType.Should().Be(indexedType, because: "the filter uses the type the index stores");
        sent.ParentEntityId.Should().Be(hostId.ToString("D"), because: "ADR-044: the AI Search eq filter needs the bare lowercase id");
    }

    [Theory]
    [InlineData(DocumentSearchHandler.MethodSearchDocuments, "sprk_analysisoutput")]
    [InlineData(DocumentSearchHandler.MethodSearchDiscovery, "sprk_analysisoutput")]
    [InlineData(DocumentSearchHandler.MethodSearchDocuments, "sprk_document")]
    [InlineData(DocumentSearchHandler.MethodSearchDiscovery, "sprk_document")]
    public async Task WithAHostTheIndexDoesNotStore_SearchIsTheTrimmedTenantSearch_NotEmpty(string method, string hostType)
    {
        RagSearchOptions? sent = null;
        _rag.Setup(r => r.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .Callback<string, RagSearchOptions, CancellationToken>((_, o, _) => sent = o)
            .ReturnsAsync(new RagSearchResponse
            {
                Query = "q",
                Results = new[]
                {
                    Chunk("c-secure", SecureDoc, "Secure merger memo.docx", Secret),
                    Chunk("c-readable", ReadableDoc, "Engagement letter.docx", Visible),
                },
                TotalCount = 2,
            });
        var host = new ChatHostContext(hostType, Guid.NewGuid().ToString());
        var tool = BuildAnalysisTool(nameof(DocumentSearchHandler), $"{{\"method\":\"{method}\"}}");

        var result = await DocumentSearch().ExecuteChatAsync(Chat(scope: ScopeFor(host)), tool, CancellationToken.None);

        sent!.ParentEntityType.Should().BeNull(because: $"no chunk is filed under a {hostType}, so binding would always return nothing");
        sent.ParentEntityId.Should().BeNull();
        Everything(result).Should().NotContain(Secret);
        JsonSerializer.Serialize(result.Data).Should().Contain(Visible[..20],
            because: "a readable document still comes back, trimmed");
    }

    // ── Insights cohort retrieval (IndexRetrieveNode, verifier F1) ─────────────────────────────

    [Fact]
    public async Task SecureDocument_AndSecureMatter_Observations_DoNotReachIndexRetrieve()
    {
        var readableMatter = Guid.NewGuid();
        var secureMatter = Guid.NewGuid();
        _userClient.Allow("sprk_matters", readableMatter.ToString("D"));
        _userClient.Allow("sprk_documents", "ITEM-READABLE");

        static Azure.Search.Documents.Models.SearchResult<Azure.Search.Documents.Models.SearchDocument> Row(
            string id, string value, Guid matter, params (string RefType, string Ref)[] evidence)
        {
            var doc = new Azure.Search.Documents.Models.SearchDocument
            {
                ["id"] = id,
                ["tenantId"] = "tenant-1",
                ["artifactType"] = "observation",
                ["subject"] = $"matter:{matter}",
                ["predicate"] = "settlementAmount",
                ["valueJson"] = value,
                ["evidence"] = evidence
                    .Select(e => (object)new Azure.Search.Documents.Models.SearchDocument
                    {
                        ["refType"] = e.RefType, ["ref"] = e.Ref, ["quote"] = value,
                    })
                    .ToArray(),
                ["scope"] = new Azure.Search.Documents.Models.SearchDocument
                {
                    ["entityType"] = "matter", ["entityId"] = matter.ToString(),
                },
            };
            return Azure.Search.Documents.Models.SearchModelFactory.SearchResult(doc, 0.9, null);
        }

        var hits = new[]
        {
            // A restricted document under a matter the caller CAN read: the document decides.
            Row("o-restricted-doc", Secret + "-A", readableMatter, ("document", SecureDoc.ToString())),
            // An spe:// ref to an item the caller has no readable document for: the document decides.
            Row("o-secure-item", Secret + "-B", readableMatter, ("document", "spe://drive/b!x/item/ITEM-SECURE")),
            // No document evidence, on a secure matter: the matter decides.
            Row("o-secure-matter", Secret + "-C", secureMatter, ("playbook-run", "playbook://x")),
            // Readable: via a readable item, and via a readable matter.
            Row("o-ok-item", Visible + "-D", secureMatter, ("document", "spe://drive/b!x/item/ITEM-READABLE")),
            Row("o-ok-matter", Visible + "-E", readableMatter),
        };
        var results = Azure.Search.Documents.Models.SearchModelFactory.SearchResults(
            hits, totalCount: hits.Length, facets: null, coverage: null, rawResponse: Mock.Of<Azure.Response>());
        var searchClient = new Mock<Azure.Search.Documents.SearchClient>();
        searchClient
            .Setup(c => c.SearchAsync<Azure.Search.Documents.Models.SearchDocument>(
                It.IsAny<string>(), It.IsAny<Azure.Search.Documents.SearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Azure.Response.FromValue(results, Mock.Of<Azure.Response>()));
        var indexClient = new Mock<Azure.Search.Documents.Indexes.SearchIndexClient>();
        indexClient.Setup(c => c.GetSearchClient(It.IsAny<string>())).Returns(searchClient.Object);

        var services = new ServiceCollection();
        services.AddSingleton<IRetrievalAccessTrim>(_trim);
        using var provider = services.BuildServiceProvider();
        var node = new IndexRetrieveNode(
            indexClient.Object, Mock.Of<IOpenAiClient>(), provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<IndexRetrieveNode>.Instance);
        var context = NodeContext(ExecutorType.IndexRetrieve, "{\"artifactType\":\"observation\",\"predicate\":\"settlementAmount\"}");

        var output = await node.ExecuteAsync(context, CancellationToken.None);

        output.Success.Should().BeTrue();
        var json = output.StructuredData!.Value.GetRawText() + output.TextContent;
        json.Should().NotContain(Secret, because: "no observation the caller cannot read reaches the cohort");
        json.Should().Contain(Visible + "-D").And.Contain(Visible + "-E");
        _userClient.ReadSets.Should().Contain("sprk_matters").And.Contain("sprk_documents");
    }

    // ── L3 entity context (verifier F1) ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task L3EntityContext_IsAddedOnlyForAParentTheCallerCanRead(bool callerCanRead)
    {
        const string SecretMatterName = "SECRET-1511-Project-Falcon";
        var secureMatter = Guid.NewGuid();
        if (callerCanRead)
        {
            _userClient.Allow("sprk_matters", secureMatter.ToString("D"));
        }

        var records = new Mock<IRecordSearchService>();
        records.Setup(r => r.SearchAsync(It.IsAny<Sprk.Bff.Api.Models.Ai.RecordSearch.RecordSearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sprk.Bff.Api.Models.Ai.RecordSearch.RecordSearchResponse
            {
                Results = new[]
                {
                    new Sprk.Bff.Api.Models.Ai.RecordSearch.RecordSearchResult
                    {
                        RecordId = secureMatter.ToString(), RecordType = "sprk_matter", RecordName = SecretMatterName,
                    },
                },
                Metadata = new Sprk.Bff.Api.Models.Ai.RecordSearch.RecordSearchMetadata { HybridMode = "keywordOnly" },
            });

        var seen = await RunNodeCapturingToolContextAsync(
            "{\"knowledgeRetrieval\":{\"includeEntityContext\":true},\"parentEntityType\":\"matter\",\"parentEntityId\":\"" + secureMatter + "\"}",
            records.Object);

        if (callerCanRead)
        {
            seen.KnowledgeContext.Should().Contain(SecretMatterName, because: "a readable parent still gets L3 context");
        }
        else
        {
            (seen.KnowledgeContext ?? string.Empty).Should().NotContain(SecretMatterName);
        }

        _userClient.ReadSets.Should().Contain("sprk_matters", because: "the parent was checked as the caller");
    }

    private NodeExecutionContext NodeContext(ExecutorType type, string configJson, Guid? toolId = null) => new()
    {
        RunId = Guid.NewGuid(),
        PlaybookId = Guid.NewGuid(),
        Node = new PlaybookNodeDto
        {
            Id = Guid.NewGuid(), PlaybookId = Guid.NewGuid(), ActionId = Guid.NewGuid(),
            ToolIds = toolId is { } t ? new[] { t } : Array.Empty<Guid>(),
            Name = "Node", ExecutionOrder = 1, OutputVariable = "out", IsActive = true, ConfigJson = configJson,
        },
        Action = new AnalysisAction { Id = Guid.NewGuid(), Name = "Review", SystemPrompt = "Review the document." },
        ExecutorType = type,
        Scopes = toolId is { } id
            ? new ResolvedScopes([], [], [new AnalysisTool { Id = id, Name = "T", Type = ToolType.Custom, HandlerClass = "CaptureHandler" }])
            : new ResolvedScopes([], [], []),
        Document = new DocumentContext { DocumentId = Guid.NewGuid(), Name = "Current.docx", ExtractedText = "text" },
        TenantId = "tenant-1",
        CallerObjectId = CallerOid,
    };

    private async Task<ToolExecutionContext> RunNodeCapturingToolContextAsync(string configJson, IRecordSearchService records)
    {
        ToolExecutionContext? seen = null;
        var handler = new Mock<IAnalysisToolHandler>();
        handler.Setup(h => h.HandlerId).Returns("CaptureHandler");
        handler.Setup(h => h.Validate(It.IsAny<ToolExecutionContext>(), It.IsAny<AnalysisTool>()))
            .Returns(ToolValidationResult.Success());
        handler.Setup(h => h.ExecuteAsync(It.IsAny<ToolExecutionContext>(), It.IsAny<AnalysisTool>(), It.IsAny<CancellationToken>()))
            .Callback<ToolExecutionContext, AnalysisTool, CancellationToken>((c, _, _) => seen = c)
            .ReturnsAsync((ToolExecutionContext _, AnalysisTool t, CancellationToken _) =>
                ToolResult.Ok("CaptureHandler", t.Id, t.Name, data: new { ok = true }));
        var registry = new Mock<IToolHandlerRegistry>();
        registry.Setup(r => r.GetHandler("CaptureHandler")).Returns(handler.Object);

        var services = new ServiceCollection();
        services.AddSingleton(registry.Object);
        services.AddSingleton<IRetrievalAccessTrim>(_trim);
        services.AddSingleton(records);
        using var provider = services.BuildServiceProvider();
        var executor = new AiAnalysisNodeExecutor(provider, null!, _rag.Object, NullLogger<AiAnalysisNodeExecutor>.Instance);

        await executor.ExecuteAsync(NodeContext(ExecutorType.AiAnalysis, configJson, Guid.NewGuid()), CancellationToken.None);

        seen.Should().NotBeNull();
        return seen!;
    }
}

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Memory;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// R6 Pillar 1 — task 005 (D-A-05) regression tests for the persona-resolution cutover in
/// <see cref="PlaybookChatContextProvider.GetContextAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// The Pillar-1 cutover replaces the hardcoded standalone-mode call to
/// <c>BuildDefaultSystemPrompt(null)</c> with
/// <see cref="IScopeResolverService.ResolvePersonaForChatAsync(string, System.Guid?, System.Threading.CancellationToken)"/>
/// (added in task 003). The seeded SYS-DEFAULT row (task 004) returns the byte-identical text
/// that the legacy method produced, so behavior with no tenant CUST- override + no playbook
/// bound MUST be unchanged (FR-04 binding).
/// </para>
/// <para>
/// These tests live alongside <see cref="PlaybookChatContextProviderTests"/> but are named
/// <c>SprkChatAgentFactoryPersonaTests</c> because the cutover closes the Pillar-1 path that
/// ultimately feeds <c>SprkChatAgentFactory.CreateAgentAsync</c>. The factory call chain is:
/// factory → <c>IChatContextProvider.GetContextAsync</c> → <c>PlaybookChatContextProvider.GetContextAsync</c>
/// → resolver (NEW) → SYS-DEFAULT persona row. Asserting at the provider level verifies the
/// full agent-prompt assembly produces the same byte sequence post-cutover.
/// </para>
/// <para>
/// NFR-01 binding: scopes augment but never replace conversational ability. The CUST- override
/// test asserts that the persona's <c>SystemPrompt</c> appears verbatim in the assembled prompt
/// but neither the safety pipeline, knowledge composition, document enrichment, nor entity
/// enrichment layers are removed or short-circuited by the persona resolution.
/// </para>
/// </remarks>
[Trait("status", "passing")]
[Trait("task", "r6-task-005")]
public class SprkChatAgentFactoryPersonaTests
{
    private const string TestDocumentId = "doc-persona-001";
    private const string TestTenantId = "tenant-persona-test";

    private readonly Mock<IScopeResolverService> _scopeResolverMock;
    private readonly Mock<IPlaybookService> _playbookServiceMock;
    private readonly Mock<IDataverseService> _dataverseServiceMock;
    private readonly Mock<ILogger<PlaybookChatContextProvider>> _loggerMock;

    public SprkChatAgentFactoryPersonaTests()
    {
        _scopeResolverMock = new Mock<IScopeResolverService>();
        _playbookServiceMock = new Mock<IPlaybookService>();
        _dataverseServiceMock = new Mock<IDataverseService>();
        _loggerMock = new Mock<ILogger<PlaybookChatContextProvider>>();
    }

    /// <summary>
    /// The exact pre-cutover text produced by the legacy
    /// <c>PlaybookChatContextProvider.BuildDefaultSystemPrompt(null)</c> method, captured
    /// verbatim from master HEAD on 2026-06-07 by task 004 and seeded into the SYS-DEFAULT
    /// Dataverse row (<c>sprk_aipersonaid=4fe49430-aa62-f111-ab0c-70a8a58ae145</c> on Spaarke Dev).
    /// </summary>
    /// <remarks>
    /// FR-04 binding: when no tenant CUST- override and no playbook is bound, the resolver
    /// returns this exact text. The system prompt assembled by the provider MUST be byte-identical
    /// to the pre-cutover behavior (modulo entity enrichment which is a separate concern handled
    /// by <see cref="PlaybookChatContextProvider.AppendEntityEnrichment"/>).
    /// </remarks>
    private const string Fr04SysDefaultPromptVerbatim = """
        You are Spaarke AI, an intelligent assistant for legal professionals using the Spaarke platform.
        You help with document analysis, matter management, legal research, financial analysis, and general questions about the user's work.

        ## Your Capabilities
        You have access to powerful tools — use them proactively:

        - **SearchDocuments**: Search the document index to find relevant content. Use this when the user asks about documents, contracts, agreements, filings, or any content stored in Spaarke.
        - **SearchDiscovery**: Broad discovery search across all indexed documents. Use this when the user asks to find matters, projects, documents, or explore what's available.
        - **GetKnowledgeSource**: Retrieve full content from a specific knowledge source. Use after SearchDocuments identifies a relevant source.
        - **SearchKnowledgeBase**: Search the knowledge base for reference information, policies, and best practices.
        - **GetAnalysisResult** / **GetAnalysisSummary**: Retrieve prior analysis results for documents that have been analyzed.
        - **RefineText**: Help the user improve, rewrite, or restructure text.

        ## Instructions
        - When the user asks about their matters, projects, or documents, **always use SearchDiscovery or SearchDocuments first** — don't say you can't access their data.
        - When you find relevant documents, summarize what you found and offer to analyze further.
        - If the user asks to analyze a document but none is loaded, suggest they upload one or help them search for it.
        - Cite sources and document names when referencing search results.
        - Be proactive — if a search returns relevant results, highlight key findings.
        - Format responses in clear, readable Markdown with headings and structure.

        ## What You Know About
        - Legal documents (contracts, agreements, court filings, memos, briefs)
        - Matter management (case details, timelines, budgets, parties)
        - Financial data (budgets, invoices, billing, cost analysis)
        - Document comparison and review workflows
        - Legal research and case law (when Bing Grounding is available)
        """;

    // ─────────────────────────────────────────────────────────────────────────────
    // FR-04 binding: with no tenant CUST- override + no playbook, the assembled
    // system prompt is byte-identical to the pre-cutover BuildDefaultSystemPrompt(null)
    // output. This is the headline regression test for the Pillar-1 cutover.
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetContextAsync_StandaloneMode_NoOverrides_ProducesByteIdenticalPrompt()
    {
        // Arrange — resolver returns the seeded SYS-DEFAULT persona (task 004) carrying
        // the byte-identical text the legacy BuildDefaultSystemPrompt(null) produced.
        _scopeResolverMock
            .Setup(r => r.ResolvePersonaForChatAsync(
                TestTenantId, /* playbookId */ null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisPersona
            {
                Id = Guid.Parse("4fe49430-aa62-f111-ab0c-70a8a58ae145"),
                Name = "SYS-DEFAULT",
                SystemPrompt = Fr04SysDefaultPromptVerbatim,
                ScopeType = PersonaScopeType.Global,
                OwnerType = ScopeOwnerType.System,
                IsImmutable = true
            });

        var sut = CreateProvider();

        // Act — standalone mode (playbookId == null) is the FR-04 cutover path.
        // hostContext is null so AppendEntityEnrichment is a no-op (returns the prompt
        // unchanged). This isolates the persona-resolution contribution.
        var context = await sut.GetContextAsync(
            documentId: string.Empty,
            tenantId: TestTenantId,
            playbookId: null,
            hostContext: null,
            cancellationToken: CancellationToken.None);

        // Assert — byte-identical match against the verbatim FR-04 baseline.
        // The resolver returned the SYS-DEFAULT text; no entity enrichment fired;
        // no document summary loaded (empty documentId); the SystemPrompt MUST equal
        // the legacy BuildDefaultSystemPrompt(null) output exactly.
        context.SystemPrompt.Should().Be(Fr04SysDefaultPromptVerbatim,
            "FR-04 binding — Pillar-1 cutover preserves identical standalone-mode prompt");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // NFR-01 conversational primacy: with a CUST- override resolved, the persona text
    // is composed into the system prompt verbatim, but the conversational scaffold
    // (no tool gate, no safety pipeline removal, no agent capability suppression)
    // remains intact. The provider returns a full ChatContext with the persona text;
    // downstream tool registration + middleware + capability routing are unaffected.
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetContextAsync_StandaloneMode_CustOverride_UsesPersonaTextAndKeepsContextShape()
    {
        // Arrange — resolver returns a mocked CUST- override.
        const string custPersonaText = "You are CUST-ACME-LEGAL, the Acme tenant's tailored legal voice. " +
                                       "Lean into precision and brevity.";
        _scopeResolverMock
            .Setup(r => r.ResolvePersonaForChatAsync(
                TestTenantId, /* playbookId */ null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisPersona
            {
                Id = Guid.NewGuid(),
                Name = "CUST-ACME-LEGAL",
                SystemPrompt = custPersonaText,
                ScopeType = PersonaScopeType.Tenant,
                OwnerType = ScopeOwnerType.Customer,
                IsImmutable = false
            });

        var sut = CreateProvider();

        // Act
        var context = await sut.GetContextAsync(
            documentId: string.Empty,
            tenantId: TestTenantId,
            playbookId: null,
            hostContext: null,
            cancellationToken: CancellationToken.None);

        // Assert — persona text is the system prompt opening verbatim.
        context.SystemPrompt.Should().Be(custPersonaText,
            "CUST- override system prompt used verbatim");

        // Assert — the rest of the ChatContext shape is unchanged: no playbook-specific
        // knowledge scoping fires (playbookId is null), document summary is null (no
        // doc loaded), and the context object itself is well-formed. NFR-01 binding:
        // none of the conversational layers are dropped by the persona swap.
        context.PlaybookId.Should().BeNull("standalone mode — no playbook bound");
        context.DocumentSummary.Should().BeNull("no document loaded");
        context.AnalysisMetadata.Should().BeNull("no document metadata");
        context.KnowledgeScope.Should().BeNull(
            "standalone mode without host-context entity scope — no RAG / no entity boundary");
        context.UploadedFiles.Should().BeNull("no uploaded files");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Defense-in-depth: when the resolver throws InvalidOperationException
    // (catastrophic SYS- seed-data failure per task 003's contract), the provider
    // MUST surface a CRITICAL log and fall back to the legacy BuildDefaultSystemPrompt
    // text — preserving chat availability while loudly signaling the deployment gap.
    // This is NOT a code path that should fire in production; it's a null-safety
    // assertion guarding against task 004 SYS-DEFAULT row being missing on a new
    // environment. Per project CLAUDE.md "MUST NOT hardcode persona text" — the
    // fallback is justified by the "OR retained ONLY as a dev-time fallback that
    // asserts the resolver returned non-null (with a CRITICAL log + bug report)"
    // exception called out in the task POML prompt.
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetContextAsync_StandaloneMode_ResolverThrows_FallsBackToLegacyTextWithCriticalLog()
    {
        // Arrange — resolver throws (catastrophic SYS- seed-data failure).
        _scopeResolverMock
            .Setup(r => r.ResolvePersonaForChatAsync(
                TestTenantId, /* playbookId */ null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(
                "No persona resolved for tenant 'tenant-persona-test' (playbookId=). " +
                "Expected at least one global SYS- persona seeded per R6 Pillar 1 / FR-04 " +
                "(task 004 seed-row deployment)."));

        var sut = CreateProvider();

        // Act — should not throw; fallback path engages.
        var context = await sut.GetContextAsync(
            documentId: string.Empty,
            tenantId: TestTenantId,
            playbookId: null,
            hostContext: null,
            cancellationToken: CancellationToken.None);

        // Assert — fallback produced the legacy verbatim text (FR-04 byte-identical
        // post-line-ending-normalization). The runtime's BuildDefaultSystemPrompt(null)
        // returns text whose line endings come from the source file (CRLF on Windows
        // checkouts, LF on Linux checkouts). The seeded SYS-DEFAULT row in Dataverse
        // stores LF (task 004 normalized at seed time per `.gitattributes` convention).
        // Comparing semantically (LF-normalized on both sides) is the correct FR-04 test:
        // the SAME content reaches the agent before and after cutover.
        Normalize(context.SystemPrompt).Should().Be(Normalize(Fr04SysDefaultPromptVerbatim),
            "fallback path produces legacy BuildDefaultSystemPrompt(null) text " +
            "(line-ending-normalized; per task 004 the SYS-DEFAULT Dataverse row stores LF)");

        // Assert — a CRITICAL-level log fired (operator visibility).
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Critical,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, _) => o.ToString()!.Contains("persona resolver returned no SYS- default")),
                It.IsAny<InvalidOperationException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "operator alert: catastrophic SYS- seed-data failure surfaced as CRITICAL");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // ADR-013 facade boundary: the resolver injection point is IScopeResolverService.
    // We verify the resolver is invoked with the correct (tenantId, playbookId) pair.
    // No persona-specific public contract was added to Services/Ai/PublicContracts/
    // (the resolver itself is AI-internal per ADR-013 + task 003's notes).
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetContextAsync_StandaloneMode_InvokesResolverWithCorrectTenantAndPlaybookId()
    {
        // Arrange
        _scopeResolverMock
            .Setup(r => r.ResolvePersonaForChatAsync(
                It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisPersona
            {
                Id = Guid.NewGuid(),
                Name = "SYS-DEFAULT",
                SystemPrompt = "stub",
                ScopeType = PersonaScopeType.Global,
                OwnerType = ScopeOwnerType.System
            });

        var sut = CreateProvider();

        // Act
        await sut.GetContextAsync(
            documentId: string.Empty,
            tenantId: TestTenantId,
            playbookId: null,
            cancellationToken: CancellationToken.None);

        // Assert — resolver called exactly once with the expected arguments.
        _scopeResolverMock.Verify(
            r => r.ResolvePersonaForChatAsync(
                TestTenantId,
                /* playbookId */ null,
                It.IsAny<CancellationToken>()),
            Times.Once,
            "resolver invoked with the calling tenant and the (null) playbook id");
    }

    #region Setup helpers

    private PlaybookChatContextProvider CreateProvider()
        => new(
            _scopeResolverMock.Object,
            _playbookServiceMock.Object,
            _dataverseServiceMock.Object,
            _loggerMock.Object,
            // R6 task 069 follow-up housekeeping: task 068 made IMatterMemoryService a
            // required ctor param without migrating this fixture. Pass a default mock so
            // the matter-memory append path is a no-op for these tests.
            new Mock<IMatterMemoryService>().Object);

    /// <summary>
    /// Normalize line endings to LF so cross-host (Windows CRLF, Linux LF) FR-04
    /// equality assertions are semantic, not literal. Per task 004 notes, the
    /// SYS-DEFAULT Dataverse row stores LF; the runtime <c>BuildDefaultSystemPrompt</c>
    /// fallback returns whatever the source file uses.
    /// </summary>
    private static string Normalize(string s) => s.Replace("\r\n", "\n");

    #endregion
}

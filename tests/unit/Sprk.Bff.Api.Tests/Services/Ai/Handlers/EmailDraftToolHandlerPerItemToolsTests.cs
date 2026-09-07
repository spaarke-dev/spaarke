using System.IO;
using System.Text.Json;
using FluentAssertions;
using Moq;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Handlers;

/// <summary>
/// Unit tests for the per-item Email tools added to <see cref="EmailDraftToolHandler"/>
/// (spaarkeai-assistant-enhancements-r3 task 023, FR-09): <c>draft_reply</c> / <c>draft_forward</c>
/// (auto-draft the authored body TEXT) and <c>summarize_thread</c> (plain narrative in chat).
/// </summary>
/// <remarks>
/// <para>
/// The tools EXTEND the existing handler (§11 reuse-first / NFR-06 — not a new handler family) via
/// the <c>sprk_configuration.method</c> discriminator, mirroring the TextRefinementHandler multi-row
/// precedent. Boundaries mocked at the module edge (per ADR-038): <see cref="IDataverseUserClient"/>
/// (the OBO Dataverse read) and <see cref="IEmailDraftAi"/> (the PublicContracts AI facade).
/// </para>
/// <para>
/// Load-bearing groups: (1) the draft output is the AUTHORED BODY TEXT ONLY and never embeds the
/// quoted thread (FR-10 invariant boundary — the client owns thread preservation); (2)
/// summarize_thread returns a PLAIN NARRATIVE identical in shape to file-summarize, opening no
/// composer; (3) content is fetched by communicationId OVER OBO (fail-closed — a user who cannot read
/// the email fails with their own access error, and no AI call is made).
/// </para>
/// </remarks>
public sealed class EmailDraftToolHandlerPerItemToolsTests : TypedToolHandlerTestFixture
{
    private readonly Mock<IDataverseUserClient> _dataverse = new();
    private readonly Mock<IEmailDraftAi> _emailDraftAi = new();

    private EmailDraftToolHandler CreateHandler() =>
        new(_dataverse.Object, _emailDraftAi.Object,
            Sprk.Bff.Api.Tests.TestInfrastructure.CoreAncestorResolverFixtures.Inert(),
            CreateLogger<EmailDraftToolHandler>());

    private static AnalysisTool BuildTool(string method, string name) =>
        BuildAnalysisTool(
            handlerClass: nameof(EmailDraftToolHandler),
            configuration: $$"""{"method":"{{method}}"}""",
            name: name);

    private static JsonElement ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static readonly Guid CommunicationId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid EmlDocumentId = Guid.Parse("99999999-8888-7777-6666-555555555555");

    /// <summary>Source email body that INCLUDES a quoted thread — used to prove the draft never embeds it.</summary>
    private const string SourceBodyWithQuotedThread =
        "Please review the attached MSA before Friday.\n\n" +
        "----- Original Message -----\n" +
        "From: opposing.counsel@example.com\n" +
        "Older thread content the composer preserves separately.";

    private static string ArgsFor(Guid id, string? mode = null) => mode is null
        ? $$"""{"communicationId":"{{id:D}}"}"""
        : $$"""{"communicationId":"{{id:D}}","mode":"{{mode}}"}""";

    /// <summary>Sets up the OBO Dataverse reads: the communication record + the archived-.eml lookup.</summary>
    private void SetupCommunicationRead(string body, int bodyFormat = 100000000, string subject = "Acme MSA")
    {
        _dataverse
            .Setup(d => d.GetAsync(
                It.Is<string>(p => p.StartsWith("sprk_communications(")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson(
                JsonSerializer.Serialize(new
                {
                    sprk_subject = subject,
                    sprk_body = body,
                    sprk_bodyformat = bodyFormat
                }))));

        _dataverse
            .Setup(d => d.GetAsync(
                It.Is<string>(p => p.StartsWith("sprk_documents?")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson(
                $$"""{ "value": [ { "sprk_documentid": "{{EmlDocumentId:D}}" } ] }""")));
    }

    private void SetupAiDraft(string result, Action<EmailDraftAiRequest>? capture = null) =>
        _emailDraftAi
            .Setup(a => a.DraftAsync(It.IsAny<EmailDraftAiRequest>(), It.IsAny<CancellationToken>()))
            .Callback<EmailDraftAiRequest, CancellationToken>((req, _) => capture?.Invoke(req))
            .ReturnsAsync(result);

    private static JsonElement DataOf(ToolResult result)
    {
        var json = JsonSerializer.Serialize(result.Data);
        return ParseJson(json);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // draft_reply / draft_forward — auto-generate the authored body TEXT (over OBO)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_DraftReply_ProducesAuthoredReplyBodyFromThreadOverObo()
    {
        SetupCommunicationRead(SourceBodyWithQuotedThread);
        EmailDraftAiRequest? captured = null;
        const string replyText = "Thank you — I will review the MSA and respond by Friday.";
        SetupAiDraft(replyText, req => captured = req);

        var result = await CreateHandler().ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: ArgsFor(CommunicationId, mode: "replyAll")),
            BuildTool("reply", "SYS-Email Reply Draft"),
            CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var data = DataOf(result);
        data.GetProperty("tool").GetString().Should().Be("email.draft_reply");
        data.GetProperty("communicationId").GetString().Should().Be(CommunicationId.ToString("D"));
        data.GetProperty("mode").GetString().Should().Be("replyAll");
        data.GetProperty("draft").GetString().Should().Be(replyText, "the tool returns exactly the facade-authored body");
        data.GetProperty("emlDocumentId").GetString().Should().Be(EmlDocumentId.ToString("D"), "the archived .eml id is echoed for grounding");

        // AI generation goes through the PublicContracts facade with the reply preset over the OBO-read body.
        captured.Should().NotBeNull();
        captured!.Intent.Should().Be("reply");
        captured.CurrentBody.Should().Be(SourceBodyWithQuotedThread);

        // OBO: the source email was read through IDataverseUserClient (the user's token).
        _dataverse.Verify(
            d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_communications(")), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteChatAsync_DraftReply_DraftIsAuthoredBodyOnly_NeverEmbedsQuotedThread()
    {
        // FR-10 invariant boundary: the tool returns ONLY the authored region. It must not append,
        // quote, or duplicate the source thread — task 024's bodyOverride composes it above the
        // reducer-derived quotedThread, which the composer appends. The handler owns none of that.
        SetupCommunicationRead(SourceBodyWithQuotedThread);
        const string cleanReply = "Thanks — I'll circulate comments on the MSA shortly.";
        SetupAiDraft(cleanReply);

        var result = await CreateHandler().ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: ArgsFor(CommunicationId)),
            BuildTool("reply", "SYS-Email Reply Draft"),
            CancellationToken.None);

        var draft = DataOf(result).GetProperty("draft").GetString();
        draft.Should().Be(cleanReply);
        draft.Should().NotContain("----- Original Message -----",
            "the draft must not embed the quoted thread — the composer preserves it (FR-10)");
        draft.Should().NotContain("Older thread content",
            "the draft must not duplicate any of the source thread body");
    }

    [Fact]
    public async Task ExecuteChatAsync_DraftForward_ProducesForwardNote_ViaCustomFacadeIntent()
    {
        SetupCommunicationRead(SourceBodyWithQuotedThread, bodyFormat: 100000001 /* HTML */);
        EmailDraftAiRequest? captured = null;
        const string note = "Sharing the MSA below for your review.";
        SetupAiDraft(note, req => captured = req);

        var result = await CreateHandler().ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: ArgsFor(CommunicationId)),
            BuildTool("forward", "SYS-Email Forward Draft"),
            CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var data = DataOf(result);
        data.GetProperty("tool").GetString().Should().Be("email.draft_forward");
        data.GetProperty("draft").GetString().Should().Be(note);
        data.GetProperty("draft").GetString().Should().NotContain("Older thread content",
            "the forward note must not restate the forwarded thread (FR-10)");

        captured.Should().NotBeNull();
        captured!.Intent.Should().Be("custom", "forward uses the facade's custom intent with a server-authored forwarding-note instruction");
        captured.UserInstruction.Should().NotBeNullOrWhiteSpace();
        captured.IsHtml.Should().BeTrue("the draft follows the source email's HTML body format so the composer renders it cleanly");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // summarize_thread — plain narrative in chat, identical to file-summarize
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_SummarizeThread_ReturnsPlainNarrative_NotStructured_AndOpensNoComposer()
    {
        SetupCommunicationRead(SourceBodyWithQuotedThread);
        EmailDraftAiRequest? captured = null;
        const string narrative = "The email asks the recipient to review the attached MSA before Friday and references an earlier exchange.";
        SetupAiDraft(narrative, req => captured = req);

        var result = await CreateHandler().ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: ArgsFor(CommunicationId)),
            BuildTool("summarize_thread", "SYS-Email Thread Summary"),
            CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var data = DataOf(result);
        data.GetProperty("tool").GetString().Should().Be("email.summarize_thread");
        data.GetProperty("text").GetString().Should().Be(narrative,
            "the summary is a plain narrative in a 'text' field — identical in shape to file-summarize");

        // NEGATIVE: no bespoke structured thread format (no sections object, no draft/composer field).
        data.TryGetProperty("sections", out _).Should().BeFalse("summarize_thread must NOT emit a structured sections object");
        data.TryGetProperty("draft", out _).Should().BeFalse("summarize_thread answers in chat — it drafts nothing");

        // The facade's plain-text summarise preset is used (IsHtml=false → plain narrative out).
        captured.Should().NotBeNull();
        captured!.Intent.Should().Be("summarize");
        captured.IsHtml.Should().BeFalse();

        // Opens NO composer: no widget/created-record metadata, and no Dataverse write of any kind.
        (result.Metadata is null || !result.Metadata.ContainsKey(ToolResultMetadataKeys.Widget))
            .Should().BeTrue("summarize_thread emits no widget/composer surface");
        _dataverse.Verify(d => d.PostAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        _dataverse.Verify(d => d.PatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // OBO scoping — fail-closed, no AI call when the user cannot read the email
    // ═════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("reply")]
    [InlineData("forward")]
    [InlineData("summarize_thread")]
    public async Task ExecuteChatAsync_UserCannotReadCommunication_SurfacesOwnAccessError_AndCallsNoAi(string method)
    {
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_communications(")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(403, DataverseUserClientErrorCodes.AccessDenied,
                "The user cannot read this communication."));

        var result = await CreateHandler().ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: ArgsFor(CommunicationId)),
            BuildTool(method, "SYS-Email Tool"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied,
            "content is fetched over OBO — a privilege-denied read surfaces the user's own access error (no app-only escalation)");
        _emailDraftAi.Verify(a => a.DraftAsync(It.IsAny<EmailDraftAiRequest>(), It.IsAny<CancellationToken>()), Times.Never,
            "no content was obtained over OBO, so no email content ever reaches the AI facade");
    }

    [Theory]
    [InlineData("reply")]
    [InlineData("summarize_thread")]
    public async Task ExecuteChatAsync_AiUnavailable_ReturnsDependencyUnavailable(string method)
    {
        SetupCommunicationRead(SourceBodyWithQuotedThread);
        // NullEmailDraftAi (AzureOpenAI gate off) returns null.
        _emailDraftAi
            .Setup(a => a.DraftAsync(It.IsAny<EmailDraftAiRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await CreateHandler().ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: ArgsFor(CommunicationId)),
            BuildTool(method, "SYS-Email Tool"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.DependencyUnavailable);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Argument validation (closed by-id vocabulary — no email content in args)
    // ═════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("reply", "{}", "*communicationId*")]
    [InlineData("forward", """{"communicationId":"not-a-guid"}""", "*GUID*")]
    [InlineData("summarize_thread", """{"communicationId":""}""", "*communicationId*")]
    [InlineData("reply", """{"communicationId":"11111111-2222-3333-4444-555555555555","mode":"bogus"}""", "*mode*")]
    public void ValidateChat_InvalidArgs_Fails(string method, string argsJson, string expectedPattern)
    {
        var result = CreateHandler().ValidateChat(
            BuildChatInvocationContext(toolArgumentsJson: argsJson),
            BuildTool(method, "SYS-Email Tool"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainMatch(expectedPattern);
    }

    [Fact]
    public void ValidateChat_ValidByIdArgs_Succeeds()
    {
        CreateHandler().ValidateChat(
            BuildChatInvocationContext(toolArgumentsJson: ArgsFor(CommunicationId)),
            BuildTool("summarize_thread", "SYS-Email Thread Summary"))
            .IsValid.Should().BeTrue();
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ADR-015 telemetry — counts + ids only, never subject/body/draft/summary text
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_Telemetry_NeverLogsSubjectBodyOrDraftText()
    {
        SetupCommunicationRead(SourceBodyWithQuotedThread, subject: "Confidential MSA subject line");
        const string narrative = "A confidential narrative summary that must never appear in application logs.";
        SetupAiDraft(narrative);

        await CreateHandler().ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: ArgsFor(CommunicationId)),
            BuildTool("summarize_thread", "SYS-Email Thread Summary"),
            CancellationToken.None);

        AssertTelemetryRespectsAdr015(
            "Confidential MSA subject line",
            SourceBodyWithQuotedThread,
            narrative);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Catalog rows / §11 extension — the three tools EXTEND the handler (no new family)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void CatalogRows_ThreeNewEmailTools_AllRouteToEmailDraftToolHandler_ViaMethodDiscriminator()
    {
        var dir = Path.Combine(FindRepoRoot(), "infra", "dataverse");

        var expected = new (string File, string ToolId, string Method)[]
        {
            ("sprk_analysistool-email-draft-reply-row.json", "email.draft_reply", "reply"),
            ("sprk_analysistool-email-draft-forward-row.json", "email.draft_forward", "forward"),
            ("sprk_analysistool-email-summarize-thread-row.json", "email.summarize_thread", "summarize_thread"),
        };

        foreach (var (file, toolId, method) in expected)
        {
            var path = Path.Combine(dir, file);
            File.Exists(path).Should().BeTrue($"the {toolId} catalog row must exist at infra/dataverse/{file}");

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;

            root.GetProperty("sprk_handlerclass").GetString().Should().Be(
                nameof(EmailDraftToolHandler),
                "§11 / NFR-06: the per-item tools EXTEND EmailDraftToolHandler — they are NOT a new handler family");
            root.GetProperty("sprk_toolid").GetString().Should().Be(toolId);
            root.GetProperty("sprk_configuration").GetProperty("method").GetString().Should().Be(
                method, "the row's method discriminator routes dispatch within the shared handler");
            root.GetProperty("sprk_availableincontexts").GetInt32().Should().Be(100000001, "Chat only (agent loop)");
            root.GetProperty("sprk_sideeffectclass").GetInt32().Should().Be(100000000, "Read — no record write, no send, no gate");
            root.GetProperty("sprk_description").GetString().Should().NotBeNullOrWhiteSpace(
                "ADR-039 closed-catalog rows carry an authored LLM-facing description");
        }
    }

    [Fact]
    public void CatalogRows_AllEmailDraftHandlerRows_ShareOneHandlerClass_ProvingMultiRowExtension()
    {
        var dir = Path.Combine(FindRepoRoot(), "infra", "dataverse");
        var emailDraftRows = Directory
            .EnumerateFiles(dir, "sprk_analysistool-email-*-row.json")
            .Select(f => JsonDocument.Parse(File.ReadAllText(f)))
            .Select(d => d.RootElement.TryGetProperty("sprk_handlerclass", out var hc) ? hc.GetString() : null)
            .ToList();

        emailDraftRows.Should().HaveCountGreaterThanOrEqualTo(4,
            "the email.draft row plus the three task-023 rows all live on EmailDraftToolHandler");
        emailDraftRows.Should().OnlyContain(hc => hc == nameof(EmailDraftToolHandler),
            "every email.* tool routes to the single EmailDraftToolHandler class (extension, not a new family)");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Spaarke.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate the repo root (Spaarke.sln).");
    }
}

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Sprk.Bff.Api.Api.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Unit tests for the attachments[] payload validation and composition helpers
/// added by task 050 to <see cref="ChatEndpoints"/>.
///
/// Tests cover (FR-07 / NFR-04 — spaarke-ai-platform-unification-r3 task 051):
///   • Validation rule 1 — max 5 attachments (6 → 400 ProblemDetails)
///   • Validation rule 2 — MIME allow-list (image/png → 400)
///   • Validation rule 3 — per-file textContent cap (2.5M chars → 400)
///   • Validation rule 4 — sum-of-all textContent cap (5M chars total → 400)
///   • Happy paths — 1, 3, 5 attachments accepted (returns null)
///   • Backward compat — null / empty attachments returns null (pre-026 client behavior)
///   • Composition — ComposeMessageWithAttachments preserves user message verbatim when no
///     attachments, and emits the expected structured prefix format when present
///   • RFC 7807 ProblemDetails shape (title / status / detail) per ADR-019
///
/// IMPLEMENTATION NOTE — Why reflection?
///   ValidateAttachments and ComposeMessageWithAttachments are private static helpers on
///   ChatEndpoints. The class is public and the test assembly already has
///   InternalsVisibleTo, but private members are not reachable directly. Reflection lets
///   us exercise the rules in isolation without spinning up a WebApplicationFactory and
///   without modifying production code (task 050 owns ChatEndpoints.cs; task 051 must not
///   touch it per the POML constraint).
///
///   The constants (MaxAttachmentsPerMessage, MaxAttachmentTextCharsPerFile,
///   MaxAttachmentTextCharsTotal) are internal and reachable directly via
///   InternalsVisibleTo — we use the symbols, not magic numbers, so changes to caps will
///   keep the tests honest.
///
/// @see Sprk.Bff.Api.Api.Ai.ChatEndpoints.ValidateAttachments
/// @see Sprk.Bff.Api.Api.Ai.ChatEndpoints.ComposeMessageWithAttachments
/// @see Sprk.Bff.Api.Api.Ai.ChatMessageAttachment
/// @see ADR-019 (ProblemDetails)
/// @see ADR-013 (AI in-process extension — single LLM call per turn)
/// @see project spaarke-ai-platform-unification-r3, task 050 (production code) and 051 (tests).
/// </summary>
public class ChatEndpointsAttachmentsTests
{
    // ---------------------------------------------------------------------
    // Reflection helpers — bind once at type init.
    // ---------------------------------------------------------------------

    private static readonly MethodInfo ValidateAttachmentsMethod =
        typeof(ChatEndpoints).GetMethod(
            "ValidateAttachments",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException(
            nameof(ChatEndpoints), "ValidateAttachments");

    private static readonly MethodInfo ComposeMessageMethod =
        typeof(ChatEndpoints).GetMethod(
            "ComposeMessageWithAttachments",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException(
            nameof(ChatEndpoints), "ComposeMessageWithAttachments");

    /// <summary>Invokes ValidateAttachments and returns the boxed (int, object)? tuple.</summary>
    private static object? InvokeValidate(IReadOnlyList<ChatMessageAttachment>? attachments)
    {
        return ValidateAttachmentsMethod.Invoke(null, [attachments]);
    }

    /// <summary>Invokes ComposeMessageWithAttachments and returns the composed string.</summary>
    private static string InvokeCompose(
        string message,
        IReadOnlyList<ChatMessageAttachment>? attachments)
    {
        return (string)ComposeMessageMethod.Invoke(null, [message, attachments])!;
    }

    /// <summary>
    /// Asserts that the validation result is a (int, object)? rejection with the expected
    /// HTTP status, and returns the RFC 7807 ProblemDetails payload for further assertions.
    /// </summary>
    private static object ExpectRejection(object? validationResult, int expectedStatus)
    {
        validationResult.Should().NotBeNull(
            "validation must return a non-null (statusCode, payload) tuple when rejecting");

        // The boxed nullable tuple is unwrapped to a ValueTuple<int, object> at runtime.
        var tupleType = validationResult!.GetType();
        var statusCode = (int)tupleType.GetField("Item1")!.GetValue(validationResult)!;
        var payload = tupleType.GetField("Item2")!.GetValue(validationResult)!;

        statusCode.Should().Be(expectedStatus,
            "validation rejections must surface as the expected HTTP status");

        return payload;
    }

    /// <summary>Reads a property by name from an anonymous-typed ProblemDetails payload.</summary>
    private static T GetProperty<T>(object payload, string name)
    {
        var prop = payload.GetType().GetProperty(name);
        prop.Should().NotBeNull($"ProblemDetails payload must include '{name}' per RFC 7807 / ADR-019");
        return (T)prop!.GetValue(payload)!;
    }

    /// <summary>
    /// Asserts the standard RFC 7807 ProblemDetails shape (title, status, detail, type)
    /// per ADR-019. Returns the payload for caller-specific assertions.
    /// </summary>
    private static (string title, int status, string detail) AssertProblemDetailsShape(object payload, int expectedStatus)
    {
        var title = GetProperty<string>(payload, "title");
        var status = GetProperty<int>(payload, "status");
        var detail = GetProperty<string>(payload, "detail");
        var type = GetProperty<string>(payload, "type");

        title.Should().NotBeNullOrWhiteSpace("ADR-019: title required");
        status.Should().Be(expectedStatus, "ADR-019: status must match HTTP code");
        detail.Should().NotBeNullOrWhiteSpace("ADR-019: detail required");
        type.Should().NotBeNullOrWhiteSpace("RFC 7807: type URI required");

        return (title, status, detail);
    }

    // ---------------------------------------------------------------------
    // Test data builders
    // ---------------------------------------------------------------------

    private static ChatMessageAttachment Attachment(
        string filename = "note.txt",
        string contentType = "text/plain",
        string textContent = "hello world")
    {
        return new ChatMessageAttachment(filename, contentType, textContent);
    }

    private static IReadOnlyList<ChatMessageAttachment> NAttachments(int n, string contentType = "text/plain")
    {
        return Enumerable.Range(1, n)
            .Select(i => Attachment(
                filename: $"file-{i}.txt",
                contentType: contentType,
                textContent: $"content-{i}"))
            .ToList();
    }

    // =====================================================================
    // 1. Happy-path acceptance tests (1 / 3 / 5 attachments + empty/null)
    // =====================================================================

    [Fact]
    public void ValidateAttachments_NullList_ReturnsNullAccept()
    {
        // Backward compat: clients that omit the attachments field (pre-026) must continue to work.
        var result = InvokeValidate(null);

        result.Should().BeNull("null attachments must be accepted (pre-026 backward compat per FR-07)");
    }

    [Fact]
    public void ValidateAttachments_EmptyList_ReturnsNullAccept()
    {
        var result = InvokeValidate(new List<ChatMessageAttachment>());

        result.Should().BeNull("empty attachments list must be accepted (identical to null)");
    }

    [Fact]
    public void ValidateAttachments_OneAttachment_ReturnsNullAccept()
    {
        var result = InvokeValidate(NAttachments(1));

        result.Should().BeNull("1 attachment is within the max-5 cap and must be accepted");
    }

    [Fact]
    public void ValidateAttachments_ThreeAttachments_ReturnsNullAccept()
    {
        var result = InvokeValidate(NAttachments(3));

        result.Should().BeNull("3 attachments is within the max-5 cap and must be accepted");
    }

    [Fact]
    public void ValidateAttachments_FiveAttachments_BoundaryAccepted()
    {
        // Boundary: exactly MaxAttachmentsPerMessage must be accepted.
        var result = InvokeValidate(NAttachments(ChatEndpoints.MaxAttachmentsPerMessage));

        result.Should().BeNull(
            "exactly MaxAttachmentsPerMessage (5) attachments must be accepted (boundary == cap)");
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("text/markdown")]
    [InlineData("application/pdf")]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    public void ValidateAttachments_AllowedMimeTypes_Accepted(string contentType)
    {
        var result = InvokeValidate([Attachment(contentType: contentType)]);

        result.Should().BeNull($"contentType '{contentType}' is in the allow-list and must be accepted");
    }

    // =====================================================================
    // 2. Rejection — too many attachments (>5 → 400 ProblemDetails)
    // =====================================================================

    [Fact]
    public void ValidateAttachments_SixAttachments_Returns400ProblemDetails()
    {
        // 6 = MaxAttachmentsPerMessage + 1 — first reject beyond cap.
        var result = InvokeValidate(NAttachments(ChatEndpoints.MaxAttachmentsPerMessage + 1));

        var payload = ExpectRejection(result, expectedStatus: 400);
        var (title, _, detail) = AssertProblemDetailsShape(payload, expectedStatus: 400);

        title.Should().Be("Too many attachments",
            "the 6-attachment rejection must surface a clear title");
        detail.Should().Contain(ChatEndpoints.MaxAttachmentsPerMessage.ToString(),
            "detail should cite the configured max (5) so callers can fix the request");
        detail.Should().Contain("6",
            "detail should cite the actual count received so callers can diagnose");
    }

    // =====================================================================
    // 3. Rejection — disallowed MIME type → 400 ProblemDetails
    // =====================================================================

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("application/zip")]
    [InlineData("application/octet-stream")]
    [InlineData("text/html")]
    public void ValidateAttachments_DisallowedMime_Returns400ProblemDetails(string disallowedContentType)
    {
        var result = InvokeValidate([Attachment(contentType: disallowedContentType)]);

        var payload = ExpectRejection(result, expectedStatus: 400);
        var (title, _, detail) = AssertProblemDetailsShape(payload, expectedStatus: 400);

        title.Should().Be("Unsupported attachment content type",
            "the MIME-rejection must surface a clear title");
        detail.Should().Contain(disallowedContentType,
            "detail should cite the offending contentType so callers can diagnose");
    }

    // =====================================================================
    // 4. Rejection — per-file textContent length cap (>2.5M chars → 400)
    // =====================================================================

    [Fact]
    public void ValidateAttachments_PerFileTextContentOverCap_Returns400ProblemDetails()
    {
        // One char beyond the per-file cap should reject.
        var oversized = new string('x', ChatEndpoints.MaxAttachmentTextCharsPerFile + 1);
        var result = InvokeValidate([Attachment(textContent: oversized)]);

        var payload = ExpectRejection(result, expectedStatus: 400);
        var (title, _, detail) = AssertProblemDetailsShape(payload, expectedStatus: 400);

        title.Should().Be("Attachment too large",
            "the per-file size-cap rejection must surface a clear title");
        detail.Should().Contain(ChatEndpoints.MaxAttachmentTextCharsPerFile.ToString(),
            "detail should cite the per-file cap so callers can fix the upload");
    }

    [Fact]
    public void ValidateAttachments_PerFileTextContentAtCap_Accepted()
    {
        // Boundary: exactly the per-file cap must be accepted (strict ">", not ">=").
        var atCap = new string('x', ChatEndpoints.MaxAttachmentTextCharsPerFile);
        var result = InvokeValidate([Attachment(textContent: atCap)]);

        result.Should().BeNull(
            "exactly MaxAttachmentTextCharsPerFile characters must be accepted (boundary == cap)");
    }

    // =====================================================================
    // 5. Rejection — total textContent length cap (sum >5M chars → 400)
    // =====================================================================

    [Fact]
    public void ValidateAttachments_FiveAttachmentsTotalOverCap_Returns400ProblemDetails()
    {
        // Build 5 attachments, each just under the per-file cap, whose SUM exceeds the total cap.
        // perFileCap = 2_500_000 → 3 attachments at perFileCap = 7_500_000 > 5_000_000 totalCap.
        // We use 3 attachments at per-file cap to trip the SUM rule cleanly. The first two pass;
        // the third pushes total past MaxAttachmentTextCharsTotal.
        var perFile = new string('x', ChatEndpoints.MaxAttachmentTextCharsPerFile);
        var attachments = new List<ChatMessageAttachment>
        {
            Attachment(textContent: perFile, filename: "f1.txt"),
            Attachment(textContent: perFile, filename: "f2.txt"),
            Attachment(textContent: perFile, filename: "f3.txt"), // sum exceeds 5M here
        };

        // Sanity: 3 * 2.5M = 7.5M > 5M total cap.
        attachments.Sum(a => (long)a.TextContent.Length)
            .Should().BeGreaterThan(ChatEndpoints.MaxAttachmentTextCharsTotal,
                "test fixture must exceed the total-text cap to exercise rule 4");

        var result = InvokeValidate(attachments);

        var payload = ExpectRejection(result, expectedStatus: 400);
        var (title, _, detail) = AssertProblemDetailsShape(payload, expectedStatus: 400);

        title.Should().Be("Attachments exceed total size limit",
            "the sum-of-text rejection must surface a clear title");
        detail.Should().Contain(ChatEndpoints.MaxAttachmentTextCharsTotal.ToString(),
            "detail should cite the configured total cap (5,000,000) so callers can fix the request");
    }

    // =====================================================================
    // 6. Rejection — defensive null/missing fields → 400 ProblemDetails
    // =====================================================================

    [Fact]
    public void ValidateAttachments_AttachmentWithNullContentType_Returns400()
    {
        // Direct record construction won't allow null on string fields; simulate the
        // deserializer edge case by using null! to bypass the nullability check.
        var attachments = new List<ChatMessageAttachment>
        {
            new("file.txt", null!, "content"),
        };

        var result = InvokeValidate(attachments);

        var payload = ExpectRejection(result, expectedStatus: 400);
        var (title, _, _) = AssertProblemDetailsShape(payload, expectedStatus: 400);

        title.Should().Be("Invalid attachment",
            "missing required fields surfaces an 'Invalid attachment' title");
    }

    // =====================================================================
    // 7. Composition — ComposeMessageWithAttachments
    // =====================================================================

    [Fact]
    public void ComposeMessage_NoAttachments_ReturnsOriginalMessageVerbatim()
    {
        // Backward compat: pre-026 clients (or messages with no files) must see the original message bytes.
        const string original = "Summarize the contract obligations.";

        var composed = InvokeCompose(original, null);

        composed.Should().Be(original,
            "no attachments → composition must return the message verbatim (zero overhead, backward compatible)");
    }

    [Fact]
    public void ComposeMessage_EmptyAttachmentList_ReturnsOriginalMessageVerbatim()
    {
        const string original = "What are the key risks?";

        var composed = InvokeCompose(original, new List<ChatMessageAttachment>());

        composed.Should().Be(original,
            "empty attachments list → composition behaves identically to null (FR-07 backward compat)");
    }

    [Fact]
    public void ComposeMessage_WithSingleAttachment_PrefixesUserMessage_ThenAppendsBlock()
    {
        const string userMsg = "Explain this clause.";
        var attachments = new List<ChatMessageAttachment>
        {
            new("contract-a.pdf", "application/pdf", "FORCE MAJEURE: parties excused on act of god."),
        };

        var composed = InvokeCompose(userMsg, attachments);

        composed.Should().NotBe(userMsg, "with attachments, composition must differ from the original message");
        composed.Should().StartWith("User message: " + userMsg,
            "composed output starts with the labeled user message so it remains the dominant signal");
        composed.Should().Contain("[Attached files: contract-a.pdf]",
            "composed output includes the attachment-list header for the LLM");
        composed.Should().Contain("--- Attachment: contract-a.pdf (application/pdf) ---",
            "composed output includes structured block headers per file");
        composed.Should().Contain("FORCE MAJEURE: parties excused on act of god.",
            "composed output includes the extracted textContent");
    }

    [Fact]
    public void ComposeMessage_MultipleAttachments_AppendsAllInOrder()
    {
        const string userMsg = "Compare these.";
        var attachments = new List<ChatMessageAttachment>
        {
            new("a.txt", "text/plain", "AAA-text"),
            new("b.md", "text/markdown", "BBB-text"),
            new("c.pdf", "application/pdf", "CCC-text"),
        };

        var composed = InvokeCompose(userMsg, attachments);

        composed.Should().Contain("[Attached files: a.txt, b.md, c.pdf]",
            "all filenames appear in the header in submitted order");
        composed.Should().Contain("--- Attachment: a.txt (text/plain) ---");
        composed.Should().Contain("--- Attachment: b.md (text/markdown) ---");
        composed.Should().Contain("--- Attachment: c.pdf (application/pdf) ---");
        composed.Should().Contain("AAA-text");
        composed.Should().Contain("BBB-text");
        composed.Should().Contain("CCC-text");

        // Order check — A's block must precede B's, which must precede C's.
        var aIdx = composed.IndexOf("--- Attachment: a.txt", System.StringComparison.Ordinal);
        var bIdx = composed.IndexOf("--- Attachment: b.md", System.StringComparison.Ordinal);
        var cIdx = composed.IndexOf("--- Attachment: c.pdf", System.StringComparison.Ordinal);
        aIdx.Should().BeLessThan(bIdx, "attachment blocks must appear in submission order");
        bIdx.Should().BeLessThan(cIdx, "attachment blocks must appear in submission order");
    }

    // =====================================================================
    // 8. Invariant assertions — constants and DTO shape
    // =====================================================================

    [Fact]
    public void ChatEndpoints_Constants_MatchFr07Nfr04Spec()
    {
        // These constants are part of the FR-07 / NFR-04 contract. Changing them is a
        // breaking change for clients and contract tests; this assertion locks the values.
        ChatEndpoints.MaxAttachmentsPerMessage.Should().Be(5,
            "FR-07 caps attachments at 5 per message");
        ChatEndpoints.MaxAttachmentTextCharsPerFile.Should().Be(2_500_000,
            "NFR-04 per-file cap is 2.5M chars (~10 MB UTF-16)");
        ChatEndpoints.MaxAttachmentTextCharsTotal.Should().Be(5_000_000,
            "NFR-04 sum-of-all cap is 5M chars to bound the LLM prompt");
    }

    [Fact]
    public void ChatMessageAttachment_DtoShape_HasExpectedFields()
    {
        // Lock the record shape — the client serializes against these exact property names.
        // A rename here is a wire-format break.
        var sample = new ChatMessageAttachment("name.txt", "text/plain", "body");

        sample.Filename.Should().Be("name.txt");
        sample.ContentType.Should().Be("text/plain");
        sample.TextContent.Should().Be("body");
    }

    [Fact]
    public void ChatSendMessageRequest_DefaultAttachments_IsNull_ForBackwardCompat()
    {
        // FR-07 explicit: pre-026 clients that don't send `attachments` see identical behavior.
        // The record's default parameter must therefore be null.
        var request = new ChatSendMessageRequest("hello");

        request.Attachments.Should().BeNull(
            "default Attachments must be null so pre-026 clients see identical pre-FR-07 behavior");
        request.DocumentId.Should().BeNull(
            "default DocumentId remains null (unrelated invariant — sanity check)");
        request.Message.Should().Be("hello");
    }
}

using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.PublicContracts;

/// <summary>
/// Unit tests for <see cref="ConsumerRoutingService.FilterByContextType"/> — the FR-B3/B5
/// (spaarkeai-assistant-enhancements-r2 task 022) deterministic context-type pre-filter that scopes the
/// proactive-suggestion candidate set to the focused tab's context. This is the ONLY ADR-039-permitted
/// aid for the suggestion path (context scoping over the closed catalog), so its contract —
/// "keep a binding when its tags contain the context type OR are empty (=any); a blank context type
/// narrows nothing" — is regression-protected here. The last section covers the <c>requestType</c>
/// match key of <see cref="ConsumerRoutingService.TryMatchConditions"/> (spaarke-intake-pipeline-r1 FR-08).
/// </summary>
public sealed class ConsumerRoutingServiceContextFilterTests
{
    private static Binding BindingWithTags(params string[] contextTypeTags) => new()
    {
        BindingId = Guid.NewGuid(),
        ConsumerType = "chat-summarize",
        ToolDescription = "does a thing",
        ContextTypeTags = contextTypeTags,
    };

    [Fact]
    public void FilterByContextType_KeepsBindingsWhoseTagsContainTheContextType()
    {
        var match = BindingWithTags("document", "compose-doc");
        var noMatch = BindingWithTags("email");

        var result = ConsumerRoutingService.FilterByContextType(new[] { match, noMatch }, "document");

        result.Should().ContainSingle().Which.BindingId.Should().Be(match.BindingId);
    }

    [Fact]
    public void FilterByContextType_KeepsEmptyTagBindings_BecauseEmptyMeansAnyContext()
    {
        var anyContext = BindingWithTags(); // empty tags = relevant to ANY context
        var otherContext = BindingWithTags("email");

        var result = ConsumerRoutingService.FilterByContextType(new[] { anyContext, otherContext }, "document");

        result.Should().ContainSingle().Which.BindingId.Should().Be(anyContext.BindingId);
    }

    [Fact]
    public void FilterByContextType_MatchesTagsCaseInsensitively()
    {
        var match = BindingWithTags("Document");

        var result = ConsumerRoutingService.FilterByContextType(new[] { match }, "document");

        result.Should().ContainSingle().Which.BindingId.Should().Be(match.BindingId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FilterByContextType_WithBlankContextType_ReturnsAllCandidatesUnnarrowed(string? contextType)
    {
        var candidates = new[] { BindingWithTags("email"), BindingWithTags("document"), BindingWithTags() };

        var result = ConsumerRoutingService.FilterByContextType(candidates, contextType);

        result.Should().HaveCount(3);
    }

    [Fact]
    public void FilterByContextType_WhenNoBindingMatches_ReturnsEmpty()
    {
        var candidates = new[] { BindingWithTags("email"), BindingWithTags("calendar") };

        var result = ConsumerRoutingService.FilterByContextType(candidates, "matter-grid");

        result.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------------------------
    // FilterByContextTypes (spaarkeai-assistant-enhancements-r4 task 021a / FR-04) — the UNION
    // pre-filter for the conversational suggestion moment: keep a binding when its tags intersect ANY
    // of the supplied context types (the union of the open-tab context-types) OR are empty; an empty
    // type set narrows nothing. Same ADR-039-permitted deterministic aid, widened to the union.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void FilterByContextTypes_KeepsBindingsMatchingAnyOfTheSuppliedTypes()
    {
        var doc = BindingWithTags("document");
        var email = BindingWithTags("email");
        var calendarOnly = BindingWithTags("calendar");

        var result = ConsumerRoutingService.FilterByContextTypes(
            new[] { doc, email, calendarOnly }, new[] { "document", "email" });

        result.Select(b => b.BindingId).Should().BeEquivalentTo(new[] { doc.BindingId, email.BindingId });
    }

    [Fact]
    public void FilterByContextTypes_KeepsEmptyTagBindings_AndDropsNonMatching()
    {
        var anyContext = BindingWithTags();        // empty tags = relevant to ANY context
        var matching = BindingWithTags("email");
        var nonMatching = BindingWithTags("calendar");

        var result = ConsumerRoutingService.FilterByContextTypes(
            new[] { anyContext, matching, nonMatching }, new[] { "email" });

        result.Select(b => b.BindingId).Should().BeEquivalentTo(new[] { anyContext.BindingId, matching.BindingId });
    }

    [Fact]
    public void FilterByContextTypes_MatchesUnionCaseInsensitively()
    {
        var match = BindingWithTags("Document");

        var result = ConsumerRoutingService.FilterByContextTypes(new[] { match }, new[] { "document" });

        result.Should().ContainSingle().Which.BindingId.Should().Be(match.BindingId);
    }

    [Fact]
    public void FilterByContextTypes_WithEmptyTypeSet_ReturnsAllCandidatesUnnarrowed()
    {
        var candidates = new[] { BindingWithTags("email"), BindingWithTags("document"), BindingWithTags() };

        var result = ConsumerRoutingService.FilterByContextTypes(candidates, Array.Empty<string>());

        result.Should().HaveCount(3);
    }

    [Fact]
    public void FilterByContextTypes_WithNullTypeSet_ReturnsAllCandidatesUnnarrowed()
    {
        var candidates = new[] { BindingWithTags("email"), BindingWithTags() };

        var result = ConsumerRoutingService.FilterByContextTypes(candidates, contextTypes: null);

        result.Should().HaveCount(2);
    }

    [Fact]
    public void FilterByContextTypes_IgnoresBlankTypesInTheSet()
    {
        var email = BindingWithTags("email");
        var doc = BindingWithTags("document");

        // A set of only blank entries collapses to "no narrowing" (parity with FilterByContextType's blank passthrough).
        var result = ConsumerRoutingService.FilterByContextTypes(new[] { email, doc }, new[] { "  ", "" });

        result.Should().HaveCount(2);
    }

    // ---------------------------------------------------------------------------------------------
    // requestType match key (spaarke-intake-pipeline-r1 task 012 / FR-08): the request-pipeline routing
    // dimension on IRoutingContext. It is a default interface member returning null, so a context that
    // does not set it routes exactly as before (FR-08b) and a requestType condition fails closed for it.
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("nda", true)]
    [InlineData("NDA", true)]
    [InlineData("invoice", false)]
    public void TryMatchConditions_RequestTypeCondition_MatchesTheContextRequestTypeCaseInsensitively(
        string requestType, bool expected)
    {
        var context = new RoutingContext { RequestType = requestType };

        ConsumerRoutingService.TryMatchConditions("""{ "requestType": "nda" }""", context).Should().Be(expected);
    }

    [Fact]
    public void TryMatchConditions_RequestTypeInList_MatchesAnyListedType()
    {
        var context = new RoutingContext { RequestType = "trademark" };

        ConsumerRoutingService.TryMatchConditions("""{ "requestType": ["nda", "trademark"] }""", context)
            .Should().BeTrue();
    }

    [Fact]
    public void TryMatchConditions_RequestTypeAndDocumentType_RequireBothToMatch()
    {
        const string json = """{ "requestType": "nda", "documentType": "nda" }""";

        ConsumerRoutingService.TryMatchConditions(json, new RoutingContext { RequestType = "nda", DocumentType = "nda" })
            .Should().BeTrue();
        ConsumerRoutingService.TryMatchConditions(json, new RoutingContext { RequestType = "nda", DocumentType = "msa" })
            .Should().BeFalse("a non-NDA document on an nda request must not match the NDA row (FR-10)");
    }

    [Fact]
    public void TryMatchConditions_RequestTypeCondition_ContextWithoutRequestType_FailsClosed()
    {
        ConsumerRoutingService.TryMatchConditions("""{ "requestType": "nda" }""", new RoutingContext())
            .Should().BeFalse();
        ConsumerRoutingService.TryMatchConditions("""{ "requestType": "nda" }""", new MimeOnlyContext("application/pdf"))
            .Should().BeFalse("an IRoutingContext implementer that predates RequestType inherits the null default");
    }

    [Fact]
    public void TryMatchConditions_ExistingKeys_UnaffectedByASetRequestType()
    {
        var context = new RoutingContext { MimeType = "application/pdf", DocumentType = "nda", RequestType = "nda" };

        ConsumerRoutingService.TryMatchConditions("""{ "mimeType": "application/pdf", "documentType": "nda" }""", context)
            .Should().BeTrue();
        ConsumerRoutingService.TryMatchConditions("""{ "mimeType": "text/plain" }""", context)
            .Should().BeFalse();
    }

    /// <summary>An implementer written before RequestType existed: it does not declare the member.</summary>
    private sealed class MimeOnlyContext(string mimeType) : IRoutingContext
    {
        public string? MimeType { get; } = mimeType;

        public string? DocumentType => null;
    }
}

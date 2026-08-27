using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Api.SpeAdmin;
using Sprk.Bff.Api.Infrastructure.Graph;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.SpeAdmin;

/// <summary>
/// Unit tests for the Search Containers endpoint (SPE-057).
///
/// Strategy: Tests validate DTO structure, domain model records, endpoint registration shape,
/// and request/response contract. Graph SDK classes are sealed and cannot be mocked —
/// integration-level Graph Search behavior (live POST /search/query) is verified manually
/// against the dev environment. Unit tests cover the shape and mapping contract.
///
/// SPE-057: POST /api/spe/search/containers?configId={id}
/// </summary>
public class SearchContainersTests
{
    // =========================================================================
    // Validation Behaviour Tests (request contract)
    // =========================================================================

    // AMBIGUOUS (task 042): re-implements SearchContainersEndpoints' string.IsNullOrWhiteSpace(request.Query)
    // guard rather than exercising it, and is held only because no contract test covers the
    // empty-query→400 branch. /test-diet at task 090 should decide whether this stays.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void SearchContainersRequest_EmptyOrNullQuery_ShouldBeRejectedBy400(string? query)
    {
        // Arrange — the endpoint handler checks query before calling Graph
        // This test documents the expected validation: empty query → 400 Bad Request
        // The handler calls string.IsNullOrWhiteSpace(request.Query) and returns 400.
        var request = new SearchContainersEndpoints.SearchContainersRequest(
            Query: query!,
            PageSize: null,
            SkipToken: null);

        // Act & Assert — validate the query value is empty/null as the endpoint would detect
        var isInvalid = string.IsNullOrWhiteSpace(request.Query);
        isInvalid.Should().BeTrue("an empty or null query must be rejected with HTTP 400");
    }

    // =========================================================================
    // Pagination
    // =========================================================================
    //
    // REMOVED 2026-08-27 by task 042 — `SkipTokenEncoding_ProducesCorrectNextOffset` and
    // `SkipTokenDecoding_MalformedToken_DefaultsToZeroOffset`.
    //
    // Both pinned a NUMERIC from-offset skip-token scheme (`from + pageSize`, read back with
    // `int.TryParse`) and their comments claimed to "mirror the token decoding / encoding logic in
    // SearchContainersAsync". Production does not do that and has not for some time: it forwards
    // Graph's OPAQUE OData `$skiptoken` straight through
    // (`SpeAdminGraphService.SearchContainersAsync`, `&$skiptoken={Uri.EscapeDataString(skipToken)}`),
    // and the method's own remarks state "The previous numeric `from`-offset token has no meaning
    // here." There is no `int.TryParse` anywhere in it.
    //
    // They passed only because they never called production — they re-implemented the dead scheme
    // inside the test body and asserted against their own copy (ADR-038 §7 B6). Worse than useless:
    // a reader trusting them would learn a contract that no longer exists.
    //
    // Real coverage lives at the contract tier:
    //   SpeAdminSearchContractTests.SearchContainers_WhenGraphReturnsANextLink_SurfacesOnlyTheOpaqueSkipToken
    //   SpeAdminSearchContractTests.SearchContainers_WhenGraphReportsNoNextLink_ReportsNoNextPage
}

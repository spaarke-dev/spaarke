using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse.RecordCreation;

/// <summary>
/// The ONE refusal-to-ProblemDetails map of the unified create (field-mapping-server-write-path-r1 task 013, spec FR-12,
/// ADR-019): every <see cref="RecordCreationRefusalKind"/> has a status, every body carries <c>errorCode</c> and
/// <c>correlationId</c>, no upstream text reaches a body, and a missing record and one the caller cannot use answer the
/// same body. Pure mapping, no I/O — hence <c>tests/unit/domain/**</c> (ADR-038 §2 path 6).
/// </summary>
public class RecordCreationProblemMapTests
{
    private const string Correlation = "corr-013";
    private const string UpstreamSecret = "0x80040220 SecLib::RetrievePrivilegeForUser failed on server 10.0.0.7";

    /// <summary>Every member of the (internal) kind enum, by name — a member added later is a new row here.</summary>
    public static TheoryData<string> EveryKind()
    {
        var data = new TheoryData<string>();
        foreach (var kind in Enum.GetNames<RecordCreationRefusalKind>())
            data.Add(kind);
        return data;
    }

    [Theory]
    [MemberData(nameof(EveryKind))]
    public void ToProblemDetails_EveryKind_HasAStatus_AnErrorCode_AndTheCorrelationId(string kindName)
    {
        var kind = Enum.Parse<RecordCreationRefusalKind>(kindName);
        var problem = RecordCreationProblemMap.ToProblemDetails(Refusal(kind), "sprk_event", Correlation);

        problem.Status.Should().BeInRange(400, 599);
        problem.Extensions["errorCode"].Should().BeOfType<string>().Which.Should().NotBeNullOrWhiteSpace();
        problem.Extensions["correlationId"].Should().Be(Correlation);
        problem.Title.Should().NotBeNullOrWhiteSpace();
        problem.Detail.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void StatusOf_EveryKind_IsMapped_AndAnUnknownKindThrows()
    {
        foreach (var kind in Enum.GetValues<RecordCreationRefusalKind>())
        {
            var act = () => RecordCreationProblemMap.StatusOf(Refusal(kind));
            act.Should().NotThrow($"kind {kind} must have an HTTP mapping");
        }

        // Must-fire control: a value outside the enum (what a newly added member is to the map until it is mapped).
        var unknown = new RecordCreationRefusal((RecordCreationRefusalKind)999, "x.code", "detail");
        ((Action)(() => RecordCreationProblemMap.StatusOf(unknown))).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(nameof(RecordCreationRefusalKind.PrincipalUnresolved), StatusCodes.Status403Forbidden)]
    [InlineData(nameof(RecordCreationRefusalKind.BlockedColumn), StatusCodes.Status403Forbidden)]
    [InlineData(nameof(RecordCreationRefusalKind.Forbidden), StatusCodes.Status403Forbidden)]
    [InlineData(nameof(RecordCreationRefusalKind.NotFoundOrDenied), StatusCodes.Status404NotFound)]
    [InlineData(nameof(RecordCreationRefusalKind.Invalid), StatusCodes.Status400BadRequest)]
    [InlineData(nameof(RecordCreationRefusalKind.OwnerUnresolved), StatusCodes.Status409Conflict)]
    [InlineData(nameof(RecordCreationRefusalKind.Conflict), StatusCodes.Status409Conflict)]
    [InlineData(nameof(RecordCreationRefusalKind.Undecided), StatusCodes.Status500InternalServerError)]
    [InlineData(nameof(RecordCreationRefusalKind.Unavailable), StatusCodes.Status503ServiceUnavailable)]
    public void StatusOf_EachKind_IsItsDocumentedStatus(string kindName, int status) =>
        RecordCreationProblemMap.StatusOf(Refusal(Enum.Parse<RecordCreationRefusalKind>(kindName))).Should().Be(status);

    [Theory]
    [InlineData(400, StatusCodes.Status400BadRequest)]
    [InlineData(401, StatusCodes.Status403Forbidden)]
    [InlineData(403, StatusCodes.Status403Forbidden)]
    [InlineData(404, StatusCodes.Status404NotFound)]
    [InlineData(429, StatusCodes.Status429TooManyRequests)]
    [InlineData(408, StatusCodes.Status504GatewayTimeout)]
    [InlineData(504, StatusCodes.Status504GatewayTimeout)]
    [InlineData(500, StatusCodes.Status503ServiceUnavailable)]
    [InlineData(502, StatusCodes.Status503ServiceUnavailable)]
    [InlineData(0, StatusCodes.Status503ServiceUnavailable)]
    public void ToProblemDetails_AnUpstreamFailure_KeepsItsMeaning_ButNeverItsText(int upstream, int expected)
    {
        var refusal = new RecordCreationRefusal(RecordCreationRefusalKind.Upstream, "DATAVERSE_SERVICE_ERROR", UpstreamSecret)
        {
            ClientFailure = DataverseUserResponse.Fail(upstream, "DATAVERSE_SERVICE_ERROR", UpstreamSecret),
        };

        var problem = RecordCreationProblemMap.ToProblemDetails(refusal, "sprk_event", Correlation);

        problem.Status.Should().Be(expected);
        JsonSerializer.Serialize(problem).Should().NotContain("SecLib").And.NotContain("10.0.0.7").And.NotContain("0x80040220");
    }

    [Fact]
    public void ToProblemDetails_AnUnavailableRefusal_NeverRendersItsDetail()
    {
        var refusal = new RecordCreationRefusal(RecordCreationRefusalKind.Unavailable, RecordCreationRefusal.CheckFailedCode, UpstreamSecret);

        var body = JsonSerializer.Serialize(RecordCreationProblemMap.ToProblemDetails(refusal, "sprk_matter", Correlation));

        body.Should().NotContain("SecLib").And.NotContain("10.0.0.7");
        body.Should().Contain("matter");
    }

    [Fact]
    public void ToProblemDetails_NotFoundOrDenied_IsOneBody_WhateverTheDetailOrCodeSays()
    {
        var missing = new RecordCreationRefusal(RecordCreationRefusalKind.NotFoundOrDenied, RecordCreationRefusal.NotFoundCode,
            "The matter a2460000-0000-4000-8000-000000000001 does not exist.");
        var denied = new RecordCreationRefusal(RecordCreationRefusalKind.NotFoundOrDenied, "some.other_code",
            "You do not have AppendTo on the matter.");
        var upstream404 = new RecordCreationRefusal(RecordCreationRefusalKind.Upstream, "DATAVERSE_NOT_FOUND", "Does not exist.")
        {
            ClientFailure = DataverseUserResponse.Fail(404, "DATAVERSE_NOT_FOUND", "sprk_matter With Id = a246 Does Not Exist"),
        };

        var bodies = new[] { missing, denied, upstream404 }
            .Select(r => JsonSerializer.Serialize(RecordCreationProblemMap.ToProblemDetails(r, "sprk_event", Correlation)))
            .ToList();

        bodies.Distinct().Should().ContainSingle("does-not-exist and cannot-see are indistinguishable");
        bodies[0].Should().Contain(RecordCreationRefusal.NotFoundCode).And.NotContain("a2460000").And.NotContain("AppendTo");
    }

    [Fact]
    public void ToProblemDetails_AnOwnerRefusal_IsTheExistingOwnerRefusalShape()
    {
        var refusal = new RecordCreationRefusal(RecordCreationRefusalKind.OwnerUnresolved, "NoOwnerSource", "no owner could be resolved");

        var problem = RecordCreationProblemMap.ToProblemDetails(refusal, "sprk_event", Correlation);

        problem.Status.Should().Be(StatusCodes.Status409Conflict);
        problem.Title.Should().Be("Record owner unresolved");
        problem.Detail.Should().Be("The event was not saved: no owner could be resolved.");
        problem.Extensions["errorCode"].Should().Be("NoOwnerSource");
        problem.Extensions["reasonCode"].Should().Be("NoOwnerSource");
    }

    private static RecordCreationRefusal Refusal(RecordCreationRefusalKind kind) =>
        new(kind, $"test.{kind}", $"The {kind} refusal's own words.")
        {
            ClientFailure = kind == RecordCreationRefusalKind.Upstream
                ? DataverseUserResponse.Fail(503, "DATAVERSE_SERVICE_ERROR", UpstreamSecret)
                : null,
        };
}

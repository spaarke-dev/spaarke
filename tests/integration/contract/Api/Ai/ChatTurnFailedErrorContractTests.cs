using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Api.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// G-P3 UAT round-1 H3 (ADR-019) — the SendMessage catch-all SSE error contract.
/// Root incident (2026-07-07): the endpoint interpolated the raw exception
/// (<c>[ClientResultException: HTTP 400 … tools[28].function.parameters …]</c>)
/// into the SSE error event, rendering upstream-provider internals verbatim in
/// the operator's transcript. The contract is now: STABLE errorCode + STABLE
/// safe copy; exception detail is server-log-only. The construction site
/// (<see cref="ChatEndpoints.BuildTurnFailedErrorEvent"/>) takes no exception
/// input, so the leak cannot regress by interpolation.
/// </summary>
public class ChatTurnFailedErrorContractTests
{
    [Fact]
    public void BuildTurnFailedErrorEvent_CarriesStableCodeAndSafeCopy()
    {
        var evt = ChatEndpoints.BuildTurnFailedErrorEvent();

        evt.Type.Should().Be("error");
        evt.Content.Should().Be(
            $"[{ChatEndpoints.ChatTurnFailedErrorCode}] The assistant hit a problem completing this turn. Please try again.",
            "the copy is a STABLE contract — clients and transcripts rely on it never carrying server internals");
        ChatEndpoints.ChatTurnFailedErrorCode.Should().Be("chat.turn-failed",
            "ADR-019: errorCodes are stable identifiers, never renamed casually");
        evt.Data.Should().BeNull();
    }

    [Fact]
    public void BuildTurnFailedErrorEvent_SerializedFrame_NeverLeaksExceptionShapes()
    {
        // Belt-and-braces: the serialized SSE payload contains none of the shapes the
        // G-P3 banner leaked (exception type names, HTTP status internals, tools[N]
        // parameter paths).
        var json = JsonSerializer.Serialize(ChatEndpoints.BuildTurnFailedErrorEvent());

        json.Should().NotContainAny("Exception", "HTTP 400", "tools[", "function.parameters", "invalid_request_error");
    }
}

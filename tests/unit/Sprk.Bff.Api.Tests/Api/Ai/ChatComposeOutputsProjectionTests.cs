using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Unit tests for <see cref="ChatEndpoints.ProjectComposeOutputs"/> — the FR-04
/// render-follows-store READ HALF (spaarkeai-compose-r2 task 016 HOOK #1) backing
/// <c>GET /api/ai/chat/sessions/{id}/compose-outputs</c>.
///
/// <para>
/// <b>Behaviour protected (concrete FR-04 invariants)</b>: only <c>compose</c>-disposition
/// ledger entries are surfaced (a non-compose entry must NOT leak into the editor materialize
/// path); ADR-040 truncation markers are omitted (a compose leg never materializes a partial
/// draft); the opaque Compose-owned payload is passed through verbatim. Pure projection over
/// real <see cref="SessionOutput"/> objects — no mocks, no DI, no <c>Mock&lt;HttpMessageHandler&gt;</c>
/// (ADR-038).
/// </para>
/// </summary>
public class ChatComposeOutputsProjectionTests
{
    private const string ComposeBinding = "3f2504e0-4f89-41d3-9a0c-0305e82c3301";

    private static SessionOutput Output(string bindingId, int turn, string disposition, JsonElement payload) => new()
    {
        Key = SessionLedger.BuildOutputKey(bindingId, turn),
        BindingId = bindingId,
        UcId = "compose-draft-alternative",
        Turn = turn,
        Disposition = disposition,
        Payload = payload,
        CreatedAt = DateTimeOffset.UnixEpoch,
    };

    private static JsonElement DraftPayload(string newText) =>
        JsonSerializer.SerializeToElement(new { target_text = "old", new_text = newText, match_mode = "strict" });

    private static JsonElement TruncationMarker() =>
        JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            [SessionLedger.TruncationMarkerProperty] = true,
            ["original_bytes"] = 200_000,
            ["cap_bytes"] = SessionLedger.InlinePayloadCapBytes,
            ["preview"] = "…",
        });

    [Fact]
    public void ProjectComposeOutputs_WithMixedDispositions_ReturnsOnlyComposeEntriesWithPayloadIntact()
    {
        var ledger = new List<SessionOutput>
        {
            Output(ComposeBinding, 1, "informational", DraftPayload("not-a-draft")),
            Output(ComposeBinding, 2, ComposeDisposition.DispositionValue, DraftPayload("sixty (60) days")),
            Output("other-binding", 3, "record", DraftPayload("record-write")),
        };

        var projected = ChatEndpoints.ProjectComposeOutputs(ledger);

        projected.Should().ContainSingle();
        var only = projected[0];
        only.Disposition.Should().Be(ComposeDisposition.DispositionValue);
        only.Key.Should().Be($"{ComposeBinding}@t2");
        only.BindingId.Should().Be(ComposeBinding);
        only.Turn.Should().Be(2);
        // Payload passes through opaquely — the Compose-owned snake_case field survives.
        only.Payload.GetProperty("new_text").GetString().Should().Be("sixty (60) days");
    }

    [Fact]
    public void ProjectComposeOutputs_WithTruncatedComposeEntry_OmitsIt()
    {
        var ledger = new List<SessionOutput>
        {
            Output(ComposeBinding, 1, ComposeDisposition.DispositionValue, DraftPayload("materializable")),
            Output(ComposeBinding, 2, ComposeDisposition.DispositionValue, TruncationMarker()),
        };

        var projected = ChatEndpoints.ProjectComposeOutputs(ledger);

        // The truncation marker cannot be materialized (fail-loud downstream) — omitted, not shipped.
        projected.Should().ContainSingle();
        projected[0].Key.Should().Be($"{ComposeBinding}@t1");
    }

    [Fact]
    public void ProjectComposeOutputs_WithNullLedger_ReturnsEmpty()
    {
        ChatEndpoints.ProjectComposeOutputs(null).Should().BeEmpty();
    }

    [Fact]
    public void ProjectComposeOutputs_WithNoComposeEntries_ReturnsEmpty()
    {
        var ledger = new List<SessionOutput>
        {
            Output(ComposeBinding, 1, "informational", DraftPayload("x")),
            Output(ComposeBinding, 2, "work_product", DraftPayload("y")),
        };

        ChatEndpoints.ProjectComposeOutputs(ledger).Should().BeEmpty();
    }
}

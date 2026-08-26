// -----------------------------------------------------------------------------
// HandlerEnvelopeRoundTripTests.cs
//
// L2 CONTROL-PLANE wire-contract test (task 105, Phase C'' Wave G-1): proves
// HandlerEnvelope's OWN declared JSON contract (its public
// [JsonPropertyName] + [JsonIgnore(WhenWritingDefault)] attributes) survives
// a camelCase serialize/deserialize round trip losslessly -- the contract
// BOTH ServiceBusHandlerEnqueuer.EnqueueAsync (producer side) and
// ProvisioningHandlerDispatcher's envelope deserialization (consumer side)
// depend on when they independently configure a
// `JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }`
// on their own respective sides of the Service Bus wire.
//
// SCOPE NARROWED BY ADR-038 §7 BAN B8 (binding -- see docs/adr/
// ADR-038-testing-strategy.md):
//   An earlier draft of this file reflected into
//   ServiceBusHandlerEnqueuer's PRIVATE static BodySerializerOptions field
//   and ProvisioningHandlerDispatcher's PRIVATE static
//   BodyDeserializerOptions field (plus its private static
//   TryDeserializeEnvelope method, via Azure.Messaging.ServiceBus's
//   ServiceBusModelFactory) to prove the exact production JsonSerializerOptions
//   *instances* on both sides agree. ADR-038 §7 B8 explicitly bans
//   "Internal/private method tests (via InternalsVisibleTo or reflection)":
//   it locks an implementation detail that should stay free to refactor,
//   and its own prescribed fix is "test through the public surface" --
//   which for full envelope-decode fidelity means a LIVE (or SDK-model-
//   factory-driven) Service Bus message flow, out of scope for a pure unit
//   test class. Per CLAUDE.md §6.5 this is a documented Path C (pivot to
//   comply): this suite instead tests HandlerEnvelope's OWN public JSON
//   contract with a plain, LOCALLY-CONSTRUCTED camelCase
//   JsonSerializerOptions (not reflected from -- and therefore not proving
//   byte-identity with -- either production class's private options
//   instance). This is a narrower guarantee (documented-contract parity,
//   not literal options-instance identity) but is the ADR-038-compliant
//   boundary: HandlerEnvelope's [JsonPropertyName]/[JsonIgnore] attributes
//   ARE the actual, public, project-specific wire contract (not a
//   System.Text.Json framework default -- see the B12 "snapshot tests of
//   trivial JSON round-trip" distinction: this class tests attributes this
//   project authored, not the serializer's generic behavior). If the two
//   production classes' options objects were ever to drift from camelCase
//   (e.g. one gains PropertyNameCaseInsensitive or a custom converter), that
//   drift is caught by the live-Service-Bus paths this project already
//   maintains: ServiceBusSmokeTests.cs's env-guarded round trip and
//   Tests/Seam/ProvisioningDispatchSpineSeamTests.cs (task 118).
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Dispatch;

public sealed class HandlerEnvelopeRoundTripTests
{
    /// <summary>
    /// The documented wire policy both ServiceBusHandlerEnqueuer.cs and
    /// ProvisioningHandlerDispatcher.cs's file headers declare (camelCase) --
    /// constructed fresh here, NOT reflected from either production class
    /// (see file header ADR-038 B8 note).
    /// </summary>
    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void Serialize_ThenDeserialize_RoundTripsByValue()
    {
        var original = MakeEnvelope(attempt: 0);

        var json = JsonSerializer.Serialize(original, CamelCase);
        var roundTripped = JsonSerializer.Deserialize<HandlerEnvelope>(json, CamelCase);

        roundTripped.Should().Be(original,
            "HandlerEnvelope is a record -- value equality proves every field survives a camelCase " +
            "serialize/deserialize round trip, the wire contract both the enqueuer and the dispatcher " +
            "independently configure their own JsonSerializerOptions to match.");
    }

    [Fact]
    public void Serialize_ThenDeserialize_RoundTripsWithNonZeroAttempt()
    {
        var original = MakeEnvelope(attempt: 3);

        var json = JsonSerializer.Serialize(original, CamelCase);
        var roundTripped = JsonSerializer.Deserialize<HandlerEnvelope>(json, CamelCase);

        roundTripped.Should().Be(original,
            "task 107's Attempt field must round-trip identically once it is a non-default, " +
            "wire-serialized value.");
    }

    [Fact]
    public void Serialize_AttemptZero_OmittedFromWire_StillRoundTripsToZero()
    {
        var original = MakeEnvelope(attempt: 0);

        var json = JsonSerializer.Serialize(original, CamelCase);

        json.Should().NotContain("\"attempt\"",
            "first-enqueue byte-stability (task 107) requires the wire payload to OMIT attempt at 0 -- " +
            "HandlerEnvelope.Attempt's [JsonIgnore(WhenWritingDefault)] attribute.");

        var roundTripped = JsonSerializer.Deserialize<HandlerEnvelope>(json, CamelCase);
        roundTripped.Should().Be(original,
            "the omitted field must still default back to 0 on deserialize, reproducing the full " +
            "original envelope (not merely the Attempt field in isolation -- task 107's own test " +
            "coverage in ReconcilerEnqueuePayloadAttemptTests.cs already isolates the Attempt field; " +
            "this test's distinct value is proving the WHOLE envelope round-trips together).");
    }

    [Fact]
    public void Serialize_ParametersJsonContainingKeyVaultUriSyntax_RoundTripsWithoutCorruption()
    {
        // ParametersJson is itself an opaque JSON string that commonly embeds
        // Key Vault reference syntax (per HandlerEnvelope.cs's own doc
        // comment: "MUST NOT contain cleartext secrets -- KV URI refs
        // only"). This exercises nested-JSON-as-string escaping, a realistic
        // corruption vector this project's wire format specifically has to
        // survive (unlike a generic scalar-field round trip).
        var original = MakeEnvelope(
            attempt: 0,
            parametersJson: "{\"kvUri\":\"@Microsoft.KeyVault(SecretUri=https://example.vault.azure.net/secrets/x)\",\"nested\":{\"a\":1}}");

        var json = JsonSerializer.Serialize(original, CamelCase);
        var roundTripped = JsonSerializer.Deserialize<HandlerEnvelope>(json, CamelCase);

        roundTripped!.ParametersJson.Should().Be(original.ParametersJson,
            "the opaque ParametersJson payload (itself containing embedded JSON + KV URI syntax) must " +
            "survive double-encoding without corruption.");
    }

    [Fact]
    public void CamelCasePolicy_ProducesLowercaseFirstLetterPropertyNames()
    {
        var envelope = MakeEnvelope(attempt: 0);

        var json = JsonSerializer.Serialize(envelope, CamelCase);

        json.Should().Contain("\"handlerId\"").And.Contain("\"runId\"").And.Contain("\"customerId\"")
            .And.Contain("\"parametersJson\"").And.Contain("\"enqueuedAt\"");
        json.Should().NotContain("\"HandlerId\"", "camelCase policy must lowercase the first letter, not pass PascalCase through.");
    }

    // -------------------------------------------------------------------
    // Helpers.
    // -------------------------------------------------------------------

    private static HandlerEnvelope MakeEnvelope(int attempt, string? parametersJson = null) => new()
    {
        HandlerId = "H4",
        RunId = "01J7Q3ZPABCDEF0000000001",
        CustomerId = "acme-corp",
        ParametersJson = parametersJson ?? "{\"kvUri\":\"@Microsoft.KeyVault(SecretUri=https://example.vault.azure.net/secrets/x)\"}",
        EnqueuedAt = DateTimeOffset.Parse("2026-08-19T14:00:00Z"),
        Attempt = attempt,
    };
}

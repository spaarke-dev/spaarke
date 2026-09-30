// -----------------------------------------------------------------------------
// TenancyModelParserTests.cs
//
// TASK: 223 (customer-provisioning-orchestration-r1 INCOMING-D12-D13-REMEDIATION
// §5 Item 2, 2026-09-29). Coverage for the shared TenancyModel enum + parser +
// exception introduced in the same task. Pins the D-12 parse-or-reject contract
// AND the H12c idempotency-key format preservation invariant (enum member names
// round-trip byte-for-byte to the historical string literals).
//
// TEST SCOPE (per POML step 14):
//   - Exact-match parse for both defined enum members returns true + typed value
//   - Null / empty / whitespace input returns false + default enum
//   - Case-sensitive: wrong-case input returns false (case-INsensitive parse
//     would let intake bypass H1/H3's strict-literal contract)
//   - Unknown non-blank value returns false
//   - Parse (throwing overload) throws TenancyModelParseException on failure
//     and returns the enum on success
//   - CRITICAL BINDING: enum member names round-trip to the exact string
//     literals H12c embeds in its idempotency key. If this ever breaks, every
//     completed H12c phase becomes invalid — this test is the tripwire.
//   - Numeric strings ("0", "1") do NOT parse — Enum.TryParse alone would accept
//     them, but the parser scans Enum.GetNames + string.Equals(Ordinal) so it
//     admits only defined enum-member NAMES (never their numeric values)
//
// NOT COVERED (out of scope for Task 223):
//   - JSON serialization behaviour (no [JsonConverter] on the enum by design —
//     ProvisioningRun.TenancyModel stays a string field for Cosmos wire-format
//     preservation; adding a converter would risk breaking existing rows)
//   - FormatExpectedValues formatting stability (a smoke test would risk
//     freezing the diagnostic wording; the enum's name list is the ground truth)
// -----------------------------------------------------------------------------

using FluentAssertions;
using Sprk.Provisioning.ControlPlane.Core.Models;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Models;

public sealed class TenancyModelParserTests
{
    // ---------- TryParse: happy path ----------

    [Fact]
    public void TryParse_ExactMatch_Model1Shared_ReturnsTrue()
    {
        var ok = TenancyModelParser.TryParse("Model1Shared", out var parsed);

        ok.Should().BeTrue();
        parsed.Should().Be(TenancyModel.Model1Shared);
    }

    [Fact]
    public void TryParse_ExactMatch_Model2Dedicated_ReturnsTrue()
    {
        var ok = TenancyModelParser.TryParse("Model2Dedicated", out var parsed);

        ok.Should().BeTrue();
        parsed.Should().Be(TenancyModel.Model2Dedicated);
    }

    // ---------- TryParse: rejection paths ----------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void TryParse_NullOrWhitespace_ReturnsFalse(string? input)
    {
        var ok = TenancyModelParser.TryParse(input, out var parsed);

        ok.Should().BeFalse();
        parsed.Should().Be(default(TenancyModel));
    }

    [Theory]
    [InlineData("model1shared")]  // wrong case
    [InlineData("MODEL1SHARED")]  // wrong case
    [InlineData("model2dedicated")]
    [InlineData("MODEL2DEDICATED")]
    [InlineData("Model1Shared ")]  // trailing whitespace
    [InlineData(" Model2Dedicated")]  // leading whitespace
    public void TryParse_WrongCaseOrPadded_ReturnsFalse(string input)
    {
        // Case-sensitivity is load-bearing: H12c's idempotency-key format embeds this
        // string byte-for-byte; permitting "model2dedicated" downstream would either
        // silently duplicate H12c work under a new key, or (with normalization) break
        // pre-D-12 completed-phase replay.
        var ok = TenancyModelParser.TryParse(input, out var parsed);

        ok.Should().BeFalse();
        parsed.Should().Be(default(TenancyModel));
    }

    [Theory]
    [InlineData("Model3Future")]
    [InlineData("SomeOtherValue")]
    [InlineData("Model1")]
    [InlineData("Model2")]
    public void TryParse_UnknownValue_ReturnsFalse(string input)
    {
        var ok = TenancyModelParser.TryParse(input, out var parsed);

        ok.Should().BeFalse();
        parsed.Should().Be(default(TenancyModel));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("2")]
    public void TryParse_NumericString_ReturnsFalse(string input)
    {
        // Enum.TryParse alone accepts numeric strings ("0" → Model1Shared, "1" →
        // Model2Dedicated) which would silently accept a legacy integer-serialised
        // tenancyModel value. The parser scans Enum.GetNames + string.Equals(Ordinal)
        // instead, so it matches only defined enum-member NAMES (never numeric values).
        var ok = TenancyModelParser.TryParse(input, out var parsed);

        ok.Should().BeFalse();
        parsed.Should().Be(default(TenancyModel));
    }

    // ---------- Parse (throwing overload) ----------

    [Fact]
    public void Parse_ExactMatch_Model1Shared_ReturnsEnum()
    {
        TenancyModelParser.Parse("Model1Shared").Should().Be(TenancyModel.Model1Shared);
    }

    [Fact]
    public void Parse_ExactMatch_Model2Dedicated_ReturnsEnum()
    {
        TenancyModelParser.Parse("Model2Dedicated").Should().Be(TenancyModel.Model2Dedicated);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("model1shared")]
    [InlineData("Unknown")]
    public void Parse_InvalidInput_ThrowsTenancyModelParseException(string? input)
    {
        var act = () => TenancyModelParser.Parse(input);

        var ex = act.Should().Throw<TenancyModelParseException>().Which;
        ex.OffendingValue.Should().Be(input);
        ex.Message.Should().Contain("Model1Shared").And.Contain("Model2Dedicated");
    }

    // ---------- BINDING invariant: H12c idempotency-key format preservation ----------
    //
    // These two tests are the tripwire for the H12c idempotency-key format constraint:
    // the enum's .ToString() output MUST equal the historical string literal byte-for-byte.
    // If either fails, EVERY pre-Task-223 completed H12c phase becomes invalid on replay
    // (idempotency key mismatch → duplicate write / non-durable no-op). Item 3 / Task 224
    // owns any actual string value change; Item 2 pins the invariant here.

    [Fact]
    public void EnumToString_Model1Shared_EqualsHistoricalLiteral()
    {
        TenancyModel.Model1Shared.ToString().Should().Be(
            "Model1Shared",
            because: "H12c idempotency-key format h12c-{customerId}-{tenancyModel}-{endpointHash} " +
                     "embeds this string verbatim; changing it invalidates every completed H12c phase.");
    }

    [Fact]
    public void EnumToString_Model2Dedicated_EqualsHistoricalLiteral()
    {
        TenancyModel.Model2Dedicated.ToString().Should().Be(
            "Model2Dedicated",
            because: "H12c idempotency-key format h12c-{customerId}-{tenancyModel}-{endpointHash} " +
                     "embeds this string verbatim; changing it invalidates every completed H12c phase.");
    }

    // ---------- Round-trip: enum → string → enum ----------

    [Theory]
    [InlineData(TenancyModel.Model1Shared)]
    [InlineData(TenancyModel.Model2Dedicated)]
    public void RoundTrip_EnumToStringToEnum_Preserves(TenancyModel original)
    {
        // Belt-and-braces: what handlers pass through Cosmos as string MUST land back
        // as the same enum member. If a future enum addition breaks this, add a case
        // to the parser's TryParse (do NOT relax case-sensitivity).
        var asString = original.ToString();
        TenancyModelParser.TryParse(asString, out var parsed).Should().BeTrue();
        parsed.Should().Be(original);
    }
}

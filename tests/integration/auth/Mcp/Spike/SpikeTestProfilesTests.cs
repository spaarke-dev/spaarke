// SPIKE BRANCH ONLY (specialist-legal-agents-r1 task 011; never merged). The embedded test-profile set: what is loaded, how it is keyed,
// and that lookup is exact (the isolation guard the seeding proof in phase0/g3/README.md mutates).

using System.Text.Json.Nodes;
using FluentAssertions;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Spike;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.Mcp.Spike;

public sealed class SpikeTestProfilesTests
{
    private static readonly string Owner = SpikeToolsTestHost.OwnerSystemUserId.ToString("D");
    private static readonly string TestUser = SpikeToolsTestHost.TestUserSystemUserId.ToString("D");

    [Fact]
    public void Embedded_HoldsExactlyTheFourProfiles_KeyedToTheOwnerAndTheTestUser()
    {
        var all = SpikeTestProfiles.Default.All;

        all.Select(p => (p.Function, p.SystemUserId.ToString("D"), p.ProfileVersion)).Should().BeEquivalentTo(new[]
        {
            ("nda", Owner, "g1-a-v1"),
            ("nda", TestUser, "g1-b-v1"),
            ("patent", Owner, "pat-a-v1"),
            ("patent", TestUser, "pat-b-v1"),
        });
        all.Should().OnlyContain(p => p.ModelTier == "Reasoning" && p.Instructions.Count == 3);
    }

    [Fact]
    public void Find_IsExactOnFunctionAndSystemUserId_AndNeverFallsBack()
    {
        var set = SpikeTestProfiles.Default;

        set.Find("nda", Owner)!.ProfileVersion.Should().Be("g1-a-v1");
        set.Find("nda", TestUser)!.ProfileVersion.Should().Be("g1-b-v1");
        set.Find("patent", Owner)!.ProfileVersion.Should().Be("pat-a-v1");
        set.Find("patent", TestUser)!.ProfileVersion.Should().Be("pat-b-v1");
        set.Find("nda", Owner.ToUpperInvariant())!.ProfileVersion.Should().Be("g1-a-v1", "a GUID is a GUID whatever its case");

        set.Find("nda", SpikeToolsTestHost.NoProfileSystemUserId.ToString("D")).Should().BeNull("an unknown user has no profile");
        set.Find("nda", Guid.Empty.ToString("D")).Should().BeNull();
        set.Find("nda", null).Should().BeNull();
        set.Find("nda", "").Should().BeNull();
        set.Find("nda", "TO-BE-SET-BY-011").Should().BeNull();
        set.Find("nda", "{" + Owner + "}").Should().BeNull("only the resolver's 'D' form is accepted");
        set.Find("employment", Owner).Should().BeNull("an unknown function has no profile");
        set.Find("NDA", Owner).Should().BeNull("functions are matched exactly");
    }

    [Fact]
    public void ReviewInputs_AreTheFiveDeclaredPositions_TextUnderTheActionLimit_AndTheFlagABoolean()
    {
        foreach (var (user, flag) in new[] { (Owner, true), (TestUser, false) })
        {
            var inputs = SpikeTestProfiles.Default.Find("nda", user)!.ToReviewInputs();

            inputs.Keys.Should().BeEquivalentTo(SpecialistProfileSpike.PositionInputs);
            inputs["uncappedIndemnityFlag"].GetBoolean().Should().Be(flag);
            foreach (var name in SpecialistProfileSpike.PositionInputs.Where(n => n != "uncappedIndemnityFlag"))
            {
                var text = inputs[name].GetString()!;
                text.Should().StartWith("Preferred: ").And.Contain(" Fallback: ").And.Contain(" Escalate: ").And.Contain("Outcome by clause value:");
                text.Length.Should().BeLessThanOrEqualTo(SpikeTestProfiles.MaxPositionChars);
            }
        }

        var patent = SpikeTestProfiles.Default.Find("patent", Owner)!;
        patent.Invoking(p => p.ToReviewInputs()).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Load_RefusesThePlaceholderKey_ATwiceKeyedUser_ARepeatedVersion_AndANonTestProfile()
    {
        var good = File(SpikeTestProfiles.Default.Find("nda", Owner)!);

        Loading(("nda", "x", With(good, "systemuserid", "TO-BE-SET-BY-011"))).Should().Throw<InvalidOperationException>().WithMessage("*systemuserid*");
        Loading(("nda", "x", With(good, "status", "live"))).Should().Throw<InvalidOperationException>().WithMessage("*status: test*");
        Loading(("nda", "x", good), ("nda", "y", With(good, "profileVersion", "other"))).Should().Throw<InvalidOperationException>().WithMessage("*second nda profile*");
        Loading(("nda", "x", good), ("nda", "y", With(good, "systemuserid", TestUser))).Should().Throw<InvalidOperationException>().WithMessage("*repeats profileVersion*");
        Loading(("nda", "x", With(good, "modelTier", "Huge"))).Should().Throw<InvalidOperationException>().WithMessage("*modelTier*");

        var withoutFlag = JsonNode.Parse(good)!.AsObject();
        withoutFlag["positions"]!.AsObject().Remove("uncappedIndemnityFlag");
        Loading(("nda", "x", withoutFlag.ToJsonString())).Should().Throw<InvalidOperationException>().WithMessage("*five declared positions*");

        var noCanary = JsonNode.Parse(good)!.AsObject();
        noCanary.Remove("returnedInstructions");
        Loading(("nda", "x", noCanary.ToJsonString())).Should().Throw<InvalidOperationException>().WithMessage("*returnedInstructions*");
    }

    private static string File(SpikeTestProfile profile) => profile.Source.ToJsonString();

    private static string With(string json, string key, string value)
    {
        var node = JsonNode.Parse(json)!.AsObject();
        node[key] = value;
        return node.ToJsonString();
    }

    private static Action Loading(params (string Function, string Name, string Json)[] files) => () => SpikeTestProfileSet.Load(files);
}

// SPIKE BRANCH ONLY (specialist-legal-agents-r1 task 011; spike/specialist-agents-phase0, never merged; ADR-038: branch-only tests).
//
// WHAT THIS PROVES (spec FR-02, FR-03, NFR-05; POML 011 acceptance criteria), over the production MCP route (SpikeToolsTestHost):
//   - a caller with a test profile gets THAT profile, its profileVersion and its returned instructions (spike_get_profile);
//   - fail closed: NO_PROFILE and CALLER_UNRESOLVED, with no profile in the result and no review run (both tools);
//   - isolation: user A never receives user B's profile, from either tool, sequentially or with both calls in flight at once;
//   - spike_review_nda binds the caller's own five positions and the profile's tier, and returns the review, profileVersion and latency;
//   - header logging records names only: no header value, Authorization bearer token or cookie value reaches any log entry.

using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Spike;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.Mcp.Spike;

public sealed class SpikeProfileToolsTests
{
    private static readonly string OwnerToken = SpikeToolsTestHost.MintToken(SpikeToolsTestHost.OwnerOid);
    private static readonly string TestUserToken = SpikeToolsTestHost.MintToken(SpikeToolsTestHost.TestUserOid);
    private static readonly string NoProfileToken = SpikeToolsTestHost.MintToken(SpikeToolsTestHost.NoProfileOid);
    private static readonly string UnresolvedToken = SpikeToolsTestHost.MintToken(SpikeToolsTestHost.UnresolvedOid);

    private const string Nda = "Mutual NDA. Term: 2 years. Governing law: New York. Each party indemnifies the other without limit.";

    // ---- tools/list -------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ToolsList_BothSpikeTools_AreReadOnly_ModelVisible_WithOneRequiredClosedArgument()
    {
        await using var host = await SpikeToolsTestHost.StartAsync();

        var tools = await host.ListToolsAsync(OwnerToken);

        tools.Keys.Should().BeEquivalentTo(SpikeGetProfileTool.Name, SpikeReviewNdaTool.Name);
        foreach (var (name, argument) in new[] { (SpikeGetProfileTool.Name, "function"), (SpikeReviewNdaTool.Name, "documentText") })
        {
            var tool = tools[name];
            tool.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean().Should().BeTrue(name);
            tool.GetProperty("annotations").GetProperty("destructiveHint").GetBoolean().Should().BeFalse(name);
            tool.TryGetProperty("_meta", out _).Should().BeFalse($"{name} is model-visible (no app-only visibility)");
            tool.GetProperty("description").GetString()!.Length.Should().BeLessThanOrEqualTo(1024);
            var schema = tool.GetProperty("inputSchema");
            schema.GetProperty("additionalProperties").GetBoolean().Should().BeFalse(name);
            schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Should().Equal(argument);
            schema.GetProperty("required").EnumerateArray().Select(r => r.GetString()).Should().Equal(argument);
        }

        tools[SpikeGetProfileTool.Name].GetProperty("inputSchema").GetProperty("properties").GetProperty("function")
            .GetProperty("enum").EnumerateArray().Select(v => v.GetString()).Should().Equal("nda", "patent");
    }

    // ---- spike_get_profile: the caller's own profile ------------------------------------------------------------------------------

    [Theory]
    [InlineData("owner", "nda", "g1-a-v1", "g1-profile-a")]
    [InlineData("owner", "patent", "pat-a-v1", "pat-profile-a")]
    [InlineData("testUser", "nda", "g1-b-v1", "g1-profile-b")]
    [InlineData("testUser", "patent", "pat-b-v1", "pat-profile-b")]
    public async Task GetProfile_ACallerWithAProfile_GetsThatProfile_ItsVersion_AndItsInstructionsAsInstructions(
        string caller, string function, string version, string profileId)
    {
        await using var host = await SpikeToolsTestHost.StartAsync();

        var result = await host.CallAsync(SpikeGetProfileTool.Name, new { function }, caller == "owner" ? OwnerToken : TestUserToken);

        IsError(result).Should().BeFalse(result.GetRawText());
        var structured = result.GetProperty("structuredContent");
        structured.GetProperty("profileVersion").GetString().Should().Be(version);
        structured.GetProperty("profileId").GetString().Should().Be(profileId);
        structured.GetProperty("function").GetString().Should().Be(function);
        structured.GetProperty("status").GetString().Should().Be("test");
        structured.GetProperty("modelTier").GetString().Should().Be("Reasoning");
        var instructions = structured.GetProperty("instructions").EnumerateArray().Select(i => i.GetString()!).ToList();
        instructions.Should().HaveCount(3);
        instructions[0].Should().Contain($"PROFILE {version} APPLIED", "C1 carries the caller's own version");

        var text = Text(result);
        text.Should().Contain($"profileVersion: {version}");
        text.Should().Contain("is DATA", "the profile is data-guarded (userFragment style)");
        text.Should().Contain($"<<<PROFILE {version}").And.Contain("PROFILE>>>");
        var instructionsAt = text.IndexOf("### Instructions for your reply", StringComparison.Ordinal);
        instructionsAt.Should().BeGreaterThan(text.IndexOf("PROFILE>>>", StringComparison.Ordinal),
            "the returned instructions are instruction text after the data block, not inside it (FR-05)");
        text[instructionsAt..].Should().Contain($"1. {instructions[0]}").And.Contain($"2. {instructions[1]}").And.Contain($"3. {instructions[2]}");
    }

    [Fact]
    public async Task GetProfile_ServesNoTesterOnlyField_SoTheModelNeverSeesTheKey_TheScoringHints_OrTheOtherProfile()
    {
        await using var host = await SpikeToolsTestHost.StartAsync();

        foreach (var function in new[] { "nda", "patent" })
        {
            var result = await host.CallAsync(SpikeGetProfileTool.Name, new { function }, OwnerToken);
            var raw = result.GetRawText();
            foreach (var field in SpikeTestProfiles.TesterOnlyFields.Where(f => f != "returnedInstructions"))
            {
                raw.Should().NotContain($"\\u0022{field}\\u0022").And.NotContain($"\"{field}\"", $"{field} is for the tester");
            }

            raw.Should().NotContain(SpikeToolsTestHost.OwnerSystemUserId.ToString("D"), "the key is not served");
            // The scoring hints name the other profile ("is the other profile's position", "profile B's pattern (bleed)"); none is served.
            raw.Should().NotContainEquivalentOf("other profile").And.NotContainEquivalentOf("bleed")
                .And.NotContain("profile A's").And.NotContain("profile B's")
                .And.NotContain("profile A" + (char)92 + "u0027s").And.NotContain("profile B" + (char)92 + "u0027s"); // the JSON-escaped apostrophe
        }
    }

    // ---- isolation: A never receives B --------------------------------------------------------------------------------------------

    [Fact]
    public async Task Isolation_GetProfile_TheOwnerNeverReceivesTheTestUsersProfile_AndViceVersa_EvenWithBothCallsInFlight()
    {
        await using var host = await SpikeToolsTestHost.StartAsync();

        foreach (var function in new[] { "nda", "patent" })
        {
            for (var round = 0; round < 5; round++)
            {
                var both = await Task.WhenAll(
                    host.CallAsync(SpikeGetProfileTool.Name, new { function }, OwnerToken),
                    host.CallAsync(SpikeGetProfileTool.Name, new { function }, TestUserToken));
                AssertOnlyOwnProfile(both[0], function, mine: "a", other: "b");
                AssertOnlyOwnProfile(both[1], function, mine: "b", other: "a");
            }
        }
    }

    [Fact]
    public async Task Isolation_ReviewNda_BindsOnlyTheCallersOwnPositions_AndReturnsOnlyTheCallersVersion()
    {
        await using var host = await SpikeToolsTestHost.StartAsync();

        var both = await Task.WhenAll(
            host.CallAsync(SpikeReviewNdaTool.Name, new { documentText = Nda }, OwnerToken),
            host.CallAsync(SpikeReviewNdaTool.Name, new { documentText = Nda }, TestUserToken));

        AssertReviewFor(both[0], "g1-a-v1", "g1-b-v1");
        AssertReviewFor(both[1], "g1-b-v1", "g1-a-v1");

        var reviews = host.Spike.Reviews.ToList();
        reviews.Should().HaveCount(2);
        var owner = reviews.Single(r => OidOf(r.Caller) == SpikeToolsTestHost.OwnerOid.ToString());
        var testUser = reviews.Single(r => OidOf(r.Caller) == SpikeToolsTestHost.TestUserOid.ToString());
        owner.Positions["uncappedIndemnityFlag"].GetBoolean().Should().BeTrue("profile A raises the flag");
        testUser.Positions["uncappedIndemnityFlag"].GetBoolean().Should().BeFalse("profile B does not");
        owner.Positions["term"].GetString().Should().StartWith("Preferred: Confidentiality obligations last 5 years");
        testUser.Positions["term"].GetString().Should().StartWith("Preferred: Confidentiality obligations last no more than 2 years");
        owner.Positions["governingLaw"].GetString().Should().StartWith("Preferred: Delaware law.");
        testUser.Positions["governingLaw"].GetString().Should().StartWith("Preferred: New York law.");
    }

    // ---- fail closed ----------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("nda")]
    [InlineData("patent")]
    public async Task GetProfile_AResolvedCallerWithNoProfile_IsNoProfile_AndReceivesNoProfile(string function)
    {
        await using var host = await SpikeToolsTestHost.StartAsync();

        var result = await host.CallAsync(SpikeGetProfileTool.Name, new { function }, NoProfileToken);

        AssertFailedClosed(result, "NO_PROFILE");
    }

    [Theory]
    [InlineData("nda")]
    [InlineData("patent")]
    public async Task GetProfile_AnUnresolvedCaller_IsCallerUnresolved_AndReceivesNoProfile(string function)
    {
        await using var host = await SpikeToolsTestHost.StartAsync();

        var result = await host.CallAsync(SpikeGetProfileTool.Name, new { function }, UnresolvedToken);

        AssertFailedClosed(result, "CALLER_UNRESOLVED");
    }

    [Theory]
    [InlineData("noProfile", "NO_PROFILE")]
    [InlineData("unresolved", "CALLER_UNRESOLVED")]
    public async Task ReviewNda_FailsClosedTheSameWay_BeforeAnyReviewRuns(string caller, string reasonCode)
    {
        await using var host = await SpikeToolsTestHost.StartAsync();

        var result = await host.CallAsync(SpikeReviewNdaTool.Name, new { documentText = Nda }, caller == "noProfile" ? NoProfileToken : UnresolvedToken);

        AssertFailedClosed(result, reasonCode);
        host.Spike.Reviews.Should().BeEmpty("no review runs for a caller without their own profile");
    }

    [Fact]
    public async Task ReviewNda_AFailedRun_IsReviewFailed_WithNoReview()
    {
        var spike = new SpikeAiDouble { ReviewFailure = new InvalidOperationException("The Action 'agreement-review-spike' was not found in Dataverse.") };
        await using var host = await SpikeToolsTestHost.StartAsync(spike);

        var result = await host.CallAsync(SpikeReviewNdaTool.Name, new { documentText = Nda }, OwnerToken);

        IsError(result).Should().BeTrue();
        result.GetProperty("structuredContent").GetProperty("reasonCode").GetString().Should().Be("REVIEW_FAILED");
        result.GetProperty("structuredContent").TryGetProperty("review", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("""{ }""")]
    [InlineData("""{ "function": "employment" }""")]
    [InlineData("""{ "function": "nda", "systemuserid": "849beec2-2b30-f111-88b5-7c1e520aa4df" }""")]
    [InlineData("""{ "function": 1 }""")]
    public async Task GetProfile_ArgumentsOtherThanOneKnownFunction_AreRefused_AndNeverPickAProfile(string arguments)
    {
        await using var host = await SpikeToolsTestHost.StartAsync();

        var result = await host.CallAsync(SpikeGetProfileTool.Name, JsonSerializer.Deserialize<JsonElement>(arguments), OwnerToken);

        AssertFailedClosed(result, "INVALID_ARGUMENT");
    }

    // ---- header logging (FR-03, NFR-05) --------------------------------------------------------------------------------------------

    [Fact]
    public async Task HeaderLogging_RecordsHeaderNamesOnly_NeverAValue_TheBearerToken_OrACookie()
    {
        await using var host = await SpikeToolsTestHost.StartAsync();
        const string probeValue = "probe-value-7f3a91";
        const string cookieValue = "session-cookie-value-c0ffee";
        const string conversationValue = "conversation-id-value-55d1";
        var headers = new Dictionary<string, string>
        {
            ["X-Spike-Probe"] = probeValue,
            ["Cookie"] = $"spaarke_session={cookieValue}",
            ["x-ms-conversation-id"] = conversationValue,
        };

        await host.CallAsync(SpikeGetProfileTool.Name, new { function = "nda" }, OwnerToken, headers);
        await host.CallAsync(SpikeReviewNdaTool.Name, new { documentText = Nda }, OwnerToken, headers);

        var lines = host.Logs.Entries.Where(e => e.Message.Contains("request header names", StringComparison.Ordinal)).ToList();
        lines.Should().HaveCount(2, "one header-name line per spike tool call");
        foreach (var line in lines)
        {
            var names = ((string)line.State["HeaderNames"]!).Split(',');
            names.Should().Contain(["Authorization", "Cookie", "X-Spike-Probe", "x-ms-conversation-id"]);
        }

        // Every entry the host wrote (every category and level, message and structured state): no value, token or cookie.
        var everything = string.Join("\n", host.Logs.Entries.Select(e =>
            e.Message + "|" + string.Join("|", e.State.Select(kv => $"{kv.Key}={kv.Value}"))));
        everything.Should().NotContain(probeValue);
        everything.Should().NotContain(cookieValue);
        everything.Should().NotContain(conversationValue);
        everything.Should().NotContain(OwnerToken);
        everything.Should().NotContain(OwnerToken.Split('.')[1], "not even the token's payload segment");
        everything.Should().NotContain("Bearer ey", "no bearer credential (the framework's \"AuthenticationScheme: Bearer\" scheme-name line is not one)");
        everything.Should().NotContain(Nda, "the document text is never logged");
        everything.Should().NotContain(SpikeToolsTestHost.OwnerSystemUserId.ToString("D"), "the BFF logs no caller identity");
    }

    [Fact]
    public void HeaderNames_KeepsOnlyHeaderTokenCharacters_AndCutsLongNames()
    {
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        http.Request.Headers["X-Ok_1.2"] = "v1";
        http.Request.Headers["X-Bad<script>"] = "v2";
        http.Request.Headers[new string('a', 200)] = "v3";

        var names = SpikeToolCall.HeaderNames(http);

        names.Should().Contain("X-Ok_1.2").And.Contain("X-Badscript");
        names.Should().Contain(new string('a', SpikeToolCall.MaxHeaderNameChars));
        names.Should().NotContain(n => n.Contains("v1") || n.Contains("v2") || n.Contains("v3"));
    }

    // ---- helpers ----------------------------------------------------------------------------------------------------------------------

    private static void AssertOnlyOwnProfile(JsonElement result, string function, string mine, string other)
    {
        IsError(result).Should().BeFalse(result.GetRawText());
        var (mineVersion, otherVersion) = function == "nda" ? ($"g1-{mine}-v1", $"g1-{other}-v1") : ($"pat-{mine}-v1", $"pat-{other}-v1");
        result.GetProperty("structuredContent").GetProperty("profileVersion").GetString().Should().Be(mineVersion);
        var raw = result.GetRawText();
        raw.Should().NotContain(otherVersion, "user A never receives user B's profile");
        raw.Should().NotContain(other == "b" ? SpikeToolsTestHost.TestUserSystemUserId.ToString("D") : SpikeToolsTestHost.OwnerSystemUserId.ToString("D"));
    }

    private static void AssertReviewFor(JsonElement result, string mine, string other)
    {
        IsError(result).Should().BeFalse(result.GetRawText());
        var structured = result.GetProperty("structuredContent");
        structured.GetProperty("profileVersion").GetString().Should().Be(mine);
        structured.GetProperty("actionCode").GetString().Should().Be(SpecialistProfileSpike.ActionCode);
        structured.GetProperty("modelTier").GetString().Should().Be("Reasoning", "the profile's tier is applied");
        structured.GetProperty("latencyMs").GetInt64().Should().BeGreaterThanOrEqualTo(0);
        structured.GetProperty("review").GetProperty("findings").GetArrayLength().Should().Be(5);
        Text(result).Should().Contain($"profile {mine}");
        result.GetRawText().Should().NotContain(other);
    }

    private static void AssertFailedClosed(JsonElement result, string reasonCode)
    {
        IsError(result).Should().BeTrue(result.GetRawText());
        var structured = result.GetProperty("structuredContent");
        structured.GetProperty("reasonCode").GetString().Should().Be(reasonCode);
        structured.TryGetProperty("profileVersion", out _).Should().BeFalse();
        structured.TryGetProperty("profile", out _).Should().BeFalse();
        var raw = result.GetRawText();
        foreach (var version in new[] { "g1-a-v1", "g1-b-v1", "pat-a-v1", "pat-b-v1" })
        {
            raw.Should().NotContain(version, "a failed call carries no profile, default or otherwise");
        }
    }

    private static string? OidOf(System.Security.Claims.ClaimsPrincipal caller) =>
        caller.FindFirst("oid")?.Value ?? caller.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;

    private static bool IsError(JsonElement result) => result.TryGetProperty("isError", out var e) && e.GetBoolean();

    private static string Text(JsonElement result) => result.GetProperty("content")[0].GetProperty("text").GetString()!;
}

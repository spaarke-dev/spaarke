using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Core;
using Azure.Identity;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// The spec NFR-04 negative canary: a BLOCKING merge gate for the FR-20 swap (task 036).
/// </summary>
/// <remarks>
/// <para><b>The failure this guards.</b> When Dataverse impersonation is not applied, the query runs as
/// the BFF application user — a System Administrator on dev — and returns the org-wide row set with
/// HTTP 200 and no error, no exception, and no log line. Every downstream layer
/// (<c>AccessibleRecordSetService</c> → <c>Tier2ScopeFilterInjector</c> → the module data response)
/// then does exactly what it is supposed to do with a set that is silently wrong. The only observable
/// difference between "impersonation works" and "org-wide disclosure" is that the impersonated answer
/// stopped being SMALLER than the app-only answer. That comparison is this file.</para>
///
/// <para><b>Equality is a failure.</b> Not a skip, not a warning, not a "TODO: seed the canary". A test
/// that goes green when impersonation does nothing is worse than no test at all, because it converts an
/// unknown into a gate signature on a merge.</para>
///
/// <para><b>Three layers, deliberately.</b>
/// <list type="number">
///   <item><b>Perturbation (always runs, no tenant).</b> Feeds the invariant the exact fail-OPEN state
///   and asserts it reports failure. This is what makes the gate real on every CI run: an assertion
///   nobody has watched fail is an assertion nobody has verified.</item>
///   <item><b>Live tenant (Tests 1–3).</b> The actual row-set comparison against a provisioned canary
///   user. FAILS — never quietly passes — when the canary is absent AND the gate is open (spec
///   NFR-01); see <c>TryAcquireCanary</c> for how "open" is decided.</item>
///   <item><b>Config tripwire (always runs, no tenant).</b> The FR-20 flag cannot be turned on in
///   checked-in configuration unless this canary is provisioned for the run. This is the mechanical
///   half of "034 is a blocking merge gate for 036".</item>
/// </list></para>
///
/// <para><b>Relationship to <c>ImpersonationFailClosedTests</c> (task 001).</b> That file pins the
/// argument-validation guards on a service built from throwaway configuration, before any I/O. It is
/// not superseded and is not duplicated here: Test 3 below re-pins the <see cref="Guid.Empty"/> refusal
/// on the LIVE-configured instance, so a refactor cannot make the guard conditional on configuration
/// that only the live path supplies.</para>
///
/// <para>Provisioning procedure: <c>tests/integration/auth/README.md</c> §
/// "NFR-04 impersonation negative canary".</para>
/// </remarks>
public class ImpersonationNegativeCanaryTests
{
    /// <summary>
    /// The impersonated read primitive returns ONE Dataverse page (it does not follow
    /// <c>@odata.nextLink</c>), so both sides of the comparison are capped identically and a run that
    /// reaches the cap is rejected rather than compared — a truncated baseline can manufacture either
    /// verdict.
    /// </summary>
    private const int PageCap = 5000;

    private static readonly string IdOnlyQuery =
        $"$select={ImpersonationCanaryEnvironment.PrimaryKeyField}&$top={PageCap}";

    // ══════════════════════════════════════════════════════════════════════════════════════════════
    // Layer 1 — PERTURBATION. Runs everywhere, needs no tenant.
    //
    // These prove the canary's FAIL direction. Without them the live assertions are a claim; with them
    // they are a verified mechanism, and a future refactor that softens "strictly fewer" into "fewer or
    // equal" turns this file red immediately instead of at the next disclosure.
    // ══════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// THE load-bearing perturbation: identical sets — the exact shape of "the MSCRMCallerID header was
    /// not applied" — must be reported as a failure, not a pass.
    /// </summary>
    [Fact]
    public void Evaluate_WhenImpersonatedSetEqualsAppOnlySet_ReportsInertRatherThanPassing()
    {
        var orgWide = new[] { Id(1), Id(2), Id(3), Id(4) };

        var outcome = ImpersonationNegativeCanary.Evaluate(appOnlyIds: orgWide, impersonatedIds: orgWide);

        outcome.Passed.Should().BeFalse(
            "equality means impersonation returned the app identity's org-wide rows — the fail-OPEN state");
        outcome.Verdict.Should().Be(CanaryVerdict.Inert);
        outcome.Message.Should().Contain("IMPERSONATION IS INERT");
        outcome.Message.Should().Contain("prvActOnBehalfOfAnotherUser",
            "the message must name the privilege an operator has to check");
    }

    /// <summary>
    /// Same-count-but-different-membership is NOT equality, and must still fail: it means the header was
    /// applied to a principal the canary contract does not describe. Pins that the subset invariant is
    /// evaluated independently of the count invariant, rather than one standing in for the other.
    /// </summary>
    [Fact]
    public void Evaluate_WhenImpersonatedSetIsNotASubset_ReportsNotASubset()
    {
        var outcome = ImpersonationNegativeCanary.Evaluate(
            appOnlyIds: new[] { Id(1), Id(2) },
            impersonatedIds: new[] { Id(1), Id(9) });

        outcome.Verdict.Should().Be(CanaryVerdict.NotASubset);
        outcome.Message.Should().Contain(Id(9).ToString(), "the offending id belongs in the build log");
    }

    /// <summary>
    /// A duplicated row must not be able to buy a passing count. Sets, not lists, decide the verdict —
    /// otherwise <c>{a, a, b}</c> vs <c>{a, b}</c> would read as "strictly fewer" while the underlying
    /// answers are identical.
    /// </summary>
    [Fact]
    public void Evaluate_WhenRowsAreDuplicated_ComparesDistinctIdsAndStillReportsInert()
    {
        var outcome = ImpersonationNegativeCanary.Evaluate(
            appOnlyIds: new[] { Id(1), Id(1), Id(2) },
            impersonatedIds: new[] { Id(1), Id(2) });

        outcome.Verdict.Should().Be(CanaryVerdict.Inert);
    }

    /// <summary>
    /// An empty app-only baseline makes "subset" trivially true and "strictly fewer" unsatisfiable, so no
    /// verdict is meaningful. Refusing to render one stops a mistyped entity set from presenting as a
    /// canary that cannot fail.
    /// </summary>
    [Fact]
    public void Evaluate_WhenAppOnlyBaselineIsEmpty_RefusesToRenderAVerdict()
    {
        var outcome = ImpersonationNegativeCanary.Evaluate(
            appOnlyIds: Array.Empty<Guid>(),
            impersonatedIds: Array.Empty<Guid>());

        outcome.Verdict.Should().Be(CanaryVerdict.VacuousBaseline);
        outcome.Passed.Should().BeFalse();
    }

    /// <summary>
    /// Zero impersonated rows satisfies subset AND strictly-fewer arithmetically, but is equally
    /// consistent with a disabled canary user or a malformed query. Treating it as proof that the header
    /// worked would be the same category of error as treating equality as proof that it did.
    /// </summary>
    [Fact]
    public void Evaluate_WhenImpersonatedSetIsEmpty_DoesNotAcceptItAsEvidence()
    {
        var outcome = ImpersonationNegativeCanary.Evaluate(
            appOnlyIds: new[] { Id(1), Id(2) },
            impersonatedIds: Array.Empty<Guid>());

        outcome.Verdict.Should().Be(CanaryVerdict.EmptyImpersonatedSet);
        outcome.Passed.Should().BeFalse();
    }

    /// <summary>The passing shape — a genuine strict subset — must actually pass, or the gate is a wall.</summary>
    [Fact]
    public void Evaluate_WhenImpersonatedSetIsAStrictSubset_Passes()
    {
        var outcome = ImpersonationNegativeCanary.Evaluate(
            appOnlyIds: new[] { Id(1), Id(2), Id(3) },
            impersonatedIds: new[] { Id(1) });

        outcome.Passed.Should().BeTrue();
        outcome.Verdict.Should().Be(CanaryVerdict.Scoped);
    }

    /// <summary>
    /// Exactness catches the case subset-plus-strictly-fewer cannot: a header applied to the WRONG user
    /// still narrows the result, and would otherwise sail through Test 1.
    /// </summary>
    [Fact]
    public void EvaluateExactness_WhenImpersonatedSetIsNarrowedToTheWrongUser_Fails()
    {
        var outcome = ImpersonationNegativeCanary.EvaluateExactness(
            impersonatedIds: new[] { Id(7) },
            expectedSeedIds: new[] { Id(1), Id(2) });

        outcome.Verdict.Should().Be(CanaryVerdict.NotExactlyTheSeededSet);
        outcome.Message.Should().Contain(Id(7).ToString());
    }

    /// <summary>
    /// The canary environment must FAIL loudly rather than skip when unprovisioned, and the message must
    /// carry the provisioning contract — which user, which privilege, which seed count (spec NFR-01).
    /// A skipped canary and a passing canary look the same in a build log.
    /// </summary>
    [Fact]
    public void Require_WhenCanaryEnvironmentIsAbsent_ThrowsWithTheProvisioningContract()
    {
        using var _ = new ScopedEnvironment(
            (ImpersonationCanaryEnvironment.ServiceUrlVariable, null),
            (ImpersonationCanaryEnvironment.SystemUserIdVariable, null),
            (ImpersonationCanaryEnvironment.SeededMatterIdsVariable, null));

        var act = () => ImpersonationCanaryEnvironment.Require();

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().ContainAll(
                "FAILURE, not a skip",
                "User-level (basic) Read on sprk_matter",
                "prvActOnBehalfOfAnotherUser",
                ImpersonationCanaryEnvironment.SeededMatterIdsVariable);
    }

    /// <summary>
    /// A malformed canary systemuserid must be rejected at configuration-read time. <c>Guid.Empty</c> is
    /// the specific value the read primitive refuses, so letting it through would trade a clear
    /// provisioning error for an argument exception mid-query.
    /// </summary>
    [Fact]
    public void Require_WhenCanarySystemUserIdIsEmptyGuid_Throws()
    {
        using var _ = new ScopedEnvironment(
            (ImpersonationCanaryEnvironment.ServiceUrlVariable, "https://example.crm.dynamics.com"),
            (ImpersonationCanaryEnvironment.SystemUserIdVariable, Guid.Empty.ToString()),
            (ImpersonationCanaryEnvironment.SeededMatterIdsVariable, Id(1).ToString()));

        var act = () => ImpersonationCanaryEnvironment.Require();

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain(ImpersonationCanaryEnvironment.SystemUserIdVariable);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════
    // Layer 2 — LIVE TENANT. Tests 1–3 from investigation 08 §3d.
    //
    // These require the provisioned canary user. See TryAcquireCanary for how "must not skip"
    // (spec NFR-01) is honored without turning every credential-less CI run permanently red.
    // ══════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// TEST 1 — the header-not-applied catcher. The impersonated read of <c>sprk_matters</c> must be a
    /// strict subset of, and STRICTLY FEWER rows than, the identical app-only read.
    /// </summary>
    /// <remarks>
    /// The app-only baseline is issued by this test directly rather than through
    /// <c>DataverseWebApiService</c>, on purpose: it is the CONTROL. Routing both sides through the same
    /// production code path would let a defect in that path cancel itself out of the comparison.
    /// </remarks>
    [Fact]
    [Trait("Category", "LiveDataverseCanary")]
    public async Task ImpersonatedMatterRead_AgainstTheCanaryUser_ReturnsAStrictSubsetAndStrictlyFewerRows()
    {
        var canary = TryAcquireCanary();
        if (canary is null)
        {
            return; // NOT RUN — see TryAcquireCanary. Not a pass; the gate is held by the tripwire below.
        }

        var appOnlyIds = await ReadAppOnlyMatterIdsAsync(canary);
        var impersonatedIds = await ReadImpersonatedMatterIdsAsync(canary);

        appOnlyIds.Count.Should().BeLessThan(
            PageCap,
            "the app-only baseline hit the single-page cap, so the comparison would be truncation-"
            + "contaminated rather than a security signal — narrow the environment or the query first");

        var outcome = ImpersonationNegativeCanary.Evaluate(appOnlyIds, impersonatedIds);

        outcome.Passed.Should().BeTrue(outcome.Message);
    }

    /// <summary>
    /// TEST 2 — exactness. The impersonated set must be precisely the K seeded matters the canary user
    /// owns. Test 1 proves the header changed the answer; only this proves it changed it to the right one.
    /// </summary>
    [Fact]
    [Trait("Category", "LiveDataverseCanary")]
    public async Task ImpersonatedMatterRead_AgainstTheCanaryUser_ReturnsExactlyTheSeededMatters()
    {
        var canary = TryAcquireCanary();
        if (canary is null)
        {
            return; // NOT RUN — see TryAcquireCanary.
        }

        var impersonatedIds = await ReadImpersonatedMatterIdsAsync(canary);

        var outcome = ImpersonationNegativeCanary.EvaluateExactness(impersonatedIds, canary.SeededMatterIds);

        outcome.Passed.Should().BeTrue(outcome.Message);
    }

    /// <summary>
    /// TEST 3 — the empty-caller guard, pinned on the LIVE-configured service. Task 001's
    /// <c>ImpersonationFailClosedTests</c> pins the same refusal on a throwaway configuration; this
    /// re-pins it on the instance the canary actually uses, so the guard cannot become conditional on
    /// configuration that only the live path supplies. The refusal happens before any request is sent.
    /// </summary>
    [Fact]
    [Trait("Category", "LiveDataverseCanary")]
    public async Task RetrieveMultipleImpersonatedAsync_OnTheLiveConfiguredService_RefusesAnEmptyCaller()
    {
        var canary = TryAcquireCanary();
        if (canary is null)
        {
            return; // NOT RUN — see TryAcquireCanary.
        }

        using var http = new HttpClient();
        var service = NewLiveService(canary, http);

        var act = async () => await service.RetrieveMultipleImpersonatedAsync(
            ImpersonationCanaryEnvironment.EntitySetName,
            IdOnlyQuery,
            callerSystemUserId: Guid.Empty);

        (await act.Should().ThrowAsync<ArgumentException>(
                "an impersonated read MUST NOT degrade to an app-only org-wide query"))
            .And.ParamName.Should().Be("callerSystemUserId");
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════
    // Layer 3 — CONFIG TRIPWIRE. Runs everywhere, needs no tenant. This is the blocking-gate wiring.
    // ══════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The FR-20 swap may not be enabled in checked-in configuration unless the canary is provisioned for
    /// the run that carries the change.
    /// </summary>
    /// <remarks>
    /// <para>This is the mechanical half of "task 034 is a blocking merge gate for task 036". Task 036's
    /// flag (<c>ExternalAccess:ImpersonatedRootSets:Enabled</c>) defaults to <c>false</c> and rolls out
    /// through App Service configuration, so this test is silent for the normal case and fires only on
    /// the one action it exists to stop: flipping the checked-in default to <c>true</c> while the canary
    /// is unprovisioned — i.e. shipping the impersonated root-set path on by default with nothing
    /// watching whether impersonation is actually doing anything.</para>
    ///
    /// <para>It runs in the ordinary suite, with no secrets and no tenant, which is the only reason it is
    /// genuinely blocking today. See <c>tests/integration/auth/README.md</c> for why the LIVE canary is
    /// not (yet) a CI-blocking check and what the open decision is.</para>
    /// </remarks>
    [Fact]
    public void Fr20ImpersonatedRootSetFlag_WhenEnabledInCheckedInConfiguration_RequiresAProvisionedCanary()
    {
        AppSettingsFiles().Should().NotBeEmpty(
            "the scan must find the BFF settings files, or this gate passes vacuously");

        var enabledIn = Fr20FlagEnabledInCheckedInConfiguration();

        if (enabledIn.Length == 0)
        {
            return; // The flag is off by default (task 036's fail-safe rollout). Nothing to gate.
        }

        ImpersonationCanaryEnvironment.IsConfigured().Should().BeTrue(
            "the FR-20 impersonated root-set swap is enabled in checked-in configuration ({0}), so spec "
            + "NFR-04 requires the negative canary to run in this pipeline — an unwatched impersonation "
            + "path returns the app identity's org-wide rows with HTTP 200 when the header stops being "
            + "applied. Either provision the canary for this run (tests/integration/auth/README.md) or "
            + "return the checked-in default to false and roll the flag out through environment "
            + "configuration instead.",
            string.Join(", ", enabledIn));
    }

    /// <summary>
    /// Names of the checked-in BFF settings files in which the FR-20 impersonated-root-set flag is NOT
    /// demonstrably off. Empty is the expected steady state — task 036 ships the flag defaulted to
    /// <c>false</c> and rolls it out through environment configuration.
    /// </summary>
    /// <remarks>
    /// <para><b>Why a text scan rather than <c>ConfigurationBuilder</c>.</b>
    /// <c>appsettings.template.json</c> is a deploy-time token template, not valid JSON — it contains
    /// bare substitution tokens such as <c>"RecordMatchingEnabled": #{RECORD_MATCHING_ENABLED}#</c>, so
    /// a JSON parser throws on it. Skipping unparseable files would have created exactly the blind spot
    /// this gate exists to close: the template is the file a deployment actually renders.</para>
    ///
    /// <para><b>Anything that is not literally <c>false</c> counts as enabled.</b> A tokenized value
    /// (<c>#{...}#</c>) or a Key Vault reference is indeterminate at review time, and an indeterminate
    /// security flag is one a deployment can turn on without the canary ever being provisioned. The gate
    /// resolves that ambiguity toward requiring the canary.</para>
    /// </remarks>
    private static string[] Fr20FlagEnabledInCheckedInConfiguration() =>
        AppSettingsFiles()
            .Where(path => Fr20FlagIsNotDemonstrablyOff(File.ReadAllText(path)))
            .Select(path => Path.GetFileName(path))
            .ToArray();

    private static bool Fr20FlagIsNotDemonstrablyOff(string settingsText)
    {
        foreach (Match match in Fr20FlagPattern.Matches(settingsText))
        {
            var value = match.Groups["value"].Value.Trim().Trim('"');
            if (!string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Matches the task-036 rollout flag in both shapes it can be written: nested
    /// (<c>"ImpersonatedRootSets": { "Enabled": … }</c>) and flattened
    /// (<c>"ExternalAccess:ImpersonatedRootSets:Enabled": …</c>).
    /// </summary>
    private static readonly Regex Fr20FlagPattern = new(
        @"(?:""ImpersonatedRootSets""\s*:\s*\{[^}]*?""Enabled""|""[^""]*ImpersonatedRootSets:Enabled"")\s*:\s*(?<value>[^,}]+)",
        RegexOptions.IgnoreCase);

    private static string[] AppSettingsFiles() => Directory.GetFiles(
        Path.Combine(ResolveRepoRoot(), "src", "server", "api", "Sprk.Bff.Api"),
        "appsettings*.json",
        SearchOption.TopDirectoryOnly);

    // ══════════════════════════════════════════════════════════════════════════════════════════════
    // Live-tenant plumbing
    // ══════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Resolves the canary environment, throws when the gate is open but the canary is unprovisioned,
    /// and returns <c>null</c> when this run is simply not a canary run.
    /// </summary>
    /// <remarks>
    /// <para><b>The reconciliation.</b> Spec NFR-01 says an unprovisioned canary must FAIL, never skip,
    /// "because a skipped canary is an open gate". Taken literally against a class compiled into the
    /// always-run suite, that makes every credential-less run — every CI build, every developer machine —
    /// permanently red, and a permanently red gate is deleted within a week. So the rule is applied to
    /// its actual subject: the canary may not be absent WHILE THE GATE IS OPEN.</para>
    ///
    /// <para>The gate is open when either signal is present:
    /// <list type="bullet">
    ///   <item><c>SPAARKE_CANARY_REQUIRED=true</c> — the operator or pipeline asserting that this run is
    ///   a canary run. Provisioning then cannot be quietly dropped: the run fails.</item>
    ///   <item>the FR-20 flag is enabled in checked-in configuration — the impersonated root-set path is
    ///   shipping on by default, which is precisely when an unwatched impersonation path becomes an
    ///   org-wide disclosure. (Same condition as the Layer 3 tripwire, applied here to the live tests.)</item>
    /// </list>
    /// Absent both, the run is not a canary run and cannot honestly report one either way, so it halts
    /// as NOT RUN with a message that refuses to be read as a pass.</para>
    ///
    /// <para><b>Open item.</b> This repo has no pipeline that can reach Dataverse — CI holds no
    /// environment credential — so no automated run currently satisfies the first signal. That is
    /// escalated, not papered over; see <c>tests/integration/auth/README.md</c> § "What is NOT yet wired"
    /// and <c>projects/unified-access-control-r2/notes/task-034-negative-canary.md</c>.</para>
    /// </remarks>
    private static ImpersonationCanaryEnvironment? TryAcquireCanary()
    {
        if (ImpersonationCanaryEnvironment.IsConfigured())
        {
            return ImpersonationCanaryEnvironment.Require();
        }

        if (CanaryIsMandatoryForThisRun())
        {
            // Throws with the full provisioning contract. This is the "never silently skip" branch:
            // the gate is open, so an absent canary is a build failure.
            return ImpersonationCanaryEnvironment.Require();
        }

        // Not a canary run. xUnit 2.9 has no dynamic skip (no Assert.Skip, no SkippableFact package),
        // so the only two outcomes available are pass and fail — and a permanently failing test is a
        // deleted test. The not-run state is therefore made harmless rather than invisible: it cannot
        // coexist with an open gate, because Fr20ImpersonatedRootSetFlag_… below runs unconditionally
        // and fails if the FR-20 flag is enabled while the canary is unprovisioned.
        return null;
    }

    /// <summary>The two signals that make an unprovisioned canary a build failure rather than a non-run.</summary>
    private static bool CanaryIsMandatoryForThisRun() =>
        string.Equals(
            Environment.GetEnvironmentVariable(CanaryRequiredVariable), "true", StringComparison.OrdinalIgnoreCase)
        || Fr20FlagEnabledInCheckedInConfiguration().Length > 0;

    /// <summary>
    /// Signals that this run IS a canary run, so missing provisioning must fail rather than halt as
    /// not-run. Set by an operator following the README, or by any pipeline that gains Dataverse
    /// credentials.
    /// </summary>
    private const string CanaryRequiredVariable = "SPAARKE_CANARY_REQUIRED";

    /// <summary>Builds the production service on the managed-identity branch (ambient Azure credential).</summary>
    private static DataverseWebApiService NewLiveService(ImpersonationCanaryEnvironment canary, HttpClient http)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = canary.DataverseServiceUrl,
            ["Graph:ManagedIdentity:Enabled"] = "true"
        };

        if (!string.IsNullOrWhiteSpace(canary.ManagedIdentityClientId))
        {
            settings["ManagedIdentity:ClientId"] = canary.ManagedIdentityClientId;
        }

        return new DataverseWebApiService(
            http,
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            NullLogger<DataverseWebApiService>.Instance);
    }

    /// <summary>The impersonated side — through the PRODUCTION primitive tasks 035/036 will consume.</summary>
    private static async Task<IReadOnlyCollection<Guid>> ReadImpersonatedMatterIdsAsync(
        ImpersonationCanaryEnvironment canary)
    {
        using var http = new HttpClient();
        var service = NewLiveService(canary, http);

        var rows = await service.RetrieveMultipleImpersonatedAsync(
            ImpersonationCanaryEnvironment.EntitySetName,
            IdOnlyQuery,
            canary.CanarySystemUserId);

        return ExtractIds(rows);
    }

    /// <summary>
    /// The app-only side — the CONTROL, issued directly so it shares no code path with the impersonated
    /// read beyond the credential. Same entity set, same <c>$select</c>, same <c>$top</c>; the ONLY
    /// difference is the absent <c>MSCRMCallerID</c> header.
    /// </summary>
    private static async Task<IReadOnlyCollection<Guid>> ReadAppOnlyMatterIdsAsync(
        ImpersonationCanaryEnvironment canary)
    {
        var options = new DefaultAzureCredentialOptions();
        if (!string.IsNullOrWhiteSpace(canary.ManagedIdentityClientId))
        {
            options.ManagedIdentityClientId = canary.ManagedIdentityClientId;
        }

        var token = await new DefaultAzureCredential(options).GetTokenAsync(
            new TokenRequestContext(new[] { $"{canary.DataverseServiceUrl}/.default" }),
            CancellationToken.None);

        using var http = new HttpClient
        {
            BaseAddress = new Uri($"{canary.DataverseServiceUrl}/api/data/v9.2/")
        };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        http.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
        http.DefaultRequestHeaders.Add("OData-Version", "4.0");
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await http.GetAsync(
            $"{ImpersonationCanaryEnvironment.EntitySetName}?{IdOnlyQuery}");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<AppOnlyCollection>();
        return ExtractIds(payload?.Value ?? new List<Dictionary<string, JsonElement>>());
    }

    private static IReadOnlyCollection<Guid> ExtractIds(
        IReadOnlyList<Dictionary<string, JsonElement>> rows) =>
        rows
            .Where(row => row.ContainsKey(ImpersonationCanaryEnvironment.PrimaryKeyField))
            .Select(row => row[ImpersonationCanaryEnvironment.PrimaryKeyField].GetString())
            .Where(value => Guid.TryParse(value, out _))
            .Select(value => Guid.Parse(value!))
            .ToArray();

    private sealed class AppOnlyCollection
    {
        [System.Text.Json.Serialization.JsonPropertyName("value")]
        public List<Dictionary<string, JsonElement>> Value { get; set; } = new();
    }

    /// <summary>
    /// Walks up from the test assembly to the repo root (a directory holding both <c>src</c> and
    /// <c>tests</c>). Mirrors <c>OperationAccessPolicyCompletenessTests.ResolveRepoRoot</c>, including
    /// its refusal to fall back: a wrong root would make the config tripwire scan nothing and pass.
    /// </summary>
    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src")) &&
                Directory.Exists(Path.Combine(dir.FullName, "tests")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the repo root (a directory containing both 'src' and 'tests') by walking up "
            + $"from '{AppContext.BaseDirectory}'. This gate scans source, so it fails loudly rather than "
            + "silently scanning nothing.");
    }

    private static Guid Id(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:D12}");

    /// <summary>
    /// Sets environment variables for the duration of a test and restores them afterwards, so a
    /// developer running the suite on a machine that HAS canary configuration still exercises the
    /// unprovisioned path deterministically.
    /// </summary>
    private sealed class ScopedEnvironment : IDisposable
    {
        private readonly (string Name, string? Previous)[] _saved;

        public ScopedEnvironment(params (string Name, string? Value)[] variables)
        {
            _saved = variables
                .Select(v => (v.Name, Environment.GetEnvironmentVariable(v.Name)))
                .ToArray();

            foreach (var (name, value) in variables)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        public void Dispose()
        {
            foreach (var (name, previous) in _saved)
            {
                Environment.SetEnvironmentVariable(name, previous);
            }
        }
    }
}

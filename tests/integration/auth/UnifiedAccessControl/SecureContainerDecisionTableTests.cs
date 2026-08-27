using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 075 — the C# half of the SHARED decision table.
///
/// <para><b>What this test is for.</b> The record-aware container decision exists twice: here in C#
/// (<see cref="SecureContainerDecision"/>) and in TypeScript (<c>decideContainer</c> in
/// <c>src/client/shared/Spaarke.UI.Components/src/services/RecordContainerResolver.ts</c>). Two
/// implementations exist because INV-7 keeps business-unit resolution client-side while server-side email
/// ingest has no client at all. Two implementations of an ISOLATION rule is a known failure mode, so the
/// rule is pinned in ONE machine-readable place — <c>tests/fixtures/secure-container-decision-table.json</c>
/// — and BOTH halves' suites drive their own pure decision function against that same file.</para>
///
/// <para>The drift mechanics are therefore mechanical rather than promised: change this half's behaviour and
/// this test fails; edit the fixture to suit this half and the TypeScript suite fails; add a case and both
/// halves must implement it.</para>
///
/// <para><b>Why the vacuous-pass guards matter.</b> A fixture-driven suite that silently stops finding its
/// fixture, or finds it and iterates zero rows, passes green while verifying nothing — the same failure shape
/// task 074 guarded against with <c>ScannerAccountsForEveryRegistrationInTheGovernedFiles</c>. So this file
/// asserts the fixture is found, that the declared <c>caseCount</c> matches the rows actually present, and
/// that every case name was exercised.</para>
/// </summary>
public class SecureContainerDecisionTableTests
{
    private const string FixtureRelativePath = "tests/fixtures/secure-container-decision-table.json";

    [Fact(DisplayName = "Task 075: the C# decision matches every case in the shared decision table")]
    public void CSharpDecision_MatchesEveryCaseInTheSharedTable()
    {
        var table = LoadTable();
        var exercised = new List<string>();

        foreach (var testCase in table.Cases)
        {
            var actual = SecureContainerDecision.Decide(
                testCase.IsSecure, testCase.OwnContainerId, testCase.FallbackContainerId);

            var expectedOutcome = testCase.Expect.Outcome switch
            {
                "resolved-secure" => ContainerDecisionOutcome.ResolvedSecure,
                "resolved-fallback" => ContainerDecisionOutcome.ResolvedFallback,
                "unresolved" => ContainerDecisionOutcome.Unresolved,
                "fail-closed" => ContainerDecisionOutcome.FailClosed,
                _ => throw new InvalidOperationException(
                    $"Case '{testCase.Name}' declares unknown outcome '{testCase.Expect.Outcome}'. The "
                    + "fixture's own 'outcomes' block enumerates the legal values; add the mapping here and "
                    + "in the TypeScript half if a new one is genuinely needed.")
            };

            actual.Outcome.Should().Be(
                expectedOutcome,
                "decision table case '{0}' — {1}", testCase.Name, testCase.Why);

            actual.ContainerId.Should().Be(
                testCase.Expect.ContainerId,
                "decision table case '{0}' must resolve to the container the table names (and to null when "
                + "it names none) — {1}", testCase.Name, testCase.Why);

            exercised.Add(testCase.Name);
        }

        // Vacuous-pass guards.
        exercised.Should().HaveCount(
            table.CaseCount,
            "the fixture declares caseCount={0} but {1} case(s) were exercised. A fixture-driven suite that "
            + "iterates fewer rows than the file declares passes green while verifying less than it claims.",
            table.CaseCount, exercised.Count);

        exercised.Should().OnlyHaveUniqueItems(
            "duplicate case names mean one of them is unreviewed, and it becomes ambiguous which behaviour "
            + "the TypeScript half is supposed to match.");
    }

    [Fact(DisplayName = "Task 075: the decision table actually covers the fail-closed branch")]
    public void TheTable_CoversTheFailClosedBranch_WithAFallbackAvailable()
    {
        // The single most important case in the task, asserted to be PRESENT rather than merely passing.
        // A table that lost this row would still be internally consistent and both halves would still agree
        // — on nothing. The distinguishing detail is that a fallback IS available and must not be used;
        // "fail closed when there is nothing to fall back to" is a much weaker claim and easy to satisfy by
        // accident.
        var table = LoadTable();

        var failClosedWithFallback = table.Cases
            .Where(c => c.IsSecure
                        && c.Expect.Outcome == "fail-closed"
                        && !string.IsNullOrWhiteSpace(c.FallbackContainerId))
            .ToList();

        failClosedWithFallback.Should().NotBeEmpty(
            "the decision table MUST contain at least one case where a secure record has no container of "
            + "its own WHILE a usable non-secure fallback is available, and the expected outcome is "
            + "fail-closed. That is the entire point of task 075: SPE permissions are additive-only, so "
            + "silently using the available fallback would place secure content in a shared container "
            + "permanently and the upload would SUCCEED.");
    }

    [Fact(DisplayName = "Task 075: 'unresolved' is unreachable for a secure record, in the table and in the code")]
    public void Unresolved_IsUnreachable_ForASecureRecord()
    {
        // The load-bearing invariant. 'Unresolved' is the benign config-absence outcome, and callers are
        // allowed to skip quietly on it (the ingest path does exactly that when ArchiveContainerId is
        // unset). If a secure record could ever produce it, a secure record's content would reach a
        // quiet-skip path that is indistinguishable from success at the call site.
        var table = LoadTable();

        table.Cases
            .Where(c => c.IsSecure)
            .Should().OnlyContain(
                c => c.Expect.Outcome != "unresolved",
                "no case in the shared table may expect 'unresolved' for a secure record.");

        // And the code, independently of the table — exhaustively over the blank forms a container id can
        // take, crossed with the blank forms a fallback can take.
        string?[] blanks = [null, "", "   ", "\t"];

        foreach (var own in blanks)
        {
            foreach (var fallback in blanks)
            {
                SecureContainerDecision.Decide(isSecure: true, own, fallback)
                    .Outcome.Should().Be(
                        ContainerDecisionOutcome.FailClosed,
                        "a secure record with a blank container ('{0}') and fallback ('{1}') must fail "
                        + "closed, never resolve and never report Unresolved",
                        own ?? "null", fallback ?? "null");
            }
        }
    }

    [Fact(DisplayName = "Task 075: a resolved outcome always carries a container id, and a non-resolved one never does")]
    public void ResolvedOutcomes_AlwaysCarryAContainerId()
    {
        // Guards the contract IRecordContainerResolver documents. A ResolvedSecure with a null container id
        // would satisfy every outcome assertion above and still hand a null container to SpeFileStore.
        string?[] values = [null, "", "  ", "b!x", "  b!x  "];

        foreach (var isSecure in new[] { true, false })
        {
            foreach (var own in values)
            {
                foreach (var fallback in values)
                {
                    var d = SecureContainerDecision.Decide(isSecure, own, fallback);

                    switch (d.Outcome)
                    {
                        case ContainerDecisionOutcome.ResolvedSecure:
                        case ContainerDecisionOutcome.ResolvedFallback:
                            d.ContainerId.Should().NotBeNullOrWhiteSpace(
                                "a resolved decision must carry a usable container id");
                            d.ContainerId.Should().Be(
                                d.ContainerId!.Trim(), "container ids are trimmed identically in both halves");
                            break;

                        default:
                            d.ContainerId.Should().BeNull(
                                "a non-resolved decision must not carry a container id, or a caller that "
                                + "reads ContainerId without checking Outcome would use it");
                            break;
                    }
                }
            }
        }
    }

    // ---------------------------------------------------------------------------------------------

    private static DecisionTable LoadTable()
    {
        var path = Path.Combine(ResolveRepoRoot(), FixtureRelativePath.Replace('/', Path.DirectorySeparatorChar));

        File.Exists(path).Should().BeTrue(
            "the SHARED decision table must be reachable from the test assembly, otherwise this suite "
            + "verifies nothing and the TypeScript half is unpinned. Looked for '{0}'", path);

        var table = JsonSerializer.Deserialize<DecisionTable>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        table.Should().NotBeNull("the decision table must deserialize");
        table!.Cases.Should().NotBeNullOrEmpty("an empty decision table pins nothing");

        return table;
    }

    /// <summary>
    /// Walks up from the test assembly looking for the repo root (a directory holding both <c>src</c> and
    /// <c>tests</c>). Throws rather than falling back, matching
    /// <c>OperationAccessPolicyCompletenessTests.ResolveRepoRoot</c>: a wrong root would make the fixture
    /// load fail in a way that could be mistaken for an absent file.
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
            $"Could not locate the repo root walking up from '{AppContext.BaseDirectory}'.");
    }

    private sealed record DecisionTable(int CaseCount, List<DecisionCase> Cases);

    private sealed record DecisionCase(
        string Name,
        bool IsSecure,
        string? OwnContainerId,
        string? FallbackContainerId,
        ExpectedDecision Expect,
        string Why);

    private sealed record ExpectedDecision(string Outcome, string? ContainerId);
}

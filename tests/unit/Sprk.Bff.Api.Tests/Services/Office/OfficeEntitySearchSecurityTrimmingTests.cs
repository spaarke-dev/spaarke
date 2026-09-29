using System.Text.RegularExpressions;
using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Office;

/// <summary>
/// unified-access-control-r2 task 126 / GitHub #1021 — the Office entity search must return only what
/// the CALLER may see.
/// </summary>
/// <remarks>
/// <para><b>Why these are SOURCE guards rather than behavioural tests, stated plainly.</b>
/// <c>OfficeService</c> has twelve-plus concrete constructor dependencies and no test in this repo
/// constructs one. A behavioural test would therefore have to be an integration test against a real
/// Dataverse with two differently-permissioned users — which is a manual gate in this project, not CI
/// (owner directive: no Dataverse test in CI).</para>
///
/// <para>The alternative was tempting and wrong: stand up a fake <see cref="IDataverseUserClient"/>,
/// assert it gets called, and assert some arithmetic about counts. That tests the fake and the test's
/// own local variables — it would pass just as happily if <c>OfficeService</c> went back to the
/// app-only client tomorrow. This project has already been bitten by exactly that (a Rule-B test that
/// iterated the same list it checked, session 25), so the shape is avoided here deliberately.</para>
///
/// <para>What these DO prove is the property that can actually regress: that the search path still
/// reaches for the delegated client and cannot quietly revert to the app-only one. Source scanning is
/// the established mechanism for that in this repo — <c>RouteAuthorizationGuardTests</c> works the
/// same way.</para>
///
/// <para>⚠️ None of this asserts that Dataverse trims correctly. That is the platform's own security
/// model; testing it here would be testing Dataverse.</para>
/// </remarks>
[Trait("Category", "Security")]
public class OfficeEntitySearchSecurityTrimmingTests
{
    private static string ReadOfficeServiceSource()
    {
        var path = Path.Combine(
            RepoRoot(),
            "src", "server", "api", "Sprk.Bff.Api", "Services", "Office", "OfficeService.cs");

        File.Exists(path).Should().BeTrue($"OfficeService.cs must be locatable at {path}");
        return File.ReadAllText(path);
    }

    private static string RepoRoot()
    {
        // ⚠️ In a git WORKTREE — which is how this repo is normally developed — `.git` is a FILE
        // containing "gitdir: ...", not a directory. A Directory.Exists-only probe walks past the
        // root and returns null, which is how the first version of this guard failed everywhere at
        // once rather than failing where the real defect would be.
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var git = Path.Combine(dir, ".git");
            if (Directory.Exists(git) || File.Exists(git))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException(
            "Could not locate the repository root (no .git file or directory found walking up from "
            + AppContext.BaseDirectory + ").");
    }

    /// <summary>
    /// The body of <c>QuerySearchEntityAsync</c> — the single method that issues the search read.
    /// </summary>
    private static string QuerySearchEntityBody()
    {
        var source = ReadOfficeServiceSource();

        var start = source.IndexOf(
            "private async Task<List<EntitySearchResult>> QuerySearchEntityAsync",
            StringComparison.Ordinal);
        start.Should().BeGreaterThan(
            -1,
            "QuerySearchEntityAsync must still exist — if it was renamed, update this guard rather "
            + "than deleting it");

        // Up to the next method declaration at the same indentation.
        var next = source.IndexOf("\n    private ", start + 10, StringComparison.Ordinal);
        if (next < 0) next = source.Length;

        return source[start..next];
    }

    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void TheDelegatedClient_IsGeneralInfrastructure_NotAiInternalPlumbing()
    {
        // The fix rests on this type being reachable from CRUD code. It lived under
        // Services/Ai/Handlers/Dataverse/ and described itself as "AI-internal plumbing", which under
        // ADR-013 put the BFF's only per-user Dataverse client formally off-limits to every non-AI
        // caller that needed security trimming. Task 126 relocated it (owner-approved §6.5 path C).
        // Move it back and this fails.
        typeof(IDataverseUserClient).Namespace
            .Should().Be(
                "Sprk.Bff.Api.Infrastructure.Dataverse",
                "CRUD callers needing per-user trimming must be able to use the delegated Dataverse "
                + "client without violating ADR-013's AI-facade rule");
    }

    [Fact]
    public void TheEntitySearchRead_GoesThroughTheDelegatedClient()
    {
        QuerySearchEntityBody()
            .Should().Contain(
                "_userClient!.GetAsync",
                "the entity search must execute under the CALLER's Dataverse security context so "
                + "Dataverse trims the result set (GitHub #1021)");
    }

    [Fact]
    public void TheEntitySearchRead_DoesNotTouchTheAppOnlyClient()
    {
        // 🔴 The regression that matters. The app-only client returns every matching row in the
        // tenant regardless of who asked — that WAS this route's defect. A well-meaning change to
        // "fix" a permissions complaint, or a revert, would reintroduce it, and no behavioural test
        // in CI would notice.
        QuerySearchEntityBody()
            .Should().NotContain(
                "_dataverseClient",
                "the app-only Dataverse client must never be used for the entity search — it ignores "
                + "the caller's permissions, which is the whole of GitHub #1021");
    }

    [Fact]
    public void AnAuthorizationFailure_IsNotSwallowedIntoAnEmptyResultSet()
    {
        var body = QuerySearchEntityBody();

        body.Should().MatchRegex(
            @"401\s+or\s+403",
            "a denied delegated context must be distinguished from 'no matches'");
        body.Should().Contain(
            "throw new InvalidOperationException",
            "a denied delegated context must propagate; swallowing it would empty every entity type "
            + "and present a broken security context as a successful search");
    }

    [Fact]
    public void TheBestEffortCatch_DoesNotSwallowTheAuthorizationFailure()
    {
        // The per-type loop is deliberately best-effort for DATA faults so one entity cannot break
        // the picker. If that catch were widened back to a bare `catch (Exception)`, it would
        // re-swallow the authorization throw above and the previous guard would become decorative.
        ReadOfficeServiceSource()
            .Should().Contain(
                "catch (Exception ex) when (ex is not InvalidOperationException)",
                "the per-type best-effort catch must continue to exclude the authorization failure");
    }

    [Fact]
    public void CallerVisibleCounts_AreDerivedFromTheReturnedRows_NotFromARawMatchTotal()
    {
        // Why this is guarded at all: post-filtering an app-only result set would trim the ROWS but
        // leave TotalCount reporting how many records matched the substring. A caller learning
        // "847 matters contain 'ac'" without being allowed to see one is the same disclosure in a
        // smaller box — the enumeration-oracle shape task 022 removed from bulk download.
        //
        // `ordered` is the post-query set; both counts must come from it.
        var source = ReadOfficeServiceSource();

        source.Should().MatchRegex(
            @"TotalCount\s*=\s*ordered\.Count",
            "TotalCount must be derived from the returned (already-trimmed) set");
        source.Should().MatchRegex(
            @"HasMore\s*=\s*ordered\.Count",
            "HasMore must be derived from the returned (already-trimmed) set");
    }

    [Fact]
    public void TheSearch_RefusesToRunWithoutTheDelegatedClient_RatherThanFallingBack()
    {
        // Fail-closed, per ADR-003. A fallback to the app-only client would restore the tenant-wide
        // enumeration while every test stayed green — the shape of the JobOwnershipFilter fail-open
        // that task 120 closed.
        ReadOfficeServiceSource()
            .Should().Contain(
                "_userClient is null && _dataverseClient is not null",
                "entity search must refuse to run when the delegated client is unavailable instead of "
                + "silently falling back to the app-only path");
    }
}

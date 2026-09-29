using FluentAssertions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Office;

/// <summary>
/// unified-access-control-r2 task 127 / GitHub #1020 — the three
/// <c>/api/office/communications</c> reads must stay delegated.
/// </summary>
/// <remarks>
/// <para>Source guards, for the same reason as
/// <see cref="OfficeEntitySearchSecurityTrimmingTests"/>: the behavioural contract is covered by
/// <c>CommunicationsEndpointsContractTests</c>, but those program a MOCK of the delegated client — so
/// they would pass unchanged if a handler quietly went back to the app-only service for one of its
/// reads. What can actually regress here is the CHOICE OF CLIENT, and that is what these pin.</para>
///
/// <para>The regression is not hypothetical. Every read on this surface was app-only until
/// 2026-09-29, the caller's object id was resolved on all three handlers and used only in log
/// statements, and the class-level documentation described app-only access as the intended pattern.
/// A change that "restores the existing BFF pattern" would reopen all three holes and break no
/// behavioural test.</para>
/// </remarks>
[Trait("Category", "Security")]
public class CommunicationsDelegatedReadGuardTests
{
    private static string Source()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            // ⚠️ `.git` is a FILE in a git worktree, which is how this repo is normally developed.
            var git = Path.Combine(dir, ".git");
            if (Directory.Exists(git) || File.Exists(git))
            {
                var path = Path.Combine(
                    dir, "src", "server", "api", "Sprk.Bff.Api", "Api", "Office",
                    "CommunicationsEndpoints.cs");
                File.Exists(path).Should().BeTrue($"CommunicationsEndpoints.cs must exist at {path}");
                return File.ReadAllText(path);
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }

    [Fact]
    public void NoHandlerOnThisSurface_ReadsThroughTheAppOnlyService()
    {
        // 🔴 The regression that matters. IGenericEntityService is an app-only singleton: it cannot
        // carry per-request user context, so any read through it returns rows regardless of who
        // asked. Documentation references are fine; a code reference is not.
        var source = Source();

        var codeReferences = source
            .Split('\n')
            .Where(line => !line.TrimStart().StartsWith("///", StringComparison.Ordinal))
            .Where(line => line.Contains("entityService", StringComparison.Ordinal)
                        || line.Contains("IGenericEntityService", StringComparison.Ordinal))
            .ToList();

        codeReferences.Should().BeEmpty(
            "every read on /api/office/communications must go through the DELEGATED client so "
            + "Dataverse applies the caller's security model (GitHub #1020); found: {0}",
            string.Join(" | ", codeReferences.Select(l => l.Trim())));
    }

    [Fact]
    public void AllThreeReads_GoThroughTheDelegatedClient()
    {
        var source = Source();

        // The communication lookup, the candidate-name resolution and the linked-todos query.
        System.Text.RegularExpressions.Regex
            .Matches(source, @"userClient\.GetAsync")
            .Count
            .Should().BeGreaterThanOrEqualTo(
                3,
                "the communication lookup, the candidate display-name resolution and the linked-todos "
                + "query must each read under the caller's context");
    }

    [Fact]
    public void AnUnreadableCandidate_IsDropped_NotMerelyLeftUnnamed()
    {
        // The second-order leak. Returning the candidate id without its name would still disclose
        // that the record exists and was associated with the email — the same disclosure, minus the
        // label. The old code swallowed every resolution failure into "no name", which under a
        // delegated client would have turned a denial into a silent omission.
        Source().Should().Contain(
            "not readable by caller",
            "a candidate the caller cannot read must be dropped from the suggestions response");
    }

    [Fact]
    public void AuthorizationFailures_AreNotReportedAsAbsence()
    {
        var source = Source();

        source.Should().MatchRegex(
            @"401\s+or\s+403",
            "a broken delegated context must be distinguished from 'no such record'");
        source.Should().Contain(
            "throw new InvalidOperationException",
            "a broken delegated context must propagate rather than rendering as a normal 404, which "
            + "the add-in would show as an ordinary no-preselection state");
    }
}

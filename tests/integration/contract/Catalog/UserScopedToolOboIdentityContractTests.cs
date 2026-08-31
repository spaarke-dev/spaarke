using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Contract.Catalog;

/// <summary>
/// FR-05 / QW1 (spaarkeai-assistant-enhancements-r4 task 020) — the OBO-identity-assertion
/// regression net for identity-scoped grounded tools.
/// </summary>
/// <remarks>
/// <para>
/// <b>The defect this guards (P2 dead-end)</b>: a grounded tool whose results are filtered by
/// <em>who the caller is</em> (their own tasks, their own briefing) tempted the model to ask the
/// user "what is your id / name?" to scope the query — a dead-end, because the tool already runs
/// under the caller's OBO token (ADR-028) and the identity is known. The fix (shipped in R3 for the
/// two identity-scoped overview tools, and locked here) is a description directive telling the model
/// it already knows the user and must NEVER ask for their identity.
/// </para>
/// <para>
/// <b>Why only these two tools (accurate scoping, root CLAUDE.md §11 — default to reuse, do not
/// spray)</b>: the assertion is semantically about tools whose results are scoped BY the caller's
/// identity. Those are <c>spaarke.grid_overview</c> (the caller's My-Tasks/owned-record grids) and
/// <c>spaarke.daily_briefing_overview</c> (the caller's own portfolio briefing). The remaining
/// <c>dataverse-user-context</c> tools are deliberately NOT in scope:
/// <list type="bullet">
///   <item>the generic <c>dataverse.*</c> MCP tools (read_query/search_data/describe/create/update/
///   delete) query an explicitly-named table, NOT "the caller's own records", and their descriptions
///   are frozen against the GA Dataverse MCP surface (<c>DataverseToolNameFreezeTests</c>) — the
///   identity assertion does not apply and would be noise;</item>
///   <item>the <c>email.*</c> and <c>memory.write</c> tools act under the caller's OBO identity but
///   are parameterized by recipients / memory content, not by "who the caller is", so they do not
///   provoke an identity ask.</item>
/// </list>
/// This is the same data-derived, non-arbitrary scoping discipline
/// <see cref="CatalogToolDescriptionParityContractTests"/> uses. Byte-parity between the handler
/// <c>Metadata.Description</c> and the seed-row <c>sprk_description</c> is already guarded there; the
/// gap this test closes is that byte-parity alone would NOT catch removing the directive from BOTH
/// mirrors at once.
/// </para>
/// </remarks>
public class UserScopedToolOboIdentityContractTests
{
    // Stable substrings of the shipped OBO-identity directive (kept short so incidental wording
    // tweaks around them do not break the guard — the two clauses that carry the load-bearing
    // instruction are what must survive).
    private const string OboIdentityClause = "runs under the CALLING USER's identity (OBO) automatically";
    private const string NeverAskClause = "NEVER ask the user for their user id";

    [Theory]
    [InlineData("sprk_analysistool-grid-overview-row.json")]
    [InlineData("sprk_analysistool-daily-briefing-overview-row.json")]
    public void IdentityScopedToolDescription_AssertsOboIdentity_AndForbidsAskingWhoTheUserIs(string rowFile)
    {
        var description = ReadSeedDescription(rowFile);

        description.Should().Contain(
            OboIdentityClause,
            because: "FR-05/QW1: an identity-scoped grounded tool MUST tell the model it already runs " +
                     "under the caller's OBO identity, so the model does not ask the user who they are.");
        description.Should().Contain(
            NeverAskClause,
            because: "FR-05/QW1: the description MUST forbid asking the user for their id/name to scope " +
                     "results (the P2 dead-end). Removing this directive re-opens the defect.");
    }

    private static string ReadSeedDescription(string rowFile)
    {
        var path = Path.Combine(FindRepoRoot(), "infra", "dataverse", rowFile);
        File.Exists(path).Should().BeTrue($"the identity-scoped tool seed row '{rowFile}' must exist");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        doc.RootElement.TryGetProperty("sprk_description", out var d).Should().BeTrue(
            $"'{rowFile}' must carry an authored sprk_description");
        d.ValueKind.Should().Be(JsonValueKind.String);
        return d.GetString()!;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Spaarke.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the repo root (Spaarke.sln) from AppContext.BaseDirectory — " +
            "the user-scoped OBO-identity contract assertions require an in-repo test run.");
    }
}

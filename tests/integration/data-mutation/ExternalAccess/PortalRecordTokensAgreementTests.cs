using System.Text.RegularExpressions;
using FluentAssertions;
using Sprk.Bff.Api.Api.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// Agreement guard for the portal's record-link tokens (spaarke-ontology-platform-r1 task 160, fix round 1, R3 review; enforcement
/// ladder). <see cref="GrantAccessNotifier.PortalRecordTokens"/> is a hand copy of <c>RECORD_LINK_TOKENS</c> in the external SPA's
/// <c>src/client/external-spa/src/components/detail/record-targets.ts</c>, whose parser is the authority for which
/// <c>?record={type}:{id}</c> tokens open anything. A token the server builds that the SPA does not parse is a link that opens
/// nothing; a token the SPA gained and the server lacks is a record the inquiry email cannot link to.
/// </summary>
/// <remarks>
/// <para><b>Controls.</b> <see cref="Disagreement"/> is exercised on seeded key sets: it fires on a missing key and on an extra key
/// (must-fire) and passes equal sets in any order (must-not-fire). <see cref="TokenKeys"/> is exercised on seeded TypeScript
/// (a modified key list is seen). The real-file test reads the repo's file; the mutation (a key removed from, or added to, either
/// side) fails <see cref="TheServerTokens_AreTheSpaRecordLinkTokens"/>.</para>
/// <para><b>Maintenance.</b> If the SPA's tokens change, change <c>PortalRecordTokens</c> in the same PR (and the reverse). Do not
/// edit the expectation here to make it pass.</para>
/// </remarks>
public class PortalRecordTokensAgreementTests
{
    private const string SpaFile = "src/client/external-spa/src/components/detail/record-targets.ts";

    [Fact]
    public void TheServerTokens_AreTheSpaRecordLinkTokens()
    {
        var spa = TokenKeys(File.ReadAllText(Path.Combine(RepoRoot(), SpaFile.Replace('/', Path.DirectorySeparatorChar))));

        spa.Should().NotBeEmpty("the parser found RECORD_LINK_TOKENS in the SPA file");
        Disagreement(GrantAccessNotifier.PortalRecordTokens, spa).Should().BeNull();
    }

    [Fact]
    public void Disagreement_FiresOnAMissingOrAnExtraKey_AndPassesEqualSetsInAnyOrder()
    {
        string[] spa = ["project", "matter", "invoice"];

        // Must fire.
        Disagreement(new HashSet<string> { "project", "matter" }, spa).Should().Contain("invoice");
        Disagreement(new HashSet<string> { "project", "matter", "invoice", "event" }, spa).Should().Contain("event");

        // Must not fire.
        Disagreement(new HashSet<string> { "invoice", "project", "matter" }, spa).Should().BeNull();
    }

    [Fact]
    public void TokenKeys_ReadsTheKeysOfTheRecordLinkTokensObjectOnly()
    {
        const string ts = """
            export const RECORD_LINK_TOKENS: Readonly<Record<string, string>> = {
              project: 'sprk_project',
              matter: 'sprk_matter',
              servicerequest: 'sprk_servicerequest',
            };

            export const FEED_RECORD_TYPES: Readonly<Record<string, string>> = {
              projects: 'sprk_project',
            };
            """;

        TokenKeys(ts).Should().BeEquivalentTo("project", "matter", "servicerequest");
        TokenKeys(ts.Replace("matter: 'sprk_matter',", string.Empty)).Should().BeEquivalentTo("project", "servicerequest");
    }

    /// <summary>Null when the sets are equal, else which keys differ.</summary>
    internal static string? Disagreement(IReadOnlySet<string> server, IEnumerable<string> spa)
    {
        var client = spa.ToHashSet(StringComparer.Ordinal);
        var onlyServer = server.Where(k => !client.Contains(k)).Order().ToArray();
        var onlySpa = client.Where(k => !server.Contains(k)).Order().ToArray();
        return onlyServer.Length == 0 && onlySpa.Length == 0
            ? null
            : $"only in GrantAccessNotifier.PortalRecordTokens: [{string.Join(", ", onlyServer)}]; only in the SPA's RECORD_LINK_TOKENS: [{string.Join(", ", onlySpa)}]";
    }

    /// <summary>The keys of the <c>RECORD_LINK_TOKENS</c> object literal in <paramref name="typeScript"/>.</summary>
    internal static IReadOnlyList<string> TokenKeys(string typeScript)
    {
        var body = Regex.Match(typeScript, @"RECORD_LINK_TOKENS[^=]*=\s*\{(?<body>[^}]*)\}", RegexOptions.CultureInvariant);
        if (!body.Success)
            return [];

        return Regex.Matches(body.Groups["body"].Value, @"^\s*['""]?(?<key>[A-Za-z0-9_]+)['""]?\s*:", RegexOptions.Multiline | RegexOptions.CultureInvariant)
            .Select(m => m.Groups["key"].Value)
            .ToArray();
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var git = Path.Combine(dir, ".git");
            if (Directory.Exists(git) || File.Exists(git))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}

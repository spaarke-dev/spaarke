using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>One topic section of the pinned playbook: its id and the position text under it.</summary>
internal sealed record PinnedNdaTopic(string Id, string Position);

/// <summary>
/// The NDA standard the edit plan is written against, consumed at a pinned version (spaarke-intake-pipeline-r1 NFR-09):
/// "Spaarke NDA – Mutual" v1.2, the accelerator's topic-keyed playbook, embedded verbatim from
/// <c>src/accelerators/practices/nda/positions/</c>. The name, the version, the topic ids and the position text all
/// come from that one file, so the stamp on a plan can never name a version other than the one the model was shown.
/// </summary>
/// <remarks>
/// <para>
/// Moving to a new standard version is a deliberate change: replace the embedded copy, and
/// <c>AgreementEditPlanPinnedStandardContractTests</c> (which compares the copy with the accelerator's file and its
/// <c>topics.yaml</c>) tells the author what else has to follow. Counsel ratification of the standard is a rollout gate, not
/// this class's concern.
/// </para>
/// <para>The same embedded-resource approach as <c>RuleBodySchemaValidator</c>'s schemas: no on-disk path at runtime.</para>
/// </remarks>
internal static class PinnedNdaStandard
{
    internal const string PlaybookResource = "Sprk.Bff.Api.Standards.spaarke-nda-mutual-v1.2.md";

    internal const string EditPlanSchemaResource = "Sprk.Bff.Api.Standards.edit-plan.schema.json";

    private static readonly Regex TitleLine = new(
        @"^#\s*Playbook:\s*(?<name>.+?)\s+v(?<version>\d+(?:\.\d+)*)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex VersionLine = new(
        @"^Version:\s*(?<version>\d+(?:\.\d+)*)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TopicHeading = new(
        @"^##\s*topic:\s*(?<id>[a-z]+\.[a-z0-9-]+)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Lazy<Parsed> Loaded = new(() => Parse(ReadResource(PlaybookResource)));

    /// <summary>The playbook's title without its version, for example "Spaarke NDA – Mutual".</summary>
    public static string Name => Loaded.Value.Name;

    /// <summary>The playbook's version, for example "1.2".</summary>
    public static string Version => Loaded.Value.Version;

    /// <summary>The playbook's scope paragraph and header line (side represented, jurisdiction, what it is not for).</summary>
    public static string Scope => Loaded.Value.Scope;

    /// <summary>The topics in playbook order.</summary>
    public static IReadOnlyList<PinnedNdaTopic> Topics => Loaded.Value.Topics;

    /// <summary>The topic ids, for membership checks.</summary>
    public static FrozenSet<string> TopicIds => Loaded.Value.TopicIds;

    /// <summary>Reads one embedded resource as UTF-8 text.</summary>
    internal static string ReadResource(string resourceName)
    {
        var assembly = typeof(PinnedNdaStandard).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' is missing from {assembly.GetName().Name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Parses a playbook in the accelerator's format. Throws <see cref="InvalidOperationException"/> when it is malformed.</summary>
    internal static Parsed Parse(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var title = TitleLine.Match(lines.FirstOrDefault()?.Trim() ?? string.Empty);
        if (!title.Success)
        {
            throw new InvalidOperationException("The pinned NDA standard has no '# Playbook: <name> v<version>' title line.");
        }

        var name = title.Groups["name"].Value;
        var version = title.Groups["version"].Value;

        var scope = new List<string>();
        var topics = new List<PinnedNdaTopic>();
        string? currentId = null;
        var body = new List<string>();

        void CloseTopic()
        {
            if (currentId is not null)
            {
                topics.Add(new PinnedNdaTopic(currentId, string.Join('\n', body).Trim()));
            }

            body.Clear();
        }

        foreach (var raw in lines.Skip(1))
        {
            var line = raw.TrimEnd();
            var heading = TopicHeading.Match(line);
            if (heading.Success)
            {
                CloseTopic();
                currentId = heading.Groups["id"].Value;
                continue;
            }

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                // A heading that is not a topic ends the topic above it; the format has no other headings today.
                CloseTopic();
                currentId = null;
                continue;
            }

            if (currentId is null)
            {
                scope.Add(line);
            }
            else
            {
                body.Add(line);
            }
        }

        CloseTopic();

        var headerVersion = scope
            .Select(l => VersionLine.Match(l))
            .FirstOrDefault(m => m.Success)?.Groups["version"].Value;
        if (!string.Equals(headerVersion, version, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The pinned NDA standard names two different versions (title line and 'Version:' header line).");
        }

        if (topics.Count == 0)
        {
            throw new InvalidOperationException("The pinned NDA standard has no '## topic:' sections.");
        }

        var duplicate = topics.GroupBy(t => t.Id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"The pinned NDA standard repeats topic '{duplicate.Key}'.");
        }

        return new Parsed(
            name,
            version,
            string.Join('\n', scope).Trim(),
            topics,
            topics.Select(t => t.Id).ToFrozenSet(StringComparer.Ordinal));
    }

    internal sealed record Parsed(
        string Name,
        string Version,
        string Scope,
        IReadOnlyList<PinnedNdaTopic> Topics,
        FrozenSet<string> TopicIds);
}

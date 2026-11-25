using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ISS-053 (ebilling-r1, #3106) — duplicate detection must stay OFF on every Service Bus queue the BFF processors read
/// (<c>sdap-jobs</c> via <c>ServiceBusJobProcessor</c>, <c>sdap-communication</c> via <c>CommunicationJobProcessor</c>).
/// </summary>
/// <remarks>
/// <para><b>Why.</b> ISS-025 retries a failed job by scheduling a copy of the message with the SAME <c>MessageId</c> and then
/// completing the original (<c>JobMessageRetry</c>, <c>JobSubmissionService</c>). With duplicate detection ON the broker
/// silently drops the copy inside the detection window; the original is already completed, so the job is lost with no
/// dead-letter and no log. <c>requiresDuplicateDetection</c> is create-time only, so a later edit reaches only NEW
/// stamps, where nothing would notice. Receive-side idempotency in each handler is the guarantee (ADR-004).</para>
/// <para><b>What is scanned.</b> Every <c>*.bicep</c> under <c>infrastructure/</c>. Every non-<c>existing</c> queue resource
/// (<c>Microsoft.ServiceBus/namespaces/queues</c>) must set <c>requiresDuplicateDetection: false</c> literally. A missing
/// property fails too: the platform default can differ from <c>false</c> and the intent would be invisible. The names of
/// the BFF queues are not visible in <c>service-bus.bicep</c> (they arrive as the <c>queueNames</c> loop parameter from
/// <c>customer.bicep</c>), so a queue is treated as BFF-readable unless it is allowlisted by file and resource name.
/// Comments are stripped before scanning, so prose that mentions the property cannot satisfy or trip the rule.</para>
/// <para><b>MAINTENANCE PROCEDURE</b>: a failure names the file and resource. Fix the bicep (set
/// <c>requiresDuplicateDetection: false</c>) — do not add an exemption for a queue a BFF processor reads. A NEW queue
/// that no BFF processor reads and that needs wire-level de-duplication belongs in <see cref="Exempt"/> with a written
/// reason and ADR citation, and its name must be passed as a literal at the module call site so
/// <see cref="ExemptQueueIsOnlyEverGivenItsOwnName"/> can prove it is not a BFF queue. A NEW BFF queue constant
/// needs no change here: it is created by <c>service-bus.bicep</c> and checked already; just make sure
/// <c>customer.bicep</c> lists it. Live stamps are not covered by this scan (the value cannot be changed on an existing
/// queue and nothing reads it back); the stamp-side read-only verify belongs to customer-provisioning-orchestration-r1.</para>
/// <para>Enforced by: this class. Prose rule: <c>.claude/constraints/jobs.md</c>; assumption documented at
/// <c>JobSubmissionService.cs</c> / <c>JobMessageRetry.cs</c>. Per <c>tests/CLAUDE.md</c> "Structural fitness functions"
/// this file is MAINTAIN-class.</para>
/// </remarks>
public class JobQueueDuplicateDetectionOffGuardTests
{
    private const string BffQueueModule = "infrastructure/bicep/modules/service-bus.bicep";
    private const string StampTemplate = "infrastructure/bicep/customer.bicep";
    private const string FleetTemplate = "infrastructure/bicep/platform-controlplane.bicep";
    private const string ProvisioningQueueModule = "infrastructure/bicep/modules/controlplane-sb-queue.bicep";
    private const string ProvisioningQueueName = "sprk-provisioning-jobs";

    /// <summary>
    /// Queue resources allowed to keep duplicate detection ON, keyed by "relative path|resource symbol", with the written reason.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Exempt = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [ProvisioningQueueModule + "|provisioningJobsQueue"] =
            "sprk-provisioning-jobs is the control-plane fleet queue (FR-22 Level-1 wire-level idempotency, session-serialized per customer). " +
            "It is read only by the control-plane ServiceBusSessionProcessor, never by ServiceBusJobProcessor or CommunicationJobProcessor, " +
            "so the ISS-025 same-MessageId retry copy never targets it. ADR-004 (idempotent handlers; the retry-copy assumption applies " +
            "only to the BFF job queues).",
    };

    private static readonly Regex QueueResource = new(
        @"resource\s+(?<sym>\w+)\s+'(?:Microsoft\.ServiceBus/namespaces/)?queues@[^']*'(?<existing>\s+existing)?\s*=",
        RegexOptions.Compiled);

    private static readonly Regex DuplicateDetection = new(
        @"(?<![\w.])requiresDuplicateDetection\s*:\s*(?<value>[^\s,}]+)",
        RegexOptions.Compiled);

    // ── Real-file rules ────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "ISS-053: every BFF-readable Service Bus queue in the bicep tree sets requiresDuplicateDetection: false")]
    public void NoBffReadableQueue_HasDuplicateDetectionOn()
    {
        var violations = new List<string>();
        foreach (var file in BicepFiles())
        {
            var rel = Relative(file);
            foreach (var q in Violations(File.ReadAllText(file)))
            {
                if (Exempt.ContainsKey($"{rel}|{q.Symbol}")) continue;
                violations.Add($"{rel}: queue resource '{q.Symbol}' {q.Problem}");
            }
        }

        Assert.True(violations.Count == 0,
            "ISS-053: duplicate detection must stay OFF on queues a BFF processor reads — ISS-025's retry copy reuses the MessageId and the " +
            "broker would silently drop it, losing the job. Set `requiresDuplicateDetection: false` (see the maintenance procedure in " +
            nameof(JobQueueDuplicateDetectionOffGuardTests) + "): " + string.Join("; ", violations));
    }

    [Fact(DisplayName = "ISS-053: the scan still sees the BFF queues (service-bus.bicep declares a queue; customer.bicep lists sdap-jobs and sdap-communication and feeds it)")]
    public void ScanStillSeesTheBffQueues()
    {
        var module = ReadRepo(BffQueueModule);
        Assert.True(QueueDeclarations(module).Any(q => !q.Existing),
            $"{BffQueueModule} declares no creatable queue resource — the scan is no longer seeing the BFF queues.");

        var stamp = StripComments(ReadRepo(StampTemplate));
        Assert.Contains("'sdap-jobs'", stamp);
        Assert.Contains("'sdap-communication'", stamp);
        Assert.Matches(@"module\s+\w+\s+'modules/service-bus\.bicep'", stamp);
        Assert.Matches(@"queueNames\s*:\s*serviceBusQueues", stamp);
    }

    [Fact(DisplayName = "ISS-053: the exemption is real — the provisioning queue is declared with detection ON and only that name is passed to it")]
    public void ExemptQueueIsOnlyEverGivenItsOwnName()
    {
        foreach (var key in Exempt.Keys)
        {
            var parts = key.Split('|');
            var hits = Violations(ReadRepo(parts[0])).Where(q => q.Symbol == parts[1]).ToList();
            Assert.True(hits.Count == 1, $"Exempt entry '{key}' no longer matches a queue that violates the rule — remove the stale exemption.");
        }

        // The exempt module is invoked with a literal name that is not a BFF queue, and by nothing else.
        var callers = BicepFiles()
            .Where(f => Regex.IsMatch(StripComments(File.ReadAllText(f)), @"module\s+\w+\s+'[^']*controlplane-sb-queue\.bicep'"))
            .Select(Relative).ToList();
        Assert.Equal(new[] { FleetTemplate }, callers);

        var fleet = StripComments(ReadRepo(FleetTemplate));
        var names = Regex.Matches(fleet, @"module\s+\w+\s+'modules/controlplane-sb-queue\.bicep'\s*=\s*\{.*?queueName\s*:\s*(?<n>[^\r\n]+)", RegexOptions.Singleline)
            .Select(m => m.Groups["n"].Value.Trim()).ToList();
        Assert.Equal(new[] { $"'{ProvisioningQueueName}'" }, names);
    }

    // ── Controls ───────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "ISS-053 control (must fire): requiresDuplicateDetection: true on a looped queue resource is a violation")]
    public void Control_True_Fires()
    {
        const string seeded = """
            resource queues 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = [for queueName in queueNames: {
              parent: serviceBusNamespace
              name: queueName
              properties: {
                requiresDuplicateDetection: true
                requiresSession: false
              }
            }]
            """;
        var hit = Assert.Single(Violations(seeded));
        Assert.Equal("queues", hit.Symbol);
    }

    [Fact(DisplayName = "ISS-053 control (must fire): an omitted property, a non-literal value and a nested short-form queue are violations")]
    public void Control_Omitted_NonLiteral_AndNested_Fire()
    {
        const string omitted = "resource q 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {\n  name: 'x'\n  properties: { lockDuration: 'PT5M' }\n}";
        const string nonLiteral = "resource q 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {\n  name: 'x'\n  properties: { requiresDuplicateDetection: dedupe }\n}";
        const string nested = "resource ns 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {\n  name: 'n'\n  resource q 'queues@2022-10-01-preview' = {\n    name: 'x'\n    properties: { requiresDuplicateDetection: true }\n  }\n}";

        Assert.Single(Violations(omitted));
        Assert.Single(Violations(nonLiteral));
        Assert.Single(Violations(nested));
    }

    [Fact(DisplayName = "ISS-053 control (must fire): a comment that says false does not satisfy the rule")]
    public void Control_CommentCannotSatisfy()
    {
        const string seeded = "resource q 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {\n  name: 'x'\n  // requiresDuplicateDetection: false\n  properties: { requiresDuplicateDetection: true }\n}";
        Assert.Single(Violations(seeded));
    }

    [Fact(DisplayName = "ISS-053 control (must not fire): false passes, an `existing` reference passes, and a non-queue resource mentioning the property passes")]
    public void Control_False_Existing_AndNonQueue_DoNotFire()
    {
        const string compliant = "resource queues 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = [for n in names: {\n  name: n\n  properties: { requiresDuplicateDetection: false }\n}]";
        const string existing = "resource q 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' existing = {\n  name: 'x'\n}";
        const string nonQueue = "resource t 'Microsoft.ServiceBus/namespaces/topics@2022-10-01-preview' = {\n  name: 'x'\n  properties: { requiresDuplicateDetection: true }\n}";

        Assert.Empty(Violations(compliant));
        Assert.Empty(Violations(existing));
        Assert.Empty(Violations(nonQueue));
    }

    [Fact(DisplayName = "ISS-053 control (must not fire): the real service-bus.bicep passes unexempted; the provisioning queue fires without its exemption")]
    public void Control_RealFiles()
    {
        Assert.Empty(Violations(ReadRepo(BffQueueModule)));

        // Proves the detector SEES the provisioning queue, so its exemption is load-bearing rather than a gap in the scan.
        var provisioning = Violations(ReadRepo(ProvisioningQueueModule));
        Assert.Equal("provisioningJobsQueue", Assert.Single(provisioning).Symbol);
        Assert.Contains(ProvisioningQueueModule + "|provisioningJobsQueue", Exempt.Keys);
        Assert.All(Exempt.Values, reason => Assert.Contains("ADR-004", reason));
    }

    // ── Detector ───────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record QueueDeclaration(string Symbol, bool Existing, string Body);

    private sealed record QueueViolation(string Symbol, string Problem);

    private static List<QueueViolation> Violations(string bicep)
    {
        var result = new List<QueueViolation>();
        foreach (var q in QueueDeclarations(bicep))
        {
            if (q.Existing) continue; // a reference to a queue created elsewhere; the creating resource is scanned there
            var m = DuplicateDetection.Match(q.Body);
            if (!m.Success)
                result.Add(new(q.Symbol, "does not set requiresDuplicateDetection (must be the literal false)"));
            else if (m.Groups["value"].Value != "false")
                result.Add(new(q.Symbol, $"sets requiresDuplicateDetection: {m.Groups["value"].Value} (must be the literal false)"));
        }
        return result;
    }

    private static List<QueueDeclaration> QueueDeclarations(string bicep)
    {
        var text = StripComments(bicep);
        var list = new List<QueueDeclaration>();
        foreach (Match m in QueueResource.Matches(text))
        {
            var open = text.IndexOf('{', m.Index + m.Length);
            if (open < 0) continue;
            var depth = 0;
            var end = open;
            for (; end < text.Length; end++)
            {
                if (text[end] == '{') depth++;
                else if (text[end] == '}' && --depth == 0) break;
            }
            list.Add(new(m.Groups["sym"].Value, m.Groups["existing"].Success, text[open..Math.Min(end + 1, text.Length)]));
        }
        return list;
    }

    /// <summary>Removes <c>/* */</c> and <c>//</c> comments; a <c>//</c> after a colon (a URL) is kept.</summary>
    private static string StripComments(string bicep) =>
        Regex.Replace(Regex.Replace(bicep, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline), @"(?<!:)//[^\r\n]*", string.Empty);

    private static IEnumerable<string> BicepFiles() =>
        Directory.EnumerateFiles(Path.Combine(SourceScan.RepoRoot, "infrastructure"), "*.bicep", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string Relative(string file) =>
        Path.GetRelativePath(SourceScan.RepoRoot, file).Replace('\\', '/');

    private static string ReadRepo(string relative) => File.ReadAllText(Path.Combine(SourceScan.RepoRoot, relative));
}

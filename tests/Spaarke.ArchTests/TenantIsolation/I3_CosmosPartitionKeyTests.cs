using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests.TenantIsolation;

/// <summary>
/// Tenant-isolation invariant <b>I3</b> (spec.md FR-30 / design.md §4D I3):
/// every Cosmos DB SDK call site under <c>src/server/**</c> must pass an
/// explicit <c>PartitionKey</c> argument (not <c>PartitionKey.None</c>);
/// cross-partition queries against tenant-scoped containers are forbidden.
///
/// <para>
/// <b>Severity</b>: HIGH. A cross-partition Cosmos query on a tenant-scoped
/// container (AI sessions, prompts, pinned context, audit rows, feedback)
/// returns records from other customers — conversational PII / prompt-shape
/// leak (§4D I3 rationale).
/// </para>
///
/// <para>
/// <b>Scan shape</b>: file-level regex over <c>src/server/**/*.cs</c> for
/// every invocation of the Cosmos SDK <c>Container</c> item / query methods:
/// <c>ReadItemAsync</c>, <c>CreateItemAsync</c>, <c>UpsertItemAsync</c>,
/// <c>ReplaceItemAsync</c>, <c>DeleteItemAsync</c>, <c>PatchItemAsync</c>,
/// <c>ReadManyItemsAsync</c>, <c>GetItemQueryIterator</c>. For each call
/// site the ArchTest extracts the balanced argument list and verifies EITHER
/// (a) a <c>PartitionKey</c> argument is present and is not
/// <c>PartitionKey.None</c>, OR (b) the enclosing method or type carries an
/// <c>[AllowCrossPartitionScan("...")]</c> attribute waiver.
/// </para>
///
/// <para>
/// <b>Waiver</b>: <see cref="Spaarke.Core.Attributes.AllowCrossPartitionScanAttribute"/>
/// — added by <c>customer-provisioning-orchestration-r1</c> task 064; the
/// attribute is matched BY NAME (regex over the source syntax) so consumers
/// can either reference the canonical Spaarke.Core definition or declare a
/// same-named local attribute. Reviewer contract on every use: the attribute
/// arg MUST cite a design section, spec FR, task ID, or ADR that justifies
/// the waiver.
/// </para>
///
/// <para>
/// <b>Legitimate waiver examples</b> (per design.md §4.2 crash recovery /
/// task 060): the L2 reconciler startup scan MUST enumerate every partition
/// of the <c>runs</c> container to find orphaned runs older than 2× median
/// handler duration; that scan carries an
/// <c>[AllowCrossPartitionScan("§4.2 crash recovery / task 060")]</c>
/// annotation and is legitimately cross-partition.
/// </para>
/// </summary>
public class I3_CosmosPartitionKeyTests
{
    private static readonly string RepoRoot = ResolveRepoRoot();
    private const string ScanRelDir = "src/server";

    /// <summary>
    /// Cosmos SDK <c>Container</c> item / query methods that MUST carry an
    /// explicit <c>PartitionKey</c> (either as a positional or named argument,
    /// or inside a <c>QueryRequestOptions</c> / <c>ItemRequestOptions</c>).
    /// Order matters here for the failure-message readability — the first
    /// match on a line wins.
    /// </summary>
    private static readonly string[] CosmosMethodNames =
    {
        "ReadItemAsync",
        "CreateItemAsync",
        "UpsertItemAsync",
        "ReplaceItemAsync",
        "DeleteItemAsync",
        "PatchItemAsync",
        "ReadManyItemsAsync",
        "GetItemQueryIterator",
        "GetItemLinqQueryable",
    };

    /// <summary>
    /// Matches an invocation of any of <see cref="CosmosMethodNames"/> against
    /// a receiver, capturing the method name in group 1. The receiver may be
    /// any expression; the actual argument list is extracted by paren-matching
    /// in a follow-up step. Receiver-shape filtering (Cosmos Container vs
    /// facade store) is applied by <see cref="IsCosmosContainerReceiver"/>.
    /// </summary>
    private static readonly Regex CosmosMethodInvocation = new(
        @"\.(" + string.Join("|", CosmosMethodNames) + @")\s*(?:<[^>]+>)?\s*\(",
        RegexOptions.Compiled);

    /// <summary>
    /// A receiver token (word immediately before the <c>.</c>) that matches
    /// one of these shapes is considered a Cosmos <c>Container</c>:
    /// <list type="bullet">
    ///   <item>identifier containing <c>container</c> (case-insensitive) —
    ///     e.g., <c>container</c>, <c>_container</c>, <c>eventsContainer</c></item>
    ///   <item>the literal call <c>GetContainer()</c> and its argumented
    ///     variants</item>
    /// </list>
    /// Facade-style receivers like <c>store</c>, <c>_repository</c>, or
    /// <c>facade</c> do NOT match and are excluded from the scan (they call
    /// their own <c>DeleteItemAsync</c> / <c>ReadItemAsync</c> methods on the
    /// underlying IMemoryItemStore / IPromptRepository interfaces, not on the
    /// Cosmos SDK).
    /// </summary>
    private static readonly Regex ContainerReceiver = new(
        @"\b\w*[Cc]ontainer\w*$|GetContainer\s*\(\s*\)$",
        RegexOptions.Compiled);

    /// <summary>
    /// Presence of a PartitionKey reference in the argument text of a SINGLE
    /// call site. Accepts:
    /// <list type="bullet">
    ///   <item><c>new PartitionKey(...)</c> literal construction</item>
    ///   <item><c>partitionKey: ...</c> named argument</item>
    ///   <item>a bare identifier matching <c>*partitionKey</c> or
    ///     <c>*PartitionKey</c> passed positionally (e.g., a hoisted local of
    ///     type PartitionKey)</item>
    /// </list>
    /// <c>PartitionKey.None</c> is matched separately and explicitly forbidden.
    /// </summary>
    private static readonly Regex PartitionKeyPresent = new(
        @"\bnew\s+PartitionKey\s*\(|\bpartitionKey\s*:|\b\w*[Pp]artitionKey\b",
        RegexOptions.Compiled);

    /// <summary>
    /// Presence of a PartitionKey-scoping marker in the enclosing METHOD SCOPE
    /// (i.e., a few lines above the call site) — catches the common compliant
    /// shape where a <c>QueryRequestOptions { PartitionKey = new PartitionKey(...) }</c>
    /// or a hoisted <c>var partitionKey = new PartitionKey(...)</c> is
    /// declared above the call and then passed positionally. Matched:
    /// <list type="bullet">
    ///   <item><c>new PartitionKey(</c> — construction anywhere in the window</item>
    ///   <item><c>PartitionKey =</c> — property assignment (e.g., on
    ///     QueryRequestOptions / ItemRequestOptions initializer)</item>
    /// </list>
    /// </summary>
    private static readonly Regex PartitionKeyInScope = new(
        @"\bnew\s+PartitionKey\s*\(|\bPartitionKey\s*=",
        RegexOptions.Compiled);

    private static readonly Regex PartitionKeyNone = new(
        @"\bPartitionKey\.None\b",
        RegexOptions.Compiled);

    /// <summary>
    /// Waiver-attribute presence in the enclosing method or class. Matched
    /// by NAME (per design of the attribute in <c>Spaarke.Core.Attributes</c>).
    /// </summary>
    private static readonly Regex AllowCrossPartitionAttr = new(
        @"\[\s*AllowCrossPartitionScan\s*\(",
        RegexOptions.Compiled);

    /// <summary>
    /// Files under <see cref="ScanRelDir"/> excluded from the I3 predicate.
    /// Two exclusion categories only:
    /// <list type="bullet">
    ///   <item>Null-object placeholders under <c>Infrastructure/Cache/NullObjects/</c>
    ///     — pattern implementations of Cosmos-adjacent interfaces (e.g., Redis
    ///     null object) that surface method signatures with names like
    ///     <c>GeoSearchAsync</c> / <c>ReadAsync</c>; NOT actual Cosmos calls.</item>
    ///   <item>Files whose Cosmos-method NAME matches by chance because they
    ///     implement a same-named facade method (e.g. an <c>IMemoryItemStore</c>
    ///     that declares a <c>DeleteItemAsync</c> method). These are excluded
    ///     by the receiver-anchor requirement in the invocation regex (the
    ///     invocation MUST start with a <c>.</c>) — no explicit list needed.</item>
    /// </list>
    /// The overall philosophy: the file exclusion list is intentionally tiny;
    /// exceptions belong on the CALL SITE as
    /// <c>[AllowCrossPartitionScan(...)]</c>, not as blanket file skips.
    /// </summary>
    private static readonly HashSet<string> ExcludedFileRelPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        // Currently empty. Any future file-level waiver requires a documented
        // reason inline here.
    };

    [Fact(DisplayName = "FR-30/§4D I3: every Cosmos SDK call site has an explicit PartitionKey (or an [AllowCrossPartitionScan] waiver)")]
    public void CosmosCallSites_HaveExplicitPartitionKey()
    {
        var serverRoot = Path.Combine(RepoRoot, ScanRelDir);
        Assert.True(
            Directory.Exists(serverRoot),
            $"{ScanRelDir} directory not found at '{serverRoot}'. The I3 ArchTest cannot run without it.");

        var offenders = new List<string>();
        foreach (var file in EnumerateProductionCsFiles(serverRoot))
        {
            var rel = RelPath(file);
            if (ExcludedFileRelPaths.Contains(rel)) continue;

            var text = File.ReadAllText(file);
            var matches = CosmosMethodInvocation.Matches(text);
            if (matches.Count == 0) continue;

            foreach (Match m in matches)
            {
                var methodName = m.Groups[1].Value;

                // Receiver-shape filter: skip facade-style receivers (store, repo,
                // service) whose method name matches by coincidence — they are not
                // Cosmos SDK calls. Real Container calls have a receiver token
                // containing 'container' or being `GetContainer()`.
                if (!IsCosmosContainerReceiver(text, m.Index)) continue;

                var openParenIndex = m.Index + m.Length - 1;   // the '(' matched by the regex
                var args = ExtractBalancedArgList(text, openParenIndex);
                if (args is null) continue;   // unbalanced (unlikely); skip conservatively

                // If the enclosing method / type is waived, skip this call site.
                if (IsInsideWaivedScope(text, m.Index)) continue;

                var hasKey = PartitionKeyPresent.IsMatch(args);
                var hasNone = PartitionKeyNone.IsMatch(args);

                // If not present in the immediate args, check the enclosing method
                // scope (hoisted local / QueryRequestOptions initializer above the call).
                if (!hasKey)
                {
                    hasKey = HasPartitionKeyInMethodScope(text, m.Index);
                }

                if (!hasKey || hasNone)
                {
                    var lineNumber = LineNumberFor(text, m.Index);
                    var reasonFragment = hasNone
                        ? "argument list uses PartitionKey.None (cross-partition — explicitly forbidden by I3)"
                        : "no PartitionKey / partitionKey: argument present in the invocation";
                    offenders.Add(
                        $"{rel}:{lineNumber} — Container.{methodName}(...): {reasonFragment}. " +
                        $"Fix (in order of preference): (a) pass an explicit `partitionKey: new PartitionKey(tenantOrCustomerId)`; " +
                        $"(b) for a query, set `QueryRequestOptions.PartitionKey`; " +
                        $"(c) if the call is a legitimate fleet-wide scan (rare — reconciler / migration), " +
                        $"annotate the enclosing method with [AllowCrossPartitionScan(\"reason\")] " +
                        $"(Spaarke.Core.Attributes.AllowCrossPartitionScanAttribute). " +
                        $"Reference: spec.md FR-30 / design.md §4D I3.");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "§4D I3 violation: Cosmos SDK call site(s) without an explicit PartitionKey and without an " +
            "[AllowCrossPartitionScan] waiver. Cross-partition scans on tenant-scoped containers leak " +
            "records from other customers (HIGH severity per §4D). Add per-tenant partition scoping " +
            "or annotate with a documented waiver.\n" +
            $"Offenders:\n{string.Join("\n", offenders.OrderBy(x => x, StringComparer.Ordinal))}");
    }

    // -----------------------------------------------------------------------
    // Negative controls
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "FR-30 negative control: predicate flags a bare GetItemQueryIterator without PartitionKey")]
    public void CosmosPredicate_FlagsMissingPartitionKey()
    {
        var badSnippet =
            "public async Task Query() {\n" +
            "    using var it = container.GetItemQueryIterator<int>(query);\n" +
            "}\n";
        var m = CosmosMethodInvocation.Match(badSnippet);
        Assert.True(m.Success, "Method invocation should match.");
        var args = ExtractBalancedArgList(badSnippet, m.Index + m.Length - 1);
        Assert.NotNull(args);
        Assert.False(PartitionKeyPresent.IsMatch(args!),
            "Predicate should recognize missing PartitionKey in the argument list.");
    }

    [Fact(DisplayName = "FR-30 negative control: predicate passes when PartitionKey is present in various shapes")]
    public void CosmosPredicate_PassesCompliantShapes()
    {
        Assert.Matches(PartitionKeyPresent, "item: run, partitionKey: new PartitionKey(run.CustomerId)");
        Assert.Matches(PartitionKeyPresent, "id, new PartitionKey(tenantId), cancellationToken: ct");
        Assert.Matches(PartitionKeyPresent, "id, partitionKey, ct");
        Assert.Matches(PartitionKeyPresent, "document, myPartitionKey, requestOptions, ct");
    }

    [Fact(DisplayName = "FR-30 negative control: receiver-shape filter distinguishes Cosmos Container from facade store/repository")]
    public void ReceiverShapeFilter_DistinguishesContainerFromFacade()
    {
        // Cosmos Container receivers — must be recognized.
        var containerSnippet = "await container.ReadItemAsync<Doc>(id, pk, ct);";
        var m1 = CosmosMethodInvocation.Match(containerSnippet);
        Assert.True(m1.Success);
        Assert.True(IsCosmosContainerReceiver(containerSnippet, m1.Index),
            "'container' receiver should be recognized as a Cosmos Container.");

        var underscoreContainerSnippet = "await _container.DeleteItemAsync<Doc>(id, pk, ct);";
        var m2 = CosmosMethodInvocation.Match(underscoreContainerSnippet);
        Assert.True(m2.Success);
        Assert.True(IsCosmosContainerReceiver(underscoreContainerSnippet, m2.Index),
            "'_container' receiver should be recognized as a Cosmos Container.");

        var getContainerSnippet = "using var it = GetContainer().GetItemQueryIterator<Doc>(query, requestOptions: opts);";
        var m3 = CosmosMethodInvocation.Match(getContainerSnippet);
        Assert.True(m3.Success);
        Assert.True(IsCosmosContainerReceiver(getContainerSnippet, m3.Index),
            "'GetContainer()' call as receiver should be recognized as a Cosmos Container.");

        // Facade receivers — must NOT be recognized as Cosmos calls (avoid false positives on
        // IMemoryItemStore.DeleteItemAsync, IPromptRepository.ReadItemAsync, etc.).
        var facadeSnippet = "await store.DeleteItemAsync(subjectKey, itemId, ct);";
        var m4 = CosmosMethodInvocation.Match(facadeSnippet);
        Assert.True(m4.Success, "The invocation regex still matches by method name (we want that — the filter is receiver-based).");
        Assert.False(IsCosmosContainerReceiver(facadeSnippet, m4.Index),
            "'store' receiver should NOT be recognized as a Cosmos Container (it is a facade IMemoryItemStore).");
    }

    [Fact(DisplayName = "FR-30 negative control: method-scope predicate recognizes hoisted PartitionKey / QueryRequestOptions initializer")]
    public void MethodScopePredicate_RecognizesHoistedPartitionKey()
    {
        // Compliant pattern: hoisted local, then positional pass.
        var hoistedSnippet =
            "public async Task DoIt() {\n" +
            "    var partitionKey = new PartitionKey(subjectKey);\n" +
            "    await _container.ReadItemAsync<Doc>(id, partitionKey, ct);\n" +
            "}\n";
        var callMatch = CosmosMethodInvocation.Match(hoistedSnippet);
        Assert.True(callMatch.Success);
        Assert.True(HasPartitionKeyInMethodScope(hoistedSnippet, callMatch.Index),
            "Method-scope predicate must recognize a hoisted `new PartitionKey(...)` above the call.");

        // Compliant pattern: QueryRequestOptions initializer with PartitionKey = ...
        var initSnippet =
            "public async Task DoIt() {\n" +
            "    var requestOptions = new QueryRequestOptions { PartitionKey = new PartitionKey(tenantId) };\n" +
            "    using var it = container.GetItemQueryIterator<int>(query, requestOptions: requestOptions);\n" +
            "}\n";
        var initMatch = CosmosMethodInvocation.Match(initSnippet);
        Assert.True(initMatch.Success);
        Assert.True(HasPartitionKeyInMethodScope(initSnippet, initMatch.Index),
            "Method-scope predicate must recognize a QueryRequestOptions { PartitionKey = new PartitionKey(...) } initializer above the call.");
    }

    [Fact(DisplayName = "FR-30 negative control: predicate rejects PartitionKey.None explicitly")]
    public void CosmosPredicate_RejectsPartitionKeyNone()
    {
        Assert.True(PartitionKeyNone.IsMatch("id, PartitionKey.None, ct"),
            "PartitionKey.None must be explicitly rejected by the None-detector.");
    }

    [Fact(DisplayName = "FR-30 negative control: waiver attribute name is matched (canonical + local shape)")]
    public void WaiverAttribute_Recognized()
    {
        Assert.Matches(AllowCrossPartitionAttr, "[AllowCrossPartitionScan(\"§4.2 reconciler\")]");
        // Whitespace tolerance around the attribute is required for source-code diversity.
        Assert.Matches(AllowCrossPartitionAttr, "[ AllowCrossPartitionScan ( \"anything\" ) ]");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Extracts the text inside a balanced <c>(...)</c> starting at the
    /// opening paren at <paramref name="openParenIndex"/>. Returns
    /// <c>null</c> on unbalanced input.
    /// </summary>
    private static string? ExtractBalancedArgList(string source, int openParenIndex)
    {
        if (openParenIndex < 0 || openParenIndex >= source.Length ||
            source[openParenIndex] != '(') return null;

        int depth = 1;
        for (int i = openParenIndex + 1; i < source.Length; i++)
        {
            var c = source[i];
            switch (c)
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    if (depth == 0) return source[(openParenIndex + 1)..i];
                    break;
            }
        }
        return null;
    }

    /// <summary>
    /// Detects whether the call at <paramref name="callIndex"/> is inside a
    /// method or type whose declaration is annotated with
    /// <c>[AllowCrossPartitionScan(...)]</c>. Approximate: scans the 2,000
    /// characters immediately preceding the call site (covers all realistic
    /// method / class definitions in the codebase — largest methods stay
    /// well under 500 lines × 80 chars).
    /// </summary>
    private static bool IsInsideWaivedScope(string source, int callIndex)
    {
        int scanStart = Math.Max(0, callIndex - 2_000);
        var window = source.Substring(scanStart, callIndex - scanStart);
        return AllowCrossPartitionAttr.IsMatch(window);
    }

    /// <summary>
    /// Detects a PartitionKey-scoping marker in the method scope preceding
    /// the call site. Covers the common compliant patterns where a
    /// <c>QueryRequestOptions { PartitionKey = new PartitionKey(...) }</c>
    /// or a hoisted <c>var partitionKey = new PartitionKey(...)</c> is
    /// declared above the call and threaded in as a positional variable.
    /// Scan window: 1,200 characters (covers ~40-line methods).
    /// </summary>
    private static bool HasPartitionKeyInMethodScope(string source, int callIndex)
    {
        int scanStart = Math.Max(0, callIndex - 1_200);
        var window = source.Substring(scanStart, callIndex - scanStart);
        return PartitionKeyInScope.IsMatch(window);
    }

    /// <summary>
    /// Extracts the receiver token immediately before the <c>.</c> at
    /// <paramref name="dotIndex"/> and checks it matches the Cosmos
    /// <c>Container</c> receiver shape (<see cref="ContainerReceiver"/>).
    /// </summary>
    private static bool IsCosmosContainerReceiver(string source, int dotIndex)
    {
        // The regex match includes the leading `.` at `dotIndex`. Walk backwards
        // to collect the receiver expression (up to 40 chars, or until we hit a
        // clear delimiter like whitespace preceded by non-word content).
        int start = dotIndex;
        int minStart = Math.Max(0, dotIndex - 40);
        int i = dotIndex - 1;
        // Skip trailing whitespace between the receiver and the dot.
        while (i >= minStart && char.IsWhiteSpace(source[i])) i--;
        // Collect either a `)`-terminated call expression like `GetContainer()`
        // or a bare identifier.
        if (i >= minStart && source[i] == ')')
        {
            // Walk back to matching '('.
            int depth = 1;
            i--;
            while (i >= minStart && depth > 0)
            {
                if (source[i] == ')') depth++;
                else if (source[i] == '(') depth--;
                i--;
            }
            // Now walk back over the method name identifier.
            while (i >= minStart && (char.IsLetterOrDigit(source[i]) || source[i] == '_')) i--;
            start = i + 1;
        }
        else
        {
            while (i >= minStart && (char.IsLetterOrDigit(source[i]) || source[i] == '_')) i--;
            start = i + 1;
        }

        if (start >= dotIndex) return false;
        var receiver = source.Substring(start, dotIndex - start).TrimEnd();
        return ContainerReceiver.IsMatch(receiver);
    }

    private static IEnumerable<string> EnumerateProductionCsFiles(string root)
        => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static int LineNumberFor(string source, int charIndex)
    {
        if (charIndex < 0 || charIndex > source.Length) return 1;
        int line = 1;
        for (int i = 0; i < charIndex; i++)
        {
            if (source[i] == '\n') line++;
        }
        return line;
    }

    private static string RelPath(string fullPath)
        => fullPath.Replace(RepoRoot + Path.DirectorySeparatorChar, string.Empty)
                   .Replace(Path.DirectorySeparatorChar, '/');

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
        return AppContext.BaseDirectory;
    }
}

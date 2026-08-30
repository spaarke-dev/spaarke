using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// unified-access-control-r2 task 092 — every <c>/spe/...</c> URL the SPE Admin client calls must
/// resolve to a route the BFF actually serves.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> Task 091's caller inventory found TWO endpoints the client called that the
/// server has never served. One was dead client code. The other,
/// <c>POST …/items/{itemId}/sharing</c> against a server serving <c>/share</c>, sat behind the SPE
/// Admin "create sharing link" button — so the feature 404'd for its entire life, and because
/// <c>FileDetailPanel</c> catches the failure and renders <i>"Failed to create sharing link."</i>, the
/// UI gave nobody a reason to suspect a routing bug. Neither compiler sees across this boundary: the
/// client's URL is a string, the server's route is a string, and nothing compared them.
/// </para>
/// <para>
/// <b>Why an ArchTest and not a client test.</b> <c>SpeAdminApp</c> has no test framework at all — no
/// vitest/jest, no test files, no <c>test</c> script. Adding one to assert two URL strings would be a
/// new component to justify (CLAUDE.md §11) for a defect that is not client logic at all. This is an
/// AGREEMENT bug between two files, and a source-scanning cross-check is the instrument that matches
/// it — the same shape as this project's other structural guards, and it catches the class rather than
/// the two known instances.
/// </para>
/// <para>
/// <b>Fail-closed.</b> A client call site whose URL this cannot parse is REPORTED, never skipped —
/// see <see cref="EveryClientCallSiteIsParseable"/>. An unparseable call site is exactly where the
/// next mismatch would hide.
/// </para>
/// <para>ADR-038 structural fitness function (the eighth KEEP path per tests/CLAUDE.md), so it carries
/// a negative control proving it fires and a positive control proving it does not fire on the
/// sanctioned shape.</para>
/// </remarks>
public class SpeAdminClientRouteAgreementTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static string ClientFile => Path.Combine(
        RepoRoot, "src", "solutions", "SpeAdminApp", "src", "services", "speApiClient.ts");

    private static string ServerDir => Path.Combine(
        RepoRoot, "src", "server", "api", "Sprk.Bff.Api", "Api", "SpeAdmin");

    /// <summary>The prefix the <c>/api/spe</c> group contributes; client URLs omit the <c>/api</c>.</summary>
    private const string ClientPrefix = "/spe";

    // =============================================================================================
    // THE RULE
    // =============================================================================================

    [Fact(DisplayName = "Task 092: every /spe URL the SPE Admin client calls matches a real server route")]
    public void EveryClientUrlMatchesAServerRoute()
    {
        var serverRoutes = ServerRouteTemplates();
        var clientUrls = ClientUrlTemplates(File.ReadAllText(ClientFile));

        Assert.True(
            serverRoutes.Count > 0,
            "No server routes were parsed from Api/SpeAdmin/**. The scan found nothing, so this rule "
            + "would pass vacuously — fix the scan rather than the assertion.");

        Assert.True(
            clientUrls.Count > 0,
            "No client URLs were parsed from speApiClient.ts. Same vacuity problem as above.");

        var orphans = clientUrls
            .Where(u => !serverRoutes.Contains(u.Template))
            .Select(u => $"{u.Template}   (speApiClient.ts line {u.Line})")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            orphans.Count == 0,
            "The SPE Admin client calls URLs the BFF does not serve. Each of these 404s at runtime, and "
            + "the client surfaces it as a generic error — which is why the two found by task 091 "
            + "survived unnoticed (one of them behind a shipped button).\n\n"
            + "Fix the CLIENT unless the server route is genuinely wrong: a server route is a published "
            + "contract (WithName/OpenAPI) and renaming it breaks every other consumer. If the client "
            + "method has no server route AND no callers, DELETE it — do not invent a route to justify "
            + "dead code (task 083's disposition; precedent in tasks 071 and 073).\n\n"
            + $"Unmatched client URLs:\n  {string.Join("\n  ", orphans)}\n\n"
            + $"Server routes available ({serverRoutes.Count}):\n  "
            + string.Join("\n  ", serverRoutes.OrderBy(r => r, StringComparer.Ordinal)));
    }

    [Fact(DisplayName = "Task 092: every SPE Admin client call site yields a parseable URL (fail-closed)")]
    public void EveryClientCallSiteIsParseable()
    {
        var source = File.ReadAllText(ClientFile);

        // Every occurrence of a "/spe/..." string literal is a call site this guard must account for.
        // If the count of literals exceeds the count of templates we extracted, something in the file
        // uses a shape the parser does not understand — and a URL the parser cannot see is a URL it
        // cannot check.
        var literalCount = Regex.Matches(source, @"""" + Regex.Escape(ClientPrefix) + @"[/""]").Count;
        var parsed = ClientUrlTemplates(source);

        Assert.True(
            parsed.Count >= literalCount,
            $"Found {literalCount} \"{ClientPrefix}...\" string literals but parsed only {parsed.Count} "
            + "URL templates. Some call site uses a shape this parser does not handle, so its URL is "
            + "NOT being checked against the server. Extend the parser — do not lower this assertion; "
            + "an unparseable call site is precisely where the next mismatch hides.");

        // A URL whose VERB could not be determined is equally unchecked. It would surface in the main
        // rule as an "UNKNOWN /spe/..." mismatch, but that reads like a missing route rather than an
        // unrecognised helper, and the fix is completely different. Report it as what it is.
        var unknownVerbs = parsed
            .Where(u => u.Template.StartsWith("UNKNOWN ", StringComparison.Ordinal))
            .Select(u => $"{u.Template}   (line {u.Line})")
            .ToList();

        Assert.True(
            unknownVerbs.Count == 0,
            "These call sites use a transport helper this guard does not recognise, so their HTTP verb "
            + "is unknown and they cannot be matched against a server route. Add the helper to the map "
            + "in VerbBefore — do NOT let it default to a guess. The verb is load-bearing here: the item "
            + "surface serves DELETE on a path that has no GET, so a verb-blind comparison reports "
            + "agreement where there is none.\n\n  " + string.Join("\n  ", unknownVerbs));
    }

    // =============================================================================================
    // CONTROLS
    // =============================================================================================

    [Fact(DisplayName = "Task 092 negative control: the detector fires on both real mismatches, reintroduced as source")]
    public void Detector_NegativeControl_FiresOnBothHistoricalMismatches()
    {
        var serverRoutes = ServerRouteTemplates();

        // Mismatch #1 — the LIVE one. Client posted to /sharing; the server serves /share.
        var sharing = ClientUrlTemplates(
            """
                  createSharingLink(containerId: string, itemId: string, configId: string) {
                    return post<typeof body, SharingLink>(
                      "/spe/containers/" + containerId + "/items/" + itemId + "/sharing" + qs({ configId }),
                      body,
                    );
                  },
            """);

        Assert.Single(sharing);
        Assert.Equal("POST /spe/containers/{}/items/{}/sharing", sharing[0].Template);
        Assert.DoesNotContain(sharing[0].Template, serverRoutes);

        // Mismatch #2 — the DEAD one. GET on a single item; no such server route has ever existed.
        var singleItem = ClientUrlTemplates(
            """
                  async get(containerId: string, itemId: string, configId: string): Promise<DriveItem> {
                    return mapDriveItem(
                      await get<WireDriveItem>(
                        "/spe/containers/" + containerId + "/items/" + itemId + qs({ configId }),
                      ),
                    );
                  },
            """);

        Assert.Single(singleItem);
        Assert.Equal("GET /spe/containers/{}/items/{}", singleItem[0].Template);
        Assert.DoesNotContain(singleItem[0].Template, serverRoutes);
    }

    [Fact(DisplayName = "Task 092 positive control: the corrected sharing-link URL DOES match a server route")]
    public void Detector_PositiveControl_DoesNotFireOnTheCorrectedShape()
    {
        var serverRoutes = ServerRouteTemplates();

        var corrected = ClientUrlTemplates(
            """
                    return post<typeof body, SharingLink>(
                      "/spe/containers/" + containerId + "/items/" + itemId + "/share" + qs({ configId }),
                      body,
                    );
            """);

        Assert.Single(corrected);
        Assert.Equal("POST /spe/containers/{}/items/{}/share", corrected[0].Template);
        Assert.Contains(
            corrected[0].Template,
            serverRoutes);
    }

    [Fact(DisplayName = "Task 092 control: a trailing query-string concatenation is not mistaken for a path segment")]
    public void Parser_TreatsQueryConcatenationAsQueryNotPath()
    {
        // "/spe/configs" + query  →  /spe/configs, NOT /spe/configs/{}. The distinction matters: the
        // former matches the real route and the latter would be a false failure. A guard that cries
        // wolf on a correct call site gets disabled, so this is load-bearing.
        var parsed = ClientUrlTemplates("""      return get<Config[]>("/spe/configs" + query);""");

        Assert.Single(parsed);
        Assert.Equal("GET /spe/configs", parsed[0].Template);
    }

    // =============================================================================================
    // PARSING
    // =============================================================================================

    private readonly record struct ClientUrl(string Template, int Line);

    /// <summary>
    /// Extracts each client URL as a template with <c>{}</c> for interpolated path segments.
    ///
    /// <para>The client builds URLs by concatenation:
    /// <c>"/spe/containers/" + containerId + "/items/" + itemId + "/share" + qs({ configId })</c>.
    /// Walking that expression, a non-literal term is a PATH SEGMENT when the accumulated text ends in
    /// <c>/</c>, and otherwise is a query-string suffix — at which point the path is complete. That one
    /// distinction is what separates <c>"/spe/environments/" + id</c> (a segment) from
    /// <c>"/spe/configs" + query</c> (a query).</para>
    /// </summary>
    private static List<ClientUrl> ClientUrlTemplates(string source)
    {
        var results = new List<ClientUrl>();

        foreach (Match start in Regex.Matches(source, @"""" + Regex.Escape(ClientPrefix) + @"(?=[/""])"))
        {
            var line = source.Take(start.Index).Count(c => c == '\n') + 1;
            var i = start.Index;
            var template = new System.Text.StringBuilder();
            var complete = false;

            while (i < source.Length && !complete)
            {
                if (source[i] == '"')
                {
                    // A string literal: append its contents verbatim.
                    var end = source.IndexOf('"', i + 1);
                    if (end < 0) break;

                    template.Append(source, i + 1, end - i - 1);
                    i = end + 1;
                }
                else if (source[i] == '+')
                {
                    i++;
                }
                else if (char.IsWhiteSpace(source[i]))
                {
                    i++;
                }
                else
                {
                    // A non-literal term. Path segment if we are sitting on a '/', else the path has
                    // ended and the remainder is a query string.
                    if (template.Length > 0 && template[^1] == '/')
                    {
                        template.Append("{}");

                        // Skip the term to the next '+' that continues the concatenation, or to the end
                        // of the expression — tracking paren depth, because a term may itself be a CALL:
                        // `encodeURIComponent(typeId)`. Without depth tracking the inner ')' looked like
                        // the end of the URL expression and silently truncated
                        // "/spe/containertypes/{}/owners" to "/spe/containertypes/{}" — a wrong template
                        // that still parses, which is worse than one that fails to parse.
                        var depth = 0;
                        while (i < source.Length)
                        {
                            var c = source[i];
                            if (c == '(') depth++;
                            else if (c == ')')
                            {
                                if (depth == 0) break;
                                depth--;
                            }
                            else if (depth == 0 && (c == '+' || c == ',')) break;

                            i++;
                        }
                    }
                    else
                    {
                        complete = true;
                    }
                }

                // A closing paren/comma at depth zero ends the URL expression.
                if (i < source.Length && (source[i] == ')' || source[i] == ','))
                {
                    complete = true;
                }
            }

            var text = template.ToString().TrimEnd('/');
            if (text.StartsWith(ClientPrefix, StringComparison.Ordinal))
            {
                results.Add(new ClientUrl($"{VerbBefore(source, start.Index)} {text}", line));
            }
        }

        return results;
    }

    /// <summary>
    /// The HTTP verb of the transport helper invoking a URL literal, found by walking backwards from
    /// the literal past the opening paren and any generic argument list to the function name.
    /// </summary>
    /// <remarks>
    /// The verb is not decoration. Without it this guard compares paths only, and the SPE Admin item
    /// surface has a path served by exactly one verb: <c>DELETE …/items/{itemId}</c> exists while
    /// <c>GET …/items/{itemId}</c> does not. A path-only comparison matched the client's dead GET
    /// against the server's DELETE and reported agreement — the negative control caught precisely
    /// this, which is what the control is for.
    /// </remarks>
    private static string VerbBefore(string source, int literalIndex)
    {
        var i = literalIndex - 1;

        while (i >= 0 && (char.IsWhiteSpace(source[i]) || source[i] == '(' || source[i] == ','))
        {
            i--;
        }

        // Step back over a generic argument list, e.g. post<typeof body, SharingLink>(…).
        if (i >= 0 && source[i] == '>')
        {
            var depth = 0;
            while (i >= 0)
            {
                if (source[i] == '>') depth++;
                else if (source[i] == '<' && --depth == 0) { i--; break; }
                i--;
            }
        }

        var end = i;
        while (i >= 0 && (char.IsLetterOrDigit(source[i]) || source[i] == '_'))
        {
            i--;
        }

        var name = end > i ? source.Substring(i + 1, end - i) : string.Empty;

        // The seven transport helpers declared in speApiClient.ts, plus the raw-Response escape hatch.
        // `postAction` is a POST with no body — it was missing from an earlier version of this map and
        // produced six UNKNOWN verbs, which is the failure mode this switch's default is for: an
        // unrecognised helper is reported loudly, never guessed at.
        if (name == "authenticatedFetch")
        {
            // The raw-fetch escape hatch carries its verb in the options object rather than the
            // function name: authenticatedFetch(url, { method: "GET" }).
            var window = source.Length - literalIndex < 400 ? source.Length - literalIndex : 400;
            var method = Regex.Match(
                source.Substring(literalIndex, window), @"method\s*:\s*""(?<verb>[A-Z]+)""");

            return method.Success ? method.Groups["verb"].Value : "UNKNOWN";
        }

        return name switch
        {
            "get" => "GET",
            "post" or "postAction" or "postFormData" => "POST",
            "put" => "PUT",
            "patch" => "PATCH",
            "del" or "delete" => "DELETE",
            _ => "UNKNOWN",
        };
    }

    /// <summary>
    /// Every route registered by any file under <c>Api/SpeAdmin/**</c>, normalised to the client's
    /// vocabulary: verb-qualified, route parameters collapsed to <c>{}</c>, group prefix applied.
    /// </summary>
    private static HashSet<string> ServerRouteTemplates()
    {
        var routes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(ServerDir, "*.cs", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(file);

            // Sub-groups first. Several files nest one level below /api/spe —
            //   var security = group.MapGroup("/security");
            //   security.MapGet("/alerts", …);            →  /api/spe/security/alerts
            // Reading only the leaf route yields "/alerts" and reports a false mismatch against a
            // client that correctly calls "/spe/security/alerts". Four such files exist (security,
            // configs, dashboard, …), and a guard that flags correct call sites is a guard that gets
            // disabled — so the receiver variable has to be resolved, not assumed.
            var groupPrefixes = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (Match g in Regex.Matches(
                         source, @"var\s+(?<var>\w+)\s*=\s*(?<parent>\w+)\.MapGroup\s*\(\s*""(?<prefix>[^""]+)"""))
            {
                var parent = g.Groups["parent"].Value;
                var parentPrefix = groupPrefixes.TryGetValue(parent, out var p) ? p : string.Empty;
                groupPrefixes[g.Groups["var"].Value] =
                    parentPrefix + g.Groups["prefix"].Value.TrimEnd('/');
            }

            foreach (Match match in Regex.Matches(
                         source,
                         @"(?<recv>\w+)\.Map(?<verb>Get|Post|Put|Patch|Delete)\s*\(\s*""(?<route>[^""]+)"""))
            {
                var receiver = match.Groups["recv"].Value;
                var prefix = groupPrefixes.TryGetValue(receiver, out var gp) ? gp : string.Empty;
                var route = prefix + match.Groups["route"].Value;

                // Collapse {id}, {id:guid}, {*path} to the client's {} placeholder.
                route = Regex.Replace(route, @"\{[^}]+\}", "{}");

                // Files register group-relative ("/containers/…"); a legacy absolute path
                // ("/api/spe/containers/…") is normalised to the same shape rather than dropped, so
                // this rule keeps working if one reappears. RouteAuthorizationGuardTests Rule E is what
                // objects to absolute paths — this rule's job is agreement, not registration style.
                var normalised = route.StartsWith("/api/spe", StringComparison.Ordinal)
                    ? route["/api".Length..]
                    : ClientPrefix + route;

                routes.Add($"{match.Groups["verb"].Value.ToUpperInvariant()} {normalised.TrimEnd('/')}");
            }
        }

        return routes;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
               ?? throw new InvalidOperationException(
                   "Could not locate the repository root from the test output directory.");
    }
}

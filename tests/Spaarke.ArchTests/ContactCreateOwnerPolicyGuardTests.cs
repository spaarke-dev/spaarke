using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ISS-005 (#1673, TENANT-ISOLATION; UAC review) — "this binder creates contacts with no owner" is a decision that only
/// <c>ContactIdentityBinderFactory</c> (a binder over ANOTHER Dataverse environment) may make. Every other binder, the
/// DI-registered one over this BFF's own environment included, is built with
/// <c>ContactCreateOwner.FromCustomerBusinessUnit</c> and refuses a create it cannot own. So
/// <c>ContactCreateOwner.NoneForForeignEnvironment</c> may be mentioned in server code only inside that factory class (and
/// declared in <c>ContactCreateOwner.cs</c>); a use anywhere else is how an own-environment binder would silently create
/// app-owned contacts in root again.
/// </summary>
/// <remarks>
/// <para><b>What counts as a mention.</b> The name itself, in code: a call, a method group, a <c>nameof</c>. Comments (line,
/// doc) are not code and are skipped. The factory class is located by name in <c>ContactIdentityBinder.cs</c> and its body by
/// brace matching, so a use in the binder class, or anywhere else in that file, still fails. Test code is out of scope: tests
/// that do not exercise ownership build their binder with a named test-kit policy.</para>
/// <para>Known limit: the rule matches the member's name, not its behaviour. Reflection by a computed string is not seen;
/// the constructor of <c>ContactCreateOwner</c> is private and its factory methods are pinned below, so a new way to say "no
/// owner" has to appear in the one file this guard reads.</para>
/// <para>Crude by design (<see cref="SourceScan"/>), like every rule built on it: each rule has a must-fire and a
/// must-not-fire control below. Enforced by: this guard (ArchTests, CI and the Stop hook).</para>
/// </remarks>
public class ContactCreateOwnerPolicyGuardTests
{
    private const string FactoryFile = "src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ContactIdentityBinder.cs";
    private const string FactoryClass = "ContactIdentityBinderFactory";
    private const string DeclaringFile = "src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ContactCreateOwner.cs";

    // UAC review F3: also the bare variant name. ContactCreateOwner once had a nested ForeignEnvironment class with an internal
    // constructor, which `new ContactCreateOwner.ForeignEnvironment("x")` could use without naming the factory method.
    private static readonly Regex Mention = new(
        @"\b(?:NoneFor)?ForeignEnvironment\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The member's declaration; group 1 is the name, so a mention can be tied to it by position.</summary>
    private static readonly Regex Declaration = new(
        @"\bstatic\s+ContactCreateOwner\s+(NoneForForeignEnvironment)\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The mentions in one file's code text that the rule does not allow: "line N" for each.</summary>
    internal static IReadOnlyList<string> Violations(string relativePath, string code)
    {
        var allowed = new List<(int Start, int End)>();
        if (relativePath == FactoryFile)
        {
            var span = ClassBody(code, FactoryClass);
            if (span is null)
            {
                return new[] { $"class {FactoryClass} was not found (the guard would be vacuous)" };
            }

            allowed.Add(span.Value);
        }

        var declarations = relativePath == DeclaringFile
            ? Declaration.Matches(code).Select(d => d.Groups[1].Index).ToHashSet()
            : new HashSet<int>();
        var violations = new List<string>();
        foreach (Match m in Mention.Matches(code))
        {
            var inFactory = allowed.Any(a => m.Index >= a.Start && m.Index < a.End);
            var isDeclaration = declarations.Contains(m.Index);
            if (!inFactory && !isDeclaration)
            {
                violations.Add($"line {SourceScan.LineOf(code, m.Index)}");
            }
        }

        return violations;
    }

    /// <summary>[start, end) of the body of the first class with this name: from its opening brace to the matching one.</summary>
    private static (int Start, int End)? ClassBody(string code, string className)
    {
        var declaration = Regex.Match(code, @"\bclass\s+" + Regex.Escape(className) + @"\b");
        if (!declaration.Success)
        {
            return null;
        }

        var open = code.IndexOf('{', declaration.Index);
        if (open < 0)
        {
            return null;
        }

        var depth = 0;
        for (var i = open; i < code.Length; i++)
        {
            if (code[i] == '{')
            {
                depth++;
            }
            else if (code[i] == '}' && --depth == 0)
            {
                return (open, i + 1);
            }
        }

        return null;
    }

    [Fact(DisplayName = "ISS-005: NoneForForeignEnvironment is mentioned in server code only in ContactIdentityBinderFactory")]
    public void NoneForForeignEnvironment_IsUsedOnlyByTheFactory()
    {
        var offenders = new List<string>();
        foreach (var file in SourceScan.ServerSourceFiles())
        {
            var path = SourceScan.Relative(file).Replace('\\', '/');
            var code = SourceScan.CodeText(File.ReadAllLines(file));
            if (!Mention.IsMatch(code))
            {
                continue;
            }

            offenders.AddRange(Violations(path, code).Select(v => $"{path}: {v}"));
        }

        Assert.True(
            offenders.Count == 0,
            "ContactCreateOwner.NoneForForeignEnvironment means 'this binder creates ownerless contacts'. It is for a binder over "
            + "ANOTHER Dataverse environment and so only ContactIdentityBinderFactory may use it (ISS-005, #1673; the gap it leaves "
            + "is tracked in #2270). A binder over this BFF's own environment takes ContactCreateOwner.FromCustomerBusinessUnit. "
            + "Offending mentions:\n" + string.Join("\n", offenders));
    }

    [Fact(DisplayName = "ISS-005 guard, non-vacuous: the factory does use it, and the declaration exists")]
    public void TheGuard_SeesTheFactorysUse_AndTheDeclaration()
    {
        var factoryCode = SourceScan.CodeText(File.ReadAllLines(Path.Combine(SourceScan.RepoRoot, FactoryFile)));
        var body = ClassBody(factoryCode, FactoryClass);
        Assert.NotNull(body);
        Assert.True(
            Mention.Matches(factoryCode).Any(m => m.Index >= body!.Value.Start && m.Index < body.Value.End),
            "ContactIdentityBinderFactory no longer uses NoneForForeignEnvironment: update this guard with the change.");

        var declaringCode = SourceScan.CodeText(File.ReadAllLines(Path.Combine(SourceScan.RepoRoot, DeclaringFile)));
        Assert.Matches(Declaration, declaringCode);
    }

    // ── No ownerless contact create anywhere in server code (ISS-005, #1673; UAC re-check K3) ───────────────────

    private const string ServerRoot = "src/server/";
    private const string BffRoot = "src/server/api/Sprk.Bff.Api/";
    private const string StoreFile = BffRoot + "Infrastructure/ExternalAccess/ContactIdentityStore.cs";
    private const string InviteFile = BffRoot + "Api/ExternalAccess/ExternalInviteService.cs";

    /// <summary>
    /// A reference to the contact table in a create call: the entity-set / logical-name LITERAL, a constant named like the
    /// entity set (<c>*ContactEntitySet</c>), or a string constant anywhere in server code that holds <c>"contact"</c> /
    /// <c>"contacts"</c> (<paramref name="constantNames"/>, found by <see cref="ContactConstantNames"/>), possibly qualified
    /// (<c>Type.Name</c>).
    /// </summary>
    private static string ContactRef(IEnumerable<string> constantNames)
    {
        var names = new[] { @"\w*Contacts?EntitySet" }.Concat(constantNames.Distinct().Select(Regex.Escape));
        return "(?:" + ContactLiteral + @"|(?:[A-Za-z_]\w*\.)*(?:" + string.Join("|", names) + @")\b)";
    }

    /// <summary>
    /// The literal naming the contact table, in the forms a create call uses: <c>"contacts"</c>, an interpolated <c>$"contacts"</c>
    /// (UAC round 3 K3), and a URL or path ending in it, interpolated or not (<c>$"{GetApiUrl()}/contacts"</c>, the style of
    /// <c>ExternalDataService</c>; <c>".../api/data/v9.2/contacts"</c>). It must be the whole literal or END in <c>/contact(s)</c>:
    /// <c>"contacts(" + id</c>, <c>"contacts?$select=..."</c> and <c>"sprk_contacts"</c> are not creates.
    /// </summary>
    private const string ContactLiteral = @"(?:\$@?|@\$?)?""(?:[^""\n]*/)?contacts?""";

    /// <summary>A string constant holding the contact table: <c>const string X = "contacts";</c> or <c>static readonly string X = "contact";</c>.</summary>
    private static readonly Regex ContactConstant = new(
        @"\b(?:const|static\s+readonly)\s+string\s+(\w+)\s*=\s*""contacts?""\s*;", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The names of the string constants in one file's code that hold <c>"contact"</c> or <c>"contacts"</c>.</summary>
    internal static IReadOnlyList<string> ContactConstantNames(string code)
        => ContactConstant.Matches(code).Select(m => m.Groups[1].Value).ToList();

    private enum Shape
    {
        /// <summary><c>x.CreateAsync(contacts, payload)</c> (the Web API client, <c>DataverseWebApiClient</c>).</summary>
        WebApiCreateAsync,

        /// <summary>
        /// <c>WriteAsync(HttpMethod.Post, "contacts", ...)</c>, a <c>(HttpMethod.Post, "contacts", payload)</c> request tuple or a
        /// <c>new HttpRequestMessage(HttpMethod.Post, "contacts")</c>; the URL may be interpolated and carry a prefix
        /// (<c>new HttpRequestMessage(HttpMethod.Post, $"{GetApiUrl()}/contacts")</c>, the style of <c>ExternalDataService</c>).
        /// </summary>
        HttpPost,

        /// <summary>
        /// <c>x.PostAsync(".../contacts", body)</c>, <c>PostAsJsonAsync</c> and the like: a user-context Web API client
        /// (<c>IDataverseUserClient.PostAsync</c>) creating a contact as the caller (UAC round 3 K3).
        /// </summary>
        UserClientPost,

        /// <summary>
        /// <c>new Entity("contact")</c>, the SDK entity that <c>IGenericEntityService.CreateAsync(Entity)</c> and
        /// <c>IOrganizationService.Create</c> take (so an <c>IGenericEntityService</c> contact create is seen where its entity is
        /// built with a literal or a discovered constant).
        /// </summary>
        SdkEntity,
    }

    /// <summary>Each shape's regex; <c>{0}</c> is the contact reference. Braces are doubled for <see cref="string.Format(string, object)"/>.</summary>
    private static readonly (Shape Shape, string Pattern)[] ShapePatterns =
    {
        (Shape.WebApiCreateAsync, @"\.CreateAsync\s*\(\s*{0}"),
        (Shape.HttpPost, @"\bHttpMethod\.Post\s*,\s*{0}"),
        (Shape.UserClientPost, @"\.Post\w*Async\s*(?:<[^()]*>)?\s*\(\s*{0}"),
        (Shape.SdkEntity, @"\bnew\s+(?:[A-Za-z_]\w*\.)*Entity\s*(?:\(\s*{0}|\{{\s*LogicalName\s*=\s*{0})"),
    };

    // ── Member bounds (UAC round 3 K3.1): found by BRACE MATCHING, not by an access modifier ─────────────────────

    /// <summary>
    /// A member of the scanned code as the create-guard sees it: its header start (the text after the previous
    /// <c>;</c> / <c>{</c> / <c>}</c>), its end, the enclosing type's name and the member's own name (either may be null).
    /// </summary>
    internal readonly record struct MemberSpan(int Start, int End, string? TypeName, string? MemberName);

    /// <summary>A block whose header starts with one of these is control flow or a statement (an object initializer), not a member.</summary>
    private static readonly Regex ControlHeader = new(
        @"^\s*(?:if|else|for|foreach|while|do|switch|using|lock|catch|finally|try|fixed|unsafe|checked|unchecked|case|default|return|throw|yield|await|new|var|goto)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TypeHeader = new(
        @"\b(?:class|struct|record|interface)\s+(?:class\s+|struct\s+)?([A-Za-z_]\w*)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>An identifier followed by its parameter list: the member's name is the first one that is not a modifier.</summary>
    private static readonly Regex NameBeforeParameters = new(
        @"\b([A-Za-z_]\w*)\s*(?:<[^()]*>)?\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> NotAMemberName = new(StringComparer.Ordinal)
    {
        "public", "private", "protected", "internal", "static", "async", "virtual", "override", "sealed", "abstract", "extern",
        "unsafe", "partial", "new", "readonly", "return", "await", "throw", "yield", "this", "base", "where", "when",
    };

    /// <summary>
    /// <paramref name="code"/> with the CONTENT of every string and character literal blanked (newlines kept, length and indexes
    /// unchanged), so a brace or semicolon inside a literal (<c>"{"</c>, <c>$"/teams({id:D})"</c>) cannot unbalance a brace match.
    /// A regular literal ends at its line's end (line comments were already stripped by <see cref="SourceScan.CodeText"/>, which
    /// can leave <c>"https:</c> unterminated). Crude by design: an interpolation hole that itself holds a string literal is not
    /// followed.
    /// </summary>
    internal static string MaskLiterals(string code)
    {
        var masked = new StringBuilder(code);
        void Blank(int from, int to)
        {
            for (var k = from; k < to && k < masked.Length; k++)
            {
                if (masked[k] is not ('\n' or '\r'))
                {
                    masked[k] = ' ';
                }
            }
        }

        var i = 0;
        while (i < code.Length)
        {
            var c = code[i];
            if (c == '"')
            {
                var quotes = 0;
                while (i + quotes < code.Length && code[i + quotes] == '"')
                {
                    quotes++;
                }

                if (quotes >= 3)
                {
                    var close = code.IndexOf(new string('"', quotes), i + quotes, StringComparison.Ordinal);
                    var end = close < 0 ? code.Length : close + quotes;
                    Blank(i, end);
                    i = end;
                    continue;
                }

                var verbatim = i > 0 && (code[i - 1] == '@' || (code[i - 1] == '$' && i > 1 && code[i - 2] == '@'));
                var j = i + 1;
                if (verbatim)
                {
                    while (j < code.Length)
                    {
                        if (code[j] == '"')
                        {
                            if (j + 1 < code.Length && code[j + 1] == '"')
                            {
                                j += 2;
                                continue;
                            }

                            break;
                        }

                        j++;
                    }
                }
                else
                {
                    while (j < code.Length && code[j] != '\n')
                    {
                        if (code[j] == '\\')
                        {
                            j += 2;
                            continue;
                        }

                        if (code[j] == '"')
                        {
                            break;
                        }

                        j++;
                    }
                }

                var stop = Math.Min(j + 1, code.Length);
                Blank(i, stop);
                i = stop;
                continue;
            }

            if (c == '\'')
            {
                var j = i + 1;
                j += j < code.Length && code[j] == '\\' ? 2 : 1;
                while (j < code.Length && code[j] != '\'' && code[j] != '\n' && j - i < 10)
                {
                    j++;
                }

                if (j < code.Length && code[j] == '\'')
                {
                    Blank(i, j + 1);
                    i = j + 1;
                    continue;
                }
            }

            i++;
        }

        return masked.ToString();
    }

    /// <summary>The index of the <c>}</c> matching the <c>{</c> at <paramref name="open"/> in masked code (the end when unmatched).</summary>
    private static int MatchingBrace(string masked, int open)
    {
        var depth = 0;
        for (var i = open; i < masked.Length; i++)
        {
            if (masked[i] == '{')
            {
                depth++;
            }
            else if (masked[i] == '}' && --depth == 0)
            {
                return i;
            }
        }

        return masked.Length - 1;
    }

    /// <summary>The first index after the last <c>;</c>, <c>{</c> or <c>}</c> before <paramref name="index"/>: where a declaration or statement begins.</summary>
    private static int StatementStart(string masked, int index)
        => index <= 0 ? 0 : masked.LastIndexOfAny(new[] { ';', '{', '}' }, index - 1) + 1;

    /// <summary>
    /// The member that contains <paramref name="index"/>: the INNERMOST enclosing brace block that is a method, constructor,
    /// accessor-less member or local function (a header with a parameter list, not control flow, no <c>=</c> before it), taken
    /// from its declaration to its matching close brace - so a member with no access modifier, or a local function, is its own
    /// member. Lambdas and control-flow blocks belong to the member around them. A create that sits in no such block (an
    /// expression-bodied member, <c>=> (HttpMethod.Post, "contacts", ...);</c>) is bounded by its own statement. Pure.
    /// </summary>
    internal static MemberSpan FindMember(string code, string masked, int index)
    {
        var stack = new List<int>();
        for (var i = 0; i < index && i < masked.Length; i++)
        {
            if (masked[i] == '{')
            {
                stack.Add(i);
            }
            else if (masked[i] == '}' && stack.Count > 0)
            {
                stack.RemoveAt(stack.Count - 1);
            }
        }

        string? typeName = null;
        for (var k = stack.Count - 1; k >= 0 && typeName is null; k--)
        {
            var header = code[StatementStart(masked, stack[k])..stack[k]];
            if (TypeHeader.Match(header) is { Success: true } type && !ControlHeader.IsMatch(header))
            {
                typeName = type.Groups[1].Value;
            }
        }

        for (var k = stack.Count - 1; k >= 0; k--)
        {
            var open = stack[k];
            var start = StatementStart(masked, open);
            var header = code[start..open];
            var firstParenthesis = header.IndexOf('(', StringComparison.Ordinal);
            var methodLike = firstParenthesis >= 0
                && header.TrimEnd().EndsWith(')')
                && !header[..firstParenthesis].Contains('=', StringComparison.Ordinal)
                && !ControlHeader.IsMatch(header)
                && !TypeHeader.IsMatch(header);
            if (methodLike)
            {
                return new MemberSpan(start, MatchingBrace(masked, open) + 1, typeName, MemberNameOf(header));
            }
        }

        var statementStart = StatementStart(masked, index);
        var statementEnd = masked.Length;
        var nesting = 0;
        for (var i = index; i < masked.Length; i++)
        {
            if (masked[i] == '{')
            {
                nesting++;
            }
            else if (masked[i] == '}' && --nesting < 0)
            {
                statementEnd = i;
                break;
            }
            else if (masked[i] == ';' && nesting == 0)
            {
                statementEnd = i + 1;
                break;
            }
        }

        return new MemberSpan(statementStart, statementEnd, typeName, MemberNameOf(code[statementStart..index]));
    }

    private static string? MemberNameOf(string header)
        => NameBeforeParameters.Matches(header).Select(m => m.Groups[1].Value).FirstOrDefault(n => !NotAMemberName.Contains(n));

    /// <summary>
    /// Evidence that a member binds an owner: the Web API bind, the identity store's key constant, or the SDK
    /// <c>ownerid</c> attribute (the literal or <c>OwnerAttribute</c>).
    /// </summary>
    private static readonly Regex OwnerBind = new(
        @"ownerid@odata\.bind|\bOwnerBindKey\b|""ownerid""|\bOwnerAttribute\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>A known contact-create seam: where it is, and why it is allowed (or invisible to the scan).</summary>
    internal sealed record ContactCreateSeam(string File, string MemberDeclaration, string Reason);

    /// <summary>
    /// An allow-listed contact create (UAC round 3 K3.5): ONE member of ONE type in ONE file. Matched on the enclosing type's
    /// name and the member's name as found by <see cref="FindMember"/>, never on text in the member's header.
    /// </summary>
    internal sealed record AllowedContactCreate(string File, string TypeName, string MemberName, string Reason);

    /// <summary>
    /// The known seams that MATCH a create shape and bind their owner elsewhere. Each is allow-listed by file and member, with
    /// the reason; <see cref="EveryAllowedSeam_IsStillExercised_ByARealFile"/> fails when one stops matching, so the list cannot
    /// rot into dead entries. A NEW create shape matches no entry and fails the build.
    /// </summary>
    internal static readonly IReadOnlyList<AllowedContactCreate> AllowedSeams = new[]
    {
        new AllowedContactCreate(
            StoreFile,
            "DataverseContactIdentityStore",
            "BuildCreateRequest",
            "The identity store's one create (registration, first sign-in, licensed-user link): the request is the pure pair "
            + "BuildCreateRequest/CreatePayload, and CreatePayload pins ownerid@odata.bind on the Workforce plane (a "
            + "WorkforceHome create without an owner throws). The binders resolve that owner through "
            + "ContactIdentityBinder.ResolveCustomerOwnerTeamAsync before they call the store."),
    };

    /// <summary>
    /// Contact creates this guard CANNOT see, by design, and why each is outside the rule. Not allow-list entries (nothing
    /// matches them): <see cref="TheUndetectableSeams_StillExist"/> pins them so a rename shows up here.
    /// </summary>
    internal static readonly IReadOnlyList<ContactCreateSeam> UndetectableSeams = new[]
    {
        new ContactCreateSeam(
            BffRoot + "Services/Ai/Handlers/DataverseCreateRecordHandler.cs",
            "tablename",
            "Chat dataverse.create_record with tablename=contact: the table is a RUNTIME tool argument, so no source text names it. "
            + "App-only, owned by the caller's business-unit team via the general record-first resolver (OwnedChildWrite), and "
            + "it does NOT use ExternalAccess:CustomerBusinessUnitId: it is not an identity-contact create."),
    };

    /// <summary>
    /// One contact create found in a file: where, which shape, whether its member binds an owner, the enclosing type's name and
    /// the member's name (null when none was found), and the member's head up to the create.
    /// </summary>
    internal readonly record struct ContactCreateHit(
        int Index, int Line, string Shape, bool Owned, string? TypeName, string? MemberName, string Header);

    /// <summary>
    /// Every contact create in one server file's code (any of the shapes), with whether the enclosing member binds an owner. The
    /// member is found by brace matching (<see cref="FindMember"/>). The Web API shapes look from the member's declaration to the
    /// call (the payload is built before it); the SDK shape looks to the end of the member (the owner is set on the entity between
    /// its construction and the create).
    /// </summary>
    internal static IReadOnlyList<ContactCreateHit> ContactCreates(
        string relativePath, string code, IReadOnlyCollection<string>? constantNames = null)
    {
        var hits = new List<ContactCreateHit>();
        if (!relativePath.StartsWith(ServerRoot, StringComparison.Ordinal))
        {
            return hits;
        }

        var contact = ContactRef(constantNames ?? Array.Empty<string>());
        var masked = MaskLiterals(code);
        foreach (var (shape, pattern) in ShapePatterns)
        {
            var regex = new Regex(
                string.Format(System.Globalization.CultureInfo.InvariantCulture, pattern, contact), RegexOptions.CultureInvariant);
            foreach (Match create in regex.Matches(code))
            {
                var member = FindMember(code, masked, create.Index);
                var evidenceEnd = shape == Shape.SdkEntity ? member.End : create.Index;
                hits.Add(new ContactCreateHit(
                    create.Index,
                    SourceScan.LineOf(code, create.Index),
                    shape.ToString(),
                    OwnerBind.IsMatch(code.Substring(member.Start, Math.Max(0, evidenceEnd - member.Start))),
                    member.TypeName,
                    member.MemberName,
                    code.Substring(member.Start, Math.Max(0, create.Index - member.Start))));
            }
        }

        return hits.OrderBy(h => h.Index).ToList();
    }

    /// <summary>
    /// True only for a create inside an allow-listed seam's own declaration: the same file, the same enclosing TYPE name and the
    /// same MEMBER name (UAC round 3 K3.5; it used to accept any member whose header merely contained
    /// <c>BuildCreateRequest(</c>, so a same-named member of another type, or a longer name, was excused too).
    /// </summary>
    private static bool IsAllowedSeam(string relativePath, ContactCreateHit hit)
        => AllowedSeams.Any(a => a.File == relativePath
            && string.Equals(a.TypeName, hit.TypeName, StringComparison.Ordinal)
            && string.Equals(a.MemberName, hit.MemberName, StringComparison.Ordinal));

    /// <summary>
    /// The contact creates in one server file's code that bind no owner and are not an allow-listed seam: "line N (shape)" for
    /// each. A contact created through the Web API without an owner is owned by the calling application user in the root
    /// business unit (ISS-005). Known limits (the scan is name-based, <see cref="SourceScan"/>): a payload built in ANOTHER member
    /// and passed in is flagged too (build it where it is created); a create whose table is a runtime value
    /// (<c>dataverse.create_record</c>'s <c>tablename</c>, <c>ChildRecordEndpoints</c>), a <c>CreateRequest</c> / <c>ExecuteAsync</c>,
    /// a table name built by concatenation, or a constant that is not a <c>const</c>/<c>static readonly string</c> holding the
    /// literal is NOT seen (<see cref="UndetectableSeams"/> names the known ones); an <c>IGenericEntityService.CreateAsync</c> is
    /// seen only through its entity's <c>new Entity("contact")</c>; owner evidence is by name and does not prove the value is a
    /// resolved team. The member is found by brace matching (<see cref="FindMember"/>): a method with a <c>where</c> constraint
    /// (its header does not end in <c>)</c>) or an expression-bodied local function is not seen as a member, so its create is
    /// judged by the member around it (a local function) or by its own statement (a constraint-headed method).
    /// </summary>
    internal static IReadOnlyList<string> OwnerlessContactCreates(
        string relativePath, string code, IReadOnlyCollection<string>? constantNames = null)
        => ContactCreates(relativePath, code, constantNames)
            .Where(h => !h.Owned && !IsAllowedSeam(relativePath, h))
            .Select(h => $"line {h.Line} ({h.Shape})")
            .ToList();

    /// <summary>Every server source file as (relative path, code text).</summary>
    private static IReadOnlyList<(string Path, string Code)> ServerCode()
        => SourceScan.ServerSourceFiles()
            .Select(f => (SourceScan.Relative(f).Replace('\\', '/'), SourceScan.CodeText(File.ReadAllLines(f))))
            .ToList();

    private static IReadOnlyCollection<string> AllContactConstantNames(IEnumerable<(string Path, string Code)> files)
        => files.SelectMany(f => ContactConstantNames(f.Code)).Distinct().ToList();

    [Fact(DisplayName = "ISS-005: no contact create in server code is ownerless (CreateAsync, HttpMethod.Post, new Entity)")]
    public void EveryContactCreate_InServerCode_SetsAnOwner_OrIsAnAllowListedSeam()
    {
        var files = ServerCode();
        var names = AllContactConstantNames(files);
        var offenders = new List<string>();
        var seen = 0;
        foreach (var (path, code) in files)
        {
            seen += ContactCreates(path, code, names).Count;
            offenders.AddRange(OwnerlessContactCreates(path, code, names).Select(v => $"{path}: {v}"));
        }

        Assert.True(seen > 0, "the scan saw no contact create at all (the invite create, the store request): the guard is vacuous");
        Assert.True(
            offenders.Count == 0,
            "A contact created with no ownerid is owned by the BFF's application user in the root business unit (ISS-005, #1673). "
            + "Resolve the owner team (ContactIdentityBinder.ResolveCreateOwnerAsync) and bind it, or - for a create that is owned "
            + "by construction elsewhere - add an AllowedSeams entry with the reason. Offending creates:\n" + string.Join("\n", offenders));
    }

    [Fact(DisplayName = "ISS-005 guard, non-vacuous: every allow-listed seam is still matched by a real file")]
    public void EveryAllowedSeam_IsStillExercised_ByARealFile()
    {
        var files = ServerCode();
        var names = AllContactConstantNames(files);
        foreach (var seam in AllowedSeams)
        {
            var (path, code) = files.Single(f => f.Path == seam.File);
            Assert.True(
                ContactCreates(path, code, names).Any(h => IsAllowedSeam(path, h)),
                $"allow-listed seam {seam.File} / {seam.TypeName}.{seam.MemberName} no longer matches a contact create: remove or update the entry.");
        }

        // The invite create is the other real create, and it is owned (not a seam).
        var invite = files.Single(f => f.Path == InviteFile);
        Assert.Contains(ContactCreates(invite.Path, invite.Code, names), h => h.Owned && h.Shape == nameof(Shape.WebApiCreateAsync));
    }

    [Fact(DisplayName = "ISS-005 guard: the seams it cannot detect still exist where the doc comment says")]
    public void TheUndetectableSeams_StillExist()
    {
        foreach (var seam in UndetectableSeams)
        {
            var path = Path.Combine(SourceScan.RepoRoot, seam.File.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"{seam.File} moved: update UndetectableSeams (the record-first chat create).");
            Assert.Contains(seam.MemberDeclaration, File.ReadAllText(path), StringComparison.Ordinal);
        }
    }

    // ── Controls: must fire (one per create shape, and the constant form) ────────────────────────────

    [Fact(DisplayName = "ISS-005 guard, must-fire: an ownerless contact CreateAsync is caught")]
    public void Control_AnOwnerlessContactCreate_IsCaught()
        => Assert.Single(OwnerlessContactCreates(InviteFile, """
            private static async Task<Guid> CreateContactAsync(DataverseWebApiClient client, string email)
            {
                var payload = new Dictionary<string, object?> { ["emailaddress1"] = email };
                return await client.CreateAsync("contacts", payload, ct);
            }
            """));

    [Fact(DisplayName = "ISS-005 guard, must-fire: a constant-named contact set is caught, and a bind in ANOTHER member does not excuse it")]
    public void Control_AConstantNamedSet_AndABindElsewhere_AreCaught()
        => Assert.Single(OwnerlessContactCreates(InviteFile, """
            private static object Other() => new Dictionary<string, object?> { ["ownerid@odata.bind"] = "/teams(x)" };

            private static async Task<Guid> CreateContactAsync(DataverseWebApiClient client, string email)
            {
                var payload = new Dictionary<string, object?> { ["emailaddress1"] = email };
                return await client.CreateAsync(ContactEntitySet, payload, ct);
            }
            """));

    [Fact(DisplayName = "ISS-005 guard, must-fire: WriteAsync(HttpMethod.Post, \"contacts\") is caught")]
    public void Control_AWriteAsyncPostToContacts_IsCaught()
        => Assert.Single(OwnerlessContactCreates(BffRoot + "Services/X/NewStore.cs", """
            public async Task<StoreWriteResult> MakeAsync(Dictionary<string, object?> payload, CancellationToken ct)
            {
                return await WriteAsync(HttpMethod.Post, "contacts", payload, null, ct);
            }
            """));

    [Fact(DisplayName = "ISS-005 guard, must-fire: a (HttpMethod.Post, \"contacts\", payload) request tuple is caught outside the allow-list")]
    public void Control_ARequestTuple_OutsideTheAllowList_IsCaught()
        => Assert.Single(OwnerlessContactCreates(BffRoot + "Services/X/NewStore.cs", """
            public static (HttpMethod Method, string Path, Dictionary<string, object?> Payload) BuildCreateRequest(Guid oid)
                => (HttpMethod.Post, "contacts", new Dictionary<string, object?>());
            """));

    [Fact(DisplayName = "ISS-005 guard, must-fire: new Entity(\"contact\") with no owner is caught, in each spelling")]
    public void Control_AnSdkContactEntity_WithNoOwner_IsCaught()
    {
        Assert.Single(OwnerlessContactCreates(BffRoot + "Services/X/Makes.cs", """
            private async Task<Guid> MakeAsync(IGenericEntityService entities, CancellationToken ct)
            {
                var entity = new Entity("contact");
                entity["lastname"] = "Example";
                return await entities.CreateAsync(entity, ct);
            }
            """));
        Assert.Single(OwnerlessContactCreates(BffRoot + "Services/X/Makes.cs", """
            private Task<Guid> MakeAsync(IGenericEntityService entities, CancellationToken ct)
                => entities.CreateAsync(new Microsoft.Xrm.Sdk.Entity("contact") { ["lastname"] = "Example" }, ct);
            """));
        Assert.Single(OwnerlessContactCreates(BffRoot + "Services/X/Makes.cs", """
            private Task<Guid> MakeAsync(IGenericEntityService entities, CancellationToken ct)
                => entities.CreateAsync(new Entity { LogicalName = "contact" }, ct);
            """));
    }

    [Fact(DisplayName = "ISS-005 guard, must-fire: a string constant holding \"contact\" is followed into the create call")]
    public void Control_AStringConstantHoldingContact_IsFollowed()
    {
        // The constant is declared in ONE file and used in another: the names are collected across server code.
        var names = ContactConstantNames("public const string Person = \"contact\";\ninternal static readonly string PersonSet = \"contacts\";");
        Assert.Equal(new[] { "Person", "PersonSet" }, names);

        Assert.Single(OwnerlessContactCreates(BffRoot + "Services/X/Makes.cs", """
            private Task<Guid> MakeAsync(IGenericEntityService entities, CancellationToken ct)
            {
                var entity = new Entity(Names.Person);
                return entities.CreateAsync(entity, ct);
            }
            """, names));
        Assert.Single(OwnerlessContactCreates(BffRoot + "Services/X/Makes.cs", """
            private Task<Guid> MakeAsync(DataverseWebApiClient client, object payload, CancellationToken ct)
                => client.CreateAsync(PersonSet, payload, ct);
            """, names));

        // Without the discovered names the same code is invisible: the constants pass is what makes it fire.
        Assert.Empty(OwnerlessContactCreates(BffRoot + "Services/X/Makes.cs", "return client.CreateAsync(PersonSet, payload, ct);"));
    }

    [Fact(DisplayName = "ISS-005 guard, must-fire: a create outside the BFF, in shared server code, is caught")]
    public void Control_ACreateOutsideTheBff_IsCaught()
        => Assert.Single(OwnerlessContactCreates("src/server/shared/Spaarke.Dataverse/X.cs", "return await client.CreateAsync(\"contacts\", payload, ct);"));

    // ── Controls: must not fire ──────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "ISS-005 guard, must-not-fire: a contact create that binds the owner passes (Web API and SDK)")]
    public void Control_AnOwnedContactCreate_HasNoViolation()
    {
        Assert.Empty(OwnerlessContactCreates(InviteFile, """
            private static async Task<Guid> CreateContactAsync(DataverseWebApiClient client, string email, Guid team)
            {
                var payload = new Dictionary<string, object?>
                {
                    ["emailaddress1"] = email,
                    ["ownerid@odata.bind"] = $"/teams({team:D})",
                };
                return await client.CreateAsync(ContactEntitySet, payload, ct);
            }
            """));

        // The SDK owner is set AFTER the construction and before the create: the whole member is the evidence.
        Assert.Empty(OwnerlessContactCreates(BffRoot + "Services/X/Makes.cs", """
            private async Task<Guid> MakeAsync(IGenericEntityService entities, Guid team, CancellationToken ct)
            {
                var entity = new Entity("contact");
                entity["lastname"] = "Example";
                entity["ownerid"] = new EntityReference("team", team);
                return await entities.CreateAsync(entity, ct);
            }
            """));
        Assert.Empty(OwnerlessContactCreates(BffRoot + "Services/X/Makes.cs", """
            private async Task<Guid> MakeAsync(IGenericEntityService entities, Guid team, CancellationToken ct)
            {
                var entity = new Entity("contact");
                entity[OwnerAttribute] = new EntityReference("team", team);
                return await entities.CreateAsync(entity, ct);
            }
            """));
    }

    private const string StoreSeamShape = """
        public sealed class DataverseContactIdentityStore : IContactIdentityStore
        {
            public static (HttpMethod Method, string Path, Dictionary<string, object?> Payload) BuildCreateRequest(
                Guid oid, IdentityPlaneMarker plane, NewContactDetails details)
                => (HttpMethod.Post, "contacts", CreatePayload(oid, plane, details));
        }
        """;

    [Fact(DisplayName = "ISS-005 guard, must-not-fire: the identity store's BuildCreateRequest tuple is the allow-listed seam")]
    public void Control_TheIdentityStoresRequestTuple_IsAllowListed()
    {
        Assert.Empty(OwnerlessContactCreates(StoreFile, StoreSeamShape));

        // The seam is found by its type and member names: the control sees them, not just "no violation".
        var hit = Assert.Single(ContactCreates(StoreFile, StoreSeamShape));
        Assert.Equal("DataverseContactIdentityStore", hit.TypeName);
        Assert.Equal("BuildCreateRequest", hit.MemberName);
    }

    [Fact(DisplayName = "ISS-005 guard, must-not-fire: creates in other tables, EntityReference to a contact, reads and PATCHes pass")]
    public void Control_OtherTables_References_AndReads_HaveNoViolation()
    {
        Assert.Empty(OwnerlessContactCreates(InviteFile, "return await client.CreateAsync(\"accounts\", payload, ct);"));
        Assert.Empty(OwnerlessContactCreates(InviteFile, "var e = new Entity(\"sprk_todo\"); e[\"sprk_assignedto\"] = new EntityReference(\"contact\", id);"));
        Assert.Empty(OwnerlessContactCreates(InviteFile, "var row = await client.RetrieveAsync<Contact>(\"contacts\", id, ct);"));
        Assert.Empty(OwnerlessContactCreates(InviteFile, "return await WriteAsync(HttpMethod.Patch, \"contacts(\" + id + \")\", payload, null, ct);"));
        Assert.Empty(OwnerlessContactCreates(InviteFile, "return await WriteAsync(HttpMethod.Post, \"accounts\", payload, null, ct);"));
        Assert.Empty(OwnerlessContactCreates("src/client/X.cs", "return await client.CreateAsync(\"contacts\", payload, ct);"));
        Assert.Empty(OwnerlessContactCreates(InviteFile, "var e = new Entity(Names.Person);", new[] { "Other" }));
    }

    private const string StoreSeamWithASecondCreate = """
        public sealed class DataverseContactIdentityStore : IContactIdentityStore
        {
            public static (HttpMethod Method, string Path, Dictionary<string, object?> Payload) BuildCreateRequest(
                Guid oid, IdentityPlaneMarker plane, NewContactDetails details)
                => (HttpMethod.Post, "contacts", CreatePayload(oid, plane, details));

            public async Task<Guid> OtherAsync(DataverseWebApiClient client, object payload, CancellationToken ct)
            {
                return await client.CreateAsync("contacts", payload, ct);
            }
        }
        """;

    [Fact(DisplayName = "ISS-005 guard, must-not-fire: a seam's allow-list entry does not excuse the same shape in another file, type or member")]
    public void Control_TheAllowList_IsPerFileTypeAndMember()
    {
        Assert.Empty(OwnerlessContactCreates(StoreFile, StoreSeamShape));

        // Another file.
        Assert.Single(OwnerlessContactCreates(BffRoot + "Services/X/Other.cs", StoreSeamShape));
        // Another member name, or a longer one that merely CONTAINS the name (it used to be excused by a substring test).
        Assert.Single(OwnerlessContactCreates(StoreFile, StoreSeamShape.Replace("BuildCreateRequest", "BuildAnotherRequest", StringComparison.Ordinal)));
        Assert.Single(OwnerlessContactCreates(StoreFile, StoreSeamShape.Replace("BuildCreateRequest", "BuildCreateRequestFor", StringComparison.Ordinal)));
        // Another type in the SAME file with the same member name (UAC round 3 K3.5), and no enclosing type at all.
        Assert.Single(OwnerlessContactCreates(StoreFile, StoreSeamShape.Replace("DataverseContactIdentityStore", "SomeOtherStore", StringComparison.Ordinal)));
        Assert.Single(OwnerlessContactCreates(StoreFile, """
            public static (HttpMethod Method, string Path, Dictionary<string, object?> Payload) BuildCreateRequest(Guid oid)
                => (HttpMethod.Post, "contacts", new Dictionary<string, object?>());
            """));
        // A second, ownerless create in the seam's own type is not excused by the seam member's entry.
        var other = Assert.Single(OwnerlessContactCreates(StoreFile, StoreSeamWithASecondCreate));
        Assert.Contains("WebApiCreateAsync", other, StringComparison.Ordinal);
    }

    // ── UAC round 3 K3.1: member bounds by brace matching ───────────────────────────────────────────

    [Fact(DisplayName = "ISS-005 guard, must-fire: an ownerless create right after an owned member with NO access modifier is caught")]
    public void Control_AnOwnerlessCreate_AfterAnOwnedModifierlessMember_IsCaught()
        => Assert.Single(OwnerlessContactCreates(BffRoot + "Services/X/Makes.cs", """
            public sealed class Maker
            {
                async Task<Guid> OwnedAsync(DataverseWebApiClient client, Guid team, CancellationToken ct)
                {
                    var payload = new Dictionary<string, object?> { ["ownerid@odata.bind"] = $"/teams({team:D})" };
                    return await client.CreateAsync("contacts", payload, ct);
                }

                async Task<Guid> OwnerlessAsync(DataverseWebApiClient client, CancellationToken ct)
                {
                    return await client.CreateAsync("contacts", new { lastname = "x" }, ct);
                }
            }
            """));

    [Fact(DisplayName = "ISS-005 guard, must-fire: an ownerless local function is its own member, even inside an owned method")]
    public void Control_AnOwnerlessLocalFunction_InsideAnOwnedMethod_IsCaught()
        => Assert.Single(OwnerlessContactCreates(BffRoot + "Services/X/Makes.cs", """
            public sealed class Maker
            {
                public async Task<Guid> RunAsync(DataverseWebApiClient client, Guid team, CancellationToken ct)
                {
                    var owner = new Dictionary<string, object?> { ["ownerid@odata.bind"] = $"/teams({team:D})" };

                    async Task<Guid> Ownerless()
                    {
                        return await client.CreateAsync("contacts", new { lastname = "x" }, ct);
                    }

                    return await Ownerless();
                }
            }
            """));

    [Fact(DisplayName = "ISS-005 guard, must-fire: an SDK contact entity with no owner in a modifier-less member after an owned one is caught")]
    public void Control_AnSdkCreate_AfterAnOwnedModifierlessMember_IsCaught()
        => Assert.Single(OwnerlessContactCreates(BffRoot + "Services/X/Makes.cs", """
            public sealed class Maker
            {
                Task<Guid> Owned(IGenericEntityService entities, Guid team, CancellationToken ct)
                {
                    var entity = new Entity("contact");
                    entity["ownerid"] = new EntityReference("team", team);
                    return entities.CreateAsync(entity, ct);
                }

                Task<Guid> Ownerless(IGenericEntityService entities, CancellationToken ct)
                {
                    var entity = new Entity("contact");
                    return entities.CreateAsync(entity, ct);
                }
            }
            """));

    [Fact(DisplayName = "ISS-005 guard, must-fire: a brace inside a string or character literal does not hide the member boundary")]
    public void Control_ABraceInALiteral_DoesNotHideTheBoundary()
        => Assert.Single(OwnerlessContactCreates(BffRoot + "Services/X/Makes.cs", """
            public sealed class Maker
            {
                async Task<Guid> OwnedAsync(DataverseWebApiClient client, Guid team, CancellationToken ct)
                {
                    var open = "{";
                    var payload = new Dictionary<string, object?> { ["ownerid@odata.bind"] = $"/teams({team:D})", ["x"] = open };
                    return await client.CreateAsync("contacts", payload, ct);
                }

                async Task<Guid> OwnerlessAsync(DataverseWebApiClient client, CancellationToken ct)
                {
                    var open = '{';
                    return await client.CreateAsync("contacts", new { lastname = "x" }, ct);
                }
            }
            """));

    [Fact(DisplayName = "ISS-005 guard, must-not-fire: owned members without a modifier, owned local functions and lambdas pass")]
    public void Control_OwnedModifierlessMembers_LocalFunctions_AndLambdas_HaveNoViolation()
        => Assert.Empty(OwnerlessContactCreates(BffRoot + "Services/X/Makes.cs", """
            public sealed class Maker
            {
                async Task<Guid> OwnedAsync(DataverseWebApiClient client, Guid team, CancellationToken ct)
                {
                    var payload = new Dictionary<string, object?> { ["ownerid@odata.bind"] = $"/teams({team:D})" };
                    if (team != Guid.Empty)
                    {
                        return await client.CreateAsync("contacts", payload, ct);
                    }

                    return Guid.Empty;
                }

                async Task<Guid> AlsoOwnedAsync(DataverseWebApiClient client, Guid team, CancellationToken ct)
                {
                    var owned = new Dictionary<string, object?> { ["ownerid@odata.bind"] = $"/teams({team:D})" };
                    var make = async () => { return await client.CreateAsync("contacts", owned, ct); };
                    return await make();
                }

                async Task<Guid> OwnedWithABraceInALiteral(DataverseWebApiClient client, Guid team, CancellationToken ct)
                {
                    var close = "}";
                    var payload = new Dictionary<string, object?> { ["ownerid@odata.bind"] = $"/teams({team:D})", ["x"] = close };
                    return await client.CreateAsync("contacts", payload, ct);
                }

                async Task<Guid> OwnedInTheLocalFunction(DataverseWebApiClient client, Guid team, CancellationToken ct)
                {
                    async Task<Guid> Local()
                    {
                        var payload = new Dictionary<string, object?> { ["ownerid@odata.bind"] = $"/teams({team:D})" };
                        return await client.CreateAsync("contacts", payload, ct);
                    }

                    return await Local();
                }
            }
            """));

    [Fact(DisplayName = "ISS-005 guard: literal masking blanks string and character contents, keeps length and newlines")]
    public void Control_MaskLiterals_BlanksLiteralsOnly()
    {
        const string code = "var a = \"{\"; var b = '{'; var c = $\"/teams({id:D})\"; var d = @\"x\"\"{\"; var e = \"https:\nvar f = 1; {}";
        var masked = MaskLiterals(code);

        Assert.Equal(code.Length, masked.Length);
        Assert.Equal(code.Count(ch => ch == '\n'), masked.Count(ch => ch == '\n'));
        // Only the one structural pair on the last line survives: the literal braces (string, char, interpolation, verbatim, unterminated) are blanked.
        Assert.Equal(1, masked.Count(ch => ch == '{'));
        Assert.Equal(1, masked.Count(ch => ch == '}'));
        Assert.Contains("var f = 1; {}", masked, StringComparison.Ordinal);
    }

    // ── UAC round 3 K3.2 to K3.4: the create shapes the guard missed ──────────────────────────────

    [Fact(DisplayName = "ISS-005 guard, must-fire: new HttpRequestMessage(HttpMethod.Post, $\"{GetApiUrl()}/contacts\") is caught")]
    public void Control_AnInterpolatedPostUrlWithAPrefix_IsCaught()
    {
        var hit = Assert.Single(ContactCreates(BffRoot + "Services/X/Makes.cs", """
            private async Task<HttpResponseMessage> MakeAsync(HttpClient http, string json, CancellationToken ct)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{GetApiUrl()}/contacts");
                request.Content = new StringContent(json);
                return await http.SendAsync(request, ct);
            }
            """));
        Assert.Equal(nameof(Shape.HttpPost), hit.Shape);
        Assert.False(hit.Owned);
    }

    [Theory(DisplayName = "ISS-005 guard, must-fire: an interpolated or path-prefixed contacts literal in a create call is caught")]
    [InlineData("return client.CreateAsync($\"contacts\", payload, ct);", "WebApiCreateAsync")]
    [InlineData("return client.CreateAsync($\"{prefix}/contacts\", payload, ct);", "WebApiCreateAsync")]
    [InlineData("return client.CreateAsync($@\"{prefix}/contacts\", payload, ct);", "WebApiCreateAsync")]
    [InlineData("return WriteAsync(HttpMethod.Post, $\"contacts\", payload, null, ct);", "HttpPost")]
    [InlineData("return new HttpRequestMessage(HttpMethod.Post, \"/api/data/v9.2/contacts\");", "HttpPost")]
    [InlineData("var e = new Entity($\"contact\");", "SdkEntity")]
    public void Control_AnInterpolatedOrPrefixedContactsLiteral_IsCaught(string statement, string shape)
    {
        var hit = Assert.Single(ContactCreates(BffRoot + "Services/X/Makes.cs", "private Task MakeAsync() { " + statement + " }"));
        Assert.Equal(shape, hit.Shape);
        Assert.False(hit.Owned);
    }

    [Theory(DisplayName = "ISS-005 guard, must-fire: a user-context PostAsync / PostAsJsonAsync to contacts is caught")]
    [InlineData("return await userClient.PostAsync(\"/api/data/v9.2/contacts\", body, ct);")]
    [InlineData("return await userClient.PostAsync($\"{baseUrl}/contacts\", body, ct);")]
    [InlineData("return await http.PostAsJsonAsync(\"contacts\", payload, ct);")]
    [InlineData("return await userClient.PostAsync<ContactRow>(\"contacts\", payload, ct);")]
    public void Control_AUserContextPostToContacts_IsCaught(string statement)
    {
        var code = "private Task MakeAsync() { " + statement + " }";
        var hit = Assert.Single(ContactCreates(BffRoot + "Services/X/Makes.cs", code));
        Assert.Equal(nameof(Shape.UserClientPost), hit.Shape);
        Assert.Single(OwnerlessContactCreates(BffRoot + "Services/X/Makes.cs", code));
    }

    [Fact(DisplayName = "ISS-005 guard, must-not-fire: a user-context contacts POST that binds an owner passes")]
    public void Control_AnOwnedUserContextPost_HasNoViolation()
        => Assert.Empty(OwnerlessContactCreates(BffRoot + "Services/X/Makes.cs", """
            private async Task<HttpResponseMessage> MakeAsync(IDataverseUserClient userClient, Guid team, CancellationToken ct)
            {
                var body = new Dictionary<string, object?> { ["ownerid@odata.bind"] = $"/teams({team:D})" };
                return await userClient.PostAsync("/api/data/v9.2/contacts", body, ct);
            }
            """));

    [Theory(DisplayName = "ISS-005 guard, must-not-fire: other tables, reads, PATCHes and record URLs under the new literal forms pass")]
    [InlineData("return client.CreateAsync($\"accounts\", payload, ct);")]
    [InlineData("return client.CreateAsync($\"{prefix}/accounts\", payload, ct);")]
    [InlineData("return new HttpRequestMessage(HttpMethod.Post, $\"{GetApiUrl()}/sprk_signals\");")]
    [InlineData("return new HttpRequestMessage(HttpMethod.Get, $\"{GetApiUrl()}/contacts\");")]
    [InlineData("return new HttpRequestMessage(HttpMethod.Patch, $\"{GetApiUrl()}/contacts({id})\");")]
    [InlineData("return new HttpRequestMessage(HttpMethod.Post, $\"{GetApiUrl()}/contacts({id})/Microsoft.Dynamics.CRM.X\");")]
    [InlineData("return new HttpRequestMessage(HttpMethod.Post, $\"{GetApiUrl()}/sprk_contacts\");")]
    [InlineData("return await userClient.PostAsync(\"/api/data/v9.2/accounts\", body, ct);")]
    [InlineData("return await userClient.GetAsync(\"/api/data/v9.2/contacts\", ct);")]
    [InlineData("return await userClient.PostAsync(\"/api/data/v9.2/contacts?$select=fullname\", body, ct);")]
    [InlineData("var e = new Entity($\"account\");")]
    public void Control_OtherTargets_UnderTheNewLiteralForms_HaveNoViolation(string statement)
        => Assert.Empty(ContactCreates(BffRoot + "Services/X/Makes.cs", "private Task MakeAsync() { " + statement + " }"));

    // ── The type's shape: there is no other way to say "no owner" ────────────────────────────────────

    /// <summary>
    /// The constructors of <paramref name="type"/> and of every type nested in it, recursively, that are not private: the ways
    /// to make a policy object other than the two factory methods. Reflection, so it sees a nested class a name scan would not.
    /// </summary>
    internal static IReadOnlyList<string> NonPrivateConstructors(Type type)
    {
        var offenders = new List<string>();
        foreach (var constructor in type.GetConstructors(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!constructor.IsPrivate)
            {
                offenders.Add($"{type.FullName}: {constructor}");
            }
        }

        foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
        {
            offenders.AddRange(NonPrivateConstructors(nested));
        }

        return offenders;
    }

    [Fact(DisplayName = "ISS-005: ContactCreateOwner and every type nested in it have only private constructors; exactly the two factory methods")]
    public void ContactCreateOwner_CanOnlyBeMadeByItsTwoFactoryMethods()
    {
        Assert.Empty(NonPrivateConstructors(typeof(ContactCreateOwner)));
        Assert.True(typeof(ContactCreateOwner).IsSealed, "a subclass would be a third way to make one");

        var factories = typeof(ContactCreateOwner)
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(m => typeof(ContactCreateOwner).IsAssignableFrom(m.ReturnType) && !m.IsPrivate)
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal);
        Assert.Equal(new[] { nameof(ContactCreateOwner.FromCustomerBusinessUnit), nameof(ContactCreateOwner.NoneForForeignEnvironment) },
            factories);
    }

    // Fixtures for the reflection check's controls.
    private sealed class FixtureLeaky
    {
        private FixtureLeaky()
        {
        }

        internal sealed class Variant
        {
            internal Variant()
            {
            }
        }
    }

    private sealed class FixtureSealed
    {
        private FixtureSealed()
        {
        }

        private sealed class Variant
        {
            private Variant()
            {
            }
        }
    }

    [Fact(DisplayName = "ISS-005 guard, must-fire: a nested type with an internal constructor is caught")]
    public void Control_ANestedTypeWithAnInternalConstructor_IsCaught()
        => Assert.Single(NonPrivateConstructors(typeof(FixtureLeaky)));

    [Fact(DisplayName = "ISS-005 guard, must-not-fire: private constructors, nested ones included, pass")]
    public void Control_PrivateConstructors_AreNotFlagged()
        => Assert.Empty(NonPrivateConstructors(typeof(FixtureSealed)));

    [Theory(DisplayName = "ISS-005 guard, must-fire: the bare nested-variant name is caught outside the factory")]
    [InlineData("var owner = new ContactCreateOwner.ForeignEnvironment(\"x\");")]
    [InlineData("Func<string, object> f = r => new ForeignEnvironment(r);")]
    public void Control_TheBareVariantName_IsCaught(string statement)
        => Assert.NotEmpty(Violations(OtherFile, statement));

    [Fact(DisplayName = "ISS-005 guard, must-not-fire: IsForeignEnvironment (a different identifier) passes")]
    public void Control_AnIdentifierContainingTheName_IsNotFlagged()
        => Assert.Empty(Violations(OtherFile, "if (owner.IsForeignEnvironment) { }"));

    [Fact(DisplayName = "ISS-005: the ContactIdentityBinder constructor requires a non-null ContactCreateOwner")]
    public void TheBinderConstructor_TakesTheOwnerPolicy_AsARequiredNonNullableParameter()
    {
        var constructor = Assert.Single(typeof(ContactIdentityBinder).GetConstructors());
        var parameter = Assert.Single(constructor.GetParameters(), p => p.ParameterType == typeof(ContactCreateOwner));

        Assert.False(parameter.IsOptional, "the owner policy must have no default: a forgotten argument must not compile");
        Assert.False(parameter.HasDefaultValue);
        Assert.Equal(NullabilityState.NotNull, new NullabilityInfoContext().Create(parameter).WriteState);
    }

    // ── Controls: the rule over synthetic code ───────────────────────────────────────────────────────

    private const string OtherFile = "src/server/api/Sprk.Bff.Api/Infrastructure/DI/ExternalAccessModule.cs";

    private const string BinderFileShape = """
        public sealed class ContactIdentityBinder
        {
            public ContactIdentityBinder(ContactCreateOwner createOwner) { }
        }

        public class ContactIdentityBinderFactory
        {
            public ContactIdentityBinder CreateBinder(string url)
                => new(ContactCreateOwner.NoneForForeignEnvironment($"foreign {url}; see #2270"));
        }
        """;

    [Fact(DisplayName = "ISS-005 guard, must-not-fire: the factory class's own use, in the binder file, passes")]
    public void Control_TheFactorysUse_HasNoViolation()
        => Assert.Empty(Violations(FactoryFile, BinderFileShape));

    [Fact(DisplayName = "ISS-005 guard, must-not-fire: the declaration in ContactCreateOwner.cs passes")]
    public void Control_TheDeclaration_HasNoViolation()
        => Assert.Empty(Violations(DeclaringFile, """
            public abstract class ContactCreateOwner
            {
                public static ContactCreateOwner NoneForForeignEnvironment(string reason) => new(null, null, reason);
            }
            """));

    [Fact(DisplayName = "ISS-005 guard, must-not-fire: the other policy, and comments naming the member, pass anywhere")]
    public void Control_TheOtherPolicy_AndComments_HaveNoViolation()
    {
        var code = SourceScan.CodeText(new[]
        {
            "var owner = ContactCreateOwner.FromCustomerBusinessUnit(options, configuration);",
            "// ContactCreateOwner.NoneForForeignEnvironment(\"x\") is for the factory only",
            "/// <see cref=\"ContactCreateOwner.NoneForForeignEnvironment\"/>",
        });

        Assert.Empty(Violations(OtherFile, code));
    }

    [Theory(DisplayName = "ISS-005 guard, must-fire: a use outside the factory is caught")]
    [InlineData(OtherFile, "services.AddSingleton(sp => new ContactIdentityBinder(a, b, c, d, ContactCreateOwner.NoneForForeignEnvironment(\"x\")));")]
    [InlineData(OtherFile, "Func<string, ContactCreateOwner> none = ContactCreateOwner.NoneForForeignEnvironment;")]
    [InlineData(OtherFile, "var name = nameof(ContactCreateOwner.NoneForForeignEnvironment);")]
    [InlineData("src/server/api/Sprk.Bff.Api/Services/Registration/RegistrationDataverseService.cs",
        "return new ContactIdentityBinder(s, o, t, l, ContactCreateOwner.NoneForForeignEnvironment(\"own\"));")]
    public void Control_AUseOutsideTheFactory_IsCaught(string path, string statement)
        => Assert.NotEmpty(Violations(path, statement));

    [Fact(DisplayName = "ISS-005 guard, must-fire: a use in the binder class, in the factory's own file, is caught")]
    public void Control_AUseInTheBinderClass_InTheFactorysFile_IsCaught()
    {
        var code = BinderFileShape.Replace(
            "public ContactIdentityBinder(ContactCreateOwner createOwner) { }",
            "public ContactIdentityBinder() : this(ContactCreateOwner.NoneForForeignEnvironment(\"lazy\")) { }",
            StringComparison.Ordinal);

        Assert.Single(Violations(FactoryFile, code));
    }

    [Fact(DisplayName = "ISS-005 guard, must-fire: a use in the declaring file other than the declaration is caught")]
    public void Control_AnotherUseInTheDeclaringFile_IsCaught()
        => Assert.Single(Violations(DeclaringFile, """
            public abstract class ContactCreateOwner
            {
                public static ContactCreateOwner NoneForForeignEnvironment(string reason) => new(null, null, reason);
                public static ContactCreateOwner Default() => NoneForForeignEnvironment("default");
            }
            """));

    [Fact(DisplayName = "ISS-005 guard, must-fire: a binder file with no factory class is caught, never vacuously green")]
    public void Control_NoFactoryClass_IsCaught()
        => Assert.Contains("vacuous", Assert.Single(Violations(FactoryFile, "public sealed class ContactIdentityBinder { }")));
}

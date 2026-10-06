using System.Text.RegularExpressions;

namespace Spaarke.ArchTests;

/// <summary>
/// THE SCANNER behind <see cref="RouteAuthorizationGuardTests"/> — source analysis of every BFF route
/// registration, resolved to its full route key and its full authorization chain.
///
/// <para><b>Why this is its own file.</b> The guard has three reasons to change and they change at different
/// rates: the RULES (rarely), the DATA — waivers, ledger, credit lists — (every fix task), and this PARSER
/// (when the codebase invents a new registration shape). Keeping the parser apart means a fix task that
/// deletes a waiver never touches the code that decides what a route key is.</para>
///
/// <para><b>What it resolves, and how it fails.</b> Every <c>Map{Get|Post|Put|Patch|Delete}</c>,
/// <c>MapMethods</c> and <c>MapHealthChecks</c> call is tied to its receiver; the receiver is resolved
/// through <c>MapGroup</c> declarations in the same method, then through METHOD PARAMETERS by following
/// every call site of the enclosing method across the BFF (the extension form <c>group.MapX()</c>, the
/// static form <c>XEndpoints.MapX(group)</c> and the bare in-file form <c>MapX(group)</c>), until it reaches
/// the application root (<c>var app = builder.Build()</c> in <c>Program.cs</c>). The route key is the verb
/// plus every group prefix on the way; the chain is the route's own fluent chain plus every group's.
/// Anything this cannot follow — a path that is not a literal or a same-file const, a receiver that is
/// neither a group nor a parameter, a builder that is assigned or returned, a call site whose argument is
/// an expression, an ambiguous extension, a group bound across files by a file that is not a pinned
/// aggregator — is reported as a PROBLEM, never skipped. That is the fail-closed property ADR-003 asks of
/// the guard's own decisions.</para>
///
/// <para><b>What it deliberately does not see, and how each blind spot is closed.</b> It reads the fluent chain,
/// not runtime metadata. (1) A FILTER inside a wrapper extension with an unrelated name is invisible, and the
/// attachment-form census only classifies calls whose NAME is authorization-shaped — that fails CLOSED (the hidden
/// gate earns no credit, so the route is flagged). (2) An ANONYMITY the chain does not show — an
/// <c>[AllowAnonymous]</c> attribute on a lambda or a handler method, <c>WithMetadata(new
/// AllowAnonymousAttribute())</c>, or an <c>.AllowAnonymous()</c> hidden in a wrapper — would fail OPEN, so
/// <c>AnonymityIsDeclaredOnlyOnAScannedChain</c> refuses every one (task 167 r1). So would an anonymity by
/// OMISSION — no proven sign-in requirement and no <c>.AllowAnonymous()</c> on the effective chain, a route that scans
/// as signed-in — so <c>NoRouteIsAnonymousByOmission</c> refuses it, whatever its waiver (task 167 r2/f1; the runtime
/// FallbackPolicy answers it 401 since owner round 14). A group CONTINUATION statement counts only in the unbroken
/// run right after the group's declaration; anywhere else — a nested or conditional block, after an intervening
/// statement — it is a problem, never credited (task 167 f1); in a method that RECEIVES the group it counts only when
/// every call passing the group in is itself in such a run (task 167 f2). (3) A registration API outside
/// the vocabulary — <c>.Map(...)</c>, <c>MapFallback*</c>, <c>MapHub&lt;T&gt;</c>, <c>MapControllers</c>, or any
/// undeclared <c>Map*</c> call — would put routes beside the census, so
/// <c>NoRouteIsRegisteredInAFormTheScannerCannotRead</c> refuses it (task 167 r1). What remains unseen is request
/// handling that is not endpoint routing at all (terminal middleware via <c>app.Use</c>/<c>app.Run</c>); that is
/// not a route registration and is reviewed as middleware.</para>
/// </summary>
public partial class RouteAuthorizationGuardTests
{
    // =============================================================================================
    // LEXING — comments blanked (Text), and comments + string/char literal contents blanked (Code).
    // Offsets and line structure are preserved in both, so an index in one is an index in the other.
    // =============================================================================================

    private static (string Text, string Code) Lex(string raw)
    {
        var text = raw.ToCharArray();
        var code = raw.ToCharArray();
        var n = raw.Length;
        var i = 0;

        while (i < n)
        {
            var c = raw[i];

            if (c == '/' && i + 1 < n && raw[i + 1] == '/')
            {
                while (i < n && raw[i] != '\n')
                {
                    text[i] = ' ';
                    code[i] = ' ';
                    i++;
                }

                continue;
            }

            if (c == '/' && i + 1 < n && raw[i + 1] == '*')
            {
                var close = raw.IndexOf("*/", i + 2, StringComparison.Ordinal);
                var stop = close < 0 ? n : close + 2;
                for (var j = i; j < stop; j++)
                {
                    if (raw[j] != '\n')
                    {
                        text[j] = ' ';
                        code[j] = ' ';
                    }
                }

                i = stop;
                continue;
            }

            if (c == '"')
            {
                var end = StringLiteralEnd(raw, i);
                for (var j = i + 1; j < end - 1; j++)
                {
                    if (raw[j] != '\n')
                    {
                        code[j] = ' ';
                    }
                }

                i = end;
                continue;
            }

            if (c == '\'')
            {
                var end = CharLiteralEnd(raw, i);
                for (var j = i + 1; j < end - 1; j++)
                {
                    code[j] = ' ';
                }

                i = end;
                continue;
            }

            i++;
        }

        return (new string(text), new string(code));
    }

    /// <summary>Index just past the string literal opening at <paramref name="i"/> (regular, verbatim,
    /// interpolated, raw).</summary>
    private static int StringLiteralEnd(string raw, int i)
    {
        var n = raw.Length;
        var quotes = 0;
        while (i + quotes < n && raw[i + quotes] == '"')
        {
            quotes++;
        }

        if (quotes >= 3)
        {
            var closing = new string('"', quotes);
            var close = raw.IndexOf(closing, i + quotes, StringComparison.Ordinal);
            return close < 0 ? n : close + quotes;
        }

        if (quotes == 2)
        {
            return i + 2;   // the empty string
        }

        var k = i - 1;
        var verbatim = k >= 0 && (raw[k] == '@' || (raw[k] == '$' && k > 0 && raw[k - 1] == '@'));
        var interpolated = k >= 0 && (raw[k] == '$' || (raw[k] == '@' && k > 0 && raw[k - 1] == '$'));
        var j = i + 1;

        while (j < n)
        {
            var ch = raw[j];
            if (!verbatim && ch == '\\')
            {
                j += 2;
                continue;
            }

            if (ch == '"')
            {
                if (verbatim && j + 1 < n && raw[j + 1] == '"')
                {
                    j += 2;
                    continue;
                }

                return j + 1;
            }

            if (interpolated && ch == '{')
            {
                if (j + 1 < n && raw[j + 1] == '{')
                {
                    j += 2;
                    continue;
                }

                j = InterpolationHoleEnd(raw, j + 1);
                continue;
            }

            if (!verbatim && ch == '\n')
            {
                return j;   // unterminated on this line — stop rather than swallow the file
            }

            j++;
        }

        return n;
    }

    private static int InterpolationHoleEnd(string raw, int start)
    {
        var depth = 0;
        var j = start;
        while (j < raw.Length)
        {
            var ch = raw[j];
            if (ch == '"')
            {
                j = StringLiteralEnd(raw, j);
                continue;
            }

            if (ch == '\'')
            {
                j = CharLiteralEnd(raw, j);
                continue;
            }

            if (ch == '{')
            {
                depth++;
            }
            else if (ch == '}')
            {
                if (depth == 0)
                {
                    return j + 1;
                }

                depth--;
            }

            j++;
        }

        return raw.Length;
    }

    private static int CharLiteralEnd(string raw, int i)
    {
        var n = raw.Length;
        var limit = Math.Min(n, i + 12);
        var j = i + 1;
        j += j < n && raw[j] == '\\' ? 2 : 1;
        while (j < limit && raw[j] != '\'')
        {
            j++;
        }

        return j < limit ? j + 1 : i + 1;
    }

    /// <summary>Comments AND literal contents blanked — the text Rule B and the vocabulary checks read, so
    /// neither a doc comment nor a log message can pass as evidence of a decision.</summary>
    private static string CodeOf(string raw) => Lex(raw).Code;

    private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>True when <paramref name="token"/> occurs in <paramref name="code"/> as a WHOLE identifier.
    /// "IAiAuthorizationService" therefore does not contain "AuthorizationService".</summary>
    private static bool ContainsToken(string code, string token)
    {
        var index = 0;
        while ((index = code.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            var before = index == 0 || !IsIdentChar(code[index - 1]);
            var afterIndex = index + token.Length;
            var after = afterIndex >= code.Length || !IsIdentChar(code[afterIndex]);
            if (before && after)
            {
                return true;
            }

            index = afterIndex;
        }

        return false;
    }

    // =============================================================================================
    // SOURCE MODEL
    // =============================================================================================

    private sealed record TypeDecl(string Name, string Kind, bool IsPartial, int BodyStart, int BodyEnd);

    private sealed record ParamDecl(string Name, string Type, bool IsThis);

    private sealed class MethodDecl
    {
        public MethodDecl(SourceUnit unit, string name, string? declaringType, IReadOnlyList<ParamDecl> parameters,
            int nameIndex, int bodyStart, int bodyEnd)
        {
            Unit = unit;
            Name = name;
            DeclaringType = declaringType;
            Params = parameters;
            NameIndex = nameIndex;
            BodyStart = bodyStart;
            BodyEnd = bodyEnd;
        }

        public SourceUnit Unit { get; }
        public string Name { get; }
        public string? DeclaringType { get; }
        public IReadOnlyList<ParamDecl> Params { get; }
        public int NameIndex { get; }
        public int BodyStart { get; }
        public int BodyEnd { get; }
        public bool IsExtension => Params.Count > 0 && Params[0].IsThis;
        public string Body => Unit.Code[BodyStart..BodyEnd];
    }

    private sealed class SourceUnit
    {
        private readonly int[] _lineStarts;

        public SourceUnit(string path, string raw)
        {
            Path = path;
            (Text, Code) = Lex(raw);
            var starts = new List<int> { 0 };
            for (var i = 0; i < Text.Length; i++)
            {
                if (Text[i] == '\n')
                {
                    starts.Add(i + 1);
                }
            }

            _lineStarts = starts.ToArray();
            Types = ParseTypes(Code);
            Methods = ParseMethods(this);
        }

        public string Path { get; }
        public string Text { get; }
        public string Code { get; }
        public IReadOnlyList<TypeDecl> Types { get; }
        public IReadOnlyList<MethodDecl> Methods { get; }

        public int LineOf(int index)
        {
            var at = Array.BinarySearch(_lineStarts, index);
            return (at >= 0 ? at : ~at - 1) + 1;
        }

        public MethodDecl? MethodAt(int index)
            => Methods.Where(m => m.BodyStart <= index && index < m.BodyEnd)
                .OrderBy(m => m.BodyEnd - m.BodyStart)
                .FirstOrDefault();

        public TypeDecl? TypeAt(int index)
            => Types.Where(t => t.BodyStart <= index && index < t.BodyEnd)
                .OrderBy(t => t.BodyEnd - t.BodyStart)
                .FirstOrDefault();
    }

    private static readonly Regex TypeHeader = new(
        @"(?<![\w.])(?<mods>(?:(?:public|private|internal|protected|static|sealed|abstract|partial|readonly|ref|file|unsafe|new)\s+)*)"
        + @"(?<kind>class|interface|struct|enum|record(?:\s+(?:class|struct))?)\s+(?<name>[A-Za-z_]\w*)",
        RegexOptions.Compiled);

    // The return type's generic argument list may hold ONE level of tuple parentheses — `Task<(EventEntity[] Items, int
    // TotalCount)>` (task 167 integration, 2026-10-05: DataverseWebApiService.QueryEventsAsCallerAsync, the hop task 159's
    // list HandlerDecision follows, was invisible to the parser, so the hop failed as "declares no method"). Anything deeper
    // still does not parse as a method header — the same fail-closed outcome as before for that shape.
    private static readonly Regex MethodHeader = new(
        @"(?<![\w.])(?:(?:public|private|internal|protected|static|async|override|sealed|virtual|unsafe|new|extern|partial|readonly)\s+)+"
        + @"(?:[A-Za-z_][\w.]*(?:\s*<(?:[^;{}()]|\([^;{}()]*\))*?>)?(?:\s*\[\s*\])*\??|\([^;{}]*?\))\s+"
        + @"(?<name>[A-Za-z_]\w*)\s*(?:<[^;{}()]*?>)?\s*\(",
        RegexOptions.Compiled);

    private static IReadOnlyList<TypeDecl> ParseTypes(string code)
    {
        var types = new List<TypeDecl>();
        foreach (Match m in TypeHeader.Matches(code))
        {
            var i = m.Index + m.Length;
            var parens = 0;
            while (i < code.Length)
            {
                var c = code[i];
                if (c == '(')
                {
                    parens++;
                }
                else if (c == ')')
                {
                    parens--;
                }
                else if (parens == 0 && (c == '{' || c == ';'))
                {
                    break;
                }

                i++;
            }

            if (i >= code.Length || code[i] != '{')
            {
                continue;   // a body-less record, or not a declaration at all
            }

            var close = MatchClose(code, i);
            if (close < 0)
            {
                continue;
            }

            var kind = Regex.Replace(m.Groups["kind"].Value, @"\s+", " ");
            types.Add(new TypeDecl(m.Groups["name"].Value, kind, m.Groups["mods"].Value.Contains("partial"), i, close + 1));
        }

        return types;
    }

    private static IReadOnlyList<MethodDecl> ParseMethods(SourceUnit unit)
    {
        var code = unit.Code;
        var methods = new List<MethodDecl>();

        foreach (Match m in MethodHeader.Matches(code))
        {
            var open = m.Index + m.Length - 1;
            var close = MatchClose(code, open);
            if (close < 0)
            {
                continue;
            }

            var k = SkipWs(code, close + 1);
            if (k + 5 <= code.Length && code.AsSpan(k, 5).SequenceEqual("where") && (k + 5 == code.Length || !IsIdentChar(code[k + 5])))
            {
                while (k < code.Length && code[k] != '{' && !(code[k] == '=' && k + 1 < code.Length && code[k + 1] == '>') && code[k] != ';')
                {
                    k++;
                }
            }

            int bodyStart;
            int bodyEnd;
            if (k < code.Length && code[k] == '{')
            {
                var bodyClose = MatchClose(code, k);
                if (bodyClose < 0)
                {
                    continue;
                }

                bodyStart = k;
                bodyEnd = bodyClose + 1;
            }
            else if (k + 1 < code.Length && code[k] == '=' && code[k + 1] == '>')
            {
                var end = StatementEnd(code, k + 2);
                if (end < 0)
                {
                    continue;
                }

                bodyStart = k;
                bodyEnd = end + 1;
            }
            else
            {
                continue;   // abstract / interface member, or not a method
            }

            var name = m.Groups["name"].Value;
            var nameIndex = m.Groups["name"].Index;
            var parameters = ParseParams(code[(open + 1)..close]);
            methods.Add(new MethodDecl(unit, name, null, parameters, nameIndex, bodyStart, bodyEnd));
        }

        // Declaring type is the innermost type containing the method name.
        return methods
            .Select(md => new MethodDecl(md.Unit, md.Name,
                unit.Types.Where(t => t.BodyStart <= md.NameIndex && md.NameIndex < t.BodyEnd)
                    .OrderBy(t => t.BodyEnd - t.BodyStart).FirstOrDefault()?.Name,
                md.Params, md.NameIndex, md.BodyStart, md.BodyEnd))
            .ToList();
    }

    private static IReadOnlyList<ParamDecl> ParseParams(string text)
    {
        var result = new List<ParamDecl>();
        foreach (var (start, end) in SplitTopLevel(text, 0, text.Length))
        {
            var part = Regex.Replace(text[start..end], @"\[[^\]]*\]", " ").Trim();
            if (part.Length == 0)
            {
                continue;
            }

            var eq = IndexOfTopLevel(part, '=');
            if (eq >= 0)
            {
                part = part[..eq].Trim();
            }

            var isThis = Regex.IsMatch(part, @"^this\s");
            var nameMatch = Regex.Match(part, @"([A-Za-z_]\w*)\s*$");
            if (!nameMatch.Success)
            {
                continue;
            }

            var type = Regex.Replace(part[..nameMatch.Index], @"^(?:(?:this|ref|out|in|params|scoped)\s+)+", string.Empty).Trim();
            result.Add(new ParamDecl(nameMatch.Groups[1].Value, type, isThis));
        }

        return result;
    }

    // ---------------- structural helpers (all run over Code, where literals cannot confuse them) ----------------

    private static int SkipWs(string code, int i)
    {
        while (i < code.Length && char.IsWhiteSpace(code[i]))
        {
            i++;
        }

        return i;
    }

    /// <summary>Index of the bracket closing the one at <paramref name="open"/> ((, [ or {), or -1.</summary>
    private static int MatchClose(string code, int open)
    {
        var depth = 0;
        for (var i = open; i < code.Length; i++)
        {
            var c = code[i];
            if (c is '(' or '[' or '{')
            {
                depth++;
            }
            else if (c is ')' or ']' or '}')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }

                if (depth < 0)
                {
                    return -1;
                }
            }
        }

        return -1;
    }

    private static int MatchAngle(string code, int open)
    {
        var depth = 0;
        for (var i = open; i < code.Length; i++)
        {
            if (code[i] == '<')
            {
                depth++;
            }
            else if (code[i] == '>')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
            else if (code[i] is ';' or '{' or '}')
            {
                return -1;
            }
        }

        return -1;
    }

    /// <summary>Index of the <c>;</c> ending the statement that begins at <paramref name="start"/>, at bracket
    /// depth zero, or -1.</summary>
    private static int StatementEnd(string code, int start)
    {
        var depth = 0;
        for (var i = start; i < code.Length; i++)
        {
            var c = code[i];
            if (c is '(' or '[' or '{')
            {
                depth++;
            }
            else if (c is ')' or ']' or '}')
            {
                depth--;
                if (depth < 0)
                {
                    return -1;
                }
            }
            else if (c == ';' && depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Top-level comma-separated spans of <c>text[start..end]</c>.</summary>
    private static List<(int Start, int End)> SplitTopLevel(string text, int start, int end)
    {
        var spans = new List<(int, int)>();
        var depth = 0;
        var from = start;
        for (var i = start; i < end; i++)
        {
            var c = text[i];
            if (c is '(' or '[' or '{' or '<')
            {
                depth++;
            }
            else if (c is ')' or ']' or '}' or '>')
            {
                // '>' of a lambda arrow is not a bracket.
                if (c == '>' && i > 0 && text[i - 1] == '=')
                {
                    continue;
                }

                depth--;
            }
            else if (c == ',' && depth == 0)
            {
                spans.Add((from, i));
                from = i + 1;
            }
        }

        if (end > from && text[from..end].Trim().Length > 0)
        {
            spans.Add((from, end));
        }

        return spans;
    }

    private static int IndexOfTopLevel(string text, char target)
    {
        var depth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '(' or '[' or '{' or '<')
            {
                depth++;
            }
            else if (c is ')' or ']' or '}' or '>')
            {
                depth--;
            }
            else if (c == target && depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    private static string Squash(string s) => Regex.Replace(s.Trim(), @"\s+", " ");

    /// <summary>The identifier ending just before <paramref name="index"/> (whitespace skipped), with its start
    /// index; empty when the preceding token is not an identifier.</summary>
    private static (string Name, int Start) IdentifierBefore(string code, int index)
    {
        var end = index;
        while (end > 0 && char.IsWhiteSpace(code[end - 1]))
        {
            end--;
        }

        var start = end;
        while (start > 0 && IsIdentChar(code[start - 1]))
        {
            start--;
        }

        return (code[start..end], start);
    }

    /// <summary>True when <paramref name="index"/> begins a statement: the previous significant character is
    /// <c>;</c>, <c>{</c>, <c>}</c>, or there is none.</summary>
    private static bool AtStatementStart(string code, int index)
    {
        var p = index - 1;
        while (p >= 0 && char.IsWhiteSpace(code[p]))
        {
            p--;
        }

        return p < 0 || code[p] is ';' or '{' or '}';
    }

    /// <summary>Words that start a statement which can branch, loop, jump or declare — never part of an UNCONDITIONAL run
    /// (task 167 f2). A run is the sequence of plain call/assignment statements that opens a block.</summary>
    private static readonly IReadOnlySet<string> StatementKeywords = new HashSet<string>(StringComparer.Ordinal)
    {
        "if", "else", "for", "foreach", "while", "do", "switch", "case", "default", "try", "catch", "finally",
        "return", "throw", "goto", "break", "continue", "yield", "using", "lock", "fixed", "unsafe", "checked",
        "unchecked", "await", "var", "new", "static", "const",
    };

    /// <summary>
    /// The UNCONDITIONAL opening run of the block whose <c>{</c> is at <paramref name="open"/>: the consecutive statements
    /// from the brace on that each start with an identifier which is not a <see cref="StatementKeywords"/> word and end at
    /// a depth-zero <c>;</c> inside the block — plain calls and assignments. The run stops at the first statement that is
    /// anything else (an <c>if</c>, a loop, a <c>return</c>, a nested block, a declaration), so every statement in it runs
    /// whenever the block is entered (task 167 f2).
    /// </summary>
    private static List<(int Start, int End)> PlainStatementRun(string code, int open)
    {
        var run = new List<(int Start, int End)>();
        var close = MatchClose(code, open);
        if (close < 0)
        {
            return run;
        }

        var s = open + 1;
        while (true)
        {
            s = SkipWs(code, s);
            if (s >= close || !(char.IsLetter(code[s]) || code[s] == '_'))
            {
                return run;
            }

            var wordEnd = s;
            while (wordEnd < code.Length && IsIdentChar(code[wordEnd]))
            {
                wordEnd++;
            }

            if (StatementKeywords.Contains(code[s..wordEnd]))
            {
                return run;
            }

            var end = StatementEnd(code, s);
            if (end < 0 || end >= close)
            {
                return run;
            }

            run.Add((s, end));
            s = end + 1;
        }
    }

    // =============================================================================================
    // CHAINS — the fluent calls after a registration or a MapGroup declaration
    // =============================================================================================

    /// <summary>One call in a fluent chain, e.g. <c>.AddDocumentAuthorizationFilter("read")</c>.</summary>
    private sealed record ChainCall(string Name, string? Generic, string Args, string File, int Line)
    {
        public string Form => NormalizeForm(this);

        public bool IsAuthorizationShaped => AuthorizationShapedName.IsMatch(Name);
    }

    /// <summary>
    /// "Authorization-shaped" per the task-167 constraint — any <c>.Add*Filter</c> (including
    /// <c>.AddEndpointFilter</c>), any <c>.Require*</c>, <c>.WithMetadata</c> (which can carry an authorize
    /// attribute), and anything whose name mentions authorization or anonymity. Deliberately WIDER than the
    /// constraint's four shapes: an unknown <c>.RequireFoo()</c> must be classified before it can merge.
    /// </summary>
    private static readonly Regex AuthorizationShapedName = new(
        @"^(?:Add\w*Filter\w*|Require\w+|WithMetadata|\w*Authoriz\w*|\w*Anonymous\w*)$",
        RegexOptions.Compiled);

    /// <summary>Filters whose ARGUMENT changes what they decide, so the argument is part of the form.</summary>
    private static readonly IReadOnlySet<string> ArgumentDiscriminatedForms =
        new HashSet<string>(StringComparer.Ordinal) { "AddDataverseAuthorizationFilter" };

    private static string NormalizeForm(ChainCall call)
    {
        var args = Squash(call.Args);

        if (call.Name == "AddEndpointFilter")
        {
            if (call.Generic is not null)
            {
                return $"AddEndpointFilter<{Squash(call.Generic)}>";
            }

            return Regex.IsMatch(args, @"^(?:static\s+)?(?:async\s+)?(?:\([^)]*\)|[A-Za-z_]\w*)\s*=>")
                ? "AddEndpointFilter(lambda)"
                : $"AddEndpointFilter({args})";
        }

        if (call.Name is "RequireAuthorization")
        {
            return $"RequireAuthorization({args})";
        }

        if (ArgumentDiscriminatedForms.Contains(call.Name))
        {
            var first = SplitTopLevel(args, 0, args.Length).Select(s => args[s.Start..s.End].Trim()).FirstOrDefault() ?? string.Empty;
            return $"{call.Name}({first})";
        }

        if (call.Name == "WithMetadata")
        {
            var ctor = Regex.Match(args, @"^new\s+([A-Za-z_][\w.]*)\s*\(");
            return ctor.Success ? $"WithMetadata(new {ctor.Groups[1].Value})" : $"WithMetadata({args})";
        }

        return call.Generic is not null ? $"{call.Name}<{Squash(call.Generic)}>" : call.Name;
    }

    private static (List<ChainCall> Calls, int End, string? Problem) ParseChain(SourceUnit unit, int start)
    {
        var code = unit.Code;
        var calls = new List<ChainCall>();
        var i = start;

        while (true)
        {
            i = SkipWs(code, i);
            if (i >= code.Length || code[i] != '.')
            {
                return (calls, i, null);
            }

            var j = SkipWs(code, i + 1);
            var nameStart = j;
            while (j < code.Length && IsIdentChar(code[j]))
            {
                j++;
            }

            if (j == nameStart)
            {
                return (calls, i, "a chain member is not an identifier");
            }

            var name = code[nameStart..j];
            j = SkipWs(code, j);
            string? generic = null;
            if (j < code.Length && code[j] == '<')
            {
                var closeAngle = MatchAngle(code, j);
                if (closeAngle < 0)
                {
                    return (calls, j, $"chain member '{name}' has an unreadable generic argument");
                }

                generic = unit.Text[(j + 1)..closeAngle];
                j = SkipWs(code, closeAngle + 1);
            }

            if (j >= code.Length || code[j] != '(')
            {
                return (calls, j, $"chain member '{name}' is not a call");
            }

            var close = MatchClose(code, j);
            if (close < 0)
            {
                return (calls, j, $"chain member '{name}' has no closing parenthesis");
            }

            calls.Add(new ChainCall(name, generic, unit.Text[(j + 1)..close], unit.Path, unit.LineOf(nameStart)));
            i = close + 1;
        }
    }

    // =============================================================================================
    // REGISTRATION SITES, GROUPS, CONTINUATIONS — per file
    // =============================================================================================

    /// <summary>The registration vocabulary. The census (<see cref="EndpointFiles"/>) and the scanner use the
    /// SAME pattern, so a file registering only <c>MapMethods</c> or <c>MapHealthChecks</c> is counted.</summary>
    private static readonly Regex RegistrationCall = new(
        @"\.\s*Map(?<verb>Get|Post|Put|Patch|Delete|Methods|HealthChecks)\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex GroupDeclaration = new(
        @"(?<![\w.])var\s+(?<var>[A-Za-z_]\w*)\s*=\s*(?<recv>[A-Za-z_]\w*)\s*\.\s*MapGroup\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex AnyMapGroup = new(@"\.\s*MapGroup\s*\(", RegexOptions.Compiled);

    private static readonly Regex MemberCall = new(
        @"(?<![\w.])(?<recv>[A-Za-z_]\w*)\s*\.\s*(?<name>[A-Za-z_]\w*)\s*(?:<[^;(){}]*>)?\s*\(",
        RegexOptions.Compiled);

    private sealed record RegistrationSite(
        SourceUnit Unit, int Index, int Line, string Receiver, IReadOnlyList<string> Verbs, string? Path,
        IReadOnlyList<ChainCall> OwnChain, int HandlerStart, int HandlerEnd, string? Problem);

    private sealed class GroupNode
    {
        public GroupNode(SourceUnit unit, MethodDecl? method, string variable, string parentReceiver, int index,
            string? segment, string? problem, List<ChainCall> chain, int statementEnd)
        {
            Unit = unit;
            Method = method;
            Variable = variable;
            ParentReceiver = parentReceiver;
            Index = index;
            Segment = segment;
            Problem = problem;
            Chain = chain;
            StatementEnd = statementEnd;
        }

        public SourceUnit Unit { get; }
        public MethodDecl? Method { get; }
        public string Variable { get; }
        public string ParentReceiver { get; }
        public int Index { get; }
        public string? Segment { get; }
        public string? Problem { get; }

        /// <summary>The index of the <c>;</c> that ends the declaration statement, or -1 when the statement could
        /// not be read (then <see cref="Problem"/> is set). Continuation statements are credited only in the
        /// unbroken run that starts right after it (task 167 f1).</summary>
        public int StatementEnd { get; }

        /// <summary>The declaration's own chain PLUS any continuation statements (<c>group.AddX();</c>).</summary>
        public List<ChainCall> Chain { get; }
    }

    private static (string? Value, string? Problem) ResolvePathArgument(SourceUnit unit, int start, int end)
    {
        var raw = unit.Text[start..end].Trim();

        // A plain or verbatim literal. Interpolated ($"...") and concatenated paths are deliberately NOT
        // read: their value is decided at runtime, so the key would be a guess.
        var literal = Regex.Match(raw, "^(@?)\"((?:[^\"\\\\]|\"\")*)\"$");
        if (literal.Success)
        {
            var value = literal.Groups[2].Value;
            return (literal.Groups[1].Value == "@" ? value.Replace("\"\"", "\"") : value, null);
        }

        var name = Regex.Match(raw, @"^(?:[A-Za-z_]\w*\.)*(?<name>[A-Za-z_]\w*)$");
        if (name.Success)
        {
            var constant = Regex.Match(unit.Text,
                @"(?:const\s+string|static\s+readonly\s+string)\s+" + Regex.Escape(name.Groups["name"].Value)
                + "\\s*=\\s*\"([^\"]*)\"\\s*;");
            return constant.Success
                ? (constant.Groups[1].Value, null)
                : (null, $"the path '{raw}' is a name that does not resolve to a const string in this file");
        }

        return (null, $"the path '{Squash(raw)}' is not a string literal or a same-file const");
    }

    private static IReadOnlyList<string>? ParseVerbArray(string raw)
    {
        var s = Squash(raw);
        var items = Regex.Match(s,
            "^(?:\\[\\s*(?<items>[^\\]]*)\\]|new\\s*\\[\\s*\\]\\s*\\{\\s*(?<items>[^}]*)\\}|new\\s+string\\s*\\[\\s*\\]\\s*\\{\\s*(?<items>[^}]*)\\})$");
        if (!items.Success)
        {
            return null;
        }

        var verbs = new List<string>();
        foreach (var part in items.Groups["items"].Value.Split(','))
        {
            var verb = Regex.Match(part.Trim(), "^\"([A-Za-z]+)\"$");
            if (!verb.Success)
            {
                return null;
            }

            verbs.Add(verb.Groups[1].Value.ToUpperInvariant());
        }

        return verbs.Count == 0 ? null : verbs;
    }

    private static List<RegistrationSite> RegistrationSitesIn(SourceUnit unit)
    {
        var sites = new List<RegistrationSite>();
        var code = unit.Code;

        foreach (Match m in RegistrationCall.Matches(code))
        {
            var line = unit.LineOf(m.Index);
            var verbWord = m.Groups["verb"].Value;
            var (receiver, receiverStart) = IdentifierBefore(code, m.Index);
            var open = m.Index + m.Length - 1;

            RegistrationSite Fail(string problem)
                => new(unit, m.Index, line, receiver, new[] { verbWord.ToUpperInvariant() }, null,
                    Array.Empty<ChainCall>(), 0, 0, problem);

            if (receiver.Length == 0)
            {
                sites.Add(Fail("the receiver of the registration is an expression, not a variable"));
                continue;
            }

            var beforeReceiver = receiverStart - 1;
            while (beforeReceiver >= 0 && char.IsWhiteSpace(code[beforeReceiver]))
            {
                beforeReceiver--;
            }

            if (beforeReceiver >= 0 && code[beforeReceiver] == '.')
            {
                sites.Add(Fail("the receiver of the registration is a member access, not a variable"));
                continue;
            }

            if (!AtStatementStart(code, receiverStart))
            {
                sites.Add(Fail("the route builder is assigned, returned or passed on, so its chain could continue "
                               + "where this scanner cannot see it"));
                continue;
            }

            var close = MatchClose(code, open);
            if (close < 0)
            {
                sites.Add(Fail("the registration call has no closing parenthesis"));
                continue;
            }

            var args = SplitTopLevel(code, open + 1, close);
            if (args.Count == 0)
            {
                sites.Add(Fail("the registration call has no arguments"));
                continue;
            }

            var (path, pathProblem) = ResolvePathArgument(unit, args[0].Start, args[0].End);
            if (path is null)
            {
                sites.Add(Fail(pathProblem!));
                continue;
            }

            IReadOnlyList<string> verbs;
            var handlerIndex = 1;
            if (verbWord == "Methods")
            {
                if (args.Count < 3)
                {
                    sites.Add(Fail("MapMethods needs a path, a verb array and a handler"));
                    continue;
                }

                var parsed = ParseVerbArray(unit.Text[args[1].Start..args[1].End]);
                if (parsed is null)
                {
                    sites.Add(Fail("MapMethods' verbs are not in a readable form ([\"PATCH\"], new[] { \"PATCH\" } "
                                   + "or new string[] { ... })"));
                    continue;
                }

                verbs = parsed;
                handlerIndex = 2;
            }
            else if (verbWord == "HealthChecks")
            {
                verbs = new[] { "GET" };
                handlerIndex = -1;
            }
            else
            {
                verbs = new[] { verbWord.ToUpperInvariant() };
            }

            var handlerStart = 0;
            var handlerEnd = 0;
            if (handlerIndex >= 0)
            {
                if (args.Count <= handlerIndex)
                {
                    sites.Add(Fail("the registration call has no handler argument"));
                    continue;
                }

                handlerStart = args[handlerIndex].Start;
                handlerEnd = args[handlerIndex].End;
            }

            var (chain, chainEnd, chainProblem) = ParseChain(unit, close + 1);
            if (chainProblem is null && (chainEnd >= code.Length || code[chainEnd] != ';'))
            {
                chainProblem = "the registration statement does not end where its chain ends";
            }

            sites.Add(new RegistrationSite(unit, m.Index, line, receiver, verbs, path, chain, handlerStart, handlerEnd,
                chainProblem));
        }

        return sites;
    }

    private static (List<GroupNode> Groups, List<string> Problems) GroupsIn(SourceUnit unit)
    {
        var groups = new List<GroupNode>();
        var problems = new List<string>();
        var code = unit.Code;
        var declared = new HashSet<int>();

        foreach (Match m in GroupDeclaration.Matches(code))
        {
            var mapGroupDot = code.LastIndexOf(".", m.Index + m.Length - 1, StringComparison.Ordinal);
            declared.Add(mapGroupDot);

            var open = m.Index + m.Length - 1;
            var close = MatchClose(code, open);
            var method = unit.MethodAt(m.Index);
            string? segment = null;
            string? problem = null;
            var chain = new List<ChainCall>();
            var statementEnd = -1;

            if (close < 0)
            {
                problem = "the MapGroup call has no closing parenthesis";
            }
            else
            {
                var args = SplitTopLevel(code, open + 1, close);
                if (args.Count != 1)
                {
                    problem = "MapGroup takes exactly one argument here";
                }
                else
                {
                    (segment, problem) = ResolvePathArgument(unit, args[0].Start, args[0].End);
                }

                var (calls, end, chainProblem) = ParseChain(unit, close + 1);
                chain.AddRange(calls);
                if (problem is null && chainProblem is not null)
                {
                    problem = chainProblem;
                }
                else if (problem is null && (end >= code.Length || code[end] != ';'))
                {
                    problem = "the MapGroup statement does not end where its chain ends";
                }

                if (end < code.Length && code[end] == ';')
                {
                    statementEnd = end;
                }
            }

            groups.Add(new GroupNode(unit, method, m.Groups["var"].Value, m.Groups["recv"].Value, m.Index, segment,
                problem, chain, statementEnd));
        }

        foreach (Match m in AnyMapGroup.Matches(code))
        {
            if (!declared.Contains(m.Index))
            {
                problems.Add($"{unit.Path}:{unit.LineOf(m.Index)}: a MapGroup that is not written "
                             + "`var x = y.MapGroup(\"...\")...;` — its prefix and chain cannot be followed");
            }
        }

        return (groups, problems);
    }

    // =============================================================================================
    // THE SOURCE SET — the universe call sites are searched in
    // =============================================================================================

    private sealed record CallSite(
        SourceUnit Unit, MethodDecl? Caller, int Index, bool ExtensionForm, string? Receiver,
        IReadOnlyList<(string? Name, string Text)> Args, string? Problem);

    private sealed record BindingContext(IReadOnlyList<GroupNode> Nodes, string? Aggregator, string? Problem);

    private sealed class SourceSet
    {
        private readonly Dictionary<string, SourceUnit> _byPath;
        private readonly Dictionary<MethodDecl, List<CallSite>> _callSites = new();
        private readonly Dictionary<string, List<MethodDecl>> _extensionsByName;

        public SourceSet(IEnumerable<SourceUnit> units, IReadOnlySet<string> aggregators)
        {
            Units = units.ToList();
            _byPath = Units.ToDictionary(u => u.Path, StringComparer.Ordinal);
            Aggregators = aggregators;
            _extensionsByName = Units.SelectMany(u => u.Methods).Where(m => m.IsExtension)
                .GroupBy(m => m.Name, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

            Groups = new List<GroupNode>();
            Problems = new List<string>();
            foreach (var unit in Units)
            {
                if (!unit.Code.Contains("MapGroup", StringComparison.Ordinal))
                {
                    continue;
                }

                var (groups, problems) = GroupsIn(unit);
                Groups.AddRange(groups);
                Problems.AddRange(problems);
            }

            AttachContinuations();
        }

        public IReadOnlyList<SourceUnit> Units { get; }
        public IReadOnlySet<string> Aggregators { get; }
        public List<GroupNode> Groups { get; }
        public List<string> Problems { get; }

        /// <summary>Every <c>identifier(</c> occurrence in the set, keyed by identifier — built in ONE pass so a
        /// call-site search does not rescan 20 MB of source per method.</summary>
        private Lazy<Dictionary<string, List<(SourceUnit, int)>>> CallIndex => _callIndex ??= new(() =>
        {
            var index = new Dictionary<string, List<(SourceUnit, int)>>(StringComparer.Ordinal);
            foreach (var unit in Units)
            {
                var code = unit.Code;
                var i = 0;
                while (i < code.Length)
                {
                    if (!(char.IsLetter(code[i]) || code[i] == '_') || (i > 0 && IsIdentChar(code[i - 1])))
                    {
                        i++;
                        continue;
                    }

                    var start = i;
                    while (i < code.Length && IsIdentChar(code[i]))
                    {
                        i++;
                    }

                    var next = SkipWs(code, i);
                    if (next < code.Length && code[next] == '(')
                    {
                        var name = code[start..i];
                        if (!index.TryGetValue(name, out var list))
                        {
                            index[name] = list = new List<(SourceUnit, int)>();
                        }

                        list.Add((unit, start));
                    }
                }
            }

            return index;
        });

        private Lazy<Dictionary<string, List<(SourceUnit, int)>>>? _callIndex;

        public SourceUnit? Get(string path) => _byPath.GetValueOrDefault(path);

        public GroupNode? GroupIn(SourceUnit unit, MethodDecl? method, string variable)
            => Groups.FirstOrDefault(g => ReferenceEquals(g.Unit, unit) && ReferenceEquals(g.Method, method)
                                          && g.Variable == variable);

        /// <summary>
        /// Statements such as <c>group.RequireAuthorization();</c> that add to a group AFTER its declaration.
        /// None exist today; reading them anyway is what keeps "the group's chain" honest if one is written.
        ///
        /// <para><b>Only an UNCONDITIONAL continuation is credited</b> (task 167 f1, closing the r2 verifier's
        /// residual). A statement counts as part of the group's chain only when it sits in the unbroken run of
        /// <c>group.…;</c> statements that starts IMMEDIATELY after the group's declaration statement — or, for a
        /// group the method RECEIVES as a parameter, immediately after the method body's opening brace. Nothing but
        /// whitespace and comments may separate the statements of that run, so no <c>if</c>, <c>else</c>, loop,
        /// <c>switch</c>, lambda, <c>try</c>, <c>return</c>, <c>throw</c>, <c>break</c>, <c>continue</c> or
        /// <c>goto</c> can stand between the group and the call: the call runs whenever the group exists. Before
        /// f1, <c>AtStatementStart</c> treated a <c>{</c> as a statement start, so
        /// <c>if (cond) { group.RequireAuthorization(); }</c> was credited as if unconditional — a FAIL-OPEN for
        /// the sign-in rule and for any credited filter.</para>
        ///
        /// <para>Every other authorization-shaped call on a group variable or parameter — a nested block, a
        /// brace-less <c>if (cond) group.X();</c>, <c>var y = group.X();</c>, <c>return group.X();</c>, a call
        /// after an intervening statement — is a PROBLEM, never silently ignored: the remedy is to declare it on the
        /// <c>MapGroup(...)</c> chain (or directly after the declaration).</para>
        ///
        /// <para><b>A received group's run is only as unconditional as the CALLS that pass the group in</b> (task 167
        /// f2, the f1 verifier's item 4). The run that opens a method with a <c>RouteGroupBuilder</c> parameter is
        /// unconditional WITHIN that method; f1 then attached it to every caller's group without asking whether the call
        /// itself ran — so <c>if (cond) SecureRead(docs);</c> credited <c>SecureRead</c>'s
        /// <c>g.AddDocumentAuthorizationFilter("read")</c> to every route of <c>docs</c>, a filter that never runs. Now
        /// such a continuation is credited only when EVERY call site binding the group sits in an unconditional run
        /// itself — directly after the group's declaration, or (recursively) in the opening run of a method that
        /// received it. Otherwise it is a problem and credits nothing.</para>
        /// </summary>
        private void AttachContinuations()
        {
            foreach (var unit in Units)
            {
                var runs = RunsOf(unit);

                foreach (Match m in MemberCall.Matches(unit.Code))
                {
                    var receiver = m.Groups["recv"].Value;
                    var name = m.Groups["name"].Value;
                    if (name.StartsWith("Map", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var method = unit.MethodAt(m.Index);
                    var isGroupVariable = GroupIn(unit, method, receiver) is not null;
                    var isGroupParameter = IsGroupParameter(method, receiver);
                    if (!isGroupVariable && !isGroupParameter)
                    {
                        continue;
                    }

                    var dot = unit.Code.IndexOf('.', m.Groups["recv"].Index + receiver.Length);
                    var (calls, _, problem) = ParseChain(unit, dot);
                    var shaped = calls.Where(c => c.IsAuthorizationShaped).ToList();
                    if (shaped.Count == 0 && problem is null)
                    {
                        continue;
                    }

                    if (problem is not null)
                    {
                        Problems.Add($"{unit.Path}:{unit.LineOf(m.Index)}: a statement continuing group '{receiver}' "
                                     + $"cannot be read ({problem})");
                        continue;
                    }

                    var run = runs.FirstOrDefault(r => r.Start == m.Index && r.Receiver == receiver);
                    if (run is null)
                    {
                        Problems.Add($"{unit.Path}:{unit.LineOf(m.Index)}: '{receiver}.{string.Join("().", shaped.Select(c => c.Name))}()' "
                                     + $"adds to group '{receiver}' outside the unbroken run of statements that directly follows "
                                     + "the group's declaration (or opens the method that receives it), so it may be "
                                     + "conditional or skipped — an if/else, a loop, a lambda, an early return. It is NOT "
                                     + "credited. Declare it on the MapGroup(...) chain, or as a statement directly after it.");
                        continue;
                    }

                    // A run that opens a method RECEIVING the group is unconditional only within that method. Whether it
                    // runs at all depends on the CALL that passes the group in (task 167 f2, the f1 verifier's item 4):
                    // `if (cond) SecureRead(docs);` would otherwise lend SecureRead's first statements to every route of
                    // 'docs'. So every call site that binds the group must itself sit in an unconditional run.
                    if (run.ReceivedBy is not null)
                    {
                        var siteProblems = new List<string>();
                        CollectConditionalBindings(run.ReceivedBy, receiver, 0, new HashSet<(MethodDecl, string)>(), siteProblems);
                        if (siteProblems.Count > 0)
                        {
                            Problems.Add($"{unit.Path}:{unit.LineOf(m.Index)}: '{receiver}.{string.Join("().", shaped.Select(c => c.Name))}()' "
                                         + $"opens {run.ReceivedBy.Name}, which RECEIVES the group, but not every call that passes the "
                                         + "group in runs unconditionally, so it is NOT credited to any caller's routes: "
                                         + string.Join("; ", siteProblems)
                                         + ". Make each such call a statement directly after the group's declaration (or the first "
                                         + "statements of a method that itself receives the group), or declare the call on the MapGroup(...) chain.");
                            continue;
                        }
                    }

                    foreach (var context in Resolve(unit, method, receiver, 0, m.Index))
                    {
                        if (context.Problem is not null || context.Nodes.Count == 0)
                        {
                            Problems.Add($"{unit.Path}:{unit.LineOf(m.Index)}: a statement continuing group "
                                         + $"'{receiver}' does not resolve to a group ({context.Problem ?? "it is the root"})");
                            continue;
                        }

                        context.Nodes[^1].Chain.AddRange(shaped);
                    }
                }
            }
        }

        private static bool IsGroupParameter(MethodDecl? method, string identifier)
            => method?.Params.Any(p => p.Name == identifier && p.Type.Contains("RouteGroupBuilder", StringComparison.Ordinal)) == true;

        /// <summary>
        /// One statement of an UNCONDITIONAL run on a group: <paramref name="Start"/> is the statement's first token,
        /// <paramref name="CallNameIndex"/> the name of the call it makes on (or with) the group — the index a
        /// <see cref="CallSite"/> records — and <paramref name="ReceivedBy"/> the method whose PARAMETER the run walks
        /// (null for a run that follows a group's declaration).
        /// </summary>
        private sealed record RunStatement(int Start, int CallNameIndex, string Receiver, MethodDecl? ReceivedBy);

        private readonly Dictionary<SourceUnit, List<RunStatement>> _runs = new();

        /// <summary>
        /// Every statement in an UNCONDITIONAL run of <paramref name="unit"/>: for each group declared here, the
        /// consecutive statements that begin right after the declaration's <c>;</c>; for each block-bodied method with a
        /// <c>RouteGroupBuilder</c> parameter, the consecutive statements that begin right after the body's <c>{</c>. A
        /// statement belongs to the run when it is <c>receiver.…;</c> (a chain on the group — a continuation, a
        /// registration, or an extension call passing it on) or <c>[Type.]Name(…, receiver, …);</c> (a call that passes
        /// the group on, task 167 f2). A run ends at the first token that starts anything else, so no <c>if</c>,
        /// loop, lambda, <c>try</c>, <c>return</c> or unrelated statement can stand inside it.
        /// </summary>
        private List<RunStatement> RunsOf(SourceUnit unit)
        {
            if (_runs.TryGetValue(unit, out var cached))
            {
                return cached;
            }

            var runs = new List<RunStatement>();
            var code = unit.Code;

            bool IsWholeIdentifierAt(int p, string name)
                => p + name.Length <= code.Length
                   && string.CompareOrdinal(code, p, name, 0, name.Length) == 0
                   && (p + name.Length == code.Length || !IsIdentChar(code[p + name.Length]));

            void Walk(int from, string receiver, MethodDecl? receivedBy)
            {
                var p = from;
                while (true)
                {
                    p = SkipWs(code, p);
                    if (p >= code.Length)
                    {
                        return;
                    }

                    if (IsWholeIdentifierAt(p, receiver))
                    {
                        // receiver.Chain(...)...;
                        var dot = SkipWs(code, p + receiver.Length);
                        if (dot >= code.Length || code[dot] != '.')
                        {
                            return;
                        }

                        var (_, end, problem) = ParseChain(unit, dot);
                        if (problem is not null || end >= code.Length || code[end] != ';')
                        {
                            return;
                        }

                        runs.Add(new RunStatement(p, SkipWs(code, dot + 1), receiver, receivedBy));
                        p = end + 1;
                        continue;
                    }

                    // [Qualifier.]*Name[<T>](..., receiver, ...);  — a call that passes the group on.
                    var q = p;
                    var nameIndex = -1;
                    while (true)
                    {
                        var identStart = q;
                        while (q < code.Length && IsIdentChar(code[q]))
                        {
                            q++;
                        }

                        if (q == identStart || char.IsDigit(code[identStart]))
                        {
                            return;
                        }

                        if (identStart == p && StatementKeywords.Contains(code[identStart..q]))
                        {
                            return;
                        }

                        nameIndex = identStart;
                        var next = SkipWs(code, q);
                        if (next < code.Length && code[next] == '.')
                        {
                            q = SkipWs(code, next + 1);
                            continue;
                        }

                        q = next;
                        break;
                    }

                    if (q < code.Length && code[q] == '<')
                    {
                        var closeAngle = MatchAngle(code, q);
                        if (closeAngle < 0)
                        {
                            return;
                        }

                        q = SkipWs(code, closeAngle + 1);
                    }

                    if (q >= code.Length || code[q] != '(')
                    {
                        return;
                    }

                    var close = MatchClose(code, q);
                    var semicolon = close < 0 ? -1 : SkipWs(code, close + 1);
                    if (close < 0 || semicolon >= code.Length || code[semicolon] != ';')
                    {
                        return;
                    }

                    var passesReceiver = SplitTopLevel(code, q + 1, close)
                        .Select(span => code[span.Start..span.End].Trim())
                        .Any(arg => arg == receiver || Regex.IsMatch(arg, $@"^[A-Za-z_]\w*\s*:\s*{Regex.Escape(receiver)}$"));
                    if (!passesReceiver)
                    {
                        return;
                    }

                    runs.Add(new RunStatement(p, nameIndex, receiver, receivedBy));
                    p = semicolon + 1;
                }
            }

            foreach (var group in Groups.Where(g => ReferenceEquals(g.Unit, unit) && g.StatementEnd >= 0))
            {
                Walk(group.StatementEnd + 1, group.Variable, null);
            }

            foreach (var method in unit.Methods.Where(m => code[m.BodyStart] == '{'))
            {
                foreach (var parameter in method.Params.Where(p => p.Type.Contains("RouteGroupBuilder", StringComparison.Ordinal)))
                {
                    Walk(method.BodyStart + 1, parameter.Name, method);
                }
            }

            _runs[unit] = runs;
            return runs;
        }

        /// <summary>
        /// Adds a problem for every call of <paramref name="method"/> that binds its group parameter
        /// <paramref name="parameter"/> from a statement OUTSIDE an unconditional run — recursively, when that run in turn
        /// opens a method that received the group (task 167 f2). A call site the scanner cannot read is a problem too.
        /// </summary>
        private void CollectConditionalBindings(MethodDecl method, string parameter, int depth,
            HashSet<(MethodDecl, string)> visiting, List<string> problems)
        {
            if (depth > 24)
            {
                problems.Add($"the binding chain into {method.Name} is too deep to follow");
                return;
            }

            if (!visiting.Add((method, parameter)))
            {
                problems.Add($"{method.Name} passes the group '{parameter}' back into itself — a recursive binding is not followed");
                return;
            }

            var parameterIndex = -1;
            for (var k = 0; k < method.Params.Count; k++)
            {
                if (method.Params[k].Name == parameter)
                {
                    parameterIndex = k;
                }
            }

            foreach (var site in CallSitesOf(method))
            {
                var at = $"{site.Unit.Path}:{site.Unit.LineOf(site.Index)}";
                if (site.Problem is not null)
                {
                    problems.Add($"{at}: {site.Problem}");
                    continue;
                }

                var argument = ArgumentFor(site, method, parameterIndex);
                if (argument is null || !Regex.IsMatch(argument, @"^[A-Za-z_]\w*$"))
                {
                    problems.Add($"{at}: the argument bound to '{parameter}' of {method.Name} is not a variable");
                    continue;
                }

                var statement = RunsOf(site.Unit).FirstOrDefault(r => r.CallNameIndex == site.Index && r.Receiver == argument);
                if (statement is null)
                {
                    problems.Add($"{at}: the call passing '{argument}' to {method.Name} is not in the unbroken run of statements "
                                 + $"that directly follows '{argument}''s declaration (or opens the method receiving it) — it "
                                 + "may be conditional or skipped");
                    continue;
                }

                if (statement.ReceivedBy is not null)
                {
                    CollectConditionalBindings(statement.ReceivedBy, argument, depth + 1, visiting, problems);
                }
            }

            visiting.Remove((method, parameter));
        }

        /// <summary>The argument a call site passes for parameter <paramref name="parameterIndex"/> of
        /// <paramref name="method"/> — named, positional, or the receiver of an extension call; null when absent.</summary>
        private static string? ArgumentFor(CallSite site, MethodDecl method, int parameterIndex)
        {
            if (parameterIndex < 0)
            {
                return null;
            }

            var parameter = method.Params[parameterIndex];
            var argument = site.Args.FirstOrDefault(a => a.Name == parameter.Name).Text;
            if (argument is not null)
            {
                return argument;
            }

            var positional = site.Args.Where(a => a.Name is null).Select(a => a.Text).ToList();
            if (site.ExtensionForm)
            {
                return parameterIndex == 0 ? site.Receiver : parameterIndex - 1 < positional.Count ? positional[parameterIndex - 1] : null;
            }

            return parameterIndex < positional.Count ? positional[parameterIndex] : null;
        }

        /// <summary>Every call site of <paramref name="method"/> in this set.</summary>
        public List<CallSite> CallSitesOf(MethodDecl method)
        {
            if (_callSites.TryGetValue(method, out var cached))
            {
                return cached;
            }

            var sites = new List<CallSite>();
            var name = method.Name;

            foreach (var (unit, at) in CallIndex.Value.GetValueOrDefault(name) ?? new List<(SourceUnit, int)>())
            {
                {
                    var code = unit.Code;
                    var open = SkipWs(code, at + name.Length);

                    if (ReferenceEquals(unit, method.Unit) && at == method.NameIndex)
                    {
                        continue;   // the declaration itself
                    }

                    var (previousWord, _) = IdentifierBefore(code, at);
                    if (previousWord == "new")
                    {
                        continue;
                    }

                    var p = at - 1;
                    while (p >= 0 && char.IsWhiteSpace(code[p]))
                    {
                        p--;
                    }

                    var caller = unit.MethodAt(at);
                    bool extensionForm;
                    string? receiver = null;
                    string? problem = null;

                    if (p >= 0 && code[p] == '.')
                    {
                        var (recv, _) = IdentifierBefore(code, p);
                        if (recv.Length == 0)
                        {
                            if (!method.IsExtension)
                            {
                                continue;
                            }

                            problem = "the binding call is made on an expression, not a variable";
                            extensionForm = true;
                        }
                        else if (recv == method.DeclaringType)
                        {
                            extensionForm = false;   // XEndpoints.MapX(group)
                        }
                        else if (method.IsExtension)
                        {
                            if (_extensionsByName.TryGetValue(name, out var same) && same.Count > 1)
                            {
                                problem = $"the extension name '{name}' is declared {same.Count} times, so "
                                          + $"'{recv}.{name}(...)' cannot be tied to one of them";
                            }

                            extensionForm = true;
                            receiver = recv;
                        }
                        else
                        {
                            continue;   // some other object's method of the same name
                        }
                    }
                    else
                    {
                        // A bare call binds only within the declaring type.
                        if (!ReferenceEquals(unit, method.Unit) || unit.TypeAt(at)?.Name != method.DeclaringType)
                        {
                            continue;
                        }

                        extensionForm = false;
                    }

                    var close = MatchClose(code, open);
                    if (close < 0)
                    {
                        sites.Add(new CallSite(unit, caller, at, extensionForm, receiver, Array.Empty<(string?, string)>(),
                            "the binding call has no closing parenthesis"));
                        continue;
                    }

                    var args = SplitTopLevel(code, open + 1, close)
                        .Select(span =>
                        {
                            var argument = unit.Text[span.Start..span.End].Trim();
                            var named = Regex.Match(argument, @"^([A-Za-z_]\w*)\s*:\s*(?!:)(.*)$", RegexOptions.Singleline);
                            return named.Success ? ((string?)named.Groups[1].Value, named.Groups[2].Value.Trim()) : ((string?)null, argument);
                        })
                        .ToList();

                    var after = SkipWs(code, close + 1);
                    if (problem is null && after < code.Length && code[after] == '.')
                    {
                        problem = "a chain continues on the result of the binding call, so the group's chain could be "
                                  + "extended where this scanner does not look";
                    }

                    sites.Add(new CallSite(unit, caller, at, extensionForm, receiver, args, problem));
                }
            }

            _callSites[method] = sites;
            return sites;
        }

        /// <summary>
        /// Resolves <paramref name="identifier"/>, used as a route builder inside <paramref name="method"/>, to
        /// every group context it can denote. Empty means "never bound" — the method has no caller.
        /// </summary>
        public List<BindingContext> Resolve(SourceUnit unit, MethodDecl? method, string identifier, int depth, int position)
        {
            if (depth > 24)
            {
                return new List<BindingContext> { new(Array.Empty<GroupNode>(), null, "the binding chain is too deep to follow") };
            }

            var group = GroupIn(unit, method, identifier);
            if (group is not null)
            {
                if (group.Problem is not null)
                {
                    return new List<BindingContext> { new(Array.Empty<GroupNode>(), null,
                        $"group '{identifier}' at {unit.Path}:{unit.LineOf(group.Index)}: {group.Problem}") };
                }

                return Resolve(unit, method, group.ParentReceiver, depth + 1, group.Index)
                    .Select(parent => parent with { Nodes = parent.Nodes.Append(group).ToList() })
                    .ToList();
            }

            if (method is not null)
            {
                var parameterIndex = -1;
                for (var k = 0; k < method.Params.Count; k++)
                {
                    if (method.Params[k].Name == identifier)
                    {
                        parameterIndex = k;
                    }
                }

                if (parameterIndex < 0)
                {
                    return new List<BindingContext> { new(Array.Empty<GroupNode>(), null,
                        $"'{identifier}' in {method.Name} is neither a MapGroup variable nor a parameter") };
                }

                var contexts = new List<BindingContext>();
                foreach (var site in CallSitesOf(method))
                {
                    if (site.Problem is not null)
                    {
                        contexts.Add(new(Array.Empty<GroupNode>(), null, $"{site.Unit.Path}:{site.Unit.LineOf(site.Index)}: {site.Problem}"));
                        continue;
                    }

                    var argument = ArgumentFor(site, method, parameterIndex);
                    if (argument is null || !Regex.IsMatch(argument, @"^[A-Za-z_]\w*$"))
                    {
                        contexts.Add(new(Array.Empty<GroupNode>(), null,
                            $"{site.Unit.Path}:{site.Unit.LineOf(site.Index)}: the argument bound to '{identifier}' of "
                            + $"{method.Name} is {(argument is null ? "missing" : $"the expression '{Squash(argument)}'")}, not a variable"));
                        continue;
                    }

                    foreach (var resolved in Resolve(site.Unit, site.Caller, argument, depth + 1, site.Index))
                    {
                        var context = resolved;
                        if (!ReferenceEquals(site.Unit, unit) && context.Nodes.Count > 0 && context.Aggregator is null
                            && context.Problem is null)
                        {
                            context = Aggregators.Contains(site.Unit.Path)
                                ? context with { Aggregator = site.Unit.Path }
                                : context with
                                {
                                    Problem = $"{site.Unit.Path}:{site.Unit.LineOf(site.Index)} binds a route group of "
                                              + $"its own into {unit.Path}, but it is not a pinned aggregator "
                                              + "(see Aggregators) — an unknown aggregator is a route surface nobody has "
                                              + "classified",
                                };
                        }

                        contexts.Add(context);
                    }
                }

                return contexts;
            }

            if (unit.TypeAt(position) is not null)
            {
                // Inside a type but outside every method the parser recognised: the method parser missed the
                // enclosing method. Refuse rather than guess "the root".
                return new List<BindingContext> { new(Array.Empty<GroupNode>(), null,
                    $"{unit.Path}:{unit.LineOf(position)}: '{identifier}' is used inside a type but outside every "
                    + "method the scanner recognised") };
            }

            return new List<BindingContext> { new(Array.Empty<GroupNode>(), null, null) };   // top level: the root
        }
    }

    // =============================================================================================
    // ROUTES — one per (registration site × binding context × verb)
    // =============================================================================================

    private sealed record RouteRegistration(
        string Key,
        string File,
        int Line,
        bool Unparseable,
        string? Problem,
        bool Unbound,
        string? Aggregator,
        bool Anonymous,
        IReadOnlyList<ChainCall> Chain,
        string RouteLiteral,
        IReadOnlyList<GroupNode> Groups,
        SourceUnit? Unit,
        int HandlerStart,
        int HandlerEnd)
    {
        /// <summary>The authorization-shaped forms on the effective chain (route + every group), excluding
        /// <c>AllowAnonymous</c>, which is recorded as <see cref="Anonymous"/> instead.</summary>
        public IReadOnlyList<string> Forms => Chain
            .Where(c => c.IsAuthorizationShaped && c.Name != "AllowAnonymous")
            .Select(c => c.Form)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        public string HandlerText => Unit is null || HandlerEnd <= HandlerStart ? string.Empty : Unit.Text[HandlerStart..HandlerEnd].Trim();
    }

    private static List<RouteRegistration> RoutesIn(SourceSet set, SourceUnit unit)
    {
        var routes = new List<RouteRegistration>();

        foreach (var site in RegistrationSitesIn(unit))
        {
            if (site.Problem is not null)
            {
                routes.Add(new RouteRegistration(
                    $"{site.Verbs[0]} <unresolved> ({unit.Path}:{site.Line})", unit.Path, site.Line, true, site.Problem,
                    false, null, false, site.OwnChain, site.Path ?? string.Empty, Array.Empty<GroupNode>(), unit,
                    site.HandlerStart, site.HandlerEnd));
                continue;
            }

            var method = unit.MethodAt(site.Index);
            if (method is null && unit.Types.Any(t => t.BodyStart <= site.Index && site.Index < t.BodyEnd))
            {
                routes.Add(new RouteRegistration(
                    $"{site.Verbs[0]} <unresolved> ({unit.Path}:{site.Line})", unit.Path, site.Line, true,
                    "the registration is inside a type but outside every method the scanner recognised", false, null,
                    false, site.OwnChain, site.Path!, Array.Empty<GroupNode>(), unit, site.HandlerStart, site.HandlerEnd));
                continue;
            }

            var contexts = set.Resolve(unit, method, site.Receiver, 0, site.Index);
            if (contexts.Count == 0)
            {
                foreach (var verb in site.Verbs)
                {
                    routes.Add(new RouteRegistration(
                        $"{verb} {site.Path}", unit.Path, site.Line, false, null, true, null,
                        site.OwnChain.Any(c => c.Name == "AllowAnonymous"), site.OwnChain, site.Path!,
                        Array.Empty<GroupNode>(), unit, site.HandlerStart, site.HandlerEnd));
                }

                continue;
            }

            foreach (var context in contexts)
            {
                foreach (var verb in site.Verbs)
                {
                    if (context.Problem is not null)
                    {
                        routes.Add(new RouteRegistration(
                            $"{verb} <unresolved> ({unit.Path}:{site.Line})", unit.Path, site.Line, true, context.Problem,
                            false, null, false, site.OwnChain, site.Path!, Array.Empty<GroupNode>(), unit,
                            site.HandlerStart, site.HandlerEnd));
                        continue;
                    }

                    var prefix = context.Nodes.Aggregate(string.Empty, (acc, node) => Combine(acc, node.Segment!));
                    var chain = context.Nodes.SelectMany(node => node.Chain).Concat(site.OwnChain).ToList();
                    routes.Add(new RouteRegistration(
                        $"{verb} {Combine(prefix, site.Path!)}", unit.Path, site.Line, false, null, false,
                        context.Aggregator, chain.Any(c => c.Name == "AllowAnonymous"), chain, site.Path!,
                        context.Nodes, unit, site.HandlerStart, site.HandlerEnd));
                }
            }
        }

        return routes;
    }

    private static string Combine(string prefix, string relative)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            return relative;
        }

        if (relative is "" or "/")
        {
            return prefix;
        }

        return prefix.TrimEnd('/') + "/" + relative.TrimStart('/');
    }

    // =============================================================================================
    // THE REAL SCAN (cached) AND THE FIXTURE SCAN
    // =============================================================================================

    private static string BffRoot => Path.Combine(SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api");

    private static bool IsBuildOutput(string file)
        => file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
           || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string BffRelative(string file)
        => Path.GetRelativePath(BffRoot, file).Replace(Path.DirectorySeparatorChar, '/');

    /// <summary>Every non-obj/bin .cs file under <paramref name="root"/>, lexed, keyed by its root-relative path. The
    /// real scan reads <see cref="BffRoot"/>; the census control reads a temporary root through the SAME code.</summary>
    private static List<SourceUnit> LoadUnits(string root)
        => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Select(f => (Full: f, Relative: Path.GetRelativePath(root, f)))
            .Where(f => !IsBuildOutput(Path.DirectorySeparatorChar + f.Relative))
            .OrderBy(f => f.Full, StringComparer.Ordinal)
            .Select(f => new SourceUnit(f.Relative.Replace(Path.DirectorySeparatorChar, '/'), File.ReadAllText(f.Full)))
            .ToList();

    /// <summary>THE census selection: a unit is an endpoint file when its code (comments and literals blanked)
    /// registers a route in the scanner's vocabulary. <see cref="EndpointFiles"/> is this over the real units.</summary>
    private static List<string> CensusOf(IEnumerable<SourceUnit> units)
        => units.Where(u => RegistrationCall.IsMatch(u.Code)).Select(u => u.Path).ToList();

    private sealed class RealScan
    {
        public RealScan()
        {
            var units = LoadUnits(BffRoot);

            Set = new SourceSet(units, Aggregators.Select(a => a.RelativePath).ToHashSet(StringComparer.Ordinal));
            CensusFiles = CensusOf(units);
            Routes = CensusFiles.SelectMany(path => RoutesIn(Set, Set.Get(path)!)).ToList();
        }

        public SourceSet Set { get; }
        public IReadOnlyList<string> CensusFiles { get; }
        public IReadOnlyList<RouteRegistration> Routes { get; }
        public IEnumerable<RouteRegistration> Live => Routes.Where(r => !r.Unparseable && !r.Unbound);
    }

    private static readonly Lazy<RealScan> RealLazy = new(() => new RealScan(), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The real scan, built once per test run.</summary>
    private static RealScan Real => RealLazy.Value;

    /// <summary>Every BFF file that registers HTTP routes — the census subject, by the scanner's own
    /// vocabulary (<see cref="CensusOf"/> over <see cref="LoadUnits"/> of the BFF root).</summary>
    private static IReadOnlyList<string> EndpointFiles() => Real.CensusFiles;

    private static List<RouteRegistration> ScanFile(string relativePath)
        => Real.Routes.Where(r => r.File == relativePath).ToList();

    /// <summary>
    /// Scans literal source text, isolated from the real BFF — the instrument the negative controls use, so a
    /// control cannot go stale when a real file changes. A receiver at TOP LEVEL (no enclosing method) that is
    /// not a group resolves to the application root, which is how the bare-statement fixtures read.
    /// </summary>
    private static List<RouteRegistration> ScanText(string relativePath, IEnumerable<string> lines)
        => ScanFixtures(new[] { (relativePath, string.Join("\n", lines)) }, relativePath);

    private static List<RouteRegistration> ScanFixtures(
        IEnumerable<(string Path, string Source)> files, string scanPath, IEnumerable<string>? aggregators = null)
    {
        var units = files.Select(f => new SourceUnit(f.Path, f.Source)).ToList();
        var set = new SourceSet(units, (aggregators ?? Array.Empty<string>()).ToHashSet(StringComparer.Ordinal));
        var routes = RoutesIn(set, set.Get(scanPath)!);
        return set.Problems.Count == 0
            ? routes
            : routes.Concat(set.Problems.Select(p => new RouteRegistration(
                $"<problem> {p}", scanPath, 0, true, p, false, null, false, Array.Empty<ChainCall>(), string.Empty,
                Array.Empty<GroupNode>(), null, 0, 0))).ToList();
    }
}

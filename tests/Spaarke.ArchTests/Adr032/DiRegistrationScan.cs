using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Spaarke.ArchTests.Adr032;

/// <summary>
/// A lexical model of the BFF's DI composition: every <c>Add{Singleton|Scoped|Transient|HostedService|HttpClient}</c>
/// registration, the <c>if</c>/<c>else</c> branch path it executes under (following calls from <c>Program.cs</c> through
/// the module extension methods, and treating <c>if (…) return</c> as an implicit gate on the rest of the block), and
/// the dependencies of the registered implementation (constructor parameters by reflection, or
/// <c>GetRequiredService&lt;T&gt;</c> calls inside a factory lambda).
///
/// <para>Used by <see cref="AsymmetricRegistrationTests"/> (ADR-032 / CLAUDE.md §10 F.1 asymmetric registration). Its
/// single question is "is this dependency registered on EVERY path on which its consumer is registered?".</para>
///
/// <para><b>Crude by design</b> (same stance as <see cref="SourceScan"/>): not a compiler. Comments and string/char
/// literal contents are blanked, braces and parentheses are matched, and statements are classified by their leading
/// keyword. It does not evaluate conditions, so two different <c>if</c> statements are unrelated even if they test the
/// same flag; the false positives that produces are handled by the reasoned allowlist in the test, and the false
/// negatives are the listed known limits there.</para>
/// </summary>
internal static class DiRegistrationScan
{
    internal sealed record Cond(int IfId, bool IsElse, string Text);

    internal sealed class Reg
    {
        public required string Key { get; init; }              // service type, simple name ("" for hosted services)
        public required string? Impl { get; init; }            // implementation type text (qualified, generics stripped) or null
        public required bool HasFactoryOrInstance { get; init; }
        public required IReadOnlyList<string> FactoryDeps { get; init; }
        public required IReadOnlyList<Cond> Path { get; init; }
        public required string File { get; init; }
        public required int Line { get; init; }
        public required string Method { get; init; }
        public string Gate => Path.Count == 0
            ? "(unconditional)"
            : string.Join(" && ", Path.Select(c => c.IsElse ? $"else-of[{c.Text}]" : $"[{c.Text}]"));
        public override string ToString() => $"{Key}{(Impl is null ? "" : "->" + Impl)} @ {File}:{Line} in {Method} under {Gate}";
    }

    internal sealed record Violation(Reg Consumer, string Dependency, IReadOnlyList<Reg> DependencyRegs);

    internal sealed class Result
    {
        public List<Reg> Registrations { get; } = new();
        public List<Violation> Violations { get; } = new();
        public List<string> UnreachableMethodsWithRegistrations { get; } = new();
        public List<string> AmbiguousTypes { get; } = new();
        /// <summary>Branches whose block throws unconditionally (<c>if (bad) { …; throw …; }</c>): execution never continues through them, so nothing has to be registered on that side.</summary>
        public HashSet<(int IfId, bool IsElse)> DeadBranches { get; } = new();
        public int RawRegistrationTokens { get; set; }
        public int ReachableRegistrationTokens { get; set; }
    }

    // ───────────────────────────── type lookup ─────────────────────────────

    internal sealed class TypeLookup
    {
        private readonly Dictionary<string, List<Type>> _bySimple = new(StringComparer.Ordinal);

        public TypeLookup(IEnumerable<Assembly> assemblies)
        {
            foreach (var asm in assemblies)
            {
                Type?[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types; }
                foreach (var t in types.OfType<Type>())
                {
                    var simple = StripArity(t.Name);
                    if (!_bySimple.TryGetValue(simple, out var list)) _bySimple[simple] = list = new();
                    list.Add(t);
                }
            }
        }

        public IReadOnlyList<Type> Find(string qualified)
        {
            var simple = LastSegment(qualified);
            if (!_bySimple.TryGetValue(simple, out var list)) return Array.Empty<Type>();
            if (list.Count == 1 || !qualified.Contains('.')) return list;
            var narrowed = list.Where(t => (t.FullName ?? "").Replace('+', '.').EndsWith(qualified, StringComparison.Ordinal)).ToList();
            return narrowed.Count > 0 ? narrowed : list;
        }
    }

    internal static string StripArity(string name)
    {
        var i = name.IndexOf('`');
        return i < 0 ? name : name[..i];
    }

    /// <summary>"global::A.B.C&lt;D&gt;?" → "A.B.C"</summary>
    internal static string Qualified(string typeText)
    {
        var t = typeText.Trim().Replace("global::", "", StringComparison.Ordinal);
        var lt = t.IndexOf('<');
        if (lt >= 0) t = t[..lt];
        return t.TrimEnd('?', ' ');
    }

    internal static string LastSegment(string typeText)
    {
        var q = Qualified(typeText);
        var dot = q.LastIndexOf('.');
        return dot < 0 ? q : q[(dot + 1)..];
    }

    // ───────────────────────────── blanking ─────────────────────────────

    /// <summary>Same-length copy of <paramref name="src"/> with comments and string/char literal contents replaced by spaces (newlines kept).</summary>
    internal static string Blank(string src)
    {
        var sb = new StringBuilder(src.Length);
        int i = 0, n = src.Length;
        void Put(char c) => sb.Append(c == '\n' || c == '\r' ? c : ' ');
        while (i < n)
        {
            char c = src[i];
            if (c == '/' && i + 1 < n && src[i + 1] == '/')
            {
                while (i < n && src[i] != '\n') { Put(src[i]); i++; }
            }
            else if (c == '/' && i + 1 < n && src[i + 1] == '*')
            {
                Put(src[i]); Put(src[i + 1]); i += 2;
                while (i < n && !(src[i] == '*' && i + 1 < n && src[i + 1] == '/')) { Put(src[i]); i++; }
                if (i < n) { Put(src[i]); Put(src[i + 1]); i += 2; }
            }
            else if (c == '"' || ((c == '$' || c == '@') && IsStringStart(src, i)))
            {
                int j = i; bool verbatim = false;
                while (src[j] != '"') { if (src[j] == '@') verbatim = true; sb.Append(src[j]); j++; }
                // raw string literal """ ... """
                if (j + 2 < n && src[j + 1] == '"' && src[j + 2] == '"')
                {
                    sb.Append("\"\"\""); j += 3;
                    while (j < n && !(src[j] == '"' && j + 2 < n && src[j + 1] == '"' && src[j + 2] == '"')) { Put(src[j]); j++; }
                    if (j < n) { sb.Append("\"\"\""); j += 3; }
                    i = j; continue;
                }
                sb.Append('"'); j++;
                while (j < n)
                {
                    if (verbatim)
                    {
                        if (src[j] == '"' && j + 1 < n && src[j + 1] == '"') { Put(src[j]); Put(src[j + 1]); j += 2; continue; }
                        if (src[j] == '"') break;
                    }
                    else
                    {
                        if (src[j] == '\\' && j + 1 < n) { Put(src[j]); Put(src[j + 1]); j += 2; continue; }
                        if (src[j] == '"') break;
                    }
                    Put(src[j]); j++;
                }
                if (j < n) { sb.Append('"'); j++; }
                i = j;
            }
            else if (c == '\'')
            {
                int j = i + 1;
                sb.Append('\'');
                while (j < n && src[j] != '\'' && src[j] != '\n') { if (src[j] == '\\' && j + 1 < n) { Put(src[j]); j++; } Put(src[j]); j++; }
                if (j < n && src[j] == '\'') { sb.Append('\''); j++; }
                i = j;
            }
            else { sb.Append(c); i++; }
        }
        return sb.ToString();
    }

    private static bool IsStringStart(string s, int i)
    {
        int j = i;
        while (j < s.Length && (s[j] == '$' || s[j] == '@')) j++;
        return j < s.Length && s[j] == '"';
    }

    // ───────────────────────────── structure helpers (on blanked text) ─────────────────────────────

    private static int MatchClose(string t, int open)
    {
        char o = t[open], c = o switch { '(' => ')', '{' => '}', '[' => ']', '<' => '>', _ => throw new ArgumentException("not an opener") };
        int depth = 0;
        for (int i = open; i < t.Length; i++)
        {
            if (t[i] == o) depth++;
            else if (t[i] == c && --depth == 0) return i;
        }
        return t.Length - 1;
    }

    private static int SkipWs(string t, int i)
    {
        while (i < t.Length && char.IsWhiteSpace(t[i])) i++;
        return i;
    }

    private static bool StartsWithKeyword(string t, int i, string kw)
        => i + kw.Length <= t.Length
           && string.CompareOrdinal(t, i, kw, 0, kw.Length) == 0
           && (i + kw.Length >= t.Length || !(char.IsLetterOrDigit(t[i + kw.Length]) || t[i + kw.Length] == '_'));

    /// <summary>End index (exclusive) of the simple statement starting at <paramref name="i"/>.</summary>
    private static int StatementEnd(string t, int i, int limit)
    {
        int paren = 0, brace = 0, bracket = 0;
        bool sawAssign = false;
        for (int j = i; j < limit; j++)
        {
            char c = t[j];
            switch (c)
            {
                case '(': paren++; break;
                case ')': paren--; break;
                case '[': bracket++; break;
                case ']': bracket--; break;
                case '=': if (paren == 0 && brace == 0) sawAssign = true; break;
                case '{': brace++; break;
                case '}':
                    brace--;
                    if (brace == 0 && paren == 0 && bracket == 0 && !sawAssign && !HasLambdaOrNew(t, i, j))
                        return j + 1; // local function / member body ends at its closing brace
                    break;
                case ';':
                    if (paren == 0 && brace == 0 && bracket == 0) return j + 1;
                    break;
            }
        }
        return limit;
    }

    private static bool HasLambdaOrNew(string t, int i, int j)
    {
        var s = t.AsSpan(i, j - i);
        return s.Contains("=>".AsSpan(), StringComparison.Ordinal) || s.Contains("new ".AsSpan(), StringComparison.Ordinal) || s.Contains("new(".AsSpan(), StringComparison.Ordinal);
    }

    // ───────────────────────────── analysis ─────────────────────────────

    private sealed class MethodDef
    {
        public required string Name { get; init; }
        public required string File { get; init; }
        public required string Text { get; init; }   // the file's blanked text
        public required int BodyStart { get; init; } // inclusive
        public required int BodyEnd { get; init; }   // exclusive
        public bool Walked { get; set; }
        public int RegistrationTokens { get; set; }
    }

    private static readonly Regex MethodDefRx = new(
        @"\bstatic\s+[\w<>\[\]\.\?, ]+?\s+(?<name>[A-Za-z_]\w*)\s*(?:<[^>()]*>)?\s*\(\s*(?:this\s+)?(?:Microsoft\.Extensions\.DependencyInjection\.)?IServiceCollection\s+\w+",
        RegexOptions.Compiled);

    private static readonly Regex RegTokenRx = new(
        @"\b(?<try>Try)?Add(?<keyed>Keyed)?(?<kind>Singleton|Scoped|Transient|HostedService|HttpClient)\b",
        RegexOptions.Compiled);

    /// <summary>
    /// Microsoft extension methods that register a service the BFF's own code injects. Without these the scan would
    /// see only the BFF's metrics DECORATOR of <c>IDistributedCache</c> (a conditional <c>Replace</c> of an existing
    /// descriptor) and report every cache consumer as depending on a conditional service.
    /// </summary>
    private static readonly (string Helper, string Provided)[] FrameworkProviders =
    {
        ("AddDistributedMemoryCache", "IDistributedCache"),
        ("AddStackExchangeRedisCache", "IDistributedCache"),
        ("AddMemoryCache", "IMemoryCache"),
    };

    private static readonly Regex CallRx = new(@"\b(?<name>[A-Za-z_]\w*)\s*(?:<[^()]*?>)?\s*\(", RegexOptions.Compiled);
    private static readonly Regex GetRequiredRx = new(@"GetRequiredService\s*<\s*(?<t>[\w\.:]+)", RegexOptions.Compiled);

    internal static Result Analyze(IEnumerable<(string File, string Source)> sources, TypeLookup types)
    {
        var result = new Result();
        var originals = new Dictionary<object, string>(ReferenceEqualityComparer.Instance);
        var files = sources.Select(s => { var blank = Blank(s.Source); originals[blank] = s.Source; return (s.File, Text: blank); }).ToList();

        // 1. every IServiceCollection-taking static method
        var methods = new Dictionary<string, List<MethodDef>>(StringComparer.Ordinal);
        var allMethods = new List<MethodDef>();
        foreach (var (file, text) in files)
        {
            foreach (Match m in MethodDefRx.Matches(text))
            {
                var open = text.IndexOf('(', m.Groups["name"].Index);
                var closeParen = MatchClose(text, open);
                int k = SkipWs(text, closeParen + 1);
                while (k < text.Length && StartsWithKeyword(text, k, "where"))
                {
                    while (k < text.Length && text[k] != '{' && !(text[k] == '=' && k + 1 < text.Length && text[k + 1] == '>')) k++;
                }
                MethodDef def;
                if (k < text.Length && text[k] == '{')
                {
                    var end = MatchClose(text, k);
                    def = new MethodDef { Name = m.Groups["name"].Value, File = file, Text = text, BodyStart = k + 1, BodyEnd = end };
                }
                else if (k + 1 < text.Length && text[k] == '=' && text[k + 1] == '>')
                {
                    var end = StatementEnd(text, k + 2, text.Length);
                    def = new MethodDef { Name = m.Groups["name"].Value, File = file, Text = text, BodyStart = k + 2, BodyEnd = end };
                }
                else continue;
                if (!methods.TryGetValue(def.Name, out var list)) methods[def.Name] = list = new();
                list.Add(def);
                allMethods.Add(def);
                def.RegistrationTokens = RegTokenRx.Matches(text.Substring(def.BodyStart, def.BodyEnd - def.BodyStart)).Count;
            }
        }

        // 2. walk from Program.cs, inlining calls
        var program = files.FirstOrDefault(f => Path.GetFileName(f.File) == "Program.cs");
        if (program.File is null) throw new InvalidOperationException("Program.cs not among the scanned sources — it is the entry point of the walk.");

        int ifCounter = 0;
        var walker = new Walker(methods, result, () => ++ifCounter, originals);
        walker.WalkRange(program.Text, 0, program.Text.Length, new List<Cond>(), program.File, "Program.cs", new HashSet<MethodDef>(), 0);

        foreach (var def in allMethods.Where(d => !d.Walked && d.RegistrationTokens > 0))
            result.UnreachableMethodsWithRegistrations.Add($"{def.File}::{def.Name} ({def.RegistrationTokens} registration tokens)");

        result.RawRegistrationTokens = files.Sum(f => RegTokenRx.Matches(f.Text).Count);
        result.ReachableRegistrationTokens = walker.RegistrationTokensSeenOncePerSite;

        // 3. check
        var providedBy = result.Registrations.Where(r => r.Key.Length > 0).GroupBy(r => r.Key).ToDictionary(g => g.Key, g => g.ToList());
        var ambiguous = new HashSet<string>();
        foreach (var consumer in result.Registrations)
        {
            foreach (var dep in DependenciesOf(consumer, types, ambiguous))
            {
                if (!providedBy.TryGetValue(dep, out var regs)) continue; // not ours (framework / config) or never registered → a different failure mode
                if (!AvailableUnder(consumer.Path, regs, result.DeadBranches))
                    result.Violations.Add(new Violation(consumer, dep, regs));
            }
        }
        result.AmbiguousTypes.AddRange(ambiguous.OrderBy(x => x));
        var seen = new HashSet<string>();
        result.Violations.RemoveAll(v => !seen.Add($"{v.Consumer.File}:{v.Consumer.Line}|{v.Consumer.Key}|{v.Dependency}"));
        return result;
    }

    // dependency simple-names of a registration
    private static IEnumerable<string> DependenciesOf(Reg r, TypeLookup types, HashSet<string> ambiguous)
    {
        if (r.HasFactoryOrInstance) return r.FactoryDeps;
        if (r.Impl is null) return Array.Empty<string>();
        var found = types.Find(r.Impl).Where(t => t.IsClass && !t.IsAbstract).ToList();
        if (found.Count == 0) return Array.Empty<string>();
        if (found.Count > 1) { ambiguous.Add(r.Impl); return Array.Empty<string>(); }
        var ctors = found[0].GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        if (ctors.Length == 0) return Array.Empty<string>();
        // DI picks the longest constructor it can satisfy, so the registration is only broken when EVERY public
        // constructor needs something absent: report the constructor with the fewest required (non-defaulted) parameters.
        return ctors
            .Select(c => c.GetParameters().Where(p => !p.HasDefaultValue && !p.IsOptional).Select(p => StripArity(p.ParameterType.Name)).ToList())
            .OrderBy(l => l.Count)
            .First();
    }

    // Is a service registered on every execution path of a consumer registered under consumerPath?
    private static bool AvailableUnder(IReadOnlyList<Cond> consumerPath, IReadOnlyList<Reg> depRegs, HashSet<(int IfId, bool IsElse)> dead)
    {
        var chosen = consumerPath.ToDictionary(c => c.IfId, c => c.IsElse);

        // What the consumer's own path proves, by condition TEXT: the conjuncts of every `then` condition are true;
        // a single-conjunct `else` condition is false. This is what lets a consumer under
        // `if (analysis && docIntel)` rely on a dependency under `if (docIntel)` — the conditions are different
        // statements, but the first implies the second.
        var trueAtoms = new HashSet<string>(StringComparer.Ordinal);
        var falseAtoms = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in consumerPath)
        {
            var atoms = Atoms(c.Text);
            if (!c.IsElse) { foreach (var a in atoms) trueAtoms.Add(a); }
            else if (atoms.Count == 1) falseAtoms.Add(atoms[0]);
        }

        var reduced = new List<List<Cond>>();
        foreach (var r in depRegs)
        {
            if (r.Path.Any(c => chosen.TryGetValue(c.IfId, out var isElse) && isElse != c.IsElse)) continue; // contradicts the consumer's own branch
            var remaining = new List<Cond>();
            var contradicted = false;
            foreach (var c in r.Path)
            {
                if (chosen.ContainsKey(c.IfId)) continue; // same statement as one of the consumer's own branches
                var atoms = Atoms(c.Text);
                if (!c.IsElse)
                {
                    if (atoms.All(trueAtoms.Contains)) continue;                  // implied by the consumer's path
                    if (atoms.Count == 1 && falseAtoms.Contains(atoms[0])) { contradicted = true; break; }
                }
                else
                {
                    if (atoms.Count == 1 && falseAtoms.Contains(atoms[0])) continue;
                    if (atoms.All(trueAtoms.Contains)) { contradicted = true; break; }
                }
                remaining.Add(c);
            }
            if (!contradicted) reduced.Add(remaining);
        }
        return Covered(reduced, dead);
    }

    /// <summary>Top-level <c>&amp;&amp;</c> conjuncts of a condition; a condition containing <c>||</c> is a single opaque atom.</summary>
    private static List<string> Atoms(string cond)
    {
        if (cond.Contains("||", StringComparison.Ordinal)) return new List<string> { cond.Trim() };
        return cond.Split("&&", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(a => a.Trim()).ToList();
    }

    private static bool Covered(List<List<Cond>> paths, HashSet<(int IfId, bool IsElse)> dead)
    {
        if (paths.Count == 0) return false;
        if (paths.Any(p => p.Count == 0)) return true;
        foreach (var byIf in paths.GroupBy(p => p[0].IfId))
        {
            var id = byIf.Key;
            var thenSide = byIf.Where(p => !p[0].IsElse).Select(p => p.Skip(1).ToList()).ToList();
            var elseSide = byIf.Where(p => p[0].IsElse).Select(p => p.Skip(1).ToList()).ToList();
            var thenOk = dead.Contains((id, false)) || Covered(thenSide, dead);
            var elseOk = dead.Contains((id, true)) || Covered(elseSide, dead);
            if (thenOk && elseOk) return true;
        }
        return false;
    }

    // ───────────────────────────── walker ─────────────────────────────

    private sealed class Walker
    {
        private readonly Dictionary<string, List<MethodDef>> _methods;
        private readonly Result _result;
        private readonly Func<int> _nextIfId;
        private readonly HashSet<string> _seenSites = new();
        public int RegistrationTokensSeenOncePerSite { get; private set; }

        private readonly Dictionary<object, string> _originals; // blanked text (by reference) → original text

        public Walker(Dictionary<string, List<MethodDef>> methods, Result result, Func<int> nextIfId, Dictionary<object, string> originals)
        { _methods = methods; _result = result; _nextIfId = nextIfId; _originals = originals; }

        public void WalkRange(string t, int start, int end, List<Cond> path, string file, string method, HashSet<MethodDef> stack, int depth)
        {
            int i = start;
            var current = path;
            while (true)
            {
                i = SkipWs(t, i);
                if (i >= end) break;
                if (t[i] == '{') // bare block
                {
                    var close = MatchClose(t, i);
                    WalkRange(t, i + 1, close, current, file, method, stack, depth);
                    i = close + 1;
                    continue;
                }
                if (StartsWithKeyword(t, i, "if"))
                {
                    var condOpen = t.IndexOf('(', i);
                    var condClose = MatchClose(t, condOpen);
                    // condition text from the ORIGINAL source (same offsets): string literals in a condition, e.g.
                    // GetValue<bool>("DocumentIntelligence:RecordMatchingEnabled"), are blanked in the structural copy
                    var condSource = _originals.TryGetValue(t, out var original) ? original : t;
                    var condText = Regex.Replace(condSource.Substring(condOpen + 1, condClose - condOpen - 1), @"\s+", " ").Trim();
                    var (thenS, thenE, afterThen) = Body(t, condClose + 1, end);
                    int afterAll = afterThen;
                    int elseS = -1, elseE = -1;
                    var k = SkipWs(t, afterThen);
                    if (k < end && StartsWithKeyword(t, k, "else"))
                    {
                        (elseS, elseE, afterAll) = Body(t, k + 4, end);
                    }
                    var id = _nextIfId();
                    var thenPath = new List<Cond>(current) { new Cond(id, false, condText) };
                    var elsePath = new List<Cond>(current) { new Cond(id, true, condText) };
                    if (Throws(t, thenS, thenE)) _result.DeadBranches.Add((id, false));
                    if (elseS >= 0 && Throws(t, elseS, elseE)) _result.DeadBranches.Add((id, true));
                    WalkRange(t, thenS, thenE, thenPath, file, method, stack, depth);
                    if (elseS >= 0) WalkRange(t, elseS, elseE, elsePath, file, method, stack, depth);
                    bool thenReturns = Returns(t, thenS, thenE), elseReturns = elseS >= 0 && Returns(t, elseS, elseE);
                    if (thenReturns && !elseReturns) current = elsePath;        // rest of the block only runs when the condition is false
                    else if (elseReturns && !thenReturns) current = thenPath;   // …or only when it is true
                    i = afterAll;
                    continue;
                }
                if (StartsWithKeyword(t, i, "foreach") || StartsWithKeyword(t, i, "for") || StartsWithKeyword(t, i, "while")
                    || (StartsWithKeyword(t, i, "using") && t[SkipWs(t, i + 5)] == '(')
                    || StartsWithKeyword(t, i, "lock") || StartsWithKeyword(t, i, "switch"))
                {
                    var open = t.IndexOf('(', i);
                    var close = MatchClose(t, open);
                    var (bs, be, after) = Body(t, close + 1, end);
                    WalkRange(t, bs, be, current, file, method, stack, depth);
                    i = after;
                    continue;
                }
                if (StartsWithKeyword(t, i, "try") || StartsWithKeyword(t, i, "finally"))
                {
                    var kwLen = StartsWithKeyword(t, i, "try") ? 3 : 7;
                    var (bs, be, after) = Body(t, i + kwLen, end);
                    WalkRange(t, bs, be, current, file, method, stack, depth);
                    i = after;
                    continue;
                }
                if (StartsWithKeyword(t, i, "catch"))
                {
                    var o = t.IndexOf('{', i);
                    i = MatchClose(t, o) + 1; // registrations inside catch blocks are not part of normal composition
                    continue;
                }
                if (StartsWithKeyword(t, i, "else")) { i += 4; continue; }

                var stmtEnd = StatementEnd(t, i, end);
                if (stmtEnd <= i) stmtEnd = i + 1;
                Statement(t, i, stmtEnd, current, file, method, stack, depth);
                i = stmtEnd;
            }
        }

        private static (int Start, int End, int After) Body(string t, int from, int limit)
        {
            var s = SkipWs(t, from);
            if (s < limit && t[s] == '{')
            {
                var close = MatchClose(t, s);
                return (s + 1, close, close + 1);
            }
            if (s < limit && StartsWithKeyword(t, s, "if"))
            {
                // `else if` chains: the nested if (and its own else) is the body
                var condOpen = t.IndexOf('(', s);
                var condClose = MatchClose(t, condOpen);
                var (_, _, afterThen) = Body(t, condClose + 1, limit);
                var k = SkipWs(t, afterThen);
                int after = afterThen;
                if (k < limit && StartsWithKeyword(t, k, "else")) after = Body(t, k + 4, limit).After;
                return (s, after, after);
            }
            var e = StatementEnd(t, s, limit);
            return (s, e, e);
        }

        /// <summary>The block always ends in a throw: a top-level throw, or an if/else-if/else chain every arm of which does.</summary>
        private static bool Throws(string t, int s, int e)
        {
            int i = s;
            while (true)
            {
                i = SkipWs(t, i);
                if (i >= e) return false;
                if (StartsWithKeyword(t, i, "throw")) return true;
                if (t[i] == '{') { if (Throws(t, i + 1, MatchClose(t, i))) return true; i = MatchClose(t, i) + 1; continue; }
                if (StartsWithKeyword(t, i, "if"))
                {
                    var condOpen = t.IndexOf('(', i);
                    var condClose = MatchClose(t, condOpen);
                    var (thenS, thenE, after) = Body(t, condClose + 1, e);
                    var k = SkipWs(t, after);
                    if (k < e && StartsWithKeyword(t, k, "else"))
                    {
                        var (elseS, elseE, afterElse) = Body(t, k + 4, e);
                        if (Throws(t, thenS, thenE) && Throws(t, elseS, elseE)) return true;
                        after = afterElse;
                    }
                    i = after;
                    continue;
                }
                var next = StatementEnd(t, i, e);
                i = next <= i ? i + 1 : next;
            }
        }

        private static bool Returns(string t, int s, int e) => TopLevelKeyword(t, s, e, "return");

        /// <summary>Does the block contain a statement starting with <paramref name="keyword"/> at its own level (not inside a nested if)?</summary>
        private static bool TopLevelKeyword(string t, int s, int e, string keyword)
        {
            int i = s;
            while (true)
            {
                i = SkipWs(t, i);
                if (i >= e) return false;
                if (StartsWithKeyword(t, i, keyword)) return true;
                if (t[i] == '{') { i = MatchClose(t, i) + 1; continue; }
                if (StartsWithKeyword(t, i, "if"))
                {
                    var condOpen = t.IndexOf('(', i);
                    var condClose = MatchClose(t, condOpen);
                    var (_, _, after) = Body(t, condClose + 1, e);
                    var k = SkipWs(t, after);
                    if (k < e && StartsWithKeyword(t, k, "else")) after = Body(t, k + 4, e).After;
                    i = after;
                    continue;
                }
                var next = StatementEnd(t, i, e);
                i = next <= i ? i + 1 : next;
            }
        }

        private void Statement(string t, int s, int e, List<Cond> path, string file, string method, HashSet<MethodDef> stack, int depth)
        {
            var text = t.Substring(s, e - s);

            foreach (Match m in RegTokenRx.Matches(text))
            {
                var site = $"{file}:{s + m.Index}";
                if (_seenSites.Add(site)) RegistrationTokensSeenOncePerSite++;
                var kind = m.Groups["kind"].Value;
                var keyed = m.Groups["keyed"].Success;
                int q = m.Index + m.Length;
                var genericArgs = new List<string>();
                while (q < text.Length && char.IsWhiteSpace(text[q])) q++;
                if (q < text.Length && text[q] == '<')
                {
                    var gclose = MatchClose(text, q);
                    genericArgs = SplitTopLevel(text.Substring(q + 1, gclose - q - 1));
                    q = gclose + 1;
                    while (q < text.Length && char.IsWhiteSpace(text[q])) q++;
                }
                var argsText = "";
                if (q < text.Length && text[q] == '(')
                {
                    var pclose = MatchClose(text, q);
                    argsText = text.Substring(q + 1, pclose - q - 1);
                }
                var hasArgs = argsText.Trim().Length > 0;
                var line = LineOf(t, s + m.Index);

                string key; string? impl;
                if (kind == "HostedService") { key = ""; impl = genericArgs.Count >= 1 ? Qualified(genericArgs[0]) : null; }
                else if (kind == "HttpClient")
                {
                    if (genericArgs.Count == 0) continue;
                    key = LastSegment(genericArgs[0]);
                    impl = Qualified(genericArgs.Count >= 2 ? genericArgs[1] : genericArgs[0]);
                }
                else if (genericArgs.Count >= 1)
                {
                    key = LastSegment(genericArgs[0]);
                    // AddKeyedX<TService>(key, ...) has ONE generic arg; AddX<TService, TImpl>() has two.
                    impl = genericArgs.Count >= 2 && !keyed ? Qualified(genericArgs[1]) : Qualified(genericArgs[0]);
                }
                else
                {
                    var typeofs = Regex.Matches(argsText, @"typeof\s*\(\s*(?<t>[\w\.:]+)\s*(?<open><\s*,*\s*>)?\s*\)")
                        .Select(x => (Name: x.Groups["t"].Value, Open: x.Groups["open"].Success)).ToList();
                    if (typeofs.Count == 0 || typeofs.Any(x => x.Open)) continue; // open generics / non-literal registrations are out of scope
                    key = LastSegment(typeofs[0].Name);
                    impl = Qualified(typeofs.Count > 1 ? typeofs[1].Name : typeofs[0].Name);
                    hasArgs = false;
                }

                // A factory / instance registration has no constructor to reflect on; its dependencies are the
                // GetRequiredService<T> calls it makes.
                var factoryDeps = hasArgs
                    ? GetRequiredRx.Matches(argsText).Select(x => LastSegment(x.Groups["t"].Value)).Distinct().ToList()
                    : new List<string>();

                _result.Registrations.Add(new Reg
                {
                    Key = key, Impl = impl, HasFactoryOrInstance = hasArgs, FactoryDeps = factoryDeps,
                    Path = path.ToList(), File = file, Line = line, Method = method,
                });
            }

            // framework helpers that register a well-known service the BFF modules then consume
            foreach (var (helper, provided) in FrameworkProviders)
            {
                foreach (Match m in Regex.Matches(text, $@"\b{helper}\s*\("))
                {
                    _result.Registrations.Add(new Reg
                    {
                        Key = provided, Impl = null, HasFactoryOrInstance = false, FactoryDeps = Array.Empty<string>(),
                        Path = path.ToList(), File = file, Line = LineOf(t, s + m.Index), Method = method,
                    });
                }
            }

            // calls into other module methods: inline them under the current path
            if (depth >= 14) return;
            foreach (Match m in CallRx.Matches(text))
            {
                var name = m.Groups["name"].Value;
                if (!_methods.TryGetValue(name, out var defs)) continue;
                foreach (var def in defs)
                {
                    if (!stack.Add(def)) continue;
                    def.Walked = true;
                    WalkRange(def.Text, def.BodyStart, def.BodyEnd, path, def.File, def.Name, stack, depth + 1);
                    stack.Remove(def);
                }
            }
        }

        private static int LineOf(string t, int index)
        {
            int line = 1;
            for (int i = 0; i < index && i < t.Length; i++) if (t[i] == '\n') line++;
            return line;
        }

        private static List<string> SplitTopLevel(string s)
        {
            var parts = new List<string>();
            int depth = 0, last = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '<') depth++;
                else if (s[i] == '>') depth--;
                else if (s[i] == ',' && depth == 0) { parts.Add(s[last..i].Trim()); last = i + 1; }
            }
            parts.Add(s[last..].Trim());
            return parts;
        }
    }
}

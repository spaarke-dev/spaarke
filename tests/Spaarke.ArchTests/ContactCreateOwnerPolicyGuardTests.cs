using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
/// <para><b>What counts as a mention.</b> The identifier token <c>NoneForForeignEnvironment</c> or <c>ForeignEnvironment</c>
/// in the syntax tree of a server file: a call, a method group, a <c>nameof</c>. Comments and literals are not identifier
/// tokens and are skipped. A mention is allowed only where its nearest enclosing class is <c>ContactIdentityBinderFactory</c>
/// (in <c>ContactIdentityBinder.cs</c>), or where it is the member's own declaration (in <c>ContactCreateOwner.cs</c>), so a
/// use in the binder class, or anywhere else in either file, still fails. Test code is out of scope: tests that do not
/// exercise ownership build their binder with a named test-kit policy.</para>
/// <para>Known limit: the rule matches the member's name, not its behaviour. Reflection by any string is not seen;
/// the constructor of <c>ContactCreateOwner</c> is private and its factory methods are pinned below, so a new way to say "no
/// owner" has to appear in the one file this guard reads.</para>
/// <para>Both rules read the Roslyn syntax tree of every server file (parsed once). Each rule has a must-fire and a
/// must-not-fire control below. Enforced by: this guard (ArchTests, CI and the Stop hook).</para>
/// </remarks>
public class ContactCreateOwnerPolicyGuardTests
{
    private const string FactoryFile = "src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ContactIdentityBinder.cs";
    private const string FactoryClass = "ContactIdentityBinderFactory";
    private const string DeclaringFile = "src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ContactCreateOwner.cs";

    // UAC review F3: also the bare variant name. ContactCreateOwner once had a nested ForeignEnvironment class with an internal
    // constructor, which `new ContactCreateOwner.ForeignEnvironment("x")` could use without naming the factory method.
    private static readonly string[] MentionNames = { "NoneForForeignEnvironment", "ForeignEnvironment" };

    /// <summary>The mentions in one file's syntax tree that the rule does not allow: "line N" for each.</summary>
    internal static IReadOnlyList<string> Violations(string relativePath, string code) => Violations(relativePath, Parse(code).GetRoot());

    internal static IReadOnlyList<string> Violations(string relativePath, SyntaxNode root)
    {
        if (relativePath == FactoryFile)
        {
            var declarations = FactoryDeclarations(root);
            if (declarations == 0)
            {
                return new[] { $"class {FactoryClass} was not found (the guard would be vacuous)" };
            }

            if (declarations > 1)
            {
                return new[] { $"class {FactoryClass} is declared {declarations} times in the file: exactly one declaration is allowed" };
            }
        }

        return Mentions(root)
            .Where(token => !IsAllowedMention(relativePath, token))
            .Select(token => $"line {token.GetLocation().GetLineSpan().StartLinePosition.Line + 1}")
            .ToList();
    }

    /// <summary>The identifier tokens that name the member (or its bare variant): code only, never a comment or a literal.</summary>
    private static IEnumerable<SyntaxToken> Mentions(SyntaxNode root)
        => root.DescendantTokens().Where(t => t.IsKind(SyntaxKind.IdentifierToken) && MentionNames.Contains(t.ValueText, StringComparer.Ordinal));

    /// <summary>The nearest enclosing class of a token is the factory in the factory file, or the token is the member's own declaration in the declaring file.</summary>
    private static bool IsAllowedMention(string relativePath, SyntaxToken token)
        => (relativePath == FactoryFile && IsInFactoryClass(token)) || (relativePath == DeclaringFile && IsTheDeclaration(token));

    private static int FactoryDeclarations(SyntaxNode root)
        => root.DescendantNodes().OfType<ClassDeclarationSyntax>().Count(c => c.Identifier.ValueText == FactoryClass);

    /// <summary>The nearest enclosing class is the factory, declared at the top level (not inside another type), and the only declaration of that name in the file.</summary>
    private static bool IsInFactoryClass(SyntaxToken token)
        => token.Parent?.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault() is { } nearest
            && nearest.Identifier.ValueText == FactoryClass
            && nearest.Parent is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax
            && token.SyntaxTree is { } tree && FactoryDeclarations(tree.GetRoot()) == 1;

    private static bool IsTheDeclaration(SyntaxToken token)
        => token.ValueText == "NoneForForeignEnvironment"
            && token.Parent is MethodDeclarationSyntax method
            && method.Identifier == token
            && method.Modifiers.Any(SyntaxKind.StaticKeyword)
            && RightmostName(method.ReturnType) == "ContactCreateOwner";

    [Fact(DisplayName = "ISS-005: NoneForForeignEnvironment is mentioned in server code only in ContactIdentityBinderFactory")]
    public void NoneForForeignEnvironment_IsUsedOnlyByTheFactory()
    {
        var offenders = ServerTree.Value
            .SelectMany(f => Violations(f.Path, f.Root).Select(v => $"{f.Path}: {v}"))
            .ToList();

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
        var files = ServerTree.Value;
        var factory = files.Single(f => f.Path == FactoryFile);
        Assert.Contains(Mentions(factory.Root), IsInFactoryClass);

        var declaring = files.Single(f => f.Path == DeclaringFile);
        Assert.Contains(Mentions(declaring.Root), IsTheDeclaration);
    }

    // ── No ownerless contact create anywhere in server code (ISS-005, #1673) ────────────────────────────────────
    //
    // The create guard parses every server file with Roslyn and reads literal VALUES from tokens (string, verbatim, raw and
    // interpolated-string text parts), so what a literal contains - a brace, a slash pair, a comment opener - never changes
    // how the code is divided into members. Members, lambdas and local functions are syntax nodes, not text ranges.

    private const string ServerRoot = "src/server/";
    private const string BffRoot = "src/server/api/Sprk.Bff.Api/";
    private const string StoreFile = BffRoot + "Infrastructure/ExternalAccess/ContactIdentityStore.cs";
    private const string InviteFile = BffRoot + "Api/ExternalAccess/ExternalInviteService.cs";

    /// <summary>Slice C2c: the invite's contact create, made by the binder's guarded create (the invite service calls it).</summary>
    private const string InviteCreateFile = BffRoot + "Infrastructure/ExternalAccess/ContactIdentityBinder.cs";

    /// <summary>Stands in for an interpolation hole (or a non-literal operand) whose value is not known from the syntax.</summary>
    private const char Hole = '\u0001';

    private static readonly Regex ContactSetName = new(@"^\w*Contacts?EntitySet$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PostMethodName = new(@"^Post\w*Async$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private enum Shape
    {
        /// <summary><c>x.CreateAsync(contacts, payload)</c> (the Web API client, <c>DataverseWebApiClient</c>).</summary>
        WebApiCreateAsync,

        /// <summary>
        /// <c>HttpMethod.Post</c> next to a contacts path in one argument list or tuple: <c>WriteAsync(HttpMethod.Post, "contacts", ...)</c>,
        /// <c>new HttpRequestMessage(HttpMethod.Post, $"{GetApiUrl()}/contacts")</c>, a <c>(HttpMethod.Post, "contacts", payload)</c> request tuple.
        /// </summary>
        HttpPost,

        /// <summary><c>x.PostAsync(".../contacts", body)</c>, <c>PostAsJsonAsync</c> and the like (a user-context Web API client).</summary>
        UserClientPost,

        /// <summary>
        /// <c>new Entity("contact")</c> or <c>new Entity { LogicalName = "contact" }</c>, the SDK entity that
        /// <c>IGenericEntityService.CreateAsync(Entity)</c> and <c>IOrganizationService.Create</c> take.
        /// </summary>
        SdkEntity,
    }

    // ── Literal values ──────────────────────────────────────────────────────────────────────────────

    /// <summary>How many local-variable hops <see cref="Texts(ExpressionSyntax, IReadOnlyCollection{string})"/> follows from one expression.</summary>
    private const int MaxLocalDepth = 4;

    /// <summary>Upper bound on the alternatives kept for one expression, so a chain of conditionals cannot grow without limit.</summary>
    private const int MaxAlternatives = 512;

    /// <summary>
    /// The possible string values of an expression, read from its tokens: a string / verbatim / raw literal, an interpolated string
    /// (text parts as written; a hole whose own expression has a known value is inlined, any other hole is <see cref="Hole"/>),
    /// a <c>+</c> concatenation (a non-literal operand is a hole), both branches of <c>?:</c> and <c>??</c>, <c>new Uri(x)</c> and
    /// <c>new Uri(base, x)</c> (the values of the last argument), a name that is a discovered contact constant
    /// (<paramref name="constantNames"/>) or ends in <c>ContactEntitySet</c>, and a LOCAL variable: the values of its initializer
    /// and of every <c>name = expr</c> in the same unit (or, for a captured variable, the unit around it), to a depth of
    /// <see cref="MaxLocalDepth"/> with a cycle guard. Empty when the expression is not a string the syntax can read.
    /// </summary>
    internal static IReadOnlyList<string> Texts(ExpressionSyntax expression, IReadOnlyCollection<string> constantNames)
        => Texts(expression, constantNames, 0, new Resolution());

    /// <summary>
    /// The work state of one local-variable resolution: the sources being expanded right now (the cycle guard) and the result per
    /// (source, depth), so a chain of many assignments to one local costs one evaluation per source and depth, not one per path.
    /// Inside a cycle the first value computed for a (source, depth) is the one reused.
    /// </summary>
    private sealed class Resolution
    {
        public HashSet<SyntaxNode> Active { get; } = new();

        public Dictionary<(SyntaxNode Source, int Depth), IReadOnlyList<string>> Values { get; } = new();

        public Dictionary<(SyntaxNode Source, int Depth), bool> Posts { get; } = new();
    }

    private static IReadOnlyList<string> Texts(ExpressionSyntax expression, IReadOnlyCollection<string> constantNames, int depth, Resolution state)
    {
        IReadOnlyList<string> Inner(ExpressionSyntax e) => Texts(e, constantNames, depth, state);

        switch (expression)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                return Inner(parenthesized.Expression);

            case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression):
                return new[] { literal.Token.ValueText };

            case InterpolatedStringExpressionSyntax interpolated:
            {
                IReadOnlyList<string> result = new[] { string.Empty };
                foreach (var part in interpolated.Contents)
                {
                    IReadOnlyList<string> alternatives = part switch
                    {
                        InterpolatedStringTextSyntax text => new[] { text.TextToken.ValueText },
                        InterpolationSyntax { AlignmentClause: null, FormatClause: null } hole
                            when Inner(hole.Expression) is { Count: > 0 } inner => inner,
                        _ => new[] { Hole.ToString() },
                    };
                    result = Product(result, alternatives);
                }

                return result;
            }

            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AddExpression):
            {
                var left = Inner(binary.Left);
                var right = Inner(binary.Right);
                return left.Count == 0 && right.Count == 0
                    ? Array.Empty<string>()
                    : Product(left.Count > 0 ? left : new[] { Hole.ToString() }, right.Count > 0 ? right : new[] { Hole.ToString() });
            }

            case BinaryExpressionSyntax coalesce when coalesce.IsKind(SyntaxKind.CoalesceExpression):
                return Inner(coalesce.Left).Concat(Inner(coalesce.Right)).ToList();

            case ConditionalExpressionSyntax conditional:
                return Inner(conditional.WhenTrue).Concat(Inner(conditional.WhenFalse)).ToList();

            case ObjectCreationExpressionSyntax { ArgumentList: { } uriArguments } uri when RightmostName(uri.Type) == "Uri":
            {
                // new Uri(x), new Uri(base, x), new Uri(x, UriKind.Relative): the path is the last argument that is not a UriKind.
                var path = uriArguments.Arguments.Select(a => a.Expression)
                    .LastOrDefault(a => !(a is MemberAccessExpressionSyntax m && RightmostName(m.Expression) == "UriKind"));
                return path is null ? Array.Empty<string>() : Inner(path);
            }

            case IdentifierNameSyntax or MemberAccessExpressionSyntax:
            {
                var values = new List<string>();
                if (RightmostName(expression) is { } name && (constantNames.Contains(name) || ContactSetName.IsMatch(name)))
                {
                    values.Add("contacts");
                }

                if (expression is IdentifierNameSyntax local && depth < MaxLocalDepth)
                {
                    foreach (var source in LocalValueSources(local))
                    {
                        if (state.Values.TryGetValue((source, depth + 1), out var known))
                        {
                            values.AddRange(known);
                        }
                        else if (state.Active.Add(source))
                        {
                            var found = Texts(source, constantNames, depth + 1, state);
                            state.Active.Remove(source);
                            state.Values[(source, depth + 1)] = found;
                            values.AddRange(found);
                        }
                    }
                }

                return Capped(values);
            }

            default:
                return Array.Empty<string>();
        }
    }

    /// <summary>What one unit's own code says about a local name: whether it is declared there, its declared type, and the expressions assigned to it.</summary>
    private sealed class LocalInfo
    {
        public bool Declared { get; set; }

        public TypeSyntax? Type { get; set; }

        public List<ExpressionSyntax> Sources { get; } = new();
    }

    private static readonly ConditionalWeakTable<SyntaxNode, Dictionary<string, LocalInfo>> LocalIndexes = new();

    /// <summary>
    /// The locals of a unit's OWN code, by name, built once per unit. A name is declared by a variable declarator, an out / pattern /
    /// deconstruction designation or a foreach variable; its sources are the declarator's initializer and every plain
    /// <c>name = expr</c>.
    /// </summary>
    private static Dictionary<string, LocalInfo> LocalIndex(SyntaxNode scope) => LocalIndexes.GetValue(scope, BuildLocalIndex);

    private static Dictionary<string, LocalInfo> BuildLocalIndex(SyntaxNode scope)
    {
        var index = new Dictionary<string, LocalInfo>(StringComparer.Ordinal);
        LocalInfo Of(string name)
        {
            if (!index.TryGetValue(name, out var info))
            {
                index[name] = info = new LocalInfo();
            }

            return info;
        }

        foreach (var node in OwnNodes(scope))
        {
            switch (node)
            {
                case VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax { Parent: not BaseFieldDeclarationSyntax } declaration } declarator:
                {
                    var info = Of(declarator.Identifier.ValueText);
                    info.Declared = true;
                    info.Type ??= declaration.Type.IsVar ? null : declaration.Type;
                    if (declarator.Initializer is { } initializer)
                    {
                        info.Sources.Add(initializer.Value);
                    }

                    break;
                }

                case SingleVariableDesignationSyntax designation:
                    Of(designation.Identifier.ValueText).Declared = true;
                    break;
                case ForEachStatementSyntax forEach:
                    Of(forEach.Identifier.ValueText).Declared = true;
                    break;
                case AssignmentExpressionSyntax { Left: IdentifierNameSyntax left } assignment when assignment.IsKind(SyntaxKind.SimpleAssignmentExpression):
                    Of(left.Identifier.ValueText).Sources.Add(assignment.Right);
                    break;
            }
        }

        return index;
    }

    /// <summary>
    /// The expressions assigned to a local variable by name, searched in the unit that holds the use and then in the units around
    /// it (a lambda or local function captures its enclosing member's locals). The first unit that declares the name decides; a
    /// name no unit declares has no sources.
    /// </summary>
    private static IReadOnlyList<ExpressionSyntax> LocalValueSources(IdentifierNameSyntax use)
    {
        foreach (var scope in EnclosingScopes(use))
        {
            if (LocalIndex(scope).TryGetValue(use.Identifier.ValueText, out var info) && info.Declared)
            {
                return info.Sources;
            }
        }

        return Array.Empty<ExpressionSyntax>();
    }

    /// <summary>The declared type of a local variable (<c>Entity e;</c> or <c>Entity e = ...;</c>), or null when no unit around the use declares it with a type.</summary>
    private static TypeSyntax? LocalDeclaredType(IdentifierNameSyntax use)
    {
        foreach (var scope in EnclosingScopes(use))
        {
            if (LocalIndex(scope).TryGetValue(use.Identifier.ValueText, out var info) && info.Declared)
            {
                return info.Type;
            }
        }

        return null;
    }

    /// <summary>The unit that holds a node, then (for a lambda or local function) each unit around it up to the enclosing member.</summary>
    private static IEnumerable<SyntaxNode> EnclosingScopes(SyntaxNode node)
    {
        var current = node;
        while (true)
        {
            var unit = EnclosingUnit(current);
            yield return unit.Node;
            if (unit.Kind is not (LambdaKind or LocalFunctionKind))
            {
                yield break;
            }

            current = unit.Node;
        }
    }

    /// <summary>The nodes of a unit's OWN code: everything below it except the inside of a lambda or local function nested in it.</summary>
    private static IEnumerable<SyntaxNode> OwnNodes(SyntaxNode scope)
    {
        var pending = new Stack<SyntaxNode>(scope.ChildNodes());
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            yield return node;
            if (!IsNestedUnit(node))
            {
                foreach (var child in node.ChildNodes())
                {
                    pending.Push(child);
                }
            }
        }
    }

    private static IReadOnlyList<string> Product(IReadOnlyList<string> left, IReadOnlyList<string> right)
        => Capped(left.SelectMany(l => right.Select(r => l + r)));

    /// <summary>The distinct values, at most <see cref="MaxAlternatives"/>; when there are more, the contact targets are kept first.</summary>
    private static IReadOnlyList<string> Capped(IEnumerable<string> values)
    {
        var distinct = values.Distinct().ToList();
        return distinct.Count <= MaxAlternatives
            ? distinct
            : distinct.OrderByDescending(IsContactTarget).Take(MaxAlternatives).ToList();
    }

    /// <summary>The last identifier of a name, member access or qualified type: <c>A.B.C</c> gives <c>C</c>.</summary>
    private static string? RightmostName(SyntaxNode? node)
        => node switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            GenericNameSyntax generic => generic.Identifier.ValueText,
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            MemberBindingExpressionSyntax binding => binding.Name.Identifier.ValueText,
            QualifiedNameSyntax qualified => RightmostName(qualified.Right),
            AliasQualifiedNameSyntax alias => RightmostName(alias.Name),
            NullableTypeSyntax nullable => RightmostName(nullable.ElementType),
            _ => null,
        };

    private static bool IsContactTable(string text)
        => text.Equals("contact", StringComparison.OrdinalIgnoreCase) || text.Equals("contacts", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when the LAST path segment (before any <c>?query</c>) is <c>contacts</c>: <c>"contacts"</c>, <c>"/api/data/v9.2/contacts"</c>
    /// and <c>"api/data/v9.2/contacts?$select=contactid"</c> are creates' targets; <c>"contacts(" + id + ")"</c>,
    /// <c>".../contacts(id)/Microsoft.Dynamics.CRM.X"</c> and <c>"sprk_contacts"</c> are not.
    /// </summary>
    private static bool IsContactPath(string text)
    {
        var query = text.IndexOf('?', StringComparison.Ordinal);
        var path = query >= 0 ? text[..query] : text;
        return path[(path.LastIndexOf('/') + 1)..].TrimStart(Hole).Equals("contacts", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsContactTarget(string text) => IsContactTable(text) || IsContactPath(text);

    /// <summary>
    /// The names of the string constants in one file that hold a contact table or path: <c>const string X = "contacts";</c> (field or
    /// local) and <c>static readonly string X = "contact";</c>. Used across all server code: a constant is followed by NAME wherever
    /// it is referenced.
    /// </summary>
    internal static IReadOnlyList<string> ContactConstantNames(string code) => ContactConstantNames(Parse(code).GetRoot());

    internal static IReadOnlyList<string> ContactConstantNames(SyntaxNode root)
    {
        var names = new List<string>();
        foreach (var declarator in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (declarator.Parent is not VariableDeclarationSyntax declaration
                || !IsStringType(declaration.Type)
                || declarator.Initializer is not { } initializer)
            {
                continue;
            }

            var modifiers = declaration.Parent switch
            {
                BaseFieldDeclarationSyntax field => field.Modifiers,
                LocalDeclarationStatementSyntax local => local.Modifiers,
                _ => default,
            };
            var isConstant = modifiers.Any(SyntaxKind.ConstKeyword)
                || (modifiers.Any(SyntaxKind.StaticKeyword) && modifiers.Any(SyntaxKind.ReadOnlyKeyword));
            if (isConstant && Texts(initializer.Value, Array.Empty<string>()).Any(IsContactTarget))
            {
                names.Add(declarator.Identifier.ValueText);
            }
        }

        return names;
    }

    private static bool IsStringType(TypeSyntax type)
        => type is PredefinedTypeSyntax predefined
            ? predefined.Keyword.IsKind(SyntaxKind.StringKeyword)
            : RightmostName(type) == "String";

    // ── Create shapes ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// True when the expression can be <c>HttpMethod.Post</c>: the member itself, either arm of a <c>?:</c> or <c>??</c>, or a
    /// local that is assigned one of those (same depth limit and cycle guard as <see cref="Texts(ExpressionSyntax, IReadOnlyCollection{string})"/>).
    /// </summary>
    private static bool IsHttpPost(ExpressionSyntax expression, int depth, Resolution state)
    {
        switch (expression)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                return IsHttpPost(parenthesized.Expression, depth, state);
            case ConditionalExpressionSyntax conditional:
                return IsHttpPost(conditional.WhenTrue, depth, state) || IsHttpPost(conditional.WhenFalse, depth, state);
            case BinaryExpressionSyntax coalesce when coalesce.IsKind(SyntaxKind.CoalesceExpression):
                return IsHttpPost(coalesce.Left, depth, state) || IsHttpPost(coalesce.Right, depth, state);
            case MemberAccessExpressionSyntax member:
                return member.Name.Identifier.ValueText == "Post" && RightmostName(member.Expression) == "HttpMethod";
            case IdentifierNameSyntax local when depth < MaxLocalDepth:
            {
                foreach (var source in LocalValueSources(local))
                {
                    if (state.Posts.TryGetValue((source, depth + 1), out var known))
                    {
                        if (known)
                        {
                            return true;
                        }
                    }
                    else if (state.Active.Add(source))
                    {
                        var found = IsHttpPost(source, depth + 1, state);
                        state.Active.Remove(source);
                        state.Posts[(source, depth + 1)] = found;
                        if (found)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            default:
                return false;
        }
    }

    private static bool PostsToContacts(IReadOnlyList<ExpressionSyntax> arguments, IReadOnlyCollection<string> names)
    {
        var state = new Resolution();
        var isPost = arguments.Select(a => IsHttpPost(a, 0, state)).ToList();
        return isPost.Contains(true)
            && arguments.Where((a, i) => !isPost[i]).Any(a => Texts(a, names).Any(IsContactPath));
    }

    private static string? MethodName(ExpressionSyntax expression)
        => expression switch
        {
            IdentifierNameSyntax or GenericNameSyntax or MemberAccessExpressionSyntax or MemberBindingExpressionSyntax => RightmostName(expression),
            _ => null,
        };

    /// <summary>
    /// <c>new Entity(...)</c>, or a target-typed <c>new(...)</c> whose declared type is <c>Entity</c>: the initializer of
    /// <c>Entity e = new("contact")</c> or the right side of <c>e = new("contact")</c> where <c>e</c> is a local of that type.
    /// </summary>
    private static bool IsSdkEntityCreation(BaseObjectCreationExpressionSyntax creation)
        => creation switch
        {
            ObjectCreationExpressionSyntax explicitType => RightmostName(explicitType.Type) == "Entity",
            ImplicitObjectCreationExpressionSyntax implicitType => implicitType.Parent switch
            {
                EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax declaration } }
                    => RightmostName(declaration.Type) == "Entity",
                AssignmentExpressionSyntax { Left: IdentifierNameSyntax target } assignment when assignment.Right == implicitType
                    => LocalDeclaredType(target) is { } type && RightmostName(type) == "Entity",
                _ => false,
            },
            _ => false,
        };

    /// <summary>The create shape a node is, or null. One node is at most one shape.</summary>
    private static Shape? CreateShape(SyntaxNode node, IReadOnlyCollection<string> names)
    {
        switch (node)
        {
            case InvocationExpressionSyntax invocation:
            {
                var arguments = invocation.ArgumentList.Arguments.Select(a => a.Expression).ToList();
                if (PostsToContacts(arguments, names))
                {
                    return Shape.HttpPost;
                }

                var method = MethodName(invocation.Expression);
                if (method == "CreateAsync" && arguments.Any(a => Texts(a, names).Any(IsContactTarget)))
                {
                    return Shape.WebApiCreateAsync;
                }

                return method is not null && PostMethodName.IsMatch(method) && arguments.Any(a => Texts(a, names).Any(IsContactPath))
                    ? Shape.UserClientPost
                    : null;
            }

            case BaseObjectCreationExpressionSyntax creation:
            {
                var arguments = creation.ArgumentList?.Arguments.Select(a => a.Expression).ToList() ?? new List<ExpressionSyntax>();
                if (PostsToContacts(arguments, names))
                {
                    return Shape.HttpPost;
                }

                if (IsSdkEntityCreation(creation))
                {
                    var byConstructor = arguments.Count > 0 && Texts(arguments[0], names).Any(IsContactTable);
                    var byInitializer = creation.Initializer?.Expressions.OfType<AssignmentExpressionSyntax>()
                        .Any(a => RightmostName(a.Left) == "LogicalName" && Texts(a.Right, names).Any(IsContactTable)) ?? false;
                    return byConstructor || byInitializer ? Shape.SdkEntity : null;
                }

                return null;
            }

            case TupleExpressionSyntax tuple:
                return PostsToContacts(tuple.Arguments.Select(a => a.Expression).ToList(), names) ? Shape.HttpPost : null;

            default:
                return null;
        }
    }

    // ── Enclosing member and owner evidence ─────────────────────────────────────────────────────────

    /// <summary>The unit of code a create is judged against: what it is, and its name (null for a lambda or a top-level statement).</summary>
    private readonly record struct Unit(SyntaxNode Node, string Kind, string? Name);

    private const string MemberKind = "member";
    private const string LocalFunctionKind = "local function";
    private const string LambdaKind = "lambda";
    private const string StatementKind = "statement";

    private static bool IsNestedUnit(SyntaxNode node) => node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax;

    /// <summary>
    /// The unit that encloses a node: the NEAREST of a lambda or anonymous method, a local function, a method / constructor /
    /// operator, a property or event accessor, a property (expression body or initializer), a field's variable initializer; with
    /// none of those, the top-level statement.
    /// </summary>
    private static Unit EnclosingUnit(SyntaxNode node)
    {
        foreach (var ancestor in node.Ancestors())
        {
            switch (ancestor)
            {
                case AnonymousFunctionExpressionSyntax:
                    return new Unit(ancestor, LambdaKind, null);
                case LocalFunctionStatementSyntax local:
                    return new Unit(ancestor, LocalFunctionKind, local.Identifier.ValueText);
                case MethodDeclarationSyntax method:
                    return new Unit(ancestor, MemberKind, method.Identifier.ValueText);
                case ConstructorDeclarationSyntax constructor:
                    return new Unit(ancestor, MemberKind, constructor.Identifier.ValueText);
                case BaseMethodDeclarationSyntax other:
                    return new Unit(other, MemberKind, null);
                case AccessorDeclarationSyntax accessor:
                    return new Unit(ancestor, MemberKind, accessor.Parent?.Parent switch
                    {
                        PropertyDeclarationSyntax property => property.Identifier.ValueText,
                        EventDeclarationSyntax @event => @event.Identifier.ValueText,
                        _ => null,
                    });
                case PropertyDeclarationSyntax property:
                    return new Unit(ancestor, MemberKind, property.Identifier.ValueText);
                case IndexerDeclarationSyntax:
                    return new Unit(ancestor, MemberKind, "this[]");
                case VariableDeclaratorSyntax { Parent.Parent: BaseFieldDeclarationSyntax } field:
                    return new Unit(ancestor, MemberKind, field.Identifier.ValueText);
            }
        }

        return new Unit(
            node.AncestorsAndSelf().OfType<GlobalStatementSyntax>().FirstOrDefault() ?? node.SyntaxTree.GetRoot(), StatementKind, null);
    }

    /// <summary>
    /// Owner evidence in a unit: a string token (literal, verbatim, raw, interpolated text) containing <c>ownerid@odata.bind</c> or
    /// equal to <c>ownerid</c>, or the identifier <c>OwnerBindKey</c> / <c>OwnerAttribute</c> (the identity store's key constant and the SDK
    /// attribute name). Comments are trivia, not tokens, so they are never evidence. The unit's OWN code only: the body of a lambda or
    /// local function nested in it is a unit of its own and is not evidence for the unit around it.
    /// </summary>
    private static bool HasOwnerEvidence(SyntaxNode unit)
    {
        foreach (var child in unit.ChildNodesAndTokens())
        {
            if (child.AsNode() is { } node)
            {
                if (!IsNestedUnit(node) && HasOwnerEvidence(node))
                {
                    return true;
                }
            }
            else if (IsOwnerToken(child.AsToken()))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsOwnerToken(SyntaxToken token)
        => token.Kind() switch
        {
            SyntaxKind.StringLiteralToken or SyntaxKind.SingleLineRawStringLiteralToken or SyntaxKind.MultiLineRawStringLiteralToken
                or SyntaxKind.InterpolatedStringTextToken
                => token.ValueText.Contains("ownerid@odata.bind", StringComparison.OrdinalIgnoreCase)
                    || token.ValueText.Equals("ownerid", StringComparison.OrdinalIgnoreCase),
            SyntaxKind.IdentifierToken => token.ValueText is "OwnerBindKey" or "OwnerAttribute",
            _ => false,
        };

    // ── The allow-list and the seams the scan cannot see ────────────────────────────────────────────

    /// <summary>A known contact-create seam: where it is, and why it is allowed (or invisible to the scan).</summary>
    internal sealed record ContactCreateSeam(string File, string MemberDeclaration, string Reason);

    /// <summary>
    /// An allow-listed contact create: ONE declared member of ONE type in ONE file. Matched on the enclosing type's name and the
    /// member's name as the syntax gives them, and only for the member itself - a lambda or local function inside it is a unit of its own.
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

    // ── The scan ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// One contact create found in a file: its line, which shape, whether its unit binds an owner, the enclosing type's name, the
    /// unit's name (null when it has none), and what kind of unit it is ("member", "local function", "lambda", "statement").
    /// </summary>
    internal readonly record struct ContactCreateHit(int Line, string Shape, bool Owned, string? TypeName, string? MemberName, string Unit);

    internal static SyntaxTree Parse(string code) => CSharpSyntaxTree.ParseText(code, new CSharpParseOptions(LanguageVersion.Latest));

    /// <summary>
    /// Every contact create in one server file (any of the shapes), with whether its unit binds an owner. The unit is the nearest
    /// enclosing member, accessor, initializer, local function or lambda (<see cref="EnclosingUnit"/>); the owner must be bound in
    /// THAT unit's own code (<see cref="HasOwnerEvidence"/>). For a create in a lambda the lambda's own body is the evidence scope,
    /// and the member around it does not stand in for it (the stricter of "the lambda, then the member" and "the lambda alone"): a
    /// payload built outside a lambda and captured by it is not seen as owned, so bind the owner inside the lambda.
    /// </summary>
    internal static IReadOnlyList<ContactCreateHit> ContactCreates(
        string relativePath, string code, IReadOnlyCollection<string>? constantNames = null)
        => ContactCreates(relativePath, Parse(code).GetRoot(), constantNames);

    internal static IReadOnlyList<ContactCreateHit> ContactCreates(
        string relativePath, SyntaxNode root, IReadOnlyCollection<string>? constantNames = null)
    {
        var hits = new List<ContactCreateHit>();
        if (!relativePath.StartsWith(ServerRoot, StringComparison.Ordinal))
        {
            return hits;
        }

        var names = constantNames ?? Array.Empty<string>();
        foreach (var node in root.DescendantNodes())
        {
            if (CreateShape(node, names) is not { } shape)
            {
                continue;
            }

            var unit = EnclosingUnit(node);
            hits.Add(new ContactCreateHit(
                node.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                shape.ToString(),
                HasOwnerEvidence(unit.Node),
                node.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText,
                unit.Name,
                unit.Kind));
        }

        return hits;
    }

    /// <summary>True only for a create inside an allow-listed seam's own declaration: the same file, type name and member name.</summary>
    private static bool IsAllowedSeam(string relativePath, ContactCreateHit hit)
        => hit.Unit == MemberKind
            && AllowedSeams.Any(a => a.File == relativePath
                && string.Equals(a.TypeName, hit.TypeName, StringComparison.Ordinal)
                && string.Equals(a.MemberName, hit.MemberName, StringComparison.Ordinal));

    /// <summary>
    /// The contact creates in one server file that bind no owner and are not an allow-listed seam: "line N (shape)" for each. A
    /// contact created through the Web API without an owner is owned by the calling application user in the root business unit
    /// (ISS-005). The scan reads syntax, not types; what it sees and what it does not:
    /// <list type="bullet">
    /// <item>Seen: <c>CreateAsync</c>, <c>HttpMethod.Post</c> next to a contacts path, <c>Post*Async</c> to a contacts path, and
    /// <c>new Entity("contact")</c> (also target-typed, <c>Entity e = new("contact")</c>) - which is how
    /// <c>IGenericEntityService.CreateAsync(Entity)</c>, <c>IOrganizationService.Create</c> and
    /// <c>new CreateRequest { Target = new Entity("contact") }</c> are seen. The table or path may be a literal, an interpolated or
    /// concatenated string, a constant (followed by name), or a local variable: its initializer and its plain assignments in the
    /// unit (or the unit around a lambda), through <c>new Uri(...)</c>, to <see cref="MaxLocalDepth"/> hops; a local introduced by
    /// <c>out var</c>, a pattern, a deconstruction or a foreach counts as declared, so its later plain assignments are followed.
    /// <c>HttpMethod.Post</c> may be held the same way (a local, either arm of a <c>?:</c>).</item>
    /// <item>Not seen: a table name that is a runtime value (<c>dataverse.create_record</c>'s <c>tablename</c>,
    /// <c>ChildRecordEndpoints</c>; <see cref="UndetectableSeams"/> names the known ones).</item>
    /// <item>K1 residue, exactly what the local following misses: a field or a parameter holding the target; a local assigned
    /// only with <c>+=</c>; the value a designation-introduced local receives where it is introduced (<c>out var</c>, pattern,
    /// deconstruction, foreach); a chain of more than <see cref="MaxLocalDepth"/> hops; a value wrapped in a cast or a call
    /// (<c>(string)$"..."</c>, <c>new Uri(...).ToString()</c>, <c>string.Format</c>, <c>string.Concat</c>); a method that is
    /// <c>new HttpMethod("POST")</c> or <c>new HttpRequestMessage { Method = ... }</c> (initializer style); a <c>$batch</c> body;
    /// <c>nameof</c>; a property-backed constant; a target-typed <c>new("contact")</c> that is not a variable's initializer or a
    /// local's assignment (inside an object initializer such as <c>new CreateRequest { Target = new("contact") }</c>, an argument,
    /// a return). Past <see cref="MaxAlternatives"/> alternatives a prefix that only becomes a contacts path after a later <c>+</c>
    /// can be dropped (a 520-arm <c>?:</c> ending in <c>"api/"</c>, then <c>PostAsync(p + "contacts")</c>). A create reached only
    /// through a cycle between locals can be missed, depending on operand order.</item>
    /// <item>K2: owner evidence is matched by NAME in the unit's own code and does not prove the value is a resolved team. An
    /// ownerless create passes when the same unit names <c>ownerid</c> in a ColumnSet, an array or a log string, or uses
    /// <c>Attributes.Remove(OwnerAttribute)</c> or <c>["ownerid@odata.bind"] = null</c>. A payload built in ANOTHER member (or
    /// another lambda / local function) and passed in is flagged: build it where it is created. A retry-lambda wrapper
    /// (<c>Retry(() =&gt; client.CreateAsync(...))</c> with the owner bound outside the lambda) is a false positive under the
    /// nested-unit rule: bind the owner inside the lambda.</item>
    /// <item>K3: constants are followed by NAME across all server code, so an unrelated constant of the same name can only add
    /// false positives, never hide a create.</item>
    /// </list>
    /// </summary>
    internal static IReadOnlyList<string> OwnerlessContactCreates(
        string relativePath, string code, IReadOnlyCollection<string>? constantNames = null)
        => ContactCreates(relativePath, code, constantNames)
            .Where(h => !h.Owned && !IsAllowedSeam(relativePath, h))
            .Select(h => $"line {h.Line} ({h.Shape})")
            .ToList();

    /// <summary>
    /// Lines of code the parser skips (an inactive <c>#if</c> branch) that mention a contact: the scan reads only active code, so a
    /// create there would be invisible. The server tree has none.
    /// </summary>
    internal static IReadOnlyList<int> SkippedContactRegions(SyntaxNode root)
        => root.DescendantTrivia(descendIntoTrivia: true)
            .Where(t => t.IsKind(SyntaxKind.DisabledTextTrivia) && t.ToString().Contains("contact", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.GetLocation().GetLineSpan().StartLinePosition.Line + 1)
            .ToList();

    private sealed record ServerFile(string Path, SyntaxNode Root);

    private static readonly Lazy<IReadOnlyList<ServerFile>> ServerTree = new(() =>
        SourceScan.ServerSourceFiles()
            .Select(f => new ServerFile(SourceScan.Relative(f).Replace('\\', '/'), Parse(File.ReadAllText(f)).GetRoot()))
            .ToList());

    private static IReadOnlyList<string> AllContactConstantNames(IEnumerable<ServerFile> files)
        => files.SelectMany(f => ContactConstantNames(f.Root)).Distinct().ToList();

    [Fact(DisplayName = "ISS-005: no contact create in server code is ownerless (CreateAsync, HttpMethod.Post, Post*Async, new Entity)")]
    public void EveryContactCreate_InServerCode_SetsAnOwner_OrIsAnAllowListedSeam()
    {
        var files = ServerTree.Value;
        var names = AllContactConstantNames(files);
        var hits = files.SelectMany(f => ContactCreates(f.Path, f.Root, names).Select(h => (f.Path, Hit: h))).ToList();

        Assert.True(hits.Count > 0, "the scan saw no contact create at all (the invite create, the store request): the guard is vacuous");
        var offenders = hits.Where(h => !h.Hit.Owned && !IsAllowedSeam(h.Path, h.Hit)).Select(h => $"{h.Path}: line {h.Hit.Line} ({h.Hit.Shape})").ToList();
        Assert.True(
            offenders.Count == 0,
            "A contact created with no ownerid is owned by the BFF's application user in the root business unit (ISS-005, #1673). "
            + "Resolve the owner team (ContactIdentityBinder.ResolveCreateOwnerAsync) and bind it, or - for a create that is owned "
            + "by construction elsewhere - add an AllowedSeams entry with the reason. Offending creates:\n" + string.Join("\n", offenders));
    }

    [Fact(DisplayName = "ISS-005 guard: no server file has skipped (#if-disabled) code that mentions a contact")]
    public void NoServerFile_HasSkippedCode_MentioningAContact()
    {
        var offenders = ServerTree.Value
            .SelectMany(f => SkippedContactRegions(f.Root).Select(line => $"{f.Path}: line {line}"))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "The create guard reads only active code. A contact create inside an inactive #if branch would not be seen:\n"
            + string.Join("\n", offenders));
    }

    [Fact(DisplayName = "ISS-005 guard, non-vacuous: every allow-listed seam is still matched by a real file, and the invite create is owned")]
    public void EveryAllowedSeam_IsStillExercised_ByARealFile()
    {
        var files = ServerTree.Value;
        var names = AllContactConstantNames(files);
        foreach (var seam in AllowedSeams)
        {
            var file = files.Single(f => f.Path == seam.File);
            Assert.True(
                ContactCreates(file.Path, file.Root, names).Any(h => IsAllowedSeam(file.Path, h)),
                $"allow-listed seam {seam.File} / {seam.TypeName}.{seam.MemberName} no longer matches a contact create: remove or update the entry.");
        }

        // The invite create is the other real create, and it is owned (not a seam).
        var invite = files.Single(f => f.Path == InviteCreateFile);
        Assert.Contains(ContactCreates(invite.Path, invite.Root, names), h => h.Owned && h.Shape == nameof(Shape.WebApiCreateAsync));
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

    [Fact(DisplayName = "ISS-005 guard, must-fire: an expression-bodied ownerless member after an owned member holding a plain \"{\" literal is caught")]
    public void Control_AnExpressionBodiedOwnerlessMember_AfterABraceLiteral_IsCaught()
        => Assert.Single(OwnerlessContactCreates(BffRoot + "Services/X/Makes.cs", """
            public sealed class Maker
            {
                async Task<Guid> OwnedAsync(DataverseWebApiClient client, Guid team, CancellationToken ct)
                {
                    var open = "{";
                    var payload = new Dictionary<string, object?> { ["ownerid@odata.bind"] = $"/teams({team:D})", ["x"] = open };
                    return await client.CreateAsync("contacts", payload, ct);
                }

                Task<Guid> OwnerlessAsync(DataverseWebApiClient client, CancellationToken ct)
                    => client.CreateAsync("contacts", new { lastname = "x" }, ct);
            }
            """));

    // ── Literal and comment shapes a text scanner cannot divide into members (P6 to P9) ──────────────

    [Fact(DisplayName = "ISS-005 guard, must-fire: an expression-bodied ownerless member after an owned member holding a URL literal in an initializer line is caught")]
    public void Control_AnExpressionBodiedOwnerlessMember_AfterAUrlLiteral_IsCaught()
        => Assert.Single(OwnerlessContactCreates(BffRoot + "Services/X/Makes.cs", """
            public sealed class Maker
            {
                async Task<Guid> OwnedAsync(DataverseWebApiClient client, Guid team, CancellationToken ct)
                {
                    var options = new Options { Origin = new Uri("https://example.com/x") };
                    var payload = new Dictionary<string, object?> { ["ownerid@odata.bind"] = $"/teams({team:D})", ["o"] = options };
                    return await client.CreateAsync("contacts", payload, ct);
                }

                Task<Guid> OwnerlessAsync(DataverseWebApiClient c, CancellationToken ct) => c.CreateAsync("contacts", new { lastname = "x" }, ct);
            }
            """));

    [Fact(DisplayName = "ISS-005 guard, must-fire: an ownerless SDK contact entity is caught when a later member binds an owner and an earlier line holds a URL literal")]
    public void Control_AnOwnerlessSdkEntity_BeforeAnOwnedMember_IsCaught()
        => Assert.Single(OwnerlessContactCreates(BffRoot + "Services/X/Makes.cs", """
            public sealed class Maker
            {
                Task<Guid> Ownerless(IGenericEntityService entities, CancellationToken ct)
                {
                    var options = new Options { Origin = new Uri("https://example.com/x") };
                    var entity = new Entity("contact");
                    return entities.CreateAsync(entity, ct);
                }

                Task<Guid> Owned(IGenericEntityService entities, Guid team, CancellationToken ct)
                {
                    var entity = new Entity("contact");
                    entity["ownerid"] = new EntityReference("team", team);
                    return entities.CreateAsync(entity, ct);
                }
            }
            """));

    [Fact(DisplayName = "ISS-005 guard, must-fire: an expression-bodied ownerless member after an owned member holding an interpolation-hole literal is caught")]
    public void Control_AnExpressionBodiedOwnerlessMember_AfterAnInterpolationHoleLiteral_IsCaught()
        => Assert.Single(OwnerlessContactCreates(BffRoot + "Services/X/Makes.cs", """
            public sealed class Maker
            {
                async Task<Guid> OwnedAsync(DataverseWebApiClient client, Guid team, bool o, CancellationToken ct)
                {
                    var note = $"{(o ? "{" : "")}";
                    var payload = new Dictionary<string, object?> { ["ownerid@odata.bind"] = $"/teams({team:D})", ["n"] = note };
                    return await client.CreateAsync("contacts", payload, ct);
                }

                Task<Guid> OwnerlessAsync(DataverseWebApiClient c, CancellationToken ct) => c.CreateAsync("contacts", new { lastname = "x" }, ct);
            }
            """));

    [Fact(DisplayName = "ISS-005 guard, must-fire: an expression-bodied ownerless member after an owned member holding a brace inside a block comment is caught")]
    public void Control_AnExpressionBodiedOwnerlessMember_AfterABlockCommentBrace_IsCaught()
        => Assert.Single(OwnerlessContactCreates(BffRoot + "Services/X/Makes.cs", """
            public sealed class Maker
            {
                async Task<Guid> OwnedAsync(DataverseWebApiClient client, Guid team, CancellationToken ct)
                {
                    /* a note with an opening brace { in it */
                    var payload = new Dictionary<string, object?> { ["ownerid@odata.bind"] = $"/teams({team:D})" };
                    return await client.CreateAsync("contacts", payload, ct);
                }

                Task<Guid> OwnerlessAsync(DataverseWebApiClient c, CancellationToken ct) => c.CreateAsync("contacts", new { lastname = "x" }, ct);
            }
            """));

    [Fact(DisplayName = "ISS-005 guard, must-fire: a method with a where constraint, an expression-bodied local function and a comment naming the bind are each their own unit")]
    public void Control_WhereConstraints_ExpressionBodiedLocalFunctions_AndComments_AreJudgedAlone()
    {
        Assert.Single(OwnerlessContactCreates(BffRoot + "Services/X/Makes.cs", """
            public sealed class Maker
            {
                Task<Guid> OwnedAsync<T>(DataverseWebApiClient client, Guid team, CancellationToken ct) where T : class
                {
                    var payload = new Dictionary<string, object?> { ["ownerid@odata.bind"] = $"/teams({team:D})" };
                    return client.CreateAsync("contacts", payload, ct);
                }

                Task<Guid> OwnerlessAsync<T>(DataverseWebApiClient client, CancellationToken ct) where T : class
                    => client.CreateAsync("contacts", new { lastname = "x" }, ct);
            }
            """));

        var local = Assert.Single(ContactCreates(BffRoot + "Services/X/Makes.cs", """
            public sealed class Maker
            {
                public Task<Guid> RunAsync(DataverseWebApiClient client, Guid team, CancellationToken ct)
                {
                    var owner = new Dictionary<string, object?> { ["ownerid@odata.bind"] = $"/teams({team:D})" };
                    Task<Guid> Ownerless() => client.CreateAsync("contacts", new { lastname = "x" }, ct);
                    return Ownerless();
                }
            }
            """));
        Assert.False(local.Owned);
        Assert.Equal("local function", local.Unit);

        // A comment is trivia, not a token: it is never owner evidence.
        Assert.Single(OwnerlessContactCreates(BffRoot + "Services/X/Makes.cs", """
            public sealed class Maker
            {
                Task<Guid> OwnerlessAsync(DataverseWebApiClient client, CancellationToken ct)
                {
                    // ["ownerid@odata.bind"] = "/teams(x)"
                    /* "ownerid" */
                    return client.CreateAsync("contacts", new { lastname = "x" }, ct);
                }
            }
            """));
    }

    [Fact(DisplayName = "ISS-005 guard, must-fire: a create in a lambda is judged by the lambda's own body, not by the member around it")]
    public void Control_ALambdaCreate_IsJudgedByTheLambdasOwnBody()
    {
        // The enclosing method binds an owner elsewhere (and in another lambda): the lambda that creates binds none.
        Assert.Single(OwnerlessContactCreates(BffRoot + "Services/X/Makes.cs", """
            public sealed class Maker
            {
                async Task<Guid> RunAsync(DataverseWebApiClient client, Guid team, CancellationToken ct)
                {
                    var owned = new Dictionary<string, object?> { ["ownerid@odata.bind"] = $"/teams({team:D})" };
                    var make = async () => { return await client.CreateAsync("contacts", new { lastname = "x" }, ct); };
                    var other = () => new Dictionary<string, object?> { ["ownerid@odata.bind"] = $"/teams({team:D})" };
                    return await make();
                }
            }
            """));

        // An ownerless create in the enclosing method is not excused by an owner bound only inside a lambda.
        Assert.Single(OwnerlessContactCreates(BffRoot + "Services/X/Makes.cs", """
            public sealed class Maker
            {
                async Task<Guid> RunAsync(DataverseWebApiClient client, Guid team, CancellationToken ct)
                {
                    var other = () => new Dictionary<string, object?> { ["ownerid@odata.bind"] = $"/teams({team:D})" };
                    return await client.CreateAsync("contacts", new { lastname = "x" }, ct);
                }
            }
            """));
    }

    [Fact(DisplayName = "ISS-005 guard: a property accessor, a property initializer and a field initializer are units of their own")]
    public void Control_Accessors_AndInitializers_AreUnitsOfTheirOwn()
    {
        Assert.Single(OwnerlessContactCreates(BffRoot + "Services/X/Makes.cs", """
            public sealed class Maker
            {
                public Task<Guid> Owner { get => OwnedAsync(); }
                public Task<Guid> Bad { get { return client.CreateAsync("contacts", new { lastname = "x" }, ct); } }
            }
            """));
        Assert.Single(OwnerlessContactCreates(BffRoot + "Services/X/Makes.cs", """
            public sealed class Maker
            {
                private static readonly Func<Task<Guid>> Make = () => client.CreateAsync("contacts", new { lastname = "x" }, ct);
                public Task<Guid> Owned => client.CreateAsync("contacts", new Dictionary<string, object?> { ["ownerid@odata.bind"] = "/teams(x)" }, ct);
            }
            """));
        Assert.Empty(OwnerlessContactCreates(BffRoot + "Services/X/Makes.cs", """
            public sealed class Maker
            {
                public Task<Guid> Good { get { return client.CreateAsync("contacts", new Dictionary<string, object?> { ["ownerid@odata.bind"] = "/teams(x)" }, ct); } }
            }
            """));
    }

    [Fact(DisplayName = "ISS-005 guard: skipped (#if-disabled) code that mentions a contact is reported")]
    public void Control_SkippedCodeMentioningAContact_IsReported()
    {
        const string skipped = """
            public sealed class Maker
            {
            #if NEVER_DEFINED
                Task<Guid> Hidden(DataverseWebApiClient client, CancellationToken ct) => client.CreateAsync("contacts", new { }, ct);
            #endif
            }
            """;
        Assert.Single(SkippedContactRegions(Parse(skipped).GetRoot()));
        Assert.Empty(SkippedContactRegions(Parse("public sealed class Maker { }").GetRoot()));
        Assert.Empty(SkippedContactRegions(Parse("#if NEVER_DEFINED\nint unrelated;\n#endif").GetRoot()));
    }

    [Fact(DisplayName = "ISS-005 guard, must-fire: raw, concatenated, conditional and constant forms of the contacts name are caught")]
    public void Control_RawConcatenatedConditionalAndLocalConstantForms_AreCaught()
    {
        Assert.Single(OwnerlessContactCreates(BffRoot + "Services/X/Makes.cs", "private Task MakeAsync() { return client.CreateAsync(\"\"\"contacts\"\"\", payload, ct); }"));
        Assert.Single(OwnerlessContactCreates(BffRoot + "Services/X/Makes.cs", "private Task MakeAsync() { return client.CreateAsync(\"con\" + \"tacts\", payload, ct); }"));
        Assert.Single(OwnerlessContactCreates(BffRoot + "Services/X/Makes.cs", "private Task MakeAsync() { return client.CreateAsync(flag ? \"accounts\" : \"contacts\", payload, ct); }"));
        Assert.Single(OwnerlessContactCreates(BffRoot + "Services/X/Makes.cs", "private Task MakeAsync() { return client.PostAsync($\"\"\"{root}/api/data/v9.2/contacts\"\"\", body, ct); }"));
        Assert.Single(OwnerlessContactCreates(BffRoot + "Services/X/Makes.cs", "private Task MakeAsync() { return client.PostAsync(root + \"/contacts\", body, ct); }"));

        const string withLocalConstant = "private Task MakeAsync() { const string table = \"contacts\"; return client.CreateAsync(table, payload, ct); }";
        var names = ContactConstantNames(withLocalConstant);
        Assert.Equal(new[] { "table" }, names);
        Assert.Single(OwnerlessContactCreates(BffRoot + "Services/X/Makes.cs", withLocalConstant, names));
    }

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
                    var make = async () =>
                    {
                        var owned = new Dictionary<string, object?> { ["ownerid@odata.bind"] = $"/teams({team:D})" };
                        return await client.CreateAsync("contacts", owned, ct);
                    };
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
    [InlineData("return await u.PostAsync(\"api/data/v9.2/contacts?$select=contactid\", body, ct);")]
    [InlineData("return await userClient.PostAsync($\"{baseUrl}/contacts?$select=contactid\", body, ct);")]
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
    [InlineData("return await userClient.PostAsync(\"/api/data/v9.2/contacts(\" + id + \")?$select=fullname\", body, ct);")]
    [InlineData("return await userClient.PostAsync(\"/api/data/v9.2/accounts?$select=name\", body, ct);")]
    [InlineData("return await userClient.PostAsync(\"/api/data/v9.2/contacts/$count\", body, ct);")]
    [InlineData("var e = new Entity($\"account\");")]
    public void Control_OtherTargets_UnderTheNewLiteralForms_HaveNoViolation(string statement)
        => Assert.Empty(ContactCreates(BffRoot + "Services/X/Makes.cs", "private Task MakeAsync() { " + statement + " }"));

    // ── UAC round 5 F3-a: the table or path held in a local variable ─────────────────────────────────

    [Theory(DisplayName = "ISS-005 guard, must-fire: a contacts path or table held in a local variable is followed into the create")]
    [InlineData("var url = $\"{GetApiUrl()}/contacts\"; using var request = new HttpRequestMessage(HttpMethod.Post, url);", "HttpPost")]
    [InlineData("var uri = new Uri($\"{GetApiUrl()}/contacts\"); return http.SendAsync(new HttpRequestMessage(HttpMethod.Post, uri), ct);", "HttpPost")]
    [InlineData("var url = $\"{GetApiUrl()}/contacts\"; return http.PostAsync(url, body, ct);", "UserClientPost")]
    [InlineData("var set = \"contacts\"; return client.CreateAsync(set, payload, ct);", "WebApiCreateAsync")]
    [InlineData("var table = \"contact\"; var e = new Entity(table);", "SdkEntity")]
    [InlineData("return new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, \"contacts\"));", "HttpPost")]
    [InlineData("return new HttpRequestMessage(HttpMethod.Post, new Uri(\"contacts\", UriKind.Relative));", "HttpPost")]
    [InlineData("var a = \"contacts\"; var b = a; var c = b; return client.CreateAsync(c, payload, ct);", "WebApiCreateAsync")]
    [InlineData("var url = $\"{GetApiUrl()}/accounts\"; url = $\"{GetApiUrl()}/contacts\"; return http.PostAsync(url, body, ct);", "UserClientPost")]
    [InlineData("var path = flag ? \"accounts\" : \"contacts\"; var url = $\"{root}/{path}\"; return http.PostAsync(url, body, ct);", "UserClientPost")]
    [InlineData("var make = () => { var set = \"contacts\"; return client.CreateAsync(set, payload, ct); }; return make();", "WebApiCreateAsync")]
    [InlineData("var set = \"contacts\"; var make = () => client.CreateAsync(set, payload, ct); return make();", "WebApiCreateAsync")]
    [InlineData("Entity e = new(\"contact\");", "SdkEntity")]
    [InlineData("Entity e = new() { LogicalName = \"contact\" };", "SdkEntity")]
    [InlineData("Entity e; e = new(\"contact\");", "SdkEntity")]
    [InlineData("Microsoft.Xrm.Sdk.Entity? e = new(\"contact\");", "SdkEntity")]
    [InlineData("var request = new CreateRequest { Target = new Entity(\"contact\") }; return org.ExecuteAsync(request);", "SdkEntity")]
    [InlineData("return org.Create(new Entity(\"contact\"));", "SdkEntity")]
    public void Control_ALocalHoldingTheContactsTarget_IsFollowed(string statement, string shape)
    {
        var hit = Assert.Single(ContactCreates(BffRoot + "Services/X/Makes.cs", "private Task MakeAsync() { " + statement + " }"));
        Assert.Equal(shape, hit.Shape);
        Assert.False(hit.Owned);
    }

    [Theory(DisplayName = "ISS-005 guard, must-not-fire: a local holding another table, a record URL or a GET passes; so do cycles and unknown targets")]
    [InlineData("var url = $\"{GetApiUrl()}/accounts\"; return http.PostAsync(url, body, ct);")]
    [InlineData("var url = $\"{GetApiUrl()}/accounts\"; using var request = new HttpRequestMessage(HttpMethod.Post, url);")]
    [InlineData("var url = $\"{GetApiUrl()}/accounts\"; url = $\"{GetApiUrl()}/contacts({id})\"; return new HttpRequestMessage(HttpMethod.Post, url);")]
    [InlineData("var url = $\"{GetApiUrl()}/contacts\"; return new HttpRequestMessage(HttpMethod.Get, url);")]
    [InlineData("var url = $\"{GetApiUrl()}/contacts\"; return http.GetAsync(url, ct);")]
    [InlineData("var set = \"accounts\"; return client.CreateAsync(set, payload, ct);")]
    [InlineData("var a = b + \"x\"; var b = a + \"y\"; return client.CreateAsync(a, payload, ct);")]
    [InlineData("return client.CreateAsync(unknownLocal, payload, ct);")]
    [InlineData("Entity e = new(\"account\"); return org.Create(e);")]
    [InlineData("Other e = new(\"contact\");")]
    public void Control_ALocalHoldingAnotherTarget_IsNotFlagged(string statement)
        => Assert.Empty(ContactCreates(BffRoot + "Services/X/Makes.cs", "private Task MakeAsync() { " + statement + " }"));

    [Fact(DisplayName = "ISS-005 guard: a local is not followed past the depth limit, and a local of ANOTHER member is not followed")]
    public void Control_LocalFollowing_IsBounded_AndPerUnit()
    {
        Assert.Empty(ContactCreates(BffRoot + "Services/X/Makes.cs",
            "private Task MakeAsync() { var a = \"contacts\"; var b = a; var c = b; var d = c; var e = d; var f = e; return client.CreateAsync(f, payload, ct); }"));
        Assert.Empty(ContactCreates(BffRoot + "Services/X/Makes.cs", """
            public sealed class Maker
            {
                private Task A() { var url = "contacts"; return Task.CompletedTask; }
                private Task B(string url) { return http.PostAsync(url, body, ct); }
            }
            """));
    }

    // ── UAC round 5 follow-up: designation-introduced locals, a held HttpMethod, bounded work ────────

    [Theory(DisplayName = "ISS-005 guard, must-fire: locals introduced by out var / pattern / deconstruction / foreach, and a held HttpMethod.Post, are followed")]
    [InlineData("Fill(out var url); url = $\"{GetApiUrl()}/contacts\"; return http.PostAsync(url, body, ct);", "UserClientPost")]
    [InlineData("if (value is string url) { url = $\"{GetApiUrl()}/contacts\"; return http.PostAsync(url, body, ct); }", "UserClientPost")]
    [InlineData("var (url, other) = Pair(); url = $\"{GetApiUrl()}/contacts\"; return http.PostAsync(url, body, ct);", "UserClientPost")]
    [InlineData("var m = HttpMethod.Post; return WriteAsync(m, \"contacts\", payload, null, ct);", "HttpPost")]
    [InlineData("var m = isNew ? HttpMethod.Post : HttpMethod.Patch; return WriteAsync(m, \"contacts\", payload, null, ct);", "HttpPost")]
    [InlineData("var m = isNew ? HttpMethod.Patch : (flag ? HttpMethod.Post : HttpMethod.Delete); return new HttpRequestMessage(m, $\"{GetApiUrl()}/contacts\");", "HttpPost")]
    [InlineData("HttpMethod m; m = HttpMethod.Post; return WriteAsync(m, \"contacts\", payload, null, ct);", "HttpPost")]
    [InlineData("var m = HttpMethod.Post; var n = m; return WriteAsync(n, \"contacts\", payload, null, ct);", "HttpPost")]
    public void Control_DesignationLocals_AndAHeldPostMethod_AreFollowed(string statement, string shape)
    {
        var hit = Assert.Single(ContactCreates(BffRoot + "Services/X/Makes.cs", "private Task MakeAsync() { " + statement + " }"));
        Assert.Equal(shape, hit.Shape);
        Assert.False(hit.Owned);
    }

    [Theory(DisplayName = "ISS-005 guard, must-not-fire: a designation local holding another path, an unknown value, and a held method that is not Post pass")]
    [InlineData("Fill(out var url); url = $\"{GetApiUrl()}/accounts\"; return http.PostAsync(url, body, ct);")]
    [InlineData("if (value is string url) { return http.PostAsync(url, body, ct); }")]
    [InlineData("var (url, other) = Pair(); return http.PostAsync(url, body, ct);")]
    [InlineData("foreach (var url in urls) { return http.PostAsync(url, body, ct); }")]
    [InlineData("var m = isNew ? HttpMethod.Patch : HttpMethod.Delete; return WriteAsync(m, \"contacts\", payload, null, ct);")]
    [InlineData("var m = HttpMethod.Get; return new HttpRequestMessage(m, $\"{GetApiUrl()}/contacts\");")]
    [InlineData("var m = HttpMethod.Post; return WriteAsync(m, \"accounts\", payload, null, ct);")]
    [InlineData("return WriteAsync(unknownMethod, \"contacts\", payload, null, ct);")]
    public void Control_DesignationLocals_AndAHeldMethod_ThatAreNotACreate_AreNotFlagged(string statement)
        => Assert.Empty(ContactCreates(BffRoot + "Services/X/Makes.cs", "private Task MakeAsync() { " + statement + " }"));

    [Fact(DisplayName = "ISS-005 guard: 60 assignments to one local resolve in bounded time, and the contacts one among them is still found")]
    public void Control_ManyAssignmentsToOneLocal_ResolveInBoundedTime()
    {
        var lines = string.Join(" ", Enumerable.Range(0, 60).Select(i => $"u = u + \"/p{i}\";"));
        string Code(string extra) => "private Task MakeAsync() { var u = GetApiUrl(); " + lines + extra + " return http.PostAsync(u, body, ct); }";

        var clock = Stopwatch.StartNew();
        Assert.Empty(ContactCreates(BffRoot + "Services/X/Makes.cs", Code(string.Empty)));
        Assert.Single(ContactCreates(BffRoot + "Services/X/Makes.cs", Code(" u = u + \"/contacts\";")));
        clock.Stop();

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"resolving 60 assignments took {clock.Elapsed}: the local following is not bounded");
    }

    // ── UAC round 6: the resolution's depth, cap and null-coalescing arms ─────────────────────────────

    [Fact(DisplayName = "ISS-005 guard, must-fire: a source reached at two depths is resolved at each (memo keyed by source AND depth)")]
    public void Control_ASourceReachedAtTwoDepths_IsResolvedAtEach()
    {
        // {a} reaches the "/contacts" local at the depth limit (unreadable there); {d} reaches the same local one hop in.
        var hit = Assert.Single(ContactCreates(BffRoot + "Services/X/Makes.cs", """
            private Task MakeAsync()
            {
                var e = "/contacts"; var d = e; var c = d; var b = c; var a = b;
                return http.PostAsync($"{a}{d}", body, ct);
            }
            """));
        Assert.Equal(nameof(Shape.UserClientPost), hit.Shape);
    }

    [Fact(DisplayName = "ISS-005 guard, must-fire: a contacts target past the alternatives cap is kept (contact targets first)")]
    public void Control_AContactsArm_PastTheAlternativesCap_IsKept()
    {
        var arms = Enumerable.Range(0, MaxAlternatives + 8).Select(i => $"c{i} ? \"a{i}\" : ");
        var code = "private Task MakeAsync() { var p = " + string.Concat(arms) + "\"contacts\"; return client.CreateAsync(p, payload, ct); }";

        var hit = Assert.Single(ContactCreates(BffRoot + "Services/X/Makes.cs", code));
        Assert.Equal(nameof(Shape.WebApiCreateAsync), hit.Shape);
    }

    [Fact(DisplayName = "ISS-005 guard, must-fire: HttpMethod.Post on the right of ?? is a create; on neither side it is not")]
    public void Control_APostOnTheRightOfACoalesce_IsCaught()
    {
        Assert.Single(ContactCreates(BffRoot + "Services/X/Makes.cs",
            "private Task MakeAsync() { return WriteAsync(verb ?? HttpMethod.Post, \"contacts\", payload, null, ct); }"));
        Assert.Single(ContactCreates(BffRoot + "Services/X/Makes.cs",
            "private Task MakeAsync() { return WriteAsync(HttpMethod.Post ?? verb, \"contacts\", payload, null, ct); }"));
        Assert.Empty(ContactCreates(BffRoot + "Services/X/Makes.cs",
            "private Task MakeAsync() { return WriteAsync(verb ?? HttpMethod.Get, \"contacts\", payload, null, ct); }"));
    }

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
        // Comments are trivia and a literal is not an identifier: the raw text, comments included, has no violation.
        const string code = """
            var owner = ContactCreateOwner.FromCustomerBusinessUnit(options, configuration);
            // ContactCreateOwner.NoneForForeignEnvironment("x") is for the factory only
            /// <see cref="ContactCreateOwner.NoneForForeignEnvironment"/>
            var text = "ContactCreateOwner.NoneForForeignEnvironment";
            """;

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

    [Fact(DisplayName = "ISS-005 guard, must-fire: a use after a URL literal (a slash pair inside a string) is caught")]
    public void Control_AUseAfterAUrlLiteral_IsCaught()
        => Assert.Single(Violations(OtherFile,
            "var binder = new ContactIdentityBinder(http, new Uri(\"https://x\"), ContactCreateOwner.NoneForForeignEnvironment(\"own\"));"));

    [Fact(DisplayName = "ISS-005 guard, must-fire: a brace inside a literal in the factory does not widen the allowed region")]
    public void Control_ABraceLiteralInTheFactory_DoesNotWidenTheAllowedRegion()
    {
        var code = BinderFileShape.Replace(
                "public ContactIdentityBinder CreateBinder",
                "private static readonly string Open = \"{\";\n    public ContactIdentityBinder CreateBinder",
                StringComparison.Ordinal)
            + "\npublic class LaterClass\n{\n    public object Make() => ContactCreateOwner.NoneForForeignEnvironment(\"later\");\n}\n";

        Assert.Contains("\"{\"", code, StringComparison.Ordinal);
        // The factory's own use passes; the use in the class after it is the only violation.
        Assert.Single(Violations(FactoryFile, code));
    }

    [Fact(DisplayName = "ISS-005 guard, must-not-fire: a use in a lambda inside the factory class passes")]
    public void Control_AUseInALambdaInTheFactory_HasNoViolation()
        => Assert.Empty(Violations(FactoryFile, """
            public class ContactIdentityBinderFactory
            {
                public Func<string, ContactCreateOwner> Make() => reason => ContactCreateOwner.NoneForForeignEnvironment(reason);
            }
            """));

    [Fact(DisplayName = "ISS-005 guard, must-fire: a factory class nested in another type, or declared twice (partial), does not allow the use")]
    public void Control_ANestedOrRepeatedFactoryClass_AllowsNothing()
    {
        Assert.NotEmpty(Violations(FactoryFile, """
            public class Wrapper
            {
                public class ContactIdentityBinderFactory
                {
                    public object Make() => ContactCreateOwner.NoneForForeignEnvironment("x");
                }
            }
            """));

        var repeated = Violations(FactoryFile, """
            public partial class ContactIdentityBinderFactory
            {
                public object Make() => ContactCreateOwner.NoneForForeignEnvironment("x");
            }

            public partial class ContactIdentityBinderFactory
            {
                public object Other() => ContactCreateOwner.NoneForForeignEnvironment("y");
            }
            """);
        Assert.Single(repeated);
        Assert.Contains("2 times", repeated[0], StringComparison.Ordinal);
    }

    [Fact(DisplayName = "ISS-005 guard, must-not-fire: the single top-level factory class passes inside a file-scoped or block namespace")]
    public void Control_TheFactory_InANamespace_HasNoViolation()
    {
        Assert.Empty(Violations(FactoryFile, """
            namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

            public class ContactIdentityBinderFactory
            {
                public object Make() => ContactCreateOwner.NoneForForeignEnvironment("x");
            }
            """));
        Assert.Empty(Violations(FactoryFile, """
            namespace Sprk.Bff.Api.Infrastructure.ExternalAccess
            {
                public class ContactIdentityBinderFactory
                {
                    public object Make() => ContactCreateOwner.NoneForForeignEnvironment("x");
                }
            }
            """));
    }

    [Fact(DisplayName = "ISS-005 guard, must-fire: a binder file with no factory class is caught, never vacuously green")]
    public void Control_NoFactoryClass_IsCaught()
        => Assert.Contains("vacuous", Assert.Single(Violations(FactoryFile, "public sealed class ContactIdentityBinder { }")));
}

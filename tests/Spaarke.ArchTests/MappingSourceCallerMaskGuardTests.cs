using System.Reflection;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// #2188 (field-mapping-server-write-path-r1 DI-004, UAC-r2 condition 4) — every source entity handed to the field-mapping
/// rule engine (<c>CreateTimeFieldMapping.Apply</c> / <c>ApplyFillOnly</c>) has first been masked to what the CALLER can read
/// by <c>MappingSourceCallerMask</c>.
/// </summary>
/// <remarks>
/// <para><b>Why a guard.</b> The mapping source is read app-only, and an app-only read ignores field-level security. The
/// Office quick-create copied a secured column the caller could not read into a record the caller could (#2188). The mask is
/// one call; forgetting it is silent and every test of the unmasked path stays green. A new engine caller — the unified
/// record-creation service's mapping step among them, when it merges — must reuse the mask, and this makes that a red build
/// rather than a review comment.</para>
///
/// <para><b>The rule.</b> At each call of <c>CreateTimeFieldMapping.Apply</c> / <c>ApplyFillOnly</c> under <c>src/server/**</c> —
/// qualified, fully qualified, through a <c>using X = …CreateTimeFieldMapping</c> alias or a <c>using static</c> import, with or
/// without a verbatim <c>@</c> — the <c>source</c> argument (third positional, or named <c>source:</c>) is either the literal
/// <c>null</c> / <c>default</c> (nothing read, nothing copied), or a plain local name. For a local name the rule is an
/// ALLOW-LIST per occurrence, decided on syntax nodes in the SAME member body (method, constructor or accessor; a local function is
/// judged as part of the member that contains it, so a variable it captures is traced; but a mask assignment inside a local function,
/// lambda or anonymous method does not count for a call outside it):
/// every occurrence of that name there must be (1) a declaration or a statement-level assignment of <c>null</c>, <c>default</c>
/// or exactly <c>(await )?MappingSourceCallerMask.ApplyAsync(…)</c> (optionally <c>.ConfigureAwait(false)</c>, nothing else);
/// (2) a null check; or (3) an engine call's source argument — and at least one assignment must be a mask call. Anything else (a
/// helper argument, an alias, a cast, a parameter, a lambda, pattern or query binding, a deconstruction, a member access, a
/// <c>ref</c>, a field, property or base-class member declared outside the body) fails, as does an expression source and the
/// engine's method taken as a method group (assigned, passed, <c>new Func&lt;…&gt;(…)</c>). A <c>global using</c> that names the
/// engine (an alias or <c>static</c>), in any file or in a project file's <c>&lt;Using&gt;</c> item
/// (<see cref="NoProjectFileImportsTheEngineGlobally"/>), is itself a violation: it would hide calls in every other file.</para>
/// <para><b>Parsed with Roslyn (a syntax tree); comments and string contents cannot hide or fake a call.</b> The sources are read
/// by <c>CSharpSyntaxTree.ParseText</c>, so a <c>//</c>, <c>/*</c>, quote or brace inside any kind of string or char literal,
/// or inside a comment, is never mistaken for code. A tree with syntax errors is still walked, node by node; real sources
/// compile, and a control that does not parse as a file is parsed as a script (statements and members at the top level).</para>
/// <para><b>Known limits</b> (accepted): the mask call's arguments are not checked. Fail-closed false positive:
/// <c>nameof(CreateTimeFieldMapping.Apply)</c> is flagged (a method group). A call made through REFLECTION
/// (<c>GetMethod("ApplyFillOnly").Invoke</c>) is not seen by any syntax scan; <see cref="TheEngine_OffersNoOtherPathThatTakesASourceEntity"/>
/// keeps the engine's own surface to the one guarded entry point, which is what makes a reflective call the only way round.
/// <c>Apply</c> (the overwrite entry point the Office creation core called) was deleted with that core by field mapping task
/// 018c; the scan still treats the name as an engine call, so restoring it is guarded by the same rule.</para>
///
/// <para>Paired with must-fire controls (a bypass in each shape fails) and must-not-fire controls (the masked Office shape, a
/// literal null, a call in a comment or a string pass), all fed through the same <see cref="Scan"/> as the real tree.</para>
/// </remarks>
public class MappingSourceCallerMaskGuardTests
{
    /// <summary>
    /// Files the scan skips, each with its reason. A caller that reads no source does NOT need an entry: passing a literal
    /// <c>null</c> source already satisfies the rule.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> Allowlist = new Dictionary<string, string>
    {
        ["src/server/api/Sprk.Bff.Api/Services/Dataverse/FieldMapping/CreateTimeFieldMapping.cs"] =
            "The engine itself: ApplyFillOnly is the guarded entry point, and it hands the source it was given to the private " +
            "ApplyAll — that is not a call site that reads a source.",
    };

    /// <summary>
    /// Unmasked engine calls tolerated PER MEMBER, keyed <c>"{repo-relative path}|{Type}.{Member}"</c>: how many, and why (the
    /// shape of <see cref="CreateOwnerResolutionGuardTests.Allowlist"/>). A call inside a local function, lambda or anonymous
    /// method counts for the declared member around it. Calls past <c>Uses</c> fail, and so does every unmasked call in any
    /// other member of the same file; an entry with fewer unmasked calls than <c>Uses</c>, a member that is gone or an empty
    /// reason fails <see cref="EveryUnmaskedCallExemption_IsStillUsed"/> (the ratchet).
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, (int Uses, string Reason)> UnmaskedCallExemptions =
        new Dictionary<string, (int Uses, string Reason)>
        {
            ["src/server/api/Sprk.Bff.Api/Services/RecordCreation/RecordCreationService.Repair.cs|RecordCreationService.RepairMappingAsync"] = (1,
                "Repair runs as the system principal with no caller to mask against (owner decision 2026-10-21, option 1; UAC-r2 " +
                "concurs). Secure sources are never mapped into a row that isn't isolated (Repair source gate, fail closed). " +
                "Field-secured source columns will be skipped by Repair using the app-only metadata reader from task 033 (TODO " +
                "task 033), failing closed when metadata is unavailable; no production caller until then " +
                "(RecordRepairReachabilityTests; 052 hard-blocked on 033); the document access and D7 link columns are refused through " +
                "RefuseAppOnlyWrite. Repair has no production caller until task 052, which is hard-blocked on 033 " +
                "(RecordRepairReachabilityTests)."),
        };

    /// <summary>
    /// The engine's simple type name, and the method names the scan treats as engine calls: its guarded entry point
    /// (<c>ApplyFillOnly</c>), and <c>Apply</c> — deleted with the Office creation core (task 018c), still scanned so a
    /// restored overwrite entry point cannot take an unmasked source unseen.
    /// </summary>
    private const string EngineType = "CreateTimeFieldMapping";

    private static readonly string[] EngineMethods = ["Apply", "ApplyFillOnly"];

    /// <summary>
    /// The engine call sites in the tree today (the unified record-creation service's step 3 and its Repair mode; the Office
    /// quick-create's own mapping step was deleted with the Office creation core, task 018c); the scan must see at least these.
    /// </summary>
    private const int KnownCallSites = 2;

    [Fact(DisplayName = "#2188: every source handed to the field-mapping engine was masked to the caller's view first")]
    public void EverySourceHandedToTheMappingEngine_PassedThroughTheCallerMask()
    {
        var offenders = new List<string>();
        var callSites = 0;

        foreach (var file in SourceScan.ServerSourceFiles())
        {
            var relative = SourceScan.Relative(file).Replace('\\', '/');
            if (Allowlist.ContainsKey(relative))
            {
                continue;
            }

            var scan = ScanCalls(File.ReadAllText(file));
            callSites += scan.Calls.Count;
            offenders.AddRange(Offenders(relative, scan, UnmaskedCallExemptions).Select(v => $"{relative}:{v}"));
        }

        Assert.True(callSites >= KnownCallSites,
            $"The scan found {callSites} call(s) of the field-mapping engine, fewer than the {KnownCallSites} known — it no longer sees them.");
        Assert.True(offenders.Count == 0,
            "A source entity reaches CreateTimeFieldMapping.Apply/ApplyFillOnly without MappingSourceCallerMask (#2188). The " +
            "source is read app-only, which ignores field-level security; mask it first:\n" +
            "  source = await MappingSourceCallerMask.ApplyAsync(callerClient, table, id, appRead, sourceColumns, logger, ct);\n" +
            "and pass that local (or a literal null). " + string.Join("\n", offenders));
    }

    [Fact(DisplayName = "#2188: every per-member unmasked-call exemption is still used, names a member that exists and gives a reason")]
    public void EveryUnmaskedCallExemption_IsStillUsed()
    {
        var stale = StaleExemptions(UnmaskedCallExemptions, relative =>
        {
            var path = Path.Combine(SourceScan.RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(path) ? File.ReadAllText(path) : null;
        });

        Assert.True(stale.Count == 0, "Fix or remove these UnmaskedCallExemptions entries (the ratchet):\n" + string.Join("\n", stale));
    }

    /// <summary>The synthetic file the exemption controls are judged as, and its one exempt member.</summary>
    private const string ControlPath = "control/Svc.cs";

    private static readonly IReadOnlyDictionary<string, (int Uses, string Reason)> ControlExemptions =
        new Dictionary<string, (int Uses, string Reason)> { [$"{ControlPath}|Svc.Exempt"] = (1, "control: one unmasked call") };

    public static TheoryData<string, string> ExemptionBypasses() => new()
    {
        {
            "a second unmasked call in the exempt member",
            """
            class Svc
            {
                void Exempt()
                {
                    CreateTimeFieldMapping.ApplyFillOnly(r, t, read.Record, p, w, l);
                    CreateTimeFieldMapping.Apply(r, t, read.Record, p, w, l);
                }
            }
            """
        },
        {
            "an unmasked call in a different member of the same file",
            """
            class Svc
            {
                void Exempt() { CreateTimeFieldMapping.ApplyFillOnly(r, t, read.Record, p, w, l); }
                void Other() { CreateTimeFieldMapping.ApplyFillOnly(r, t, read.Record, p, w, l); }
            }
            """
        },
        {
            "an unmasked call inside a lambda in a non-exempt member of the same file",
            """
            class Svc
            {
                void Exempt() { CreateTimeFieldMapping.ApplyFillOnly(r, t, read.Record, p, w, l); }
                void Other() { Action a = () => CreateTimeFieldMapping.Apply(r, t, appRead, p, w, l); a(); }
            }
            """
        },
    };

    [Theory(DisplayName = "#2188 exemption control: an unmasked call past the member's allowance, or outside the exempt member, fires")]
    [MemberData(nameof(ExemptionBypasses))]
    public void TheExemption_DoesNotCoverMoreThanItsOccurrences(string shape, string snippet)
        => Assert.True(Offenders(ControlPath, ScanCalls(snippet, SourceCodeKind.Script), ControlExemptions).Count > 0,
            $"The guard did not fire on: {shape}.");

    public static TheoryData<string, string> ExemptionSanctioned() => new()
    {
        {
            "exactly one unmasked call in the exempt member",
            """
            class Svc
            {
                void Exempt() { CreateTimeFieldMapping.ApplyFillOnly(r, t, read.Record, p, w, l); }
                void Other() { CreateTimeFieldMapping.ApplyFillOnly(r, t, null, p, w, l); }
            }
            """
        },
        {
            "exactly one unmasked call, in a local function of the exempt member (counted for that member)",
            """
            class Svc
            {
                void Exempt()
                {
                    void Map() => CreateTimeFieldMapping.ApplyFillOnly(r, t, read.Record, p, w, l);
                    Map();
                }
            }
            """
        },
    };

    [Theory(DisplayName = "#2188 exemption control: the exempt member's allowed occurrence does not fire")]
    [MemberData(nameof(ExemptionSanctioned))]
    public void TheExemption_CoversItsOccurrence(string shape, string snippet)
    {
        var offenders = Offenders(ControlPath, ScanCalls(snippet, SourceCodeKind.Script), ControlExemptions);
        Assert.True(offenders.Count == 0, $"The guard fired on the exempt shape: {shape}: {string.Join("; ", offenders)}");
    }

    [Fact(DisplayName = "#2188 exemption control: an entry naming a member with no unmasked call, a gone member or no reason is stale")]
    public void AStaleExemption_IsReported()
    {
        const string source = """
            class Svc
            {
                void Exempt() { CreateTimeFieldMapping.ApplyFillOnly(r, t, read.Record, p, w, l); }
                void Masked() { CreateTimeFieldMapping.ApplyFillOnly(r, t, null, p, w, l); }
            }
            """;
        var entries = new Dictionary<string, (int Uses, string Reason)>
        {
            [$"{ControlPath}|Svc.Exempt"] = (1, "control: used"),
            [$"{ControlPath}|Svc.Masked"] = (1, "control: the member has zero unmasked calls"),
            [$"{ControlPath}|Svc.Gone"] = (1, "control: no such member"),
            [$"{ControlPath}|Svc.Exempt "] = (1, "control: malformed member name"),
            ["control/Missing.cs|Svc.Exempt"] = (1, "control: no such file"),
        };
        var noReason = new Dictionary<string, (int Uses, string Reason)> { [$"{ControlPath}|Svc.Exempt"] = (1, " ") };

        string? Read(string path) => path == ControlPath ? source : null;
        var stale = StaleExemptions(entries, Read);

        Assert.Equal(4, stale.Count);
        Assert.Contains(stale, s => s.StartsWith($"{ControlPath}|Svc.Masked:", StringComparison.Ordinal));
        Assert.Contains(stale, s => s.StartsWith($"{ControlPath}|Svc.Gone:", StringComparison.Ordinal));
        Assert.Contains(stale, s => s.StartsWith("control/Missing.cs|Svc.Exempt:", StringComparison.Ordinal));
        Assert.DoesNotContain(stale, s => s.StartsWith($"{ControlPath}|Svc.Exempt:", StringComparison.Ordinal));
        Assert.Single(StaleExemptions(noReason, Read));
    }

    /// <summary>
    /// The project-file twin of a <c>global using</c>: a <c>&lt;Using Include="…CreateTimeFieldMapping" /&gt;</c> item (plain, aliased
    /// or <c>Static="true"</c>) in any project file, <c>Directory.Build.props</c> or <c>.targets</c> that applies to
    /// <c>src/server/**</c> imports the engine into every file of the project, which the per-file scan cannot see.
    /// </summary>
    [Fact(DisplayName = "#2188: no project file imports the field-mapping engine for every file")]
    public void NoProjectFileImportsTheEngineGlobally()
    {
        var files = Directory.EnumerateFiles(Path.Combine(SourceScan.RepoRoot, "src", "server"), "*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".csproj", StringComparison.Ordinal) || f.EndsWith(".props", StringComparison.Ordinal)
                         || f.EndsWith(".targets", StringComparison.Ordinal))
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Concat(new[] { "Directory.Build.props", "Directory.Build.targets" }
                .Select(n => Path.Combine(SourceScan.RepoRoot, n)).Where(File.Exists))
            .ToList();

        Assert.NotEmpty(files);
        var offenders = files.SelectMany(f => UsingItemViolations(File.ReadAllText(f)).Select(v => $"{SourceScan.Relative(f)}: {v}")).ToList();
        Assert.True(offenders.Count == 0,
            "A project file imports CreateTimeFieldMapping into every file (a <Using> item), which hides its calls from the mask guard: "
            + string.Join("\n", offenders));
    }

    public static TheoryData<string, string> UsingItemBypasses() => new()
    {
        { "an aliased <Using> item", """<Project><ItemGroup><Using Include="Sprk.Bff.Api.Services.Dataverse.FieldMapping.CreateTimeFieldMapping" Alias="Engine" /></ItemGroup></Project>""" },
        { "a static <Using> item", """<Project><ItemGroup><Using Include="Sprk.Bff.Api.Services.Dataverse.FieldMapping.CreateTimeFieldMapping" Static="true" /></ItemGroup></Project>""" },
        { "a <Using> item changed by Update", """<Project><ItemGroup><Using Update="Sprk.Bff.Api.Services.Dataverse.FieldMapping.CreateTimeFieldMapping" Static="true" /></ItemGroup></Project>""" },
        { "a props file that is not well-formed XML but names the engine", """<Project><Using Include="X.CreateTimeFieldMapping" """ },
    };

    [Theory(DisplayName = "#2188 guard control: a project-file <Using> of the engine fires")]
    [MemberData(nameof(UsingItemBypasses))]
    public void TheProjectFileCheckFires(string shape, string xml)
        => Assert.True(UsingItemViolations(xml).Count > 0, $"The project-file check did not fire on: {shape}.");

    public static TheoryData<string, string> UsingItemSanctioned() => new()
    {
        { "an unrelated <Using> item", """<Project><ItemGroup><Using Include="System.Linq" /></ItemGroup></Project>""" },
        { "the engine named only in a comment", """<Project><!-- <Using Include="X.CreateTimeFieldMapping" /> --><ItemGroup /></Project>""" },
        { "the engine named in another element", """<Project><PropertyGroup><Description>CreateTimeFieldMapping</Description></PropertyGroup></Project>""" },
    };

    [Theory(DisplayName = "#2188 guard control: unrelated project-file content does not fire")]
    [MemberData(nameof(UsingItemSanctioned))]
    public void TheProjectFileCheckDoesNotFire(string shape, string xml)
        => Assert.True(UsingItemViolations(xml).Count == 0, $"The project-file check fired on: {shape}.");

    /// <summary>Each <c>&lt;Using&gt;</c> item in <paramref name="xml"/> that names the engine; a file that does not parse and mentions it counts.</summary>
    private static List<string> UsingItemViolations(string xml)
    {
        try
        {
            return XDocument.Parse(xml).Descendants()
                .Where(e => e.Name.LocalName == "Using")
                .Where(e => new[] { "Include", "Update" }.Select(a => (string?)e.Attribute(a))
                    .Any(v => v is not null && v.Split(';').Any(part => part.Trim().Split('.').Last() == EngineType)))
                .Select(e => $"<Using> item '{e}' imports {EngineType} for every file")
                .ToList();
        }
        catch (XmlException)
        {
            return xml.Contains(EngineType, StringComparison.Ordinal)
                ? [$"a project file that does not parse as XML mentions {EngineType}"]
                : [];
        }
    }

    /// <summary>
    /// The scan guards the entry point, so it must be the only way in: no OTHER non-private method of the engine takes
    /// anything that could carry a source — a parameter that is, or is assignable to or from, an
    /// <see cref="Microsoft.Xrm.Sdk.Entity"/>, an <see cref="Microsoft.Xrm.Sdk.AttributeCollection"/>, an <c>IEnumerable</c> of either,
    /// or an <c>IReadOnlyDictionary&lt;string, object&gt;</c> (which also catches <c>object</c> and an open generic). A third entry
    /// point would take a source the scan never looks at. Reflection, not text, so a rename or a formatting trick cannot hide it.
    /// Not covered: properties and fields holding a source, a method's return type, nested types, and calls made by reflection.
    /// </summary>
    [Fact(DisplayName = "#2188: CreateTimeFieldMapping's only non-private method that can take a source is ApplyFillOnly")]
    public void TheEngine_OffersNoOtherPathThatTakesASourceEntity()
    {
        var engine = typeof(Program).Assembly.GetType("Sprk.Bff.Api.Services.Dataverse.FieldMapping.CreateTimeFieldMapping");
        Assert.NotNull(engine);

        var carriers = new[]
        {
            typeof(Microsoft.Xrm.Sdk.Entity),
            typeof(Microsoft.Xrm.Sdk.AttributeCollection),
            typeof(IEnumerable<Microsoft.Xrm.Sdk.Entity>),
            typeof(IEnumerable<Microsoft.Xrm.Sdk.AttributeCollection>),
            typeof(IReadOnlyDictionary<string, object>),
        };

        var nonPrivate = engine!
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsPrivate)
            .ToArray();

        Assert.Contains(nonPrivate, m => m.Name == "ApplyFillOnly");

        // Task 018c: Apply (the overwrite entry point) is gone; were it restored, it is one more way in and is reported here.
        var others = nonPrivate
            .Where(m => m.Name is not "ApplyFillOnly")
            .Where(m => m.GetParameters().Any(p =>
                p.ParameterType.IsGenericParameter
                || carriers.Any(c => c.IsAssignableFrom(p.ParameterType) || p.ParameterType.IsAssignableFrom(c))))
            .Select(m => m.Name)
            .Distinct()
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.True(others.Length == 0,
            "CreateTimeFieldMapping offers a way in besides ApplyFillOnly that can take a source: " + string.Join(", ", others));
    }

    // ── controls ──────────────────────────────────────────────────────────────────────────────────────────────────

    public static TheoryData<string, string> Bypasses() => new()
    {
        {
            "the app read passed straight in",
            """
            var source = await _entities.RetrieveAsync(table, id, columns, ct);
            CreateTimeFieldMapping.Apply(rules, target, source, protectedAttributes, warnings, _logger);
            """
        },
        {
            "masked, then overwritten by the app read",
            """
            Entity? source = null;
            source = await MappingSourceCallerMask.ApplyAsync(caller, table, id, appRead, columns, _logger, ct);
            source ??= appRead;
            CreateTimeFieldMapping.Apply(rules, target, source, protectedAttributes, warnings, _logger);
            """
        },
        {
            "an expression as the source",
            """
            CreateTimeFieldMapping.ApplyFillOnly(rules, target, await ReadSourceAsync(ct), protectedAttributes, warnings, _logger);
            """
        },
        {
            "a member of an unmasked read",
            """
            var read = await MappingSourceReadProbe.ReadAsync(reader, source, columns, _entities, _logger, ct);
            CreateTimeFieldMapping.ApplyFillOnly(rules, target, read.Record, protectedAttributes, warnings, _logger);
            """
        },
        {
            "a parameter as the source",
            """
            private void Map(IEnumerable<FieldMappingRuleEntity> rules, Entity target, Entity? source)
            {
                CreateTimeFieldMapping.Apply(rules, target, source, Protected, new List<string>(), _logger);
            }
            """
        },
        {
            "a named source argument",
            """
            CreateTimeFieldMapping.Apply(rules: rules, target: target, warnings: w, logger: l,
                protectedAttributes: p, source: appRead);
            """
        },
        {
            "(review pass 1, F3 a) the mask's answer coalesced with the app read",
            """
            Entity? source = null;
            source = await MappingSourceCallerMask.ApplyAsync(caller, t, id, appRead, cols, _logger, ct) ?? appRead;
            CreateTimeFieldMapping.Apply(rules, target, source, p, w, l);
            """
        },
        {
            "(review pass 2, F3 minor) the mask call followed by more than ConfigureAwait(false)",
            """
            var source = await MappingSourceCallerMask.ApplyAsync(caller, t, id, appRead, cols, _logger, ct).ConfigureAwait(false) switch { null => appRead, var m => m };
            CreateTimeFieldMapping.Apply(rules, target, source, p, w, l);
            """
        },
        {
            "(review pass 2, F3) the masked local handed to a helper",
            """
            var source = await MappingSourceCallerMask.ApplyAsync(c, t, i, appRead, cols, _logger, ct);
            TopUp(source, appRead);
            CreateTimeFieldMapping.Apply(rules, target, source, p, w, l);
            """
        },
        {
            "(review pass 2, F3) an alias of the masked local written through",
            """
            var source = await MappingSourceCallerMask.ApplyAsync(c, t, i, appRead, cols, _logger, ct);
            var a = source; a!["sprk_fee"] = appRead["sprk_fee"];
            CreateTimeFieldMapping.Apply(rules, target, source, p, w, l);
            """
        },
        {
            "(review pass 2, F3) a cast of the masked local written through",
            """
            var source = await MappingSourceCallerMask.ApplyAsync(c, t, i, appRead, cols, _logger, ct);
            ((Entity)source!)["sprk_fee"] = appRead["sprk_fee"];
            CreateTimeFieldMapping.Apply(rules, target, source, p, w, l);
            """
        },
        {
            "(review pass 2, F3) a lambda parameter of the same name",
            """
            var source = await MappingSourceCallerMask.ApplyAsync(c, t, i, appRead, cols, _logger, ct);
            CreateTimeFieldMapping.Apply(rules, target, source, p, w, l);
            reads.ForEach(source => CreateTimeFieldMapping.ApplyFillOnly(rules, target, source, p, w, l));
            """
        },
        {
            "(review pass 2, F3) a parenthesised lambda parameter of the same name",
            """
            var source = await MappingSourceCallerMask.ApplyAsync(c, t, i, appRead, cols, _logger, ct);
            Func<Entity, int> f = (source) => { CreateTimeFieldMapping.ApplyFillOnly(rules, target, source, p, w, l); return 0; };
            """
        },
        {
            "(review pass 2, F3) an is { } pattern binding of the same name",
            """
            var source = await MappingSourceCallerMask.ApplyAsync(c, t, i, appRead, cols, _logger, ct);
            if (appRead is { } source) { CreateTimeFieldMapping.ApplyFillOnly(rules, target, source, p, w, l); }
            """
        },
        {
            "(review pass 2, F3) a case pattern binding of the same name",
            """
            var source = await MappingSourceCallerMask.ApplyAsync(c, t, i, appRead, cols, _logger, ct);
            switch (appRead) { case Entity source: CreateTimeFieldMapping.ApplyFillOnly(rules, target, source, p, w, l); break; }
            """
        },
        {
            "(review pass 2, F3) a deconstruction into the name",
            """
            Entity? source = null;
            source = await MappingSourceCallerMask.ApplyAsync(c, t, i, appRead, cols, _logger, ct);
            (source, n) = (appRead, 1);
            CreateTimeFieldMapping.Apply(rules, target, source, p, w, l);
            """
        },
        {
            "(review pass 2, F3) a query range variable of the same name",
            """
            var source = await MappingSourceCallerMask.ApplyAsync(c, t, i, appRead, cols, _logger, ct);
            CreateTimeFieldMapping.Apply(rules, target, source, p, w, l);
            var q = from source in reads select Do(source);
            """
        },
        {
            "(review pass 2, F3) a field of the same name written",
            """
            var source = await MappingSourceCallerMask.ApplyAsync(c, t, i, appRead, cols, _logger, ct);
            this.source = appRead;
            CreateTimeFieldMapping.Apply(rules, target, source, p, w, l);
            """
        },
        {
            "(review pass 1, F3 b) a second method that takes the source as a parameter",
            """
            async Task A()
            {
                Entity? source = null;
                source = await MappingSourceCallerMask.ApplyAsync(c, t, i, a, cols, _logger, ct);
                CreateTimeFieldMapping.Apply(rules, target, source, p, w, l);
            }
            void B(IEnumerable<FieldMappingRuleEntity> rules, Entity? source)
            {
                CreateTimeFieldMapping.ApplyFillOnly(rules, target, source, p, w, l);
            }
            """
        },
        {
            "(review pass 1, F3 c) the masked entity topped up with the app read",
            """
            var source = await MappingSourceCallerMask.ApplyAsync(c, t, i, appRead, cols, _logger, ct);
            source.Attributes.AddRange(appRead.Attributes);
            CreateTimeFieldMapping.Apply(rules, target, source, p, w, l);
            """
        },
        {
            "(review pass 1, F3 c) a column written back into the masked entity",
            """
            var source = await MappingSourceCallerMask.ApplyAsync(c, t, i, appRead, cols, _logger, ct);
            source!["sprk_fee"] = appRead["sprk_fee"];
            CreateTimeFieldMapping.Apply(rules, target, source, p, w, l);
            """
        },
        {
            "an engine imported with using static and called by its bare name",
            """
            using static Sprk.Bff.Api.Services.Dataverse.FieldMapping.CreateTimeFieldMapping;
            Apply(rules, target, appRead, p, w, l);
            """
        },
        {
            "(#2191 review, F a) the engine imported under a using alias and called through it",
            """
            using Engine = Sprk.Bff.Api.Services.Dataverse.FieldMapping.CreateTimeFieldMapping;
            Engine.Apply(rules, target, appRead, p, w, l);
            """
        },
        {
            "(UAC-r2 re-check) the engine's method named with a verbatim identifier",
            """
            var source = await _entities.RetrieveAsync(table, id, columns, ct);
            CreateTimeFieldMapping.@Apply(rules, target, source, protectedAttributes, warnings, _logger);
            """
        },
        {
            "(UAC-r2 re-check) the verbatim-identifier method taken as a method group",
            """
            f = CreateTimeFieldMapping.@ApplyFillOnly;
            f(rules, target, appRead, p, w, l);
            """
        },
        {
            "(UAC-r2 re-check) a block comment between the type, the dot and the method",
            """
            var source = await _entities.RetrieveAsync(table, id, columns, ct);
            CreateTimeFieldMapping /* masked */ . /* really */ ApplyFillOnly /* x */ (rules, target, source, protectedAttributes, warnings, _logger);
            """
        },
        {
            "(UAC-r2 re-check) a block comment inside the argument list, hiding the unmasked local",
            """
            var read = await _entities.RetrieveAsync(table, id, columns, ct);
            CreateTimeFieldMapping.Apply(rules, target, /* source */ read, protectedAttributes, warnings, _logger);
            """
        },
        {
            "(UAC-r2 re-check) a block comment that itself contains // does not hide the call after it",
            """
            var source = await _entities.RetrieveAsync(table, id, columns, ct);
            /* see https://example.test/mask */ CreateTimeFieldMapping.Apply(rules, target, source, p, w, l);
            """
        },
        {
            "(UAC-r2 re-check, F3 a) a verbatim string holding // then a string holding /* does not hide the call after them",
            """
            var re = @"^https?://x";
            var mime = "image/*";
            var source = await _entities.RetrieveAsync(table, id, columns, ct);
            CreateTimeFieldMapping.Apply(rules, target, source, p, w, l);
            """
        },
        {
            "(UAC-r2 re-check, F3 b) a // inside a regular string before a real call on the same line",
            """
            var url = "http://x"; CreateTimeFieldMapping.Apply(rules, target, appRead, p, w, l);
            """
        },
        {
            "(UAC-r2 re-check, F3 c) a \"*/\" string after a real block comment does not end a later comment early",
            """
            /* old */ var close = "*/";
            var open = "/*";
            CreateTimeFieldMapping.Apply(rules, target, appRead, p, w, l);
            """
        },
        {
            "(UAC-r2 re-check, F3 d) // and /* inside an interpolated string with a nested string in its hole",
            """
            var label = $"{(x ? "a//b" : "c")} /*";
            CreateTimeFieldMapping.Apply(rules, target, appRead, p, w, l);
            """
        },
        {
            "(UAC-r2 re-check, F3-1) a char literal quote inside an interpolation hole, a stray brace, then a call (as UAC wrote it)",
            """
            var a = $"{n.Trim('"')}"; } var u = "http://x"; CreateTimeFieldMapping.Apply(rules, target, appRead, p, w, l);
            """
        },
        {
            "(UAC-r2 re-check, F3-1) the same, well formed",
            """
            var a = $"{n.Trim('"')}"; var u = "http://x"; CreateTimeFieldMapping.Apply(rules, target, appRead, p, w, l);
            """
        },
        {
            "(UAC-r2 re-check, F3-1) the $@ variant with a stray brace and the call on the next line",
            """
            var a = $@"{n.Trim('"')}"; }
            CreateTimeFieldMapping.Apply(rules, target, appRead, p, w, l);
            """
        },
        {
            "(UAC-r2 re-check, F3-1) the $@ variant, well formed",
            """
            var a = $@"{n.Trim('"')}";
            CreateTimeFieldMapping.Apply(rules, target, appRead, p, w, l);
            """
        },
        {
            "(UAC-r2 re-check, F3-2) a verbatim string ending in a backslash, a // in a string, then a call",
            """
            var dir = @"C:	emp\"; var u = "http://x"; CreateTimeFieldMapping.Apply(rules, target, appRead, p, w, l);
            """
        },
        {
            "(UAC-r2 re-check, F3-2) a // inside a nested string in an interpolation hole, then a call",
            """
            var s = $"{(x ? "//" : "")}"; CreateTimeFieldMapping.Apply(rules, target, appRead, p, w, l);
            """
        },
        {
            "(UAC-r2 re-check, F3-3) a string that spells a mask assignment does not satisfy the allow-list for a base-class member",
            """
            class Svc : Base
            {
                async Task M()
                {
                    var doc = "x; Source = await MappingSourceCallerMask.ApplyAsync(a);";
                    CreateTimeFieldMapping.Apply(rules, target, Source, p, w, l);
                }
            }
            """
        },
        {
            "(UAC-r2 re-check) a source declared outside the member (a field) and used as the argument",
            """
            class Svc
            {
                private Entity? source;
                void M() { CreateTimeFieldMapping.Apply(rules, target, source, p, w, l); }
            }
            """
        },
        {
            "(UAC-r2 re-check) a source declared outside the member (a property of a partial) assigned elsewhere",
            """
            partial class Svc
            {
                Entity? Source { get; set; }
                async Task Read() { Source = await MappingSourceCallerMask.ApplyAsync(a); }
                void M() { CreateTimeFieldMapping.Apply(rules, target, Source, p, w, l); }
            }
            """
        },
        {
            "(UAC-r2 re-check, F3-1) a variable captured by a local function, assigned from the mask only under a condition",
            """
            class Svc
            {
                async Task M()
                {
                    var s = appRead;
                    async Task L()
                    {
                        if (flag) s = await MappingSourceCallerMask.ApplyAsync(c, t, i, appRead, cols, _logger, ct);
                        CreateTimeFieldMapping.Apply(r, t, s, p, w, l);
                    }
                }
            }
            """
        },
        {
            "(UAC-r2 re-check, F3-1) the same, with the engine called BEFORE the mask assignment in the local function",
            """
            class Svc
            {
                async Task M()
                {
                    var s = appRead;
                    async Task L()
                    {
                        CreateTimeFieldMapping.Apply(r, t, s, p, w, l);
                        s = await MappingSourceCallerMask.ApplyAsync(c, t, i, appRead, cols, _logger, ct);
                    }
                }
            }
            """
        },
        {
            "(UAC-r2 re-check) the mask only inside a local function that is never invoked, the call outside it",
            """
            class Svc
            {
                async Task M()
                {
                    Entity? s = null;
                    async Task L() { s = await MappingSourceCallerMask.ApplyAsync(c, t, i, appRead, cols, _logger, ct); }
                    CreateTimeFieldMapping.Apply(r, t, s, p, w, l);
                }
            }
            """
        },
        {
            "(UAC-r2 re-check) the mask only inside a local function that is invoked, the call outside it",
            """
            class Svc
            {
                async Task M()
                {
                    Entity? s = null;
                    async Task L() { s = await MappingSourceCallerMask.ApplyAsync(c, t, i, appRead, cols, _logger, ct); }
                    await L();
                    CreateTimeFieldMapping.Apply(r, t, s, p, w, l);
                }
            }
            """
        },
        {
            "(UAC-r2 re-check) the mask only inside a local function, declared from the raw read (as UAC wrote it), the call outside",
            """
            class Svc
            {
                async Task M()
                {
                    var s = appRead;
                    async Task L() { s = await MappingSourceCallerMask.ApplyAsync(c, t, i, appRead, cols, _logger, ct); }
                    CreateTimeFieldMapping.Apply(r, t, s, p, w, l);
                }
            }
            """
        },
        {
            "(UAC-r2 re-check) the mask only inside a lambda, the call outside it",
            """
            class Svc
            {
                async Task M()
                {
                    Entity? s = null;
                    Func<Task> f = async () => { s = await MappingSourceCallerMask.ApplyAsync(c, t, i, appRead, cols, _logger, ct); };
                    await f();
                    CreateTimeFieldMapping.Apply(r, t, s, p, w, l);
                }
            }
            """
        },
        {
            "(UAC-r2 re-check, F3-2) the engine named through a :: alias-qualified name",
            """
            using FM = Sprk.Bff.Api.Services.Dataverse.FieldMapping;
            FM::CreateTimeFieldMapping.Apply(r, t, raw, p, w, l);
            """
        },
        {
            "(UAC-r2 re-check, F3-3) a global using alias of the engine",
            """
            global using Engine = Sprk.Bff.Api.Services.Dataverse.FieldMapping.CreateTimeFieldMapping;
            """
        },
        {
            "(UAC-r2 re-check, F3-3) a global using static of the engine",
            """
            global using static Sprk.Bff.Api.Services.Dataverse.FieldMapping.CreateTimeFieldMapping;
            """
        },
        {
            "(UAC-r2 re-check, F3-4) a masked local reassigned from the app read before the call",
            """
            var s = await MappingSourceCallerMask.ApplyAsync(c, t, i, appRead, cols, _logger, ct);
            s = raw;
            CreateTimeFieldMapping.Apply(r, t, s, p, w, l);
            """
        },
        {
            "(#2191 review, F b) the engine's method taken as a method group and called through the delegate",
            """
            Action<IEnumerable<FieldMappingRuleEntity>, Entity, Entity?, IReadOnlySet<string>, List<string>, ILogger> f;
            f = CreateTimeFieldMapping.Apply;
            f(rules, target, appRead, p, w, l);
            """
        },
    };

    [Theory(DisplayName = "#2188 guard control: each bypass shape fires")]
    [MemberData(nameof(Bypasses))]
    public void TheGuardFires_OnEveryBypassShape(string shape, string snippet)
        => Assert.True(Scan(snippet, SourceCodeKind.Script).Violations.Count > 0, $"The guard did not fire on: {shape}.");

    public static TheoryData<string, string> SanctionedShapes() => new()
    {
        {
            "the Office quick-create shape: the app read masked into the local the engine receives",
            """
            Entity? source = null;
            if (sourceColumns.Count > 0)
            {
                Entity? appRead = null;
                appRead = await _entities.RetrieveAsync(sourceEntity, sourceRecordId, sourceColumns.ToArray(), ct)
                    .ConfigureAwait(false);
                if (appRead is not null)
                {
                    source = await MappingSourceCallerMask
                        .ApplyAsync(_callerClient, sourceEntity, sourceRecordId, appRead, sourceColumns, _logger, ct)
                        .ConfigureAwait(false);
                    if (source is null)
                    {
                        warnings.Add(SourceUnreadableWarning);
                    }
                }
            }
            CreateTimeFieldMapping.Apply(rules, target, source, protectedAttributes, warnings, _logger);
            """
        },
        {
            "no source read at all",
            """
            CreateTimeFieldMapping.ApplyFillOnly(rules, target, null, protectedAttributes, warnings, _logger);
            """
        },
        {
            "(UAC-r2 re-check) a verbatim identifier and block comments around a masked source",
            """
            var masked = await MappingSourceCallerMask.ApplyAsync(caller, table, id, appRead, columns, _logger, ct);
            CreateTimeFieldMapping /* the engine */ . @Apply(rules, target, /* masked */ masked, p, w, l);
            """
        },
        {
            "(UAC-r2 re-check) a block comment that mentions the engine does not count as a call",
            """
            /* old: CreateTimeFieldMapping.Apply(rules, target, appRead, p, w, l); */
            CreateTimeFieldMapping.ApplyFillOnly(rules, target, null, p, w, l);
            """
        },
        {
            "(UAC-r2 re-check, F3) a call inside a block comment, and one after //, stay ignored",
            """
            /* CreateTimeFieldMapping.Apply(rules, target, appRead, p, w, l); */
            // CreateTimeFieldMapping.Apply(rules, target, appRead, p, w, l);
            var note = "// not a comment, and CreateTimeFieldMapping stays a plain word"; // CreateTimeFieldMapping.Apply(rules, target, x, p, w, l);
            CreateTimeFieldMapping.ApplyFillOnly(rules, target, null, p, w, l);
            """
        },
        {
            "(UAC-r2 re-check) a call spelled inside a string literal is text, not a call",
            """
            var doc = "CreateTimeFieldMapping.Apply(rules, target, appRead, p, w, l);";
            var raw = @"CreateTimeFieldMapping.ApplyFillOnly(rules, target, appRead, p, w, l);";
            CreateTimeFieldMapping.ApplyFillOnly(rules, target, null, p, w, l);
            """
        },
        {
            "(UAC-r2 re-check) a masked local inside a member of a class",
            """
            class Svc
            {
                async Task M()
                {
                    Entity? source = null;
                    source = await MappingSourceCallerMask.ApplyAsync(c, t, i, appRead, cols, _logger, ct).ConfigureAwait(false);
                    if (source is null) { }
                    CreateTimeFieldMapping.Apply(rules, target, source, p, w, l);
                }
            }
            """
        },
        {
            "(UAC-r2 re-check) the mask and the call both inside the same local function",
            """
            class Svc
            {
                async Task M()
                {
                    async Task L()
                    {
                        Entity? s = null;
                        s = await MappingSourceCallerMask.ApplyAsync(c, t, i, appRead, cols, _logger, ct);
                        CreateTimeFieldMapping.Apply(r, t, s, p, w, l);
                    }
                    await L();
                }
            }
            """
        },
        {
            "(UAC-r2 re-check) the mask in the method body, the call in a local function inside it",
            """
            class Svc
            {
                async Task M()
                {
                    Entity? s = null;
                    s = await MappingSourceCallerMask.ApplyAsync(c, t, i, appRead, cols, _logger, ct);
                    void L() { CreateTimeFieldMapping.Apply(r, t, s, p, w, l); }
                    L();
                }
            }
            """
        },
        {
            "a named, masked source",
            """
            var masked = await MappingSourceCallerMask.ApplyAsync(caller, table, id, appRead, columns, _logger, ct);
            CreateTimeFieldMapping.Apply(rules, target, protectedAttributes: p, source: masked, warnings: w, logger: l);
            """
        },
    };

    [Theory(DisplayName = "#2188 guard control: the sanctioned shapes do not fire")]
    [MemberData(nameof(SanctionedShapes))]
    public void TheGuardDoesNotFire_OnTheSanctionedShapes(string shape, string snippet)
        => Assert.True(Scan(snippet, SourceCodeKind.Script).Violations.Count == 0,
            $"The guard fired on the sanctioned shape: {shape}: {string.Join("; ", Scan(snippet, SourceCodeKind.Script).Violations)}");

    // ── the detector ──────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The one entry point: parse <paramref name="text"/> and walk the tree for the engine's call sites and violations. The file
    /// loop (<see cref="SourceCodeKind.Regular"/>) and both control theories (<see cref="SourceCodeKind.Script"/>, so a snippet of
    /// statements and members parses without a wrapper) call it. A tree with syntax errors is walked as it stands.
    /// </summary>
    internal static (int CallSites, List<string> Violations) Scan(string text, SourceCodeKind kind = SourceCodeKind.Regular)
    {
        var scan = ScanCalls(text, kind);
        return (scan.Calls.Count, scan.Other.Concat(scan.Calls.SelectMany(c => c.Violations)).ToList());
    }

    /// <summary>One engine call: the declared member it counts for, its line, and what (if anything) leaves its source unmasked.</summary>
    internal sealed record EngineCall(string Member, int Line, IReadOnlyList<string> Violations)
    {
        public bool Unmasked => Violations.Count > 0;
    }

    /// <summary>A file's engine calls, its violations that belong to no call (a global using, a method group), and its members.</summary>
    internal sealed record CallScan(List<EngineCall> Calls, List<string> Other, IReadOnlySet<string> Members);

    /// <summary>
    /// The violations of one file: every non-call violation, and every unmasked call except up to <c>Uses</c> of them, in line
    /// order, in a member <paramref name="exemptions"/> names for <paramref name="path"/>.
    /// </summary>
    internal static List<string> Offenders(
        string path, CallScan scan, IReadOnlyDictionary<string, (int Uses, string Reason)> exemptions)
    {
        var offenders = new List<string>(scan.Other);
        foreach (var member in scan.Calls.GroupBy(c => c.Member, StringComparer.Ordinal))
        {
            var allowed = exemptions.TryGetValue($"{path}|{member.Key}", out var exemption) ? exemption.Uses : 0;
            var tolerated = 0;
            foreach (var call in member.Where(c => c.Unmasked).OrderBy(c => c.Line))
            {
                if (tolerated < allowed)
                {
                    tolerated++;
                    continue;
                }

                if (allowed > 0)
                    offenders.Add($"{call.Line}: a further unmasked call in {member.Key}; its exemption permits {allowed}");
                offenders.AddRange(call.Violations);
            }
        }

        return offenders;
    }

    /// <summary>
    /// The exemptions that no longer hold: an empty reason, a malformed key, a file or member that is gone, or fewer unmasked
    /// calls in the member than <c>Uses</c>.
    /// </summary>
    internal static List<string> StaleExemptions(
        IReadOnlyDictionary<string, (int Uses, string Reason)> exemptions, Func<string, string?> readFile)
    {
        var stale = new List<string>();
        foreach (var (key, (uses, reason)) in exemptions.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                stale.Add($"{key}: the reason is empty");
                continue;
            }

            var separator = key.IndexOf('|');
            if (separator <= 0 || uses < 1)
            {
                stale.Add($"{key}: malformed (\"path|Type.Member\", Uses at least 1)");
                continue;
            }

            if (readFile(key[..separator]) is not { } text)
            {
                stale.Add($"{key}: the file is gone — remove the entry");
                continue;
            }

            var member = key[(separator + 1)..];
            var scan = ScanCalls(text);
            var unmasked = scan.Calls.Count(c => c.Member == member && c.Unmasked);
            if (!scan.Members.Contains(member))
                stale.Add($"{key}: the member no longer exists — remove the entry");
            else if (unmasked < uses)
                stale.Add($"{key}: {unmasked} unmasked call(s), fewer than the {uses} permitted — lower Uses or remove the entry");
        }

        return stale;
    }

    /// <summary>
    /// <c>"{Type}.{Member}"</c> of a member scope (<see cref="ScopeOf"/>): the declared member a call counts for, so a call in a
    /// local function, lambda or anonymous method counts for the member around it; an accessor counts for its property.
    /// </summary>
    private static string MemberKey(SyntaxNode scope)
    {
        var declaration = scope is AccessorDeclarationSyntax accessor ? accessor.Parent?.Parent ?? scope : scope;
        var name = declaration switch
        {
            MethodDeclarationSyntax m => m.Identifier.ValueText,
            ConstructorDeclarationSyntax => ".ctor",
            DestructorDeclarationSyntax => ".dtor",
            PropertyDeclarationSyntax p => p.Identifier.ValueText,
            EventDeclarationSyntax e => e.Identifier.ValueText,
            IndexerDeclarationSyntax => "this[]",
            OperatorDeclarationSyntax o => $"operator {o.OperatorToken.ValueText}",
            ConversionOperatorDeclarationSyntax c => $"operator {c.Type}",
            BaseFieldDeclarationSyntax f => string.Join(",", f.Declaration.Variables.Select(v => v.Identifier.ValueText)),
            _ => "<top level>",
        };
        var type = declaration.AncestorsAndSelf().OfType<BaseTypeDeclarationSyntax>()
            .FirstOrDefault(t => t != declaration)?.Identifier.ValueText ?? "<script>";
        return $"{type}.{name}";
    }

    /// <summary>The walker: parse <paramref name="text"/> and find every engine call, attributed to its declared member.</summary>
    internal static CallScan ScanCalls(string text, SourceCodeKind kind = SourceCodeKind.Regular)
    {
        var tree = CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.Latest, kind: kind));
        var root = tree.GetRoot();
        var violations = new List<string>();

        // How the engine can be named: its own name, a using alias of it, or (bare Apply) a using static import of it.
        var imports = root.DescendantNodes().OfType<UsingDirectiveSyntax>().Where(u => LastName(u.NamespaceOrType) == EngineType).ToList();
        var aliases = imports.Where(u => u.Alias is not null).Select(u => u.Alias!.Name.Identifier.ValueText).ToHashSet(StringComparer.Ordinal);
        var staticImport = imports.Any(u => u.StaticKeyword.IsKind(SyntaxKind.StaticKeyword));

        // A global using of the engine would hide its calls in every OTHER file, which this per-file scan cannot see.
        foreach (var global in imports.Where(u => u.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword)))
        {
            violations.Add($"{Line(global)}: a global using names CreateTimeFieldMapping, which hides its calls in every other file");
        }

        bool IsEngineType(ExpressionSyntax e) => e switch
        {
            IdentifierNameSyntax id => id.Identifier.ValueText == EngineType || aliases.Contains(id.Identifier.ValueText),
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.ValueText == EngineType,
            AliasQualifiedNameSyntax aq => aq.Name.Identifier.ValueText == EngineType, // FM::CreateTimeFieldMapping
            _ => false,
        };

        bool NamesEngineMethod(ExpressionSyntax callee) => callee switch
        {
            MemberAccessExpressionSyntax ma => EngineMethods.Contains(ma.Name.Identifier.ValueText) && IsEngineType(ma.Expression),
            IdentifierNameSyntax id => staticImport && EngineMethods.Contains(id.Identifier.ValueText),
            _ => false,
        };

        var calls = root.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(c => NamesEngineMethod(c.Expression)).ToList();

        // The engine's method taken as a method group, not called: a delegate call hides the source.
        foreach (var node in root.DescendantNodes().Where(n => n is MemberAccessExpressionSyntax or IdentifierNameSyntax))
        {
            var isCallee = node.Parent is InvocationExpressionSyntax inv && inv.Expression == node;
            var isMemberName = node.Parent is MemberAccessExpressionSyntax parent && parent.Name == node;
            if (node is ExpressionSyntax expression && !isCallee && !isMemberName && node.Parent is not NameColonSyntax
                && NamesEngineMethod(expression))
            {
                violations.Add($"{Line(node)}: takes CreateTimeFieldMapping.{LastName(expression)} as a method group, so the source it is later called with cannot be checked");
            }
        }

        // Every call's source argument node, first: the one sanctioned non-assignment use of a masked local's name.
        var sourceArguments = new HashSet<SyntaxNode>();
        foreach (var call in calls)
        {
            if (SourceArgument(call) is { Expression: IdentifierNameSyntax id })
            {
                sourceArguments.Add(id);
            }
        }

        // Each call is judged on its own (an exemption counts calls, so a second call with the same source name is not folded
        // into the first).
        var engineCalls = new List<EngineCall>();
        foreach (var call in calls)
        {
            var scope = ScopeOf(call);
            var callViolations = new List<string>();
            var argument = SourceArgument(call);
            switch (argument?.Expression)
            {
                case null:
                    callViolations.Add($"{Line(call)}: the source argument could not be found");
                    break;

                case var e when IsNullOrDefault(e):
                    break;

                case IdentifierNameSyntax id:
                    var name = id.Identifier.ValueText;
                    callViolations.AddRange(UnsanctionedUses(scope, name, sourceArguments, call).Select(reason => $"{Line(call)}: '{name}' {reason}"));
                    break;

                default:
                    callViolations.Add($"{Line(call)}: the source is the expression '{Shorten(argument.Expression.ToString())}', not a masked local");
                    break;
            }

            engineCalls.Add(new EngineCall(MemberKey(scope), Line(call), callViolations));
        }

        var members = root.DescendantNodes()
            .Where(n => n is MemberDeclarationSyntax and not BaseTypeDeclarationSyntax and not BaseNamespaceDeclarationSyntax
                        and not GlobalStatementSyntax and not DelegateDeclarationSyntax)
            .Select(MemberKey)
            .ToHashSet(StringComparer.Ordinal);

        return new CallScan(engineCalls, violations, members);
    }

    /// <summary>The <c>source</c> argument: named <c>source:</c>, else the third positional one.</summary>
    private static ArgumentSyntax? SourceArgument(InvocationExpressionSyntax call)
    {
        var arguments = call.ArgumentList.Arguments;
        var named = arguments.FirstOrDefault(a => a.NameColon?.Name.Identifier.ValueText == "source");
        if (named is not null)
        {
            return named;
        }

        return arguments.Count > 2 && arguments[2].NameColon is null ? arguments[2] : null;
    }

    /// <summary>
    /// The member body a name is judged in: the nearest accessor or member declaration around the call, else the whole unit (a
    /// script's top-level statements). A local function is NOT a scope of its own: the whole enclosing member is judged, so a
    /// variable the local function captures is traced to where the member declares and assigns it.
    /// </summary>
    private static SyntaxNode ScopeOf(SyntaxNode call)
        => call.Ancestors().FirstOrDefault(a => a is AccessorDeclarationSyntax
                                                || (a is MemberDeclarationSyntax and not GlobalStatementSyntax))
           ?? call.SyntaxTree.GetRoot();

    /// <summary>
    /// An ALLOW-LIST per occurrence, on syntax nodes. Every identifier token spelled <paramref name="name"/> (a verbatim
    /// <c>@name</c> is the same name) in <paramref name="scope"/> must be one of: (1) a local declaration or a statement-level
    /// assignment whose value is <c>null</c>, <c>default</c> or exactly a mask call; (2) a null check; (3) an engine call's source
    /// argument. Anything else — a helper argument, an alias, a cast, a lambda, pattern or query binding, a deconstruction, a member
    /// access, a parameter, a <c>??=</c> — is a violation; so is a name never assigned from the mask in this body (a field, a
    /// property or a base-class member declared outside it).
    /// </summary>
    private static IEnumerable<string> UnsanctionedUses(SyntaxNode scope, string name, ISet<SyntaxNode> sourceArguments, SyntaxNode call)
    {
        var maskAssigned = false;
        var tokens = scope.DescendantTokens().Where(t => t.IsKind(SyntaxKind.IdentifierToken) && t.ValueText == name).ToList();

        foreach (var token in tokens)
        {
            var line = Line(token.Parent!);
            switch (token.Parent)
            {
                case IdentifierNameSyntax { Parent: NameColonSyntax }:
                    continue; // a named argument's label, not a use of the local

                case IdentifierNameSyntax { Parent: MemberAccessExpressionSyntax access } id when access.Name == id:
                    yield return $"is used as a member at line {line} ('.{name}') — a field or another object's member is not the masked local";
                    continue;

                case IdentifierNameSyntax id when sourceArguments.Contains(id) || IsNullCheck(id):
                    continue;

                case IdentifierNameSyntax { Parent: AssignmentExpressionSyntax assignment } id
                    when assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) && assignment.Left == id
                         && assignment.Parent is ExpressionStatementSyntax:
                    if (IsNullOrDefault(assignment.Right))
                    {
                        continue;
                    }

                    if (IsMaskCall(assignment.Right))
                    {
                        maskAssigned |= MaskReaches(token.Parent!, call);
                        continue;
                    }

                    yield return $"is assigned from '{Shorten(assignment.Right.ToString())}' at line {line}, not exactly from MappingSourceCallerMask.ApplyAsync(…)";
                    continue;

                // A local; or, in a script, a top-level declaration (Roslyn reads it as a field of the script class).
                case VariableDeclaratorSyntax { Parent.Parent: LocalDeclarationStatementSyntax or FieldDeclarationSyntax { Parent: CompilationUnitSyntax } } declarator
                    when declarator.Identifier == token:
                    var value = declarator.Initializer?.Value;
                    if (value is not null && IsNullOrDefault(value))
                    {
                        continue;
                    }

                    if (value is not null && IsMaskCall(value))
                    {
                        maskAssigned |= MaskReaches(token.Parent!, call);
                        continue;
                    }

                    yield return $"is declared at line {line} {(value is null ? "without a value" : $"from '{Shorten(value.ToString())}'")}, not exactly from MappingSourceCallerMask.ApplyAsync(…)";
                    continue;

                default:
                    yield return $"has an unsanctioned use at line {line}: '{Shorten(token.Parent!.Parent?.ToString() ?? token.Parent!.ToString())}'";
                    continue;
            }
        }

        if (!maskAssigned)
        {
            yield return "is never assigned from the mask in this member body (a parameter, a field, a property or a base-class member?) — mask the source where it is read";
        }
    }

    /// <summary>The local function, lambda or anonymous method around <paramref name="node"/>, or null in the member body itself.</summary>
    private static SyntaxNode? FunctionBoundary(SyntaxNode node)
        => node.Ancestors().FirstOrDefault(a => a is LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax);

    /// <summary>
    /// A mask assignment counts for an engine call only if it runs in the call's own function or an enclosing one: an assignment
    /// inside a local function, lambda or anonymous method does not count for a call outside it (it may never run). Fails closed.
    /// </summary>
    private static bool MaskReaches(SyntaxNode assignment, SyntaxNode call)
        => FunctionBoundary(assignment) is not { } boundary || call.Ancestors().Contains(boundary);

    /// <summary>True for <c>x is null</c>, <c>x is not null</c>, <c>x == null</c>, <c>x != null</c> (and the mirrored comparisons).</summary>
    private static bool IsNullCheck(IdentifierNameSyntax id)
    {
        switch (id.Parent)
        {
            case IsPatternExpressionSyntax pattern when pattern.Expression == id:
                var inner = pattern.Pattern is UnaryPatternSyntax unary ? unary.Pattern : pattern.Pattern;
                return inner is ConstantPatternSyntax constant && constant.Expression.IsKind(SyntaxKind.NullLiteralExpression);

            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.EqualsExpression) || binary.IsKind(SyntaxKind.NotEqualsExpression):
                var other = binary.Left == id ? binary.Right : binary.Left;
                return other.IsKind(SyntaxKind.NullLiteralExpression);

            default:
                return false;
        }
    }

    private static bool IsNullOrDefault(ExpressionSyntax e)
        => e.IsKind(SyntaxKind.NullLiteralExpression) || e.IsKind(SyntaxKind.DefaultLiteralExpression) || e is DefaultExpressionSyntax;

    /// <summary>
    /// True only for <c>(await )?MappingSourceCallerMask.ApplyAsync(…)</c>, optionally followed by <c>.ConfigureAwait(false)</c>, and
    /// nothing else (no <c>?? appRead</c>, no switch, no further member access). Its arguments are not checked (known limit).
    /// </summary>
    private static bool IsMaskCall(ExpressionSyntax e)
    {
        if (e is AwaitExpressionSyntax awaited)
        {
            e = awaited.Expression;
        }

        if (e is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax configure } configured
            && configure.Name.Identifier.ValueText == "ConfigureAwait"
            && configured.ArgumentList.Arguments.Count == 1
            && configured.ArgumentList.Arguments[0].Expression.IsKind(SyntaxKind.FalseLiteralExpression))
        {
            e = configure.Expression;
        }

        return e is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax apply }
               && apply.Name.Identifier.ValueText == "ApplyAsync"
               && LastName(apply.Expression) == "MappingSourceCallerMask";
    }

    /// <summary>The last identifier of a (qualified, alias-qualified or member-access) name, normalised: <c>@Apply</c> is <c>Apply</c>.</summary>
    private static string? LastName(SyntaxNode? node) => node switch
    {
        IdentifierNameSyntax id => id.Identifier.ValueText,
        SimpleNameSyntax simple => simple.Identifier.ValueText,
        QualifiedNameSyntax q => q.Right.Identifier.ValueText,
        AliasQualifiedNameSyntax a => a.Name.Identifier.ValueText,
        MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
        _ => null,
    };

    private static int Line(SyntaxNode node) => node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    private static string Shorten(string text)
    {
        var flat = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= 80 ? flat : flat[..80] + "…";
    }
}

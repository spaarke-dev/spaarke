using System.Text.RegularExpressions;
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
/// <para><b>The rule.</b> At each call of <c>CreateTimeFieldMapping.Apply(</c> or <c>.ApplyFillOnly(</c> under
/// <c>src/server/**</c>, the <c>source</c> argument (third positional, or named <c>source:</c>) is either the literal
/// <c>null</c> / <c>default</c> (nothing read, nothing copied), or a plain local whose EVERY assignment in that file is
/// <c>null</c>, <c>default</c> or the result of <c>MappingSourceCallerMask.…</c>. An expression, a parameter, an
/// <c>out</c>/pattern binding or a <c>using static</c> import of the engine cannot be verified and fails.</para>
///
/// <para>Crude by design (<see cref="SourceScan"/>): text, not syntax. Paired with must-fire controls (a bypass in each shape
/// fails) and must-not-fire controls (the masked Office shape and a literal null pass).</para>
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
            "The engine itself: Apply and ApplyFillOnly are the guarded entry points, and they hand the source they were given " +
            "to the private ApplyAll — that is not a call site that reads a source.",
    };

    private static readonly Regex EngineCall = new(
        @"\bCreateTimeFieldMapping\s*\.\s*(Apply|ApplyFillOnly)\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex StaticImport = new(
        @"using\s+static\s+[\w.]*CreateTimeFieldMapping\s*;",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

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

            var code = SourceScan.CodeText(File.ReadAllLines(file));
            callSites += EngineCall.Matches(code).Count;
            offenders.AddRange(Violations(code).Select(v => $"{relative}:{v}"));
        }

        Assert.True(callSites > 0, "The scan found no call of the field-mapping engine at all — the pattern no longer matches.");
        Assert.True(offenders.Count == 0,
            "A source entity reaches CreateTimeFieldMapping.Apply/ApplyFillOnly without MappingSourceCallerMask (#2188). The " +
            "source is read app-only, which ignores field-level security; mask it first:\n" +
            "  source = await MappingSourceCallerMask.ApplyAsync(callerClient, table, id, appRead, sourceColumns, logger, ct);\n" +
            "and pass that local (or a literal null). " + string.Join("\n", offenders));
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
            "an engine imported with using static",
            """
            using static Sprk.Bff.Api.Services.Dataverse.FieldMapping.CreateTimeFieldMapping;
            """
        },
    };

    [Theory(DisplayName = "#2188 guard control: each bypass shape fires")]
    [MemberData(nameof(Bypasses))]
    public void TheGuardFires_OnEveryBypassShape(string shape, string snippet)
        => Assert.True(Violations(snippet).Count > 0, $"The guard did not fire on: {shape}.");

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
        => Assert.True(Violations(snippet).Count == 0, $"The guard fired on the sanctioned shape: {shape}: {string.Join("; ", Violations(snippet))}");

    // ── the detector ──────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Each violation in <paramref name="code"/> as "line: reason".</summary>
    internal static List<string> Violations(string code)
    {
        var violations = new List<string>();

        if (StaticImport.Match(code) is { Success: true } import)
        {
            violations.Add($"{SourceScan.LineOf(code, import.Index)}: imports the engine with using static, so its calls cannot be checked");
        }

        foreach (Match call in EngineCall.Matches(code))
        {
            var line = SourceScan.LineOf(code, call.Index);
            var arguments = ArgumentsAt(code, call.Index + call.Length - 1);
            var source = SourceArgument(arguments);
            if (source is null)
            {
                violations.Add($"{line}: the source argument could not be found");
                continue;
            }

            if (source is "null" or "default")
            {
                continue;
            }

            if (!Regex.IsMatch(source, @"^@?[A-Za-z_]\w*$"))
            {
                violations.Add($"{line}: the source is the expression '{source}', not a masked local");
                continue;
            }

            violations.AddRange(UnmaskedAssignments(code, source).Select(reason => $"{line}: '{source}' {reason}"));
        }

        return violations;
    }

    /// <summary>Why the local <paramref name="name"/> is not provably masked in <paramref name="code"/> (empty when it is).</summary>
    private static IEnumerable<string> UnmaskedAssignments(string code, string name)
    {
        var id = Regex.Escape(name.TrimStart('@'));

        // An out argument, a pattern or foreach binding: assigned by something this scan cannot see.
        if (Regex.IsMatch(code, $@"\bout\s+(?:[\w.<>?]+\s+)?{id}\b|\bis\s+[\w.<>?]+\s+{id}\b|\bforeach\s*\([^)]*\b{id}\s+in\b"))
        {
            yield return "is bound by out / is / foreach, which cannot be checked";
        }

        var assignments = Regex.Matches(code, $@"(?<![\w.])@?{id}\s*(?:\?\?)?=(?![=>])\s*(?<rhs>[^;]+);");
        if (assignments.Count == 0)
        {
            // A parameter or a field: the source was read somewhere this call site cannot vouch for.
            yield return "is never assigned in this file (a parameter or field?) — mask the source where it is read";
        }

        foreach (Match assignment in assignments)
        {
            var rhs = assignment.Groups["rhs"].Value.Trim();
            if (rhs is "null" or "default" || Regex.IsMatch(rhs, @"^(await\s+)?MappingSourceCallerMask\b"))
            {
                continue;
            }

            yield return $"is assigned from '{Shorten(rhs)}', not from MappingSourceCallerMask";
        }
    }

    /// <summary>The top-level arguments of the call whose '(' is at <paramref name="open"/>.</summary>
    private static List<string> ArgumentsAt(string code, int open)
    {
        var arguments = new List<string>();
        var depth = 0;
        var start = open + 1;
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
                    arguments.Add(code[start..i].Trim());
                    break;
                }
            }
            else if (c == ',' && depth == 1)
            {
                arguments.Add(code[start..i].Trim());
                start = i + 1;
            }
        }

        return arguments;
    }

    /// <summary>The <c>source</c> argument: named <c>source:</c>, else the third positional one.</summary>
    private static string? SourceArgument(IReadOnlyList<string> arguments)
    {
        foreach (var argument in arguments)
        {
            var named = Regex.Match(argument, @"^source\s*:\s*(?<value>[\s\S]+)$");
            if (named.Success)
            {
                return named.Groups["value"].Value.Trim();
            }
        }

        return arguments.Count > 2 && !Regex.IsMatch(arguments[2], @"^\w+\s*:") ? arguments[2] : null;
    }

    private static string Shorten(string text)
    {
        var flat = Regex.Replace(text, @"\s+", " ");
        return flat.Length <= 80 ? flat : flat[..80] + "…";
    }
}

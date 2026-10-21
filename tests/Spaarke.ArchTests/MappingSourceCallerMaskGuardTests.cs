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
/// <c>null</c> / <c>default</c> (nothing read, nothing copied), or a plain local name. For a local name the rule is an
/// ALLOW-LIST per occurrence (review pass 2, F3): every occurrence of that name in the file must be (1) an assignment or
/// declaration, at the start of its statement, of <c>null</c>, <c>default</c> or exactly
/// <c>(await )?MappingSourceCallerMask.ApplyAsync(…)</c> (optionally <c>.ConfigureAwait(false)</c>, nothing else); (2) a null
/// check; or (3) an engine call's source argument — and at least one assignment must be a mask call. Anything else (a helper
/// argument, an alias, a cast, a parameter, a lambda, pattern or query binding, a deconstruction, a member or field access,
/// a <c>ref</c>) fails, as do an expression source, a <c>using static</c> or <c>using</c>-alias import of the engine, and the
/// engine's method taken as a method group (#2191 review).</para>
/// <para><b>Known limits</b> (accepted, review pass 2): the scan strips a line from the first <c>//</c>, so a <c>//</c> inside
/// a string hides the rest of that line; the mask call's arguments are not checked; the name inside a string literal is a
/// fail-closed false positive.</para>
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

    /// <summary>A <c>using static</c> import of the engine, or a <c>using</c> alias of it (#2191 review, F a) — either hides
    /// its calls from <see cref="EngineCall"/>.</summary>
    private static readonly Regex EngineImport = new(
        @"using\s+(?:static\s+|\w+\s*=\s*)[\w.:]*CreateTimeFieldMapping\s*;",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The engine's method taken as a method group, not called (#2191 review, F b): a delegate call hides the source.</summary>
    private static readonly Regex EngineMethodGroup = new(
        @"\bCreateTimeFieldMapping\s*\.\s*(Apply|ApplyFillOnly)\b(?!\s*\()",
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
            "an engine imported with using static",
            """
            using static Sprk.Bff.Api.Services.Dataverse.FieldMapping.CreateTimeFieldMapping;
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

        foreach (Match import in EngineImport.Matches(code))
        {
            violations.Add($"{SourceScan.LineOf(code, import.Index)}: imports the engine with using static or a using alias, so its calls cannot be checked");
        }

        foreach (Match group in EngineMethodGroup.Matches(code))
        {
            violations.Add($"{SourceScan.LineOf(code, group.Index)}: takes CreateTimeFieldMapping.{group.Groups[1].Value} as a method group, so the source it is later called with cannot be checked");
        }

        // Every engine call's source argument, with where its text starts — the one sanctioned non-assignment use of the name.
        var calls = new List<(int Line, string Source, int At)>();
        foreach (Match call in EngineCall.Matches(code))
        {
            var line = SourceScan.LineOf(code, call.Index);
            if (SourceArgument(ArgumentsAt(code, call.Index + call.Length - 1)) is not { } source)
            {
                violations.Add($"{line}: the source argument could not be found");
                continue;
            }

            if (source.Text is "null" or "default")
            {
                continue;
            }

            if (!Regex.IsMatch(source.Text, @"^@?[A-Za-z_]\w*$"))
            {
                violations.Add($"{line}: the source is the expression '{Shorten(source.Text)}', not a masked local");
                continue;
            }

            var verbatim = source.Text.StartsWith('@') ? 1 : 0;
            calls.Add((line, source.Text[verbatim..], source.At + verbatim));
        }

        foreach (var name in calls.Select(c => c.Source).Distinct(StringComparer.Ordinal))
        {
            var engineArguments = calls.Where(c => c.Source == name).Select(c => c.At).ToHashSet();
            var line = calls.First(c => c.Source == name).Line;
            violations.AddRange(UnsanctionedUses(code, name, engineArguments).Select(reason => $"{line}: '{name}' {reason}"));
        }

        return violations;
    }

    /// <summary>
    /// Review pass 2 (F3): an ALLOW-LIST per occurrence. Every occurrence of <paramref name="name"/> in the file must be one
    /// of: (1) an assignment or declaration, at the start of its statement, whose right-hand side is <c>null</c>,
    /// <c>default</c> or exactly a mask call; (2) a null check (<c>is null</c>, <c>is not null</c>, <c>== null</c>,
    /// <c>!= null</c>); (3) an engine call's source argument. Anything else — a helper argument, an alias, a cast, a lambda,
    /// pattern or query binding, a deconstruction, a member access, a <c>ref</c>, a parameter, a field write — is a violation.
    /// </summary>
    private static IEnumerable<string> UnsanctionedUses(string code, string name, IReadOnlySet<int> engineArguments)
    {
        var id = Regex.Escape(name);
        var assigned = false;

        foreach (Match use in Regex.Matches(code, $@"(?<!\w)@?{id}\b"))
        {
            var at = use.Value.StartsWith('@') ? use.Index + 1 : use.Index;
            var line = SourceScan.LineOf(code, use.Index);
            var after = code[(use.Index + use.Length)..];

            if (use.Index > 0 && code[use.Index - 1] == '.')
            {
                yield return $"is used as a member at line {line} ('.{name}') — a field or another object's member is not the masked local";
                continue;
            }

            // (3) the engine's source argument.
            if (engineArguments.Contains(at))
            {
                continue;
            }

            // (2) a null check.
            if (Regex.IsMatch(after, @"^\s*(?:is\s+(?:not\s+)?null\b|[!=]=\s*null\b)"))
            {
                continue;
            }

            // (1) an assignment or declaration at the start of a statement, of exactly null / default / the mask call.
            var assignment = Regex.Match(after, @"^\s*=(?![=>])");
            if (assignment.Success && StartsAStatement(code, use.Index))
            {
                var rhs = RightHandSide(code, use.Index + use.Length + assignment.Length);
                if (rhs is "null" or "default" || IsExactlyAMaskCall(rhs))
                {
                    assigned = true;
                    continue;
                }

                yield return $"is assigned from '{Shorten(rhs)}' at line {line}, not exactly from MappingSourceCallerMask.ApplyAsync(…)";
                continue;
            }

            var from = Math.Max(0, use.Index - 25);
            yield return $"has an unsanctioned use at line {line}: '{Shorten(code[from..Math.Min(code.Length, use.Index + use.Length + 25)])}'";
        }

        if (!assigned)
        {
            yield return "is never assigned from the mask in this file (a parameter or field?) — mask the source where it is read";
        }
    }

    /// <summary>
    /// True when the occurrence at <paramref name="index"/> begins its statement: only whitespace, or a declaration's type
    /// (<c>var</c>, <c>Entity?</c>, a qualified name), lies between it and the previous <c>;</c>, <c>{</c> or <c>}</c>.
    /// </summary>
    private static bool StartsAStatement(string code, int index)
    {
        var start = index - 1;
        while (start >= 0 && code[start] is not (';' or '{' or '}'))
        {
            start--;
        }

        var lead = code[(start + 1)..index].Trim();
        return lead.Length == 0 || Regex.IsMatch(lead, @"^(?:var|[A-Za-z_][\w.]*\??)$");
    }

    /// <summary>The right-hand side of an assignment starting at <paramref name="start"/>, up to its top-level <c>;</c>.</summary>
    private static string RightHandSide(string code, int start)
    {
        var depth = 0;
        var end = start;
        for (; end < code.Length; end++)
        {
            var c = code[end];
            if (c is '(' or '[' or '{')
            {
                depth++;
            }
            else if (c is ')' or ']' or '}')
            {
                if (depth == 0)
                {
                    break;
                }

                depth--;
            }
            else if (c == ';' && depth == 0)
            {
                break;
            }
        }

        return code[start..end].Trim();
    }

    /// <summary>
    /// True only for <c>(await )?MappingSourceCallerMask.ApplyAsync(&lt;balanced args&gt;)</c>, optionally followed by
    /// <c>.ConfigureAwait(false)</c>, and nothing else (no <c>?? appRead</c>, no further member access — review pass 1, F3 a).
    /// Its arguments are not checked (known limit).
    /// </summary>
    private static bool IsExactlyAMaskCall(string rhs)
    {
        var head = Regex.Match(rhs, @"^(?:await\s+)?MappingSourceCallerMask\s*\.\s*ApplyAsync\s*\(");
        if (!head.Success)
        {
            return false;
        }

        var depth = 0;
        var close = -1;
        for (var i = head.Length - 1; i < rhs.Length; i++)
        {
            if (rhs[i] == '(')
            {
                depth++;
            }
            else if (rhs[i] == ')' && --depth == 0)
            {
                close = i;
                break;
            }
        }

        return close > 0 && Regex.IsMatch(rhs[(close + 1)..], @"^\s*(?:\.\s*ConfigureAwait\s*\(\s*false\s*\))?\s*$");
    }

    /// <summary>The top-level arguments of the call whose '(' is at <paramref name="open"/>: each trimmed, with where it starts.</summary>
    private static List<(string Text, int At)> ArgumentsAt(string code, int open)
    {
        var arguments = new List<(string Text, int At)>();
        var depth = 0;
        var start = open + 1;

        void Add(int end)
        {
            var raw = code[start..end];
            arguments.Add((raw.Trim(), start + (raw.Length - raw.TrimStart().Length)));
        }

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
                    Add(i);
                    break;
                }
            }
            else if (c == ',' && depth == 1)
            {
                Add(i);
                start = i + 1;
            }
        }

        return arguments;
    }

    /// <summary>The <c>source</c> argument (its value, and where the value starts): named <c>source:</c>, else the third positional one.</summary>
    private static (string Text, int At)? SourceArgument(IReadOnlyList<(string Text, int At)> arguments)
    {
        foreach (var (text, at) in arguments)
        {
            var named = Regex.Match(text, @"^source\s*:\s*");
            if (named.Success)
            {
                return (text[named.Length..].Trim(), at + named.Length);
            }
        }

        return arguments.Count > 2 && !Regex.IsMatch(arguments[2].Text, @"^\w+\s*:") ? arguments[2] : null;
    }

    private static string Shorten(string text)
    {
        var flat = Regex.Replace(text, @"\s+", " ");
        return flat.Length <= 80 ? flat : flat[..80] + "…";
    }
}

namespace Spaarke.ArchTests;

/// <summary>
/// Shared source-scanning machinery for the credential arch-fitness guards
/// (<see cref="CredentialGuardTests"/> and <see cref="CredentialCensusTests"/>).
///
/// <para>Extracted rather than duplicated because the two guards must agree on what "the server source"
/// is and on how a statement is delimited. Two copies would drift, and the failure mode of drift here is
/// silent under-reporting by one of them — which for a census is worse than having none, because it
/// manufactures confidence.</para>
///
/// <para><b>Crude by design.</b> This is arch-fitness scanning, not compilation. It strips line comments
/// and splits on statement boundaries; it does not understand strings, block comments or generics. That
/// is adequate because every rule built on it is paired with a negative control proving the detector
/// fires, and a positive control proving it does not fire on the sanctioned shape.</para>
/// </summary>
internal static class SourceScan
{
    internal static readonly string RepoRoot = ResolveRepoRoot();

    /// <summary>Every <c>.cs</c> file under <c>src/server/**</c>, excluding build output.</summary>
    internal static IEnumerable<string> ServerSourceFiles()
    {
        var serverRoot = Path.Combine(RepoRoot, "src", "server");
        return Directory
            .EnumerateFiles(serverRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }

    /// <summary>
    /// Every <c>.cs</c> file under <c>tests/**</c>, excluding build output.
    /// Added for <see cref="Adr038TestBanGuardTests"/> (issue #864) — the ADR-038 §7 bans
    /// are rules about TEST source, so they need the mirror of <see cref="ServerSourceFiles"/>.
    /// </summary>
    internal static IEnumerable<string> TestSourceFiles()
    {
        var testRoot = Path.Combine(RepoRoot, "tests");
        return Directory
            .EnumerateFiles(testRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }

    /// <summary>Removes a <c>//</c> (and therefore <c>///</c>) line comment.</summary>
    internal static string StripLineComment(string line)
    {
        var idx = line.IndexOf("//", StringComparison.Ordinal);
        return idx < 0 ? line : line[..idx];
    }

    /// <summary>
    /// Splits source into statements — text between <c>;</c>, <c>{</c> and <c>}</c> boundaries — with the
    /// 1-based line each began on. Comments are stripped first.
    ///
    /// <para><b>Why multi-line statements have to be reassembled.</b> Both guards ask questions of the
    /// form "what is this construction assigned to?" and "does this statement build a confidential
    /// client?", and the real code answers those across line breaks: a fluent
    /// <c>ConfidentialClientApplicationBuilder</c> chain puts <c>.Create(...)</c> on the line AFTER the
    /// type name, and <c>ManagedIdentityAssertionProvider</c> assigns through a multi-line ternary. A
    /// line-scoped scan reports both as violations. That false positive is not hypothetical — it was
    /// caught by task 060's own positive control.</para>
    ///
    /// <para><b>Why braces are boundaries.</b> With <c>;</c> alone, a member signature and its opening
    /// brace accumulate into the first statement of the body, so an assignment ends up prefixed by the
    /// signature and an anchored <c>^\s*(\w+)\s*=</c> match lands on <c>public</c> instead of the field.
    /// Same false positive, different route.</para>
    /// </summary>
    internal static IEnumerable<(string Statement, int Line)> Statements(IReadOnlyList<string> lines)
    {
        var buffer = string.Empty;
        var startLine = 1;

        for (var i = 0; i < lines.Count; i++)
        {
            var code = StripLineComment(lines[i]);
            if (buffer.Length == 0)
            {
                if (string.IsNullOrWhiteSpace(code))
                {
                    continue;
                }

                startLine = i + 1;
            }

            buffer = buffer.Length == 0 ? code : buffer + " " + code.Trim();

            var terminated = code.Contains(';', StringComparison.Ordinal)
                             || code.Contains('{', StringComparison.Ordinal)
                             || code.Contains('}', StringComparison.Ordinal);
            if (!terminated)
            {
                continue;
            }

            yield return (buffer, startLine);
            buffer = string.Empty;
        }

        if (buffer.Length > 0)
        {
            yield return (buffer, startLine);
        }
    }

    /// <summary>
    /// The file's code with line comments blanked out but LINE STRUCTURE PRESERVED, so a match index can
    /// still be turned back into a line number for the failure message.
    ///
    /// <para>Used where a pattern legitimately spans lines and statement reassembly is too fragile — a
    /// fluent <c>ConfidentialClientApplicationBuilder.Create(...)</c> chain, for instance, contains an
    /// interpolated string whose <c>{</c> the statement splitter treats as a boundary. Matching over the
    /// whole file sidesteps that entirely.</para>
    /// </summary>
    internal static string CodeText(IEnumerable<string> lines)
        => string.Join("\n", lines.Select(l =>
        {
            var idx = l.IndexOf("//", StringComparison.Ordinal);
            return idx < 0 ? l : l[..idx];
        }));

    /// <summary>1-based line number of a character index within <see cref="CodeText"/>.</summary>
    internal static int LineOf(string codeText, int index)
        => codeText.Take(index).Count(c => c == '\n') + 1;

    internal static string Relative(string file)
        => file.Replace(RepoRoot + Path.DirectorySeparatorChar, string.Empty, StringComparison.Ordinal);

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

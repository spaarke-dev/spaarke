using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// unified-access-control-r2, owner round 122: the PROVISIONAL marker on a document's own narrowing
/// (<c>AccessInheritance.OwnProvisional</c>) cannot be forged in code. A provisional own Restricted is released (the
/// document follows its communication) once the document is linked, so a marker set anywhere else would widen a
/// document that a person, or another writer, narrowed.
/// </summary>
/// <remarks>
/// <para><b>Layers.</b> The type comes first: the property's setter is <c>private init</c>, so the compiler refuses any
/// assignment, object initializer or <c>with</c> outside the record. This scan covers the record itself and every server
/// file:</para>
/// <list type="bullet">
/// <item>the marker is set to <c>true</c> only in <c>OwnNarrowingUntilEmailIdentified</c> (the factory);</item>
/// <item>the only other assignments are <c>Parse</c> reading the stored value back (<c>= stored.OwnProvisional</c>) and
/// <c>KeepingProvisionalWith</c> keeping the record's own marker (<c>= OwnProvisional</c>; fix round 1, K3: every
/// <c>with</c> drops the marker, because the record's copy constructor does not copy it);</item>
/// <item>no <c>with { … OwnProvisional … }</c> anywhere, the factory included;</item>
/// <item>the factory is called only by <c>OfficeDocumentPersistence.ApplyEmailCommunicationOutcome</c> (write-path registry
/// I-19, the one save-time writer).</item>
/// </list>
/// <para><b>Signal and limits.</b> A source scan of <c>src/server/**</c> code text (line comments stripped). It counts
/// assignments by text, so an assignment through reflection or a JSON payload is not seen: the record column on a
/// document is not field-secured, and a user with Write could write the marker there, but such a user can already lower
/// the value directly (D7), so it widens nothing they could not. The controls below pin what the detector must and must not
/// see. Enforced by: this guard (ArchTests in CI and the Stop hook) and the private setter.</para>
/// </remarks>
public class AccessInheritanceProvisionalMarkerGuardTests
{
    private const string RecordFile = "src/server/api/Sprk.Bff.Api/Services/Access/SecureRootInheritance.Cascade.cs";
    private const string Factory = "OwnNarrowingUntilEmailIdentified";
    private const string ParseMethod = "Parse";
    private const string KeepMethod = "KeepingProvisionalWith";
    private const string OutcomeFile = "src/server/api/Sprk.Bff.Api/Services/Office/OfficeDocumentPersistence.cs";
    private const string OutcomeMethod = "ApplyEmailCommunicationOutcome";

    private static readonly Regex Assignment = new(
        @"\bOwnProvisional\s*(?:\?\?|[-+*/%&|^]|<<|>>)?=(?![=>])", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SetTrue = new(
        @"\bOwnProvisional\s*=\s*true\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ReadBack = new(
        @"\bOwnProvisional\s*=\s*stored\.OwnProvisional\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex KeepOwn = new(
        @"\bOwnProvisional\s*=\s*OwnProvisional\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WithMarker = new(
        @"\bwith\s*\{[^}]*\bOwnProvisional\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex FactoryCall = new(
        @"\b" + Factory + @"\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex FactoryDeclaration = new(
        @"\bAccessInheritance\s+" + Factory + @"\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// The marker writes in <paramref name="source"/> that are not sanctioned: every assignment, except (in the record file)
    /// one <c>= true</c> in the factory, one <c>= stored.OwnProvisional</c> in <c>Parse</c> and one <c>= OwnProvisional</c> in
    /// <c>KeepingProvisionalWith</c>; and every <c>with</c> that names the marker.
    /// </summary>
    internal static int ForgedMarkerWrites(string source, bool isRecordFile)
    {
        var code = SourceScan.CodeText(source.Split('\n'));
        var forged = Assignment.Matches(code).Count + WithMarker.Matches(code).Count;
        if (!isRecordFile)
            return forged;

        var factory = DocumentCreateStagesCommunicationFieldsGuardTests.MethodBody(source, Factory);
        var parse = DocumentCreateStagesCommunicationFieldsGuardTests.MethodBody(source, ParseMethod);
        var keep = DocumentCreateStagesCommunicationFieldsGuardTests.MethodBody(source, KeepMethod);
        var sanctioned = Math.Min(factory is null ? 0 : SetTrue.Matches(factory).Count, 1)
                         + Math.Min(parse is null ? 0 : ReadBack.Matches(parse).Count, 1)
                         + Math.Min(keep is null ? 0 : KeepOwn.Matches(keep).Count, 1);
        return forged - sanctioned;
    }

    /// <summary>The calls of the factory in <paramref name="source"/> that are not in the one save-time writer.</summary>
    internal static int FactoryCallsOutsideTheOutcome(string source, bool isOutcomeFile)
    {
        var code = SourceScan.CodeText(source.Split('\n'));
        var calls = FactoryCall.Matches(code).Count - FactoryDeclaration.Matches(code).Count;
        var inOutcome = isOutcomeFile && DocumentCreateStagesCommunicationFieldsGuardTests.MethodBody(source, OutcomeMethod) is { } body
            ? FactoryCall.Matches(body).Count
            : 0;
        return calls - inOutcome;
    }

    [Fact(DisplayName = "Round 122: the provisional marker is set only by its factory, and read back only by Parse")]
    public void OwnProvisional_IsSetOnlyByTheFactory()
    {
        var problems = new List<string>();
        foreach (var file in SourceScan.ServerSourceFiles())
        {
            var relative = SourceScan.Relative(file).Replace('\\', '/');
            var forged = ForgedMarkerWrites(File.ReadAllText(file), relative == RecordFile);
            if (forged != 0)
                problems.Add($"{relative}: {forged} write(s) of OwnProvisional outside {Factory} (and Parse's read-back)");
        }

        var recordSource = File.ReadAllText(Path.Combine(SourceScan.RepoRoot, RecordFile.Replace('/', Path.DirectorySeparatorChar)));
        Assert.True(
            DocumentCreateStagesCommunicationFieldsGuardTests.MethodBody(recordSource, Factory) is { } factory
            && SetTrue.Matches(factory).Count == 1,
            $"{RecordFile} {Factory} should set OwnProvisional = true once: the scan lost its signal.");
        Assert.True(problems.Count == 0,
            "AccessInheritance.OwnProvisional (owner round 122) releases a document's own Restricted once it is linked, so it " +
            $"is set only by AccessInheritance.{Factory} and read back by Parse:\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "Round 122: only ApplyEmailCommunicationOutcome calls the provisional factory")]
    public void TheFactory_IsCalledOnlyByTheSaveTimeWriter()
    {
        var problems = new List<string>();
        foreach (var file in SourceScan.ServerSourceFiles())
        {
            var relative = SourceScan.Relative(file).Replace('\\', '/');
            var outside = FactoryCallsOutsideTheOutcome(File.ReadAllText(file), relative == OutcomeFile);
            if (outside != 0)
                problems.Add($"{relative}: {outside} call(s) of {Factory} outside {OutcomeMethod}");
        }

        var outcomeSource = File.ReadAllText(Path.Combine(SourceScan.RepoRoot, OutcomeFile.Replace('/', Path.DirectorySeparatorChar)));
        Assert.True(
            DocumentCreateStagesCommunicationFieldsGuardTests.MethodBody(outcomeSource, OutcomeMethod) is { } body
            && FactoryCall.Matches(body).Count == 1,
            $"{OutcomeFile} {OutcomeMethod} should call {Factory} for its Restricted: the scan lost its signal.");
        Assert.True(problems.Count == 0,
            $"Only {OutcomeMethod} (write-path registry I-19) marks a save-time Restricted provisional:\n" +
            string.Join("\n", problems));
    }

    // ── Controls ──────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Round 122 guard control (must fire): a forged marker is counted, in the record file and elsewhere")]
    public void Control_ForgedMarkers_AreCounted()
    {
        const string record = """
            internal static AccessInheritance OwnNarrowingUntilEmailIdentified(int permission)
            {
                return new AccessInheritance(Array.Empty<string>(), false, Standard, false, permission) { OwnProvisional = true };
            }

            internal static (AccessInheritance? Value, bool Unreadable) Parse(string? text)
            {
                return (new AccessInheritance(p, false, f, false, o) { OwnProvisional = stored.OwnProvisional }, false);
            }

            private AccessInheritance(AccessInheritance original)
            {
                Parents = original.Parents;
                OwnProvisional = original.OwnProvisional;
            }

            internal AccessInheritance Forge() => this with { OwnProvisional = true };
            internal AccessInheritance Forge2() => this with { Parents = [], OwnProvisional = stored.OwnProvisional };
            internal AccessInheritance Forge3() => new(p, false, f, false, o) { OwnProvisional = true };
            internal AccessInheritance Forge4(bool b) => new(p, false, f, false, o) { OwnProvisional = b };
            """;
        const string elsewhere = """
            var record = AccessInheritance.OwnNarrowing(2) with { OwnProvisional = true };
            var other = new AccessInheritance(p, false, f, false, o) { OwnProvisional = true };
            flag.OwnProvisional |= true;
            """;

        // The copy constructor copying the marker (K3): one. Forge: a with (assignment + with). Forge2: a with that copies the
        // stored value (assignment + with). Forge3, Forge4: one each.
        Assert.Equal(7, ForgedMarkerWrites(record, isRecordFile: true));
        Assert.Equal(4, ForgedMarkerWrites(elsewhere, isRecordFile: false));
    }

    [Fact(DisplayName = "Round 122 guard control (must not fire): the factory, Parse's read-back, reads and comments are not counted")]
    public void Control_SanctionedShapes_AreNotCounted()
    {
        const string record = """
            /// <summary>Sets <c>OwnProvisional = true</c>; never <c>with { OwnProvisional = x }</c>.</summary>
            internal static AccessInheritance OwnNarrowingUntilEmailIdentified(int permission)
            {
                return new AccessInheritance(Array.Empty<string>(), false, Standard, false, permission)
                {
                    OwnProvisional = true,
                };
            }

            internal static (AccessInheritance? Value, bool Unreadable) Parse(string? text)
            {
                return (new AccessInheritance(p, false, f, false, o) { OwnProvisional = stored.OwnProvisional }, false);
            }

            private AccessInheritance(AccessInheritance original)
            {
                Parents = original.Parents;
            }

            internal AccessInheritance KeepingProvisionalWith(AccessInheritance facts)
            {
                return new AccessInheritance(facts.Parents, false, f, false, o) { OwnProvisional = OwnProvisional };
            }

            internal AccessInheritance Copy() => this with { Parents = facts.Parents };
            internal bool SameAs(AccessInheritance other) => OwnProvisional == other.OwnProvisional;
            """;
        const string elsewhere = """
            // record.OwnProvisional = true;   (commented out: does not count)
            if (record is { OwnProvisional: true } && record.OwnProvisional == true && !record.OwnProvisional) { }
            var keep = record.OwnProvisional;
            var next = record.KeepingProvisionalWith(facts);
            """;

        Assert.Equal(0, ForgedMarkerWrites(record, isRecordFile: true));
        Assert.Equal(0, ForgedMarkerWrites(elsewhere, isRecordFile: false));
    }

    [Fact(DisplayName = "Round 122 guard control: a factory call outside the save-time writer is counted; its declaration is not")]
    public void Control_FactoryCalls()
    {
        const string outcome = """
            internal static bool ApplyEmailCommunicationOutcome(CreateDocumentRequest create, Guid? id)
            {
                create.AccessInheritanceRecord = AccessInheritance.OwnNarrowingUntilEmailIdentified(restricted).Serialize();
                return true;
            }

            void Elsewhere()
            {
                // AccessInheritance.OwnNarrowingUntilEmailIdentified(2);   (commented out: does not count)
                var forged = AccessInheritance.OwnNarrowingUntilEmailIdentified(2);
            }
            """;
        const string declaration = """
            internal static AccessInheritance OwnNarrowingUntilEmailIdentified(int permission)
            {
                return null!;
            }
            """;

        Assert.Equal(1, FactoryCallsOutsideTheOutcome(outcome, isOutcomeFile: true));
        Assert.Equal(2, FactoryCallsOutsideTheOutcome(outcome, isOutcomeFile: false));
        Assert.Equal(0, FactoryCallsOutsideTheOutcome(declaration, isOutcomeFile: false));
    }
}

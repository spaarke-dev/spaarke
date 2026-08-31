// Task 071 (Track D) — the numbering subsystem, extracted from `ComposeDocxProjectionBuilder`.
//
// WHY THIS IS ITS OWN COMPONENT. It changes for one reason and it is not either projection's reason:
// **Word's numbering semantics**. It parses `numbering.xml` + style-linked numbering from `styles.xml`
// into a closed model, then replays Word's own numbering algorithm over that model to compute a label
// (`NumberingComputationEngine`). Its correctness bar is "identical to what Word displays" — set by
// [MS-DOCX] and by Word's behaviour, not by the editor's HTML contract or by the canonical content
// model. Both pipelines consume it; neither owns it.
//
// It is also consumed OUTSIDE this file by `ComposeShadowPatchEngine`, which is the strongest evidence
// that it was never really part of the projection builder — it was a subsystem nested inside one.
//
// Extraction is behaviour-preserving: bodies are moved verbatim, and equivalence over the whole corpus
// is proven empirically by the task-071 projection oracle rather than by argument.

using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Sprk.Bff.Api.Services.Compose;

internal static class ComposeNumbering
{
    // ── numbering model (task 030, spaarkeai-compose-fidelity-r4.5 WS-3, FR-11/FR-12) ─────────────────
    // The read-side numbering MODEL — the closed input the WS-3 computation engine (task 031) replays
    // Word's algorithm over (design §4 WS-3 "Algorithm"). Parses `numbering.xml` (`w:num` →
    // `w:abstractNumId` → `w:abstractNum`, per-level `w:numFmt`/`w:lvlText`/`w:start`/`w:lvlRestart`/
    // `w:isLgl`/`w:lvlOverride`/`w:startOverride`) and resolves STYLE-LINKED numbering from `styles.xml`
    // (FR-12: a paragraph style — e.g. Heading2 — carrying the `w:numPr`, exercised by
    // heading-style-numbering.docx / corpus-manifest.md row 10). NO number is computed here — that is
    // task 031; this region only builds + exposes the model and resolves each paragraph's effective
    // `(numId, ilvl)`. Model construction is a side-part read (numbering.xml / styles.xml are NOT the
    // document body) and per-paragraph resolution is wired into the EXISTING Pass-1 identity-assignment
    // loop in <c>ComposeDocxProjectionBuilder.Build</c> (`:134-171` at time of writing) — so this adds NO second full-document
    // walk; the single document-order paragraph walk invariant (`:18-26`) is untouched.

    /// <summary>One level's authored numbering definition. Mirrors exactly the field set
    /// <c>ComposeDocumentRenderer</c> (the write-side mirror) authors via <c>StartNumberingValue</c>/
    /// <c>LevelText</c>/<c>NumberingFormat</c>/<c>ParagraphStyleIdInLevel</c> (`:570-640`), so task 033's
    /// read↔write round-trip test can compare like-for-like.</summary>
    internal sealed record NumberingLevelDef(
        int AbstractNumId,
        int Level,
        NumberFormatValues? NumFmt,
        string? LvlText,
        int? Start,
        int? LvlRestart,
        bool IsLgl,
        string? ParagraphStyleIdInLevel,
        bool HasPictureBullet);

    /// <summary>A `numId`-scoped level override (`w:lvlOverride`) — either a `w:startOverride` (restart
    /// THIS numId's counter at a value, without redefining the level — other numIds sharing the same
    /// abstractNum keep its own `w:start`) or a full replacement `w:lvl` (<see cref="FullLevelOverride"/>
    /// non-null), which supersedes the abstractNum's own level definition for this numId.</summary>
    internal sealed record NumberingLevelOverrideDef(int? StartOverride, NumberingLevelDef? FullLevelOverride);

    /// <summary>A paragraph's resolved numbering source — direct `w:numPr` (<see cref="StyleLinked"/> =
    /// false, mirrors <c>ComposeDocxProjectionBuilder.ListInfo</c>'s (numId,ilvl) extraction exactly so the two never disagree
    /// on the direct case) or style-linked via `pStyle` (FR-12, <see cref="StyleLinked"/> = true).
    /// Resolved for EVERY numbered paragraph — including style-linked headings <c>ComposeDocxProjectionBuilder.ListInfo</c>
    /// deliberately excludes from list treatment — because 031 needs both cases to replay Word's single
    /// per-(abstractNumId, level) counter across the whole document.</summary>
    internal sealed record ParagraphNumberingRef(int NumId, int Ilvl, bool StyleLinked, string? SourceStyleId);

    /// <summary>
    /// The read-side numbering MODEL (task 030) — the closed input task 031 walks. Built ONCE per
    /// document by <see cref="BuildNumberingModel"/>.
    /// </summary>
    internal sealed class NumberingModel
    {
        public required IReadOnlyDictionary<int, int> AbstractNumIdByNumId { get; init; }
        public required IReadOnlyDictionary<(int AbstractNumId, int Level), NumberingLevelDef> Levels { get; init; }
        public required IReadOnlyDictionary<(int NumId, int Level), NumberingLevelOverrideDef> Overrides { get; init; }
        public required IReadOnlyDictionary<string, ParagraphNumberingRef> StyleLinkedNumbering { get; init; }

        /// <summary>AbstractNum ids whose `w:numStyleLink` indirection (FR-12's harder chain case — the
        /// design's flagged "numStyleLink as a potential uncovered construct") could NOT be resolved to a
        /// concrete level set. Escalation surface: a paragraph resolving to one of these ids means the
        /// doc uses a numbering construct outside this model (this task's escalation trigger).</summary>
        public required IReadOnlySet<int> UnresolvedNumStyleLinkAbstractNumIds { get; init; }

        /// <summary>Resolves the effective level definition for `(numId, ilvl)`, honoring a full
        /// `w:lvlOverride` before falling back to the abstractNum's own level — the same precedence
        /// <c>ComposeDocumentRenderer</c> authors (an override always wins). Null means the numId is
        /// unresolvable or that exact level is not defined (031's call whether to fall back to the
        /// nearest lower level, mirroring <c>ComposeDocxProjectionBuilder.ResolveOrdered</c>'s existing tolerant fallback).</summary>
        public NumberingLevelDef? ResolveLevel(int numId, int ilvl)
        {
            if (Overrides.TryGetValue((numId, ilvl), out var over) && over.FullLevelOverride is not null)
            {
                return over.FullLevelOverride;
            }
            return AbstractNumIdByNumId.TryGetValue(numId, out var abstractNumId) && Levels.TryGetValue((abstractNumId, ilvl), out var def)
                ? def
                : null;
        }

        /// <summary>A `w:startOverride` for `(numId, ilvl)`, or null if this numId does not override the
        /// level's start.</summary>
        public int? ResolveStartOverride(int numId, int ilvl) =>
            Overrides.TryGetValue((numId, ilvl), out var over) ? over.StartOverride : null;
    }

    /// <summary>
    /// Parses `numbering.xml` into a <see cref="NumberingModel"/> (FR-11) and resolves STYLE-LINKED
    /// numbering from `styles.xml` (FR-12). Pure / static / never throws on a malformed numbering or
    /// styles part (mirrors <c>ComposeDocxProjectionBuilder.ResolveOrdered</c>'s fail-open posture) — a defect in one part
    /// degrades that part of the model to empty rather than aborting the whole projection (F-04, Build()
    /// never throws). `internal` (not private) so task 031 and this task's own sanity tests can drive it
    /// directly over the corpus fixtures without a full <c>ComposeDocxProjectionBuilder.Build</c> round-trip.
    /// </summary>
    internal static NumberingModel BuildNumberingModel(MainDocumentPart mainPart)
    {
        var abstractNumIdByNumId = new Dictionary<int, int>();
        var levels = new Dictionary<(int, int), NumberingLevelDef>();
        var overrides = new Dictionary<(int, int), NumberingLevelOverrideDef>();
        var unresolvedNumStyleLink = new HashSet<int>();

        // "numbering style" (`w:style w:type="numbering"`) -> the numId it authors — needed BEFORE the
        // numbering.xml pass to chase an abstractNum's `w:numStyleLink` indirection.
        var numberingStyleNumId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var stylesPartForLink = mainPart.StyleDefinitionsPart?.Styles;
            if (stylesPartForLink is not null)
            {
                foreach (var style in stylesPartForLink.Elements<Style>())
                {
                    var sid = style.StyleId?.Value;
                    var numId = style.StyleParagraphProperties?.NumberingProperties?.NumberingId?.Val?.Value;
                    if (sid is not null && numId is not null) numberingStyleNumId[sid] = numId.Value;
                }
            }
        }
        catch { /* fail-open — numStyleLink chains simply won't resolve */ }

        try
        {
            var numbering = mainPart.NumberingDefinitionsPart?.Numbering;
            if (numbering is not null)
            {
                var abstractNumsById = numbering.Elements<AbstractNum>()
                    .Where(a => a.AbstractNumberId?.Value is not null)
                    .GroupBy(a => a.AbstractNumberId!.Value)
                    .ToDictionary(g => g.Key, g => g.First()); // GroupBy: a malformed doc could repeat an id; first wins, never throws

                foreach (var (abstractNumId, abstractNum) in abstractNumsById)
                {
                    var effective = abstractNum;
                    var numStyleLink = abstractNum.NumberingStyleLink?.Val?.Value;
                    if (!string.IsNullOrEmpty(numStyleLink))
                    {
                        var resolved = ResolveNumStyleLinkTarget(numStyleLink!, numberingStyleNumId, numbering, abstractNumsById);
                        if (resolved is not null) { effective = resolved; }
                        else { unresolvedNumStyleLink.Add(abstractNumId); continue; }
                    }

                    foreach (var level in effective.Elements<Level>())
                    {
                        var lvl = level.LevelIndex?.Value ?? 0;
                        levels[(abstractNumId, lvl)] = ToLevelDef(abstractNumId, lvl, level);
                    }
                }

                foreach (var instance in numbering.Elements<NumberingInstance>())
                {
                    var numId = instance.NumberID?.Value;
                    var abstractNumId = instance.AbstractNumId?.Val?.Value;
                    if (numId is null || abstractNumId is null) continue;
                    abstractNumIdByNumId[numId.Value] = abstractNumId.Value;

                    foreach (var lvlOverride in instance.Elements<LevelOverride>())
                    {
                        var lvl = lvlOverride.LevelIndex?.Value ?? 0;
                        var startOverride = lvlOverride.StartOverrideNumberingValue?.Val?.Value;
                        var fullOverride = lvlOverride.Level is { } overrideLevel
                            ? ToLevelDef(abstractNumId.Value, lvl, overrideLevel)
                            : null;
                        overrides[(numId.Value, lvl)] = new NumberingLevelOverrideDef(startOverride, fullOverride);
                    }
                }
            }
        }
        catch { /* fail-open — see class remarks */ }

        var styleLinked = new Dictionary<string, ParagraphNumberingRef>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var stylesPart = mainPart.StyleDefinitionsPart?.Styles;
            if (stylesPart is not null)
            {
                var stylesById = stylesPart.Elements<Style>()
                    .Where(s => s.StyleId?.Value is not null)
                    .GroupBy(s => s.StyleId!.Value!)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                foreach (var (styleId, style) in stylesById)
                {
                    var resolved = ResolveStyleNumbering(style, stylesById);
                    if (resolved is not null) styleLinked[styleId] = resolved;
                }
            }
        }
        catch { /* fail-open — see class remarks */ }

        return new NumberingModel
        {
            AbstractNumIdByNumId = abstractNumIdByNumId,
            Levels = levels,
            Overrides = overrides,
            StyleLinkedNumbering = styleLinked,
            UnresolvedNumStyleLinkAbstractNumIds = unresolvedNumStyleLink,
        };
    }

    private static NumberingLevelDef ToLevelDef(int abstractNumId, int lvl, Level level) => new(
        AbstractNumId: abstractNumId,
        Level: lvl,
        NumFmt: level.NumberingFormat?.Val?.Value,
        LvlText: level.LevelText?.Val?.Value,
        Start: level.StartNumberingValue?.Val?.Value,
        LvlRestart: level.LevelRestart?.Val?.Value,
        IsLgl: ComposeOoxmlPrimitives.IsOn(level.IsLegalNumberingStyle),
        ParagraphStyleIdInLevel: level.ParagraphStyleIdInLevel?.Val?.Value,
        HasPictureBullet: level.LevelPictureBulletId is not null);

    /// <summary>Chases an abstractNum's `w:numStyleLink` (FR-12 chain case): the target is a "numbering
    /// style" (`w:style w:type="numbering"`) whose OWN `w:numPr` names the numId carrying the REAL level
    /// definitions — one more indirection than the ordinary numId→abstractNum lookup. Bounded hop count +
    /// a visited-set cycle guard (never observed in the corpus, but the model must not hang on one).
    /// Returns null — unresolved — if the chain bottoms out without reaching a concrete abstractNum; the
    /// caller records that abstractNumId in <see cref="NumberingModel.UnresolvedNumStyleLinkAbstractNumIds"/>.</summary>
    private static AbstractNum? ResolveNumStyleLinkTarget(
        string targetStyleId,
        IReadOnlyDictionary<string, int> numberingStyleNumId,
        Numbering numbering,
        IReadOnlyDictionary<int, AbstractNum> abstractNumsById,
        int maxHops = 8)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var currentStyleId = targetStyleId;
        for (var hop = 0; hop < maxHops; hop++)
        {
            if (!visited.Add(currentStyleId)) return null; // cycle
            if (!numberingStyleNumId.TryGetValue(currentStyleId, out var numId)) return null;

            var instance = numbering.Elements<NumberingInstance>().FirstOrDefault(n => n.NumberID?.Value == numId);
            var abstractNumId = instance?.AbstractNumId?.Val?.Value;
            if (abstractNumId is null || !abstractNumsById.TryGetValue(abstractNumId.Value, out var abstractNum))
            {
                return null;
            }

            var nextLink = abstractNum.NumberingStyleLink?.Val?.Value;
            if (string.IsNullOrEmpty(nextLink)) return abstractNum; // concrete — chain resolved
            currentStyleId = nextLink!;
        }
        return null; // hop budget exhausted — treat as unresolved rather than risk a pathological loop
    }

    /// <summary>Resolves a paragraph STYLE's effective numbering (FR-12) — the style's own `w:numPr`, or
    /// (if absent) the nearest `w:basedOn` ancestor's, mirroring Word's paragraph-property style
    /// inheritance. Bounded hop count + visited-set cycle guard against a malformed `w:basedOn` chain.
    /// <see cref="ParagraphNumberingRef.SourceStyleId"/> in the returned ref is the style that ACTUALLY
    /// carries the `w:numPr` (which may be an ancestor of <paramref name="style"/>, not <paramref
    /// name="style"/> itself, when resolved via `w:basedOn`).</summary>
    private static ParagraphNumberingRef? ResolveStyleNumbering(
        Style style, IReadOnlyDictionary<string, Style> stylesById, int maxHops = 20)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Style? current = style;
        for (var hop = 0; hop < maxHops && current is not null; hop++)
        {
            var sid = current.StyleId?.Value;
            if (sid is not null && !visited.Add(sid)) return null; // cycle

            var numPr = current.StyleParagraphProperties?.NumberingProperties;
            var numId = numPr?.NumberingId?.Val?.Value;
            if (numId is not null)
            {
                var ilvl = numPr!.NumberingLevelReference?.Val?.Value ?? 0;
                return new ParagraphNumberingRef(numId.Value, ilvl, StyleLinked: true, SourceStyleId: sid);
            }

            var basedOnId = current.BasedOn?.Val?.Value;
            current = basedOnId is not null && stylesById.TryGetValue(basedOnId, out var parent) ? parent : null;
        }
        return null;
    }

    /// <summary>Resolves a paragraph's effective `(numId, ilvl)` for the WS-3 numbering model (FR-11/
    /// FR-12) — direct `w:numPr` first (mirrors <c>ComposeDocxProjectionBuilder.ListInfo</c>'s extraction exactly), else the
    /// paragraph's `pStyle` resolved through <see cref="NumberingModel.StyleLinkedNumbering"/>
    /// (heading-style-numbering.docx's Heading1/Heading2, corpus-manifest.md row 10). `internal` so 031
    /// calls this from the SAME per-paragraph call site <c>ComposeDocxProjectionBuilder.Build</c>'s Pass-1 loop already visits
    /// (no second walk) and so this task's tests can assert the resolution directly.</summary>
    internal static ParagraphNumberingRef? ResolveParagraphNumbering(Paragraph p, NumberingModel model)
    {
        var directNumPr = p.ParagraphProperties?.NumberingProperties;
        var directNumId = directNumPr?.NumberingId?.Val?.Value;
        if (directNumId is not null)
        {
            var ilvl = directNumPr!.NumberingLevelReference?.Val?.Value ?? 0;
            return new ParagraphNumberingRef(
                directNumId.Value, ilvl, StyleLinked: false, SourceStyleId: p.ParagraphProperties?.ParagraphStyleId?.Val?.Value);
        }

        var styleId = p.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        if (styleId is not null && model.StyleLinkedNumbering.TryGetValue(styleId, out var styleRef))
        {
            return styleRef;
        }

        return null;
    }

    // ── numbering computation engine (task 031, spaarkeai-compose-fidelity-r4.5 WS-3, FR-11..FR-14) ────
    // THE flagship of R4.5. Replays Word's numbering algorithm over the task-030 model (design §4 WS-3),
    // producing the EXACT displayed label per numbered paragraph — decimal / lowerLetter / upperLetter /
    // lowerRoman / upperRoman / bullet formats, multi-level "%1.%2.%3" composition, w:isLgl (legal →
    // decimal), w:start / w:lvlRestart / w:lvlOverride / w:startOverride. Deterministic (NFR-06): identical
    // inputs → identical labels, no reliance on render order. Read-time only (FR-14): computes the label as
    // READ; it does NOT auto-renumber on edit — live renumber-on-insert/delete is R5 G3, which builds on
    // THIS shared engine (recorded so R5 does not fork the model). It is the read-side twin of the write-side
    // ComposeDocumentRenderer (task 033 proves the two agree).

    /// <summary>
    /// The result of one <see cref="NumberingComputationEngine.Compute"/> call (task 040, WS-4/FR-16):
    /// <paramref name="Label"/> is the displayed number Word shows (e.g. <c>"4.2"</c>); <paramref name="ListPath"/>
    /// is the ORDINAL CHAIN — the raw numeric counter at every level from 0 through the paragraph's own
    /// <c>ilvl</c> (e.g. <c>[4, 2]</c>) — the same computation the label's <c>%n</c> substitution draws from,
    /// surfaced separately so a consumer (the projection payload, WS-4's citation map) does not need to
    /// re-parse the formatted label to recover the level-by-level counters.
    /// </summary>
    internal readonly record struct NumberingComputationResult(string Label, IReadOnlyList<int> ListPath);

    /// <summary>
    /// Deterministic replay of Word's numbering algorithm (FR-11) over a <see cref="NumberingModel"/>, run
    /// once per document across the projection's SINGLE document-order Pass-1 walk. A single instance is
    /// created before the walk and <see cref="Compute"/> is called — in document order — for each numbered
    /// paragraph; the instance carries the running counters forward, so an interrupted numbered run
    /// continues (never restarts at 1). Stateful within one <c>Build</c> call, never shared across documents.
    /// </summary>
    /// <remarks>
    /// <para><b>Counter model.</b> One running counter per <c>(numId, level)</c> — scoped to the numbering
    /// INSTANCE (<c>w:num</c>) per ECMA-376, NOT the shared <c>w:abstractNum</c> (task-035 / DEF-03 fix;
    /// two <c>w:num</c> over one <c>w:abstractNum</c> keep independent counters, so a fresh instance's
    /// <c>w:startOverride</c> restarts at 1). On each numbered paragraph the paragraph's level increments
    /// (or initializes to <c>w:start</c> / <c>w:startOverride</c> on first use since a reset), then all
    /// DEEPER levels reset per
    /// Word's default (a more-significant level increment restarts deeper levels) as modified by
    /// <c>w:lvlRestart</c> (<c>0</c> ⇒ never restart). The label is then composed from the level's
    /// <c>w:lvlText</c> template, substituting each <c>%n</c> with the counter at level <c>n-1</c> formatted
    /// per that level's <c>w:numFmt</c> — unless the current level is <c>w:isLgl</c> (legal), which forces
    /// EVERY inserted level reference to decimal.</para>
    /// <para><b>Purity / determinism (NFR-06 / ADR-007/013).</b> Pure OOXML computation over the model — no
    /// I/O, no Graph, no AI type, no RNG. Deeper-level resets remove dictionary keys (order-independent), so
    /// the emitted label depends only on document-order inputs, never on hash-iteration order.</para>
    /// </remarks>
    internal sealed class NumberingComputationEngine
    {
        private readonly NumberingModel _model;

        // The running ordinal per (numId, level). Per ECMA-376 a numbering counter is scoped to the
        // numbering-definition INSTANCE (w:num / numId), NOT the shared abstract definition
        // (w:abstractNum / abstractNumId): two independent w:num referencing one w:abstractNum have
        // INDEPENDENT counters by construction, which is exactly why a w:startOverride on a fresh
        // instance is the standard Word "Restart at 1" idiom (authored by ComposeDocumentRenderer for
        // every ordered list broken by a non-list block). Keying by (abstractNumId, level) — the task-031
        // original — silently continued the second list's count instead of restarting it (DEF-03 /
        // task-035 fix). Corpus-safe: every corpus doc uses a single numId per abstractNum, so numId and
        // abstractNumId are in 1:1 correspondence there — the 24-case golden Theory is bit-identical under
        // this keying; only the multi-numId-per-abstractNum case (which the write side authors and the
        // corpus never contained) changes, and changes to the CORRECT ECMA-376 behavior. Absence of a
        // key means the level has not been used since its last reset (next use initializes to
        // w:start / w:startOverride).
        private readonly Dictionary<(int NumId, int Level), int> _counters = new();

        // (numId, level) whose numId-scoped w:startOverride has already been consumed. A w:startOverride
        // seeds the FIRST use of that numId's level; after a subsequent reset the level falls back to the
        // abstractNum's own w:start (Word's common behavior). Tracked so the override applies exactly once.
        private readonly HashSet<(int NumId, int Level)> _appliedStartOverrides = new();

        public NumberingComputationEngine(NumberingModel model) => _model = model;

        /// <summary>
        /// Advances the counters for one numbered paragraph (in document order) and returns the displayed
        /// label Word computes for it TOGETHER WITH the level's ordinal chain (task 040, WS-4/FR-16 — e.g.
        /// <c>"4.2"</c> pairs with <c>[4, 2]</c>), or <c>null</c> when the numbering is unresolvable (the
        /// <c>numId</c> is not in the model — no corpus doc hits this; it is the escalation-relevant
        /// "construct outside the model" case, already surfaced as a warning by <c>ComposeDocxProjectionBuilder.Build</c>). Must be
        /// called exactly once per numbered paragraph, in document order, for the single-counter replay to be
        /// exact. The chain is read from the SAME counter state the label was just composed from — computed
        /// together so a caller can never observe the chain for a different (later) paragraph's counters.
        /// </summary>
        public NumberingComputationResult? Compute(ParagraphNumberingRef numbering)
        {
            var numId = numbering.NumId;
            var ilvl = numbering.Ilvl;
            if (!_model.AbstractNumIdByNumId.TryGetValue(numId, out var abstractNumId))
            {
                return null; // unresolvable numId — cannot compute a faithful label; do not fabricate one.
            }

            var levelDef = _model.ResolveLevel(numId, ilvl);

            // 1) increment this numId-instance's counter for this level (initialize on first use since the
            //    last reset). A FRESH numId sharing an already-warm abstractNum has NO key yet, so it
            //    initializes via InitialValue (honoring its own w:startOverride) rather than continuing the
            //    prior numId's count — the ECMA-376 instance-scoped restart (DEF-03 fix).
            var key = (numId, ilvl);
            if (_counters.TryGetValue(key, out var current))
            {
                _counters[key] = current + 1;
            }
            else
            {
                _counters[key] = InitialValue(numId, ilvl, levelDef);
            }

            // 2) reset deeper levels of THIS numId instance (Word default, honoring w:lvlRestart). Only this
            //    instance's deeper levels reset — a sibling numId sharing the abstractNum is independent.
            ResetDeeperLevels(numId, abstractNumId, ilvl);

            // 3) format + compose the displayed label, and (task 040, WS-4/FR-16) the raw ordinal chain
            //    (levels 0..ilvl of THIS numId's counters) the label's %1..%n substitution drew from — e.g.
            //    label "4.2" pairs with chain [4, 2]. Read BEFORE any later paragraph's Compute() call can
            //    mutate these same counter keys, so the chain always reflects THIS paragraph's numbering.
            var label = ComposeLabel(numId, abstractNumId, ilvl, levelDef);
            var listPath = BuildOrdinalChain(numId, ilvl);
            return new NumberingComputationResult(label, listPath);
        }

        /// <summary>
        /// The ordinal chain (task 040, WS-4/FR-16) — the numeric counter at every level from 0 through
        /// <paramref name="ilvl"/> for <paramref name="numId"/>, e.g. <c>[4, 2]</c> for a level-1 paragraph
        /// under a level-0 "4". Mirrors <see cref="SubstituteLvlText"/>'s own <c>%n</c> counter-or-Start
        /// fallback exactly (a shallower level not yet counted since the last reset shows its <c>w:start</c>,
        /// matching what a <c>lvlText</c> template referencing it would display) — so the chain is always
        /// consistent with what <see cref="ComposeLabel"/> would compose for any prefix of levels 0..ilvl,
        /// even when the current level's <c>lvlText</c> itself does not reference every ancestor level.
        /// </summary>
        private List<int> BuildOrdinalChain(int numId, int ilvl)
        {
            var chain = new List<int>(ilvl + 1);
            for (var level = 0; level <= ilvl; level++)
            {
                var value = _counters.TryGetValue((numId, level), out var cv)
                    ? cv
                    : (_model.ResolveLevel(numId, level)?.Start ?? 1);
                chain.Add(value);
            }
            return chain;
        }

        private int InitialValue(int numId, int ilvl, NumberingLevelDef? levelDef)
        {
            // A numId-scoped w:startOverride seeds the FIRST use of this level (once), independent of the
            // abstractNum's own w:start. HashSet.Add returns false if already applied — then fall through.
            var startOverride = _model.ResolveStartOverride(numId, ilvl);
            if (startOverride.HasValue && _appliedStartOverrides.Add((numId, ilvl)))
            {
                return startOverride.Value;
            }
            return levelDef?.Start ?? 1; // w:start default is 1 (ECMA-376 §17.9.25).
        }

        private void ResetDeeperLevels(int numId, int abstractNumId, int incrementedLevel)
        {
            // Snapshot the keys first (cannot mutate _counters while enumerating). Removal order is
            // irrelevant to the result — determinism holds regardless of hash-iteration order. Only THIS
            // numId instance's deeper levels reset (w:lvlRestart is defined on the shared abstractNum's
            // level, so ShouldRestart still consults abstractNumId).
            var deeper = _counters.Keys
                .Where(k => k.NumId == numId && k.Level > incrementedLevel)
                .ToList();
            foreach (var k in deeper)
            {
                if (ShouldRestart(abstractNumId, k.Level, incrementedLevel))
                {
                    _counters.Remove(k);
                }
            }
        }

        /// <summary>
        /// Whether a deeper level restarts when <paramref name="incrementedLevel"/> increments. Default
        /// (no <c>w:lvlRestart</c>): a deeper level restarts whenever ANY more-significant level increments
        /// (Word's behavior; the corpus exemplars use exactly this). <c>w:lvlRestart="0"</c> ⇒ the level
        /// NEVER restarts. A specific value <c>N</c> (1-based level number) ⇒ restart only when a level
        /// whose 1-based number is ≤ <c>N</c> increments (reduces to default when <c>N</c> equals the
        /// deeper level's own number).
        /// </summary>
        private bool ShouldRestart(int abstractNumId, int deeperLevel, int incrementedLevel)
        {
            var lvlRestart = _model.Levels.TryGetValue((abstractNumId, deeperLevel), out var def) ? def.LvlRestart : null;
            if (lvlRestart is null) return true;      // default: restart on any more-significant increment
            if (lvlRestart.Value <= 0) return false;  // w:lvlRestart="0" ⇒ never restart
            return (incrementedLevel + 1) <= lvlRestart.Value;
        }

        private string ComposeLabel(int numId, int abstractNumId, int ilvl, NumberingLevelDef? levelDef)
        {
            var numFmt = levelDef?.NumFmt;
            var lvlText = levelDef?.LvlText;

            // Bullet: the lvlText IS the literal marker glyph (no %n substitution) — return it verbatim
            // (the displayed marker; WS-4 treats a non-numeric marker as a non-citation).
            if (numFmt == NumberFormatValues.Bullet)
            {
                return lvlText ?? string.Empty;
            }

            // No template (rare/malformed): fall back to the bare formatted current counter.
            if (string.IsNullOrEmpty(lvlText))
            {
                var v = _counters.TryGetValue((numId, ilvl), out var cv) ? cv : 1;
                return FormatCounter(v, numFmt);
            }

            var legal = levelDef?.IsLgl == true; // legal (w:isLgl) forces decimal for EVERY inserted level ref.
            return SubstituteLvlText(lvlText!, numId, abstractNumId, legal);
        }

        /// <summary>
        /// Substitutes each <c>%n</c> (n = 1..9) placeholder in a <c>w:lvlText</c> template with the counter
        /// at level <c>n-1</c>, formatted per that level's <c>w:numFmt</c> (or decimal when
        /// <paramref name="legal"/>). Literal characters (".", "(", ")", spaces, "Article ", …) are copied
        /// verbatim — so "%1.%2.%3" → "4.2.1", "(%2)" → "(b)", "Article %1" → "Article IV". Depth reaches
        /// the current level, giving WS-4 the sub-item granularity it needs ("4.2(b)(iii)").
        /// </summary>
        private string SubstituteLvlText(string lvlText, int numId, int abstractNumId, bool legal)
        {
            var sb = new StringBuilder(lvlText.Length + 4);
            for (var i = 0; i < lvlText.Length; i++)
            {
                var c = lvlText[i];
                if (c == '%' && i + 1 < lvlText.Length && lvlText[i + 1] is >= '1' and <= '9')
                {
                    var refLevel = (lvlText[i + 1] - '0') - 1; // %n references level n-1 (0-based)
                    var refDef = _model.ResolveLevel(numId, refLevel);
                    var value = _counters.TryGetValue((numId, refLevel), out var cv)
                        ? cv
                        : (refDef?.Start ?? 1); // a not-yet-counted referenced level shows its start (valid docs reference only 0..current)
                    var fmt = legal ? NumberFormatValues.Decimal : refDef?.NumFmt;
                    sb.Append(FormatCounter(value, fmt));
                    i++; // consume the digit
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        /// <summary>Formats one level's counter per <c>w:numFmt</c> (design §4 WS-3 step 3). The five named
        /// legal schemes plus decimal; any other format (decimalZero, ordinal, cardinalText, …) is not used
        /// by the legal corpus and degrades to decimal — the honest, never-throw fallback.</summary>
        private static string FormatCounter(int value, NumberFormatValues? fmt)
        {
            if (fmt == NumberFormatValues.LowerLetter) return ToLetters(value, upper: false);
            if (fmt == NumberFormatValues.UpperLetter) return ToLetters(value, upper: true);
            if (fmt == NumberFormatValues.LowerRoman) return ToRoman(value, upper: false);
            if (fmt == NumberFormatValues.UpperRoman) return ToRoman(value, upper: true);
            return value.ToString(CultureInfo.InvariantCulture); // decimal + safe fallback
        }

        /// <summary>Bijective base-26 spreadsheet-style letters: 1→a, 26→z, 27→aa, 28→ab, … (FR-11
        /// lowerLetter/upperLetter, incl. the z→aa overflow). Non-positive input degrades to decimal.</summary>
        internal static string ToLetters(int n, bool upper)
        {
            if (n <= 0) return n.ToString(CultureInfo.InvariantCulture);
            var baseChar = upper ? 'A' : 'a';
            var sb = new StringBuilder(4);
            while (n > 0)
            {
                n--;
                sb.Insert(0, (char)(baseChar + (n % 26)));
                n /= 26;
            }
            return sb.ToString();
        }

        private static readonly (int Value, string Upper, string Lower)[] RomanNumerals =
        {
            (1000, "M", "m"), (900, "CM", "cm"), (500, "D", "d"), (400, "CD", "cd"),
            (100, "C", "c"), (90, "XC", "xc"), (50, "L", "l"), (40, "XL", "xl"),
            (10, "X", "x"), (9, "IX", "ix"), (5, "V", "v"), (4, "IV", "iv"), (1, "I", "i"),
        };

        /// <summary>Standard subtractive roman numerals: 1→i, 4→iv, 9→ix, 40→xl, … (FR-11 lowerRoman/
        /// upperRoman). Out-of-range input (≤0 or &gt;3999) degrades to decimal.</summary>
        internal static string ToRoman(int n, bool upper)
        {
            if (n <= 0 || n > 3999) return n.ToString(CultureInfo.InvariantCulture);
            var sb = new StringBuilder(8);
            foreach (var (value, u, l) in RomanNumerals)
            {
                while (n >= value)
                {
                    sb.Append(upper ? u : l);
                    n -= value;
                }
            }
            return sb.ToString();
        }
    }
}

// Task 072 (Track D) — the WRITE-side numbering author, extracted from `ComposeDocumentRenderer`.
//
// WHY THIS IS ITS OWN COMPONENT. It authors and merges `word/numbering.xml` — abstract numbering
// definitions, the style-linked multi-level clause scheme, list schemes, and the carrier-side scan that
// decides which numbering ids a re-rendered document may reuse. It changes when Word's numbering format
// changes or when a fidelity task widens what we can express, never when the body's shape changes.
//
// It is the WRITE-side mirror of `ComposeNumbering` (the read side, extracted by task 071). The two are
// deliberately separate: one parses Word's numbering into a model, this one authors a part from ours.
//
// ADR-049 I-5 — ONE BODY AUTHOR. Nothing here writes body children. It writes the NUMBERING part and
// returns plans/definitions; `ComposeDocumentRenderer` remains the only thing that appends to `w:body`.
// That was verified by reading every member, not assumed from the file split.

using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Sprk.Bff.Api.Services.Compose;

internal static class ComposeNumberingAuthor
{
    internal const int HeadingNumInstanceId = 1;           // the ONE num instance the Heading styles reference
    internal const int FirstListNumInstanceId = 2;         // list num instances are allocated from here up

    // The three abstract numbering ids this component authors and owns.
    internal const int HeadingAbstractNumId = 0;          // style-linked clause scheme (ilvl 0-8)
    internal const int OrderedAbstractNumId = 1;           // decimal list scheme (direct numPr)
    internal const int BulletAbstractNumId = 2;            // bullet list scheme (direct numPr)

    /// <summary>Whether <paramref name="blocks"/> contains any list item (recursing into table cells) —
    /// gates the carrier numbering inspection/merge so a list-free render never touches (and therefore
    /// never rewrites) the carrier's numbering part (011-T2 preserve-parts contract).</summary>
    internal static bool ModelContainsListItem(IReadOnlyList<ComposeBlock> blocks)
    {
        foreach (var block in blocks)
        {
            if (block.Kind == ComposeBlockKind.ListItem)
            {
                return true;
            }
            if (block.Kind == ComposeBlockKind.Table && block.Table is not null
                && block.Table.Rows.Any(r => r.Cells.Any(c => ModelContainsListItem(c.Blocks))))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Task 011: merges the plan's list instances into an EXISTING carrier numbering part. Renderer abstracts
    /// are inserted with REMAPPED ids above the carrier's own (schema order: AbstractNum before Num — inserted
    /// before the first existing instance), and every plan instance references those remapped abstracts. No
    /// carrier-owned abstract or instance is modified. The heading abstract/instance is deliberately absent —
    /// carrier styles govern headings (see <see cref="RenderIntoCarrier"/> remarks).
    /// </summary>
    internal static void MergeNumberingDefinitions(
        NumberingDefinitionsPart numberingPart, NumberingPlan plan, int orderedAbstractId, int bulletAbstractId)
    {
        var numbering = numberingPart.Numbering ??= new Numbering();

        // CT_Numbering order edges (review finding 011-P3): all abstractNum precede all num, and a
        // trailing w:numIdMacAtCleanup (Mac Word artifact) must stay LAST — new instances insert before
        // it, and new abstracts insert before the first existing instance (or before the cleanup marker
        // when the part has abstracts but no instances).
        var firstInstance = numbering.Elements<NumberingInstance>().FirstOrDefault();
        var macCleanup = numbering.GetFirstChild<NumberingIdMacAtCleanup>();

        void InsertAbstract(AbstractNum abstractNum)
        {
            var anchor = (OpenXmlElement?)firstInstance ?? macCleanup;
            if (anchor is not null)
            {
                numbering.InsertBefore(abstractNum, anchor);
            }
            else
            {
                numbering.AppendChild(abstractNum);
            }
        }

        void AppendInstance(NumberingInstance instance)
        {
            if (macCleanup is not null)
            {
                numbering.InsertBefore(instance, macCleanup);
            }
            else
            {
                numbering.AppendChild(instance);
            }
        }

        if (plan.OrderedInstanceIds.Count > 0)
        {
            InsertAbstract(BuildOrderedAbstractNum(orderedAbstractId));
            foreach (var orderedId in plan.OrderedInstanceIds)
            {
                var instance = new NumberingInstance(new AbstractNumId { Val = orderedAbstractId }) { NumberID = orderedId };
                instance.AppendChild(new LevelOverride(new StartOverrideNumberingValue { Val = 1 }) { LevelIndex = 0 });
                AppendInstance(instance);
            }
        }

        if (plan.BulletInstanceId is { } bulletId)
        {
            InsertAbstract(BuildBulletAbstractNum(bulletAbstractId));
            AppendInstance(new NumberingInstance(new AbstractNumId { Val = bulletAbstractId }) { NumberID = bulletId });
        }

        numberingPart.Numbering.Save();
    }

    internal static void AddNumberingDefinitions(MainDocumentPart mainPart, NumberingPlan plan)
    {
        var numberingPart = mainPart.AddNewPart<NumberingDefinitionsPart>();
        var numbering = new Numbering();

        // AbstractNum elements MUST precede Num elements (schema order).
        numbering.AppendChild(BuildHeadingAbstractNum());
        numbering.AppendChild(BuildOrderedAbstractNum());
        numbering.AppendChild(BuildBulletAbstractNum());

        // The ONE heading num instance the Heading styles reference (numId 1 → heading abstract).
        numbering.AppendChild(new NumberingInstance(new AbstractNumId { Val = HeadingAbstractNumId }) { NumberID = HeadingNumInstanceId });

        // The shared bullet instance (allocated only if a bullet list was rendered).
        if (plan.BulletInstanceId is { } bulletId)
        {
            numbering.AppendChild(new NumberingInstance(new AbstractNumId { Val = BulletAbstractNumId }) { NumberID = bulletId });
        }

        // One ordered instance per restart-scoped ordered list, each with a startOverride so it restarts at 1.
        foreach (var orderedId in plan.OrderedInstanceIds)
        {
            var instance = new NumberingInstance(new AbstractNumId { Val = OrderedAbstractNumId }) { NumberID = orderedId };
            instance.AppendChild(new LevelOverride(new StartOverrideNumberingValue { Val = 1 }) { LevelIndex = 0 });
            numbering.AppendChild(instance);
        }

        numberingPart.Numbering = numbering;
        numberingPart.Numbering.Save();
    }

    /// <summary>
    /// The style-linked multi-level clause scheme (FR-27): ONE multilevel abstractNum, 9 levels (ilvl 0-8),
    /// each a decimal <c>%N</c> cascade (<c>%1</c> / <c>%1.%2</c> / <c>%1.%2.%3</c> …). Levels 0-5 back-link
    /// their <c>Heading1..6</c> style via <c>w:pStyle</c>; levels 6-8 are numbered (for completeness) but
    /// unlinked (headings only reach level 6). Each level restarts its counter after a higher level advances.
    /// </summary>
    private static AbstractNum BuildHeadingAbstractNum()
    {
        // CT_AbstractNum order: nsid precedes multiLevelType precedes the levels.
        var abstractNum = new AbstractNum(
            new Nsid { Val = "0E7D0000" },
            new MultiLevelType { Val = MultiLevelValues.Multilevel })
        { AbstractNumberId = HeadingAbstractNumId };

        for (var ilvl = 0; ilvl <= 8; ilvl++)
        {
            var cascade = string.Join(".", Enumerable.Range(1, ilvl + 1).Select(k => $"%{k}"));
            var level = new Level(
                new StartNumberingValue { Val = 1 },
                new NumberingFormat { Val = NumberFormatValues.Decimal },
                new LevelText { Val = cascade },
                new LevelJustification { Val = LevelJustificationValues.Left },
                new PreviousParagraphProperties(
                    new Indentation { Left = (720 * (ilvl + 1)).ToString(CultureInfo.InvariantCulture), Hanging = "360" }))
            {
                LevelIndex = ilvl,
            };

            // Style-link levels 0-5 → Heading1..6 (the abstract side of the link). Schema order places
            // w:pStyle after w:numFmt and BEFORE w:lvlText (matches the real CSA numbering.xml idiom).
            if (ilvl < ComposeDocumentRenderer.MaxHeadingLevel)
            {
                level.InsertBefore(new ParagraphStyleIdInLevel { Val = ComposeStyleCatalog.HeadingStyleId(ilvl + 1) }, level.GetFirstChild<LevelText>());
            }

            abstractNum.AppendChild(level);
        }

        return abstractNum;
    }

    /// <summary>The ordered-list scheme: 9 decimal levels (<c>%N.</c>), consumed via a DIRECT numPr on
    /// ListParagraph items. No style link (lists are not styled-numbered). <paramref name="abstractNumId"/>
    /// defaults to the blank-package id; carrier mode (task 011) passes a remapped id above the carrier's own.</summary>
    private static AbstractNum BuildOrderedAbstractNum(int abstractNumId = OrderedAbstractNumId)
    {
        var abstractNum = new AbstractNum(
            new Nsid { Val = "0E7D0001" },
            new MultiLevelType { Val = MultiLevelValues.HybridMultilevel })
        { AbstractNumberId = abstractNumId };

        for (var ilvl = 0; ilvl <= 8; ilvl++)
        {
            abstractNum.AppendChild(new Level(
                new StartNumberingValue { Val = 1 },
                new NumberingFormat { Val = NumberFormatValues.Decimal },
                new LevelText { Val = $"%{ilvl + 1}." },
                new LevelJustification { Val = LevelJustificationValues.Left },
                new PreviousParagraphProperties(
                    new Indentation { Left = (720 * (ilvl + 1)).ToString(CultureInfo.InvariantCulture), Hanging = "360" }))
            {
                LevelIndex = ilvl,
            });
        }

        return abstractNum;
    }

    /// <summary>The bullet-list scheme: 9 bullet levels (Symbol-font glyphs), consumed via a DIRECT numPr.
    /// <paramref name="abstractNumId"/> defaults to the blank-package id; carrier mode remaps (task 011).</summary>
    private static AbstractNum BuildBulletAbstractNum(int abstractNumId = BulletAbstractNumId)
    {
        var abstractNum = new AbstractNum(
            new Nsid { Val = "0E7D0002" },
            new MultiLevelType { Val = MultiLevelValues.HybridMultilevel })
        { AbstractNumberId = abstractNumId };

        // Cycle the three classic Word bullet glyphs across depths.
        var glyphs = new[] { "", "o", "" }; // • (Symbol), o (Courier), ▪ (Wingdings)
        var fonts = new[] { "Symbol", "Courier New", "Wingdings" };

        for (var ilvl = 0; ilvl <= 8; ilvl++)
        {
            var pick = ilvl % 3;
            abstractNum.AppendChild(new Level(
                new StartNumberingValue { Val = 1 },
                new NumberingFormat { Val = NumberFormatValues.Bullet },
                new LevelText { Val = glyphs[pick] },
                new LevelJustification { Val = LevelJustificationValues.Left },
                new PreviousParagraphProperties(
                    new Indentation { Left = (720 * (ilvl + 1)).ToString(CultureInfo.InvariantCulture), Hanging = "360" }),
                new NumberingSymbolRunProperties(
                    new RunFonts { Ascii = fonts[pick], HighAnsi = fonts[pick], Hint = FontTypeHintValues.Default }))
            {
                LevelIndex = ilvl,
            });
        }

        return abstractNum;
    }

    /// <summary>
    /// The carrier numbering facts <see cref="RenderIntoCarrier"/> needs BEFORE rendering: the referencable
    /// <c>w:num</c> id set, the collision-safe allocation base (max instance/abstract ids), and a
    /// per-(instance, level) ordered-vs-bullet classification for the F2 kind guard.
    /// </summary>
    internal sealed class CarrierNumberingScan
    {
        private readonly HashSet<int> _numIds = new();
        private readonly Dictionary<int, int> _abstractByNumId = new();
        private readonly Dictionary<(int AbstractId, int Level), bool> _bulletByAbstractLevel = new();
        private readonly Dictionary<(int NumId, int Level), bool> _bulletByInstanceOverride = new();

        public int MaxNumId { get; private set; }
        public int MaxAbstractNumId { get; private set; }

        public bool ContainsNumId(int numId) => _numIds.Contains(numId);

        /// <summary>
        /// Whether the carrier instance's scheme at <paramref name="level"/> matches the item's kind.
        /// Tolerant probe (exact level, then nearer-lower, then higher — mirroring the projector's
        /// <c>ResolveOrderedFromModel</c> posture); an UNCLASSIFIABLE id/level returns compatible — the
        /// designed same-source carrier always matches, so unknown defaults to direct reference.
        /// </summary>
        public bool IsKindCompatible(int numId, int level, bool ordered)
        {
            var isBullet = ResolveBulletness(numId, level);
            return isBullet is null || isBullet.Value != ordered;
        }

        private bool? ResolveBulletness(int numId, int level)
        {
            if (_bulletByInstanceOverride.TryGetValue((numId, level), out var overridden))
            {
                return overridden;
            }
            if (!_abstractByNumId.TryGetValue(numId, out var abstractId))
            {
                return null;
            }
            if (_bulletByAbstractLevel.TryGetValue((abstractId, level), out var exact))
            {
                return exact;
            }
            for (var probe = level - 1; probe >= 0; probe--)
            {
                if (_bulletByAbstractLevel.TryGetValue((abstractId, probe), out var lower))
                {
                    return lower;
                }
            }
            for (var probe = level + 1; probe <= 8; probe++)
            {
                if (_bulletByAbstractLevel.TryGetValue((abstractId, probe), out var higher))
                {
                    return higher;
                }
            }
            return null;
        }

        public void RecordAbstract(AbstractNum abstractNum)
        {
            if (abstractNum.AbstractNumberId?.Value is not int abstractId)
            {
                return;
            }
            MaxAbstractNumId = Math.Max(MaxAbstractNumId, abstractId);
            foreach (var level in abstractNum.Elements<Level>())
            {
                if (level.LevelIndex?.Value is int ilvl && level.NumberingFormat?.Val is { } fmt)
                {
                    _bulletByAbstractLevel[(abstractId, ilvl)] = fmt.Value == NumberFormatValues.Bullet;
                }
            }
        }

        public void RecordInstance(NumberingInstance instance)
        {
            if (instance.NumberID?.Value is not int numId)
            {
                return;
            }
            _numIds.Add(numId);
            MaxNumId = Math.Max(MaxNumId, numId);
            if (instance.AbstractNumId?.Val?.Value is int abstractId)
            {
                _abstractByNumId[numId] = abstractId;
            }
            // A w:lvlOverride carrying a FULL w:lvl redefinition can change the level's numFmt for this
            // instance only — record it so the kind guard sees the instance-effective classification.
            foreach (var levelOverride in instance.Elements<LevelOverride>())
            {
                if (levelOverride.LevelIndex?.Value is int ilvl
                    && levelOverride.GetFirstChild<Level>()?.NumberingFormat?.Val is { } fmt)
                {
                    _bulletByInstanceOverride[(numId, ilvl)] = fmt.Value == NumberFormatValues.Bullet;
                }
            }
        }
    }

    /// <summary>
    /// Task 021: inspects the carrier's numbering part via a SEPARATE READ-ONLY open of the carrier bytes —
    /// never the editable package, whose Numbering DOM would be marked for autoSave re-serialization by the
    /// mere read (the 011-T2 preserve-parts hazard). Returns the carrier's <c>w:num</c> id set + kind
    /// classification (for direct reference) and max instance/abstract ids (the collision-safe allocation
    /// base). A malformed numbering part surfaces as <see cref="ComposePatchException"/> (Step-9.5 fix F4 —
    /// the package-level open is lazy, so bytes that passed the editable open can still fail the part parse
    /// here).
    /// </summary>
    internal static CarrierNumberingScan ScanCarrierNumbering(byte[] carrierBytes)
    {
        try
        {
            return ComposeDocumentRenderer.ScanCarrierBytes(carrierBytes, doc =>
            {
                var scan = new CarrierNumberingScan();
                var numbering = doc.MainDocumentPart?.NumberingDefinitionsPart?.Numbering;
                if (numbering is null)
                {
                    return scan;
                }

                foreach (var abstractNum in numbering.Elements<AbstractNum>())
                {
                    scan.RecordAbstract(abstractNum);
                }
                foreach (var instance in numbering.Elements<NumberingInstance>())
                {
                    scan.RecordInstance(instance);
                }
                return scan;
            });
        }
        catch (Exception ex) when (ex is not ComposePatchException and not OutOfMemoryException)
        {
            throw new ComposePatchException(
                ComposePatchErrorKind.MalformedDocument,
                "The carrier .docx numbering part is not readable.",
                ex);
        }
    }

    /// <summary>
    /// Accumulates the list <c>w:num</c> instances a body render allocates: a single shared bullet instance
    /// (lazily) and one instance per restart-scoped ordered list. The heading instance (numId 1) is fixed and
    /// authored unconditionally, so it is not tracked here.
    /// </summary>
    internal sealed class NumberingPlan
    {
        private int _nextNumId;

        /// <summary>Blank-package authoring — instances allocate from <see cref="FirstListNumInstanceId"/>.</summary>
        public NumberingPlan() : this(FirstListNumInstanceId) { }

        /// <summary>Task 011 (carrier mode): allocate instances from <paramref name="firstNumId"/> — set
        /// ABOVE the carrier's own max numId so a rendered list can never capture a carrier num definition.</summary>
        public NumberingPlan(int firstNumId) => _nextNumId = firstNumId;

        /// <summary>The allocated ordered-list instance ids, in allocation order (each restarts at 1).</summary>
        public List<int> OrderedInstanceIds { get; } = new();

        /// <summary>The shared bullet-list instance id, or null when no bullet list was rendered.</summary>
        public int? BulletInstanceId { get; private set; }

        /// <summary>Allocates a fresh ordered-list instance (a new numbered list that restarts at 1).</summary>
        public int NewOrderedInstance()
        {
            var id = _nextNumId++;
            OrderedInstanceIds.Add(id);
            return id;
        }

        /// <summary>Returns the shared bullet-list instance id, allocating it on first use.</summary>
        public int BulletInstance() => BulletInstanceId ??= _nextNumId++;
    }
}

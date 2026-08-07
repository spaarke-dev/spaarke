using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// Task 030 (spaarkeai-compose-r6, FR-05) — the house-style chrome engine: merges the editor's rendered
/// <c>.docx</c> BODY into a firm/matter <c>.dotx</c> template via DIRECT OOXML part-merge, so the output's
/// <c>styles.xml</c> / <c>numbering.xml</c> / theme / headers / footers / <c>sectPr</c> come from the
/// TEMPLATE and the body content comes from the EDITOR. <c>byte[]</c>-in / <c>byte[]</c>-out (ADR-007).
/// </summary>
/// <remarks>
/// <para>
/// <b>Template-as-base (the design decision, task 030 step 2)</b>: the merged package IS the template
/// package re-typed to a document (<see cref="WordprocessingDocument.ChangeDocumentType"/>). That makes the
/// template's chrome adoption structurally free — its <c>StyleDefinitionsPart</c>,
/// <c>NumberingDefinitionsPart</c>, <c>ThemePart</c>, <c>HeaderPart</c>s/<c>FooterPart</c>s, font table,
/// settings, and the trailing <c>sectPr</c> (whose header/footer <c>r:id</c>s point at the template's own
/// parts) all remain in place with VALID relationships. Only the grafted body needs reconciliation:
/// </para>
/// <list type="bullet">
///   <item><b>Style ids</b> — body <c>pStyle</c>/<c>rStyle</c>/<c>tblStyle</c> references resolve against
///   the template's styles. On id collision the TEMPLATE'S definition WINS (that is the feature — house
///   style restyles the body). Styles the body references that the template does NOT define are grafted
///   from the source package (transitively over <c>basedOn</c>/<c>link</c>/<c>next</c>) so nothing renders
///   unstyled.</item>
///   <item><b>Numbering identity (acceptance criterion 3)</b> — the body's <c>numId</c> references are
///   REMAPPED, never recomputed: each referenced source <c>w:num</c> + its <c>w:abstractNum</c> is cloned
///   VERBATIM into the template's numbering part under fresh ids (offset past the template's own), and the
///   grafted body's <c>w:numId</c> values are rewritten through the map. Identical definitions ⇒ identical
///   computed numbers (the <c>NumberingComputationEngine</c> output survives untouched).</item>
///   <item><b>Relationships</b> — every <c>r:</c>-namespace reference in the grafted body (hyperlinks,
///   images, mid-body header refs, …) is re-created on the template's main part: hyperlink/external
///   relationships are re-added with the same target URI; part references are deep-copied cross-package via
///   <see cref="OpenXmlPartContainer.AddPart{T}(T)"/>. An unresolvable reference drops its hosting element
///   LOUDLY (<c>template-merge-unresolved-reference</c>) rather than shipping a repair-prompt docx.</item>
///   <item><b>Comments / footnotes / endnotes</b> — referenced story content is carried over (whole-part
///   copy when the template has none; per-item id-remapped merge when it does), so a rendered body's
///   comment anchors survive the restyle.</item>
/// </list>
/// <para>
/// <b>NOT <c>altChunk</c> (FR-05 hard requirement)</b>: <c>altChunk</c> embeds foreign content for Word to
/// merge at open time — the template's chrome would not be adopted server-side and the output would not be
/// inspectable by the provenance seam (task 033). This engine produces the final merged package itself.
/// </para>
/// <para>
/// <b>Purity</b>: pure OOXML packaging on the already-referenced DocumentFormat.OpenXml 3.5.1 — no AI
/// internals (ADR-013), no <c>Microsoft.Graph</c> (ADR-007), no dispatch (ADR-039). Thread-safe stateless
/// singleton (ADR-010). Template STORAGE and variable rendering are task 031's seam
/// (<c>template</c> entity + <c>ITemplateEngine</c>) — this class never fetches anything.
/// </para>
/// </remarks>
public sealed class ComposeTemplatePartMergeEngine
{
    private const string RelationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>
    /// Merges <paramref name="renderedBodyDocx"/>'s body into <paramref name="templateDotx"/>'s chrome and
    /// returns the merged <c>.docx</c> bytes. Degradations (dropped unresolvable references, dropped
    /// story content on part conflicts) are reported through <paramref name="warnings"/> — loudly, never
    /// silently (operator principle).
    /// </summary>
    /// <param name="renderedBodyDocx">The editor's rendered document (ComposeDocumentRenderer output — or any
    /// valid docx whose body is the content source).</param>
    /// <param name="templateDotx">The firm/matter template package (<c>.dotx</c> or <c>.docx</c> — the
    /// output is always re-typed to a document).</param>
    /// <param name="warnings">Optional degradation sink (<c>template-merge-*</c> codes).</param>
    public byte[] Merge(byte[] renderedBodyDocx, byte[] templateDotx, ICollection<ComposeProjectionWarning>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(renderedBodyDocx);
        ArgumentNullException.ThrowIfNull(templateDotx);

        using var sourceStream = new MemoryStream(renderedBodyDocx, writable: false);
        using var source = WordprocessingDocument.Open(sourceStream, isEditable: false);
        var sourceMain = source.MainDocumentPart
            ?? throw new ArgumentException("Rendered body docx has no main document part.", nameof(renderedBodyDocx));
        var sourceBody = sourceMain.Document?.Body
            ?? throw new ArgumentException("Rendered body docx has no body.", nameof(renderedBodyDocx));

        var output = new MemoryStream();
        output.Write(templateDotx, 0, templateDotx.Length);
        output.Position = 0;

        using (var merged = WordprocessingDocument.Open(output, isEditable: true))
        {
            if (merged.DocumentType != WordprocessingDocumentType.Document)
            {
                merged.ChangeDocumentType(WordprocessingDocumentType.Document);
            }

            var mergedMain = merged.MainDocumentPart
                ?? throw new ArgumentException("Template has no main document part.", nameof(templateDotx));
            mergedMain.Document ??= new Document(new Body());
            var mergedBody = mergedMain.Document.Body ??= new Body();

            // ── 1. Template body reset: its boilerplate content goes; its trailing sectPr (the chrome —
            // page setup + header/footer references into the template's OWN parts) stays.
            var templateSectPr = mergedBody.Elements<SectionProperties>().LastOrDefault();
            if (templateSectPr is null)
            {
                // A sectPr-less template cannot supply page chrome — adopt the body's so the output stays
                // a valid single-section document, and say so loudly.
                templateSectPr = sourceBody.Elements<SectionProperties>().LastOrDefault()?.CloneNode(true) as SectionProperties;
                Warn(warnings, "template-merge-missing-sectpr", 1,
                    "The template has no trailing sectPr; the document's own page setup was kept.");
                // Header/footer references inside the ADOPTED sectPr point at SOURCE parts — reconciled in
                // the relationship pass below (it walks the whole grafted tree incl. this sectPr).
            }
            mergedBody.RemoveAllChildren();

            // ── 2. Collect what the grafted body will need from the source package.
            var bodyChildren = sourceBody.ChildElements
                .Where(c => c is not SectionProperties)
                .Select(c => c.CloneNode(true))
                .ToList();
            if (templateSectPr is not null && templateSectPr.Parent is not null)
            {
                templateSectPr = (SectionProperties)templateSectPr.CloneNode(true);
            }

            var neededStyleIds = CollectReferencedStyleIds(bodyChildren);
            var neededNumIds = CollectReferencedNumIds(bodyChildren);

            // Source numbering definitions pull in style-link + per-level style dependencies.
            var sourceNumbering = sourceMain.NumberingDefinitionsPart?.Numbering;
            foreach (var styleId in CollectNumberingStyleDependencies(sourceNumbering, neededNumIds))
            {
                neededStyleIds.Add(styleId);
            }

            // ── 3. Styles: template wins on collision; graft the transitive closure of what's missing.
            var graftedStyles = GraftMissingStyles(sourceMain, mergedMain, neededStyleIds, warnings);

            // Grafted style definitions may themselves reference numbering (style-attached lists).
            foreach (var numIdVal in graftedStyles.SelectMany(s => s.Descendants<NumberingId>())
                         .Select(n => n.Val?.Value).OfType<int>())
            {
                neededNumIds.Add(numIdVal);
            }

            // ── 4. Numbering: clone referenced num + abstractNum VERBATIM under remapped ids (identity
            // preserved — never recomputed), then rewrite refs in the grafted styles + body.
            var numIdMap = GraftNumbering(sourceNumbering, mergedMain, neededNumIds, warnings);
            foreach (var element in graftedStyles.Cast<OpenXmlElement>().Concat(bodyChildren))
            {
                RemapNumberingIds(element, numIdMap);
            }

            // ── 5. Story parts referenced from the body (comments / footnotes / endnotes).
            var commentIdMap = MergeCommentsPart(sourceMain, mergedMain, bodyChildren, warnings);
            var footnoteIdMap = MergeStoryPart(
                sourceMain.FootnotesPart?.Footnotes,
                () => mergedMain.FootnotesPart,
                () => mergedMain.AddNewPart<FootnotesPart>(),
                p => p.Footnotes,
                (p, root) => p.Footnotes = (Footnotes)root,
                bodyChildren.SelectMany(c => c.Descendants<FootnoteReference>()).Select(r => r.Id?.Value).OfType<long>());
            var endnoteIdMap = MergeStoryPart(
                sourceMain.EndnotesPart?.Endnotes,
                () => mergedMain.EndnotesPart,
                () => mergedMain.AddNewPart<EndnotesPart>(),
                p => p.Endnotes,
                (p, root) => p.Endnotes = (Endnotes)root,
                bodyChildren.SelectMany(c => c.Descendants<EndnoteReference>()).Select(r => r.Id?.Value).OfType<long>());

            // ── 6. Graft the body ahead of the template's sectPr, then reconcile every r:-namespace
            // reference the grafted content carries.
            foreach (var child in bodyChildren)
            {
                mergedBody.AppendChild(child);
            }
            if (templateSectPr is not null)
            {
                mergedBody.AppendChild(templateSectPr);
            }

            RemapCommentIds(mergedBody, commentIdMap);
            RemapFootnoteEndnoteIds(mergedBody, footnoteIdMap, endnoteIdMap);
            ReconcileRelationshipReferences(sourceMain, mergedMain, bodyChildren, warnings);

            mergedMain.Document.Save();
        }

        return output.ToArray();
    }

    // ───────────────────────────────── styles ─────────────────────────────────

    private static HashSet<string> CollectReferencedStyleIds(IEnumerable<OpenXmlElement> elements)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in elements)
        {
            foreach (var d in element.Descendants())
            {
                var val = d switch
                {
                    ParagraphStyleId p => p.Val?.Value,
                    RunStyle r => r.Val?.Value,
                    TableStyle t => t.Val?.Value,
                    _ => null,
                };
                if (!string.IsNullOrEmpty(val)) ids.Add(val);
            }
        }
        return ids;
    }

    private static IEnumerable<string> CollectNumberingStyleDependencies(Numbering? sourceNumbering, IReadOnlySet<int> numIds)
    {
        if (sourceNumbering is null || numIds.Count == 0) yield break;
        foreach (var abstractNum in ResolveAbstractNums(sourceNumbering, numIds).Values)
        {
            var link = abstractNum.GetFirstChild<NumberingStyleLink>()?.Val?.Value
                       ?? abstractNum.GetFirstChild<StyleLink>()?.Val?.Value;
            if (!string.IsNullOrEmpty(link)) yield return link;
            foreach (var lvlStyle in abstractNum.Descendants<ParagraphStyleIdInLevel>())
            {
                if (!string.IsNullOrEmpty(lvlStyle.Val?.Value)) yield return lvlStyle.Val!.Value!;
            }
        }
    }

    /// <summary>Grafts source style definitions the template lacks (transitive over basedOn/link/next).
    /// Returns the grafted clones (their numbering refs still carry SOURCE numIds — caller remaps).</summary>
    private static List<Style> GraftMissingStyles(
        MainDocumentPart sourceMain,
        MainDocumentPart mergedMain,
        HashSet<string> neededStyleIds,
        ICollection<ComposeProjectionWarning>? warnings)
    {
        var grafted = new List<Style>();
        if (neededStyleIds.Count == 0) return grafted;

        var sourceStyles = sourceMain.StyleDefinitionsPart?.Styles;
        if (sourceStyles is null) return grafted;

        if (mergedMain.StyleDefinitionsPart is null)
        {
            // Chrome-less template: adopt the source catalog wholesale so the body doesn't render unstyled.
            var part = mergedMain.AddNewPart<StyleDefinitionsPart>();
            part.Styles = (Styles)sourceStyles.CloneNode(true);
            Warn(warnings, "template-merge-template-has-no-styles", 1,
                "The template supplies no styles.xml; the document's own style catalog was kept.");
            // The wholesale-cloned catalog carries SOURCE numbering refs — report it as "grafted" so the
            // caller's numId collection + remap cover it like any other graft.
            return part.Styles.Elements<Style>().ToList();
        }

        var mergedStyles = mergedMain.StyleDefinitionsPart.Styles ??= new Styles();
        var mergedIds = new HashSet<string>(
            mergedStyles.Elements<Style>().Select(s => s.StyleId?.Value).OfType<string>(),
            StringComparer.Ordinal);
        var sourceById = sourceStyles.Elements<Style>()
            .Where(s => !string.IsNullOrEmpty(s.StyleId?.Value))
            .GroupBy(s => s.StyleId!.Value!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var queue = new Queue<string>(neededStyleIds);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!visited.Add(id) || mergedIds.Contains(id)) continue; // template wins on collision
            if (!sourceById.TryGetValue(id, out var sourceStyle))
            {
                // Referenced but defined nowhere — Word falls back to defaults; not a merge defect.
                continue;
            }

            var clone = (Style)sourceStyle.CloneNode(true);
            mergedStyles.AppendChild(clone);
            mergedIds.Add(id);
            grafted.Add(clone);

            foreach (var dep in new[]
                     {
                         sourceStyle.BasedOn?.Val?.Value,
                         sourceStyle.LinkedStyle?.Val?.Value,
                         sourceStyle.NextParagraphStyle?.Val?.Value,
                     })
            {
                if (!string.IsNullOrEmpty(dep)) queue.Enqueue(dep!);
            }
        }

        return grafted;
    }

    // ──────────────────────────────── numbering ───────────────────────────────

    private static HashSet<int> CollectReferencedNumIds(IEnumerable<OpenXmlElement> elements)
    {
        var ids = new HashSet<int>();
        foreach (var element in elements)
        {
            foreach (var numId in element.Descendants<NumberingId>())
            {
                if (numId.Val?.Value is int v && v > 0) ids.Add(v);
            }
        }
        return ids;
    }

    private static Dictionary<int, AbstractNum> ResolveAbstractNums(Numbering sourceNumbering, IReadOnlySet<int> numIds)
    {
        var abstractById = sourceNumbering.Elements<AbstractNum>()
            .Where(a => a.AbstractNumberId?.Value is not null)
            .ToDictionary(a => a.AbstractNumberId!.Value);
        var result = new Dictionary<int, AbstractNum>();
        foreach (var num in sourceNumbering.Elements<NumberingInstance>())
        {
            if (num.NumberID?.Value is not int numId || !numIds.Contains(numId)) continue;
            if (num.GetFirstChild<AbstractNumId>()?.Val?.Value is not int absId) continue;
            if (abstractById.TryGetValue(absId, out var abs)) result[absId] = abs;
        }
        return result;
    }

    /// <summary>Clones each referenced source num + abstractNum VERBATIM into the template's numbering part
    /// under offset ids. Returns the numId map (source → merged). Identity preserved — never recomputed.</summary>
    private static Dictionary<int, int> GraftNumbering(
        Numbering? sourceNumbering,
        MainDocumentPart mergedMain,
        IReadOnlySet<int> neededNumIds,
        ICollection<ComposeProjectionWarning>? warnings)
    {
        var map = new Dictionary<int, int>();
        if (sourceNumbering is null || neededNumIds.Count == 0) return map;

        var numberingPart = mergedMain.NumberingDefinitionsPart;
        if (numberingPart is null)
        {
            numberingPart = mergedMain.AddNewPart<NumberingDefinitionsPart>();
            numberingPart.Numbering = new Numbering();
        }
        var mergedNumbering = numberingPart.Numbering ??= new Numbering();

        var nextAbstractId = mergedNumbering.Elements<AbstractNum>()
            .Select(a => a.AbstractNumberId?.Value ?? 0).DefaultIfEmpty(-1).Max() + 1;
        var nextNumId = mergedNumbering.Elements<NumberingInstance>()
            .Select(n => n.NumberID?.Value ?? 0).DefaultIfEmpty(0).Max() + 1;

        // One remapped abstractNum per SOURCE abstractNum (nums sharing an abstract keep sharing it —
        // restart/override semantics depend on that shape).
        var abstractMap = new Dictionary<int, int>();
        var lastAbstract = mergedNumbering.Elements<AbstractNum>().LastOrDefault();
        var sourceAbstracts = sourceNumbering.Elements<AbstractNum>()
            .Where(a => a.AbstractNumberId?.Value is not null)
            .ToDictionary(a => a.AbstractNumberId!.Value);

        foreach (var sourceNum in sourceNumbering.Elements<NumberingInstance>())
        {
            if (sourceNum.NumberID?.Value is not int sourceNumId || !neededNumIds.Contains(sourceNumId)) continue;
            if (map.ContainsKey(sourceNumId)) continue;
            if (sourceNum.GetFirstChild<AbstractNumId>()?.Val?.Value is not int sourceAbsId
                || !sourceAbstracts.TryGetValue(sourceAbsId, out var sourceAbs))
            {
                Warn(warnings, "template-merge-numbering-unresolved", 1,
                    $"numId {sourceNumId} has no resolvable abstractNum; its paragraphs keep the reference but may render unnumbered.");
                continue;
            }

            if (!abstractMap.TryGetValue(sourceAbsId, out var newAbsId))
            {
                newAbsId = nextAbstractId++;
                abstractMap[sourceAbsId] = newAbsId;
                var absClone = (AbstractNum)sourceAbs.CloneNode(true);
                absClone.AbstractNumberId = newAbsId;
                // Schema order: every abstractNum precedes every num.
                if (lastAbstract is null)
                {
                    lastAbstract = mergedNumbering.FirstChild is null
                        ? mergedNumbering.AppendChild(absClone)
                        : mergedNumbering.InsertBefore(absClone, mergedNumbering.FirstChild);
                }
                else
                {
                    lastAbstract = mergedNumbering.InsertAfter(absClone, lastAbstract);
                }
            }

            var newNumId = nextNumId++;
            map[sourceNumId] = newNumId;
            var numClone = (NumberingInstance)sourceNum.CloneNode(true);
            numClone.NumberID = newNumId;
            numClone.GetFirstChild<AbstractNumId>()!.Val = abstractMap[sourceAbsId];
            mergedNumbering.AppendChild(numClone);
        }

        return map;
    }

    private static void RemapNumberingIds(OpenXmlElement root, IReadOnlyDictionary<int, int> numIdMap)
    {
        if (numIdMap.Count == 0) return;
        foreach (var numId in root.Descendants<NumberingId>())
        {
            if (numId.Val?.Value is int v && numIdMap.TryGetValue(v, out var mapped))
            {
                numId.Val = mapped;
            }
        }
    }

    // ─────────────────────────── comments / story parts ───────────────────────

    /// <summary>Carries the source comments the grafted body anchors into the merged package. Whole-part
    /// copy when the template has no comments part; per-comment id-remapped merge when it does.</summary>
    private static Dictionary<string, string> MergeCommentsPart(
        MainDocumentPart sourceMain,
        MainDocumentPart mergedMain,
        IReadOnlyList<OpenXmlElement> bodyChildren,
        ICollection<ComposeProjectionWarning>? warnings)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var referencedIds = bodyChildren
            .SelectMany(c => c.Descendants())
            .Select(d => d switch
            {
                CommentReference r => r.Id?.Value,
                CommentRangeStart s => s.Id?.Value,
                CommentRangeEnd e => e.Id?.Value,
                _ => null,
            })
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        if (referencedIds.Count == 0) return map;

        var sourceComments = sourceMain.WordprocessingCommentsPart?.Comments;
        if (sourceComments is null) return map;

        var mergedPart = mergedMain.WordprocessingCommentsPart;
        if (mergedPart is null)
        {
            mergedPart = mergedMain.AddNewPart<WordprocessingCommentsPart>();
            mergedPart.Comments = new Comments();
        }
        var mergedComments = mergedPart.Comments ??= new Comments();

        var takenIds = mergedComments.Elements<Comment>()
            .Select(c => c.Id?.Value).OfType<string>()
            .Select(v => long.TryParse(v, out var n) ? n : 0)
            .ToHashSet();
        var nextId = takenIds.Count == 0 ? 1 : takenIds.Max() + 1;

        foreach (var comment in sourceComments.Elements<Comment>())
        {
            var id = comment.Id?.Value;
            if (id is null || !referencedIds.Contains(id)) continue;
            var clone = (Comment)comment.CloneNode(true);
            if (long.TryParse(id, out var numeric) && !takenIds.Contains(numeric))
            {
                takenIds.Add(numeric);
            }
            else
            {
                clone.Id = nextId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                map[id] = clone.Id!.Value!;
                takenIds.Add(nextId);
                nextId++;
            }
            mergedComments.AppendChild(clone);
        }

        return map;
    }

    private static void RemapCommentIds(OpenXmlElement root, IReadOnlyDictionary<string, string> map)
    {
        if (map.Count == 0) return;
        foreach (var d in root.Descendants())
        {
            switch (d)
            {
                case CommentReference r when r.Id?.Value is string v && map.TryGetValue(v, out var m): r.Id = m; break;
                case CommentRangeStart s when s.Id?.Value is string v && map.TryGetValue(v, out var m): s.Id = m; break;
                case CommentRangeEnd e when e.Id?.Value is string v && map.TryGetValue(v, out var m): e.Id = m; break;
            }
        }
    }

    /// <summary>Merges referenced footnote/endnote items into the template's story part. Items with id ≥ 1
    /// (real content — 0/-1 are Word's separators) are cloned with ids offset past the template's own.</summary>
    private static Dictionary<long, long> MergeStoryPart<TPart>(
        OpenXmlElement? sourceRoot,
        Func<TPart?> getPart,
        Func<TPart> addPart,
        Func<TPart, OpenXmlElement?> getRoot,
        Action<TPart, OpenXmlElement> setRoot,
        IEnumerable<long> referencedIds)
        where TPart : OpenXmlPart
    {
        var map = new Dictionary<long, long>();
        var needed = referencedIds.ToHashSet();
        if (needed.Count == 0 || sourceRoot is null) return map;

        var part = getPart();
        if (part is null)
        {
            part = addPart();
            setRoot(part, sourceRoot.CloneNode(true));
            // Whole-part copy — ids preserved, body references stay valid as-is.
            return map;
        }

        var mergedRoot = getRoot(part);
        if (mergedRoot is null)
        {
            setRoot(part, sourceRoot.CloneNode(true));
            return map;
        }

        var taken = mergedRoot.ChildElements
            .Select(GetStoryId).OfType<long>().ToHashSet();
        var nextId = taken.Count == 0 ? 1 : Math.Max(1, taken.Max() + 1);

        foreach (var item in sourceRoot.ChildElements)
        {
            if (GetStoryId(item) is not long id || id < 1 || !needed.Contains(id)) continue;
            var clone = item.CloneNode(true);
            if (taken.Contains(id))
            {
                SetStoryId(clone, nextId);
                map[id] = nextId;
                taken.Add(nextId);
                nextId++;
            }
            else
            {
                taken.Add(id);
            }
            mergedRoot.AppendChild(clone);
        }

        return map;
    }

    private static long? GetStoryId(OpenXmlElement element) => element switch
    {
        Footnote f => f.Id?.Value,
        Endnote e => e.Id?.Value,
        _ => null,
    };

    private static void SetStoryId(OpenXmlElement element, long id)
    {
        switch (element)
        {
            case Footnote f: f.Id = id; break;
            case Endnote e: e.Id = id; break;
        }
    }

    private static void RemapFootnoteEndnoteIds(
        OpenXmlElement root,
        IReadOnlyDictionary<long, long> footnoteMap,
        IReadOnlyDictionary<long, long> endnoteMap)
    {
        if (footnoteMap.Count == 0 && endnoteMap.Count == 0) return;
        foreach (var d in root.Descendants())
        {
            switch (d)
            {
                case FootnoteReference f when f.Id?.Value is long v && footnoteMap.TryGetValue(v, out var m): f.Id = m; break;
                case EndnoteReference e when e.Id?.Value is long v && endnoteMap.TryGetValue(v, out var m): e.Id = m; break;
            }
        }
    }

    // ─────────────────────────────── relationships ────────────────────────────

    /// <summary>Re-creates every r:-namespace reference the grafted body carries on the merged main part:
    /// hyperlinks/external rels by target URI, part references by cross-package deep copy. Unresolvable
    /// references drop their hosting element LOUDLY (never a dangling rId / Word repair prompt).</summary>
    private static void ReconcileRelationshipReferences(
        MainDocumentPart sourceMain,
        MainDocumentPart mergedMain,
        IReadOnlyList<OpenXmlElement> bodyChildren,
        ICollection<ComposeProjectionWarning>? warnings)
    {
        var idMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var unresolved = new HashSet<OpenXmlElement>();

        foreach (var child in bodyChildren)
        {
            foreach (var element in new[] { child }.Concat(child.Descendants()).ToList())
            {
                foreach (var attr in element.GetAttributes())
                {
                    if (attr.NamespaceUri != RelationshipNs || string.IsNullOrEmpty(attr.Value)) continue;
                    var oldId = attr.Value!;

                    if (!idMap.TryGetValue(oldId, out var newId))
                    {
                        newId = RecreateRelationship(sourceMain, mergedMain, oldId)!;
                        if (newId is null)
                        {
                            unresolved.Add(element);
                            continue;
                        }
                        idMap[oldId] = newId;
                    }

                    element.SetAttribute(new OpenXmlAttribute(attr.Prefix, attr.LocalName, attr.NamespaceUri, newId));
                }
            }
        }

        if (unresolved.Count > 0)
        {
            foreach (var element in unresolved)
            {
                // Drop the smallest self-contained host: the run child (drawing/pict/object) when the
                // reference sits inside a run, else the element itself. Parent guard: a prior removal may
                // have already detached this element's subtree.
                var host = element.Ancestors().TakeWhile(a => a is not Body)
                    .LastOrDefault(a => a.Parent is Run) ?? element;
                if (host.Parent is not null) host.Remove();
            }
            Warn(warnings, "template-merge-unresolved-reference", unresolved.Count,
                "Content referencing a relationship that could not be carried into the template was dropped.");
        }
    }

    /// <summary>Resolves <paramref name="oldId"/> against the source main part and re-creates the same
    /// relationship on the merged main part. Returns the new id, or null when unresolvable.</summary>
    private static string? RecreateRelationship(MainDocumentPart sourceMain, MainDocumentPart mergedMain, string oldId)
    {
        var hyperlink = sourceMain.HyperlinkRelationships.FirstOrDefault(h => h.Id == oldId);
        if (hyperlink is not null)
        {
            return mergedMain.AddHyperlinkRelationship(hyperlink.Uri, hyperlink.IsExternal).Id;
        }

        var external = sourceMain.ExternalRelationships.FirstOrDefault(e => e.Id == oldId);
        if (external is not null)
        {
            return mergedMain.AddExternalRelationship(external.RelationshipType, external.Uri).Id;
        }

        try
        {
            var part = sourceMain.GetPartById(oldId);
            var added = mergedMain.AddPart(part); // cross-package deep copy (incl. sub-parts)
            return mergedMain.GetIdOfPart(added);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null; // no such relationship in the source — dangling in the input
        }
        catch (InvalidOperationException)
        {
            return null; // part type not addable (e.g., singleton conflict) — drop loudly upstream
        }
    }

    private static void Warn(ICollection<ComposeProjectionWarning>? warnings, string code, int count, string detail)
    {
        warnings?.Add(new ComposeProjectionWarning(code, count, detail));
    }
}

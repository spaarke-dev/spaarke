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
/// parts) all remain in place with VALID relationships. Only grafted content needs reconciliation:
/// </para>
/// <list type="bullet">
///   <item><b>Style ids</b> — body <c>pStyle</c>/<c>rStyle</c>/<c>tblStyle</c> references resolve against
///   the template's styles. On id collision the TEMPLATE'S definition WINS (that is the feature — house
///   style restyles the body). Styles the grafted content references that the template does NOT define are
///   grafted from the source package over a FIXED-POINT closure (basedOn/link/next chains, style-attached
///   numbering, numbering style-links — 030-review F8) so nothing renders unstyled or mis-linked.</item>
///   <item><b>Numbering identity (acceptance criterion 3)</b> — grafted <c>numId</c> references are
///   REMAPPED, never recomputed: each referenced source <c>w:num</c> + its <c>w:abstractNum</c> (+ any
///   referenced <c>w:numPicBullet</c>) is cloned VERBATIM into the template's numbering part under fresh
///   ids — at the schema-correct insertion points (<c>numPicBullet* → abstractNum* → num* →
///   numIdMacAtCleanup?</c>, 030-review F5) — and the grafted content's references are rewritten through
///   the map. Identical definitions ⇒ identical computed numbers. A numbering reference that cannot be
///   resolved in the source is STRIPPED loudly (<c>template-merge-numbering-unresolved</c>) rather than
///   silently capturing the template's same-id scheme (030-review F7).</item>
///   <item><b>Relationships</b> — every <c>r:</c>-namespace reference in grafted content is re-created on
///   the part that now hosts it (main part for the body; comments/footnotes/endnotes/numbering parts for
///   their own carried content — 030-review F2): hyperlink/external relationships re-added by target URI,
///   part references deep-copied cross-package via <see cref="OpenXmlPartContainer.AddPart{T}(T)"/>. An
///   unresolvable reference unwraps (hyperlink text survives) or drops its hosting element LOUDLY
///   (<c>template-merge-unresolved-reference</c>) rather than shipping a repair-prompt docx.</item>
///   <item><b>Comments / footnotes / endnotes</b> — story content the body anchors is carried with
///   collision-proof id allocation (030-review F4); dangling story references are stripped loudly rather
///   than cross-wiring to a same-id template item (030-review F9). Modern comment metadata
///   (<c>commentsExtended</c> threading/resolution) is NOT carried — degraded loudly.</item>
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
    /// returns the merged <c>.docx</c> bytes. Degradations (stripped unresolvable references / numbering /
    /// story anchors, dropped macros or comment threading) are reported through <paramref name="warnings"/>
    /// — loudly, never silently (operator principle).
    /// </summary>
    /// <param name="renderedBodyDocx">The editor's rendered document (ComposeDocumentRenderer output — or any
    /// valid docx whose body is the content source).</param>
    /// <param name="templateDotx">The firm/matter template package (<c>.dotx</c> or <c>.docx</c>; a
    /// macro-enabled input has its VBA project stripped loudly — the output is always a plain document).</param>
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

            // 030-review F12: a macro-enabled template input would ship an orphan VBA project inside a
            // re-typed Document (content-type mismatch). Strip it, loudly.
            if (mergedMain.VbaProjectPart is not null)
            {
                mergedMain.DeletePart(mergedMain.VbaProjectPart);
                Warn(warnings, "template-merge-macros-stripped", 1,
                    "The template was macro-enabled; its VBA project was removed (Compose outputs are plain documents).");
            }

            // ── 1. Template body reset: its boilerplate content goes; its trailing sectPr (the chrome —
            // page setup + header/footer references into the template's OWN parts) stays.
            var templateSectPr = mergedBody.Elements<SectionProperties>().LastOrDefault();
            var sectPrAdoptedFromSource = false;
            if (templateSectPr is not null)
            {
                templateSectPr = (SectionProperties)templateSectPr.CloneNode(true);
            }
            else
            {
                // A sectPr-less template cannot supply page chrome — adopt the body's so the output stays
                // a valid single-section document, and say so loudly. The adopted sectPr's header/footer
                // r:ids point at SOURCE parts — it JOINS the reconciliation roots below (030-review F1).
                templateSectPr = sourceBody.Elements<SectionProperties>().LastOrDefault()?.CloneNode(true) as SectionProperties;
                sectPrAdoptedFromSource = templateSectPr is not null;
                Warn(warnings, "template-merge-missing-sectpr", 1,
                    "The template has no trailing sectPr; the document's own page setup was kept.");
            }
            mergedBody.RemoveAllChildren();

            // ── 2. Clone the graft content. mainContentRoots = everything whose relationships resolve
            // against the SOURCE MAIN part (body + an adopted sectPr).
            var bodyChildren = sourceBody.ChildElements
                .Where(c => c is not SectionProperties)
                .Select(c => c.CloneNode(true))
                .ToList();
            var mainContentRoots = new List<OpenXmlElement>(bodyChildren);
            if (sectPrAdoptedFromSource && templateSectPr is not null)
            {
                mainContentRoots.Add(templateSectPr);
            }

            // ── 3. Story content the body anchors (comments / footnotes / endnotes), cloned EARLY so it
            // participates in style/numbering collection + remap like any other grafted content (F3).
            var story = PrepareStoryClones(sourceMain, bodyChildren, warnings);
            var allGraftRoots = mainContentRoots.Concat(story.AllClones).ToList();

            // ── 4. Fixed-point dependency closure over the SOURCE catalogs (F8): styles pull styles
            // (basedOn/link/next) and numbering (style-attached numPr); numbering pulls styles
            // (numStyleLink/styleLink + per-level pStyle). Iterate until stable.
            var (neededStyleIds, neededNumIds) = ComputeDependencyClosure(sourceMain, allGraftRoots);

            // ── 5. Styles: template wins on collision; graft what's missing (closure already complete).
            var graftedStyles = GraftMissingStyles(sourceMain, mergedMain, neededStyleIds, warnings);

            // ── 6. Numbering: verbatim clones under remapped ids at schema-correct positions (F5), with
            // numPicBullet carry (F6). Unresolvable references collected for loud stripping (F7).
            var numGraft = GraftNumbering(sourceMain, mergedMain, neededNumIds, warnings);
            foreach (var root in allGraftRoots.Concat(graftedStyles))
            {
                RemapNumberingIds(root, numGraft.NumIdMap);
            }
            StripUnresolvedNumbering(allGraftRoots.Concat(graftedStyles), numGraft, warnings);

            // ── 7. Attach story clones to their target parts (collision-proof ids, F4) and remap the
            // body's story references; dangling references were already stripped in step 3.
            AttachStoryClones(sourceMain, mergedMain, story, mergedBodyRoots: mainContentRoots, warnings);

            // ── 8. Graft the body ahead of the template's sectPr.
            foreach (var child in bodyChildren)
            {
                mergedBody.AppendChild(child);
            }
            if (templateSectPr is not null)
            {
                mergedBody.AppendChild(templateSectPr);
            }

            // ── 9. Relationship reconciliation, PER HOSTING PART (F2): body + adopted sectPr against the
            // main part; carried story/numbering content against its own source → target part pair.
            var dropped = 0;
            dropped += ReconcileRelationshipReferences(sourceMain, mergedMain, mainContentRoots);
            if (story.CommentClones.Count > 0 && sourceMain.WordprocessingCommentsPart is not null)
            {
                dropped += ReconcileRelationshipReferences(
                    sourceMain.WordprocessingCommentsPart, mergedMain.WordprocessingCommentsPart!, story.CommentClones);
            }
            if (story.FootnoteClones.Count > 0 && sourceMain.FootnotesPart is not null)
            {
                dropped += ReconcileRelationshipReferences(
                    sourceMain.FootnotesPart, mergedMain.FootnotesPart!, story.FootnoteClones);
            }
            if (story.EndnoteClones.Count > 0 && sourceMain.EndnotesPart is not null)
            {
                dropped += ReconcileRelationshipReferences(
                    sourceMain.EndnotesPart, mergedMain.EndnotesPart!, story.EndnoteClones);
            }
            if (numGraft.CopiedPictureBullets.Count > 0 && sourceMain.NumberingDefinitionsPart is not null)
            {
                dropped += ReconcileRelationshipReferences(
                    sourceMain.NumberingDefinitionsPart, mergedMain.NumberingDefinitionsPart!, numGraft.CopiedPictureBullets);
            }
            if (dropped > 0)
            {
                Warn(warnings, "template-merge-unresolved-reference", dropped,
                    "Content referencing a relationship that could not be carried into the template was dropped.");
            }

            mergedMain.Document.Save();
        }

        return output.ToArray();
    }

    // ─────────────────────────── dependency closure (F8) ──────────────────────

    private static (HashSet<string> StyleIds, HashSet<int> NumIds) ComputeDependencyClosure(
        MainDocumentPart sourceMain,
        IReadOnlyList<OpenXmlElement> graftRoots)
    {
        var styleIds = CollectReferencedStyleIds(graftRoots);
        var numIds = CollectReferencedNumIds(graftRoots);

        var sourceStyles = sourceMain.StyleDefinitionsPart?.Styles?.Elements<Style>()
            .Where(s => !string.IsNullOrEmpty(s.StyleId?.Value))
            .GroupBy(s => s.StyleId!.Value!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal)
            ?? new Dictionary<string, Style>(StringComparer.Ordinal);
        var sourceNumbering = sourceMain.NumberingDefinitionsPart?.Numbering;

        bool grew = true;
        var processedStyles = new HashSet<string>(StringComparer.Ordinal);
        var processedNums = new HashSet<int>();
        while (grew)
        {
            grew = false;

            foreach (var styleId in styleIds.ToList())
            {
                if (!processedStyles.Add(styleId) || !sourceStyles.TryGetValue(styleId, out var style)) continue;
                foreach (var dep in new[]
                         {
                             style.BasedOn?.Val?.Value,
                             style.LinkedStyle?.Val?.Value,
                             style.NextParagraphStyle?.Val?.Value,
                         })
                {
                    if (!string.IsNullOrEmpty(dep) && styleIds.Add(dep!)) grew = true;
                }
                foreach (var numId in style.Descendants<NumberingId>().Select(n => n.Val?.Value).OfType<int>())
                {
                    if (numId > 0 && numIds.Add(numId)) grew = true;
                }
            }

            if (sourceNumbering is not null)
            {
                foreach (var numId in numIds.ToList())
                {
                    if (!processedNums.Add(numId)) continue;
                    var abs = ResolveAbstractNum(sourceNumbering, numId);
                    if (abs is null) continue;
                    var link = abs.GetFirstChild<NumberingStyleLink>()?.Val?.Value
                               ?? abs.GetFirstChild<StyleLink>()?.Val?.Value;
                    if (!string.IsNullOrEmpty(link) && styleIds.Add(link!)) grew = true;
                    foreach (var lvlStyle in abs.Descendants<ParagraphStyleIdInLevel>())
                    {
                        var v = lvlStyle.Val?.Value;
                        if (!string.IsNullOrEmpty(v) && styleIds.Add(v!)) grew = true;
                    }
                }
            }
        }

        return (styleIds, numIds);
    }

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

    private static AbstractNum? ResolveAbstractNum(Numbering numbering, int numId)
    {
        var num = numbering.Elements<NumberingInstance>().FirstOrDefault(n => n.NumberID?.Value == numId);
        if (num?.GetFirstChild<AbstractNumId>()?.Val?.Value is not int absId) return null;
        return numbering.Elements<AbstractNum>().FirstOrDefault(a => a.AbstractNumberId?.Value == absId);
    }

    // ───────────────────────────────── styles ─────────────────────────────────

    /// <summary>Grafts source style definitions the template lacks (the closure is already transitive).
    /// Returns the grafted clones (their numbering refs still carry SOURCE numIds — caller remaps).</summary>
    private static List<Style> GraftMissingStyles(
        MainDocumentPart sourceMain,
        MainDocumentPart mergedMain,
        IReadOnlySet<string> neededStyleIds,
        ICollection<ComposeProjectionWarning>? warnings)
    {
        var grafted = new List<Style>();
        if (neededStyleIds.Count == 0) return grafted;

        var sourceStyles = sourceMain.StyleDefinitionsPart?.Styles;
        if (sourceStyles is null) return grafted;

        if (mergedMain.StyleDefinitionsPart is null)
        {
            // Chrome-less template: adopt the source catalog wholesale so the body doesn't render unstyled.
            // Reported as "grafted" so the caller's numId remap covers the catalog's numbering refs.
            var part = mergedMain.AddNewPart<StyleDefinitionsPart>();
            part.Styles = (Styles)sourceStyles.CloneNode(true);
            Warn(warnings, "template-merge-template-has-no-styles", 1,
                "The template supplies no styles.xml; the document's own style catalog was kept.");
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

        foreach (var id in neededStyleIds)
        {
            if (mergedIds.Contains(id)) continue; // template wins on collision — that IS house style
            if (!sourceById.TryGetValue(id, out var sourceStyle)) continue; // defined nowhere — Word defaults

            var clone = (Style)sourceStyle.CloneNode(true);
            mergedStyles.AppendChild(clone);
            mergedIds.Add(id);
            grafted.Add(clone);
        }

        return grafted;
    }

    // ──────────────────────────────── numbering ───────────────────────────────

    private sealed class NumberingGraftResult
    {
        /// <summary>Source numId → merged numId for every successfully grafted instance.</summary>
        public Dictionary<int, int> NumIdMap { get; } = new();

        /// <summary>Source numIds that were REFERENCED but could not be resolved to a source definition —
        /// their references must be stripped (loud), never left to capture a template scheme (F7).</summary>
        public HashSet<int> UnresolvedNumIds { get; } = new();

        /// <summary>numPicBullet clones copied into the merged numbering part (F6) — their image
        /// relationships reconcile against the numbering-part pair.</summary>
        public List<OpenXmlElement> CopiedPictureBullets { get; } = new();
    }

    /// <summary>Clones each referenced source num + abstractNum (+ referenced numPicBullets) VERBATIM into
    /// the template's numbering part under offset ids at schema-correct positions. Identity preserved —
    /// never recomputed.</summary>
    private static NumberingGraftResult GraftNumbering(
        MainDocumentPart sourceMain,
        MainDocumentPart mergedMain,
        IReadOnlySet<int> neededNumIds,
        ICollection<ComposeProjectionWarning>? warnings)
    {
        var result = new NumberingGraftResult();
        if (neededNumIds.Count == 0) return result;

        var sourceNumbering = sourceMain.NumberingDefinitionsPart?.Numbering;
        if (sourceNumbering is null)
        {
            // The graft content references numbering the source cannot define (dangling input refs) —
            // every referenced id is unresolvable (F7: strip loudly, never capture the template's scheme).
            result.UnresolvedNumIds.UnionWith(neededNumIds);
            return result;
        }

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
        var nextPicBulletId = mergedNumbering.Elements<NumberingPictureBullet>()
            .Select(p => (int?)(p.NumberingPictureBulletId?.Value ?? 0)).DefaultIfEmpty(-1).Max()!.Value + 1;

        // One remapped abstractNum per SOURCE abstractNum (nums sharing an abstract keep sharing it —
        // restart/override semantics depend on that shape). Same for picture bullets.
        var abstractMap = new Dictionary<int, int>();
        var picBulletMap = new Dictionary<int, int>();
        var sourceAbstracts = sourceNumbering.Elements<AbstractNum>()
            .Where(a => a.AbstractNumberId?.Value is not null)
            .ToDictionary(a => a.AbstractNumberId!.Value);
        var sourcePicBullets = sourceNumbering.Elements<NumberingPictureBullet>()
            .Where(p => p.NumberingPictureBulletId?.Value is not null)
            .GroupBy(p => (int)p.NumberingPictureBulletId!.Value!)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var sourceNumId in neededNumIds)
        {
            var sourceNum = sourceNumbering.Elements<NumberingInstance>()
                .FirstOrDefault(n => n.NumberID?.Value == sourceNumId);
            if (sourceNum?.GetFirstChild<AbstractNumId>()?.Val?.Value is not int sourceAbsId
                || !sourceAbstracts.TryGetValue(sourceAbsId, out var sourceAbs))
            {
                result.UnresolvedNumIds.Add(sourceNumId);
                continue;
            }

            if (!abstractMap.TryGetValue(sourceAbsId, out var newAbsId))
            {
                newAbsId = nextAbstractId++;
                abstractMap[sourceAbsId] = newAbsId;
                var absClone = (AbstractNum)sourceAbs.CloneNode(true);
                absClone.AbstractNumberId = newAbsId;

                // F6: carry any referenced picture bullets, remapping lvlPicBulletId through its own map.
                foreach (var lvlPic in absClone.Descendants<LevelPictureBulletId>())
                {
                    if (lvlPic.Val?.Value is not int picId) continue;
                    if (!picBulletMap.TryGetValue(picId, out var newPicId))
                    {
                        if (!sourcePicBullets.TryGetValue(picId, out var sourcePic))
                        {
                            continue; // dangling in the SOURCE — leave as-is; Word falls back to the char bullet
                        }
                        newPicId = nextPicBulletId++;
                        picBulletMap[picId] = newPicId;
                        var picClone = (NumberingPictureBullet)sourcePic.CloneNode(true);
                        picClone.NumberingPictureBulletId = newPicId;
                        InsertNumberingChild(mergedNumbering, picClone, NumberingSlot.PictureBullet);
                        result.CopiedPictureBullets.Add(picClone);
                    }
                    lvlPic.Val = newPicId;
                }

                InsertNumberingChild(mergedNumbering, absClone, NumberingSlot.AbstractNum);
            }

            var newNumId = nextNumId++;
            result.NumIdMap[sourceNumId] = newNumId;
            var numClone = (NumberingInstance)sourceNum.CloneNode(true);
            numClone.NumberID = newNumId;
            numClone.GetFirstChild<AbstractNumId>()!.Val = abstractMap[sourceAbsId];
            InsertNumberingChild(mergedNumbering, numClone, NumberingSlot.NumberingInstance);
        }

        return result;
    }

    private enum NumberingSlot { PictureBullet, AbstractNum, NumberingInstance }

    /// <summary>Inserts a numbering child at its schema-correct slot: <c>numPicBullet* → abstractNum* →
    /// num* → numIdMacAtCleanup?</c> (030-review F5 — append-only breaks on real template shapes).</summary>
    private static void InsertNumberingChild(Numbering numbering, OpenXmlElement child, NumberingSlot slot)
    {
        OpenXmlElement? insertAfter = slot switch
        {
            NumberingSlot.PictureBullet => numbering.Elements<NumberingPictureBullet>().LastOrDefault(),
            NumberingSlot.AbstractNum => numbering.Elements<AbstractNum>().LastOrDefault()
                ?? (OpenXmlElement?)numbering.Elements<NumberingPictureBullet>().LastOrDefault(),
            NumberingSlot.NumberingInstance => numbering.Elements<NumberingInstance>().LastOrDefault()
                ?? numbering.Elements<AbstractNum>().LastOrDefault()
                ?? (OpenXmlElement?)numbering.Elements<NumberingPictureBullet>().LastOrDefault(),
            _ => null,
        };

        if (insertAfter is not null)
        {
            numbering.InsertAfter(child, insertAfter);
        }
        else if (numbering.FirstChild is not null)
        {
            numbering.InsertBefore(child, numbering.FirstChild);
        }
        else
        {
            numbering.AppendChild(child);
        }
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

    /// <summary>030-review F7: a numbering reference that could not be grafted must NOT survive — left
    /// alone it silently captures the TEMPLATE's same-id scheme (wrong numbers). Strip the
    /// <c>w:numPr</c> (the paragraph renders visibly unnumbered) and warn.</summary>
    private static void StripUnresolvedNumbering(
        IEnumerable<OpenXmlElement> roots,
        NumberingGraftResult numGraft,
        ICollection<ComposeProjectionWarning>? warnings)
    {
        if (numGraft.UnresolvedNumIds.Count == 0) return;
        var stripped = 0;
        foreach (var root in roots)
        {
            foreach (var numPr in root.Descendants<NumberingProperties>().ToList())
            {
                if (numPr.NumberingId?.Val?.Value is int v && numGraft.UnresolvedNumIds.Contains(v))
                {
                    numPr.Remove();
                    stripped++;
                }
            }
        }
        if (stripped > 0)
        {
            Warn(warnings, "template-merge-numbering-unresolved", stripped,
                "A numbering reference could not be carried into the template; the affected paragraphs render unnumbered.");
        }
    }

    // ─────────────────────────── comments / story parts ───────────────────────

    private sealed class StoryClones
    {
        public List<OpenXmlElement> CommentClones { get; } = new();
        public List<OpenXmlElement> FootnoteClones { get; } = new();
        public List<OpenXmlElement> EndnoteClones { get; } = new();
        public bool SourceHadCommentsEx { get; set; }
        public IEnumerable<OpenXmlElement> AllClones =>
            CommentClones.Concat(FootnoteClones).Concat(EndnoteClones);
    }

    /// <summary>Clones the story items (comments/footnotes/endnotes) the body references, EARLY — so they
    /// participate in style/numbering closure + remap (F3). Dangling references (an id the source part
    /// does not define) are stripped from the body here, loudly — leaving them would cross-wire to a
    /// same-id TEMPLATE item (F9).</summary>
    private static StoryClones PrepareStoryClones(
        MainDocumentPart sourceMain,
        IReadOnlyList<OpenXmlElement> bodyChildren,
        ICollection<ComposeProjectionWarning>? warnings)
    {
        var story = new StoryClones();
        var strippedRefs = 0;

        // Comments (string ids).
        var commentIds = bodyChildren.SelectMany(c => c.Descendants())
            .Select(d => d switch
            {
                CommentReference r => r.Id?.Value,
                CommentRangeStart s => s.Id?.Value,
                CommentRangeEnd e => e.Id?.Value,
                _ => null,
            })
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        if (commentIds.Count > 0)
        {
            var sourceComments = sourceMain.WordprocessingCommentsPart?.Comments;
            var defined = sourceComments?.Elements<Comment>()
                .Select(c => c.Id?.Value).OfType<string>().ToHashSet(StringComparer.Ordinal)
                ?? new HashSet<string>(StringComparer.Ordinal);
            foreach (var comment in sourceComments?.Elements<Comment>() ?? Enumerable.Empty<Comment>())
            {
                if (comment.Id?.Value is string id && commentIds.Contains(id))
                {
                    story.CommentClones.Add(comment.CloneNode(true));
                }
            }
            var danglingComments = commentIds.Where(id => !defined.Contains(id)).ToHashSet(StringComparer.Ordinal);
            if (danglingComments.Count > 0)
            {
                foreach (var child in bodyChildren)
                {
                    foreach (var d in child.Descendants().ToList())
                    {
                        var id = d switch
                        {
                            CommentReference r => r.Id?.Value,
                            CommentRangeStart s => s.Id?.Value,
                            CommentRangeEnd e => e.Id?.Value,
                            _ => null,
                        };
                        if (id is not null && danglingComments.Contains(id))
                        {
                            (d is CommentReference && d.Parent is Run run && run.ChildElements.OfType<CommentReference>().Count() == run.ChildElements.Count(e => e is not RunProperties)
                                ? (OpenXmlElement)run : d).Remove();
                            strippedRefs++;
                        }
                    }
                }
            }
            story.SourceHadCommentsEx = sourceMain.WordprocessingCommentsExPart is not null && story.CommentClones.Count > 0;
        }

        // Footnotes / endnotes (long ids; ids >= 1 are content — 0/-1 are Word's separators).
        CollectStoryItems<FootnoteReference>(
            bodyChildren, sourceMain.FootnotesPart?.Footnotes, story.FootnoteClones, ref strippedRefs);
        CollectStoryItems<EndnoteReference>(
            bodyChildren, sourceMain.EndnotesPart?.Endnotes, story.EndnoteClones, ref strippedRefs);

        if (strippedRefs > 0)
        {
            Warn(warnings, "template-merge-story-reference-dropped", strippedRefs,
                "A comment/footnote/endnote reference had no matching content in the document and was removed.");
        }
        if (story.SourceHadCommentsEx)
        {
            Warn(warnings, "template-merge-comment-threading-dropped", 1,
                "Comment threading/resolution metadata (commentsExtended) is not carried by the template merge.");
        }

        return story;
    }

    private static void CollectStoryItems<TRef>(
        IReadOnlyList<OpenXmlElement> bodyChildren,
        OpenXmlElement? sourceRoot,
        List<OpenXmlElement> clones,
        ref int strippedRefs)
        where TRef : OpenXmlElement
    {
        var refs = bodyChildren.SelectMany(c => c.Descendants<TRef>()).ToList();
        if (refs.Count == 0) return;

        var neededIds = refs.Select(GetRefId).OfType<long>().ToHashSet();
        var definedIds = new HashSet<long>();
        if (sourceRoot is not null)
        {
            foreach (var item in sourceRoot.ChildElements)
            {
                if (GetStoryId(item) is long id && id >= 1 && neededIds.Contains(id))
                {
                    clones.Add(item.CloneNode(true));
                    definedIds.Add(id);
                }
            }
        }

        foreach (var r in refs)
        {
            if (GetRefId(r) is not long id || definedIds.Contains(id)) continue;
            // Dangling — remove the reference's run (the marker) so it cannot cross-wire to a template item.
            ((OpenXmlElement?)r.Ancestors<Run>().FirstOrDefault() ?? r).Remove();
            strippedRefs++;
        }
    }

    private static long? GetRefId(OpenXmlElement r) => r switch
    {
        FootnoteReference f => f.Id?.Value,
        EndnoteReference e => e.Id?.Value,
        _ => null,
    };

    /// <summary>Attaches the prepared story clones to their target parts with collision-proof id
    /// allocation (F4: every minted id is checked against the live taken-set), then remaps the body's
    /// references.</summary>
    private static void AttachStoryClones(
        MainDocumentPart sourceMain,
        MainDocumentPart mergedMain,
        StoryClones story,
        IReadOnlyList<OpenXmlElement> mergedBodyRoots,
        ICollection<ComposeProjectionWarning>? warnings)
    {
        // Comments.
        if (story.CommentClones.Count > 0)
        {
            var part = mergedMain.WordprocessingCommentsPart;
            if (part is null)
            {
                part = mergedMain.AddNewPart<WordprocessingCommentsPart>();
                part.Comments = new Comments();
            }
            var comments = part.Comments ??= new Comments();
            var taken = comments.Elements<Comment>()
                .Select(c => c.Id?.Value).OfType<string>()
                .Select(v => long.TryParse(v, out var n) ? (long?)n : null)
                .OfType<long>()
                .ToHashSet();
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            long nextId = taken.Count == 0 ? 1 : taken.Max() + 1;
            var existingIds = comments.Elements<Comment>()
                .Select(c => c.Id?.Value).OfType<string>().ToHashSet(StringComparer.Ordinal);

            foreach (var cloneElement in story.CommentClones)
            {
                var clone = (Comment)cloneElement;
                var id = clone.Id?.Value;
                if (id is not null && !existingIds.Contains(id))
                {
                    existingIds.Add(id);
                    if (long.TryParse(id, out var n)) taken.Add(n);
                }
                else
                {
                    while (taken.Contains(nextId)) nextId++; // F4: never mint a duplicate
                    var minted = nextId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (id is not null) map[id] = minted;
                    clone.Id = minted;
                    taken.Add(nextId);
                    existingIds.Add(minted);
                }
                comments.AppendChild(clone);
            }

            if (map.Count > 0)
            {
                foreach (var root in mergedBodyRoots)
                {
                    RemapCommentIds(root, map);
                }
            }
        }

        // Footnotes / endnotes.
        AttachNoteClones(story.FootnoteClones,
            () => mergedMain.FootnotesPart,
            () => { var p = mergedMain.AddNewPart<FootnotesPart>(); p.Footnotes = new Footnotes(); return p; },
            p => p.Footnotes ??= new Footnotes(),
            mergedBodyRoots, isFootnote: true);
        AttachNoteClones(story.EndnoteClones,
            () => mergedMain.EndnotesPart,
            () => { var p = mergedMain.AddNewPart<EndnotesPart>(); p.Endnotes = new Endnotes(); return p; },
            p => p.Endnotes ??= new Endnotes(),
            mergedBodyRoots, isFootnote: false);
    }

    private static void AttachNoteClones<TPart>(
        List<OpenXmlElement> clones,
        Func<TPart?> getPart,
        Func<TPart> addPart,
        Func<TPart, OpenXmlElement> getRoot,
        IReadOnlyList<OpenXmlElement> mergedBodyRoots,
        bool isFootnote)
        where TPart : OpenXmlPart
    {
        if (clones.Count == 0) return;

        var part = getPart() ?? addPart();
        var root = getRoot(part);

        var taken = root.ChildElements.Select(GetStoryId).OfType<long>().ToHashSet();
        var map = new Dictionary<long, long>();
        long nextId = taken.Where(t => t >= 1).DefaultIfEmpty(0).Max() + 1;
        if (nextId < 1) nextId = 1;

        foreach (var clone in clones)
        {
            if (GetStoryId(clone) is not long id) continue;
            if (!taken.Contains(id))
            {
                taken.Add(id);
            }
            else
            {
                while (taken.Contains(nextId)) nextId++; // F4: never mint a duplicate
                SetStoryId(clone, nextId);
                map[id] = nextId;
                taken.Add(nextId);
            }
            root.AppendChild(clone);
        }

        if (map.Count > 0)
        {
            foreach (var bodyRoot in mergedBodyRoots)
            {
                foreach (var d in bodyRoot.Descendants().ToList())
                {
                    switch (d)
                    {
                        case FootnoteReference f when isFootnote && f.Id?.Value is long v && map.TryGetValue(v, out var m): f.Id = m; break;
                        case EndnoteReference e when !isFootnote && e.Id?.Value is long v && map.TryGetValue(v, out var m): e.Id = m; break;
                    }
                }
            }
        }
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

    // ─────────────────────────────── relationships ────────────────────────────

    /// <summary>Re-creates every r:-namespace reference the grafted <paramref name="roots"/> carry on the
    /// TARGET part (F2: per hosting part — main, comments, footnotes, endnotes, numbering): hyperlinks and
    /// external rels by target URI, part references by cross-package deep copy. An unresolvable reference
    /// UNWRAPS its hyperlink (text survives — 030-review F10) or drops its hosting element. Returns the
    /// number of dropped/unwrapped hosts (caller warns once, aggregated).</summary>
    private static int ReconcileRelationshipReferences(
        OpenXmlPart sourcePart,
        OpenXmlPart targetPart,
        IReadOnlyList<OpenXmlElement> roots)
    {
        var idMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var unresolved = new HashSet<OpenXmlElement>();

        foreach (var root in roots)
        {
            foreach (var element in new[] { root }.Concat(root.Descendants()).ToList())
            {
                foreach (var attr in element.GetAttributes())
                {
                    if (attr.NamespaceUri != RelationshipNs || string.IsNullOrEmpty(attr.Value)) continue;
                    var oldId = attr.Value!;

                    if (!idMap.TryGetValue(oldId, out var newId))
                    {
                        newId = RecreateRelationship(sourcePart, targetPart, oldId)!;
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

        foreach (var element in unresolved)
        {
            if (element is Hyperlink hyperlink)
            {
                // F10: unwrap — the link is gone but its TEXT survives.
                if (hyperlink.Parent is not null)
                {
                    foreach (var child in hyperlink.ChildElements.ToList())
                    {
                        child.Remove();
                        hyperlink.Parent.InsertBefore(child, hyperlink);
                    }
                    hyperlink.Remove();
                }
                continue;
            }

            // Drop the smallest self-contained host: the run child (drawing/pict/object) when the
            // reference sits inside a run, else the element itself. Parent guard: a prior removal may
            // have already detached this element's subtree.
            var host = element.Ancestors().TakeWhile(a => a is not Body)
                .LastOrDefault(a => a.Parent is Run) ?? element;
            if (host.Parent is not null) host.Remove();
        }

        return unresolved.Count;
    }

    /// <summary>Resolves <paramref name="oldId"/> against the source part and re-creates the same
    /// relationship on the target part. Returns the new id, or null when unresolvable.</summary>
    private static string? RecreateRelationship(OpenXmlPart sourcePart, OpenXmlPart targetPart, string oldId)
    {
        var hyperlink = sourcePart.HyperlinkRelationships.FirstOrDefault(h => h.Id == oldId);
        if (hyperlink is not null)
        {
            return targetPart.AddHyperlinkRelationship(hyperlink.Uri, hyperlink.IsExternal).Id;
        }

        var external = sourcePart.ExternalRelationships.FirstOrDefault(e => e.Id == oldId);
        if (external is not null)
        {
            return targetPart.AddExternalRelationship(external.RelationshipType, external.Uri).Id;
        }

        try
        {
            var part = sourcePart.GetPartById(oldId);
            var added = targetPart.AddPart(part); // cross-package deep copy (incl. sub-parts)
            return targetPart.GetIdOfPart(added);
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

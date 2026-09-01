// Task 072 (Track D) — the style catalog, extracted from `ComposeDocumentRenderer`.
//
// WHY THIS IS ITS OWN COMPONENT. It authors `word/styles.xml`: Normal, Heading1-6 and ListParagraph,
// plus the heading-style → numbering link. Its reason to change is what a Spaarke-authored document
// should LOOK like (sizes, style ids, whether headings carry numbering) — a presentation decision,
// independent of how the body is assembled.
//
// ADR-049 I-5 — ONE BODY AUTHOR. Nothing here writes body children; it writes the STYLES part only.

using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Sprk.Bff.Api.Services.Compose;

internal static class ComposeStyleCatalog
{
    internal const string NormalStyleId = "Normal";
    internal const string ListParagraphStyleId = "ListParagraph";

    internal static void AddStyleDefinitions(MainDocumentPart mainPart, bool includeHeadingNumbering = true)
    {
        var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
        var styles = new Styles();

        // Normal — the default paragraph style every other style is based on.
        styles.AppendChild(new Style(
            new StyleName { Val = "Normal" },
            new PrimaryStyle())
        {
            Type = StyleValues.Paragraph,
            StyleId = NormalStyleId,
            Default = true,
        });

        // Heading1..6 — each carries a w:numPr referencing the ONE heading num instance at its own ilvl
        // (the STYLE side of the style-link) + an outlineLvl so the doc has a navigable outline. Descending
        // sizes; all bold; keepNext so a heading stays with its following paragraph. Carrier mode (task 011)
        // passes includeHeadingNumbering=false — the heading num instance is never authored there, so a
        // style-linked numPr would dangle or capture a carrier num definition (review finding 011-M1).
        var headingSizes = new[] { "32", "28", "26", "24", "22", "22" }; // half-points: 16pt..11pt
        for (var level = 1; level <= ComposeDocumentRenderer.MaxHeadingLevel; level++)
        {
            styles.AppendChild(BuildHeadingStyle(level, headingSizes[level - 1], includeHeadingNumbering));
        }

        // ListParagraph — indent only; NO numbering (list items supply a direct numPr).
        styles.AppendChild(new Style(
            new StyleName { Val = "List Paragraph" },
            new BasedOn { Val = NormalStyleId },
            new UIPriority { Val = 34 },
            new PrimaryStyle(),
            new StyleParagraphProperties(
                new Indentation { Left = "720" },
                new ContextualSpacing()))
        {
            Type = StyleValues.Paragraph,
            StyleId = ListParagraphStyleId,
        });

        stylesPart.Styles = styles;
        stylesPart.Styles.Save();
    }

    private static Style BuildHeadingStyle(int level, string sizeHalfPoints, bool includeNumbering = true)
    {
        var ilvl = level - 1;

        // CT_PPrBase child order: keepNext precedes numPr precedes spacing precedes outlineLvl.
        var pPr = new StyleParagraphProperties();
        pPr.AppendChild(new KeepNext());
        if (includeNumbering)
        {
            pPr.AppendChild(new NumberingProperties(
                new NumberingLevelReference { Val = ilvl },
                new NumberingId { Val = ComposeNumberingAuthor.HeadingNumInstanceId }));
        }
        pPr.AppendChild(new SpacingBetweenLines { Before = "240", After = "120" });
        pPr.AppendChild(new OutlineLevel { Val = ilvl });

        return new Style(
            new StyleName { Val = $"heading {level}" },
            new BasedOn { Val = NormalStyleId },
            new UIPriority { Val = 9 },
            new PrimaryStyle(),
            pPr,
            new StyleRunProperties(
                new Bold(),
                new FontSize { Val = sizeHalfPoints },
                new FontSizeComplexScript { Val = sizeHalfPoints }))
        {
            Type = StyleValues.Paragraph,
            StyleId = HeadingStyleId(level),
        };
    }

    internal static string HeadingStyleId(int level) => $"Heading{level}";
}

// #777 (spaarkeai-compose-r8) — the `paragraph-style-flattened` half of the fidelity wideners.
//
// WHAT THIS PINS. `ComposeBlockMerge.InheritProperties` used to exclude `w:pStyle` WHOLESALE, so any block
// the user edited came back as Normal and lost a custom paragraph style — a firm body style, a Quote, a
// localized heading like "Überschrift1" that `ComposeOoxmlPrimitives.HeadingLevel`'s "Heading" prefix cannot
// classify. The exclusion's REASON is sound and is still enforced below: a user who DEMOTES a heading to a
// paragraph must not have the heading style handed back to them. It was simply too broad — it also flattened
// every style the model has no opinion about whatsoever.
//
// The rule now: exclude only the styles the CONTENT MODEL owns (Normal / Heading1-6 / ListParagraph, per
// ComposeStyleCatalog). For those, a rendered block carrying no `w:pStyle` is the model SPEAKING. For any
// other style the model is silent because it cannot represent it, and silence is not consent to flatten.
//
// WHY A DEDICATED FILE. `ComposeResidualLossParityTests` measures construct families through the whole
// renderer and holds the published residual-loss list to what it measured — it covers neither `w:pStyle` nor
// `w:ind`, which is exactly why it passed UNCHANGED when this behaviour was fixed. A green suite that would
// not have noticed the regression is not coverage. These tests assert the merge rule at the seam where it is
// decided, in both directions (carried AND deliberately-not-carried), so re-broadening the exclusion fails
// here rather than silently.
//
// MAINTAIN-class (tests/integration/seam/** vertical-slice KEEP path, ADR-038).

using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeParagraphStyleInheritanceSeamTests
{
    /// <summary>A base paragraph as the retained document held it: a style plus real formatting.</summary>
    private static Paragraph BaseParagraph(string? styleId)
    {
        var pPr = new ParagraphProperties();
        if (styleId is not null)
        {
            pPr.AppendChild(new ParagraphStyleId { Val = styleId });
        }

        // Indentation rides along on every case: it is the OTHER half of #777 and is inherited
        // unconditionally, so each test below doubles as a guard that the pStyle rule did not
        // disturb the ordinary unmodeled-property path.
        pPr.AppendChild(new Indentation { Left = "1440", FirstLine = "720" });
        return new Paragraph(pPr, new Run(new Text("base text")));
    }

    /// <summary>A block the model re-authored: a PLAIN paragraph, i.e. no <c>w:pStyle</c> of its own.</summary>
    private static Paragraph RenderedPlainParagraph() =>
        new(new ParagraphProperties(), new Run(new Text("edited text")));

    private static string? StyleOf(Paragraph p) => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value;

    [Theory]
    [InlineData("BodyIndent")]      // a firm body style
    [InlineData("Quote")]           // a Word built-in the model does not represent
    [InlineData("Überschrift1")]    // a LOCALIZED heading — HeadingLevel's "Heading" prefix cannot classify it
    [InlineData("ClauseLevel2")]    // a numbered-clause style (numbering rides the style definition)
    public void InheritProperties_WhenBaseStyleIsUnmodeled_CarriesItOntoTheEditedBlock(string styleId)
    {
        var rendered = RenderedPlainParagraph();

        ComposeBlockMerge.InheritProperties(rendered, BaseParagraph(styleId));

        // The model cannot express this style, so its absence on the rendered block carries no user
        // intent. Flattening it to Normal was the `paragraph-style-flattened` degradation.
        StyleOf(rendered).Should().Be(styleId);
    }

    [Theory]
    [InlineData("Heading1")]
    [InlineData("Heading6")]
    [InlineData("ListParagraph")]
    [InlineData("Normal")]
    public void InheritProperties_WhenBaseStyleIsModelDetermined_LeavesTheEditedBlockUnstyled(string styleId)
    {
        var rendered = RenderedPlainParagraph();

        ComposeBlockMerge.InheritProperties(rendered, BaseParagraph(styleId));

        // The ORIGINAL reason for the exclusion, still enforced: the model owns these, so a rendered
        // block with no pStyle means the user demoted it. Re-inheriting would undo the edit.
        StyleOf(rendered).Should().BeNull();
    }

    [Fact]
    public void InheritProperties_WhenTheEditedBlockAlreadyCarriesAStyle_NeverOverwritesIt()
    {
        // A block the model rendered AS a heading states its own style. The base must not win — this is
        // the promotion direction (plain paragraph → Heading2), the mirror of the demotion case above.
        var rendered = new Paragraph(
            new ParagraphProperties(new ParagraphStyleId { Val = "Heading2" }),
            new Run(new Text("edited text")));

        ComposeBlockMerge.InheritProperties(rendered, BaseParagraph("BodyIndent"));

        StyleOf(rendered).Should().Be("Heading2");
    }

    [Fact]
    public void InheritProperties_WhenStyleIsCarried_StillDoesNotInheritNumberingOrSectionProperties()
    {
        // Guards the double-numbering worry directly: `w:numPr` stays unconditionally excluded, so a
        // carried style can never drag a direct numbering reference across with it. `w:sectPr` likewise —
        // the renderer detaches and re-attaches the trailing section itself.
        var basePr = new ParagraphProperties(
            new ParagraphStyleId { Val = "ClauseLevel2" },
            new NumberingProperties(new NumberingLevelReference { Val = 1 }, new NumberingId { Val = 7 }));
        basePr.AppendChild(new SectionProperties());
        var baseParagraph = new Paragraph(basePr, new Run(new Text("base text")));

        var rendered = RenderedPlainParagraph();

        ComposeBlockMerge.InheritProperties(rendered, baseParagraph);

        StyleOf(rendered).Should().Be("ClauseLevel2");
        rendered.ParagraphProperties!.Elements<NumberingProperties>().Should().BeEmpty();
        rendered.ParagraphProperties!.Elements<SectionProperties>().Should().BeEmpty();
    }

    [Fact]
    public void InheritProperties_OnEveryStyleOutcome_StillCarriesIndentation()
    {
        // #777's other reported code (`indentation-dropped`). `w:ind` was never in the exclusion set, so
        // it is carried in BOTH the model-determined and unmodeled cases. Pinned because the projector
        // still emits `indentation-dropped` on the premise that it is lost — see the note in
        // ComposeContentModelProjector; this is the evidence that premise is false on the save path.
        foreach (var styleId in new[] { "BodyIndent", "Heading1" })
        {
            var rendered = RenderedPlainParagraph();

            ComposeBlockMerge.InheritProperties(rendered, BaseParagraph(styleId));

            var indentation = rendered.ParagraphProperties!.Elements<Indentation>().SingleOrDefault();
            indentation.Should().NotBeNull($"indentation must survive an edit under base style '{styleId}'");
            indentation!.Left!.Value.Should().Be("1440");
            indentation.FirstLine!.Value.Should().Be("720");
        }
    }
}

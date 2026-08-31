// Task 049 (spaarkeai-compose-r8, FR-A10 residual) — Word FIELDS carried through an edited paragraph.
//
// Owner decision 2026-08-25: the residual-loss list is NOT signed off with fields on it. A field in the
// paragraph a user edits was being flattened to the text it happened to be displaying, so a cross-reference
// stopped being a cross-reference and became a frozen number that goes quietly wrong the moment the document
// renumbers. This file is the evidence that it no longer does.
//
// WHAT IS MEASURED, AND WHY THIS SHAPE:
//
//   * Over the REAL corpus document (`ref-cross-references.docx`), through the REAL renderer. A hand-built
//     XML fragment can be made to pass by a carry that only handles the fragment; the corpus document is the
//     one a lawyer actually opened, with `w:noProof` on its result run and a `\r \h` switch pair on its
//     instruction. ADR-038: seam tests measure the real seam.
//
//   * BOTH field FORMS, because they are different constructs in the file: `w:fldSimple` (one element, the
//     instruction an attribute) and the `w:fldChar` begin/instrText/separate/result/end RUN sequence. A carry
//     that normalised one into the other would silently rewrite what the document contains — the `Symbol`
//     rule from task 048, applied to a bigger construct.
//
//   * The bookmark TARGET, in the same save. A carried `REF` is only an improvement if its target survives;
//     if it does not, Word shows broken-reference text where resolved prose stood, and freezing would have
//     been the better outcome. The renderer's own remarks claimed "the model does not carry bookmarks" —
//     that comment predates task 041 and this test is the measurement that settles it rather than assuming.
//
//   * The classes deliberately NOT carried (nested, unterminated, instruction-less), each degrading to
//     today's flatten + named warning and never to a refusal (ADR-049 invariant 1).
//
// MAINTAIN-class (tests/integration/seam/** vertical-slice KEEP path, ADR-038).

using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeFieldCarrySeamTests
{
    private readonly ITestOutputHelper _output;

    public ComposeFieldCarrySeamTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private const string EditMarker = " [edited]";

    /// <summary>The corpus document that carries both field forms AND their shared bookmark target.</summary>
    private const string CrossReferenceFixture = "ref-cross-references.docx";

    // Block layout of `ref-cross-references.docx` (verified against the fixture's own document.xml):
    //   0 — "Section 4. Confidentiality."  wrapped in bookmarkStart/End `_Ref_Confidentiality`
    //   1 — "As provided in Section {REF _Ref_Confidentiality \r \h}, the receiving party ..."  (w:fldSimple)
    //   2 — "See also page {PAGEREF _Ref_Confidentiality \h} of this Agreement."                (w:fldChar)
    private const int BookmarkBlock = 0;
    private const int SimpleFieldBlock = 1;
    private const int ComplexFieldBlock = 2;

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // (1) The carry — over the real corpus document, through the real renderer.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void EditedParagraph_KeepsItsSimpleField_AsAFieldNotAsFrozenText()
    {
        var source = LoadCorpus(CrossReferenceFixture);

        var saved = RenderWithEditAt(source, SimpleFieldBlock, out var codes);

        var fields = SimpleFieldsIn(saved);
        fields.Should().HaveCount(1,
            "the user edited the paragraph the REF field sits in — the field must survive the save AS A " +
            "FIELD. Flattening it to its cached '4' leaves a number that is silently wrong the moment the " +
            "document renumbers, which in an executed agreement is worse than a visible error.");

        fields[0].Instruction.Should().Be(" REF _Ref_Confidentiality \\r \\h ",
            "the INSTRUCTION is the field's identity — carrying only the resolved display would re-author " +
            "a look-alike, exactly what the task-048 Symbol rule forbids");
        fields[0].CachedResult.Should().Be("4",
            "the cached result is what the reader currently sees; carrying it means the save changes " +
            "nothing on screen while restoring the field's ability to update");

        codes.Should().NotContain("field-flattened-to-text",
            "nothing was flattened, so nothing may be reported flattened — a warning for a loss that did " +
            "not happen trains users to ignore the ones that did");
    }

    [Fact]
    public void EditedParagraph_KeepsItsComplexField_AsTheFldCharSequenceItWas()
    {
        var source = LoadCorpus(CrossReferenceFixture);

        var saved = RenderWithEditAt(source, ComplexFieldBlock, out var codes);

        var complex = ComplexFieldsIn(saved);
        complex.Should().HaveCount(1,
            "the PAGEREF field is authored as a w:fldChar begin/instrText/separate/result/end run sequence " +
            "and must come back as one — normalising it into a w:fldSimple would rewrite the construct the " +
            "document contains, not preserve it");

        complex[0].Instruction.Should().Be(" PAGEREF _Ref_Confidentiality \\h ");
        complex[0].CachedResult.Should().Be("1");

        // The three control characters are what make it a field at all.
        CountIn(saved, "fldChar").Should().Be(3, "begin, separate and end must all be re-emitted");
        CountIn(saved, "instrText").Should().Be(1);

        codes.Should().NotContain("field-flattened-to-text");
    }

    [Fact]
    public void UntouchedParagraph_KeepsItsFields_Unchanged()
    {
        // The control arm. A construct in a block the user did not touch is CLONED, so this holds whether or
        // not the carry exists — asserted so a regression in the carry cannot be mistaken for one here.
        var source = LoadCorpus(CrossReferenceFixture);

        var saved = RenderWithEditAt(source, BookmarkBlock, out _);

        CountIn(saved, "fldSimple").Should().Be(CountIn(source, "fldSimple"));
        CountIn(saved, "fldChar").Should().Be(CountIn(source, "fldChar"));
        CountIn(saved, "instrText").Should().Be(CountIn(source, "instrText"));
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // (2) The carry is only an improvement if the TARGET survives. This is the measurement that
    //     replaces the renderer's stale "the model does not carry bookmarks" claim with a number.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void EditedBookmarkParagraph_StillCarriesTheTarget_SoACarriedRefResolves()
    {
        var source = LoadCorpus(CrossReferenceFixture);

        // The WORST case for a carried REF: the user edits the paragraph the target bookmark lives in, so
        // the target is re-authored rather than cloned. If it did not survive here, carrying REF live would
        // trade resolved prose for Word's broken-reference text and freezing would be the better outcome.
        var saved = RenderWithEditAt(source, BookmarkBlock, out _);

        var names = BookmarkNamesIn(saved);
        names.Should().Contain("_Ref_Confidentiality",
            "task 041's CarryBookmarks restores the base block's bookmarks onto the rendered paragraph. " +
            "This is the evidence that the renderer's 011-P4/P9 remark ('the model does not carry " +
            "bookmarks') is STALE — and the whole basis for carrying REF/PAGEREF live rather than freezing " +
            "them.");

        // …and both fields, in the two blocks that were NOT edited, still point at it.
        SimpleFieldsIn(saved).Should().ContainSingle()
            .Which.Instruction.Should().Contain("_Ref_Confidentiality");
        ComplexFieldsIn(saved).Should().ContainSingle()
            .Which.Instruction.Should().Contain("_Ref_Confidentiality");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // (3) NESTED fields — the conditional merge block a real template is built from (task 058).
    //
    // Task 049 flattened these, for a reason that was true and is still true: the outer scan folds the
    // inner field's `w:instrText` into the outer accumulation, so the only instruction RECOVERABLE from
    // that scan is a CONCATENATION, and re-emitting it would author a different field. What 049 did not
    // consider is that a field need not be RECONSTRUCTED to be carried. The span is a contiguous sibling
    // run sequence; captured and re-emitted VERBATIM it reproduces the tree exactly, because nothing ever
    // parses the tree. The instruction is not recovered — it is never taken apart.
    //
    // These tests hold that to the standard the reconstruction could not meet: the field's own OOXML,
    // byte-for-byte, over a real corpus document.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>The corpus document authored as a CONDITIONAL MERGE TEMPLATE (task 058's fixture).</summary>
    private const string NestedMergeFixture = "nested-merge-fields.docx";

    // Block layout of `nested-merge-fields.docx` (see its generator under
    // tests/fixtures/compose-corpus/generators/make-nested-merge-fields.py):
    //   0 — "Schedule 2 - Governing Law."
    //   1 — "This Schedule is entered into by {MERGEFIELD ClientName}."       plain field — carried since 049
    //   2 — "The Client, { IF {MERGEFIELD Entity} = … }, agrees as follows."  nested, mid-sentence
    //   3 — "{ IF {MERGEFIELD State} = … {MERGEFIELD State} … }"              nested, alone in its block
    //   4 — "Executed as of the date first written above."
    private const int PlainMergeFieldBlock = 1;
    private const int InlineConditionalBlock = 2;
    private const int StandaloneConditionalBlock = 3;

    [Fact]
    public void EditedParagraph_KeepsItsConditionalMergeField_ByteForByte()
    {
        // The whole point of the verbatim carry, stated as the strictest assertion available: the field's
        // own run sequence in the SAVED document is character-identical to the one in the source. Anything
        // weaker — "the instruction is still there", "the field count matches" — is satisfiable by a
        // reconstruction, and a reconstruction is exactly what task 049 refused to ship.
        var source = LoadCorpus(NestedMergeFixture);
        var before = FieldSpanXmlIn(source, StandaloneConditionalBlock);
        before.Should().NotBeNullOrEmpty("the fixture's block[3] must actually contain a field span");

        var saved = RenderWithAppendedRunAt(source, StandaloneConditionalBlock, out var codes);

        FieldSpanXmlIn(saved, StandaloneConditionalBlock).Should().Be(before,
            "a nested field is carried by NOT being taken apart — the span the projection captured is the " +
            "span the renderer re-emits, so the outer IF, both inner MERGEFIELDs, every instruction run " +
            "and every result run come back exactly as the document authored them");

        codes.Should().NotContain("field-flattened-to-text",
            "nothing was flattened, so nothing may be reported flattened");
    }

    [Fact]
    public void EditedParagraph_KeepsAnInlineConditionalMergeField_WithBothOfItsInnerFields()
    {
        // The mid-sentence shape. Block[3] is a field alone in its paragraph — the easy case, where the
        // block's whole content is the construct. A conditional inside a sentence the user is rewriting is
        // the shape a template actually takes, and it is the one where a carry has to survive being placed
        // among re-authored runs.
        var source = LoadCorpus(NestedMergeFixture);
        var before = FieldSpanXmlIn(source, InlineConditionalBlock);

        var saved = RenderWithAppendedRunAt(source, InlineConditionalBlock, out var codes);

        FieldSpanXmlIn(saved, InlineConditionalBlock).Should().Be(before);
        codes.Should().NotContain("field-flattened-to-text");

        // …and the user's own edit landed, in the right place. A carry that preserved the field by
        // discarding the edit would pass every assertion above.
        TextOf(saved).Should().Contain("The Client, ").And.Contain(", agrees as follows." + EditMarker);
    }

    [Fact]
    public void CarriedNestedField_IsExemptFromPropertyInheritance_ButTheUsersOwnNewRunIsNot()
    {
        // The defect this carry surfaced, and the boundary of its fix.
        //
        // `ComposeBlockMerge.InheritRunProperties` donates the base paragraph's DOMINANT run properties to
        // every rendered run, because a re-authored run lost its own at projection time. In this fixture the
        // dominant run is the outer IF's result — the longest — and it is BOLD. Applied to a verbatim carry
        // that rule stops being a repair and becomes a mutation: measured before the exemption, all 17
        // carried runs came back carrying `w:b`, so both inner MERGEFIELD values were silently bolded. A
        // fidelity loss introduced by the fix for a fidelity loss.
        //
        // The exemption is therefore scoped to runs that were CARRIED, and this test pins both halves of
        // that: the span keeps exactly its own properties, and the run the user actually typed still
        // inherits, because that one really was authored from a model that dropped its formatting.
        var source = LoadCorpus(NestedMergeFixture);

        var beforeSpan = FieldSpanXmlIn(source, StandaloneConditionalBlock);
        CountOf(beforeSpan, "<w:noProof />").Should().Be(3,
            "the fixture's conditional has three result runs — two inner MERGEFIELDs and the outer IF — " +
            "and Word marks every one of them noProof");
        CountOf(beforeSpan, "<w:b />").Should().Be(1,
            "only the outer IF's result is bold; the two inner merge values are not");

        var saved = RenderWithAppendedRunAt(source, StandaloneConditionalBlock, out _);
        var afterSpan = FieldSpanXmlIn(saved, StandaloneConditionalBlock);

        CountOf(afterSpan, "<w:noProof />").Should().Be(3,
            "a property nobody modelled survives because nothing re-authored the runs it sits on — the " +
            "same reason cloning an untouched block preserves everything in it");
        CountOf(afterSpan, "<w:b />").Should().Be(1,
            "and, more importantly, nothing was ADDED: the inner merge values are still not bold");

        // The other half of the boundary. Without this the exemption could be widened to 'skip every field
        // run' — or to the whole paragraph — and this test would keep passing while inheritance quietly
        // stopped working for the content it exists to serve.
        CountInBlock(saved, StandaloneConditionalBlock, "noProof").Should().Be(4,
            "the run the user just typed is NOT carried — it is authored from the model, so it inherits " +
            "the base paragraph's dominant properties exactly as every other rendered run does");
    }

    [Fact]
    public void EditingANestedField_LeavesThePlainMergeFieldInTheSameDocumentAlone()
    {
        // The flat-scan non-regression, measured on ONE document rather than inferred across two. The
        // fixture holds a plain `{ MERGEFIELD ClientName }` two blocks away from the conditional; the
        // classes task 049 shipped must be unaffected by the nested carry in either direction.
        var source = LoadCorpus(NestedMergeFixture);

        var editedNested = RenderWithAppendedRunAt(source, StandaloneConditionalBlock, out _);
        FieldSpanXmlIn(editedNested, PlainMergeFieldBlock).Should().Be(
            FieldSpanXmlIn(source, PlainMergeFieldBlock),
            "the plain merge field's block was not edited, so it is CLONED — the scope rule, on the same " +
            "document that exercises the new carry");

        // …and the plain field still carries as a SCALAR field when its own block is the edited one.
        var editedPlain = RenderWithAppendedRunAt(source, PlainMergeFieldBlock, out var codes);
        ComplexFieldsIn(editedPlain).Select(f => f.Instruction).Should()
            .Contain(" MERGEFIELD  ClientName  \\* MERGEFORMAT ",
                "the task-049 instruction carry is the shipped path for a non-nested field and must keep " +
                "working — the nested carry is an ADDITION to the scan, not a replacement for it");
        codes.Should().NotContain("field-flattened-to-text");
    }

    [Fact]
    public void NestedFieldInTheCompactForm_IsCarriedToo_AndComesBackAsFldSimple()
    {
        // The OTHER authoring form. `w:fldSimple` is one element rather than a run sequence, and it may
        // contain a field — Word writes that. The capture wraps it in the same holder `w:p` so a single
        // parse gate serves both forms, and the structural check has a branch for it.
        //
        // Written because that branch existed with nothing exercising it. An untested branch in a path whose
        // job is to decide what may be authored into a saved document is exactly the shape that turns into a
        // finding later — and the corpus has no nested `fldSimple`, so this one is synthetic by necessity.
        var source = BuildSynthetic(
            "<w:fldSimple w:instr=\" IF &quot;A&quot; = &quot;A&quot; &quot;yes&quot; &quot;no&quot; \">"
            + "<w:fldSimple w:instr=\" MERGEFIELD  Party  \">"
            + "<w:r><w:rPr><w:noProof/></w:rPr><w:t>Acme</w:t></w:r>"
            + "</w:fldSimple>"
            + "</w:fldSimple>");

        var before = CountIn(source, "fldSimple");
        before.Should().Be(2, "the fixture must really be a nested compact field");

        var saved = RenderWithAppendedRunAt(source, 1, out var codes);

        CountIn(saved, "fldSimple").Should().Be(before,
            "the compact form comes back as the compact form — normalising it into a fldChar run sequence " +
            "would rewrite what the file contains, which is the rule task 049 set for the outer form and " +
            "which applies just as much one level in");
        CountIn(saved, "noProof").Should().BeGreaterThan(0,
            "and the inner result run's own properties ride along, because nothing re-authored them");
        codes.Should().NotContain("field-flattened-to-text");
    }

    [Fact]
    public void KeystrokeEdit_WhoseModelCarriesNoField_StillKeepsTheConditional_FromTheBaseBlock()
    {
        // The half that makes the carry reachable from a browser.
        //
        // A field reaches the editor as an OPAQUE atom, and the client's mapper contributes nothing for one
        // — so a keystroke edit posts a paragraph with no field in it at all. For an ORDINARY field task 057
        // closed that by putting the instruction on the atom, because an instruction is a scalar the
        // document already states in plain text. That door is deliberately shut here: a nested field's
        // payload is its SUBTREE, and markup does not cross the wire (ADR-049 I-2).
        //
        // So the only mechanism left is base-carry — the server takes the span from the block's pre-edit
        // base, exactly as task 041 does for bookmarks and task 056 for embedded objects. Without it, the
        // model carry would be a producer with no consumer, which is precisely the shape task 049 shipped
        // and task 057 had to come back for.
        var source = LoadCorpus(NestedMergeFixture);
        var expected = FieldSpanXmlIn(source, StandaloneConditionalBlock);

        var saved = RenderWithStrippedFieldAt(source, StandaloneConditionalBlock, out var codes);

        FieldSpanXmlIn(saved, StandaloneConditionalBlock).Should().Be(expected,
            "the field is absent from the posted model, so the ONLY thing that can put it back is the base " +
            "block — and it comes back as the base's own bytes, which is why it is byte-identical here too");
        codes.Should().NotContain("field-flattened-to-text");
        TextOf(saved).Should().Contain(EditMarker.Trim(), "the user's own edit still landed");
    }

    [Fact]
    public void KeystrokeEdit_DoesNotDoubleAConditionalTheModelAlreadyCarried()
    {
        // The failure a naive "append the base's fields" would ship. A server-side model round trip DOES
        // carry the field, so the restore must recognise it is already there — otherwise a saved agreement
        // holds the same conditional clause twice, which is a worse outcome than the one being fixed and
        // exactly the shape task 056 hit with embedded objects.
        var source = LoadCorpus(NestedMergeFixture);
        var before = CountInBlock(source, StandaloneConditionalBlock, "fldChar");

        var saved = RenderWithAppendedRunAt(source, StandaloneConditionalBlock, out _);

        CountInBlock(saved, StandaloneConditionalBlock, "fldChar").Should().Be(before,
            "restore-if-missing: the model carried the span, so the base-carry must restore nothing");
    }

    [Fact]
    public void NestedFieldWithAnInterleavedNonRunElement_IsNeverCapturedAsAVerbatimSpan()
    {
        // The capture's safety argument, asserted where the gate actually acts: on the MODEL.
        //
        // The scan consumes RUNS, but the container can hold other children between them — here a
        // `w:bookmarkStart` inside the field span, which is legal and is what a cross-reference target
        // wrapping part of a conditional looks like. Those elements are emitted by their OWN arms of the
        // projection walk. Capturing just the runs would produce a `SpanXml` the source document never
        // contained: an element silently omitted, presented as "the field's own OOXML, verbatim".
        //
        // That is the one claim this whole design rests on — nothing is parsed, so nothing can be lost — and
        // a carry that quietly drops an element it walked past would falsify it while still looking correct
        // in the saved file. So the capture refuses, and the field takes the base-carry path instead, which
        // claims nothing about interior position and therefore cannot be wrong about it.
        var source = BuildSynthetic(
            "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>"
            + "<w:r><w:instrText xml:space=\"preserve\"> IF </w:instrText></w:r>"
            + "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>"
            + "<w:r><w:instrText xml:space=\"preserve\"> PAGE </w:instrText></w:r>"
            + "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>"
            + "<w:bookmarkStart w:id=\"9\" w:name=\"_Ref_Interleaved\"/>"
            + "<w:r><w:t>1</w:t></w:r>"
            + "<w:bookmarkEnd w:id=\"9\"/>"
            + "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>"
            + "<w:r><w:instrText xml:space=\"preserve\"> = 1 \"First page\" \"Later page\" </w:instrText></w:r>"
            + "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>"
            + "<w:r><w:t>First page</w:t></w:r>"
            + "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>");

        var model = new ComposeDocxProjectionBuilder()
            .BuildContentModel(source, CancellationToken.None).Model!;

        model.Blocks[1].Runs.Where(r => r.Field?.SpanXml is not null).Should().BeEmpty(
            "a span whose runs are not consecutive siblings is NOT captured. Capturing it would fabricate " +
            "a span the document never contained — with the bookmark silently missing from it — and " +
            "present that as a verbatim carry. The projection therefore emits no field marker at all here, " +
            "which is what it did before this task; the difference is that the base-carry now covers it.");

        // …and the user still gets the field, and the bookmark, because the base-carry does not depend on
        // the capture. It claims nothing about where inside the paragraph the bookmark sat, which is
        // precisely why it is allowed to restore what the capture would not fabricate.
        var saved = RenderWithAppendedRunAt(source, 1, out var codes);

        CountIn(saved, "fldChar").Should().Be(CountIn(source, "fldChar"),
            "the base block still has the field, and the base-carry restores it whole");
        BookmarkNamesIn(saved).Should().Contain("_Ref_Interleaved",
            "and the interleaved construct survives on its own, by task 041's bookmark carry");
        codes.Should().NotContain("field-flattened-to-text",
            "nothing was lost, so nothing may be reported lost");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // (3b) The classes still NOT carried. Each degrades to today's flatten + named warning.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void FieldWithNoRecoverableInstruction_DegradesToFlatten_NeverToARefusal()
    {
        // A w:fldChar sequence whose code phase carries no w:instrText at all. There is no identity to
        // carry, so the cached result is kept as prose and the loss is named.
        var source = BuildSynthetic(
            "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>"
            + "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>"
            + "<w:r><w:t>orphaned result</w:t></w:r>"
            + "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>");

        var act = () => RenderWithEditAt(source, 1, out _);
        act.Should().NotThrow("every save terminates in a defined outcome (ADR-049 invariant 1)");

        RenderWithEditAt(source, 1, out var codes);
        codes.Should().Contain("field-flattened-to-text");
    }

    [Fact]
    public void UnterminatedField_KeepsFlattening_AndIsNamedOnBothSurfaces()
    {
        // A w:fldChar begin with no end in the same paragraph — the shape a TOC or INDEX takes, whose result
        // spans paragraph marks. The scan never closes, so there is no complete field to carry.
        //
        // The two warning surfaces are DIFFERENT and both matter: the PROJECTION reports the anomaly it saw
        // (`field-unterminated`, at read time), while the SAVE reports the outcome the user got
        // (`field-flattened-to-text`, counted by ComposeBlockMerge comparing base against rendered). This
        // test pins both, because a carry that quietly stopped one of them would look clean in the other.
        var source = BuildSynthetic(
            "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>"
            + "<w:r><w:instrText xml:space=\"preserve\"> TOC \\o \"1-3\" \\h </w:instrText></w:r>"
            + "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>"
            + "<w:r><w:t>Table of contents entry</w:t></w:r>");

        var projection = new ComposeDocxProjectionBuilder().BuildContentModel(source, CancellationToken.None);
        projection.Warnings.Select(w => w.Code).Should().Contain("field-unterminated");

        var saved = RenderWithEditAt(source, 1, out var codes);

        codes.Should().Contain("field-flattened-to-text",
            "the user's outcome is a flattened field, and the save must say so");
        CountIn(saved, "fldChar").Should().Be(0, "there was no complete field to re-emit");
        TextOf(saved).Should().Contain("Table of contents entry",
            "flattening keeps the visible text — a defined outcome, never a refusal");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // (4) `w:fldLock` — the one thing that must NOT become live.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void LockedField_StaysLocked_SoAFrozenFieldIsNotSilentlyMadeLive()
    {
        // The author set w:fldLock precisely so this field never updates. Carrying the instruction while
        // dropping the lock would convert a deliberately-frozen field into a live one — the exact hazard
        // the per-class decision exists to avoid, expressed in the document's own mechanism rather than ours.
        var source = BuildSynthetic(
            "<w:fldSimple w:instr=\" DATE \\@ &quot;d MMMM yyyy&quot; \" w:fldLock=\"true\">"
            + "<w:r><w:t>1 March 2026</w:t></w:r></w:fldSimple>");

        var saved = RenderWithEditAt(source, 1, out _);

        var fields = SimpleFieldsIn(saved);
        fields.Should().ContainSingle();
        fields[0].Locked.Should().BeTrue(
            "w:fldLock is part of the field's identity; dropping it re-authors a frozen field as a live one");
        fields[0].Instruction.Should().Be(" DATE \\@ \"d MMMM yyyy\" ");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // (5) A POSTED model is client input reaching OOXML authoring — the recurring 021-F1/022-F1/024-F1
    //     finding class in this renderer. The carry must not become a way to author an unopenable file.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("", "control characters")]
    [InlineData("", "an empty instruction")]
    [InlineData("   ", "a whitespace-only instruction")]
    public void PostedFieldWithAnUnusableInstruction_FlattensToItsResult_AndTheFileStillOpens(
        string instruction, string why)
    {
        var source = LoadCorpus(CrossReferenceFixture);
        var model = new ComposeDocxProjectionBuilder()
            .BuildContentModel(source, CancellationToken.None).Model!;

        var blocks = model.Blocks.ToList();
        blocks[SimpleFieldBlock] = blocks[SimpleFieldBlock] with
        {
            Runs = new[]
            {
                new ComposeInlineRun { Text = "As provided in Section " },
                new ComposeInlineRun
                {
                    Field = new ComposeField { Instruction = instruction, CachedResult = "4" },
                },
            },
        };

        var degradations = new List<ComposeProjectionWarning>();
        var saved = new ComposeDocumentRenderer()
            .RenderIntoCarrier(source, model with { Blocks = blocks }, "field-carry", degradations);

        // The file OPENS — the property that matters. An XML-illegal character written into w:instr makes
        // the package unreadable, which is an UNDEFINED outcome and ADR-049 invariant 1 forbids it.
        var act = () => TextOf(saved);
        act.Should().NotThrow($"a posted field carrying {why} must never produce an unopenable package");

        TextOf(saved).Should().Contain("4",
            "the cached result is kept as prose — today's flatten, a defined outcome, never a refusal");
        degradations.Select(d => d.Code).Should().Contain("field-flattened-to-text",
            "the merge's base-vs-rendered count reports the field as lost, so the user is told");
    }

    [Fact]
    public void PostedFieldWithAnAbsurdlyLongInstruction_Flattens_RatherThanBeingTruncated()
    {
        // Truncating would author a DIFFERENT field, which is the look-alike defect the carry exists to
        // avoid. Refusing the carry keeps the outcome honest and bounded.
        var source = LoadCorpus(CrossReferenceFixture);
        var model = new ComposeDocxProjectionBuilder()
            .BuildContentModel(source, CancellationToken.None).Model!;

        var blocks = model.Blocks.ToList();
        blocks[SimpleFieldBlock] = blocks[SimpleFieldBlock] with
        {
            Runs = new[]
            {
                new ComposeInlineRun
                {
                    Field = new ComposeField
                    {
                        Instruction = " REF " + new string('X', 8192) + " ",
                        CachedResult = "4",
                    },
                },
            },
        };

        var degradations = new List<ComposeProjectionWarning>();
        var saved = new ComposeDocumentRenderer()
            .RenderIntoCarrier(source, model with { Blocks = blocks }, "field-carry", degradations);

        SimpleFieldsIn(saved).Should().BeEmpty("an over-long instruction is refused, not shortened");
        TextOf(saved).Should().Contain("4");
        degradations.Select(d => d.Code).Should().Contain("field-flattened-to-text");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // (6) `SpanXml` is the ONE carry in this file whose payload is not root-gated by the SDK (task 058).
    //
    //     Every other opaque carry parses through a typed class whose generated constructor validates the
    //     root element: a `ComposeEmbeddedObject.Xml` payload can only ever parse as a `w:drawing`. A field
    //     span's holder is a `w:p`, and a `w:p` admits any paragraph content there is — so without a
    //     STRUCTURAL check the property would be a general-purpose way to author arbitrary markup into a
    //     saved legal document. These are the tests that hold that check in place.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(
        "<w:r><w:t xml:space=\"preserve\">HOSTILE_PROSE</w:t></w:r>",
        "HOSTILE_PROSE",
        "ordinary prose")]
    [InlineData(
        "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>"
        + "<w:r><w:instrText xml:space=\"preserve\"> PAGE HOSTILE_FLAT </w:instrText></w:r>"
        + "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>",
        "HOSTILE_FLAT",
        "a field that does not nest — that class has its own carry and must not get a second one")]
    [InlineData(
        "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>"
        + "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>"
        + "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>"
        + "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>"
        + "<w:r><w:t xml:space=\"preserve\">HOSTILE_TRAILING</w:t></w:r>",
        "HOSTILE_TRAILING",
        "content trailing after the outer field closes")]
    [InlineData(
        "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>"
        + "<w:r><w:t xml:space=\"preserve\">HOSTILE_STRAY_END</w:t></w:r>",
        "HOSTILE_STRAY_END",
        "an end with no begin")]
    [InlineData("HOSTILE_NOT_XML", "HOSTILE_NOT_XML", "a payload that is not XML")]
    [InlineData(
        "<w:tbl><w:tr><w:tc><w:p><w:r><w:t>HOSTILE_TABLE</w:t></w:r></w:p></w:tc></w:tr></w:tbl>",
        "HOSTILE_TABLE",
        "a table smuggled into the holder")]
    public void PostedFieldSpanThatIsNotANestedField_AuthorsNothing_AndTheDocumentKeepsItsOwnField(
        string holderChildren, string mustNotAppear, string why)
    {
        // `SpanXml` is the ONE carry in the renderer whose payload is not root-gated by the SDK: its holder
        // is a `w:p`, and a `w:p` admits any paragraph content there is. So the structural gate is what
        // stands between a posted model and arbitrary markup in a saved legal document, and `mustNotAppear`
        // is the distinctive token of each payload so that assertion is real for every row rather than
        // vacuously true for four of six.
        var source = LoadCorpus(NestedMergeFixture);
        var expected = FieldSpanXmlIn(source, StandaloneConditionalBlock);

        var saved = RenderWithPostedSpan(source, HolderParagraph(holderChildren), out var codes);

        // The package OPENS. An undefined outcome is what ADR-049 invariant 1 forbids.
        var act = () => TextOf(saved);
        act.Should().NotThrow($"a posted span carrying {why} must never produce an unopenable package");

        DocumentXmlOf(saved).Should().NotContain(mustNotAppear,
            $"a span that is {why} is NOT the construct this carry exists for. Authoring it would make " +
            "SpanXml a general-purpose way to write markup into a saved document rather than a field carry.");

        // …and what the user gets is not a hole: the block's own conditional comes back from the BASE,
        // because a refused payload leaves the rendered paragraph without a field and the base-carry then
        // restores the one the document actually had. The refusal costs nothing.
        FieldSpanXmlIn(saved, StandaloneConditionalBlock).Should().Be(expected,
            "the refusal falls through to the base-carry, so the document keeps its own field rather than " +
            "either the hostile payload or a flatten");
        codes.Should().NotContain("field-flattened-to-text",
            "nothing was lost, so nothing may be reported lost");
    }

    [Fact]
    public void PostedFieldSpanOverTheOpaqueCarryCap_IsRefused_RatherThanTruncated()
    {
        // Truncating a span would author half a field, which is not the construct the document contained —
        // the same rule the instruction cap follows.
        var source = LoadCorpus(NestedMergeFixture);
        var expected = FieldSpanXmlIn(source, StandaloneConditionalBlock);

        var huge = "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>"
                   + "<w:r><w:instrText xml:space=\"preserve\"> IF HOSTILE_HUGE "
                   + new string('X', 40 * 1024) + " </w:instrText></w:r>"
                   + "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>"
                   + "<w:r><w:instrText xml:space=\"preserve\"> PAGE </w:instrText></w:r>"
                   + "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>"
                   + "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>";

        var saved = RenderWithPostedSpan(source, HolderParagraph(huge), out _);

        DocumentXmlOf(saved).Should().NotContain("HOSTILE_HUGE",
            "over the shared opaque-carry cap the span is REFUSED, never shortened");
        FieldSpanXmlIn(saved, StandaloneConditionalBlock).Should().Be(expected);
    }

    [Fact]
    public void PostedFieldSpan_WithNoBaseToFallBackOn_DegradesToFlatten_NeverToARefusal()
    {
        // The other end of the refusal. Every row above lands on a block that HAS a base counterpart, so the
        // base-carry rescues it. On a block with none — a paragraph the user just created, or the R6
        // fail-open path where the baseline could not be re-projected — there is nothing to restore from,
        // and the outcome has to be defined anyway (ADR-049 invariant 1). It is the flatten, and it is
        // reported.
        var source = BuildSynthetic("<w:r><w:t xml:space=\"preserve\">plain carrier text</w:t></w:r>");
        var model = new ComposeDocxProjectionBuilder()
            .BuildContentModel(source, CancellationToken.None).Model!;

        var blocks = model.Blocks.ToList();
        blocks.Insert(2, new ComposeBlock
        {
            Kind = ComposeBlockKind.Paragraph,
            Runs = new[]
            {
                new ComposeInlineRun
                {
                    Field = new ComposeField
                    {
                        Instruction = string.Empty,
                        CachedResult = "cached display text",
                        Complex = true,
                        SpanXml = HolderParagraph("<w:r><w:t xml:space=\"preserve\">HOSTILE_NEW_BLOCK</w:t></w:r>"),
                    },
                },
            },
        });

        var degradations = new List<ComposeProjectionWarning>();
        var saved = new ComposeDocumentRenderer()
            .RenderIntoCarrier(source, model with { Blocks = blocks }, "field-carry", degradations);

        var act = () => TextOf(saved);
        act.Should().NotThrow("every save terminates in a defined outcome (ADR-049 invariant 1)");
        DocumentXmlOf(saved).Should().NotContain("HOSTILE_NEW_BLOCK");
        TextOf(saved).Should().Contain("cached display text",
            "with no base to fall back on, the refusal degrades to today's flatten — never a refusal to save");
    }

    [Fact]
    public void NestedFieldAtom_CarriesNoPayloadToTheClient_SoNoOoxmlCrossesTheWire()
    {
        // ADR-049 I-2, held at the read end. A field INSTRUCTION is a scalar the document states in plain
        // text, which is why task 049 could put it on the atom. A field SUBTREE is markup, and markup does
        // not cross the wire — so `SpanXml` is server-set and the nested atom carries nothing at all. That
        // is not merely a decision recorded in a comment: the ABSENCE of the payload is what stops a client
        // from posting a span the server would then have to distrust.
        var projection = new ComposeDocxProjectionBuilder()
            .Build(LoadCorpus(NestedMergeFixture), CancellationToken.None);

        projection.Html.Should().Contain("data-atom-kind=\"field\"",
            "the nested field is still shown to the user as a non-editable chip");

        var atoms = projection.Html.Split("data-atom-kind=\"field\"").Length - 1;
        atoms.Should().Be(3, "the fixture has three fields — one plain and two conditionals");

        CountOf(projection.Html, "data-field-instr=").Should().Be(1,
            "only the PLAIN MERGEFIELD gets a payload. The two conditionals get none, so their OOXML never " +
            "reaches the browser and a client cannot hand back a construct the server would refuse.");
        projection.Html.Should().NotContain("fldChar",
            "no field markup of any kind is in the HTML the editor mounts");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The holder <c>w:p</c> shape <see cref="ComposeField.SpanXml"/> travels in — a paragraph whose
    /// children are the field, carrying the namespace declarations the fragment needs.
    /// </summary>
    private static string HolderParagraph(string children) =>
        "<w:p xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">"
        + children + "</w:p>";

    /// <summary>Renders with a posted <see cref="ComposeField.SpanXml"/> replacing the conditional block's
    /// content — the client-input shape the gates exist for.</summary>
    private byte[] RenderWithPostedSpan(byte[] source, string spanXml, out List<string> codes)
    {
        var model = new ComposeDocxProjectionBuilder()
            .BuildContentModel(source, CancellationToken.None).Model!;

        var blocks = model.Blocks.ToList();
        blocks[StandaloneConditionalBlock] = blocks[StandaloneConditionalBlock] with
        {
            Runs = new[]
            {
                new ComposeInlineRun
                {
                    Field = new ComposeField
                    {
                        Instruction = string.Empty,
                        CachedResult = "cached display text",
                        Complex = true,
                        SpanXml = spanXml,
                    },
                },
            },
        };

        var degradations = new List<ComposeProjectionWarning>();
        var rendered = new ComposeDocumentRenderer()
            .RenderIntoCarrier(source, model with { Blocks = blocks }, "field-carry", degradations);

        codes = degradations.Select(d => d.Code).ToList();
        _output.WriteLine("posted-span · codes: " +
                          (codes.Count == 0 ? "(none)" : string.Join(", ", codes)));
        return rendered;
    }

    /// <summary>The saved package's <c>document.xml</c> as text — the surface a hostile payload would have
    /// to reach to matter, whether or not it lands anywhere the object model exposes.</summary>
    private static string DocumentXmlOf(byte[] docx)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);
        using var stream = doc.MainDocumentPart!.GetStream();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private readonly record struct FieldFacts(string Instruction, string CachedResult, bool Locked);

    private static byte[] LoadCorpus(string fileName)
    {
        var path = ComposeCorpusFixtureLocator.EnumerateDocumentPaths()
            .Single(p => string.Equals(Path.GetFileName(p), fileName, StringComparison.OrdinalIgnoreCase));
        return ComposeCorpusFixtureLocator.LoadVerifiedBytes(path);
    }

    private byte[] RenderWithEditAt(byte[] source, int blockIndex, out List<string> codes)
    {
        var projection = new ComposeDocxProjectionBuilder().BuildContentModel(source, CancellationToken.None);
        projection.Status.Should().NotBe(ComposeProjectionStatus.Failed);

        var model = projection.Model!;
        var blocks = model.Blocks.ToList();
        blocks.Count.Should().BeGreaterThan(blockIndex);

        var runs = blocks[blockIndex].Runs.ToList();
        if (runs.Count == 0)
        {
            runs.Add(new ComposeInlineRun { Text = EditMarker.TrimStart() });
        }
        else
        {
            runs[0] = runs[0] with { Text = (runs[0].Text ?? string.Empty) + EditMarker };
        }
        blocks[blockIndex] = blocks[blockIndex] with { Runs = runs };

        var degradations = new List<ComposeProjectionWarning>();
        var rendered = new ComposeDocumentRenderer()
            .RenderIntoCarrier(source, model with { Blocks = blocks }, "field-carry", degradations);

        codes = degradations.Select(d => d.Code).ToList();
        _output.WriteLine($"edit@{blockIndex} · codes: " +
                          (codes.Count == 0 ? "(none)" : string.Join(", ", codes)));
        return rendered;
    }

    /// <summary>
    /// Task 058: renders with a NEW run appended to <paramref name="blockIndex"/>'s runs, rather than by
    /// mutating <c>runs[0]</c>'s text.
    /// </summary>
    /// <remarks>
    /// A block whose first model run IS the field (block[3] of the nested fixture) has no text on run[0] to
    /// mutate — the field marker carries its cached result, not editable prose — so the shared helper's edit
    /// would be invisible in the output and the assertions would be measuring a paragraph nobody can see was
    /// edited. Appending a run is also closer to what the user did: they typed at the end of a paragraph
    /// that happens to contain a conditional.
    /// </remarks>
    private byte[] RenderWithAppendedRunAt(byte[] source, int blockIndex, out List<string> codes)
    {
        var projection = new ComposeDocxProjectionBuilder().BuildContentModel(source, CancellationToken.None);
        projection.Status.Should().NotBe(ComposeProjectionStatus.Failed);

        var model = projection.Model!;
        var blocks = model.Blocks.ToList();
        blocks.Count.Should().BeGreaterThan(blockIndex);

        var runs = blocks[blockIndex].Runs.ToList();
        runs.Add(new ComposeInlineRun { Text = EditMarker });
        blocks[blockIndex] = blocks[blockIndex] with { Runs = runs };

        var degradations = new List<ComposeProjectionWarning>();
        var rendered = new ComposeDocumentRenderer()
            .RenderIntoCarrier(source, model with { Blocks = blocks }, "field-carry", degradations);

        codes = degradations.Select(d => d.Code).ToList();
        _output.WriteLine($"append@{blockIndex} · codes: " +
                          (codes.Count == 0 ? "(none)" : string.Join(", ", codes)));
        return rendered;
    }

    /// <summary>
    /// Task 058: renders the posted model a KEYSTROKE edit produces — every field marker stripped from the
    /// block, because the editor's mapper contributes nothing for an opaque atom — plus the run the user
    /// typed. This is what <c>docxBridge.ts</c> posts today for a nested field, which carries no payload.
    /// </summary>
    private byte[] RenderWithStrippedFieldAt(byte[] source, int blockIndex, out List<string> codes)
    {
        var projection = new ComposeDocxProjectionBuilder().BuildContentModel(source, CancellationToken.None);
        var model = projection.Model!;
        var blocks = model.Blocks.ToList();

        var runs = blocks[blockIndex].Runs.Where(r => r.Field is null).ToList();
        runs.Count.Should().BeLessThan(blocks[blockIndex].Runs.Count,
            "the block must actually have had a field marker to strip — otherwise this measures nothing");
        runs.Add(new ComposeInlineRun { Text = EditMarker });
        blocks[blockIndex] = blocks[blockIndex] with { Runs = runs };

        var degradations = new List<ComposeProjectionWarning>();
        var rendered = new ComposeDocumentRenderer()
            .RenderIntoCarrier(source, model with { Blocks = blocks }, "field-carry", degradations);

        codes = degradations.Select(d => d.Code).ToList();
        _output.WriteLine($"keystroke@{blockIndex} · codes: " +
                          (codes.Count == 0 ? "(none)" : string.Join(", ", codes)));
        return rendered;
    }

    /// <summary>
    /// The OUTERMOST complete <c>w:fldChar</c> field span in body block <paramref name="blockIndex"/>,
    /// serialized through a holder <c>w:p</c> so two documents' spans are directly comparable as strings.
    /// Empty when the block holds no complete span.
    /// </summary>
    /// <remarks>
    /// Comparing the spans rather than the whole paragraph is deliberate: the paragraph legitimately
    /// differs (the user's edit is in it). The span is the construct under test, and holding it to string
    /// equality is the assertion a RECONSTRUCTION cannot pass — which is the whole reason task 049 declined
    /// to reconstruct one.
    /// </remarks>
    private static string FieldSpanXmlIn(byte[] docx, int blockIndex)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);
        var block = doc.MainDocumentPart?.Document?.Body?.ChildElements
            .OfType<Paragraph>()
            .ElementAtOrDefault(blockIndex);
        if (block is null) return string.Empty;

        var holder = new Paragraph();
        var depth = 0;
        foreach (var run in block.Elements<Run>())
        {
            var type = run.GetFirstChild<FieldChar>()?.FieldCharType?.Value;
            if (type == FieldCharValues.Begin) depth++;
            if (depth > 0) holder.AppendChild(run.CloneNode(true));
            if (type == FieldCharValues.End && depth > 0 && --depth == 0) break;
        }

        return holder.ChildElements.Count == 0 ? string.Empty : holder.OuterXml;
    }

    /// <summary>Non-overlapping occurrences of <paramref name="needle"/> in <paramref name="haystack"/>.</summary>
    private static int CountOf(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal);
             i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }

    /// <summary>Occurrences of an OOXML local name inside body block <paramref name="blockIndex"/>.</summary>
    private static int CountInBlock(byte[] docx, int blockIndex, string localName)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);
        var block = doc.MainDocumentPart?.Document?.Body?.ChildElements
            .OfType<Paragraph>()
            .ElementAtOrDefault(blockIndex);
        return block is null ? 0 : block.Descendants().Count(e => e.LocalName == localName);
    }

    /// <summary>Every <c>w:fldSimple</c> in the saved body, as instruction + cached display text.</summary>
    private static List<FieldFacts> SimpleFieldsIn(byte[] docx)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null) return new List<FieldFacts>();

        return body.Descendants<SimpleField>()
            .Select(sf => new FieldFacts(
                sf.Instruction?.Value ?? string.Empty,
                string.Concat(sf.Descendants<Text>().Select(t => t.Text)),
                sf.FieldLock?.Value == true))
            .ToList();
    }

    /// <summary>
    /// Every complete <c>w:fldChar</c> begin/…/end sequence in the saved body, reassembled by walking each
    /// paragraph's direct children — the same shape the projection scans, so a sequence that came back
    /// half-formed reads as absent here rather than silently passing.
    /// </summary>
    private static List<FieldFacts> ComplexFieldsIn(byte[] docx)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);
        var body = doc.MainDocumentPart?.Document?.Body;
        var found = new List<FieldFacts>();
        if (body is null) return found;

        foreach (var paragraph in body.Descendants<Paragraph>())
        {
            var depth = 0;
            var instruction = new System.Text.StringBuilder();
            var result = new System.Text.StringBuilder();
            var locked = false;
            var inResult = false;

            foreach (var run in paragraph.Elements<Run>())
            {
                var fldChar = run.GetFirstChild<FieldChar>();
                if (fldChar is not null)
                {
                    var type = fldChar.FieldCharType?.Value;
                    if (type == FieldCharValues.Begin)
                    {
                        depth++;
                        locked |= fldChar.FieldLock?.Value == true;
                    }
                    else if (type == FieldCharValues.Separate)
                    {
                        inResult = true;
                    }
                    else if (type == FieldCharValues.End && depth > 0)
                    {
                        depth--;
                        if (depth == 0)
                        {
                            found.Add(new FieldFacts(instruction.ToString(), result.ToString(), locked));
                            instruction.Clear();
                            result.Clear();
                            locked = false;
                            inResult = false;
                        }
                    }
                    continue;
                }

                if (depth == 0) continue;
                if (inResult)
                {
                    foreach (var t in run.Elements<Text>()) result.Append(t.Text);
                }
                else
                {
                    foreach (var i in run.Elements<FieldCode>()) instruction.Append(i.Text);
                }
            }
        }

        return found;
    }

    private static List<string> BookmarkNamesIn(byte[] docx)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);
        return doc.MainDocumentPart?.Document?.Body?
            .Descendants<BookmarkStart>()
            .Select(b => b.Name?.Value ?? string.Empty)
            .ToList() ?? new List<string>();
    }

    private static int CountIn(byte[] docx, string localName)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);
        return doc.MainDocumentPart?.Document?.Body is { } body
            ? body.Descendants().Count(e => e.LocalName == localName)
            : 0;
    }

    private static string TextOf(byte[] docx)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);
        return doc.MainDocumentPart?.Document?.Body is { } body
            ? string.Concat(body.Descendants<Text>().Select(t => t.Text))
            : string.Empty;
    }

    /// <summary>
    /// A three-block package with <paramref name="inlineMarkup"/> in block[1]. Authored as a raw OPC package
    /// for the same reason <c>ComposeResidualLossParityTests</c> is: <c>Body.InnerXml</c> parses without the
    /// element's namespace declarations in scope, so prefixed markup cannot be injected through the SDK
    /// object model.
    /// </summary>
    private static byte[] BuildSynthetic(string inlineMarkup)
    {
        const string decl = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>";

        static string Para(string paraId, string children) =>
            $"<w:p w14:paraId=\"{paraId}\" w14:textId=\"{paraId}\">{children}</w:p>";

        var body =
            Para("4B000001", "<w:r><w:t xml:space=\"preserve\">Opening paragraph.</w:t></w:r>")
            + Para("4B000002", "<w:r><w:t xml:space=\"preserve\">Carrier. </w:t></w:r>" + inlineMarkup)
            + Para("4B000003", "<w:r><w:t xml:space=\"preserve\">Closing paragraph.</w:t></w:r>")
            + "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/>"
            + "<w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\"/></w:sectPr>";

        var document = decl
            + "<w:document"
            + " xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\""
            + " xmlns:w14=\"http://schemas.microsoft.com/office/word/2010/wordml\""
            + " xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\""
            + " xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\""
            + " mc:Ignorable=\"w14\">"
            + "<w:body>" + body + "</w:body></w:document>";

        const string contentTypes = decl
            + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
            + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
            + "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
            + "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-"
            + "officedocument.wordprocessingml.document.main+xml\"/></Types>";

        const string rootRels = decl
            + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
            + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/"
            + "relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>";

        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(
                   ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "[Content_Types].xml", contentTypes);
            WriteEntry(zip, "_rels/.rels", rootRels);
            WriteEntry(zip, "word/document.xml", document);
        }
        return ms.ToArray();
    }

    private static void WriteEntry(System.IO.Compression.ZipArchive zip, string name, string content)
    {
        using var stream = zip.CreateEntry(name).Open();
        using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
        writer.Write(content);
    }
}

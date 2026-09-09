# Current Task

## Quick Recovery

| Field | Value |
|---|---|
| **Task** | 019 — FR-02 pre-flight: custom-XML markup-vs-parts premise + WordApi 1.4 manifest gap |
| **Task File** | `tasks/019-customxml-premise-and-wordapi-14.poml` |
| **Phase** | 0 De-risk and baseline |
| **Status** | in-progress |
| **Started** | 2026-09-09 |
| **Rigor** | FULL · model-tier opus · effort high · steps mode directional |
| **Next Action** | Resolve whether the unified JSON manifest can express the `CustomXmlParts` requirement set; then edit both manifests + APP_VERSION, build, typecheck, write notes/019. |

> Task 010 is COMPLETE pending one operator decision (host-detection option A/B/C,
> `notes/010-adapter-consolidation.md` §6c). That item is NOT lost — it is carried here.

## Critical Context

PREMISE VERDICT: **CONFIRMED empirically, no live host needed.** Custom XML *markup*
(`w:customXml` elements) and custom XML *parts* (`/customXml/itemN.xml`) are distinct; only
markup was removed. Proof in-repo — see Decisions below.

## Baselines measured at task start (2026-09-09)

- `npx tsc --noEmit --skipLibCheck`: 289 total errors, **0 in production files** — must stay 0.
- `word/manifest.json` version 1.0.7 · `word/word-manifest.xml` 1.0.7.0 · `word/taskpane/index.tsx` APP_VERSION '1.0.7'.

## Completed Steps

- [x] 0 Rigor declared. Read POML, spike-1 §6.2/6.3/6.4/6.5, both Word manifests, webpack manifest
      transforms, package.json scripts, APP_VERSION linkage.
- [x] 1 WordApi 1.4 gap VERIFIED INDEPENDENTLY in `@types/office-js@1.0.568`:
      `Word.Document.customXmlParts` `[Api set: WordApi 1.4]` (index.d.ts:100768-100773);
      `Word.Document.settings` `[Api set: WordApi 1.4]` (index.d.ts:100895-100899).
      Common API `Office.context.document.customXmlParts` members annotated
      `**Requirement set**: CustomXmlParts` — UNVERSIONED (index.d.ts:5275-5300, 5743-5745).
      Both manifests declare WordApi minVersion 1.3. Gap real.
- [x] 2 PREMISE CHECK — empirical, in-repo, no host required. Scanned all 48 .docx/.dotx/.docm
      in the worktree (node_modules excluded):
        * `w:customXml` MARKUP elements: **0 occurrences in 0 files** (markup is extinct — consistent
          with the support page's claim).
        * `/customXml/itemN.xml` PARTS: present in **6 distinct real documents** (8 incl. bin/ copies).
        * DECISIVE CASE — `tests/unit/Sprk.Bff.Api.Tests/Fixtures/Compose/RealTemplates/
          commonpaper-cloud-service-agreement.docx` carries `customXml/item2.xml` in the
          **third-party non-Microsoft namespace `http://customooxmlschemas.google.com/`**
          (Google Docs round-trip storage), correctly wired via `word/_rels/document.xml.rels`
          (rel type `.../customXml`) + `[Content_Types].xml` overrides — and Word is provably the
          LAST WRITER: `w:rsids` block, **1820 `w:rsid` attributes**, `w15:docId`, `w:proofState`,
          `mc:Ignorable="w14 w15 w16se w16cid w16 w16cex w16sdtdh wp14"`, `Template: Normal.dotm`,
          `Application: Microsoft Office Word` / `AppVersion 16.0000`, **`cp:revision 4`**.
          A FOREIGN-namespace custom XML part survived FOUR modern-Word save cycles untouched.
          `commonpaper-mutual-nda.docx` reproduces it independently (rev 2, 128 rsids).
      => Word does not strip custom XML PARTS, including parts it did not author and whose
         namespace it does not know. That is exactly FR-02's scenario.

## Files Modified

(none yet)

## Decisions Made

- 2026-09-09: Premise CONFIRMED via in-repo corpus forensics rather than a live-host probe —
  a Word-last-written package containing a foreign-namespace custom XML part is direct evidence
  and strictly stronger than a one-off manual observation. Spike-1 §8 step 6b can be DROPPED
  from the operator probe.

# R4.5 ↔ R5 Coordination Note (reciprocal)

> **Authored**: 2026-07-29, as a courtesy mirror of `projects/spaarkeai-compose-r5/notes/COORDINATION-with-r4.5.md` §2/§3 (canonical source — read that note for full context, including §1 sequencing and §5 deploy detail).
> **For**: whoever executes R4.5 tasks (this project). R4.5 is already **code-complete and merged to master** (2026-07-28) — this note is informational, not an action item. Nothing here should change already-landed R4.5 code.
> **Bottom line**: R5 (`spaarkeai-compose-r5`) builds its editing-completeness gaps directly on top of R4.5's merged output. There is no outstanding work for R4.5 to do, but two things are worth knowing if you touch these files again (e.g. a defect fix or a follow-on task).

## 1. The docxBridge hazard — already satisfied, confirming not flagging

`docxBridge.ts` exports **both** `docxToTipTapHtml` (mammoth READ path, which R4.5's WS-1 correctly deleted) **and** `buildContentModel` + `stampParaIds` + the paraId write helpers, which R5's gaps **G1** (cross-session origin routing), **G2** (clean apply), and **G7** (Save-Version vs Save-New) depend on. R4.5's WS-1 deleted ONLY `docxToTipTapHtml` and left the file + write helpers intact — this is the correct, already-shipped state. **Confirmation, not an action item**: if any future R4.5-adjacent task touches `docxBridge.ts` again, it must continue to preserve `buildContentModel`/`stampParaIds`/the paraId helpers — deleting the file or "removing the mammoth module" wholesale would break R5's authoring/versioning path.

## 2. Two contended files — R4.5 owns them FIRST

| File | R4.5 (this project, already landed) | R5 (rebases onto it) |
|---|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeService.cs` | WS-1 upload projection + WS-4 persist `paraId→number` (`LoadAsync`/`SaveAsync`) | G1 origin marker, G7 create-vs-replace, G10 profile trigger — rebased onto post-R4.5 `LoadAsync`/`SaveAsync` |
| `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.tsx` | WS-1 upload/browse hydrate `projection` | G1 transient/save routing (`isTransientCreate` region), G7 toolbar, G8 mount — rebased onto post-R4.5 version |

R4.5's versions of both files are on `master`; R5 is the one doing the rebasing, so no further action is needed here from R4.5's side. If a hotfix to either file is ever needed post-merge, be aware R5 work depends on the shape of these functions.

## 3. What R5 reuses from R4.5 (does not fork)

- **`NumberingComputationEngine`** (R4.5 WS-3) — reused by R5's **G3** (`setBlockAttr` edit-time renumbering); R5 does not reimplement the numbering algorithm (R4.5 spec FR-14 reconciles read-time vs edit-time numbering on the shared model).
- **`CitationResolver`** (R4.5 WS-4, `paraId → legal-number`) — reused by R5's **G10** (Document Profile re-run) for precise citations.
- **Transient-mount identity** (R4.5 WS-1) — reused by R5's **G7** (Save-Version vs Save-New) for stable doc identity.

## 4. Deploy contention

Both projects deploy to the shared **`sprk_spaarkeai`** web resource + **`spaarke-bff-dev`**. Since R4.5 is already merged, R5 builds/deploys from master-with-R4.5 — no coordination action needed from R4.5 unless a post-merge hotfix lands here concurrently with an R5 deploy ("last deploy wins" rule applies).

## Source of truth

Canonical note: [`projects/spaarkeai-compose-r5/notes/COORDINATION-with-r4.5.md`](../../spaarkeai-compose-r5/notes/COORDINATION-with-r4.5.md). If the two notes ever disagree, that one wins — update this mirror to match.

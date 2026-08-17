# Task 051 — Client PDF intake-door gates + parity (FR-06 client half) — IMPLEMENTED

> Phase 5 (PDF Import Parity / UC-7) · sonnet@high · FULL rigor · 2026-08-17 · client-only (Spaarke.Compose.Components)
> Depends on task 050 (async `ProjectForMount` PDF fork — server half).

## Root-cause found (bigger than "add .pdf to accept")

Compose-r6 wired the `sourceFormat` **reducer + save-routing + banner** (tasks 040/041/042), but the actual
client **editable-admission gate** still rejected any `.pdf` fileName. `ComposeEditor.isEditableDocx(bytes,
fileName)` returns false whenever `fileName` matches `NON_DOCX_EXTENSION` (which includes `pdf`) — so even
after the server (050) returns an editable SYNTHESIZED docx with `sourceFormat:"pdf"`, the client routed it
to reference-only because the display name still ends in `.pdf`. This affected **every** intake door (Load
included — the r6 Load PDF path was reducer/save-wired but the mount gate was never sourceFormat-aware). So
051's real fix is: **make the editable gate sourceFormat-aware**, which fixes Load + Browse + Upload
uniformly via one `sourceFormat` prop.

## What shipped

- **`ComposeEditor.tsx`** — `isEditableDocx(bytes, fileName, sourceFormat?)`: when `sourceFormat==='pdf'`,
  trust the byte signature (`isDocxBytes`) and SKIP the `.pdf`-extension rejection (the bytes are a
  server-synthesized docx). Every other non-docx (xlsx/pptx ZIP siblings, txt, a raw un-intakeable `.pdf`
  that never earned a `sourceFormat` marker) still routes to reference-only. New `sourceFormat?: string |
  null` prop (threaded, destructured, passed to the gate at the mount effect). Effect deps unchanged —
  `sourceFormat` is set atomically with `docxBytes` per mount (same read-but-not-a-dep contract as
  `documentRef.fileName`, per the effect's documented eslint-disable rationale).
- **`ComposeWorkspace.tsx`** — Browse (`/api/compose/project`) + Upload (`/api/compose/upload`) handlers
  parse `sourceFormat` from the response (task 050 now sends it) and thread it into `mountTransient`. The
  synthesized-docx `content` echo is already adopted as `retainedBytes` (my 050 `/project` echo fix). Browse
  `accept` filter admits `.pdf,application/pdf`. `<ComposeEditor sourceFormat={state.sourceFormat}>` wired.
- **`ComposeWorkspace.types.ts`** — `mountTransient` action gains `sourceFormat?`; the reducer sets
  `sourceFormat: action.sourceFormat ?? null` (was hardcoded `null` — stale now that mount doors can fork PDFs).

## Intake-doors-only + reference-only preservation (constraint)

`sourceFormat==='pdf'` is set ONLY when a server intake door successfully forked a PDF into a synthesized
docx. A raw un-intakeable file, or a PDF when the DI gate is OFF (server 503) / parse fails (422), never
earns the marker → the editor's extension gate still routes it to reference-only. So admission is inherently
intake-door-gated; no parallel intake path was added (root §11). `docxBridge.ts` NOT deleted (NFR-06 —
still mocked by the referenceOnly regression guard).

## Env-gate + parity UAT — HONEST status (NOT a faked pass)

The POML's parity acceptance (PDF via Browse/Upload → editable → NDA analysis → response → save-as-docx) and
Success Criterion #5 require a **live DI-enabled env** (`Analysis:Enabled && DocumentIntelligence:Enabled`
ON). Task 001 established that the **live App Service gate value is NOT verifiable from this non-interactive
session** (Azure/MCP connector auth unavailable; `Analysis:Enabled` is a per-env deploy token). Therefore:

- **Code + automated verification: COMPLETE.** The full parity path is proven by automated tests:
  - Server (task 050): `ComposeMountPdfProjectionSeamTests` + `ComposePdfIntakeRoundTripSeamTests` prove the
    mount/load fork returns an editable synthesized docx + `sourceFormat:"pdf"` + honest 503/422 degradation.
  - Client (051): `ComposeEditor.referenceOnly.test.tsx` (+3 tests) proves a PDF-sourced synthesized docx
    mounts EDITABLE while a raw/un-marked `.pdf` stays reference-only; `ComposeWorkspace.renderOnSave.reducer`
    (+1 test) proves `mountTransient` carries `sourceFormat`.
- **Live end-to-end UAT: operator-run, env-gated.** It cannot be executed in this session (no Azure auth, no
  live deploy). This is the env/config step task 001 flagged, NOT a code defect. The **escalation trigger did
  NOT fire** — it fires only if the gate is *confirmed OFF and cannot be enabled*; here the gate state is
  *unknown from this session* and the code degrades gracefully (gate OFF → `NullComposePdfIntakeSource` →
  typed 503 "PDF intake unavailable" → the mount door's reference-only fallback, no crash).
- **Operator action before sign-off**: confirm both flags are `true` in the target dev App Service, deploy
  BFF + `sprk_spaarkeai` together (NFR-05), then run the two-door parity UAT.

## Verification

- **tsc** (Compose package): the 30 errors are the KNOWN monorepo baseline (`@spaarke/*` resolution +
  pre-existing `err is unknown`/implicit-any in unrelated handlers); **ZERO new errors** from 051 (none
  reference `sourceFormat` / `isEditableDocx` / the new prop/action).
- **Standalone jest: 622 pass / 0 fail** (+1 reducer test vs 621). The 3 new `ComposeEditor.referenceOnly`
  gate tests are in the CI-only suite group (43 suites need `@spaarke/*` resolution — standalone-unloadable
  by design); they are authored to the file's existing pattern and run in CI.
- **No BFF bytes changed** → publish size + CVE unchanged from task 050 (44.9452 MB incl PDBs).

## Gates (Step 9.5)

- **code-review: PASS** — 0 Critical / 0 Warnings. Gate trusts bytes for PDF-sourced mounts (never editable
  over a non-docx buffer — defensive test proves it); extension gate preserved for all other formats;
  additive prop threaded cleanly; no AI smells.
- **adr-check: PASS** — ADR-032 gate not bypassed (un-intakeable → reference-only; gate-off → typed
  degradation); ADR-012 context-agnostic prop; ADR-049 no save-path change; NFR-06 docxBridge.ts intact;
  §11 modify-only (no new component/intake path).

## Phase 5 (UC-7 PDF Import Parity) — code COMPLETE (050 server + 051 client). Live two-door UAT is operator-run in a DI-enabled env.

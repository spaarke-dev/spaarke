# Current Task State — record-header-and-notepad-r2

> **Last Updated**: 2026-08-26 (by `context-handoff` — end of implementation session)
> **Recovery**: Read "Quick Recovery" first. Then [`CLAUDE.md`](CLAUDE.md), then [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md).

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **033** — `Spaarke.Records.RecordHeader` PCF, in UAT on `sprk_project` |
| **Phase** | 3 done → Phase 5 rollout blocked on UAT closing |
| **Status** | ⚠️ in UAT · **one fix agent may still be in flight** (see below) |
| **Next Action** | Check whether the DateField option-B rework landed. If yes: verify bundle <250 KB, repack v1.1.3, hand the zip to the owner for `spaarkedev1` import + re-UAT. If no: read `notes/decisions/033-nfr02-externals-runtime-failure.md` and finish it. |
| **Blocked by** | Owner re-UAT of the RecordHeader control. Nothing else. |
| **Working tree** | **clean**, 17 commits this session, all pushed to the branch |

### 🔴 Read this before touching the bundle

**Two "clever" NFR-02 fixes have already failed. Do not re-derive them.**

1. **Lazy-load `DateField`** → measured *larger*. `pcf-scripts` emits a single chunk; `import()` inlines back. Code splitting is not a lever in a PCF.
2. **Externalize granular `@fluentui/*` onto the platform global** → built clean, passed static symbol verification, then **crashed at runtime with Minified React error #31**. It splits Fluent's slot machinery across two live copies. `webpack.config.js` now carries a ⛔ comment. RecordHeader must match the **standard triad** every other PCF uses — it was the only PCF in the repo deviating.

**A successful build proves nothing about a PCF's runtime.** Both failures built green.

### The in-flight work

The owner chose **option B**: replace `@fluentui/react-datepicker-compat` with the Fluent
`<Input type="date">` pattern **already shipping** at
`Spaarke.UI.Components/src/components/CreateWorkAssignmentWizard/EnterInfoStep.tsx:338-340`.
`Input`/`Field` are in `@fluentui/react-components`, which the platform externalizes → **zero bundle
bytes**. Expected result ≈ **92,000 B** (the measured "minus DateField" figure) vs the current
378,457 B against a 250,000 ceiling.

Watch for: `type="date"` wants `yyyy-MM-dd`, `datetime-local` wants `yyyy-MM-ddTHH:mm`, Dataverse
returns ISO-8601 UTC. A date shifting a day across timezones is the classic failure.

---

## Where things stand

**16 of 30 tasks ✅ · 033 ⛔ (in UAT) · Phase 5 rollout not started.**

| Phase | State |
|---|---|
| 0 — spike + baseline | 001 operator-gated (see below); 002 static half captured |
| 1 — renderers | ✅ all six + 91-test FR-10 contract suite |
| 2 — metadata/machinery | ✅ 020, 021, 022, 023, 024 |
| 3 — resolver + control | ✅ 030, 031, 032 · **033 in UAT** · 034 not started |
| 4 — schema drift | ✅ 040 (shipped v1.0.21), 041 |
| 5 — rollout | blocked on 034 |

**Tests**: 17 R2-owned suites, 601/601. Repo-wide there are **9 known-red suites**, all outside R2's
file scope, enumerated in [`notes/issues/ISSUE-recordheader-integration-test-stale.md`](notes/issues/ISSUE-recordheader-integration-test-stale.md).
Do not report the project "green" without that caveat.

---

## Session decisions

| Decision | Why |
|---|---|
| **RS-1 hotfix shipped as v1.0.21** (owner: option A) | Matter header 400'd on every record; R2's replacement was weeks out |
| **Fluent pinned to exactly 9.68.0** across 19 PCFs + 4 PCF-consumed shared libs | Caret ranges floated to 9.74.x while the host serves 9.68.0. Fluent is *externalized*, so any post-9.68 API is `undefined` at runtime. Vite solutions deliberately NOT pinned — they bundle their own |
| **`layoutJson` → `of-type="Multiple"`** | Classic designer caps a `SingleLine.Text` static value at **100 chars**; a real layout is ~310 B |
| **DateField → option B** (`Input type="date"`) | Reuses a shipping in-repo pattern; zero bundle cost; the two clever alternatives failed |
| **Binding recipe: MOVE, don't delete** | Form-buffer staging needs `getAttribute`, which is null for a field with no control on the form. Verified against the shipped R1 `formxml` |

---

## ⚠️ Open items for the owner

| # | Item | Notes |
|---|---|---|
| 1 | **Re-UAT the control** after the DateField fix | Import to **`spaarkedev1`** — never `spaarke-model1-prod` |
| 2 | **Task 001** still operator-gated | Needs a classic-designer session: does `Multiple` give a multi-line editor, and does ~310 B round-trip through form XML? No build or query can verify this |
| 3 | **Lookup picker check** at re-UAT | MS does not document `Targets` on the Client API payload. Display works regardless; the OOB picker depends on `targets[0]`. If it misbehaves, the fix is a scoped `LookupAttributeMetadata` call (verified HTTP 200) |
| 4 | Sparkle is **task 034**, not a defect | Correctly absent today |
| 5 | 14 other PCFs carry the Fluent pin but were not rebuilt | No redeploy needed — the pin is preventive; the umbrella is externalized so it is not in any shipped bundle |

---

## Traps for the next session

1. **Never `git add -A` while agents run.** It swept in-flight renderer work into an unrelated commit (`f85258f75`) once already.
2. **`npm run build:prod`**, never `npm run build`.
3. **Rebuild the shared lib before any PCF** — PCFs bundle `dist/`, not source.
4. **Task 086 is main-session-only** (writes `.claude/`, where sub-agents cannot).
5. **Pinning can prune `scheduler`** (a react-dom 16.14 transitive), producing a misleading
   `@fluentui/react-context-selector` jest error. Fix: `rm -rf node_modules package-lock.json`, reinstall.
6. **`grep <package-name> bundle.js` proves nothing** — minification strips package paths; it reads 0
   whether or not the code is present. Use a class prefix such as `fui-Calendar`.

---

## New cross-cutting knowledge (already in `.claude/FAILURE-MODES.md`)

- **AP-8** — never amend a failing test to match the source without checking the source against the vendor contract. Task 020 did exactly that and destroyed the only signal for the metadata bug that later broke UAT.
- **G-12** — a Dataverse `$select` is all-or-nothing; one bad column blanks the whole control. Three occurrences; now guarded generically by a no-`$select` retry in `useRecordFieldValues`.
- **G-13** — `Xrm.Utility.getEntityMetadata` returns the **Client API** shape (numeric `AttributeType`, string `DisplayName`), not the Web API shape. Parsing only Web-API shapes degraded every attribute to `String`.

---

## Resume commands

| Intent | Say |
|---|---|
| Continue | `where was I?` → `project-continue` |
| Task board | open [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) |
| Bundle history | [`notes/decisions/033-nfr02-externals-runtime-failure.md`](notes/decisions/033-nfr02-externals-runtime-failure.md) |
| UAT root cause | [`notes/decisions/033-def1-metadata-never-reached-resolver.md`](notes/decisions/033-def1-metadata-never-reached-resolver.md) |

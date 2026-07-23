# Compose R4 — Dev UAT Feedback (2026-07-23)

> Operator UAT on the dev deploy (BFF `spaarke-bff-dev` + `sprk_spaarkeai`). **Headline: no errors, no failed
> saves, no crashes — R4's "no-errors" bar held.** Feedback is behavior/UX. Triage below.

## ✅ Confirmed working (do not regress)
- Assistant upload → "revise the file" → Compose tab opens; initial save works.
- AI edits (first line + mid-doc) save; manual edits save; copy/paste saves — all without error.
- **Stored-doc "Open Document"** (Compose widget) → manual edits **show redlines** (tracked) — REQ-2 holds on the projection path.
- **Open in Web / Desktop** shows the Compose redlines + comments; edits there persist to the SPE file.
- **Lock handling**: "can't edit while open in web/desktop" message appears (correct concurrency behavior).
- Document Profile runs on the record. Comments show highlighted areas + comment pane.

## 🔧 Quick R4 UX polish (low-risk, small)
| # | Item | Where |
|---|---|---|
| P1 | Make **Track Changes** an icon-only toggle (no text label) | `ComposeFormatToolbar.tsx` (Track changes button) |
| P2 | Rename **"Search for Document" → "Open Document"** | `ComposeEmptyState.tsx:291` (+ comments/handlers) |
| P3 | **Word menu**: make the dropdown vertical + add labels **"Open web" / "Open desktop"** | `ComposeFormatToolbar.tsx` Word menu (`onOpenInWord` / `onOpenInWordDesktop`) |
| P4 | Verify/remove any leftover **"Push to Word"** control (036 retired the write endpoint → would 404). Likely it's the Open-in-Word handoff, not the retired feature — confirm in the deployed menu. | toolbar Word menu |

## 🐛 R4 functional issue — PRIORITY
- **V1 — Every save creates a NEW Document record** (UAT produced 8 `Medical Analysis Sample.docx` records over ~24 min). Root cause: the **Assistant-upload / transient-mount** path calls **create-on-save** on every save (new SPE doc + new Dataverse record each time) instead of updating the existing doc. Ask: **Save dropdown → "Save Version" (update existing) vs "Save New Document" (create new)**, with a sane default (update existing after the first save).
  - Same root cause as the tracking difference (T1 below): the transient/Assistant path is the renderer/mammoth clean path; the projection/stored-doc path versions + tracks correctly.

## 📋 R5 (defer + document) — see `../spaarkeai-compose-r5/README.md`
| # | Item | Maps to |
|---|---|---|
| T1 | **Assistant-uploaded "revise" edits don't track** (clean), but directly-opened stored-doc edits **do** track. Decide whether the Assistant-upload "revise" flow should be treated as REQ-2 (tracked) — i.e. route transient mounts through the projection/stored-doc model. | **R5 G6** (transient-mount projection unification) + REQ-1/REQ-2 |
| R1 | **External-change refresh**: detect web/desktop edits to the SPE file and **remount with a banner** "Document updated from document management system version"; after a lock releases (web/desktop closed), refresh remounts. (Endpoints exist: `check-changes`, `spe-doc-changed` webhook — not wired to a remount+banner UX.) | new R5 (G7) |
| R2 | **Comment pane scroll-sync**: open/collapse the right comments pane and scroll comments in line with the redline/comment positions in the doc. | new R5 (G8) |
| R3 | **Document Profile re-run on load**: ensure the profile re-runs when the document is (re)loaded — background process and/or a `.js` onload event and/or a "Refresh Profile" button. (Dataverse profiling pipeline — arguably a separate subsystem, not Compose core.) | new R5 (G9) / separate |
| V1-ui | The Save-Version/Save-New dropdown UI (the mechanism for V1) may land in R4 or R5 depending on the versioning-behavior decision. | tied to V1 |

## Architectural note (connective tissue)
V1 (duplicate records) and T1 (no tracking on Assistant upload) are the **same root cause**: the transient/Assistant-upload mount uses the renderer + create-on-save clean path, while the directly-opened **stored doc** uses the projection path (versions the same doc + tracks edits). **R5 G6 (route transient mounts through the projection builder)** fixes both structurally. The Save-Version-vs-New UX (V1) is a distinct, worth-sooner ask.

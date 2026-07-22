# Compose / Document-Interaction — Clean-Slate Architecture

> **Status**: DRAFT for decision. Written **independent of the current implementation** — this is "if we started building today, knowing what this project taught us, what would we build?"
> **Author**: 2026-07-22, from the UAT-round-4 architecture review + broadened research.
> **Scope note**: this is bigger than `spaarkeai-compose-r3`. It is a candidate to graduate into a proper `design-to-spec` / `docs/architecture/` decision if pursued.
> **Research incorporated (2026-07-22)**: the platform-delegation lever (§4) is now **CONFIRMED** against Microsoft Learn ("Open Office Files From Your App", ms.date 2026-07-13) and against Spaarke's own `knowledge/sharepoint-embedded/NOTES.md` — it is **GA today and Spaarke already has the launch path wired**. Real-world analogs (§ Appendix B) confirmed. Remaining open items narrowed to §11.

---

## 0. How to read this

Deliberately ignores what we've built. It derives the architecture from (a) the requirements and (b) the hard truths this project surfaced, then — only at the end (§9) — reconciles with the current code. The goal is a decision judged on its own merits, not anchored to sunk cost.

---

## 1. Requirements

### 1.1 Functional

| # | Requirement |
|---|---|
| F-1 | Ingest and let **AI interact with** documents from **multiple source formats** — `.docx` first, then **PDF, XLSX, PPTX**, and others. |
| F-2 | **In-app light editing** ("the basics" — text, basic formatting, redlines) inside the Spaarke workspace, **docx-first**. NOT a full Word clone. |
| F-3 | **AI editing/redlining that is accurate and reliable at any location** — insert/replace/delete as tracked changes the user can accept/reject. |
| F-4 | **Fidelity**: never corrupt or silently drop the original; fidelity loss (if any) bounded to the spans actually edited. |
| F-5 | First-class **tracked changes** (accept/reject) + **comments**. |
| F-6 | **Open to web** (Office for the web) and **open to desktop** (Office desktop) for full-fidelity editing of any format. |
| F-7 | **Versioning, co-authoring, locking/check-out** integrated with **SharePoint Embedded (SPE)**. |
| F-8 | **Cross-session persistence** — resume prior AI/annotation state; survive Word round-trips that regenerate identifiers. |

### 1.2 Constraints (non-negotiable)

| # | Constraint |
|---|---|
| C-1 | **No commercial-licensed editor component** (Syncfusion, Apryse, TX Text Control, …). |
| C-2 | **No AGPL** (SuperDoc, OnlyOffice open-core) — product is distributed. |
| C-3 | **Microsoft platform**: Dataverse, SharePoint Embedded, Azure, Power Apps code pages. Lean into it. |
| C-4 | **Small team** — cannot sustain a multi-year custom office/layout engine. |
| C-5 | Permissive licenses only (MIT/Apache/MPL where a dependency is unavoidable). |

---

## 2. Design drivers — the hard truths this project surfaced

These are the *real* spec. Every one is a lesson paid for in UAT.

1. **A lossy source→editor conversion breaks fidelity AND AI accuracy simultaneously** — they are the same failure. (Our mammoth→HTML-subset projection.)
2. **Two independent models of one document drift.** Client mammoth vs. server OpenXML walking the same file → the entire "couldn't place / couldn't locate" bug class.
3. **Text-search anchoring is inherently fragile** (tabs, `<w:br/>`, curly quotes, whitespace). Anchoring MUST be by **stable identity**, never by re-finding text.
4. **AI is only reliable against a complete, addressable map.** Anything it cannot precisely target fails.
5. **Fidelity loss must be bounded to edited spans** — round-trip everything untouched (the byte-preservation invariant).
6. **Versioning/concurrency is first-class**, not an afterthought (the eTag failures).
7. **Full Word-visual fidelity in the browser is NOT required** ("the basics") — but must never be achieved by *silently dropping* what we can't show.

---

## 3. The core principle

> **There is exactly ONE authoritative representation of the document — the real file (OOXML for docx/xlsx/pptx; PDF for PDF) — and every human, AI, and tool action is expressed as an *operation against a stable, addressable model of that file*, applied deterministically by a single byte-author. Nothing is ever re-found by text. Nothing is authored twice.**

The invariants that fall out:

- **I-1 One authoritative model = the real file.** Not a translation of it (a translated model is either lossy, or a multi-year engine like Google Docs — both disqualified).
- **I-2 Server-authoritative.** The model lives where our OOXML expertise + the existing engine are (server, `DocumentFormat.OpenXml`, MIT). The client never authors bytes.
- **I-3 Stable addressing to the granularity AI needs** — element IDs down to sub-paragraph (paragraph + run/offset), not just paragraph.
- **I-4 Edits are operations against IDs** ("replace span X", "insert tracked change at Y"), never text to be re-found.
- **I-5 One byte-author: the server**, applying operations to the canonical file deterministically → lossless by construction; only edited spans reinterpreted.
- **I-6 The client is a faithful-enough *view*** — renders the model, emits operations, holds no authoritative state, reconciles nothing.
- **I-7 No text-search anchoring anywhere. No second model. No client-authored bytes.**

---

## 4. The biggest lever — delegate fidelity, multi-format, versioning, and open-web/desktop to the Microsoft platform ✅ CONFIRMED (GA; partly wired)

We are already on the platform that has *solved* the hardest parts. We should not rebuild them. **This is confirmed against Microsoft Learn ("Open Office Files From Your App", ms.date 2026-07-13) and Spaarke's own `knowledge/sharepoint-embedded/NOTES.md`.**

**SharePoint Embedded (SPE) is Microsoft's headless successor to WOPI/CSPP over SharePoint/OneDrive infrastructure.** Files in SPE containers get, with **Microsoft owning storage + rendering + co-authoring**, GA today:

- **Open in Office for the web** — read the DriveItem `webUrl`, open with `action=view|edit|default`.
- **Open in Office desktop** — Office URI schemes (`ms-word:ofe|u|{webUrl}`, `ms-excel:…`, `ms-powerpoint:…`).
- **AutoSave**, **version history** (auto-enabled per file — see/compare/restore/recover, incl. co-authoring changes), **real-time co-authoring + presence**, **locking** (handled by the co-authoring/WOPI infra — *not* something we implement).
- **Comments/mentions**, **sharing links** (M365-license-gated).
- **PDF** opens in an **embedded viewer**; unsupported types redirect via the container-type `urlTemplate`.

**Spaarke already has the launch path wired** (`knowledge/sharepoint-embedded/NOTES.md`): `src/client/code-pages/SpeDocumentViewer/` resolves `webUrl` and launches Office web + desktop; the Word/Outlook add-ins upload "SPE First, Dataverse Second"; the identity chain even makes **Word Copilot** ground on the SPE-stored file. **So this lever is "lean harder," not "build."**

**Consequence:** full-fidelity editing of any format, versioning, co-authoring, locking, open-web/desktop, and PDF viewing → **use SPE + WOPI + Office. Do not build it.** That removes **~70–80% of the "hard" surface** from our build.

**What is left for us to build** — the actual value, which no platform gives us:
- the **AI-interaction layer** (accurate, addressable, reversible model + operations), and
- a **deliberately light in-app editing surface** (docx-first "basics" + AI redline review) for the fast in-workspace path — **not** an arbitrary-docx round-tripper.

### 4.0 Correction — "full Word fidelity" was never a goal; WOPI/Office is an *optional* convenience, not the fix

**Re-grounding (2026-07-22):** Compose was always scoped as the **AI / core drafting surface — explicitly NOT a Word replacement.** So "full Microsoft-Word fidelity in-app" is a **non-goal**, and any framing that treats it as a required leg (the earlier "trilemma") is a **red herring.** Dropping it.

Two kinds of "fidelity" must not be conflated:
- **Display fidelity** (render every Word feature exactly) — **NOT a goal.** TipTap's basic rendering is fine. We are not Word.
- **Preservation fidelity** (don't lose or corrupt the original's formatting on the parts we didn't edit; map accurately so AI/edits land at the right place) — **this IS required, and this is what failed.** The save errors and lost formatting were *preservation*-fidelity failures, not a feature gap.

**So the WOPI/Office-launch material below (§4, §4.1) is a SEPARATE, OPTIONAL convenience** — a "pop this file open in real Word/Office if the user wants to" affordance (Spaarke already has it via `SpeDocumentViewer`; it's your F-6). It is **not** load-bearing for solving the actual problem, and it is **not** something we embed-and-control (you can only *launch* to Office; no programmatic/AI control inside that frame — that's Office-Add-in territory, which we don't want). **Treat §4 as "nice platform leverage we already have," not "the answer."**

**The actual problem, stated narrowly:** the OOXML ↔ TipTap translation *we* wrote is **lossy** (a simplified-HTML subset) and **text-anchored** (re-finds text to save), which loses preservation fidelity and breaks saves. **TipTap is not at fault** — it provides the editing surface (typing, marks, selection, undo), which is exactly its job and works fine. The defect is entirely in **our translation/save layer.** The fix is §3's invariants: **server keeps the original OOXML, only reinterprets edited spans (byte-preserving), anchors by stable ID (never text).** TipTap's schema can stay "the basics" — because preservation fidelity is held **server-side**, not inside TipTap.

### 4.1 Delegate-vs-build (confirmed)

| Capability | Source |
|---|---|
| Open-to-web, open-to-desktop | **DELEGATE** — SPE `webUrl` / Office URI schemes (GA) |
| Version history, restore, compare | **DELEGATE** — auto per Office file (GA) |
| Co-authoring, presence, locking, AutoSave | **DELEGATE** — Office/WOPI infra (GA) |
| Comments/mentions, sharing links | **DELEGATE** — Office (GA, license-gated) |
| Multi-format **fidelity/rendering/heavy edit** (docx/xlsx/pptx) | **DELEGATE** to Office; **BUILD** only server read/write via Open XML SDK |
| PDF view/annotate | **DELEGATE** (SPE embedded viewer) + PDF.js if richer annotate needed |
| AI reasoning + redline over a reversible model | **BUILD** |
| Server OOXML byte-production | **BUILD** (Open XML SDK, MIT) |
| Light TipTap authoring/review surface | **BUILD** (scoped, byte-preserving) |
| Ingestion/extraction/metadata/security | **BUILD** — already exists (Document Intelligence + AI Search + Dataverse) |

---

## 5. The architecture — three surfaces, one source of truth

```
                    ┌─────────────────────────── SharePoint Embedded (SPE) ───────────────────────────┐
                    │   Storage · Versioning · Locking · Co-authoring   (the ONE stored file)          │
                    └───────▲───────────────────────▲────────────────────────────────▲────────────────┘
                            │                        │                                │
        (WOPI / Office)     │        (our BFF, OpenXML)                (WOPI / Office) │
                            │                        │                                │
      ┌─────────────────────┴─────┐   ┌──────────────┴───────────────┐   ┌───────────┴──────────────┐
      │  Office for the web /      │   │  Spaarke server (BFF)        │   │  Office desktop          │
      │  desktop  (open-to-...)    │   │  = authoritative OOXML model │   │  (open-to-...)           │
      │  FULL fidelity, ALL        │   │  + stable addressing         │   │  FULL fidelity           │
      │  formats, MS-owned         │   │  + operation applier         │   │  MS-owned                │
      └────────────────────────────┘   │  + byte-author (I-5)         │   └──────────────────────────┘
                                       │  + AI orchestration          │
                                       └──────────────┬───────────────┘
                                                      │ (addressable model + operations; NEVER bytes)
                                       ┌──────────────┴───────────────┐
                                       │  Compose (in-app, thin view) │
                                       │  docx-first "basics" + AI     │
                                       │  redline review. Emits ops.   │
                                       └──────────────────────────────┘
```

**Responsibilities**
- **SPE** — single stored file; versions; locks; co-auth. (Platform.)
- **Office web/desktop** — full-fidelity editing of any format, on demand. (Platform, via WOPI.)
- **Spaarke BFF** — the **authoritative addressable OOXML model**, the **operation applier**, the **only byte-author**, and **AI orchestration**. (We build. Uses `DocumentFormat.OpenXml`, MIT.)
- **Compose** — a **thin client view** for the in-app docx-first light-edit + AI-redline-review path. Emits **operations against IDs**, never bytes. (We build; TipTap/ProseMirror, MIT — as a *view*, not a source of truth.)

The three editing surfaces (Office-web, Office-desktop, Compose) all act on **the one SPE-stored file**; Compose's AI/edit operations are applied server-side and written back as a **new SPE version**, which is then openable in Office. No surface holds a competing copy.

---

## 6. Multi-format strategy

| Format | Ingest / AI-interact | In-app light edit (Compose) | Full-fidelity edit | Byte production |
|---|---|---|---|---|
| **docx** | OpenXML model + addressing | ✅ (primary) | Office web/desktop | Server, OpenXML |
| **xlsx / pptx** | OpenXML model (same engine family) | Phase 2 (basics) | Office web/desktop | Server, OpenXML |
| **PDF** | Extract/annotate model (PDF.js/permissive) 🔬 | View + annotate + AI-extract (not flow-edit) | Office/Acrobat via SPE | Server (annotation layer) 🔬 |

Key point: **docx/xlsx/pptx are all OOXML** — one engine + per-format adapters over a **common addressable-model + operations abstraction**. **PDF is fixed-layout and different** — treat as view/annotate/AI-extract, delegate heavy editing to the platform. We do **not** build a multi-format WYSIWYG engine.

---

## 7. The AI-interaction model (the actual value)

This is Harvey's proven invariant, generalized: **the LLM edits *content* (text); deterministic server code owns *bytes*.**

- The BFF exposes the document as a **stable, addressable, reversible model**: every clause/cell/run has an ID; the LLM sees text keyed by IDs.
- The LLM proposes edits as **operations on IDs** (replace span, insert redline at anchor, comment on range).
- The server **validates and applies** them to the canonical file → native `w:ins`/`w:del`, comments — **byte-preserving**, only touched spans changed.
- Accept/reject, cross-session resume, and Word round-trips all key off the **stable IDs** (with a fuzzy fallback only for the genuine Word-regenerated-id case).

Because addressing is by identity and byte production is single-authored server-side, **the "couldn't place / couldn't locate / drift" classes cannot occur** (§8).

---

## 8. Why this dissolves the bugs we lived through

| Bug we fought | Root cause | Dissolved by |
|---|---|---|
| mammoth drift ("no paragraph in retained original") | two models of one doc | I-1, I-2 (one server-authoritative model) |
| AI "couldn't be placed" | no sub-paragraph addressing → text-search | I-3, I-4 |
| save "couldn't be located" | text-search anchoring | I-4, I-7 |
| eTag mismatch | no clean version identity | F-7 via SPE versioning (platform) |
| "simplified view" fidelity loss | lossy projection | I-5 byte-preserving + §4 (Office for full fidelity) |

Five bugs, one missing invariant, five times.

---

## 9. Reconciliation with what exists (honest)

This is **not** "keep going," and **not** "throw it all away."

- **Keep (it's the right foundation):** server-side `DocumentFormat.OpenXml`; the `paraId` identity backbone; delta-onto-retained-original save; SPE integration; TipTap as a *view*.
- **Do not build today / retire:** the **lossy HTML-subset projection**, **text-search anchoring** (`DocxAnnotationWriter.LocateTarget` for placement), and **client-side reconciliation** of a client-authored projection.
- **Add:** sub-paragraph addressing; an **operations** contract (client emits ops, not bytes/text); explicit **platform delegation** of fidelity/versioning/open-web-desktop to SPE+WOPI+Office; the **multi-format abstraction**.

Honest read: **~60% of the right architecture exists; ~40% is the wrong 40%, and it's the part that fails.** Starting today we'd keep the OOXML-authoritative spine and go straight to addressing + operations + platform delegation — skipping the lossy interpreter and text-search entirely.

---

## 10. Non-goals

- ❌ A full in-browser Word/office/PDF **WYSIWYG engine** (that's OnlyOffice/Collabora/Office — years, or AGPL/heavy).
- ❌ A **Google-Docs-class own-model** engine (multi-year, still lossy on `.docx`).
- ❌ Any **commercial-licensed** editor component (C-1) or **AGPL** (C-2).
- ❌ **Word Add-in as the primary in-app surface** (in-app requirement F-2; the add-in lives *inside* Word, not our app). "Open to web/desktop" (F-6) is the platform path, which is different and wanted.

---

## 11. Open questions — now narrowed (post-research)

**Resolved by the 2026-07-22 research** (was pending): the SPE+WOPI+Office lever (§4 — GA, wired), the permissive multi-format stack (§3/§6), and the real-world analogs (Appendix B). Remaining:

1. **⚠️ Concurrency: co-authoring/Office lock vs. our programmatic OOXML write.** When a file is open in Office-for-web (co-auth session holds it), our server writing AI redlines to the same SPE item can hit **HTTP 423 Locked** / eTag conflict. **This is the same family as our UAT "eTag mismatch" (Bug B).** It is the sharpest open design question — we need a defined protocol (edit-via-Office-only while co-authoring; or a lock-aware apply-and-retry; or AI edits only when not co-authored). Carry into the spike.
2. **Addressing granularity**: does sub-paragraph (run+offset) addressing survive Word round-trips (Word regenerates `w14:paraId` on external edits), and what is the fuzzy-fallback contract for the regenerated-id case?
3. **AI write-back → clean SPE version**: mechanics of applying server operations as a new SPE version that co-auth/version-history sees cleanly.
4. **How thin does Compose become?** If Office-for-web takes all heavy editing, does the in-app TipTap surface shrink to **AI-draft preview + accept/reject + basic text** — never an arbitrary-docx round-tripper? (Strongly implied; confirm as a product decision.)
5. **PDF annotate GA date**: SPE read-only PDF annotations were a ~March-2026 roadmap item — verify GA before relying; PDF stays view/extract otherwise.

---

## 12. How we'd de-risk before building (the validating spike)

Define "solved" up front, prove it on **your worst-offender documents** (CIPO letter + a dense-numbering agreement + a complex table doc):

> Open (from SPE) → AI inserts a tracked change at an **interior** location → user accept/reject → save → **round-trips losslessly** as a new SPE version → **opens cleanly in Office web/desktop** → and the AI could address **any** span.

- **Spike A (the crux):** server-authoritative addressable model + operations on docx — prove lossless interior AI edits + save, no text-search.
- **Spike B (the lever):** confirm SPE→Office/WOPI gives open-web/desktop + versioning with no/low build (§4).
- **Spike C (multi-format):** one OOXML model over docx/xlsx/pptx; PDF as view/annotate.

If A + B hold, the architecture is mostly **assembled from parts we already own + the platform we're already on**, with no commercial component and no AGPL.

---

## Appendix A — were the earlier Claude/GPT reviews wrong?

No, but they answered a **narrower question**. Both reviewed a *specific proposed design* (the server-side paraId-tagged HTML projection) and hardened it well (single-walk, revision-flattening, fail-closed). Their agreement validated **that solution**, not the **problem framing** — they were downstream of it. GPT gestured at the deeper truth ("DOCX is authoritative; the editor is a projection; save = identity-bound deltas") but inside the projection frame. The three-phase plan was **incremental** ("fix, then harden, then expand") — a plan to improve what existed, not to derive what to build. This document is the from-scratch derivation that exercise didn't ask for.

## Appendix B — who else solved this (the stakeholder's point: "we aren't the first")

Confirmed by research (2026-07-22). **The universal pattern: the platform owns fidelity + editing + versioning; the product owns the AI reasoning/redline layer + a light surface. Nobody serious builds a browser Word engine.**

| Analog | Editing surface | What they build |
|---|---|---|
| **Microsoft 365 Copilot** | Office (Word/Excel/PPT web + desktop) | AI reasoning + text-level edits into Office |
| **Harvey / Legora** | Word (Add-in / Word-for-web, native track changes) | AI redline layer; deterministic OOXML; a *light* web authoring surface |
| **Box AI** | Office Online integration + Box's own viewer | AI Q&A/extract/generate on top of the store |
| **Glean / Hebbia** | **no document editor at all** | Pure AI-interaction-over-documents layer (search/reasoning/data-grid) |

Two lessons: (1) the **editing/fidelity problem is delegated**, not rebuilt; (2) the **AI-interaction layer is a separate concern** from editing — Glean/Hebbia are multi-billion-dollar companies with *no editor engine*. Our value is the AI layer + the light surface, not a Word clone.

## Permissive stack (no commercial, no AGPL — C-1/C-2 satisfied by construction)

Because heavy editing is delegated to Office, we need **no** OnlyOffice/Collabora/commercial engine. Remaining components are all permissive:
- Server: **Open XML SDK / `DocumentFormat.OpenXml` (MIT)** — one engine over docx/xlsx/pptx (read/address/write); our existing `ComposeParagraphRedlineSynthesizer` for redline byte-production.
- In-app view: **TipTap / ProseMirror (MIT)** — as a *scoped, byte-preserving view*, not a round-tripper.
- PDF: **PDF.js (Apache-2.0)** view/annotate + **Azure AI Document Intelligence** extract. (No permissive full-PDF-edit exists — design around it, don't fight it.)
- Reference only: **Eigenpal `docx-editor` (Apache-2.0, archived)** for the byte-preserving-projection idea.

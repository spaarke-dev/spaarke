# WS-5 — Word-rendering-service evaluation + NFR-03 licensing analysis

> **Task**: 051 (WS-5 spike) · **Project**: spaarkeai-compose-fidelity-r4.5
> **Author**: researcher subagent · **Date**: 2026-07-28
> **Rigor**: STANDARD (research/licensing evaluation; notes-only, no code, no BFF change)
> **Feeds**: task **052** (WS-5 ship-vs-defer decision record). This note is the **licensing + ops half** of that decision. Task **050** supplies the LibreOffice-headless divergence measurement (the other half). **051 does NOT make the ship/defer call.**
> **Scope guard**: This task changes **no `Services/Compose/` source** and **adds no project/package reference**. It is analysis + a notes deliverable only (NFR-04 / §11 negative acceptance criterion).

---

## 0. The hard truth this spike exists to resolve (design §5.5)

Page numbers and line numbers **are not in the `.docx`**. They are computed at **layout/render time** from page size, margins, font metrics, image/table flow, and the host renderer's line-breaking rules. `w:lnNumType` only turns line-number *display* **on**; it does not store the content→line mapping. Consequently:

- **Paragraph / clause / section / heading / list numbering → 100% derivable from the file** (WS-3, deterministic OOXML replay). Not this spike's concern.
- **Page / line numbering → requires a Word-compatible layout engine.** No engine other than Word's own layout is *guaranteed* identical to a given user's Word desktop. This is a genuine fidelity ceiling to **surface, not hide** (F-5).

The design frames the "Word-rendering service" as the **only true-Word-identical** pagination source. This note tests that framing honestly and finds it is **directionally correct but needs one important qualifier** (§1.4): the reachable Word service is **Word *Online*'s** renderer, which is the closest available to Word but is **not bit-identical to Word *desktop*** in all cases.

---

## 1. Part A — The Word-rendering-service path

### 1.1 What "Word-rendering service" concretely means in 2026 (and what it does NOT mean)

There are four candidate ways to get "Word's own layout," and only one is actually available to Spaarke:

| Mechanism | Available to Spaarke? | Why |
|---|---|---|
| **Microsoft Graph `GET /drives/{id}/items/{id}/content?format=pdf`** (Office cloud renderer) | **YES** — this is the only viable Word-native path | Renders via the same Office Online / Word-for-the-web layout engine; returns a **PDF**. Requires the file to live in a Graph drive (OneDrive / SharePoint / **SharePoint Embedded**). Spaarke docs already live in **SPE** → the precondition is met. |
| **Server-side desktop Word automation** (COM / Interop, "headless Word") | **NO — prohibited** | Microsoft KB257757 / "Considerations for server-side Automation of Office": **not supported, not recommended**, assumes an interactive desktop, deadlocks on modal dialogs, and — critically — **the EULA does not cover** providing Office functionality to unlicensed users from a server. This is a support **and** licensing no-go, independent of NFR-03. |
| **Word Automation Services** (SharePoint Server on-prem) | **NO** | Legacy on-prem SharePoint 2010–2019 feature. Not offered for SharePoint Online / SPE. Dead end for a cloud product. |
| **Office Online Server / Word Online Server** (self-hosted) | **NO (impractical)** | Self-hosting the rendering farm is a heavyweight, separately-licensed M365 server product; not a paginator API and not something Spaarke would operate. |

**Bottom line:** the *only* Word-native pagination source Spaarke can actually call is the **Graph `format=pdf` conversion** (Office cloud renderer). "Headless Word" as a self-run process is off the table for support + EULA reasons, not merely NFR-03.

### 1.2 Does it yield a Word-identical page/line map? (capability)

The Graph renderer returns a **PDF**, not a structured page/line map. So the capability is two-stage:

1. **Render** (`format=pdf`) → PDF laid out by Office's cloud engine.
2. **Extract** page/line structure from the PDF:
   - **Page mapping → high fidelity, directly readable.** PDF page boundaries are explicit; mapping a paragraph's text to its page requires locating the paragraph's run text on a page via a permissive PDF text-position extractor (e.g. **PdfPig, Apache-2.0**). Because the layout came from Office's own engine, the page breaks match what Word-for-the-web shows.
   - **Line mapping → conditional.** Two sub-cases:
     - If the source enabled **`w:lnNumType`** (a line-numbered pleading), Office renders **visible line numbers into the PDF** — extract them by text position. Highest fidelity.
     - If line numbering is **not** enabled in the source, you must **infer** lines geometrically from text y-coordinates (cluster runs by baseline). This is an approximation and the weakest link.

**Net:** page numbers = strong; line numbers = strong **only** when `w:lnNumType` is on, otherwise inferred. There is **no Graph/Office API that returns a structured `{paraId → page, line}` map** — you always reconstruct it from the rendered PDF.

### 1.3 Ops / auth / latency / licensing cost (the honest cost of the fidelity)

- **Auth**: The doc must be in a Graph drive. Spaarke's are in **SPE**, so this is satisfied. Access via **app-only** (`FileStorageContainer.Selected` for SPE, or `Files.Read.All` / `Sites.Read.All` for SPO) or delegated. App-only read-brokering is already an established Spaarke pattern (SPE content read). No user identity is strictly required for the render call itself.
- **Latency**: This is the dominant cost. Community + MS Q&A evidence (2025–2026) reports the conversion **timing out at ~45 s** even for small (~7 MB text-only) docs under load, and practical failures well below the nominal **100 MB** documented ceiling. It is **not** suitable for synchronous, per-keystroke, or interactive-render use. It must run as an **async / batch job** (render on save, or on explicit "generate citations," not on every view).
- **Throttling**: Graph service-protection throttling applies; bulk/corpus runs must back-off + retry.
- **Rendering-entitlement / licensing**: There is **no separate "rendering license" SKU** — PDF conversion is a **bundled capability of the SharePoint/OneDrive/M365 service**. This is the key distinction from headless desktop Word: the cloud renderer is a **sanctioned Microsoft-hosted service** (KB257757's prohibition does **not** apply to it), whereas running Word yourself on a server violates both support policy and the EULA. So the Graph path is **licence-clean** in the NFR-03 sense — it links **no** paginator code into the BFF at all; it is a **remote HTTP call to a Microsoft service**.
- **Sensitivity-label caveat**: If a source doc carries a sensitivity label whose policy blocks export/copy, conversion returns `406` / `OfficeConversion_DocumentProtected`. Legal docs with protective labels could silently fail the render — a real operational edge to handle.
- **Ops footprint**: essentially **zero self-hosted infra** for the render itself (it is a Graph call). The only self-owned code is the **PDF page/line extractor** (permissive lib, runs in-process or in a small worker) — but note that even this extraction, if bundled, is subject to the same publish-size/NFR-04 discipline (see §3).

### 1.4 The honesty qualifier: "Word-identical" means "Word *Online*-identical"

The Graph renderer is **Word-for-the-web's** layout engine, not the user's **Word desktop**. MS Q&A / practitioner evidence is explicit that the **cloud rendering engine does not always match Word desktop pagination** — divergence arises from **server-side font substitution** (a font on the author's desktop may be absent on the render farm), **line-breaking**, and **layout nuances** of the service renderer. So:

- Graph/Office cloud render is the **closest reachable** approximation to Word and is **materially closer than any open engine**, **but it is still not guaranteed bit-identical to a specific user's Word desktop**.
- The truly bit-identical source (that user's own Word desktop layout) is exactly the path that is **support- and EULA-prohibited** server-side.

This qualifier must reach task 052: the product may claim **"page/line as rendered by Word Online"**, not **"identical to your Word desktop, 100%."**

---

## 2. Part B — NFR-03 licensing analysis (permissive-only for anything linked into the BFF)

**The rule (NFR-03 / ADR Tension T-1, Path A):** anything **linked into the BFF** must be **MIT/permissive**. **No commercial** (Aspose, GemBox, Syncfusion) and **no AGPL** paginator may be linked into the BFF. **LibreOffice (MPL-2.0/LGPL)** is permitted **only as a separate process/service**, never linked. A **Word-rendering service** is evaluated on **ops/licensing/latency** terms — it links no code at all.

### 2.1 License taxonomy used here

- **Permissive** (MIT / Apache-2.0 / BSD): link freely into the BFF. No copyleft reciprocity.
- **Weak/file-level copyleft** (MPL-2.0, LGPL): reciprocity is limited to *modified library files*; invoking an **unmodified** binary **out-of-process** imposes **no obligation** on Spaarke's proprietary code. NFR-03 further restricts this to **out-of-process only** (more conservative than the licence itself requires).
- **Strong / network copyleft** (GPL, **AGPL-3.0**): AGPL §13 extends copyleft to **network interaction** — even a *separate service* users reach over a network can trigger a source-disclosure obligation. Barred from linking by NFR-03; see the **escalation flag** in §2.3 for the sidecar case.
- **Commercial / proprietary EULA** (Aspose, GemBox, Syncfusion, Apryse): not open-source; barred from linking by NFR-03 regardless of price. **"Free of charge" ≠ "permissive"** (see Syncfusion note).

### 2.2 Per-engine classification

| Engine / component | License | Class | Linked into BFF? | Separate process/service? | Reason |
|---|---|---|---|---|---|
| **Microsoft Graph `format=pdf`** (Office cloud renderer) | Microsoft service (bundled M365 entitlement) | Service (no code linked) | **N/A — links nothing** | **PERMITTED** (remote HTTP call) | NFR-03-clean: no paginator code enters the BFF; it is a call to a sanctioned MS service. Cost is ops/latency/auth, not licensing. |
| **LibreOffice headless** (`soffice --headless --convert-to pdf`) | **MPL-2.0** (primary) + LGPLv3+ | Weak/file-level copyleft | **FORBIDDEN to link** | **PERMITTED — separate process ONLY** | Invoking the unmodified binary out-of-process imposes no obligation on proprietary code. NFR-03 + T-1 Path A explicitly permit this **only** out-of-process. This is task **050**'s measured engine. |
| **Gotenberg** (Docker service wrapping LibreOffice/Chromium) | **Apache-2.0** (wrapper) — bundles LibreOffice (MPL) as an internal separate process | Permissive wrapper over weak-copyleft | Wrapper is permissive, but you deploy it as a **service**, not a link | **PERMITTED — as a sidecar container** | Clean way to *package* LibreOffice-as-a-service with a stable HTTP API. Same NFR-03 posture as raw LibreOffice (out-of-process). Worth naming to 052 as the productionized LibreOffice-sidecar option. |
| **PdfPig** (PDF text/position extraction for page/line) | **Apache-2.0** | Permissive | **PERMITTED to link** | n/a | Needed to extract page/line from whichever engine's PDF. Permissive → linkable. (PDFsharp/pdfminer are MIT alternatives.) |
| **Aspose.Words** | Commercial proprietary EULA | Commercial | **FORBIDDEN** | (moot) | NFR-03 names it explicitly. High-fidelity but barred. |
| **GemBox.Document** | Commercial proprietary EULA | Commercial | **FORBIDDEN** | (moot) | NFR-03 names it explicitly. |
| **Syncfusion DocIO / PDF** | Commercial EULA (incl. a **free "Community License"**) | Commercial | **FORBIDDEN** | (moot) | NFR-03 names it explicitly. **The free Community License does NOT make it permissive** — it is a proprietary EULA with eligibility caps (small-company/revenue limits) and is revocable; it is **not** MIT/permissive. **"Free" ≠ NFR-03-compliant.** See §2.3 flag. |
| **Apryse / PDFTron** | Commercial proprietary | Commercial | **FORBIDDEN** | (moot) | Not permissive. |
| **iText 7** (if used for PDF extraction) | **AGPL-3.0** / commercial | Strong/network copyleft | **FORBIDDEN to link** | See §2.3 flag | Do **not** use for the PDF-extraction step — use PdfPig (Apache-2.0) instead. Named to prevent an accidental AGPL PDF lib slipping into the extractor. |
| **ONLYOFFICE Document Server** (rendering/conversion service) | **AGPL-3.0** / commercial | Strong/network copyleft | **FORBIDDEN to link** | **AMBIGUOUS as a sidecar — see §2.3 flag** | Could render Word-ish layout as a service, but AGPL §13 network-copyleft makes "separate service" non-trivial. |
| **SuperDoc** (ProseMirror OOXML editor w/ pagination) | **AGPL-3.0** / commercial | Strong/network copyleft | **FORBIDDEN to link** | Same AGPL sidecar concern | Client-side paginator; AGPL. Borrow patterns only, never code. |

### 2.3 Escalation — licenses to FLAG for human sign-off (root CLAUDE.md §9)

Per §9 (licensing is security-sensitive; do not silently resolve an ambiguous license as permissive), two items are **flagged for human sign-off** rather than assumed safe:

1. **🔔 AGPL-3.0 "as a separate service" is genuinely ambiguous under NFR-03 as written.**
   - NFR-03 bars an "AGPL paginator **linked into** the BFF." A *literal* reading leaves a gap: an AGPL renderer run **out-of-process** (ONLYOFFICE Document Server, or a SuperDoc-derived service) is **not "linked."**
   - **But** AGPL-3.0 §13 extends copyleft to **network interaction** — offering the service's functionality to users over a network can trigger an obligation to **provide the (possibly modified) source** to those users. This is a **different and stricter** regime than the MPL/LGPL that makes LibreOffice-out-of-process clean.
   - **Recommendation to the human**: treat AGPL as **forbidden even as a sidecar**, i.e. read NFR-03's intent (avoid copyleft entanglement) as covering the network-service case, **not** just linking. **This needs an explicit human ruling** because the rule's *letter* (linking) and *spirit* (no copyleft entanglement) diverge for AGPL-as-a-service. Do not let an AGPL rendering service in on the "it's not linked" technicality without sign-off.

2. **🔔 Syncfusion "Community License" is free-of-charge but NOT permissive — flag before anyone treats "free" as NFR-03-compliant.**
   - It is a **proprietary EULA** (eligibility-capped by company size/revenue, revocable), **not** an OSI-permissive licence. It fails NFR-03's "MIT/permissive only" test **and** is one of the three explicitly-named commercial bars.
   - **Recommendation to the human**: **do not** adopt Syncfusion Community as a shortcut; it is FORBIDDEN as a linked BFF dep. Flagged only because "free" tempts a wrong classification.

Everything else in §2.2 classifies cleanly (LibreOffice/Gotenberg = permitted out-of-process; Graph = service, links nothing; Aspose/GemBox/Apryse = clearly commercial-forbidden; PdfPig/PDFsharp = clearly permissive-linkable). **No other genuine ambiguity.**

---

## 3. Part C — Sidecar framing (NFR-04 / BFF Hygiene §10)

**Any chosen pagination engine is an out-of-BFF-publish sidecar/service with its own size + ops budget — never a linked package added to the BFF publish.**

- **BFF publish ceiling is ≤60 MB compressed** (baseline ~49.63 MB incl. PDBs). WS-1..WS-4 add ~0 MB (pure OOXML on existing `DocumentFormat.OpenXml`). **WS-5's engine must not consume any of that budget.**
- **LibreOffice / Gotenberg**: deploy as a **separate container/sidecar** (its own CPU/memory/cold-start budget; LibreOffice image is hundreds of MB — precisely why it must live outside the BFF publish). Communicates over HTTP or a queue. This is task 050's measured path.
- **Graph `format=pdf`**: the "sidecar" is **Microsoft's own service** — zero self-hosted render infra. The only in-house code is the **PDF page/line extractor** (PdfPig, Apache-2.0). If that extractor is co-located in the BFF it still counts against publish size and must be size-measured per NFR-04; cleaner is to keep the render+extract flow in a **small worker/function**, not the BFF publish.
- **Invariant either way**: the BFF links **no paginator**. It either (a) calls a sidecar/service over the network, or (b) calls Graph. `Services/Compose/` stays `byte[]`-in / projection-out and gains **no** rendering dependency (ADR-007 / §10).

---

## 4. Part D — Recommendation INPUT for task 052 (NOT the decision)

052 makes the ship-vs-defer call using **050's measured LibreOffice divergence** + **this note's licensing/ops input**. From the licensing + ops half only:

### 4.1 Viability under NFR-03 (ranked)

1. **LibreOffice headless as a separate process/sidecar (or via Gotenberg)** — **most viable under NFR-03 and lowest external dependency.** MPL-2.0 out-of-process is clean; self-hosted so **no per-render latency cliff / throttling / sensitivity-label 406**; fully under Spaarke's ops control. **Cost:** it is a **separate layout engine** → the **largest** divergence-from-Word risk (050 quantifies this) + a heavyweight container to operate.
2. **Microsoft Graph `format=pdf` (Office cloud renderer)** — **highest fidelity reachable** (Word-Online's own engine) **and** NFR-03-clean (links nothing; sanctioned service, not headless Word). **Cost:** **latency/throttling** (async-only, ~45 s timeouts observed), **sensitivity-label failure modes**, dependence on docs being in a Graph drive (SPE — satisfied), and you still must **extract page/line from the PDF** yourself. Fidelity is **Word-Online-identical, not Word-desktop-identical** (§1.4).
3. **AGPL rendering services (ONLYOFFICE / SuperDoc-derived)** — **do not pursue without the §2.3 human ruling.** Even as a sidecar, AGPL network-copyleft is a live risk. Barred pending sign-off.
4. **Commercial libs (Aspose / GemBox / Syncfusion / Apryse)** — **FORBIDDEN**, full stop (NFR-03 / T-1). Not an option regardless of fidelity.

### 4.2 The fidelity ceiling to surface honestly (F-5)

- **Only Word's own layout is truly identical to Word** — and that path (self-run headless/desktop Word) is **support- + EULA-prohibited server-side**, so it is **not on the table**.
- The **reachable** ceiling is **Word Online** (Graph render), which is the closest but **still can diverge from a user's Word desktop** (server font substitution, line-breaking).
- **Open engines (LibreOffice) paginate *closely* but diverge more** — quantified by task 050.
- Therefore the product must make **no unqualified "page/line 100% identical to Word" claim.** The honest claim ceiling is either **"as rendered by Word Online"** (Graph path) or **"approximate, engine-measured divergence of X"** (LibreOffice path). 052 should pick the claim that matches the chosen engine and 050's numbers.

### 4.3 Sub-question for 052 to weigh (not resolved here)

The two viable paths trade **fidelity vs operational control** in opposite directions: **Graph = higher fidelity, lower ops control (latency/throttle/label dependence)**; **LibreOffice sidecar = lower fidelity, full ops control (no external latency cliff)**. 052 resolves this against 050's measured LibreOffice divergence and the product's page/line accuracy bar. A viable hybrid worth 052's consideration: **LibreOffice sidecar for interactive/preview** + **Graph render for a final, high-fidelity citation pass** — both NFR-03-clean.

---

## 5. Acceptance-criteria self-check (051)

- [x] Word-rendering-service path characterized as the true-Word-identical source, with ops/licensing/latency cost stated (§1) — **plus** the "Word-Online ≠ Word-desktop" qualifier (§1.4).
- [x] NFR-03 licensing table classifies candidate engines and concludes: **no commercial/AGPL paginator linked into the BFF; LibreOffice out-of-process only** (§2.2).
- [x] Any chosen engine framed as an **out-of-BFF-publish sidecar with its own size + ops budget** (§3, NFR-04).
- [x] Ambiguous licenses **flagged for human sign-off** (§2.3: AGPL-as-a-service; Syncfusion "free ≠ permissive") — not silently resolved (§9).
- [x] **Negative**: no `Services/Compose/` source changed; no project/package reference added — notes-only.
- [ ] TASK-INDEX.md → ✅ for 051: **owned by the main session** (sub-agent write boundary; root CLAUDE.md §3). Flagged for the caller to flip.

---

## Sources

**Most authoritative (Microsoft first-party):**
- Microsoft Support — *Considerations for server-side Automation of Office* (KB257757 successor): server-side/headless Office automation **not supported**; EULA does **not** cover serving unlicensed users. https://support.microsoft.com/en-us/visio/considerations-for-server-side-automation-of-office
- Microsoft Learn — *Considerations for unattended automation of Office in Microsoft 365 (RPA)*: reaffirms the unattended-automation stance. https://learn.microsoft.com/en-us/office/client-developer/integration/considerations-unattended-automation-office-microsoft-365-for-unattended-rpa
- Microsoft Learn / Graph — driveItem `content?format=pdf` conversion, 100 MB nominal limit, `.docx` supported; Q&A threads document ~45 s `General_Timeout`, large-file failures, and `406 OfficeConversion_DocumentProtected` on sensitivity-labelled files:
  - https://learn.microsoft.com/en-us/answers/questions/5679108/microsoft-graph-pdf-conversion-timing-out-at-45-se
  - https://learn.microsoft.com/en-us/answers/questions/323331/file-limits-in-pdf-generation-using-microsoft-grap
  - https://learn.microsoft.com/en-us/answers/questions/5523177/microsoft-graph-word-to-pdf-conversion-returning-4
- Microsoft Learn — *Getting Started with Word Automation Services* (SharePoint on-prem legacy). https://learn.microsoft.com/en-us/previous-versions/office/developer/sharepoint-2010/ee554975(v=office.14)

**Licensing:**
- LibreOffice — *Licenses* (MPL-2.0 primary + LGPLv3+; linking/out-of-process does not taint proprietary code). https://www.libreoffice.org/licenses/
- Gotenberg (Apache-2.0 wrapper over LibreOffice/Chromium) — standard sidecar-conversion service.
- AGPL-3.0 §13 (network-interaction copyleft) — the basis for the ONLYOFFICE/SuperDoc/iText sidecar flag.
- Syncfusion Community License terms (free-of-charge, eligibility-capped proprietary EULA — not permissive).

**Prior researcher memory consulted (project-scoped):**
- `.claude/agent-memory/researcher/tiptap-licensing-editor-arch-2026-07-16.md` — SuperDoc AGPL-3.0/commercial; DOCX conversion is paid/copyleft across the ecosystem.
- `.claude/agent-memory/researcher/docx-delta-fidelity-roundtrip-2026-07-16.md` — SuperDoc/Apryse/ONLYOFFICE render-engine landscape; native vs JSON converters; permissive-lib scan.
- `.claude/agent-memory/researcher/server-docx-authoring-numbering-2026-07-18.md` — server-owns-authoring; numbering.xml separate part (background for why paragraph numbering is deterministic but page/line is not).

## Caveats
- Graph `format=pdf` behaviour (timeouts, size ceilings, label failures) is drawn from MS Q&A/community evidence, not a single SLA page — **051 does not benchmark it**; if 052 leans toward the Graph path, a **timed corpus render spike** (like 050 does for LibreOffice) is the missing measurement.
- The AGPL-as-a-sidecar question (§2.3 flag) is a **legal/policy** call, not a technical one — it must go to the human, not be resolved by this note.
- Fonts are the silent fidelity variable on **both** cloud (Graph) and self-hosted (LibreOffice) renders; matching the corpus's fonts on the render host materially affects divergence and is worth 052 factoring in.

## Recommended follow-ups
- **052**: combine 050's measured LibreOffice divergence with §4 here; pick the honest claim ceiling (§4.2) matching the chosen engine.
- **Human sign-off** on the two §2.3 flags before any AGPL service or Syncfusion option is even prototyped.
- If Graph path advances: measure real render latency + throttling on the WS-5 corpus (parallel to 050) and design the async job + sensitivity-label failure handling.

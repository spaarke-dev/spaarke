# Spaarke Compose R8 — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-08-19
> **Source**: [`design.md`](design.md) (hardened by owner Q&A 2026-08-19, a Fable architectural review of the save path, and external research into browser-editor OOXML fidelity)
> **Governing ADR**: [ADR-049](../../.claude/adr/ADR-049-compose-shadow-document.md) — R8 files a **third Path-B amendment**
> **Prior evidence**: [`../spaarkeai-compose-r7/notes/uat-issues.md`](../spaarkeai-compose-r7/notes/uat-issues.md) (UAT-01…26 + the 2026-08-18 hidden-issue audit)

---

## Executive Summary

Compose is on its **eighth** release and still fails at two levels: users **cannot reliably save**, and the
saves that do land **silently destroy Word formatting**. R8 fixes both, in that order, and installs the
structural guards that stop a ninth attempt.

Save reliability is a set of client-contract, lifecycle and storage-boundary defects requiring **no
architecture decision** — it ships first, alone. Fidelity is an architecture change: stop reconstructing an
entire Word document from a five-node editor view, and instead **copy across the blocks the user never
touched**. Two adjacent defects ride along: AI edits are located by searching for the model's echoed prose
(they should carry a deterministic anchor), and uploaded chat files die after 24h while their conversation
lives for 90 days.

---

## Scope

### In Scope

- **Track S — Save reliability (P0, ships alone, first).** Ten verified failure modes + a save-outcome contract on the wire + telemetry.
- **Track A — Faithful save.** Server-side three-way merge: re-project the retained baseline at save time, clone unchanged blocks whole, property-inherit edited blocks, thin-render-with-warning as the per-block floor. Capability gate with "Edit a copy". Opaque-atom payload carry.
- **Gate — the fidelity harness contract.** Upgrade from "did not hard-fail" to **preservation + outcome honesty**, at two comparison levels, over an extended corpus.
- **Track C — AI edit placement (P1, "MUST be completely addressed").** Retire the R2-era whole-document text search; thread the anchor the client already captures.
- **Track B — Durable session files.** Uploaded files usable for the life of their session.
- **Track D — God-class removal.** All five Compose files below 2,000 lines; all waivers deleted.

### Out of Scope

- Making carried constructs **editable** (footnotes, fields, SDT, drawings are preserved, not made editable).
- **WOPI / embedded Word** — excluded by ADR-049 D4; forfeits the AI-native surface.
- **PDF export / save-as-PDF** (deferred; use Word).
- **Pagination / page-and-line fidelity** — remains R4.5's deferred WS-5.
- Rebuilding R7's honest-signal layer (banners, degradation copy, drop sinks, re-attach-on-reopen) — **reused**.
- Any change to AI dispatch, catalogs, or playbooks (ADR-039 — engine frozen).
- Templates (`spaarkeai-compose-templates-r8`) and R7's editor-UX surface.
- Force-unlock for stale Word co-authoring locks (deferred — owner did not hit it in UAT; revisit as Open-in-Word usage grows).

### Affected Areas

| Path | Role |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeService.cs` | `SaveAsync` fork, `ResolveSaveBaselineAsync`, staleness assert, re-anchor |
| `.../Services/Compose/ComposeDocumentRenderer.cs` | `RenderIntoCarrier` — the single body author; merge lands here |
| `.../Services/Compose/ComposeDocxProjectionBuilder.cs` | Capture side; the equality oracle's projector |
| `.../Services/Compose/ComposeContentModel.cs` | Write model — atom block kind + payload carry |
| `.../Services/Compose/ComposeBaselineParaIdStamper.cs` | Promote from op-log-path-only to the render path |
| `.../Services/Compose/{ComposeEditValidator,ComposeEditModels}.cs` | Track C retirement |
| `.../Services/Compose/CitationResolver.cs` | Track C — wire it (currently no BFF consumer) |
| `.../Services/Compose/ComposeShadowPatchEngine.cs` | Retires under Track A (confirm at gate) |
| `.../Api/ComposeEndpoints.cs` | Save routes, outcome contract, Track D |
| `.../Infrastructure/Graph/UploadSessionManager.cs` | 4 MB guard, `If-Match` overload |
| `.../Services/Ai/{Sessions,Chat,LinearConsumers}/**` | Track B |
| `src/client/shared/Spaarke.Compose.Components/src/**` | Save error handling, dirty-flag lifecycle, anchor threading, capability-gate UI |
| `src/client/shared/Spaarke.Auth/src/authenticatedFetch.ts` | The `ApiError` contract client error handling must be rebuilt on |
| `tests/integration/seam/Compose/**`, `tests/fixtures/compose-corpus/**` | The gate |
| `tests/Spaarke.ArchTests/GodClassGuardTests.cs` | Track D waiver deletions |

---

## Requirements

### Track S — Save Reliability (P0)

| ID | Requirement | Acceptance |
|---|---|---|
| **FR-S01** | Client save error handling routes on `ApiError.status`; the unreachable `if (!response.ok)` block is retired | Each status (423/412/403/4xx/5xx) renders its own message + recovery; tests drive the **thrown** `ApiError` path, not a mocked `{ok:false}` |
| **FR-S02** | Concurrency = **last-writer-wins with a user-visible warning**, enforced by `If-Match` at the storage boundary. Supersedes the 412 refusal shipped 2026-08-18 | A concurrent-writer save **succeeds** and warns, naming version history as recovery. No unrecoverable refusal loop exists |
| **FR-S03** | The born-in-editor dirty flag is not cleared until the POST succeeds | A failed born-in-editor save leaves Save enabled, Ctrl+S live, `beforeunload` armed, unmount flush armed, and the toolbar **not** showing "Saved" |
| **FR-S04** | 423 (Word co-authoring lock) renders a clear message with a working Retry | Lock → named banner + Retry; Retry succeeds once the lock clears |
| **FR-S05** | The save request has a timeout, an `AbortSignal`, and an in-flight guard | A hung save cannot leave `status === 'saving'` permanently; the editor recovers without a page reload |
| **FR-S06** | A closed **save-outcome enum** crosses the wire on `SaveComposeDocumentResponse` | `persisted` / `persisted-with-warnings` / `refused-stale` / `refused-locked` / `refused-invalid` / `storage-failed` / `partially-recorded`; a failed write can never present as HTTP 200 "Saved" |
| **FR-S07** | A re-anchor path that fails to re-download current bytes MUST NOT persist the load-time baseline | No save can overwrite a newer version with pre-edit content; the failure surfaces as a defined outcome |
| **FR-S08** | Compose routes to the existing chunked-upload path; the ~22 MB request-body ceiling is removed; oversize is an honest pre-flight message | A ≥4 MB first save succeeds; a ≥30 MB document saves or is refused with a stated limit — never a raw 400/413 |
| **FR-S09** | The honest-failure set is closed: silent guard drops, name-modal gate, container/drive/**tenant** preconditions, checkout force-close (same dead-`!response.ok` bug), promote-after-write faults, Graph 429 mapping, `sprk_filesize`/`sprk_filepath` refresh on replace saves, per-document local draft slot | Each has a user-visible outcome and a test; `canSaveNow` validates `tenantId` |
| **FR-S10** | Save-outcome telemetry, per the `cosmos.write_failures` precedent | Every terminal outcome emits a metric with cause; a save-failure spike is visible without owner UAT |

### Track A — Faithful Save

| ID | Requirement | Acceptance |
|---|---|---|
| **FR-A01** | `ComposeBaselineParaIdStamper` runs on the **render path**, not only the op-log path | A baseline with no `w14:paraId` is stamped before comparison; fill-gaps-only, fail-open, text-verified behavior preserved |
| **FR-A02** | "Unchanged" is decided by comparing the posted block against a **fresh server-side re-projection of the baseline** — never raw text, never client trust | Deterministic and unfakeable; a paragraph containing tabs/breaks/fields compares correctly |
| **FR-A03** | Unchanged blocks are **cloned whole** from the baseline `w:p` subtree | Tabs, soft breaks, footnote refs, field results, `w:sym`, interior `sectPr` survive with zero property logic |
| **FR-A04** | Edited blocks render from the model with **property inheritance** from the base block (pPr clone + dominant rPr) | An edit inside a formatted run does not collapse the paragraph to Normal |
| **FR-A05** | Opaque atoms are promoted into the **write** model with their verbatim XML payload | Fields, SDT, drawings, footnote refs round-trip as whole constructs; the existing `composeBlockAtom`/`composeInlineAtom` nodes are extended, not replaced |
| **FR-A06** | Tables and atoms carry an identity | `ComposeBlock.Table` keyed by its first descendant cell paraId; atoms by server-minted `AtomId` |
| **FR-A07** | **Capability gate = read-only + "Edit a copy"** | A document we cannot safely carry opens read-only stating **what** cannot be carried, and offers "Edit a copy": the user is told what the copy will drop and confirms; the fork is a new SPE item with a uniquified filename stamped `ComposeOrigin.Authored`; **the original is never written to**. Trigger list owner-reviewed at Phase 0 |
| **FR-A08** | Two document classes are honored: `Imported` preserves against an original; `Authored` (born-in-editor, PDF-sourced, forked copies) has none | Degradation warnings **do not fire** for `Authored` documents — there is no original to lose against |
| **FR-A09** | PDF-sourced documents track their synthesized file's version coordinates | After a page refresh, save two of a PDF-sourced document resolves its baseline and clones |
| **FR-A10** | The residual loss list is **published and owner-accepted**, and matches what the gate enforces | Documented in `docs/architecture/`; no construct degrades that is not on the list |
| **FR-A11** | Cloning is correct for comments and revisions | Model-side anchors suppressed for cloned blocks; cross-boundary comment ranges validated; revision-id seeding accounts for cloned `w:ins`/`w:del`; duplicate paraIds handled by consume-in-document-order + dup-detected→fallback |
| **FR-A12** | `If-Match` is sent on the content PUT | Concurrency enforced at the storage boundary, closing the check-then-act TOCTOU window |

### Gate — Fidelity Harness Contract

| ID | Requirement | Acceptance |
|---|---|---|
| **FR-G01** | **Preservation oracle**: after a save editing one paragraph, every other block is XML-equivalent to its original | **100% near tier** (character formatting, paragraph properties, indentation, tabs, footnote refs, fields); **≥95% overall**; asserted in CI |
| **FR-G02** | **Outcome honesty**: every corpus save terminates in a defined outcome and the response reports exactly what persisted | No undefined content-refusal; asserted alongside preservation |
| **FR-G03** | **Two comparison levels** — lenient (ignores `paraId`/`textId`, detects content loss) and strict (does not, detects identity drift) | Normalizes `w:rsid*`, `w:proofErr`, bookmark ids, `numId`/`abstractNumId` remapping, attribute order, namespace prefixes |
| **FR-G04** | Corpus extended with the three constructs that broke R4 plus near-tier documents | `mc:AlternateContent` duplicate paraIds, interior text boxes, multi-part paraId collisions (synthetic, cheap); character formatting, court-filing spacing, footnotes, `REF` cross-refs, content controls (owner-supplied) |
| **FR-G05** | Word-repair check | Approximated in CI by headless LibreOffice; periodic Word Online smoke. `OpenXmlValidator` alone is explicitly insufficient |
| **FR-G06** | Heavy-restructure case measured | Section reordering / large cut-paste degrades gracefully (more blocks rebuilt + warned), never fails |
| **FR-G07** | N-cycle Word round trip asserts **no compounding drift** | A document through Word ⇄ Compose N times does not degrade cumulatively |
| **FR-G08** | New corpus documents land in the gate with **zero code changes** | The dynamic `[MemberData]` + `ComposeCorpusFixtureLocator` enumeration is preserved |

### Track C — AI Edit Placement (P1)

| ID | Requirement | Acceptance |
|---|---|---|
| **FR-C01** | The anchor the client already captures (`from`/`to` at invocation) is **threaded through request → response → apply** | Selection-driven edits place at their anchor with **zero** text matching |
| **FR-C02** | `CitationResolver` is wired for reference-driven targets ("clause 4.2") | Deterministic resolution from the numbering engine; no search |
| **FR-C03** | For model-initiated review passes, the model returns a `paraId` from an **enumerated closed set we supplied**, validated on return | An invalid id is rejected loudly, never guessed |
| **FR-C04** | The text-search placement path is **retired** | Server: `ComposeEditValidator` + `IComposeEditValidator` + `FindAll`, and `ProposedEdit.TargetText`/`match_mode`. Client: `resolveTargetSpans`, `findTargetMatches`, `MATCH_FOLD`, `collapseWhitespaceIndex`, `buildCharIndex`. **KEEP** `ComposeTextFold` (used by the stamper) and `AnnotationReanchorService` (the ADR-sanctioned return-from-Word case) |
| **FR-C05** | Every outcome is deterministic and explainable | Sub-paragraph edits → local diff within the known paragraph. Stale target → "this clause changed since the suggestion — apply anyway?" Deleted target → "the text this suggestion referred to no longer exists" |
| **FR-C06** | Tolerant matching survives **only** as a bounded, confirmable fallback for replayed/legacy edits | Low-confidence → a **proposed** placement the user confirms; never auto-apply. No regression vs UAT-21 |
| **FR-C07** | **"Wording differs slightly" is eliminated** from the AI edit flow | The string does not exist as a reachable state; return-from-Word re-anchor messaging is specific ("this document was edited in Word — here is what re-attached") |
| **FR-C08** | **One edit-capture mechanism** | An AI edit's anchor is captured at invocation and rebased by the same machinery the op-log uses (`RebasedOperationLog` / `stepOperationInterceptor`) |

### Track B — Durable Session Files

| ID | Requirement | Acceptance |
|---|---|---|
| **FR-B01** | A durable byte copy of each session upload is persisted at upload time, tenant-partitioned | Stored in **blob** (existing provisioned account + managed-identity RBAC); no new Azure resource, no new NuGet |
| **FR-B02** | Evicted chunks are **lazily re-indexed** from the durable copy on recall | A day-60 session recalls from its files identically to day 1 |
| **FR-B03** | `SessionFilesCleanupJob` evicts the **hot index only** | The durable copy is never evicted by the 24h Redis-key sweep |
| **FR-B04** | Durable lifetime follows the session's own retention | 90-day container default for unfiled; **indefinite for filed** (`StoredSession.Ttl == -1`) |
| **FR-B05** | Availability is **server-authoritative** | R7's client-side 24h heuristic (`AttachedFileSummary.available`) is replaced or removed; R7's re-attach layer is **reused, not rebuilt** |
| **FR-B06** | Session deletion and GDPR erasure delete the bytes | Mirrors `memory-items` erasability; ADR-014/015 tenant isolation on every store path |

### Track D — God-Class Removal

| ID | Requirement | Acceptance |
|---|---|---|
| **FR-D01** | All five Compose files drop below 2,000 lines and **all five waivers are deleted** from `GodClassGuardTests.cs` | `ComposeService` (3,573), `ComposeDocxProjectionBuilder` (3,085), `ComposeShadowPatchEngine` (2,999), `ComposeEndpoints` (2,651), `ComposeDocumentRenderer` (2,304) |
| **FR-D02** | `ComposeShadowPatchEngine` retires entirely rather than being decomposed | Confirmed at the Phase-0 gate **before** deletion; its EDGE-wisdom is already migrated |
| **FR-D03** | No new feature lands in the save path until the write-model gate is green | Freeze rule enforced at review |

### Non-Functional Requirements

- **NFR-01** — Publish size ≤60 MB compressed; report absolute + delta vs the **44.96 MB** net10 baseline (incl. PDBs) on every BFF-touching task.
- **NFR-02** — **No new NuGet on Track A** (pure `DocumentFormat.OpenXml`). Track B adds none (`Azure.Storage.Blobs` 12.29.1 already referenced).
- **NFR-03** — No new HIGH-severity CVE (`dotnet list package --vulnerable --include-transitive`).
- **NFR-04** — ADR-010 DI budget respected: decomposition produces **internal collaborators**, not new DI registrations.
- **NFR-05** — Deploy BFF and `sprk_spaarkeai` **together**; never build from a net8 tree; `/conflict-check` before BFF PRs.
- **NFR-06** — ADR-038 seam-first. The gate lives in `tests/integration/seam/**` (KEEP path). No `Mock<HttpMessageHandler>`, DI-registration, or ctor-null tests.
- **NFR-07** — Large-document performance: the merge adds no worse than one extra baseline projection + DOM clone per save (the carrier is already fully buffered and re-opened several times).
- **NFR-08** — **No save-path status code merges without a paired client-recovery test.** (The 412 shipped the same day its handler was dead code.)
- **NFR-09** — **NEVER delete `docxBridge.ts`.**

---

## Technical Constraints

### Applicable ADRs

| ADR | Relevance |
|---|---|
| **ADR-049** | Governing — Compose shadow document; R8 amends (see ADR Tensions) |
| **ADR-007** | `SpeFileStore` facade — no Graph types above it; engine stays `byte[]`-in/out |
| **ADR-009** | Version/re-anchor state via `IDistributedCache`, never `IMemoryCache` |
| **ADR-010** | DI minimalism — ≤15 non-framework registrations |
| **ADR-013** | AI facade — no AI internals in `Services/Compose/`; `PublicContracts` discipline |
| **ADR-014 / ADR-015** | Tenant isolation — binding on Track B's new store path |
| **ADR-021 / ADR-050** | Fluent v9 + `SprkModal` for capability-gate and fork-confirmation UI; semantic tokens only |
| **ADR-029** | BFF publish hygiene — size ratchet |
| **ADR-032** | Null-object kill-switch if any new service is feature-gated (symmetric registration) |
| **ADR-038** | Testing strategy — integration-heavy; seam KEEP paths |
| **ADR-039 / ADR-040** | AI engine frozen; session ledger — Track C's anchor is envelope-only |

### MUST Rules

- ✅ **MUST** keep exactly **one body author** (`ComposeDocumentRenderer`) — ADR-049 D5/I-5 finally holds literally when the patch engine retires.
- ✅ **MUST** treat the **projection as the only coordinate system**; nothing else independently resolves document positions.
- ✅ **MUST** carry deterministic information available at capture time — never re-derive it.
- ✅ **MUST** terminate every save in a defined outcome and report exactly what persisted.
- ✅ **MUST** preserve untouched blocks.
- ❌ **MUST NOT** reintroduce save-time anchor reconciliation or any HTTP 422 content-refusal.
- ❌ **MUST NOT** use text-search as a placement mechanism (ADR-049 I-7).
- ❌ **MUST NOT** treat `paraId` as a durable **file** key (spec-legal duplicates in `mc:AlternateContent`; part-scoped; Word regenerates on save). It **is** authoritative **within a live session** — we mint it.
- ❌ **MUST NOT** walk `body.Descendants<Paragraph>()` — descend direct `w:body` children and treat `w:txbxContent` / `mc:Choice` / `mc:Fallback` as opaque.
- ❌ **MUST NOT** author `.docx` bytes on the client (ADR-049 I-2).
- ❌ **MUST NOT** add commercial / per-seat / AGPL components (NFR-03 of ADR-049). *Clippit (MIT) is permissible if `WmlComparer` proves useful.*

### Existing Patterns to Follow

`ComposeFormatChange.PreviousPropertiesXml` — the proven opaque-XML carry with an SDK parse gate ·
`ComposeBlockAtom` / `opaqueAtomNode.ts` — shipped ProseMirror `atom: true` placeholder semantics ·
`ComposeFidelityGateHarnessTests` — dynamic corpus enumeration · R7's `SAVE_DEGRADATION_COPY` + banner stack ·
`SpeAdminGraphService`'s chunked-upload routing · `ComposeOrigin` Authored/Imported discriminator.

---

## Placement & New Components (per CLAUDE.md §10 / §11)

### Hot-Path Declaration

```xml
<hot-path-declaration>
  <bff>Y</bff>                   <!-- Services/Compose write model + save path; Services/Ai/Sessions + Chat for durable files -->
  <spaarkeai>Y</spaarkeai>       <!-- Compose save error handling, outcome banners, capability-gate + fork UI -->
  <ci-workflows>Y</ci-workflows> <!-- fidelity gate contract changes (preservation + outcome honesty) -->
  <skill-directives>N</skill-directives>
  <root-claude-md>Y</root-claude-md> <!-- §17 Compose pointer + god-class ratchet row -->
</hot-path-declaration>
```

**Placement Justification** — Tracks S/A/C/D stay in `Services/Compose/` + `Api/ComposeEndpoints.cs`, extending
and **decomposing** existing components. No new subsystem, no new package. Track B is the one new BFF surface.
≤60 MB publish check applies per BFF-touching task.

### New Components (§11 three-question gate)

| New component | Existing overlap | Can extend instead? | Cost-of-doing-nothing |
|---|---|---|---|
| Durable session-file store | None — `Azure.Storage.Blobs` referenced but its only consumer (`UploadFinalizationWorker.cs:610`) is a **stub** (GH #231); `SpeFileStore` is matter/BU-scoped DMS, wrong lifecycle for chat scratch | No — SPE would pollute the DMS with per-user ephemeral uploads and inherit its permission/retention model | A conversation reopened after 24h cannot recall from its own uploaded files; the manifest points at chunks that no longer exist |
| Save-outcome contract (enum + response field) | `SaveComposeDocumentResponse` has **no** completion field; outcomes exist only as scattered ProblemDetails the client cannot receive | Extends the existing DTO — not a new type surface | A total write failure presents as HTTP 200 "Saved ✓" (verified, FR-S06) |
| Preservation oracle | `ComposeFidelityGateHarnessTests` exists but **explicitly does not assert byte-identity** | Yes — extends the existing harness, fixture locator and corpus | Fidelity regressions ship invisibly, exactly as in R6 |
| Block-merge component (Track A) | `RenderIntoCarrier` re-authors wholesale | Extends the renderer; required as a **separate file** by Track D's ratchet obligation | Every save reconstructs the whole document from a five-node view |

All other work is **modify-only** or **deletion**.

---

## ADR Tensions (per CLAUDE.md §6.5 — MANDATORY)

| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| **ADR-049** | R6 Path-B amendment ("render-on-save supersedes surgical byte-patch on the save path") + R4 **I-4** ("untouched XML subtrees byte-identical") | R8 restores what I-4 protected **without** restoring the mechanism R6 removed. The save still renders from the model (R6 holds) *and* untouched content is preserved (I-4's intent holds). Both amendments are partly right; neither is correct as written | **B — third amendment**, filed **at the Phase-0 gate** (not before), merged with or before dependent code | Codifies: (a) render-on-save is fidelity-preserving by base re-projection + block copy-through; (b) **two standing MUSTs** no future amendment may trade away singly — *every save terminates in a defined outcome* and *untouched blocks are preserved*; (c) **the projection is the only coordinate system**; (d) paraId is a hint in the **file**, authoritative within a **session**; (e) concurrency = last-writer-wins + warning; (f) **one edit-capture mechanism**; (g) **deterministic information available at capture time MUST be carried, not re-derived** |
| **ADR-010** | ≤15 non-framework DI registrations | Track D decomposes ~14,600 lines across five files; naive per-concern services would blow the budget. Track B adds one store service | **C — comply** | Decomposition produces **internal collaborators / partial classes**, not DI registrations. Track B's single registration is symmetric (ADR-032 if gated) |
| **ADR-039** | Engine frozen; no new dispatch; closed catalogs | Track C adds `(paraId, span)` to the AI edit envelope | **C — comply** | Envelope-only — no new dispatch, no catalog row. ADR-049 **already specifies** paraId-referencing operations; this aligns the implementation with the ADR rather than deviating from it |
| **ADR-029** | Publish-size ratchet | Track A adds substantial logic | **C — comply** | No new package; report absolute + delta per task |
| **ADR-038** | Ban on DI-registration / ctor-null tests; seam-first | Track D restructuring invites structural tests | **C — comply** | Gate + merge coverage live in `tests/integration/seam/**` |

*(g) is the general rule beneath three of this project's four root causes — R6's thin model, the AI edit
contract, and the demand for a fuzzy matcher. Stated once in the ADR so it stops being rediscovered per surface.*

---

## Success Criteria

1. [ ] **Save works.** Every §Track-S failure mode closed — Verify: each has a seam/contract test driving the real `ApiError` path; owner UAT on dev after the standalone Track S deploy.
2. [ ] **No lying.** No HTTP 200 with nothing written; no "Saved ✓" on a failed write; no silent skip — Verify: FR-G02 outcome-honesty assertion in the gate.
3. [ ] **Preservation** — 100% near tier, ≥95% overall — Verify: FR-G01 in CI at two comparison levels.
4. [ ] **Zero hard-fails** across the corpus, as an asserted invariant — Verify: gate classification, no 422/5xx.
5. [ ] **Zero silent loss**; residual list published and matching the gate — Verify: FR-A10 doc + gate parity check.
6. [ ] Footnotes, fields, content controls, complex objects round-trip whole **or** are on the accepted residual list; an uncarryable document opens read-only **with a working "Edit a copy"** — Verify: corpus + UAT.
7. [ ] Concurrency is last-writer-wins with a warning, enforced by `If-Match` — Verify: seam test with a concurrent external writer.
8. [ ] A session reopened at any point in its retention recalls from its files; availability is server-authoritative; deletion deletes the bytes — Verify: Track B seam tests + a day-60 simulation.
9. [ ] **"Wording differs slightly" is eliminated**; an AI edit places at its anchor or surfaces a confirmable proposal; no mis-placement regression vs UAT-21 — Verify: Track C tests + string absence from the reachable code path.
10. [ ] **All five Compose waivers deleted** from `GodClassGuardTests.cs` — Verify: ArchTests green with the entries removed.
11. [ ] Publish ≤60 MB (absolute + delta vs 44.96 MB); no new HIGH CVE; no new NuGet on Track A; justifications recorded; `/conflict-check` clean — Verify: per-task reporting.

---

## Dependencies

### Prerequisites

1. **Sync the worktree** — `work/spaarkeai-compose-r8` is **19 behind `origin/master`, 0 ahead**.
2. **Commit** `design.md` + the `README.md` portfolio pointer.
3. **R7 is fully merged — confirmed** (`origin/work/spaarkeai-compose-r7` is contained in `origin/master`; zero commits outside). No merge action required.

### External

- **Owner-supplied worst-offender corpus documents** (Phase-0 dependency). Corteva NDA cleared; harder cases to be evaluated as they surface.
- Owner review of the **capability-gate trigger list** and the **residual loss list** at Phase 0.

---

## Owner Clarifications

| Topic | Question | Answer | Impact |
|---|---|---|---|
| Track S delivery | How should the save fixes ship? | **Alone, immediately** — own PR + dev deploy ahead of everything | Track S is a standalone phase with its own deploy gate; Phase 0 runs in parallel |
| Capability gate | What happens with a document we cannot safely carry? | **Read-only with a reason, then let the user choose to open/edit and save as a NEW document** | FR-A07 — supersedes the plain read-only gate; original never written to, user never blocked |
| Track D depth | How deep should god-class removal go? | **All five files under 2,000 lines, all waivers deleted** | FR-D01 — a full committed deliverable, not opportunistic |
| Branch base | Where does R8 branch from? | R7 reported merged — **verified true** | Branch from master; sync required |
| Track C scope | Own project or R8? | **Stays in R8**; no new Compose project | Track C is P1, runs parallel to Track A |
| Track C bar | How completely? | **"MUST be completely addressed"** | FR-C07 — elimination, not reduction |
| File storage | Cosmos or blob? | **Blob** — Cosmos holds JSON documents, not bytes (verified: 7 containers) | FR-B01; infrastructure already provisioned |
| Concurrency | Refuse or overwrite? | **Last-writer-wins with warning** | FR-S02 — supersedes the 412 refusal |
| Fidelity bar | Needed? | **Yes** | FR-G01 |
| Corpus | Cleared? | **Yes for now**; harder cases to be evaluated | External dependency |
| PDF documents | Are they "our file"? | **Yes** — no original OOXML to preserve | FR-A08 — two document classes; warnings suppressed for `Authored` |
| Word lock | 30-minute stale lock a concern? | Not hit in UAT | Force-unlock deferred; out of scope |

---

## Assumptions

- **Gate thresholds** — assuming 100% near tier / ≥95% overall. Owner confirmed a bar is needed but not the numbers; **revisit at Phase 0 when the control measurement exists**.
- **Track C envelope** — assuming the `(paraId, span)` addition qualifies as envelope-only under ADR-039 (Path C). If review disagrees, escalates to a Path-A exception.
- **Patch-engine retirement** — assuming the merge model fully subsumes `ComposeShadowPatchEngine`, including clean-apply for reopened authored documents. Confirmed at the Phase-0 gate **before** deletion.
- **Track D sizing** — assuming decomposition can be interleaved with Tracks S/A rather than run as a separate freeze.

---

## Unresolved Questions

- [ ] **Additional corpus documents** — which worst-offenders can the owner supply? *Blocks: Phase-0 gate strength (not spec authoring).*
- [ ] **Capability-gate trigger list** — which constructs force read-only? *Blocks: FR-A07 acceptance. A false positive blocks a document we could have handled — the main risk this feature carries.*
- [ ] **Residual loss list sign-off** — who accepts it and when? *Blocks: FR-A10, success criterion 5.*
- [ ] **Gate threshold confirmation** after Phase 0 measures the control. *Blocks: FR-G01 final numbers.*

---

*AI-optimized specification. Original design: [`design.md`](design.md).*

# 041 — Attachment-Locator Escalation (ESCALATION FIRED — awaiting owner decision)

> **Task**: 041 (P4, FULL, opus/high) — attachment-grounded action extraction (FR-13).
> **Trigger**: the POML `<escalation>` — "If the existing attachment text-extraction pipeline does not
> expose extracted text at the granularity needed to produce a precise attachment locator (so citation
> fidelity cannot be met without a new extractor), STOP and surface per root §6 / §6.5 rather than building
> a parallel extraction pipeline."
> **Status**: FIRED 2026-07-29. Escalation surfaced; awaiting owner path choice (A / B / C).

---

## The finding (verified against code, not just sub-agent report)

The existing attachment text-extraction pipeline exposes attachment text as a **single flat blob with no
positional or per-attachment structure**:

- [`NormalizedMessage.AttachmentText`](../../../src/server/api/Sprk.Bff.Api/Services/Communication/Models/NormalizedMessage.cs) **L69** — one `string?`,
  concatenated across ALL attachments (`\n`-joined) at the inbound boundary (`IncomingCommunicationProcessor.AddAttachmentTextAsync`).
- `TextExtractionResult.Text` — flat `string?`. Azure Document Intelligence **does** return `Pages`/`Lines`
  (page + line structure), but `ExtractViaDocIntelCoreAsync` **flattens and discards** it before returning.
  Redis caches the flat string; no structure survives.
- **No** `sprk_document` extracted-text column is persisted — the child `sprk_document` rows carry SPE
  pointers (`sprk_graphitemid`/`sprk_graphdriveid`) only; text is re-extracted on demand.
- [`CitationVerifier`](../../../src/server/api/Sprk.Bff.Api/Services/Communication/EmailProposalShaping.cs) **L131-149** —
  `BuildSourceText` MERGES subject+body+attachment into one searchable string; `IsCitedTextPresent` is a
  substring `Contains`. It cannot distinguish "quote is in the attachment" from "quote is in the body," and
  retains no offset.

**Consequence**: a *machine-verified* precise attachment locator (which file → which page → which offset)
cannot be produced without either modifying the shipped extractor to retain per-attachment (and optionally
page) structure, or adding a new structured extraction/persistence layer. The current flat blob supports
**verifying that a quoted span exists in the attachment text**, but not **pinning it to a machine-checked
position**.

---

## Three resolution paths (§6.5)

### Path A — Project-scoped exception: ship the shipped-contract locator bar (RECOMMENDED)
Adopt the *shipped NFR-06 contract* as the locator bar. **No extractor change.**
- Cite the extracted action with `Source="attachment"`, a **model-emitted human-readable `Locator`**
  (attachment filename + short anchor phrase; grounded by passing `message.Attachments` filenames into the
  Action prompt), and the verbatim `QuotedText`.
- **Refined NFR-06 gate** (the substantive addition): verify `QuotedText` exists specifically in
  `message.AttachmentText` **alone** — NOT the merged subject+body+attachment blob. This is what actually
  enforces "action stated ONLY in the attachment" and "cited TO the attachment," and it needs no new extractor
  (the field already exists).
- **Meets** every FR-13 acceptance criterion as literally worded (cited to attachment; system verifies the
  cited text exists in the extracted attachment text; feeds Job C create; deadline→confirm; best-effort; eval).
- **Honest limitation**: the *quote* is machine-verified; the *locator string* (which file / where in it) is
  model-asserted + prompt-grounded, not byte-verified. Blast radius: none on shipped extraction; additive
  Action + facade + gate + eval only.

### Path B — Extend the existing extractor for machine-verified per-attachment (± page) locators
Modify `AddAttachmentTextAsync` + `NormalizedMessage` to retain **per-attachment** extracted text (a
`(attachmentId/filename → text)` map), and optionally retain the Doc Intelligence `Pages` structure that
`ExtractViaDocIntelCoreAsync` currently throws away. Gives real, machine-verifiable per-attachment (and
optionally per-page) locators.
- **Not strictly a "parallel pipeline"** — it extends the existing extractor, so it doesn't literally trip
  the trigger's prohibition. But it **touches the shipped r4 inbound hot path** (`IncomingCommunicationProcessor`,
  `NormalizedMessage`, every rung that reads `AttachmentText`), so it needs characterization tests to keep r4
  flows green. Higher fidelity, higher blast radius + risk, larger task.

### Path C — Pivot / defer
Ship Path A now; file a follow-up issue for machine-verified per-attachment/page locators if a consumer ever
needs byte-precise positions. (Path A **is** the pivot-to-comply; C just formalizes the deferral as a tracked issue.)

---

## Recommendation
**Path A.** It satisfies FR-13's literal acceptance criteria, honors the trigger's explicit anti-scope-creep
intent ("rather than building a parallel extraction pipeline"), keeps the shipped r4 extractor untouched
(NFR-04 capture-path safety), and matches the owner's established reuse-first scoping (031 Option 2, 051b
descope). The one real gain worth taking from B — telling WHICH attachment — is delivered at the honest
"prompt-grounded + quote-verified" level, not the byte level. If byte-precise page/offset locators are a hard
requirement for r5's confirm surface, choose B and accept the r4-hot-path blast radius.

---

## Decision — Path B (owner, 2026-07-29)

**Owner chose Path B**: machine-verified per-attachment (±page) locator. §6.5 resolution = documented
exception with owner approval (rationale: byte-verifiable "cited to THE attachment + page" is the FR-13
differentiator; the reuse-only Path A locator was judged insufficient). This is a scope expansion of the
reuse-only task framing — approved at the point of decision, cited here + in the PR.

### Path B design (as built)
Extend the existing extractor + envelope to **retain what is currently discarded** — this is an *extension of
the existing extractor*, NOT a new/parallel extraction pipeline (the trigger's actual prohibition):

1. `Models/Ai/TextExtractionResult.cs` — add `IReadOnlyList<ExtractedPage>? Pages` (`ExtractedPage {int PageNumber, string Text}`);
   populate in `ExtractViaDocIntelCoreAsync` (retain Doc Intel `Pages`/`Lines` currently flattened);
   single-page fallback `[{1, Text}]` for Native/Email paths. Extend the Redis cache payload to carry pages
   (so cache hits keep structure).
2. `Services/Communication/Models/NormalizedMessage.cs` — add
   `IReadOnlyList<AttachmentExtractedText> AttachmentTexts` (`{string FileName, Guid? DocumentId, IReadOnlyList<ExtractedPage> Pages}`).
   **Keep `AttachmentText` (flat string) — DERIVED from `AttachmentTexts` (concat all pages of all attachments)
   — so every existing consumer (rungs, `CitationVerifier.BuildSourceText`) is byte-unchanged.** ← r4-safety.
3. `IncomingCommunicationProcessor.AddAttachmentTextAsync` — build the per-attachment structured list (filename
   + resolved child `sprk_document` id when available + pages) AND the flat concat from the SAME extraction;
   set both on the envelope. Best-effort/non-fatal preserved.
4. **REUSE 040's `create-task-from-email` Action + `ICommunicationCreateTaskAi` facade** (no new Action, no new
   facade — §11 reuse-maximal). New best-effort enrichment step `email-attachment-action` invokes the facade
   **PER FLAGGED ATTACHMENT**, passing ONLY that attachment's text (`Subject=""`, `BodyText=""`) so the model
   structurally can only cite the attachment ("action stated ONLY in an attachment"). A deterministic
   action-trigger **pre-filter** (cost gate — keyword scan) flags which attachments get an LLM pass.
5. **Machine-verified locator gate (new, code-derived — stronger than model-asserted)**: locate
   `candidate.Citation.QuotedText` in THIS attachment's page-structured text; (a) if not present in the
   attachment's `FullText` → DROP (attachment-scoped NFR-06, strictly stronger than the shipped merged-blob
   check); (b) DERIVE the page number = the page whose text contains the verbatim quote (null if page-spanning
   — still attachment-verified). The model never asserts the page; code proves it → byte-verified.
6. Feed Job C create via `IActionSeam.CreateTaskAsync` + 040's deadline→confirm branch + append-only
   Proposed/Applied audit rows, with a DISTINCT sentinel prefix `__attach_action__:` and `kind="attachment-action"`,
   the citation carrying the machine-verified `{attachment fileName, documentId, page}`. Best-effort (NFR-04).
7. **Characterization tests FIRST** (project CLAUDE.md binding): prove flat `AttachmentText` byte-unchanged for
   rungs + `CitationVerifier`. Then FR-13 tests + heaviest eval (action ONLY in an attachment, cited to that
   attachment+page).

### r4-hot-path blast radius (Path B accepted cost)
Touches `IncomingCommunicationProcessor` (inbound), `NormalizedMessage` (envelope), `TextExtractorService` +
`TextExtractionResult` (extractor + cache). Mitigation: `AttachmentText` stays derived + byte-identical;
characterization tests gate the change; `/conflict-check` clean (no concurrent owner on `Services/Communication/`).

### Implementation outcome (2026-07-29) — SHIPPED under Path B
Built as designed. 6 source files (additive), 2 test files + 1 eval seed. BFF builds clean (0 err); 12 new
FR-13 tests pass (5 seam + 7 eval); existing Communication suite green except the 5 documented pre-existing
sender-identity/DTO failures (disjoint from these changes — `notes/wave2-review-findings.md`). Publish-size
46.24 MB compressed incl PDBs (Δ≈0 vs ~49.63 baseline, no packages); no new HIGH CVE (only the pre-existing
`System.Security.Cryptography.Xml` branch debt). **Step 9.5: code-review SHIP-WITH-FIXES (no Critical),
adr-check CLEAN (ADR-039/013/015/045/010/032 all compliant).**

### Known limitation W1 (§6.5 Path A — documented, accepted) — cross-job duplicate extraction
The 040 `email-create-task` step grounds on the MERGED `subject+body+attachmentText`, so with a real model it
CAN also extract an action that lives in an attachment — the same case 041 targets. Both steps run in the same
`EnrichAsync`, and their idempotency sentinels differ (`__create_task__:hash(subject)` vs
`__attach_action__:hash(file+subject)`), so a single attachment-stated action can yield TWO tasks (one per
step). Not data corruption — both are cited + audited, and deadline-bearing ones are PENDING (human-confirm),
so a reviewer sees both and dismisses one. **Path A rationale**: the proper fix lives OUTSIDE 041's reuse-only
boundary — either narrow 040's grounding to exclude attachment text (changes 040's shipped behavior) or dedupe
at r5's confirm surface (r5 scope). Both are out of scope for 041. **Follow-up**: file an issue to scope 040's
grounding to non-attachment (so 041 solely owns attachment extraction) OR add cross-`kind` dedup at the r5
confirm surface. Recorded here + to be cited in the PR description.

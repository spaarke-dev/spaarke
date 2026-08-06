# R1 Golden Misfile Emails — pinned regression fixtures for FR-D3

> **Task**: 003 (R1 close-out — reconcile task 013 + pin golden fixtures) · **Date**: 2026-08-05
> **Rigor**: STANDARD · **Status**: ✅ complete
> **Purpose**: Pin the R1 UAT golden misfile email + expected engine outcomes so the **FR-D3 golden
> regression suite** (Phase 3, task 032) can assert them under an ADR-038 KEEP path. This note pins
> **fixtures + expected outcomes only**; it does NOT build the harness or modify engine code.
> **Escalation trigger evaluated → did NOT fire**: R1 task 013 reconciles unambiguously (applied), and the
> golden outcomes do not conflict across rounds — the four UAT rounds are *progressive fixes*, each closing a
> different bug, so the pinned expected state is their **union**, not a contradiction.

---

## 1. R1 task 013 reconciliation — ✅ DONE (applied to `spaarkedev1`)

| Question | Answer | Evidence |
|---|---|---|
| Was the R1 task-013 taxonomy seed applied, carried over, or superseded? | **APPLIED (done).** | `projects/email-communication-intelligence-r1/notes/013-taxonomy-seed.md` — verdict "✅ PASS", 7 starter taxonomy rows seeded live to `spaarkedev1` on 2026-07-29 via Dataverse MCP `create_record`, verified post-insert by `read_query`. Idempotency check ran first (table was empty, 0 rows). No schema created — the `sprk_triagecategory` table pre-existed (operator-created, confirmed task 001). |

**Seeded rows (category → priority-weight, all `sprk_enabled=Yes`), with live GUIDs:**

| `sprk_name` | weight | `sprk_triagecategoryid` |
|---|---|---|
| Court / Filing | 100 | `65310056-598b-f111-8077-7ced8ddc4cc6` |
| Client instruction | 80 | `5ac90050-598b-f111-8077-7ced8ddc4cc6` |
| Opposing counsel | 70 | `7d310056-598b-f111-8077-7ced8ddc4cc6` |
| Invoice / Billing | 60 | `71310056-598b-f111-8077-7ced8ddc4cc6` |
| Scheduling | 50 | `73310056-598b-f111-8077-7ced8ddc4cc6` |
| Administrative | 30 | `80310056-598b-f111-8077-7ced8ddc4cc6` |
| Marketing / Noise | 10 | `8b310056-598b-f111-8077-7ced8ddc4cc6` |

**Carry-over into R2**: none required for the seed itself (it is live). R2 FR-D4 (task 033) seeds a *different* table (`sprk_emailupdatefield`, the Job B allow-list) — do not conflate. R1 013 is closed.

---

## 2. The golden fixture — ONE representative UAT email, four pinned items

> **Important framing**: the four identifiers are **not four separate emails**. They are the identifiers +
> attachment inside **one** representative R1 UAT email — the *"new patent application"* email that references
> **PAT-942665 + PAT-942404 + REAL-2026-123456.02** and carries **3 attachments incl. `Invoice-10044725.pdf`**.
> The FR-D3 suite should reproduce this single email (or an equivalent synthesized `.eml` with the same
> identifier/attachment shape) and assert the union of expected outcomes below.
> **Source of truth**: `projects/email-communication-intelligence-r1/current-task.md` Quick-Recovery lines
> (rounds 1/2/2b record + UAT-diagnosis-method row) and `notes/061-uat-round3-core-only-autofile.md` (round 3).

### Live provenance rows (the recoverable evidence trail in `spaarkedev1`)

| Round | `sprk_communication` id (provenance row) | What it demonstrates |
|---|---|---|
| Round 1 | `1d43505d-…` (partial in notes) | bare-numeric `123456` collision with Invoice #123456 |
| Round 2 | `47251eb3-538c-f111-8076-000d3a98755b` | F1 `DocumentAssociation` followed `Invoice-10044725.pdf` → invoice #111333 |
| Round 3 | `cfd3f282-938c-f111-8076-000d3a98755b` | contact `Ralph Schroeder` written:true (the "Filed automatically" contact bug) |

Read `sprk_associationprovenance` on these `sprk_communication` rows (newest by `createdon`) to recover the exact rung scores if the suite needs them.

### Pinned items + exact expected engine outcome (transcribed from the R1 UAT record — do NOT invent)

**① PAT-942665** — patent-application number (subject/body reference)
- **Expected**: NOT auto-filed to an existing record on its own. FR-12 fires → capped `:new-record-referenced` ("Looks like a new Project"). E1 `RecordNameMatchRung` ranks the subject-tier PAT match above body/attachment tiers (subject 0.97 > body 0.90 > attach 0.82).

**② PAT-942404** — second patent-application number (same email)
- **Expected**: same as ①. Because **two matters conflict**, the matter tier resolves to **`Ambiguous`** ("Needs your decision") — matters are correctly **withheld**, never auto-crowned. This is the core "Ambiguous on conflicting matters" assertion.

**③ REAL-2026-123456.02** — well-formed record identifier (contains the digit-run `123456`)
- **Round-1 bug**: the bare-numeric substring `123456` collided with Invoice "Invoice Wizard" `f749a11e` (#123456) at 0.65 → written → became the headline via Ambiguous fall-through.
- **Fix P1** (bare-numeric substring guard): a digit-run *inside* a well-formed identifier no longer collides.
- **Expected**: `REAL-2026-123456.02` resolves to its own well-formed identifier; the embedded `123456` **must NOT** match an invoice (or any bare-numeric target). Bare-numeric identifiers never auto-file alone (need reinforcement).

**④ `Invoice-10044725.pdf`** — attachment (one of 3 on the email)
- **Round-2 bug**: the invoice association came entirely from **F1 `AttachmentDocumentAssociationRung`** (`RungKind.DocumentAssociation`) following the PDF's `sprk_invoice` link → invoice `55328b00` #111333 → written → crowned via Ambiguous fall-through.
- **Fix Round-2 (Fix B)**: `Ambiguous` produces **no denormalized headline** (stops crown-the-leftover).
- **Fix Round-2b**: F1 is **type-agnostic** (follows ALL doc links, no hard-coded type) **AND surface-only** — `AssociationStatusMapper.IsSurfaceOnly(DocumentAssociation)` makes F1 matches review **candidates**, never written as "filed".
- **Expected**: the attached invoice surfaces as **"Suggested · confirm"**, **NEVER "Filed automatically"**.

**⑤ Round-3 cross-cutting (core-only) — applies to the whole email**
- **Expected**: only **core** record types (`sprk_matter`, `sprk_project`, `sprk_servicerequest`) are auto-file-eligible / written. **Contacts, organizations, invoices, etc. → suggest-only, never auto-associated.** Concretely: contact `Ralph Schroeder` (which reinforced to 0.966 via ParticipantCorrelation + ThreadContinuity + ContactNameMatch) must show **"Suggested · confirm"**, not "Filed automatically". Core set is config-driven (`Communication:AutoFile:CoreWritableEntities`, ADR-018).

### One-line assertion summary (what FR-D3 must guard)
> Two conflicting matters → **Ambiguous** (withheld) · PAT numbers → **new-record-referenced / "Looks like a new Project"** (E1 subject-tier ranked) · embedded `123456` → **no invoice collision** · attached invoice → **Suggested, never Filed** · contact/org/invoice → **suggest-only** · only matter/project/service-request auto-file.

---

## 3. Where the fixtures live + intended FR-D3 KEEP path

- **This note** (`projects/email-communication-intelligence-r2/notes/fixtures/r1-golden-emails.md`) is the durable descriptor + expected-outcome pin.
- **Intended ADR-038 KEEP path for the FR-D3 harness (task 032)**: `tests/integration/seam/Communication/**`
  (the golden end-to-end / seam KEEP category — a MUST-KEEP under ADR-038, CI-guardable). The FR-D3 task
  should place the synthesized `.eml` fixture + the assertion suite there, **not** a scaffolding path.
- **Fixture form**: descriptor-based (identifiers + attachment shape + expected outcomes in this note). The
  FR-D3 task synthesizes a `.eml` reproducing the identifier/attachment shape, OR re-hydrates from the live
  provenance rows in §2.

---

## 4. Recovery gaps (flagged, not silently omitted — per acceptance criterion)

| Gap | Detail | Impact / handling |
|---|---|---|
| **Raw `.eml` not recoverable** | No raw `.eml`/`.msg`/`.pdf` for the golden email or `Invoice-10044725.pdf` exists in the R1 project tree (only an unrelated `competitive-analysis-email-filing.pdf`). | FR-D3 (task 032) must **synthesize** an equivalent `.eml` from the identifier/attachment descriptor in §2, or re-hydrate from the live `sprk_associationprovenance` rows. Not blocking — the expected outcomes are fully pinned. |
| **Round-1 comm GUID partial** | Round-1 provenance id recorded as `1d43505d-…` (truncated in R1 notes). | Rounds 2 (`47251eb3-…`) and 3 (`cfd3f282-…`) are full; round-1's P1 guard is independently assertable (embedded-digit-run → no invoice collision) without the exact row. Non-blocking. |
| **No cross-round conflict** | Evaluated for the escalation trigger — none found. Rounds are progressive fixes; expected state is their union. | Escalation trigger did **not** fire; baseline is unambiguous. |

---

*Feeds Phase-3 FR-D3 harness (task 032). No engine code or test harness authored here (task boundary).*

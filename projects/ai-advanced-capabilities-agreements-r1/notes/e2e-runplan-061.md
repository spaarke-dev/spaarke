# E2E Run Plan — Task 061 (`ai-advanced-capabilities-agreements-r1`)

> Composed from the `<ui-tests>` blocks in tasks 021/040/041/042/051/052 + the deferred-UAT notes in
> 011/012/022/023/032/033 + `notes/deploy-report-060.md` (deployed state) + `spec.md` Success Criteria 1–14.
> Written under task 061 Step 0. Steps 1–4 are **blocked** on three external prerequisites (§1) — this document
> makes execution turnkey the moment they clear.

---

## STATUS

| Step | State |
|---|---|
| **Step 0** — assemble test-doc set + composed run plan | ✅ **COMPLETE** — this document |
| **Step 1** — run flows 1–4 (both themes), capture evidence | 🔲 BLOCKED on §1 prerequisites |
| **Step 2** — Flow 5 (memo + Word-open fidelity) | 🔲 BLOCKED on §1 prerequisites (P2 + manual Word step) |
| **Step 3** — file findings, write `notes/e2e-report-061.md` | 🔲 pending Steps 1–2 |
| **Step 4** — flip `TASK-INDEX.md` 061 → ✅ | 🔲 pending Step 3 (or left 🔲 with findings filed, per the task's own acceptance wording) |

**Who does what**

| Party | Responsibility |
|---|---|
| **Azure/infra owner** | P1 — provision the Reasoning-tier AOAI deployment + set the App Setting (§1.P1; ~15 min once region/model availability is confirmed; commands are copy-paste ready). |
| **Operator** (human with Chrome, Word, and a licensed `spaarkedev1` account) | P2 — start `claude --chrome` (or run the flows manually); source/prepare the 5 test docs (P3); execute Flows 1–5; perform the manual Word-open sub-checklist (§3) — Word cannot be opened by an agent session. |
| **Claude Code** (this session, resumed once P1–P3 clear) | Execute this run plan verbatim, capture evidence, file findings per §4, write `notes/e2e-report-061.md`, update `TASK-INDEX.md`. |

---

## §1 PREREQUISITES (exact, checkable)

### P0 — Pre-flight sanity check (shared dev environment)

`spaarke-bff-dev` / `spaarkedev1` are shared across many active worktrees (per `projects/INDEX.md`); `deploy-report-060.md`
flags this explicitly. Before running any flow, re-verify task 060's deploy is still live:

```powershell
# BFF healthy + still task 060's HEAD
curl https://spaarke-bff-dev.azurewebsites.net/healthz   # expect 200

# agreement-classify Action + Binding still present (deploy-report-060.md GUIDs)
# via mcp__dataverse__describe / read_query:
#   sprk_analysisaction  id=53406e5b-5b8d-f111-8076-70a8a58a7766  (Agreement Classify)
#   sprk_playbookconsumer id=ed92d769-5b8d-f111-8076-70a8a58a7766 (binding, enabled=true)
```

If either check fails, STOP — another worktree's deploy may have clobbered this project's rows; escalate before
running flows against a broken environment.

### P1 — Reasoning-tier Azure OpenAI deployment (BLOCKING for classifier-accuracy validation)

**Config key**: `DocumentIntelligence:ReasoningModel` (appsettings) / **App Setting** `DocumentIntelligence__ReasoningModel`
(Azure double-underscore convention) / **CI/CD template token** `#{AI_REASONING_MODEL}#`
(`src/server/api/Sprk.Bff.Api/appsettings.tokens.md` + `appsettings.template.json`).

**Current state** (per `notes/deploy-report-060.md` "Known limitations" — confirmed still open as of the 060 deploy,
2026-07-31): **EMPTY** in `spaarkedev1`. `ModelTierDeploymentResolver.Resolve()`
(`src/server/api/Sprk.Bff.Api/Services/Ai/LinearConsumers/ModelTierDeploymentResolver.cs:44-53`) falls back to
`StandardModel` (`gpt-4o-mini`) when `ReasoningModel` is null/empty/whitespace — so `agreement-classify` (Reasoning
tier, `sprk_modeltier=100000002`) and `agreement-review` **do execute today without erroring**, but the classifier
runs on a non-reasoning model. This does NOT hard-block Flow 1/2 mechanically, but the ≥0.85 confidence-gate
assertions (FR-08/NFR-04) and classification-accuracy assertions are **not meaningful** until this is fixed — file
any confidence/accuracy anomaly as a finding tagged "possibly P1-blocked" rather than a code bug.

**Exact unblock commands** (from `projects/ai-advanced-capabilities-nda-r1/notes/task-013-reasoning-provisioning.md`
— still current; the resolver needs zero code changes when this lands):

```bash
# 1. Confirm model availability in the dev resource's region (West US 2 — NOT verified as of task-013's research pass)
az cognitiveservices account deployment list \
  --name spaarke-openai-dev --resource-group spe-infrastructure-westus2
az cognitiveservices account list-models \
  --name spaarke-openai-dev --resource-group spe-infrastructure-westus2

# 2. Provision (gpt-5 recommended per task-013 §3; gpt-5-mini fallback if gpt-5 unavailable in-region)
az cognitiveservices account deployment create \
  --name spaarke-openai-dev --resource-group spe-infrastructure-westus2 \
  --deployment-name gpt-5-reasoning --model-name gpt-5 --model-version "2025-08-07" \
  --model-format OpenAI --sku-name GlobalStandard --sku-capacity 10

# 3. Set the BFF App Setting
az webapp config appsettings set \
  --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2 \
  --settings DocumentIntelligence__ReasoningModel=gpt-5-reasoning

# 4. Smoke test — trigger any Reasoning-tier Action, confirm BFF logs show model=gpt-5-reasoning (not gpt-4o-mini)
```

**Owner**: Azure/infra owner (external to this worktree — this has been env-blocked-external across three sibling
projects: nda-r1 task 013, and now agreements-r1 tasks 020/021/060/061 all carry the same caveat forward).

### P2 — Claude Code `--chrome` session OR manual operator

- `ui-test` skill requirements: Claude Code 2.0.73+, **Google Chrome** (not Edge/Brave), "Claude in Chrome" extension
  1.0.36+, a paid plan. Start with `claude --chrome`; verify via `/chrome` → "Connected". **WSL is not supported.**
- If a `--chrome` session is unavailable: a human operator runs the flow scripts below manually in Chrome, signed
  into `spaarkedev1`, capturing the same evidence (screenshots + a DevTools Network **HAR export** for Flow 3's
  zero-LLM assertion) and reports PASS/FAIL back for the findings-filing step (§4).
- Login/MFA is manual regardless of `--chrome` (per the skill's "Requires Manual Intervention" table) — sign in to
  `https://spaarkedev1.crm.dynamics.com` with a licensed user before starting.

### P3 — Five test documents (DOCX only — PDF ingest is out of scope per spec §Out of Scope)

All docs must be DOCX, ≤25 MB (`CHAT-ATTACHMENT-POLICY.md` binary cap) — any realistic legal document is far under
the 2.5M-char/5M-char server text caps, so size is not a practical constraint here.

| # | Doc | Purpose | Sourcing guidance | Registry note |
|---|---|---|---|---|
| **1** | **NDA** | Exercises the ONLY fully-indexed knowledge pack (`KNW-011`, 14 chunks, per-clause B1–B16 taxonomy). Use for Flow 1 (expect high-confidence `nda` classification, auto-proceed) and Flow 5 (Word "Standard:" should carry real cited clause text). | Redact/anonymize a real NDA template, or synthesize one covering: Confidential Information definition, standard exclusions (already-known/public/independently-developed/rightfully-received), disclosure/use restrictions, term/survival, remedies. 5–15 sections. | `sprk_key=nda`, `sprk_knowledgepackref=KNW-011` (indexed). |
| **2** | **Employment-like non-NDA** | Exercises FR-01 generalization + the `employment` registry row. | Single-purpose employment agreement: compensation, at-will, non-compete, benefits — no confidentiality-only framing. | `sprk_key=employment`, `sprk_knowledgepackref=null` (**no indexed pack** — grounding retrieval returns zero results by design; NOT a bug. Use this doc for orientation/classification correctness only, not grounding-fidelity assertions.) |
| **3** | **Composite employment+NDA addendum** | Exercises FR-08 composite "choice-of-lens" incl. "both" = multi-pack sequential dispatch. | Concatenate doc #2 with a clearly delineated confidentiality exhibit (e.g. `"EXHIBIT A — NON-DISCLOSURE AGREEMENT"` as its own section/heading). | Should classify with candidates `employment` + `nda`. |
| **4** | **Invoice / non-agreement** | Exercises the FR-08 decline path (Flow 1 negative test). | Any non-contract document — an invoice, cover letter, or memo with no agreement language. | Should classify "not an agreement." |
| **5** | **Large 50+-section agreement (cap test)** | Exercises Flow 3's 128KB findings-payload cap/degrade-notice AND Flow 4's >25-selection batch soft-cap. | Synthesize a long numbered agreement (any type; NDA-shaped is fine — reuses the real pack) with ≥50 distinct clauses, engineered to produce ≥26 AI findings (needed to trip 041's soft cap) and a findings JSON plausibly near/over `InlinePayloadCapBytes=128KB` (`SessionLedgerEntries.cs:50`) — task 032 itself called this "a realistic worst-case findings payload." An LLM prompt asking for "a 55-section MSA, numbered 1.1–1.10, 2.1–2.10, …" is the fastest path. | N/A — behavioral/volume test doc, not a classification-accuracy test. |

### P4 — Dev environment URLs

| Surface | URL / identifier |
|---|---|
| Dataverse org | `https://spaarkedev1.crm.dynamics.com` |
| BFF API | `https://spaarke-bff-dev.azurewebsites.net` (App Service `spe-api-dev-67e2xz`, RG `spe-infrastructure-westus2`) |
| SpaarkeAi code page — general mode | `https://spaarkedev1.crm.dynamics.com/WebResources/sprk_spaarkeai` |
| SpaarkeAi — entity-scoped deep link | `https://spaarkedev1.crm.dynamics.com/WebResources/sprk_spaarkeai?entityLogicalName=sprk_matter&entityId={guid}` |
| SpaarkeAi — explicit `subDomain` deep link (isolates Flow 2's cold-load leg per task 022) | `https://spaarkedev1.crm.dynamics.com/WebResources/sprk_spaarkeai?subDomain=nda` |
| SpaarkeAi web resource record (cache-bust verification) | `5206a442-3451-f111-bec7-7ced8d1dc988` |
| "Create Analysis" wizard entry (Flow 2's actual door — not a raw URL param) | Ribbon button on a `sprk_matter`/`sprk_project` record → `docs/guides/spaarkeai-launch-points.md` Launch Points 1–2 |

---

## §2 FIVE FLOWS

Every flow: run once in **light mode**, then repeat the interaction (or at minimum the key visual states) in
**dark mode** (ADR-021) — mark both explicitly in the verdict line. Screenshot naming convention:
`{flow}-{state}-{light|dark}.png`, saved under `projects/ai-advanced-capabilities-agreements-r1/assets/e2e-061/`.

---

### Flow 1 — Interactive classifier (untyped upload → orient/confirm → grounded review)

**Setup**: Open SpaarkeAi in general mode (P4). Sign in if prompted.

**1a. Untyped upload orients + confirms** (021 ui-tests, verbatim expected)
1. Upload doc #1 (NDA); type "review this document"
2. Observe classifier gate behavior per confidence
3. Confirm a type; review runs grounded on that pack

**Expected**: "Below-threshold shows the confirm chips; after confirm, tools are agreement-scoped and the review
cites the right pack." (If P1 is unresolved, note whatever confidence score renders — do not treat a
lower-than-expected score as a code bug; tag any finding "possibly P1-blocked.")

**1b. Composite choice-of-lens** (021 ui-tests)
1. Upload doc #3 (composite employment+NDA); choose "both"

**Expected**: "Two sequential reviews run (one per pack); outcomes labelled per lens." — confirm SEQUENTIAL, not
parallel (ADR-016; watch the Network tab, only one dispatch in flight at a time).

**1c. Non-agreement decline** (negative — FR-08 acceptance criterion, spec + 021 acceptance)
1. Upload doc #4 (invoice); type "review this document"

**Expected**: An explicit "this doesn't look like an agreement" response + a general option; **no fabricated
agreement review**.

**1d. Negative — no false-fire on bare mention**
1. Type a message merely containing the word "review" with NO attached/target doc

**Expected**: The gate does NOT fire (021 acceptance criterion 6).

**1e. Dark mode** (021 ui-tests)
1. Toggle dark mode; repeat 1a's confirm-chip state

**Expected**: "Gate chips use semantic tokens; no hardcoded colors (ADR-021)."

**1f. DEF-01 spot-check (ambiguous target reported, 012 ui-tests)** — while reviewing doc #1 or #5, watch for any
finding whose target text appears >1 time in the document (e.g. a boilerplate phrase). Expected: it surfaces in the
Assistant outcome as "ambiguous" or "not_found," never silently placed on the wrong clause. If no such case
naturally occurs, mark this sub-check N/A (not a failure) — it is a regression watch, not a forced test.

**Evidence**: screenshots for 1a (chip state, light + dark), 1b (two labelled outcomes), 1c (decline message), plus
the Network tab confirming 1b's sequentiality.

**Verdict**: `Flow 1 — PASS / FAIL / FILED (finding IDs): ______  Light: __  Dark: __`

---

### Flow 2 — Explicit wizard path (type picked → deterministic bind → auto-run 033)

**Setup**: On a `sprk_matter` or `sprk_project` record, launch "Create Analysis" (ribbon).

**2a. Explicit wins, zero classifier gate** (023 ui-tests)
1. Launch via wizard with type=`nda`, upload doc #1, finish

**Expected**: "No classifier gate; review grounded on the [nda] pack." Also verify FR-17 (033): review
**auto-runs** (no manual re-upload), advisory comments render in the already-open editable Compose, and
`GET /api/ai/chat/sessions/by-analysis/{analysisId}` (Network tab) returns the bound session — the durable-FK
regression 033 exists to guard against.

**2b. Employment orientation-only check** (registry has no pack for `employment` — see P3 #2)
1. Repeat 2a with type=`employment`, doc #2

**Expected**: Orientation + tool-scoping correct (activeWorkType/subDomain set); grounding may legitimately return
zero cited standards — this is NOT a bug (see P3 table). Confirm no error/crash, just thin grounding.

**2c. Mismatch notice (warn-only, non-blocking)** (023 ui-tests)
1. Wizard type=`nda` on doc #2 (employment doc)

**Expected**: "Review runs as NDA (user wins); a non-blocking informational notice appears." — no re-route, no gate.

**2d. Cold-load door** (022 ui-tests, optional if P4's deep-link URL is reachable pre-auth)
1. Open `https://spaarkedev1.crm.dynamics.com/WebResources/sprk_spaarkeai?subDomain=nda`

**Expected**: "Three-pane opens oriented (agreement tools scoped; envelope populated)."

**2e. Dark mode**: toggle during 2a's wizard + review-render states.

**Evidence**: screenshots of wizard type-picker, zero-gate review start, mismatch notice banner, by-analysis network
response body (redact tokens).

**Verdict**: `Flow 2 — PASS / FAIL / FILED (finding IDs): ______  Light: __  Dark: __`

---

### Flow 3 — Reopen durability (zero-LLM restore of gutter + summary panel) — **FR-16, the project's headline assertion**

**Setup**: Complete a review from Flow 1 or 2 first (doc #1 NDA recommended — has real findings to restore). Open
DevTools **Network tab BEFORE reloading**; keep it recording across the reload.

**3a. Zero-LLM reopen restore** (032 ui-tests, verbatim expected)
1. Review a doc; reload the page/session with devtools network open

**Expected**: "Gutter notes + summary rows + overallRisk restore; zero dispatch/LLM calls in the trace."

**Network-trace requirement (mandatory evidence)**:
- **FORBIDDEN** during/after the reload: any `POST /api/ai/chat/sessions/{id}/messages`, any new SSE
  (`text/event-stream`) connection, any `POST /api/ai/analysis/fork` or `/promote`.
- **EXPECTED (reads only, not violations)**: `GET /api/ai/chat/sessions/by-analysis/{id}`,
  `GET /api/ai/chat/sessions/{id}/compose-outputs`, `GET /api/ai/chat/sessions/{id}/review-memo` (if the memo panel
  auto-loads), standard Dataverse/OData reads.
- **Capture**: export a HAR file (or a clearly-annotated screenshot list) spanning the reload, saved as
  `assets/e2e-061/flow3-network-trace.har`. This IS the FR-16 acceptance evidence — do not substitute a verbal
  "looked clean."

**3b. Over-cap payload (doc #5, 50+ sections)**
1. Run a review on doc #5; reload with devtools open

**Expected**: EITHER (a) full restore if the findings payload lands under 128KB, OR (b) a visible
`reviewFindingsDegraded` banner (task 032 chose Leg B — an explicit notice, not chunking; reasons `'malformed'` or
`'skipped'`) — **never silent absence** of findings. If neither happens (findings just vanish with no notice), file
a FAIL finding — this is exactly the failure mode 032 was built to close.

**3c. Coexistence — findings + edit both restore**
1. On doc #1's review, accept one draft-alternative edit on a flagged clause
2. Reload

**Expected**: BOTH the findings (gutter notes, summary panel) AND the accepted edit's state restore — the
"highest-turn-only eviction" bug 032 fixed must not have regressed.

**3d. Supersede protection**
1. On an edit output, use "Try another" (supersede)
2. Reload

**Expected**: Findings are unaffected by the supersede (different Binding — structurally unreachable per 032's
design note).

**3e. Dark mode** (032 ui-tests)
1. Reopen in dark mode

**Expected**: "Restored badges/rows use semantic tokens."

**Verdict**: `Flow 3 — PASS / FAIL / FILED (finding IDs): ______  Light: __  Dark: __  HAR attached: Y/N`

---

### Flow 4 — Batch + confirmations UX

**Setup**: A reviewed doc with ≥3 flagged clauses in the gutter (doc #1 or #5).

**4a. Batch run** (041 ui-tests, verbatim expected)
1. Review a doc; select 3 notes via checkboxes
2. Run "Draft alternative" from the sub-toolbar

**Expected**: "Progress bar; 3 sequential runs; 3 per-note Assistant outcomes identical in form to single runs."
Confirm via Network tab: never more than one dispatch in flight (ADR-016).

**4b. Confirmation formatting** (042 ui-tests, ride-along with 4a)

**Expected**: "Three visually distinct entries, each with a bold location header" and clear inter-entry spacing —
NOT a run-together wall of text. **Known caveat (042's own notes)**: no shipped caller currently threads a location
field into the dispatch request/result, so the bold-header code path may render the graceful generic fallback
instead of an actual clause label — if the header is present-but-generic (not literally "undefined"), that is
EXPECTED per 042's documented gap, not a new finding; if the header is missing entirely or shows "undefined," file
it.

**4c. Select-all + cap** (041 ui-tests) — requires doc #5 (≥26 flagged findings)
1. Select 1 note → confirm "select all" affordance appears
2. Select all (>25 total)

**Expected**: "Confirm prompt appears before running" (the soft cap ~25, task 032/spec assumption).

**4d. Negative — zero-selected**

**Expected**: No sub-toolbar renders with zero notes selected (041 acceptance criterion).

**4e. Failure isolation (best-effort)** — if a mid-batch dispatch can be induced to fail (e.g. a brief network
interruption during the batch), confirm remaining notes still run and the end-of-batch summary reports
success/failure per note. If not reliably inducible in this session, mark N/A rather than fabricating a pass.

**4f. Dark mode** (041/042 ui-tests): toggle with the sub-toolbar open; toggle during a rendered batch of
confirmations.

**Evidence**: screenshots of checkboxes + sub-toolbar, progress bar mid-run, cap-confirm prompt, the 3 distinct
confirmation entries with headers.

**Verdict**: `Flow 4 — PASS / FAIL / FILED (finding IDs): ______  Light: __  Dark: __`

---

### Flow 5 — Memo generate/email + Word-comment export fidelity (manual Word open)

**Setup**: A completed review (from Flow 1 or 2).

**5a. Generate memo** (051 ui-tests, verbatim expected)
1. Complete a review; toolbar → "Create Summary Memo" → Generate

**Expected**: "A .docx downloads; opening it shows the memo sections matching the review" — sections
{location, before, after, why, golden-ref} per persisted `sprk_analysisoutput` record.

**5b. Email memo** (051 ui-tests, verbatim expected)
1. Toolbar → "Create Summary Memo" → Email

**Expected**: "EmailComposer opens with memo body + prefilled subject" (`"Review Summary Memo — {analysis name}"`);
**nothing auto-sends** — do NOT click send; verify the draft is prefilled only.

**5c. Negative — no memo yet**
1. On a document with NO completed review, open the toolbar dropdown

**Expected**: A clear "generate the review/memo first" state — not an empty/broken export (051 acceptance
criterion). Do this check FIRST, before running any review on that doc.

**5d. Word-comment export fidelity** — **see §3 for the full manual checklist.**
1. Save the reviewed doc; download; open in Word

**5e. Dark mode**: toggle with the memo dropdown open.

**Evidence**: downloaded `.docx` filenames, EmailComposer screenshot (prefilled, unsent), Word screenshots per §3.

**Verdict**: `Flow 5 — PASS / FAIL / FILED (finding IDs): ______  Light: __  Dark: __`

---

## §3 MANUAL WORD-OPEN SUB-CHECKLIST (Flow 5d)

This step requires an actual Word desktop/web session — no agent tooling can open Word. Operator-only.

Per 052's root-cause map (`notes/word-comment-export-gap.md`) and the task's own completion notes, check each
comment in the saved-and-reopened DOCX against the on-screen gutter for these **four symptoms**:

| # | Check | Expected | If it fails |
|---|---|---|---|
| 1 | **Author** | Comment author shows the configured value (default `"AI Advisory Review"` — no admin UI currently exposes a non-default override, so this e2e pass can only confirm the string is non-empty and not the literal `"undefined"`; true configurability is proven at the unit-test level by `ComposeEditor.advisoryCommentAuthor.test.tsx`, not observable end-to-end without a code-level override). | Author blank/undefined/wrong string → file a finding. |
| 2 | **"Flagged clause" label** | Comment body's first segment reads `"Flagged clause: …"` — NOT the old `"Grounded fact — …"` raw-prose form. | File a finding (regression to the pre-052 gap). |
| 3 | **"Assessment says: …"** | A second segment with the AI's judgment prose, distinct from the flagged-clause quote. | File a finding. |
| 4 | **"Standard: …"** | A citation reference (at minimum), full clause text when the grounding pack supplied it (doc #1 NDA, via `KNW-011`, should show real cited text; doc #2 employment, ungrounded, may show citation-only or be absent per the spec's own assumption). | File a finding only if COMPLETELY absent on the NDA doc (where grounding exists) — absence on the ungrounded employment doc is expected. |

**The `\n\n`-as-literal-LF rendering nuance (052 flagged this explicitly — the key open question this manual step
resolves)**: `ApplyComment` (server, unchanged by 052) writes `commentText` as a **single OOXML text run**; 052's
segment separators between "Flagged clause" / "Assessment says" / "Standard" are **literal LF (`\n\n`) characters**,
not `<w:br/>` elements. OOXML text runs typically do NOT render bare LF characters as visible line breaks — Word may
either (a) render the three segments run together with no visible break (likely, given the encoding), or (b) some
Word/renderer combination may happen to honor it. **Check which actually happens** in this session's Word:

- If segments render on **separate lines/paragraphs** (matching the gutter's stacked visual structure) → PASS, no
  finding needed.
- If segments render **run together** (e.g. `"Flagged clause: X Assessment says: Y Standard: Z"` with no breaks) →
  this is functionally correct (all four symptoms' TEXT is present and correctly labelled) but **readability is
  degraded** relative to the gutter. File this as a **finding** (not a Flow-5d hard FAIL — the acceptance criterion
  is about label/content correctness, not layout) recommending a follow-up task to swap the literal `\n\n` for an
  actual `<w:br/>` insertion (either client-side pre-processing before `ApplyComment`, or a small server-side
  split-and-re-join at comment-write time).

**Additional checks (052 acceptance criteria)**:
- **Durable-recalled parity**: open a comment that was placed via Flow 3's reopen (not live-dispatched) — verify it
  exports identically to a live-placed comment (same 4-symptom structure). Cross-reference with Flow 3's evidence.
- **Legacy-thread graceful degrade**: if any pre-002-schema thread exists in this environment (unlikely on a fresh
  060 deploy — skip unless one is actually found), confirm it exports its raw text without crashing or fabricating
  structure it doesn't have.
- **Server untouched**: this is a code-review-time check (git diff on `ComposeShadowPatchEngine.cs`), not observable
  in Word — no action needed here, just noting it's covered elsewhere (052's own acceptance criteria).

---

## §4 FINDINGS PROTOCOL

**Findings are FILED, never hot-fixed inside this task** (POML constraint, explicit). For every FAIL or unexpected
behavior observed across §2/§3:

1. Create one file per finding: `notes/e2e-findings-061/F-{NN}-{short-slug}.md` (e.g.
   `F-01-word-comment-lf-not-linebreak.md`), using this shape (mirrors
   `.claude/skills/project-defer-issue-tracking/references/defer-issues-template.md`'s DEF/ISS entry fields so it can
   be promoted directly):

   ```markdown
   # F-{NN} — {Title}

   | Field | Value |
   |---|---|
   | **Flow** | {1-5, or §3 Word checklist} |
   | **Severity** | Blocker / Major / Minor / Cosmetic |
   | **Filed** | {YYYY-MM-DD} |
   | **Related task/FR** | {e.g. FR-15 / task 052} |

   **Repro steps**
   1. ...

   **Expected vs Actual**
   - Expected: ...
   - Actual: ...

   **Evidence**
   - {screenshot/HAR path under assets/e2e-061/}

   **Suggested owner / fix task**
   {1-2 sentences — hypothesis only, not a fix}
   ```

2. Reference each finding's ID in the corresponding flow's verdict line (§2/§3).
3. After all flows run, decide disposition per finding:
   - **Blocker/Major** → run `/project-defer-issue-tracking` to promote to a paired GitHub Issue (DEF-XXX or
     ISS-XXX) for the portfolio board, AND/OR draft a new numbered fix task under `tasks/` if it's squarely within
     this project's remaining scope.
   - **Minor/Cosmetic** → file locally under `notes/e2e-findings-061/`; portfolio-promotion optional, reviewer
     judgment.
4. Write the consolidated `notes/e2e-report-061.md` (per task 061's `<outputs>`) summarizing all 5 flows' verdicts +
   linking every filed finding — this is the task's actual completion artifact, not this run plan.
5. Per the task's acceptance criteria, 061 can complete with filed findings instead of all-PASS — "All five flows
   verdict PASS (or filed findings with precise fix tasks); evidence attached" — so a finding does NOT block closing
   061, it blocks closing it SILENTLY.

**HARD BOUNDARY carried from this task's own constraints**: do not fix anything discovered here inside task 061's
execution — file it, cite it, move to the next flow.

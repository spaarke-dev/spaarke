# Task 052b — making stale-target DETECTION durable beyond the tab

> **Task**: `052b-stale-detection-durability.poml` · **Rigor**: FULL · opus @ xhigh · **Date**: 2026-08-26
> **Scope**: DETECTION only. The resolution half (FR-17 supersession, assessment §4.4 O-1…O-6) is
> shipped, correct and **unchanged by this task**.
> **Depends on**: 052 (the question), 053/053b (the anchorless legs this shares a file with).

---

## 0. Executive summary

| # | Finding / decision | Consequence |
|---|---|---|
| **F-1** | **The staleness GATE needs only an equality-comparable FINGERPRINT.** The capture-time TEXT is needed by exactly one consumer, and it is a *display* one. | The durable datum can be a hash → no Tier-3 content at rest (ADR-015). |
| **F-2** | **A payload-borne carrier is REJECTED on measurement, not on taste.** At ~300 short clauses a whole-document revise already sits at 127,934 B against the 131,072 B cap; a 16-char fingerprint per edit tips it to 136,634 B → `ProjectComposeOutputs` **skips the entry entirely** and the suggestion VANISHES. | The POML's escalation trigger is real and reachable. Not shipped. |
| **F-3** | **The hole was never really the store — it was the INFERENCE.** 052 read "no baseline recorded" as "this must be the first materialize". That is also what a different tab, an evicted entry and a disabled store look like, so on those paths it re-baselined against a possibly-drifted paragraph and applied silently. | The fix is a *discriminator*, not a bigger store. Correctness no longer depends on any store surviving. |
| **F-4** | **The discriminator already exists in the host, structurally.** `materializeComposeDraftFromLedger(ref)` (targeted) is only ever called from the Flow-5 apply leg, which every producer emits immediately after WRITING the entry; `materializeComposeDraftFromLedger()` (untargeted) is the reopen/refresh-durability pass. | `origin: 'live' | 'replay'` is carried, not derived (project invariant 7). |
| **F-5** | **No server change was required, and none was made.** `src/server/**` and `tests/**/*.cs` are untouched. A residual that only the server can close is recorded in §7. | No BFF publish-size / CVE report needed. |

---

## 1. What the comparison actually needs — hash or text? (POML step 1)

**Answer: the GATE needs a fingerprint. The TEXT is display-only.** Evidence, in code, not by argument:

| Consumer | Reads the baseline? | What it actually needs |
|---|---|---|
| The stale gate, `usePendingRedline.planAndApplyTargeted` | Yes | `proposedAgainst !== currentText` — a pure **equality** test. Nothing else. |
| `redlineLocalDiff.narrowAnchoredSpan(editor, paragraphSpan, newText)` | **No** | Its two inputs are the LIVE paragraph (`buildRangeCharIndex` over the anchored span) and the model's `new_text`. The baseline is not a parameter and never was. |
| `redlineLocalDiff.computeLocalEditRange(currentText, replacementText)` | **No** | Same two strings. |
| `ComposeWorkspace.tsx` stale ConfirmModal | Yes | `truncateClause(redlineStaleTarget.proposedAgainst)` — the *"When suggested: “…”"* line. **The only reader of the characters.** |

So the sentence in the POML's background — *"`redlineLocalDiff` needs the TEXT, not just a hash, to compute the local edit range"* — is true of the *current* paragraph, which comes from the editor, and **not** of the capture-time baseline. Verified by reading both functions' signatures: neither takes the baseline.

**Consequence.** The durable tier can store a one-way digest. What is lost when only the digest survives is the ability to *quote* the old wording — so the modal drops that one line rather than rendering an empty quote. Detection is unaffected; the confirmation is one line poorer in the cross-tab case.

---

## 2. Carrier enumeration + the §11 three-question justification (POML step 2)

### 2.1 The candidates, and why each one does or does not fit

| Carrier | Fits? | One-sentence reason |
|---|---|---|
| **Web Storage — the existing `redlineProposalBaseline` module, extended from tab-scoped to origin-scoped** | **CHOSEN** | It is the carrier this datum already uses; widening its durability tier costs one new key and no new module, no new endpoint, no new table, and no server round-trip on a synchronous placement decision. |
| `SessionOutput.Payload` (the compose ledger payload) | No | The capture-time fingerprint would have to be written server-side at route time by a component that is *deliberately* payload-opaque (`OutputRouter`: "stores + returns; it NEVER parses the opaque payload"), **and** it fails the ADR-040 measurement in §3 — the fix would make the suggestion disappear rather than make it safe. |
| `ChatSession.ReferenceMap` (`ParaReferenceMapEntry`) | No | Wrong instant and wrong lifecycle: it is **replaced wholesale on every Load** from the freshest projection (`ComposeService.cs:852`), so it records "the paragraph at last load", never "the paragraph when this suggestion was proposed", and it is explicitly not append-only. |
| The Load-response `ParaIdMapEntry` projection (client-visible) | No | Same wrong instant — a load-time snapshot, refreshed on every mount — and it carries ids + numbering, no text. |
| An ADR-040 `WidgetEvent` ledger entry | No **for this task** | It is the right *shape* (O-3 names it), but there is **no client-callable endpoint that appends one**; reaching it needs a server change, which is outside this task's file boundary (see §7). |
| A new client store / IndexedDB / a Dataverse column | No | A third store for a datum that already has one — the exact thing CLAUDE.md §11 forbids. |

### 2.2 The three questions, answered for the one thing this task adds

The only new surface is **one additional `localStorage` key inside the existing module** (`spaarke.compose.redline-proposal-fingerprint`). No new module, no new type outside the two the gate needs, no new endpoint.

1. **Existing** — What does this overlap with? `redlineProposalBaseline.ts`'s existing `sessionStorage` tier, which holds the same datum in a richer, shorter-lived form. Verified by grep: `redlineProposalBaseline` had exactly one consumer (`usePendingRedline`) and one test import; nothing else in `src/` records or reads a capture-time paragraph.
2. **Extension** — Can I extend the existing instead? **Yes, and that is what was done.** The module keeps its public shape (`recordProposalBaseline` / `clearProposalBaselines` unchanged in signature) and gains one durability tier behind the same functions. `readProposalBaseline` was replaced by `compareProposalBaseline`, which answers the question the caller actually has rather than handing back a string for the caller to compare — that is what makes the gate structurally incapable of comparing texts across tiers.
3. **Cost-of-doing-nothing** — Name the concrete failure. *A user asks the assistant to revise clause 4.1, leaves the suggestion pending, and reopens the document in a second tab (or after a browser restart). The reopen pass re-materializes the suggestion, finds no per-tab baseline, records the clause **as the user has since rewritten it** as though it were the capture-time text, and applies the model's older wording over it — reporting success.* That is a silent overwrite of a legal clause. It is the exact defect FR-C05 exists to close, restored by a store boundary.

**No third store was built.**

---

## 3. ADR-040 cap headroom — measured before choosing (POML step 2 / acceptance criterion 4)

`SessionLedger.InlinePayloadCapBytes = 131,072` (128 KB). `ProjectComposeOutputs` (`ChatEndpoints.cs:1518`)
**skips truncation markers entirely**, so exceeding the cap does not degrade a suggestion — it deletes it
from the read projection.

Measured against a realistic `compose-revise-document` payload (`edits[]` of
`{new_text, target_para_id}` at the schema's real shape, 380-char operative sub-clauses, plus
`rationale` + empty `comments`/`sources`):

| N edits | clause chars | payload today | + 16-char fingerprint/edit | + sha-256 hex/edit | verdict |
|---|---|---|---|---|---|
| 50 | 380 | 21,434 | 22,884 | 25,484 | fine |
| 100 | 380 | 42,734 | 45,634 | 50,834 | fine |
| 200 | 380 | 85,334 | 91,134 | 101,534 | fine |
| **300** | **380** | **127,934** | **136,634** | **152,234** | **base is UNDER the cap; adding the fingerprint puts it OVER → entry skipped, suggestion vanishes** |
| 100 | 1,200 | 124,734 | 127,634 | 132,834 | sha-256 variant tips over |

Ceiling shift, i.e. how many edits a whole-document revise may carry before the cap bites:

| clause length | max edits today | with a 16-char fingerprint | with sha-256 hex |
|---|---|---|---|
| 380 chars | 307 | 287 (−20) | 258 (−49) |
| 800 chars | 154 | 149 (−5) | 141 (−13) |
| 1,200 chars | 105 | 102 (−3) | 98 (−7) |
| 16,000 chars (schema max) | 8 | 8 (−0) | 8 (−0) |

**Headroom statement.** A payload-borne carrier costs 0.4 %–6.6 % of the cap depending on N, and it
moves the cliff by up to 49 edits. There is a real, reachable band — a long agreement revised
clause-by-clause, ~287–307 short numbered clauses — where the payload fits today and would **not** fit
with the carrier attached, and in that band the failure is total disappearance rather than degradation.
That is precisely the POML's escalation trigger, so a payload-borne carrier was **not** shipped.

*Method (reproducible without repo state)*: `JSON.stringify({edits: [{new_text, target_para_id}] × N,
comments: [], rationale, sources: []})` at the shape
`infra/dataverse/outputschemas/compose-revise-document.schema.json` declares, measured with
`Buffer.byteLength(s, 'utf8')` against `SessionLedger.InlinePayloadCapBytes`; the carrier variants add
one extra string property per edit (`base_fp` at 16 chars, `base_sha256` at 64 hex chars).

The chosen carrier adds **zero bytes** to any ledger payload.

---

## 4. What was actually built

### 4.1 The gate, restated as the four cases that exist

`usePendingRedline.planAndApplyTargeted`, replacing 052's "is a baseline recorded?" two-way test:

| `origin` | recorded comparison | outcome |
|---|---|---|
| `live` | (any) | record + place — nothing can have drifted since the model wrote it |
| `replay` | `unchanged` | place — we KNOW this is the clause the suggestion was written against |
| `replay` | `changed` | **ASK**, `reason: 'changed'` — task 052's question, now durable across tabs |
| `replay` | `unrecorded` | **ASK**, `reason: 'unverifiable'` — the hole this task closes |

Two properties worth stating explicitly:

- **`live` only suppresses the `unrecorded` question.** A recorded-and-changed clause still asks on
  either origin. This task can only ADD questions 052 would not have raised, never remove one it would.
- **The discriminator is a fact about the CALL, not about a store.** That is what makes it survive
  where no store can follow: a different device, a cleared browser, private browsing.

### 4.2 `origin` is fail-closed, deliberately

`ComposeDraftProvenance.origin` is **optional on the type and `'replay'` in effect**. Rationale:

- Making it *required* would have forced ~119 mechanical edits across six test files. Mechanical edits
  invite "put `'live'` so it compiles", which converts a compile error into a silent loss of the
  guarantee — strictly worse than a safe default.
- With a fail-closed default, **omission cannot buy the caller the unsafe outcome**: the worst a
  missing `origin` costs is one confirmation; the worst a wrong `'live'` costs is the user's text.
- The property is asserted by test (*"a provenance that DECLARES NOTHING is treated as a replay"*),
  not left as a comment.

### 4.3 Why `targeted ⇒ live` is sound (traced, not assumed)

Every producer of a Flow-5 `compose_assistant_insert` carrying a `ledgerRef` emits it immediately
after WRITING that entry:

| Emitter | When |
|---|---|
| `ConversationPane.tsx:1442` | right after a compose dispatch returns, on the key `resolveCurrentComposeLedgerRef` just resolved |
| `useEditSupersession.undo` (`:197`) | on the key the supersession POST just returned (a retraction — empty payload, never reaches the anchored gate) |
| `useEditSupersession.tryAnother` (`:219`) | same retraction key, before chaining a fresh dispatch |

So a **targeted** materialize always renders an entry created moments ago in this mount. The
**untargeted** pass (`ComposeWorkspace.tsx` refresh-durability effect, `state.status === 'loaded'`) is
the opposite by construction: it replays whatever the ledger already held at load.

### 4.4 The two storage tiers

| tier | store | holds | scope | cap |
|---|---|---|---|---|
| durable | `localStorage`, one flat key | `fingerprintParagraph(text)` (~20 chars) | browser origin | 1,000 entries across all scopes (~110 KB) |
| per-tab | `sessionStorage`, per-scope key (**schema unchanged from 052**) | the paragraph TEXT | this tab | 200 entries per scope |

The per-tab tier **earns its place** and is kept: it is the only thing that can answer *"changed FROM
WHAT?"*, i.e. render the `When suggested: “…”` line. It is not a second source of truth for the
DECISION — the two tiers are two encodings of one datum written in the same call, and the per-tab tier
is consulted first only because it is strictly richer. Keeping its key schema unchanged also means a
tab already mid-session keeps its existing baselines when this ships.

**ADR-015**: the durable tier stores a digest, never text — asserted by a test that scans every
`localStorage` key for substrings of the clause. The per-tab tier keeps text exactly as 052 shipped it;
`sessionStorage` dies with the tab, the same lifetime as the editor buffer holding that paragraph.

**Fingerprint**: two independent 32-bit lanes (FNV-1a + a multiply/xorshift mix) plus the character
length, base-36. Synchronous and dependency-free by necessity — `crypto.subtle` is async and would turn
a synchronous placement decision into a promise. Residual, stated not hidden: a different-length text
can never collide; a same-length one collides at a nominal 2⁻⁶⁴, and the consequence is bounded to ONE
paragraph of ONE suggestion reverting to pre-052 behaviour.

---

## 5. The invariant-1 decision — what happens when detection cannot be established

**Decision: CONFIRM BEFORE APPLY, not apply-with-notice.** Reasoning:

1. **Cost asymmetry.** Apply-with-notice puts the model's older wording into the document over text the
   user may have typed since, then tells them. Confirm-before-apply costs one click and cannot destroy
   anything. R8's charter is that a silent wrong edit is the one outcome we do not ship.
2. **The user cannot judge it after the fact.** Once the redline is placed, the pre-edit wording is
   inside a deletion mark and the paragraph reads as a diff. The confirmation shows `Now: “…”` verbatim,
   which is the only form in which the question is answerable.
3. **§11 — no new surface.** Confirm-before-apply reuses the shipped hold-and-ask machinery
   (`deferredStaleRef`, `applyStaleTargetAnyway` / `dismissStaleTarget`, the `ConfirmModal`, and the
   host's FR-17 supersession write). Apply-with-notice would need a new banner *and* a per-clause undo.
4. **The friction is bounded and batched.** A whole-document replay raises **one** question covering N
   edits (`staleCount` / `totalCount`), not N modals — asserted by test. And the same-browser cases are
   quiet because of the durable tier, so the click only appears on a genuinely different device /
   cleared browser / disabled storage.

**The copy does not over-claim.** `reason: 'unverifiable'` gets its own title (*"Apply this earlier
suggestion?"*) and its own body — it says we have **no record of the wording it was written against**,
not that the clause changed, because we do not know that. And the `When suggested: “…”` line is
**omitted, never faked**, whenever only the fingerprint survived.

For a mixed batch the reason is `'changed'` if **any** held-back item is provably changed (the stronger
claim is then true of at least one), and the displayed paraId / text describe that same item — reason
and detail always name one and the same clause.

---

## 6. Verification

| Check | Result |
|---|---|
| `Spaarke.Compose.Components` | **103 suites / 1,317 tests, 0 failed** (baseline 102 / 1,298 → +1 suite, +19 tests) |
| `SpaarkeAi` | **121 suites / 1,121 tests, 0 failed** (baseline 121 / 1,121 — unchanged) |
| `npx tsc --noEmit` (compose pkg) | **9 errors — identical to the 9 pre-existing** (`@spaarke/ai-widgets` has no `dist`, 4 `noImplicitAny` in `ComposeWorkspace.tsx`). **Zero new.** |
| ADR-049 I-7 re-assert (grep) | the only value import of `resolveTargetSpans` on the edit path is `anchorlessReplayFallback.ts` (anchorless-only, cannot return `applied`); `usePendingRedline` imports only the TYPE + a re-export |
| `docxBridge.ts` / `redlineTextSearch.ts` | `git diff --stat` empty on both — untouched |
| BFF | **not touched** — no publish-size or CVE report applicable |

### 6.1 Test evidence — observed-failing vs proved-by-mutation

**Observed FAILING before the fix** (the whole new suite was written first and run against the
un-changed implementation): **16 of 17** initial tests failed. That included three that initially
passed *under the defect* and were strengthened until they did not — the vacuous-pass trap 053b hit:

| Initially-vacuous test | Why it passed under the defect | Strengthened with |
|---|---|---|
| *"apply anyway still places it"* | the silent apply had already left the same pending redline | assert `staleTarget !== null` **and** `pending` empty *before* answering |
| *"UNCHANGED clause: no question in the other tab"* | the defect reaches "no question" by having no record at all | assert `compareProposalBaseline(...) === 'unchanged'` from the wiped-tab view first |
| *"a LIVE materialize places without asking"* | live+apply is the pre-existing behaviour | wipe the tab afterwards and assert the record survived durably |

**Proved BY MUTATION** (each mutation reverted immediately; revert verified byte-identical by `diff`):

| Test | Mutation applied | Observed |
|---|---|---|
| *"a replayed whole-document change list asks ONCE"* | `if (origin === 'live')` → `if (true)` (i.e. delete the replay gate) | `['applied','applied']` instead of `['stale','stale']` — FAILED, reverted |
| *"a MIXED batch reports the stronger, still-true reason"* | promotion rule → plain first-wins | `reason: 'unverifiable'`, `paraId: STAB0041` instead of `'changed'` / `STAB0042` — FAILED, reverted |
| *"storage disabled: a LIVE materialize still places"* (the one test green before **and** after) | removed the `try/catch` in `storage()` | threw `storage disabled` out of `compareProposalBaseline` — FAILED, reverted |

---

## 7. Residual — recorded, not deferred silently

**The `live` leg's own window is still an approximation.** `origin: 'live'` records the paragraph as it
reads when the *result arrives*, not when the model *saw* it — an LLM call is seconds to tens of
seconds, and a user typing in the document during that window is not hypothetical. 052's module comment
said "milliseconds earlier"; that was optimistic then and is unchanged by this task.

Closing it properly is a **project-invariant-7 problem**: the server holds the document projection at
the moment it builds the compose scope, so the capture-time fingerprint is *deterministic information
available at capture time* and should be **carried, not re-derived at render time**. That requires a
server change, which is outside this task's file boundary. The shape, if the owner wants it sequenced:

- a per-edit fingerprint minted where the compose scope is built (the same instant the model is handed
  the document), keyed by `paraId`;
- carried **beside** the payload, not inside it — an `ADR-040 WidgetEvent` entry keyed by the edit's
  `{bindingId}@t{n}`, or a `SessionOutput` sibling field — because §3 shows a payload-borne carrier can
  push a realistic whole-document revise over the 128 KB cap, and `ProjectComposeOutputs` then drops
  the suggestion entirely;
- the client would read it in `compareProposalBaseline` as a **third tier**, consulted before both
  local tiers, and its presence would make the `'unverifiable'` outcome rare rather than
  device-dependent.

Until then the behaviour is honest in both directions: the replay case asks, and the live case records
a baseline whose only inaccuracy is bounded by one LLM round-trip.

**Also recorded**: `origin` is fail-closed but not compile-enforced (§4.2). A future call site that
forgets it gets a confirmation, never a silent apply — the failure mode is friction, not data loss.

---

## 8. ADR disposition

| ADR | Rule | Path |
|---|---|---|
| **ADR-049 I-7** | No text search as a placement mechanism. | **C — comply.** Re-asserted by grep (§6) and by the tripwire test in the new suite, which arms the throwing double and drives an anchored payload that *also* carries matching prose. |
| **ADR-041** | Confirmation policy. | **C — comply.** No `PendingPlanManager`, no `SessionGate`, no `gateId`, no `BindingRisk`. This task adds a second *reason* to an existing non-Gate confirmation; the resolution leg (FR-17 supersession) is byte-unchanged. |
| **ADR-040** | Inline payload cap; append-only. | **C — comply.** Nothing is written to the ledger by this task; the cap was measured (§3) as the reason NOT to use it. |
| **ADR-015** | Tier-3 content governance. | **C — comply.** The durable tier stores a digest, asserted by test. No governed-store row was needed, so the §6.5 Path B precedent (task 060) does not fire. |
| **ADR-050 / ADR-021** | Modal shell + semantic tokens. | **C — comply.** Same `ConfirmModal`, same `dismiss="alert"` default; only the title/message strings branch. |
| **CLAUDE.md §11** | Reuse, do not rebuild. | **C — comply.** §2.2. One extra key in an existing module. |

**No §6.5 escalation is raised.** The POML's two escalation triggers were both evaluated with evidence:
the ADR-040 trigger **fired** and is answered by rejecting the payload-borne carrier (§3); the ADR-015
trigger did **not** fire, because the durable datum is a digest and needs no governed store.

---

## 9. Files changed

| File | Change |
|---|---|
| `src/client/shared/Spaarke.Compose.Components/src/widgets/hooks/redlineProposalBaseline.ts` | rewritten: two tiers, `compareProposalBaseline` replaces `readProposalBaseline`, `fingerprintParagraph` added |
| `…/src/widgets/hooks/usePendingRedline.ts` | `MaterializeOrigin`; the four-case gate; `reason` + nullable `proposedAgainst` on `PendingRedlineStaleTarget`; batch reason promotion |
| `…/src/widgets/ComposeEditor.tsx` | `ComposeDraftProvenance.origin?` + type re-export |
| `…/src/widgets/ComposeWorkspace.tsx` | `origin` threaded from the two ledger-materialize legs; modal copy branches on `reason`; `When suggested` line omitted when `proposedAgainst === null` |
| `…/src/widgets/hooks/index.ts`, `…/src/index.ts` | export `MaterializeOrigin` |
| `…/src/widgets/hooks/usePendingRedline.staleDurability.test.tsx` | **new** — 19 tests |
| `…/src/widgets/hooks/usePendingRedline.deterministicOutcomes.test.tsx` | every materialize now declares `origin` (live for the proposal, replay for the re-materialize) |

`src/server/**`, `tests/**/*.cs`, `infra/dataverse/**` and `.claude/**`: **untouched**.

### Proposed `.claude/CHANGELOG.md` entry (main session to apply — sub-agents cannot write `.claude/`)

```
### 2026-08-26 — spaarkeai-compose-r8 task 052b (stale-target detection durability)
- Compose FR-C05: the stale-target QUESTION is now durable beyond the originating tab. Detection is
  discriminated by a new `ComposeDraftProvenance.origin` (`'live' | 'replay'`, fail-closed to
  `'replay'`), so a replayed suggestion with no capture-time record ASKS before it places instead of
  silently overwriting. `redlineProposalBaseline` gained an origin-scoped `localStorage` fingerprint
  tier (ADR-015: a digest, never paragraph text); the per-tab text tier is retained for the
  "when suggested" quote only. A payload-borne carrier was measured and REJECTED: at ~300 short
  clauses it tips a whole-document revise past the ADR-040 128 KB cap, and `ProjectComposeOutputs`
  skips truncated entries entirely. No ADR text changed.
```

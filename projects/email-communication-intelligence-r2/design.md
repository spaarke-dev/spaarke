# Email Communication Intelligence — R2 — Design Charter

> **Status**: DRAFT — for owner review before `/design-to-spec` → `/project-pipeline`.
> **Date**: 2026-08-03
> **Builds on**: `email-communication-intelligence-r1` (shipped, deployed to `spaarke-bff-dev`, merged to master). R2 extends the R1 Association Engine + capture pipeline — it does not re-architect them.
> **Seed inputs**: competitive analysis of iManage / NetDocuments / Worldox / ZERO email filing (`projects/email-communication-intelligence-r1/notes/competitive-analysis-email-filing.pdf`); the R1 deferred/enhancement backlog; and two fresh code audits (deduplication; capture vs user-upload surfaces).

---

## 0. Thesis — what R2 is, in one paragraph

R1 taught Spaarke to **understand** an email (associate it to the right record, triage it, propose record updates). R2 hardens the layer beneath that: **trusted capture — every email matched to the right record *once*, filed through *one* intelligent path whether it arrives by mailbox or by hand, with a modern tamper-evident tracking mechanism and a learning loop so untagged external mail keeps getting easier to match.** Concretely: a signed tracking token to replace the "luggage tag," per-matter addresses for external systems, a unified drag-to-file path that runs the same engine as capture, real de-duplication across every source, and a confirmation-driven learning loop. It closes the four competitive gaps R1 left open and fixes the two structural gaps the audits surfaced.

**R2 is NOT:** the docketing / email→dated-obligations flagship (parked — see §7, recommended as a standalone R3), or a re-architecture (every item extends the R1 engine, capture path, Outlook add-in, or shared component library). *(R2 DOES now include a focused UI pillar — **Pillar E** — to surface the intelligence R1 left dark; this supersedes r5's deferred proposal-surface scope and is built in r5's shared component library. See §2 Pillar E + §8.)*

---

## 1. Why now — what R1 left on the table

### 1.1 Competitive gaps (from the analysis)
Four cheap-to-close gaps vs iManage / NetDocuments, plus one opportunity:
- **Per-matter file-to address** — not parsed today (the "workspace email address" pattern).
- **Cross-mailbox / cross-user de-duplication** — per-mailbox only today.
- **Engine suggestions surfaced inside Outlook** — the add-in files but shows no predictions.
- **(Opportunity) a modern, signed "luggage tag"** — tamper-evident thread tracking with no subject pollution.

### 1.2 Two structural findings from the code audit (these shape the whole project)

**Finding A — the two-path problem.** Spaarke has *two* email-handling paths that do not cross-check and behave differently:
| Path | Trigger | Creates | Runs the engine? |
|---|---|---|---|
| **Capture** | Graph webhook on a monitored mailbox | `sprk_communication` | ✅ association + triage |
| **User upload** | "Save to Spaarke" Outlook add-in | `sprk_document` (email archive) | ❌ no association, no triage |
There is **no single "Spaarke intake folder"** today — monitoring is per-`sprk_communicationaccount`, one configurable folder each, default Inbox. The intake-folder mental model ("drag here and it gets processed") is a *target*, not a shipped capability, and the only user-initiated path (Save-to-Spaarke) bypasses the intelligence engine entirely.

**Finding B — de-duplication is genuinely absent, from three sources.**
- Capture dedups on `sprk_graphmessageid` (per-mailbox), **not** `sprk_internetmessageid` — the same message in two monitored mailboxes → two rows. `sprk_internetmessageid` is stored but unused for dedup; no Dataverse alternate/unique key exists.
- User upload uses a **user-scoped** idempotency key — two different users saving the same email produce two rows **by design** (test-pinned: `Scenario_TwoUsersSaveSameEmail_BothSavesSucceed`).
- SPE file upload has **no content dedup at all** — every upload = new SPE item + new `sprk_document`; no hash column. A separate *pre-spec* project (`sdap-file-duplication-detector-r1`) documented this gap and proposed a content-hash design but **built nothing**.

---

## 2. Proposed scope — four pillars

### Pillar A — Trusted threading & external matching
- **A1 · Signed tracking token — body-embedded primary (owner steer, 2026-08-03).** A cryptographically signed (HMAC, Key Vault secret) token carrying `recordType|recordId|tenantId|issued`, delivered through a **layered set of channels, primary first**:
  1. **Explicit disclosure footer (PRIMARY).** A **transparent, human-readable** reference line injected by Spaarke's send path **and** the Outlook add-in at compose — generic wording: *"This message is tracked for document management. Ref: MATTER-12345 · `<signed-token>`."* Being explicit (not hidden) is deliberate: it dissolves the DLP / anti-spam / "hidden-content" risks that would dog a stealth token, and reads as a normal professional footer (like a confidentiality notice). The human-readable reference triangulates; the embedded **signed** token makes a *present-and-intact* tag tamper-evident. Read from the **captured message body — including quoted reply/forward history** — so **zero M365 configuration**, works **user-to-user** (A5). Owner-preferred; sidesteps tenant-wide plus-addressing entirely. **Enablement + the exact footer wording are a customer-editable Dataverse configuration** (on/off + message template; ADR-018-style, no redeploy — owner Q4, 2026-08-04): the firm controls whether tracking is on and what the message says (or a compliance-approved variant), and can disable it entirely.
  2. **Hidden `X-Spaarke-Regarding` header (secondary).** Invisible, machine-clean; survives intra-tenant hops, dies on external round-trips — belt-and-suspenders for internal mail.
  3. **Reply-to / per-matter address (optional, A2).** Only where a dedicated intake address is genuinely wanted — via a **targeted mail-flow rule**, not tenant-wide plus-addressing.
  4. **Subject-line token (deferred, opt-in last resort).** Only for hostile transport that also strips/trims bodies.
  Consumed by a new **`TrackingTokenRung`** as **one triangulation input, never load-bearing** (owner: tampering "is not substantive or material to the subject matter"). A *present-and-signature-valid* token is a high-confidence, auto-file-eligible signal (a forger can't rewrite it to point at another record); a bare or edited reference with no valid signature is a **medium corroborating signal** the ladder cross-checks; a deleted one simply degrades to the other rungs. A forward carrying a prior token surfaces that record as a candidate; if other signals disagree it resolves to **Ambiguous** (the ladder refuses to guess), so a mis-forward never silently misfiles.
- **A2 · Recipient-alias rung — mail-flow-rule delivery (owner steer).** Parse To/Cc/**Bcc** for a per-record address (`matter-12345@`) → `ExplicitReference`-tier match — the deterministic answer for **external systems you can configure** (client billing, court e-filing, the iManage workspace-address pattern). Deliver via a **targeted Exchange mail-flow / transport rule** routing the pattern to the intake mailbox, **not** a tenant-wide plus-addressing toggle — surgical, minimizes the change on an enterprise client's M365, and keeps the routing criteria-configurable. *Caveat:* transport rules run at mail-flow time, **before** Spaarke's ladder, so the ladder can't drive the rule; what *is* criteria-driven is Spaarke's own token/alias recognizer and the ladder's per-channel confidence handling. Also model Bcc, which the envelope does not carry today.
- **A3 · External-reply self-association (formalize + test).** External replies preserve `In-Reply-To`/`References` even when they strip our X-header, so they already self-associate via `ThreadContinuityRung`. Formalize this as a first-class guarantee with regression coverage — it is *why* the token only has to originate once.
- **A4 · Confirmation learning loop — deterministic now, ML later.** Every human confirmation records a **sender↔record / pattern↔record affinity** as a simple per-tenant **frequency table** (explainable, no training infrastructure) — surfaced as a prediction rung (*"you've filed 8 emails from this sender to Matter X"*). It is the honest answer to matching cold inbound we can neither tag nor configure. This deterministic table is *also the training corpus* for a future ML ranker (see §3.4) that generalizes to never-seen sender/record combinations — deferred until the corpus and the deterministic-resolution metric justify it. UX is identical either way (suggest → human confirm); only the inference behind it changes.
- **A5 · User-to-user & externally-originated mail (coverage model).** The token is stamped whenever an email passes a **Spaarke-aware surface** — our send path, or the Outlook add-in at compose (the "file as I send" moment, when the user picks the matter). Email **between individual users with no Spaarke touch has no token — and that is expected and at parity with every DMS** (iManage's luggage tag is likewise only added at "Send and File"). Those are handled by the rest of the ladder: participant correlation, subject/body reference extraction, the recipient-alias (A2), thread ancestry (A3 — an external reply to *anything* we sent self-associates), and the learning loop (A4). The token makes the threads we *do* touch bulletproof; the ladder + learning make the untouched ones progressively easier. Because A1's primary channel is the **body line**, the token also survives into quoted reply/forward history that comes back to a monitored user mailbox — so even user-to-user threads get tagged the moment one participant files-as-they-send once.

### Pillar B — Unified filing surfaces (support existing email usage)
- **B0 · Realign the Outlook add-in (prerequisite).** The add-in was migrated to ADR-028 / `@spaarke/auth` (NAA via `OfficeNaaStrategy`) on **2026-07-14** — the code is *not* on a dead auth pattern; that migration closed the "broken by May/June Auth v2" window. Remaining work to make it production-current: **(a)** verify/provision the add-in's **Entra NAA app registration** (`ADDIN_CLIENT_ID`, SPA `brk-multihub://` redirect, pre-authorized to the BFF scope) — the most probable *runtime* failure, and it's config not code; **(b)** finish the **half-migrated Word manifest** (still legacy XML with a hardcoded SWA origin; the Outlook unified manifest is fine); **(c)** route the few remaining JSON BFF calls through **`authenticatedFetch`** (they hand-roll Bearer headers → miss 401-retry); **(d)** cosmetic (dead scope args, stale build/version). Realignment + a config check, **not a rebuild**.
- **B1 · Real Spaarke intake folder — *both* mechanisms (owner decision).** A shared "Spaarke" mailbox+folder **and** the add-in's drag target. Either way, dropping an email pushes it into the **full intelligence pipeline** (association + triage + provenance), not just document archive — so email not sent to a monitored mailbox still gets processed.
- **B2 · Drag-to-matter add-in.** Extend the Outlook add-in with drag-to-matter / "File to…" that opens the matter picker **pre-selected with the engine's predicted record** (reuse `derivePrimaryReview`), and finish the stubbed one-click ribbon quick-save (GitHub #234). Turns the add-in into Browse-and-File + Predictive Filing, engine-backed.
- **B3 · Unify the user-upload path with capture.** Route "Save to Spaarke" through the **same engine** as capture so a hand-filed email gets association, triage, and dedup identically. Resolves Finding A; prerequisite for B1/B2 to behave consistently.

### Pillar C — De-duplication (match once)
**Covers BOTH layers (owner requirement).** The *email/message* layer — one `sprk_communication` per internet-message-id (C1/C2/C4) — **and** the *file/attachment* layer — one canonical `sprk_document` per content hash (C3). A single email can duplicate at either layer independently (the same message re-ingested by several mailboxes/users; the same attachment arriving via different emails), so both are explicitly in scope and are reconciled with each other (C4).
- **C1 · Canonical internet-message-id key + structural enforcement.** Make `sprk_internetmessageid` the dedup key for communications, enforced by a **Dataverse alternate (unique) key** so duplicates are structurally impossible (race-proof), spanning **both** capture and user-upload. Message-id-based Service Bus idempotency.
- **C2 · Context-merge, not drop.** When the same message hits N mailboxes / M users, keep one communication and record the delivery/recipient/uploader context on it — dedupe the entity, preserve the facts.
- **C3 · SPE content dedup — absorb `sdap-file-duplication-detector-r1`.** Adopt that project's owner-locked (2026-07-14) but **code-free** design wholesale: an indexed **`sprk_document.sprk_canonicalhash`** (Dataverse = the cross-container authority key) computed with **`quickXorHash`** (the only content hash SPE reliably returns; `sha256Hash` is deprecated — "don't use"), with **two-tier detection**: Tier 1 exact hash-equality (ideally *before* the write), Tier 2 near-duplicate reusing the existing `documentVector3072` cosine-KNN "Find Similar" engine at a duplicate-tuned threshold. On a hit: **notify, never silent** (open canonical / proceed hash-linked; the copy graduates to its own indexed document once its content diverges). Hook at every upload path — the three that project mapped (Assistant upload, Assistant persist, Compose save) **plus the email-attachment path** R2 adds (`IncomingCommunicationProcessor` / `OfficeDocumentPersistence`). **Budget its two load-bearing spikes**: (1) is `quickXorHash` present + stable *immediately* post-upload (incl. >250 MB chunked uploads, arbitrary binary) — decides gate-before-vs-after-write; (2) Tier-2 threshold calibration + `documentVector3072` coverage robustness. That project is **file/attachment dedup only** — R2's C1/C2/C4 add the email-*message* layer (internet-message-id) on top. **R2 ships message-level dedup + Tier-1 (exact hash) now; Tier-2 (near-dup) is a validated fast-follow** (owner Q2, 2026-08-04) — Tier-2's false-positive risk (nagging users about legitimately-different files) earns its way in via spike 2, not on faith.
- **C4 · Cross-path reconciliation.** A captured `sprk_communication` and a user-saved `sprk_document` archive of the same email must reconcile (same internet-message-id) so we don't hold two representations of one email in two stores.

### Pillar D — R1 carry-overs (cheap, high-value; fold in)
- **D1 · Fix FR-06 RAG grounding (currently inert).** Both inbound and outbound RAG-indexing call sites pass `ParentEntity: null`, so matter-scoped grounding returns **zero results in practice**. Tag communications with their regarding at index time (+ decide a backfill). Small change, unlocks a shipped-but-dark R1 feature.
- **D2 · Batched identifier query (NFR-08).** `IdentifierReverseLookupRung` issues ≈175 queries/message (25 tokens × 7 types); a batched `In`-filter query drops it to ≤7. Optimization only.
- **D3 · Golden end-to-end regression tests.** Pin the R1 UAT misfile emails (PAT-942665 / PAT-942404 / REAL-2026-123456.02 + `Invoice-10044725.pdf`) that root-caused two distinct bugs. (Was flagged for R1 task 090.)
- **D4 · Job B allow-list starter seed.** `sprk_emailupdatefield` ships empty → Job B can propose nothing until seeded.
- **D5 · Job C apply endpoint (in scope — backs Pillar E's E3).** Job C create-task proposals land as `Proposed` rows with no confirm/apply surface; this apply endpoint is the backend for the proposed-action card (E3). Resolves the original Q3 narrow question: **yes, build it.**

### Pillar E — Surface the intelligence (make it visible + actionable) — added 2026-08-04 (owner Option A)
**The gap this closes.** R1 built the extraction backend — Job B proposed field-updates, Job C proposed tasks/deadlines, triage — with a ranked queue-feed (`GET /api/communications/queue-feed`) + apply endpoints, but **no UI renders any of it**. Today only ASSOCIATION is surfaced (the "Related Records" modal); TRIAGE is written-but-dark; Job B/C proposals are stored-but-unsurfaced. r5 (built before R1's feed existed) explicitly deferred these (its states D/E/F + Exceptions Queue). **R2 takes them over** — otherwise the whole R1+R2 investment feeds a queue nobody can see. This turns R2 from backend-only into backend **+ a focused UI pillar**; it supersedes r5's deferred surface scope (§8).
- **E1 · Triage display.** Render category / priority / summary / RI-confidence / review-outcome on the email surface (r5's `EmailWorkspace`, via `Spaarke.Communication.Components`).
- **E2 · Proposed-update card (r5 state E).** Render Job B proposed field-updates from the queue-feed — old→new, cited, confidence — with confirm → `POST /api/communications/proposals/{id}/apply` (the shipped apply endpoint).
- **E3 · Proposed-action / deadline card (r5 state F).** Render Job C create-task/deadline proposals; confirm → the Job C apply endpoint (D5).
- **E4 · Exceptions Queue (C-3 / Concept 1).** The ranked review "inbox" of associations + proposals needing a human, consuming the queue-feed. Dual-use (SpaarkeAi widget + code-page), like the r5 EmailWorkspace.
- **E5 · New-vs-related intent card (r5 state D).** Render R1's `NewRecordIntentDetector` output (Create-new-and-link / File-onto-X / Link-as-related) — completes FR-12's rendering half.
- **Built in the shared `Spaarke.Communication.Components` library (r5's component home) and mounted in r5's `EmailWorkspace` shell — no fork.** R2 owns states D/E/F + the Exceptions Queue; r5 keeps the workspace shell + association review + reading pane. Requires the r5 coordination-contract update + `/conflict-check` before every shared-lib PR (§8).

---

## 3. Deep-dive — the review questions (design detail)

### 3.1 A modern luggage tag, and matching external-originated mail
**Two axes.** *Delivery* — how the signed token physically rides the mail: **body reference line (primary)** → hidden `X-Spaarke-Regarding` header → mail-flow-rule per-matter address → deferred subject token. *Trust ladder* — the order the engine believes a match:
1. **Signed token (A1)** — highest trust; stamped on anything we send or file-as-we-send.
2. **Recipient alias (A2)** — deterministic; external systems we point at a `matter-*@` address (mail-flow rule).
3. **Thread-header ancestry (A3)** — an external reply to anything we've sent self-associates; already works.
4. **Prediction + learning loop (A4)** — cold inbound; high recall, human-confirmed, self-improving.
The token needs to originate **once** (our send, or the add-in at compose); the **body-line delivery means it survives into quoted reply/forward history with zero M365 configuration**, and layers 2–4 carry everything the token never touched. No single mechanism is load-bearing; pure user-to-user mail with no Spaarke touch is handled by 2–4, at DMS parity (A5).

### 3.2 Drag-to-file without taxonomy drift
The linked-folder trap is that a folder becomes an *input* to filing, forking a second taxonomy that drifts. R2 keeps the folder as a **processing trigger** (one intake folder, no destination encoded) and makes destination an **explicit user pick** (drag-to-matter, engine-pre-selected) — both feed the one engine, so there is nothing to drift. Optional: project filed status back into Outlook as a category, read-only, downstream of the association.

### 3.3 Dedup, done once
Canonical key = internet-message-id, enforced structurally (alternate key), across capture + upload, context-merged not dropped; SPE files deduped by content hash; the two stores reconciled. Near-duplicates with *different* message-ids (forwards, resends) are **linked, not merged** — they are genuinely distinct communications.

### 3.4 The learning loop — deterministic now, ML later (what the ML would do)
**Now (R2, deterministic).** A per-tenant affinity table counts confirmed associations by signal: sender→record, sender-domain→record, subject-keyword→record, participant-set→record. On new untagged mail it surfaces the highest-affinity record as a candidate with an explainable reason ("8 prior confirmations from this sender to this matter"). It is a lookup + counters — no model, no training job, fully auditable — and it captures the common legal case where the same counsel, clients, and contacts recur constantly.

**Future (ML).** When the deterministic table plateaus (it only knows exact senders/keywords it has already seen), a model generalizes to combinations it has *never* seen — learning that, say, mail from a firm domain, citing a docket-number format, with a certain attachment shape, tends to belong to a client's litigation matters. This is precisely what NetDocuments' "web-scale predictive filing" is under the hood: a ranker/classifier (or a semantic embedding match — we already have the `documentVector3072` infra) trained on filing history. We defer it because (a) it needs a labeled corpus, which the deterministic table is *accumulating for us* — so deterministic-now is the on-ramp, not throwaway; (b) it must preserve our "refuse to guess + show provenance + human-confirm" guarantees, which are harder to keep honest with a black box; (c) the deterministic-resolution-rate metric will tell us whether it's even worth building. The UX never changes — both just produce "suggested record, confirm?".

---

## 4. ADR tensions (surface at design time per CLAUDE.md §6.5)
- **ADR-028 (auth).** The signed token needs an HMAC secret in Key Vault + rotation story; reply-to subaddressing needs a mail-topology decision (plus-addressing / catch-all). Likely Path A exceptions, owner-approved.
- **ADR-024 / ADR-045 (regarding; extend never fork).** New rungs (tracking-token, recipient-alias, affinity) are additive to the existing ladder — the pattern R1 already used. No fork.
- **ADR-010 / ADR-032 (DI minimalism; unconditional registration).** New rungs registered unconditionally; feature-gate via config/kill-switch, not conditional DI.
- **ADR-039 (closed catalog / code-directed).** Node engine stays frozen; all new capability is code-directed rungs + config.
- **SPE dedup ownership.** `sdap-file-duplication-detector-r1` is **absorbed** (§5) — R2 owns the `sprk_canonicalhash` column + detector; close the standalone project to avoid double-building.

---

## 5. Review decisions + remaining gates

**Resolved in owner review (2026-08-03):**
- **Token delivery → body-embedded signed reference line is PRIMARY** — zero M365 config, works user-to-user, survives quoted reply/forward history. **Plus-addressing is NOT required.** Mail-flow rules are used **only** where a per-matter intake address is genuinely wanted (A2), kept surgical + criteria-configurable. Subject token deferred (opt-in last resort). [A1 / A2 / §3.1]
- **User-to-user / externally-originated mail** → token stamped at any Spaarke-aware surface (send or add-in compose); untouched mail handled by the ladder + learning loop, at DMS parity. [A5]
- **Intake mechanism** → build **both** (shared mailbox+folder *and* add-in drag target). [B1/B2]
- **SPE dedup** → **absorb** `sdap-file-duplication-detector-r1`; Pillar C covers **both** the message layer and the file/attachment layer. [C3 / Pillar C]
- **Learning loop** → **ship deterministic affinity now; defer ML.** [A4 / §3.4]
- **Outlook add-in** → **realign + verify** (already ADR-028-migrated 2026-07-14); not a rebuild. [B0]
- **Dedup merge policy** → hard-merge byte-identical messages; **link** (not merge) near-duplicates. [C1 / §3.3]

**No blocking design gates remain — ready for `/design-to-spec`.**

**Deployment / runbook inputs (not design blockers):**
- Per-client Exchange admin adds a **mail-flow rule** only if that client uses per-matter intake addresses (A2) — surgical, not tenant-wide.
- **Verify/provision the add-in's Entra NAA app registration** — the most probable reason the add-in "doesn't work" today is this config, not the code.
- **Budget two SPE-dedup spikes** (quickXorHash post-upload timing; Tier-2 near-dup robustness) before building Pillar C's Tier-1/Tier-2.

---

## 6. Success criteria (measurable — for the spec)
- **Zero** duplicate `sprk_communication` for one internet-message-id across N mailboxes **and** M users.
- **~100%** match rate for threads Spaarke originates (token + thread).
- Drag-to-file and capture produce **identical** engine output (association + triage + provenance) for the same email.
- **Baseline + trend** for the **deterministic-resolution rate (T0/P0)** — the novel, defensible "how much did we file with no human touch" metric the market research flagged as unclaimed.

---

## 7. Considered and deferred (looked at, scoped out — with rationale)
- **IP docketing / email → dated obligations.** The single unclaimed market white-space (was R1's removed Pillar-3). Its own large project; the competitive-validation gate (R1 D-12 / design §0.6 Q5) is still **open**. **Recommend as a standalone R3 flagship**, not folded into R2.
- **SprkChat over mail** (R1 D-11c, P2) — platform exists; revisit "only if a small add."
- **Daily Briefing 7th triage channel** (R1 G-6, P2) — follows an existing pattern; low urgency.
- **M365 group-mailbox capture** (R1 051b, descoped) — needs a *forked* capture pipeline + tenant-wide `Group.Read.All`; re-check only if Graph offers scoped app access. Backlog.
- **Policy-based auto-apply of the high-confidence band** (R1 D-5.5 P2) — R1 is human-confirm-everything; auto-apply the confident tail is a deliberate later step.
- **RecordNameMatch / ContactNameMatch surface-only question** (R1 open) — round-3 partially subsumed it (all non-core entities are candidate-only); the rung-level principle can wait.
- **Dual regarding-map consolidation, thread-recurrence weighting, RI-confidence weight tuning** — small R1 debts; fold opportunistically or leave to a cleanup pass.
- **r5 net-new surfaces** (new-vs-related card, proposed-update/action cards, Exceptions Queue) — r5 scope; R2 supplies the feeds.

---

## 8. Dependencies & coordination
- **r5** (`email-communication-solution-r5`) — owns the `EmailWorkspace` shell + association review + reading pane (all built). **R2 SUPERSEDES r5's deferred surface scope** — states D/E/F proposal cards + the Exceptions Queue (Pillar E): R2 builds them in the shared `Spaarke.Communication.Components` library and mounts them in r5's shell. **Coordination actions (binding):** (1) update the r1↔r5 coordination contract at `projects/email-communication-solution-r5/notes/email-intelligence-r1-coordination.md` to record R2 ownership of D/E/F + Exceptions Queue; (2) `/conflict-check` before **every** shared-lib PR — r5 is an active worktree (`C:/code_files/spaarke-wt-email-communication-solution-r5`). R2 also feeds r5 drag-to-matter suggestions + dedup-aware lists.
- **`sdap-file-duplication-detector-r1`** — **absorbed into R2 Pillar C** (§5, resolved): its owner-locked but code-free design is lifted, its two spikes inherited. The standalone project can be closed/folded once R2's spec incorporates it.
- **"augment global Document search"** project — populates `sprk_globalsearchextender`, which lights up F1 content-based attachment matching (R1 carry-over 2.4).
- **Hot paths** (root CLAUDE.md §10): shared `Services/Communication/**`, `Services/Office/**`, SPE (`SpeFileStore`), and the Outlook add-in. `parallel-safe:false` on shared writers; `/conflict-check` before every BFF PR. Publish-size ceiling ≤60 MB.

---

## 9. Housekeeping — close R1 first
R1 is substantively done (rounds 1/2/2b/3 shipped, deployed, merged) but its deploy/UAT/wrap tasks (060/061/090) are formally unmarked and `TASK-INDEX` shows 013 as 🔲 though its note records it complete (7 rows seeded). Recommend a short R1 close-out (reconcile 013, run 090 `/test-diet`, pin the golden regression tests as D3) so R2 starts from a clean, closed baseline.

---

## 10. Next steps
1. **Owner review of this charter** — confirm the four-pillar scope, resolve the §5 gates (especially mail topology), decide the IP-docketing-as-R3 recommendation and the SPE-dedup ownership.
2. `/design-to-spec` — turn the confirmed scope into `spec.md` (FRs/NFRs, acceptance criteria, ADR-tensions section).
3. `/project-pipeline` — scaffold the worktree, plan, and task POMLs.

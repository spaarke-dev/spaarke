# Email-to-Record Matching: Approaches and Technologies

**Purpose:** Guidance for the email-communication-intelligence project on matching incoming emails and attachments to Spaarke records (Matters, Projects, Invoices, and related entities). Positions this as a **matching ladder** — deterministic first, statistical/probabilistic second, embedding-based third, generative AI last and only for residue behind a confirmation gate. Consistent with D-F0 doctrine: the fact layer must be deterministic; AI is legitimate only in the interpretation/residue layer, never silently overriding a structural signal.

**Status:** Guidance for Phase 0 discovery. All component names below are `[PROPOSED]` until verified against the actual email-communication-intelligence codebase.

---

## 1. The matching ladder

Each incoming email (and each attachment) should pass through tiers in order. A tier only fires if the prior tier fails to produce a confident result. Every tier's outcome — match, no-match, or confidence score — is logged, regardless of whether it was acted on.

| Tier | Technique | Fires when | Output |
|---|---|---|---|
| **1. Structural / deterministic** | Exact key lookup | Matter/project/invoice number present in subject, body, or Dataverse-known headers; existing thread already linked (Message-ID / In-Reply-To / References) | Auto-link, full confidence, no AI involved |
| **2. Registry / rule-based** | Known-party lookup | Sender/recipient domain or address matches a registered counterparty, attorney, or client contact already associated with a record | Auto-link or high-confidence suggestion |
| **3. Statistical record linkage** | Probabilistic matching (fuzzy name/entity matching, weighted multi-signal scoring) | No exact key, but partial signals exist (participant overlap, name similarity, date proximity to matter activity, attachment metadata) | Confidence score → auto-link, suggest, or queue depending on threshold |
| **4. Semantic / embedding retrieval** | Vector similarity against record corpus | No structural or fuzzy match, but content is semantically related to an existing matter/project (subject matter, parties named in body/attachment text) | Ranked candidate list, presented as suggestion — never auto-linked |
| **5. Generative assist** | LLM reasoning over ambiguous residue | Tiers 1–4 produce no confident candidate, or multiple plausible candidates with no way to disambiguate deterministically | Candidate + rationale, always behind a one-click confirmation gate in the triage queue |

**Key principle:** Tier 5 should be the smallest tier by volume. If generative calls are resolving a large share of matches, that's a signal the ladder is missing a technique between tiers 2 and 4, not a reason to trust tier 5 more.

---

## 2. Technologies per tier

### Tier 1 — Structural
- Dataverse lookups on known identifiers (matter number, project code, invoice number) via regex extraction from subject/body
- Email threading via standard headers (`Message-ID`, `In-Reply-To`, `References`) — RFC 5322 native, no ML
- Inheritance: if a prior message in the thread is already linked to a record, propagate the link
- Existing `IEmailFilterService` rule infrastructure is a natural home for this tier — currently doing noise filtering, extendable to identifier extraction

### Tier 2 — Registry / rule-based
- A **party/participant registry**: known email addresses and domains mapped to counterparties, outside counsel, and client contacts, each linked to the records they're associated with
- This is where an explicit **knowledge graph** earns its place — rather than a flat lookup table, a graph of `Person ↔ Organization ↔ Matter/Project ↔ Invoice` relationships lets tier 2 resolve not just "this address belongs to counterparty X" but "counterparty X is currently active on three matters — which one." This is the natural extension point for your two-sided data position: the graph is what makes cross-referencing law firm and client-side participants tractable.

### Tier 3 — Statistical record linkage
- Classical entity resolution (Fellegi-Sunter–style probabilistic linkage, or a lighter weighted-scoring model): combine multiple weak signals — domain partial match, name similarity (Jaro-Winkler/Levenshtein), temporal proximity, attachment type/filename patterns, participant overlap with existing matter teams — into a single confidence score
- This is a genuine gap area: it requires either a small trained classifier (logistic regression or gradient-boosted tree over the signal features) or a hand-tuned weighted rule set as a first pass. A trained model needs a labeled dataset — see feedback loop, Section 4.
- This tier is cheap, fast, fully auditable (every signal and weight is inspectable), and should absorb most of what currently likely falls through to generative assist

### Tier 4 — Semantic/embedding retrieval
- Already have the infrastructure: `text-embedding-3-large` + Azure AI Search hybrid search, currently used for RAG content search
- Extension: embed record-level content (matter description, project scope, invoice line items) alongside email content, and match on vector similarity when structural/statistical signals are absent
- This is retrieval, not generation — output is a ranked candidate list with similarity scores, not a written answer

### Tier 5 — Generative assist
- Existing `AnalysisOrchestrationService` / `OpenAiClient` pattern applies here, scoped narrowly: given the residue case and the best candidates surfaced by tiers 3–4, produce a short rationale for the triage reviewer, never an auto-committed link
- Should always cite which record(s) it's considering and why, so the confirmation-gate reviewer sees the reasoning, not just a bare suggestion

---

## 3. Confidence and policy envelope

Borrow the green/yellow/red pattern already established for Legal Front Door:

- **Green** — Tier 1–2 result, or Tier 3 score above a high threshold: auto-link, logged, no human step
- **Yellow** — Tier 3–4 result in a mid-confidence band, or Tier 5 suggestion: surfaced in triage queue, one-click confirm
- **Red** — No tier produces a usable candidate: routed to manual triage as unmatched, never silently dropped

Thresholds should be tunable per record type (Matters, Projects, Invoices likely warrant different confidence bars given different downstream risk of a wrong link).

---

## 4. Feedback loop (what makes Tier 3 improve over time)

Every triage confirmation or rejection is a labeled training example. Logging these systematically (which tier proposed the match, what signals fired, what the reviewer decided) builds the dataset that lets Tier 3 move from hand-tuned weights to a trained classifier, and lets Tier 4/5 thresholds be recalibrated against real precision/recall rather than intuition. This should be designed in from the start, not retrofitted — it's a natural extension of the immutable audit log discipline already in place for Email Triage.

---

## 5. Governance requirements

- **Eval harness before rollout**: precision/recall against a golden, labeled set of historical email-to-record links, broken out by tier — not just an aggregate accuracy number
- **No silent overrides**: a lower tier's confident result is never overridden by a higher-numbered tier without an explicit reason logged
- **ADR-015 compliance**: matching signals derived from email/attachment content follow existing data governance rules (minimize data sent to AI services, no raw content in logs, tenant-scoped artifacts)
- **Provenance on every link**: which tier resolved the match, what signals/score, and (for Tier 5) the rationale — carried on the record link itself, not just in a log

---

## 6. Phase 0 discovery targets for Claude Code

1. Inventory current matching logic in the email-communication-intelligence codebase and tag each component against the five-tier ladder above (`[VALIDATION NEEDED]` on all current assumptions)
2. Determine what fraction of current matches resolve at each tier today, if any tiering exists at all, versus falling straight to generative assist
3. Assess feasibility of a party/participant registry or lightweight graph structure as a near-term Tier 2 build, using existing Dataverse entities (Matter, Project, Invoice, and their party/contact relationships) as the source graph
4. Identify what historical email-to-record link data (if any) exists to seed a labeled dataset for Tier 3
5. Scope Tier 4 as an extension of the existing RAG embedding infrastructure rather than a parallel system

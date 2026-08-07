# External Assistant — Access & Permission Model (open questions for a security design spike)

> **Status**: NOT fully designed. FR-26 puts AI on the **external plane, app-only (no OBO)** — a new
> security surface. This note is the input to a **dedicated security design spike** that MUST precede the
> FR-26 build. Security-sensitive → human sign-off required (root CLAUDE.md §6).

## SCOPE NARROWED (owner, 2026-08-06) — this shrinks the spike a lot
Ask Legal does **exactly two things**: (1) **semantic search over the P&P library** (RAG), (2) **route to
a defined wizard**. **No file upload/attach affordance in the chat box at all**; no summarize/analyze; no
data queries beyond P&P; file analysis only inside wizards. Consequences that de-risk the model:
- **Tool catalog = exactly 2 fixed tools** (`policy_search`, `launch_wizard`) — no file-ingest tool, no
  data tools. The "many tools, per-tool authz" problem collapses to two well-known tools.
- **Grounding = P&P library only** (published/effective policies — largely org-wide-readable content),
  NOT matters/requests/documents/other-users' data. The RAG security-trim surface shrinks to
  "published + not-restricted P&P," a much smaller problem than per-matter confidential trim.
- **Zero file-ingest surface in the assistant** → the entire "app-only analyze an uploaded file" risk is
  removed from Ask Legal (it lives only in wizards, which have their own scoped flow).
- **Legal-advice guardrail becomes largely structural** — the assistant only *retrieves existing policy
  text* + *routes to services*; it does not generate novel legal opinions. "Search + route, not advise."

**Remaining spike surface (now small)**: (a) P&P RAG entitlement/status trim (published, not-restricted,
effective-dated); (b) prove the 2-tool catalog can't be escaped via injection (no path to other
tools/grounding); (c) `launch_wizard` routing is safe (can only launch defined wizards); (d) audit. The
broad concerns below are superseded by this narrowing except where they map to (a)-(d).

## The core hazard: app-only = the backend sees everything
Because the external assistant runs **app-only** (broker-only, no OBO, no user token downstream), the
backend has FULL data access. Therefore **every retrieval and every tool call must be EXPLICITLY
re-scoped to the caller's Tier-1 (module/role entitlement) + Tier-2 (record scope)**. A single
scope-filtering omission = a cross-user / cross-matter data leak. This is the opposite of the OBO model,
where the user's own token bounds access. The whole design burden is "explicitly re-scope everything."

## What is settled (enforcement seams that exist)
- **Closed tool catalog** (ADR-039 `AgentToolCatalogProjector`) — external assistant gets only a
  whitelisted subset (Q&A + submit-request); config-driven by capability set + `sprk_requiredcapability`.
- **Persona system-prompt** (`sprk_aipersona`) + **knowledge-grounding scope** (`ChatKnowledgeScope`).
- **Scope**: workforce-internal only (R2); a NEW app-only, entitlement-scoped chat endpoint (the core
  `/api/ai/chat` is OBO/core-user — unusable here).

## Open questions the spike MUST resolve
1. **RAG grounding security-trim (highest risk)** — how is retrieval filtered per caller so the
   assistant NEVER grounds on content outside the caller's Tier-1/Tier-2 scope? Options: index
   partitioning, per-chunk metadata ACL (entitlement + `sprk_policy` status/effective-date), query-time
   filter injection. Must be verified with negative tests (ask about another matter/user's content →
   no grounded leakage).
2. **Per-tool Tier-2 authz** — each tool the assistant can invoke ("my requests", "policy lookup",
   "submit request") enforces `requester==caller` / entitlement ITSELF (app-only has no user token to
   inherit). Define the per-tool authz contract.
3. **Prompt-injection / jailbreak boundary** — the hard limit is SERVER-SIDE (tool catalog + grounding
   filter + per-tool authz), never the system prompt. Threat-model a hostile user.
4. **Legal-advice guardrail (liability)** — "don't give legal advice" as a prompt is soft. Decide:
   allowed-topic scoping, mandatory disclaimers, refuse-and-route-to-human for defined asks, and logging.
   Product/legal decision, not just technical.
5. **Auditability** — log external-assistant interactions (asks, answers, tools invoked, grounding
   sources) for compliance.
6. **Plane differences** — workforce (R2) vs the deferred outside-counsel (CIAM) exposure: different
   entitlements + grounding + possibly different guardrails.

## Recommended NFR (add to spec)
**NFR-EXT-AI**: the external assistant MUST NOT ground on, or return, any content outside the caller's
Tier-1 (entitlement) + Tier-2 (record) scope; enforcement is **server-side** (tool catalog + grounding
security-trim + per-tool authz), NOT prompt-based; a negative "no cross-scope leakage" test is required
per tool + for grounding; the legal-advice guardrail is enforced beyond the system prompt.

## Task implication
Add a **P5 security design spike — "External Assistant Access & Permission Model"** that produces the
threat model + enforcement design (grounding trim, per-tool authz, injection boundary, legal-advice
guardrail, audit) and **gates the FR-26 build task**. Security-sensitive → human sign-off (§6). Ties to
the FR-26 production task and the R2 Tier-1/Tier-2 primitives (`CallerPrincipalResolver`).

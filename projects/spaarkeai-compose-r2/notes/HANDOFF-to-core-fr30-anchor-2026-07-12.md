# HANDOFF → core (redesign-r2): FR-30 workspace-scope memory anchor

> **From**: spaarkeai-compose-r2 · **To**: spaarke-ai-architecture-redesign-r2 (core) · **Date**: 2026-07-12
> **Re**: your question "what stable identifier should we key the MemoryItem to for the unbound home-workspace file-upload case?"
> **Status**: our recommendation — you own the final call since you're building the facade + scope.

---

## Headline recommendation

**Anchor Compose document-insights to the `sprk_document` (existing Record scope), promoted at create-on-save. You likely do NOT need a separate workspace memory scope for this (the file-insight) path.**

This is exactly the simplification you floated in your Q2 ("if they only persist post-save keyed to the resulting `sprk_document`, that simplifies to record-scope-on-the-document"). We're endorsing it, and the evidence below supports it.

---

## Answers to your four questions

### Q1 — Durable anchor
**The `sprk_document` id.** It is the only server-durable, governance-addressable, stable-across-leave/return identity.
- **NOT** the chat/session id — it's a client-localStorage value (`AiSessionProvider`, namespaced per host context via `chatSessionKeyForContext`), per-device, not server-durable; and "conversation" is an **explicitly rejected** memory scope (ADR-042 §1; a write to `conversation` scope throws — `MemoryItem.cs:170-175`).
- **NOT** the SPE drive-item id — it also only exists post-save (create-on-save mints it), and it's an infrastructure handle, not the canonical record.
- **Pre-save there is no durable anchor — by design** (see Q2).

### Q2 — Transient vs. saved
A home-uploaded file is **transient**: bytes live in **Redis via `ITenantCache`** (`doc-upload-binary`, **4-hour TTL**, `ChatDocumentEndpoints.cs:42, 441-443`); there is **no `sprk_document` and no SPE item at upload time**. Both are minted only at **create-on-save** (`ComposeService.SaveAsync` → `PromoteIfEphemeralAsync`, `ComposeService.cs:318-322, 362-371`), which also rebinds the ChatSession's DocumentId from SPE id → new `sprk_documentid` (FR-07, `ComposeService.cs:509-511`).

**Recommendation:** capture insights into the **session ledger/context during the transient window (ephemeral)**, and **promote them to durable Record-scoped memory keyed to the new `sprk_document` AT create-on-save** — the same moment that already rebinds the session. If the user never saves, insights stay ephemeral and expire with the session. That's acceptable: a never-saved transient file has no durable home to anchor to.

### Q3 — Recall trigger + lifetime
- **Recall by FILE** (the `sprk_document`) via **Record scope** — not "everything in the workspace" (that conflates unrelated documents opened in the same workspace).
- **Lifetime: durable, user-owned, deletable per governance (Tier-3), living with the document.** Pre-save insights are session-lifetime only.

### Q4 — Record-bound case
**Anchor document-insights to the `sprk_document` in BOTH bound and unbound cases.** In a matter-bound workspace the uploaded file still becomes a `sprk_document` (with the matter as its regarding/parent); the insight is *about the document*, so it anchors to the document. Cross-document *matter-level* insights are a separate matter-scope concern.

So **workspace scope is not needed for Compose document-insights in either case** — Record-scope-on-the-document covers both. Reserve a workspace scope (if you build one at all) for insights that are about the *workspace itself*, which is a different use case than file/document insights.

---

## The hook point (where to attach promotion)

`ComposeService.SaveAsync` → `PromoteIfEphemeralAsync` (`ComposeService.cs:362-371`) is where the `sprk_document` is minted and the session rebinds. That is the natural place to promote ephemeral Compose insights into Record-scoped memory keyed to the resulting `sprk_document`.

**compose-r2 is already touching this create-on-save path** for the active-document-session fixes (UAT round-3). We will **expose this promotion point cleanly** so your capture facade slots in when you resume. Coordinate on `ChatSession` + `ChatDocumentEndpoints` + the create-on-save seam (shared files) — we land the UAT fixes first; you resume after.

---

## One decision left to you

If you want insights durable for home-uploaded files that are **never saved**, you'd need a durable pre-save anchor (a durable `workspaceId` / upload id). We **recommend against** it: it persists memory about never-committed content (clutter + governance risk on the very content the untrusted-origin gate was meant to guard). The create-on-save promotion keeps durable memory tied to content the user actually kept. If product genuinely needs pre-save durability, let's discuss a bounded-TTL transient-memory tier rather than durable user memory.

---

## Evidence base (compose-r2 read-only investigations, 2026-07-12)
- Memory scoping: Record `(entityType,entityId)` + User `(systemuserid)` only; conversation/session/document/tab are NOT scopes. `MemoryItemStore.cs:185-210`, `MemoryItem.cs:162-176`, ADR-042 §1/§6.
- Transient storage + create-on-save promotion: `ChatDocumentEndpoints.cs:42/346/441-443`, `ComposeEndpoints.cs` (`/api/compose/upload` Redis read), `ComposeService.cs:284/318-322/362-371/509-511`.
- Two-session model (per-tab document session owns the ledger; memory does not switch on tab change): `ChatSession.cs:252-266` (`ActiveDocumentIdentity.DocumentSessionId`, DEF-11).
- FR-30 status: compose task 063 `blocked-on-core-A0`; #629 (dispatched-action capture path + untrusted-origin gate). Full memory diagnosis in `notes/uat-round3-reuat-diagnosis.md` sibling investigations.

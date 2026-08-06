# FR-A3 — External-reply self-association: first-class guarantee (task 015)

> **Task**: 015 · **Date**: 2026-08-05 · **Rigor**: STANDARD (test-modifying → Step 9.5 gates) · **Status**: ✅
> **Production code changed**: NONE (formalization + regression only — ADR-045 scope constraint).

## The guarantee (formalized)

> **An external reply to any email Spaarke sent self-associates back to the parent's regarding — via the
> standard RFC-2822 `In-Reply-To` / `References` ancestry — even when every Spaarke-proprietary header
> (`X-Spaarke-*`) has been stripped in transit.**

This is a *first-class* guarantee, not an incidental behavior: correspondence threading must survive external
mail systems that rewrite/drop custom headers, because the reply usually comes from a party outside the tenant
(opposing counsel, client, vendor) whose mail server owes Spaarke nothing.

## Why it holds by construction

`ThreadContinuityRung.EvaluateAsync` (rung 1) reads **only** the normalized envelope's `InReplyTo` then
`References` (newest→oldest), looks up the nearest ancestor that already exists as a `sprk_communication`
(`GetCommunicationByInternetMessageIdAsync` → `GetCommunicationByGraphMessageIdAsync`), and copies that
parent's regarding across all `RegardingFieldMap.AllRegardingFields`. It **never** reads a custom header — so
stripping `X-Spaarke-*` cannot break self-association. Inheritance strength: **1.0 (auto-file)** from a
**Resolved** parent; **0.65 (suggest-band)** from an unconfirmed parent (P3 misfile guard, FR-12 UAT 2026-07-30)
— an unconfirmed parent's weak association is never amplified into an auto-file across the thread.

## Regression (CI-guarded)

`tests/integration/seam/Communication/ThreadSelfAssociationRegressionTests.cs` (ADR-038 seam KEEP path):
1. **In-Reply-To path** — external reply, custom headers stripped, `In-Reply-To` → Spaarke-sent Resolved
   parent regarding matter M ⇒ inherits M at confidence **1.0** via `RungKind.ThreadContinuity`.
2. **References-only path** — In-Reply-To absent (some systems drop it), `References` chain → the parent ⇒
   still inherits M (nearest ancestor wins).

Complements the rung-mechanics unit tests (`ThreadContinuityRungTests`) by pinning the end-to-end FR-A3
*guarantee* framing under the KEEP path.

## Coordination

Folds into the **FR-D3 golden regression suite** (task 032, Phase 3): task 032 should **absorb / co-locate**
this FR-A3 case with the golden misfile-email suite (fixtures pinned in `notes/fixtures/r1-golden-emails.md`)
— one home, not two. Run `/conflict-check` before any PR touching the shared seam suite.

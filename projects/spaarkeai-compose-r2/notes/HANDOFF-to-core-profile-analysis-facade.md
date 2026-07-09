# HANDOFF → redesign-r2 (core): profile-analysis facade for OBO-written documents (compose-r2 Fork C)

> **From**: spaarkeai-compose-r2 (Ralph Schroeder) · **To**: spaarke-ai-architecture-redesign-r2 (platform core) · **Date**: 2026-07-09
> **Re**: create-on-save (FR-05/task 013) needs a document-profile-analysis seam that is (a) ADR-013 facade-clean and (b) OBO-safe. This is core `Services/Ai/PublicContracts/` surface, not compose surface — routing to you per the AI-execution-layer ownership model (same class as the ContextEnvelope operand-home hand-off).

## TL;DR
Compose's create-on-save persists a user-authored `.docx` to SPE **under OBO** (the user's identity holds the file ACL), then must run **document profile analysis** on it. The only existing profile seam is `IAppOnlyAnalysisService` (`Services/Ai/`, app-only/MI). Two blockers make it unusable from `Services/Compose`:
1. **ADR-013 / NFR-05 facade rule** — `IAppOnlyAnalysisService` is an AI-internal namespace type; injecting it into `Services/Compose` trips the Tier-1 NetArchTest facade rule.
2. **MI-403** — it runs app-only/Managed-Identity, but MI is **not on the OBO-written Compose file's SPE ACL** → it will 403 reading the file (the same writer-identity constraint that forces indexing to run sync-OBO).

## What compose-r2 did in the interim (task 013, Option 1)
Per owner decision (2026-07-09), task 013 ships the create-on-save backbone **container → record → indexing** now, and treats **profile-analysis as a NON-BLOCKING `deferred` job step**: it emits a `deferred` state (via `JobAwareCompletionStateProjector`, which already names the four steps `container → record → profile-analysis → indexing`) and does **not** implement profile. Interim R5-E bar: a record with no SPE file OR no index is never success; a stored+created+indexed record with profile=`deferred` is interim success. **The full R5-E bar (incl. profile) is restored when core ships the facade below.**

## What core is asked to own
A thin, ADR-013-clean, **OBO-safe** profile-analysis facade in `Services/Ai/PublicContracts/`:
- **Proposed shape**: `IDocumentProfileAi` wrapping the existing `AnalyzeDocumentFromStreamAsync` (stream overload) so the caller passes the bytes it already holds under OBO — sidesteps the MI-403 (no second SPE read under MI).
- **Consumed by**: compose create-on-save (task 013) as its profile step — but it is a **general** capability (any OBO-written document ingestion path can use it), which is why it belongs in core's PublicContracts, not compose.
- **Open verification for core**: confirm the MI-vs-OBO read-access assumption — i.e. that the stream-overload path (caller-supplied bytes) avoids the MI ACL problem, OR that an OBO-token analysis entry point is added. This is the one empirical check that gates the shape.

## Coordination
- No compose code blocks on this — 013 degrades gracefully (profile `deferred`) and back-fills when the facade lands (via the deferred step + a re-profile pass).
- When core ships `IDocumentProfileAi`, compose flips task 013's profile step from `deferred` to a real enqueue (small follow-on) and restores the full R5-E bar.
- Suggest this rides the same Phase-E cadence as the other AI-execution-layer seams (ADR-043 + ContextEnvelope operand-home). If it's better modeled as a job (app-only) with an OBO pre-read, that's core's call — the requirement is only "profile an OBO-written doc without 403 and without tripping the compose facade rule."

*Contact: Ralph Schroeder. Source of truth: task 013 `<rescope>` block + notes/defer-issues.md "Open architectural escalations".*

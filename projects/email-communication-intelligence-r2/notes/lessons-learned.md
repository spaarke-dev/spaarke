# Lessons Learned — email-communication-intelligence-r2

**Written**: 2026-08-31 (task 090 wrap-up)
**Scope**: 5 pillars (A tracking/footer · B unified filing add-in · C dedup/context-merge · D RAG/Job-B/C · E reconciliation UI), ~38 tasks.

## Key decisions (as-built)

1. **SPE content dedup → gate-after-write** (2026-08-05, tasks 023/024). Read `quickXorHash` from the driveItem *after* upload, then reconcile + notify — **never silently suppress a document** (data-loss guard). Retired spikes 001/002 (the post-upload-timing unknown). `quickXorHash` is the identity; `sha256Hash` is deprecated on SPE.
2. **SPE Tier-2 (near-dup) deferred out of R2.** Exact-hash Tier-1 only. Near-dup is a follow-up.
3. **FR-E5 task fields → Path B ("add fields")** (task 034). Create via `IActionSeam.CreateTaskAsync`, then PATCH status/completed-date/base-date/final-due-date via impersonated `UpdateRecordAsync` under one audit row — keeps the AI facade (ADR-013) unchanged while delivering the full field set.
4. **Backfill forward-only** (D1 RAG-grounding + C3 canonicalhash). No historical reprocessing.
5. **FR-C3 graduate-on-divergence NOT literal-suppress** (plan `wild-waddling-sifakis`). Compose docs are editable — literal "suppress + return canonical" would collapse two matters' drafts and cross-wire sessions. The correct model is hash-linked-copy that graduates to its own canonical on first edit. Schema (`sprk_canonicaldocument` self-lookup) is operator-gated; **this remains an open R3 candidate** — the R2 dedup shipped is Tier-1 exact-hash on the immutable email-attachment path (suppress-forever, safe because attachments never diverge).
6. **Splits for BFF write endpoints** (055→055a/055b, 056→056b, owner-approved 2026-08-07). Override-apply, dismiss, and ad-hoc-create-task are distinct audit outcomes routed through the same allow-list/citation/impersonation guards — separating them kept each endpoint's audit semantics honest.

## Technical findings worth carrying forward

- **MDA `Xrm.WebApi` strips FetchXML paging annotations.** `retrieveMultipleRecords` respects injected `page`/`count` (paging works) but drops `@Microsoft.Dynamics.CRM.morerecords` + `fetchxmlpagingcookie` from the JS result → a `moreRecords`-only `hasMore` silently caps every MDA-hosted list at page 1 ("shows only 25"). Fix = page-fullness fallback: `hasMore = moreRecords === true || (pageSize > 0 && entities.length >= pageSize)`. **Codified as [ADR-051](../../../.claude/adr/ADR-051-infinite-scroll-lists.md)** + `.claude/patterns/ui/infinite-scroll-list.md` — infinite lazy-scroll + canonical thin scrollbar is now the repo standard; NO pagination (no numbered pages / prev-next / "Load more" / down-arrow).
- **`::-webkit-scrollbar` does not cascade.** Annotate the real `overflow:auto` element with `thinScrollbarStyle`, or use `thinScrollbarDescendantStyle` at a root. The DataGrid `gridScroll` inline copy (drift) converged onto the canonical `thinScrollbarStyle` — do not re-introduce a copy.
- **Provenance name extraction.** Flat primary-review candidates carry only `targetId` (GUID); the resolved name lives in a contributor provenance string as `name="…"`. Added `candidateDisplayName()` (`/name="([^"]*)"/`) used before the GUID fallback so cards show number+name, not a GUID.
- **2d/2e archive/attachments are DATA gaps, not code bugs.** Only 6/126 email archives carry `sprk_relatedcommunication`; the UAT'd needs-review email had 0 `sprk_communicationattachment` rows. The reconciliation query matches the working `CommunicationAttachmentsService`; an attachment-bearing email renders correctly. Root cause is ingestion coverage (project item 064), not the UI.

## Coordination / shared-surface notes

- R2 owns the **Pillar E reconciliation surface** in `Spaarke.Communication.Components` (grid + reconcile tabs/modals + browse shell + citation layer). Contended with **email-communication-solution-r5** (primary owner), **spaarke-dataset-grid-framework-r2** (DataGrid), the messaging/notification/email-r4 worktrees (`Services/Communication`), and compose-r5/r4.5 (`CitationResolver`, reused not forked per NFR-11). `/conflict-check` before every shared PR held.
- **master became protected mid-project** (ruleset `21824191`, 2026-08-29): PR required, required check = literal `Router`, force-push blocked. Classic `/branches/master/protection` returns a misleading 404 — check **rulesets**. Use `/merge-to-master` Path A (auto-merge PR). The final infinite-scroll + ADR-051 work merged via PR #911.

## For a future R3

- Implement FR-C3 graduate-on-divergence for editable Compose docs (schema `sprk_canonicaldocument` self-lookup; plan `wild-waddling-sifakis` is the design — build contract-first behind the operator-gated column).
- Close the 2d/2e ingestion gap (item 064): backfill `sprk_relatedcommunication` + attachment rows at capture, so archives/attachments render for all needs-review emails.
- Pillar B add-in live UAT (task 044) + FR-C4 near-dup Tier-2.

## §10 BFF publish-size (close-out)

Compressed publish ~**44 MB** (measured 2026-08-31, incl PDBs) — **under the ≤60 MB ceiling**, essentially flat vs the ~44.96 MB incl-PDB baseline (Δ ≈ −0.9 MB). `dotnet list package --vulnerable --include-transitive` → **no vulnerable packages**. See `test-diet-report.md` (clean diet, 0 deletes) + `drift-audit-2026-08-31.md` (clean).

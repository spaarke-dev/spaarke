# Task 054 — Citation navigation — COMPLETE (2026-08-07)

**Rigor**: FULL · opus·xhigh · directional. **Model**: Opus 4.8.
**Result**: shared-lib code + tests green (tsc 0; jest 25 suites / 183 tests, +15 for 054). Step 9.5 code-review + adr-check clean (one documented §6.5 Path-A exception). Conflict-check clean (email-communication-solution-r5 no overlap on the touched files).

## Escalation trigger #2 FIRED → owner-approved §6.5 Path A

The POML directed: *"REUSE composeCitationResolver.resolveCitation over a ParaIdMap-style map; do NOT build a second citation mechanism."* **That premise is architecturally infeasible for email citations** — verified in code before writing any 054 implementation:

1. **`composeCitationResolver` is a legal-section-number resolver.** It parses `"Section 4.2"` / `"4.2(b)(iii)"` / `"Sections 4–7"` and matches `ParaIdMapEntry.computedNumber/listPath` from a *numbered legal document* ([composeCitationResolver.ts](../../../src/client/shared/Spaarke.Compose.Components/src/widgets/composeCitationResolver.ts); COMPOSE-READ-REFERENCE-FIDELITY §4). Email proposal citations are `{CitationSource, CitationLocator, CitationQuotedText}` (QueueFeedModels.cs) — the anchor is **QuotedText** (verbatim prose, NFR-06) in **free-form** email/attachment text with no legal numbering. Any email locator/quote → `parseCitation` → `UNRECOGNIZED` → **zero matches**. Literal reuse ships a citation nav that never resolves.
2. **Compose's actual quoted-text primitives are editor-bound.** `AgreementReviewSummaryPanel.highlightCitedSpan` + `commentAnchorRange.ts` (`findCommentAnchorRange(doc: PMNode, …)`) anchor quoted text over the **ProseMirror/TipTap editor** (`@tiptap/pm/model` marks/ranges). The reconciliation reader (task 053) is not an editor — a sandboxed `.eml` iframe + sanitized-HTML div + plain-text folds. They cannot run over it.

So **neither Compose primitive can anchor an email QuotedText into this reader.** Escalated per §6.5; owner chose **Path A** (2026-08-07, AskUserQuestion — "reader-scoped quoted-text anchor").

**What shipped (the email-domain quoted-text ANALOG — NOT a fork of the legal-number resolver, NOT a second legal-citation mechanism):**
- **`logic/citations/readerReferenceMap.ts`** (NEW, pure logic, deep-importable per ADR-022's "/logic" constraint): `buildReaderReferenceMap({body, attachments})` → normalized segments (body + one per extractable attachment); `resolveQuotedCitation(citation, map)` → `{located, segmentId, kind, start, end}` or `{located:false, reason:'not-found'|'no-quoted-text'}`. First-occurrence, markup/whitespace-normalized, source-scoped; **never a nearest/fuzzy guess**. 11 unit tests.
- **`components/EmailBody/HighlightedText.tsx`** (NEW): renders a normalized segment with a resolved `[start,end)` span in an ephemeral Fluent-token `<mark>` + `scrollIntoView` (ADR-021 light/dark). Exact span (offsets from the resolver over the SAME normalized text) — no re-search.
- **`EmailBodyView` `activeCitation` prop** (EXTENDED, additive): resolves the citation over its own reader text and renders — a **cited-passage callout** for a body quote (the `.eml`/HTML body can't be injected in place, NFR-03), an **inline fold highlight** for an attachment quote, or a **"source not locatable"** note for a forged/absent quote (no navigation).
- **`ReconciliationBrowseShell.activeCitation`** (forwarded): a reconcile tab (055/056) sets it when the reviewer clicks a proposal citation.

## Acceptance criteria — inversion note

The POML's *"No second citation mechanism (grep-asserted: composeCitationResolver is the only resolver invoked)"* and *"single-ref / sub-locator / range parity with Compose"* criteria assumed the legal-number resolver was the right tool. Under Path A they **invert**: `composeCitationResolver` is deliberately NOT invoked (grep-asserted absent — it's the wrong domain), and single/sub/range are legal-number shapes with no email quoted-text analog. The email invariant that holds: **ONE email-domain quoted-text anchor** (`readerReferenceMap`), no duplicate mechanism.

## Coordination (NFR-11) — for spaarkeai-compose

The two shared layers now each own their citation domain: **Compose** = legal-section-number resolution over a numbered `.docx` (`composeCitationResolver`); **Communication** = quoted-text-span anchoring over free-form email/attachment prose (`readerReferenceMap`). These are genuinely different problems; neither forks the other. **Coordination item for a future convergence (not blocking):** if a shared, non-editor-bound quoted-text primitive is ever wanted (Path B), extract one both packages reuse — raise with spaarkeai-compose / -fidelity owners. Filed as a coordination note (see notes/defer-issues.md).

## Reachability caveat (documented, operator-relevant)
An archived `.eml` body renders in a `sandbox=""` iframe (NFR-03) the parent cannot reach — a body-sourced citation there is shown as the exact normalized passage in the cited-passage callout (not an in-iframe highlight). Attachment folds + the fallback body highlight exactly. Worth a browser pass at Pillar E deploy (059).

## Downstream
055 (Fields) + 056 (Tasks) proposal citations click through to this: set `ReconciliationBrowseShell.activeCitation` from the clicked proposal's `{source, locator, quotedText}`.

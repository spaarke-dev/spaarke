# ADR-044: Dataverse GUID Canonicalization at System Boundaries

> **Status**: Accepted
> **Date**: 2026-07-10
> **Domain**: Dataverse Access / Client-Server Contracts / Data Integrity
> **Concise version**: [`.claude/adr/ADR-044-dataverse-guid-canonicalization.md`](../../.claude/adr/ADR-044-dataverse-guid-canonicalization.md)
> **Supersedes / relates to**: reinforces ADR-007 (facade handles lookup complexity); relates to ADR-034 (identity normalization). Codifies FAILURE-MODES **AP-3** and **AP-6**.

---

## Context

Dataverse and the Xrm client runtime represent the same GUID in more than one lexical form:

- **Xrm registry format** — brace-wrapped and frequently UPPERCASE: `{6CEDD99B-30DA-F011-8406-7CED8D1DC988}`. Returned by `Xrm.Utility.lookupObjects` (the native lookup picker), `Xrm.Utility.getGlobalContext().userSettings.userId`, `Xrm.Page.data.entity.getId()`, and (in some cases) `Xrm.WebApi.createRecord`.
- **Web API canonical format** — bare and lowercase: `6cedd99b-30da-f011-8406-7ced8d1dc988`. Returned by `Xrm.WebApi.retrieveMultipleRecords` attribute reads and by the BFF Web API client.

These forms are semantically identical (a GUID is case-insensitive and brace-optional), but several downstream consumers are **intolerant** of the non-canonical form:

1. **OData `@odata.bind` key predicate.** A create/update payload that binds a lookup via `"<NavProp>@odata.bind": "/entityset(<guid>)"` requires a **bare** GUID. A braced value (`/entityset({GUID})`) makes the reference URL unparseable and Dataverse returns HTTP **400 "Bad Request - Error in query syntax."** The error names no property — a tell that it is a URL-parse failure, not payload-field validation.
2. **Azure AI Search `Edm.String eq` filters.** Equality on `Edm.String` is **case-sensitive**. A document indexed with an UPPERCASE `documentId` is missed by a lowercase-GUID lookup and vice-versa.

This has produced **two distinct production failures with the same root cause**:

- **AP-3 (2026-05-22)** — the Find-Similar feature indexed some document chunks with UPPERCASE IDs (from the `Xrm.Page`/`getId()` ribbon path) and others lowercase (from the Web API wizard path); case-sensitive AI Search `eq` missed half the data. Fixed in `fbbaee29` by normalizing at the BFF indexing convergence point + the ribbon.
- **AP-6 (2026-07-09)** — Create Matter/Project failed at `createRecord` with "Error in query syntax" because lookup GUIDs sourced from the native picker (`DataverseLookupField.openLookup` → `Xrm.Utility.lookupObjects`) were interpolated **raw** into `@odata.bind`. Five of seven `Create*Wizard` services and two shared services had the raw pattern; only Invoice/ReportCard had a local cleaner — so normalization was scattered and Matter shipped broken. Fixed in PR #603 with a single canonical `cleanGuid` applied at every bind site and at the Xrm adapter boundaries; exported from the package barrel in PR #609.

Because normalization was previously scattered (five duplicate `_cleanGuid`/inline copies existed) and undocumented as a rule, the same class of bug recurred. This ADR elevates the prevention — already stated informally in AP-3's "Prevention" note ("normalize at every boundary") — to a binding, enforceable decision with a single shared implementation.

## Decision

**All Dataverse GUIDs MUST be canonicalized to bare, lowercase form at every boundary where they cross a system.** The canonical client normalizer is **`cleanGuid`**, exported from `@spaarke/ui-components` (`PolymorphicResolverService`). The BFF normalizes at its single indexing convergence point.

`cleanGuid` strips braces + surrounding whitespace and lowercases; it is a **no-op on already-bare GUIDs**, so it is always safe to apply uniformly and carries no semantic risk.

Normalization is applied at three boundary classes:

1. **Egress to Dataverse** — before any `@odata.bind` value or `/entityset(guid)` reference URL.
2. **Egress to / ingress from AI Search** — before writing a document key or building an `Edm.String eq` filter.
3. **Ingress from Xrm** — the moment an Xrm-sourced GUID enters component/service state (pickers, `userSettings.userId`, created-record ids), so a braced value never propagates. This is realized in shared code at `xrmNavigationServiceAdapter.openLookup`, `xrmDataServiceAdapter.createRecord`, and `DataverseLookupField.onChange`.

## Constraints

See the [concise ADR](../../.claude/adr/ADR-044-dataverse-guid-canonicalization.md) for the enumerated MUST / MUST NOT list. Summary:

- **MUST** normalize before `@odata.bind`, before AI Search keys/filters, and at Xrm ingestion.
- **MUST** use the shared `cleanGuid` (client) / normalize at the single convergence point (BFF).
- **MUST** normalize at the boundary you own — never trust the caller.
- **MUST NOT** hand-roll per-file normalizers; **MUST NOT** interpolate a raw GUID into a key predicate; **MUST NOT** rely on Dataverse/AI-Search tolerating braces or case.

## Alternatives Considered

1. **Do nothing / fix per-site as bugs surface** (status quo before this ADR). Rejected — it is exactly what allowed AP-6 to recur after AP-3; scattered fixes miss sites.
2. **Normalize only at bind sites (no ingestion normalization).** Rejected as insufficient on its own — braced GUIDs then live in component state and can leak into other consumers (display, comparison, secondary writes). Ingestion + bind-site (belt-and-suspenders) is the chosen posture.
3. **Branded ID types (TS) / `record struct DocumentId(Guid)` (C#).** A stronger, compiler-enforced guarantee. Not adopted now due to the breadth of change (every GUID is currently a `string`), but recorded as the preferred long-term direction; this ADR is compatible with a future migration to it.
4. **Configure AI Search with a case-insensitive normalizer.** Addresses only the AP-3 symptom, not `@odata.bind`; a field-level normalizer also cannot be added to an existing field without a reindex (see FAILURE-MODES G-4). Rejected as a general solution.

## Consequences

- New client code that builds binds or AI Search keys routes GUIDs through `cleanGuid` — cheap and no-op on clean input.
- The five previously-duplicated normalizers are consolidated into one; `code-review`/`adr-check` can now cite ADR-044 when flagging a raw GUID in an `@odata.bind`.
- Solution-local code that builds its own binds outside the shared services (e.g. SmartTodo's `DataverseService`, EventDetailSidePane) remains responsible for normalizing at its own GUID source. SmartTodo already complies via `getUserId()`; EventDetailSidePane is a known audit target.
- No behavioral change to already-bare paths (no-op), so adoption is risk-free.

## References

- Concise: [`.claude/adr/ADR-044-dataverse-guid-canonicalization.md`](../../.claude/adr/ADR-044-dataverse-guid-canonicalization.md)
- Pattern: [`.claude/patterns/dataverse/relationship-navigation.md`](../../.claude/patterns/dataverse/relationship-navigation.md)
- Failure modes: [`.claude/FAILURE-MODES.md`](../../.claude/FAILURE-MODES.md) — AP-3 (case), AP-6 (braces)
- Code: `src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts` (`cleanGuid`); `src/server/api/Sprk.Bff.Api/Services/Ai/FileIndexingService.cs` (BFF normalization)
- Commits/PRs: `fbbaee29` (AP-3 fix), PR #603 / `d2696b616` (AP-6 fix), PR #609 (barrel export)

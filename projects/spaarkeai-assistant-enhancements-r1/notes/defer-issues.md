# Deferrals & Issues — Assistant Enhancements R1

> Source of truth for deferred work + issues. Each entry names a **concrete failing behavior/contract**
> (CLAUDE.md §11). `push-to-github` Step 1.6 blocks push on entries without a GitHub Issue URL — file via
> `/project-defer-issue-tracking` (`/defer`) at push time.

| ID | Title | Type | Origin | GitHub Issue |
|----|-------|------|--------|--------------|
| D-032-01 | FR-E5 BU/team enrichment in the User fragment | Deferral | task 032 (owner sign-off 2026-07-15) | {URL} |
| D-043-01 | No client-accessible preference source for the SNS chip reorder | Gap | task 043 (FR-G1) | {URL} |

---

## D-032-01 — FR-E5 role/BU/team enrichment (BU/team portion)

- **Decision**: Descoped from R1 by owner sign-off (2026-07-15). Recorded in `notes/032-budget-and-caching-decision.md` §4.
- **What ships in R1**: FR-E5's acceptance criterion — *"role reflected in the profile fragment"* — is **met**: task 030 renders the stated `sprk_primaryrole` label in the stated-profile block. Only the **BU/team** enrichment is deferred.
- **Concrete behavior deferred**: the assistant's one biased turn does NOT carry the caller's **business unit / team** membership. A user whose org-unit context would sharpen the assistant's framing gets no BU/team signal in the User fragment.
- **Why deferred** (not dropped):
  1. The carried-forward note named the wrong service — `IMembershipResolverService` resolves **record-membership** (which `sprk_matter`/document/event rows a user is on, keyed by target entityType), **not** org BU/team. BU/team live on `systemuser.businessunitid` + `teammembership`, carried by `IdentityNormalizationService.PersonIdentity`.
  2. It is **organizational scope** — the design explicitly defers `IOrganizationalContextProvider` and treats the org graph as "reference, not copy"; folding BU/team into the *User* slice back-doors deferred org-scope.
  3. It adds a second per-turn hot-path read for unproven bias value.
- **Cheapest in-boundary path when picked up**: render `- Business Unit: …` / `- Team: …` from the **already-cached** `IdentityNormalizationService.PersonIdentity` (no new Dataverse read), included in the byte-stable render + the (now 700) User budget. This is a small, self-contained follow-up — not a new pipeline.
- **Trigger to revisit**: owner wants org-unit-aware assistant framing, OR the Organizational slice / Work IQ seam gets wired (natural home for BU/team).

## D-043-01 — No client-accessible preference source for the SNS chip reorder

- **Decision**: the FR-G1 reorder mechanism shipped (task 043) but its preference input is **not yet wired** — surfaced, not fabricated (per the task's anti-guessing instruction).
- **Concrete behavior deferred**: the Suggested-Next-Steps chip reorder (`reorderChipsForDisplay` in `chipDisplayOrder.ts`) is deterministic + preference-keyed + ADR-039-clean, but **no client-accessible source of the user's chip-ordering preference exists**, so every call site passes none and the reorder deterministically falls back to the server-declared (`sprk_chiptransitions`) order. A user whose stated/learned preference should re-rank their suggestions gets the default order.
- **Why deferred**: the stated profile (`sprk_userprofile`) is read **server-side only** (`StatedProfileReader` → `ContextBinder.userFragment` as LLM prompt text); there is no GET endpoint / session-bootstrap field / SSE frame projecting it to the browser, and there is no structured "chip-ordering preference" field in the User Model yet (only free-text `sprk_assistantpreferences`).
- **What it needs when picked up** (two parts, both small, no sort-mechanics change): (1) a **structured chip-order preference** in the User Model (a `preferredBindingOrder`-shaped signal, stated via the questionnaire and/or learned via the "shape suggestions over time" capability, spec §5); (2) a **client projection** of it — either a session-bootstrap field or a client-side Dataverse read of `sprk_userprofile` — passed into `useConsumerChips` as `chipDisplayPreference`. The comparator already accepts it verbatim.
- **Trigger to revisit**: task 042 (My Assistant questionnaire) or the preference-update capability (spec §5) lands, OR a session-bootstrap profile projection is added. Natural pairing with 042.

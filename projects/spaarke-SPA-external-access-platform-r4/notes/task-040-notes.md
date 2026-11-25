# Task 040: record tabs for workforce contacts with grants (FR-24, #2508)

Status: implemented and verified; UAC review recorded (no objection). FULL rigor, opus @ high, directional steps.

## Behaviour

A workforce contact that can read at least one record sees the Projects, Matters, Work Assignments, Documents and Invoices tabs, and each lists only the records it may read. A workforce contact with no grants sees none of them (owner 2026-11-09: hidden, not shown empty). CIAM partners and licensed systemusers are unchanged.

## Signal (POML step 2)

Server-side, on the principal the request already composed. No new read, endpoint, route, `/me` field, DI registration or package.

- `ModuleEntitlementResolver.IsGrantedWorkforceContact(principal)` is true when all three hold: the plane is Workforce, the caller is a contact principal (no systemuser), and it has a readable project, matter or work assignment. "Readable" means Read-bearing entries only; None-only and Write-only entries don't count.
- When it holds, `/api/v1/external/me/entitlements` adds `assigned-work` (const `AssignedWorkModule`) to the workforce codes. This happens in the home, no-roles and roles branches alike.
- `assigned-work` is stripped (case-insensitive) from `sprk_approlemodulemap` extras on the workforce plane. A licensed systemuser therefore never gets the tabs, whatever the map holds.
- `MeEntitlementsEndpoint` still intersects the codes with the Workforce-role ceiling. `ModuleTables["assigned-work"]` is `sprk_workassignment`, so a role without Read on that table drops the code.
- The rule covers home and non-home workforce contacts, since both are contact principals. This scope is part of the UAC review below.
- SPA: the five widgets share `RECORD_TAB_GATE` in `widgetRegistry.ts`: planes `ciam` and `workforce`, `requiredEntitlement: 'assigned-work'`, and default on both planes. Billing Guidelines stays CIAM-only.
- CIAM is unchanged. The server reports `assigned-work` to every CIAM caller, and so do `me-client.ts`'s CIAM fallback and its dev persona.
- The client gate is UX only. Record scope is enforced server-side.

## Step 4: the lists are Tier-2-scoped (verified, no escalation)

All five tabs read through module fetch descriptors in `Infrastructure/DI/ExternalAccessModule.cs`: `collaboration`, `matters`, `work-assignments`, `documents` and `invoices`.
- None of them is plane-gated.
- Each is scoped from the principal's composed sets (`AccessibleRecordIds` / `ScopeDimensions`).
- Each is ceiling-checked per table on fetch.

So a workforce contact sees only its granted records, and escalation trigger 1 did not fire.

## K2 and K3

- **K2 (two unread counts; tid-keyed feed marker and identity cache): known limit, unchanged.** The record tabs read neither.
- **K3 (a licensed user linked to a WorkforceHome contact's (tid, oid) resolves as the systemuser): known limit, filed D-40 #3154.** With this task that person gets no record tabs, which is the licensed model. A fix would change principal precedence, which is escalation trigger 3 (auth plane) and outside this task.

## Review findings and outcomes

| Source | Finding | Class | Outcome |
|---|---|---|---|
| code-review | Resolver XML doc still described `assigned-work` as CIAM-only | F4 | Fixed `704fa72396` |
| adversarial 1 | Comments made wrong by the change: `InvoicesWidget.tsx` (firms "none for a workforce caller"; in fact only a systemuser is refused), the `ResolveAsync` summary, and the registry section header | F2 | Fixed `f6575abf70`; behaviour unchanged and consistent (a workforce contact's firms come from its memberships) |
| code-review, adversarial 1 | Ceiling maps one table (`sprk_workassignment`) to five tabs. A role with WA Read but no invoice Read shows an Invoices tab whose fetch is refused, so the tab looks empty; a role without WA Read hides all five even for a project-only grant. The owner prerequisite role has Read on all five. | K4 | uac-r2 chose a known limit; filed D-39 #3153 (per-tab codes) |
| K3 revisit | A licensed user linked to a WorkforceHome contact gets none of the contact's grants or tabs | K | uac-r2 agreed; filed D-40 #3154 |
| code-review | Tabs opened before a grant revocation stay until `/me` is read again | K3 | Known limit, UX only; lists stay server-scoped |
| adversarial 1, code-review | Notes and gate evidence missing | F4 | This file |
| adversarial 2 | **The Invoices tab's "Submit invoice" command can answer 403 for a workforce contact.** The command (and the record view's Submit invoice) is offered when `/me` `firms` is non-empty. `ExternalUserContextEndpoint.cs:143` gives firms to every contact and refuses only a systemuser, and `firms` ignores the Workforce-role ceiling. The submit routes, however, enforce the ceiling (`WorkforceRoleCeilingGate.cs:214-217, 264-266`). So a granted workforce contact on a role without Create on the submission table, or without AppendTo on documents, is offered a command that is then refused. Before this task the Invoices tab was CIAM-only. ebilling never decided whether a workforce contact may submit (its code only refuses systemusers; its comment assumed workforce callers had no firms). | F1 | **Owner decision pending** (see below) |
| adversarial 2 | Everything else is clean: no way to get `assigned-work` without the rule, no list outside Tier-2, no route gated on the code, CIAM unchanged; mutation check passed (removing each guard fails a test) | — | — |

## UAC review (unified-access-control-r2)

Requested 2026-11-25 against `f6575abf70`. It covers:
- home vs non-home scope;
- the ceiling mapping, with two options: (a) a known limit for R4, filed; (b) map the code to all five tables;
- K3;
- the role-map strip.

**Reply from uac-r2, 2026-11-25, at `f6575abf70`: no access objection.** It read the resolver, `MeEntitlementsEndpoint` and `ExternalAccessEndpoints` diff, plus the principal's derived views in `CallerPrincipalResolver` (L195-250).
- **The rule is sound.** Its quote: `IsGrantedWorkforceContact` "reads only the Read-bearing views", and those drop entries without Read; "a vetoed record (No Access, the Restricted contact veto) is not in the map at all". So "the code can't be raised by a no-level grant, an organization-only reach to a secure root, or a vetoed record." The rule adds no read, the tab gate is UX only, and every list stays Tier-2 scoped.
- **Scope.** "home AND non-home is right … Don't narrow it to home."
- **Ceiling: option (a).** Accept it as a known limit for R4 and file an issue. Its reasons:
  - each tab's fetch is still ceiling-checked per table, so the defect is UX only and needs a misconfigured role;
  - (b) is worse, because it "would hide a project-only contact's Projects tab under a role missing one unrelated table";
  - the later fix is "per-tab codes, each ceiling-mapped to its own table".
  
  `WorkforceRoleCeilingGate` is uac-r2's file. Filed as **D-39 #3153**.
- **uac-r2's ask: confirm how a tab shows a ceiling 403.** Traced in `gridDataverseClient` → `BffDataverseClient.mapErrorResponse` → shared `DataGrid`. The ceiling refuses the row fetch. `useLazyLoad` keeps that error, but `DataGrid` never renders it, so the tab shows the empty state "No records to display". That is an empty state, not an error banner, which is what uac-r2 asked for. The trace also shows a config-load failure would show "This list could not be loaded. Try again later." No test covers a 403 on a grid widget. A "no access" rendering is listed as optional in D-39.
- **K3: agreed it is a known limit.** Filed as **D-40 #3154**, cross-referencing ADR-028 A9.
- **No other objections.** The role-map strip is right ("a role row can't hand a licensed user the tabs"). Ignoring the roles claim for home is unchanged behaviour. CIAM is unchanged. The log line carries only a bool.

## Gate evidence

- **BFF publish size:** fresh short-path worktrees, `dotnet publish -c Release`, `Compress-Archive`. Master `a1b7db1def` gave 43,603,145 B; the branch `72e7bbcba2` gave 43,603,141 B. Both had 194 files. Delta −4 B (comment-level change).
- **CVE:** `dotnet list package --vulnerable --include-transitive` found no vulnerable packages on either side.
- **Conflict check (2026-11-25):** none of the 41 open PRs touch the changed files. #1494 is on the external-access surface, but in a different file (`GrantExpiryReminderJob.cs`).
- **BFF tests:**
  - `WorkforceContactRecordTabsTests`, `ModuleEntitlementResolverTests`, `MeEntitlements*` and `ExternalIntakeEndpointsTests`: 128 passed, 0 failed.
  - Adversarial pass 1 re-ran 116/116.
  - Build green.
- **SPA:**
  - `WorkforceContactTabs.test.tsx` 9/9. `tsc --noEmit` and eslint clean.
  - Full suite 2,123/2,124. The one failure was a 60 s timeout in `RecordTemplateSubmitInvoice.test.tsx` under machine load; re-run alone it passed 52/52.
  - In pass 1's run, `ProjectPage.perRecordWrite.test.tsx` also timed out; re-run alone it passed 2/2.
  - Neither file imports the registry.

## Test scope

- **`tests/integration/auth/ExternalAccess/WorkforceContactRecordTabsTests.cs`** (KEEP path, auth). It goes through the real route, handler, resolver rule and ceiling intersection, with doubles at module boundaries only. Cases:
  - must-fire: a granted contact, one case per record type;
  - must-not-fire: no grants; a systemuser with grants; a ceiling without WA Read;
  - CIAM unchanged, with and without grants.
- **`ModuleEntitlementResolverTests.cs`** (grandfathered file) holds the rule's branches:
  - a home contact with a grant gets the code, a home systemuser doesn't;
  - a role-map row naming the code is ignored, for a systemuser and for a contact;
  - a granted contact with roles gets each code once;
  - entries without Read don't count.

  These are contract negatives for the strip and Read rules.
- **`src/client/external-spa/tests/WorkforceContactTabs.test.tsx`** checks:
  - the registry's defaults and library for each caller shape;
  - the tab strip rendered from `/me`, with only `fetchMeEntitlements` and the widget bodies substituted, as in `PortalChrome.test.tsx`.
- **Must-fire evidence on dev:** not run. It needs a BFF and SPA dev deploy (owner go per SHA; uac-r2 deploys the BFF). The POML's ui-tests are proposed with that deploy.

## Rework

None so far.

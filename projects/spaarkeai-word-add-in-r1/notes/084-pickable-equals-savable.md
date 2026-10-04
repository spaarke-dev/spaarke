# Task 084: pickable equals savable (#1037, #1075)

> **Date**: 2026-10-01 · **Rigor**: FULL (opus / high) · **Mode**: directional
> **Owner decision** (2026-09-30, go given 2026-10-01): show a record the caller cannot file to **disabled, with the
> reason**; do not hide it.

## 1. The defect, and what the research changed

- The picker's search returns every record the caller can **read**. Task 062 impersonates the caller, so Dataverse
  trims inside the query.
- `POST /api/office/save` files only to a record the caller holds **AppendTo** on: `EntityAccessFilter` →
  `CallerRecordAccessProbe` → `OperationAccessPolicy` `entity.associate_document`.
- So a user with Read but not AppendTo could pick a record and then have the save refuse it with 403 `OFFICE_009`.

Research corrected ISS-010 in two places:
- `POST /office/todo` demands **Read**, not AppendTo, so the To Do flow is out of scope.
- **Three** places offer a filing target, not one: the Save tab's search rows, Outlook's suggestion cards, and the
  ribbon quick-save (which files straight to the prediction).

**#1075 (ISS-014)** was folded in because it changes the same ribbon path. The quick-save sent the logical name
`sprk_matter`, and the save accepts only friendly names, so every predicted quick-save got 400 `OFFICE_002`.

## 2. Design: one evaluator, opt-in, fail-closed

| Piece | What |
|---|---|
| **The evaluator** | `OfficeSearchService.EvaluateFilingAccessAsync(targets, bearer)`, the one composition of the save's three pieces: `EntityAccessFilter.TryResolveEntitySet` (the one type → set map), `CallerRecordAccessProbe` (OBO `RetrievePrincipalAccess`, as the caller), and `OperationAccessPolicy.HasRequiredRights(…, EntityAccessFilter.AssociateOperation)` (`private const` → `internal`). There is no `AccessRights.AppendTo` literal, operation string or type map anywhere new |
| **The verdict's input** | The row's FRIENDLY type (`EntitySearchResult.EntityType`), the exact string the pane sends to the save. A type the save refuses (`Account`), or an empty id, is `false` without asking |
| **Search** | `GET /api/office/search/entities?…&access=file` → per-row `canFile: bool?`. **Opt-in**: without it (the To Do assignee search, every other caller) there is no rights call and `canFile` is absent. Any other `access` value is ignored |
| **Suggestions** | `CommunicationSuggestionsResponse.FilingAccess` (`targetId` → bool), computed only for **named** candidates. An unnamed candidate is one the caller cannot read; the client drops it, so asking about it would only disclose |
| **Probe** | New `GetCallerRightsForRecordsAsync`: the OBO exchange and `WhoAmI` happen **once**, then `RetrievePrincipalAccess` per record, **at most 4 at once** (`MaxConcurrentRecordLookups`). The per-record answer comes from the same private steps as the single-record method; those three steps became `protected virtual` so a test can count them |
| **Bounds** | At most 50 records per request (`MaxFilingAccessChecks`); past it the answer is `null` ("not checked"). The search can never reach it (`top` ≤ 50) |
| **Failure** | A probe that cannot answer gives `AccessRights.None`, so `false`; never `true` on doubt. One row's failure affects no other row and never fails the response |
| **No new surface** | No endpoint, filter or DI registration. `/search/entities` keeps its Permanent waiver (#1021): this is a response field, not a gate. The save remains the enforcement if rights change between search and save |

**Client** (`office-addins`; implemented by a subagent, reviewed and amended here):
- **Picker:** `RelatedToPicker` renders a `canFile === false` record as a disabled card: `aria-disabled`,
  `tabIndex -1`, no handler, `InfoRegular` + the verbatim reason. This applies to both search rows and suggestion
  cards.
- **Never selected, by any path:** the picker clears a selection that the server later reports not fileable;
  `SaveFlow.handleEntitySelect` refuses one; `useSaveFlow`'s session restore drops one.
- **Wiring:** `SaveFlow` sends `access=file` and carries `canFile` through its explicit field map. The
  suggestions service maps `filingAccess` → `canFile` on the cards and on `predicted`.
- **Ribbon:**
  - It sends `target.entityType` (#1075).
  - On a prediction with `canFile === false`, it posts nothing, shows the verbatim notice and opens the pane.
  - `LOGICAL_TO_ENTITY_TYPE` loses `account`, which the save stopped accepting on 2026-09-04. An Account
    prediction now takes the "no suggested record" path instead of a 400.

**Two ribbon defects found and fixed during the review here:**
1. **`notifyInfo` used `addAsync` with a key it had already used** ("Saving to Spaarke…", then the outcome, both
   under `spaarke_save`). Outlook does not accept a second `addAsync` on an existing key, so the outcome, including
   the new notice, could stay hidden behind "Saving…". It now uses `replaceAsync`, which adds or replaces. Pinned by
   a test.
2. **Outlook refuses a notification message over 150 characters**, which a long record name could exceed. The name
   is shortened inside the fixed sentence (60 characters), with `fitNotification` as a backstop. The verbatim
   wording is unchanged. Pinned by a test.

## 3. Evidence

**The core test: parity** (`tests/integration/contract/Api/Office/OfficeFilingAccessParityContractTests.cs`).
- For each of `{None, Read, Read|Write, AppendTo, Read|AppendTo, Read|Write|Append|AppendTo}`, the search with
  `access=file` and the save run in ONE host under the SAME rights, row by row across all five types.
- `canFile == false` ⇔ the save's gate refuses that row (403 `insufficient_rights`, or 400 `OFFICE_002` for an
  unfileable type).
- The probe double answers both of its methods from the bearer token. Everything between is shipped code.

**Seeded negative controls:**

| Seed | Result |
|---|---|
| Server: `verdicts[i] = true` in place of the policy check | **6 failed / 8 passed**: the three non-AppendTo masks, the Read-vs-AppendTo pin, the per-row failure test and the Read-only suggestion case. The AppendTo masks still pass, correctly, because the seed changes nothing there. Restored; no seed text left |
| Client (a): the picker's disabled branch removed | 7 failed / 1 passed (the auto-clear test does not depend on that branch) |
| Client (b): `canFile` dropped from `SaveFlow`'s map | 1 failed / 2 passed |
| Client (c): the ribbon's `canFile` branch removed | 2 failed ("Expected number of calls: 0 / Received: 1" on the save post) |
| Client (d): the logical name sent again, and `account` re-added | 10 failed |

**Other server tests:**
- `tests/integration/auth/Office/CallerRecordAccessProbeRecordsTests.cs` (7), on the shipped probe with only its
  three Dataverse-facing steps substituted:
  - one exchange and one `WhoAmI` per set, and N lookups;
  - every lookup asks about the caller, and results come back in order;
  - **max in flight = 4**, observed with a barrier rather than timing;
  - per-record failure → `None` for that record only;
  - `WhoAmI` failure → all `None` with 0 lookups; exchange failure → no `WhoAmI`; no token → no exchange;
  - empty targets → nothing asked.
- `OfficeFilingAccessParityContractTests` also covers: opt-in (no `access=file` → 0 lookups, `canFile` absent);
  an unknown `access` value → nothing; one lookup per page, never for `account`; one unreadable row → only that row
  `false`; the cap (55 targets → 50 true, 5 null); suggestion helper parity on {Read, Read|AppendTo}, with an
  unnamed candidate not asked about and a duplicate asked once.
- `tests/integration/regression/Issue1075_QuickSaveLogicalNameTests.cs`: `sprk_matter` → 400 `OFFICE_002`;
  `Matter` is not refused for its type.
- `OfficeEntitySearchAuthorizationContractTests`: one added fact (no `access=file` → `canFile` absent on every row).
  The existing facts are unmodified.

## 4. Latency (acceptance criterion, measured, with a stated limit)

The dev BFF still runs pre-#1045 code, and deploys are deferred by the owner. A local BFF cannot perform the OBO
exchange (module CLAUDE.md: `az login` covers everything except OBO). So the **added Dataverse calls were measured
directly against `spaarkedev1` from this workstation**, using the caller's own token:

| Call | p50 | p90 |
|---|---|---|
| The picker's search query (one type, `contains`, `top=10`): the existing cost | **184 ms** | |
| `WhoAmI` | 182 ms | 202 ms (n=5) |
| `RetrievePrincipalAccess`, one record | 177 ms | 270 ms (n=50) |

- **Modelled added time at `top=10`, 4 concurrent:** `WhoAmI` plus 3 waves = **≈0.71 s p50** from the
  workstation, plus one OBO exchange on a token's first use (MSAL caches it afterwards).
- A PowerShell-parallel measurement read 1.3 s p50, but it includes runspace start-up overhead and is not the
  number to use.
- **Escalation trigger not fired** (its threshold is +1 s p50). From Azure, close to Dataverse, the per-call
  round trip is much shorter, so the real cost should be well under the model.
- **To confirm after the next deploy from master:** time `access=file&top=10` against plain `top=10` on the dev BFF.
- If the cost proves too high, the options already in the POML are: (b) render rows first and fill in `canFile`
  with a separate call; (c) check only on selection.

## 5. Gates

| Gate | Result |
|---|---|
| Build | BFF 0 warnings / 0 errors |
| Server tests (touched suites) | 2,249/0/3 before the new tests; new and touched classes **32/32** |
| **Full BFF suite** | **13,064 passed / 0 failed / 54 skipped**. That is +24 on #1076's 13,040, **exactly** the new tests: parity 14, probe 7, #1075 2, the search fact 1 |
| ArchTests | **337 / 337**. The route census is unchanged: no new filter, and `/search/entities` keeps its Permanent waiver |
| office-addins jest (all suites = the 61 gated) | **61/61 suites, 809 tests** (was 58 suites / 774) |
| lint | **0** (`--max-warnings 0`) |
| typecheck | **68** = baseline, all in test files; **0 production** |
| `npm run build` | exit 0, no errors (placeholder env; the output is gitignored) |
| **Publish size (§10)** | Fresh master `c08ef6013` **45.458 MB** (47,666,123 B) → branch `91e73b6fc` **45.463 MB** (47,671,166 B) = **+5,043 bytes**. `Compress-Archive` Optimal, incl. PDBs, short-path fresh worktrees (removed afterwards); **212 files on both sides** |
| CVE | No package change; `dotnet list package --vulnerable --include-transitive`: none |
| **Code review** | **0 critical.** Found and fixed in review: the ribbon's `addAsync`-on-an-existing-key defect, and the 150-character limit (§2). Accepted, as judgment calls: the client agent's removal of `account` from `LOGICAL_TO_ENTITY_TYPE` (consistent with the server since 2026-09-04), and its restore-path guard. **Security:** `canFile` reports only the caller's OWN rights on records they can already read; suggestions ask only about candidates that resolved a name (readable), so there is no new existence or rights oracle. Enforcement is unchanged: the save still checks |
| **ADR check** | **0 violations.** ADR-008: no filter added; a response field, not a gate. ADR-010: no interface or registration; the probe is extended through `virtual` members, its existing seam. ADR-012 path A: no `@spaarke/ui-components`. ADR-013: no AI. ADR-019: no new code. ADR-021: Fluent tokens only, `InfoRegular`. ADR-028: OBO through the shared provider; no secret. ADR-032: nothing conditional. ADR-038: server tests in `contract/`, `auth/Office/` and `regression/`; client tests in `__tests__` (project Path A). UAC-r2's `CommunicationsEndpoints.cs` rule holds (no code line names `entityService`/`IGenericEntityService`) |

## 6. Not done here, stated

- **Live check:** no deploy has happened (the owner defers deploys), so the criterion stays open. In dev, only an
  administrator token is available here, so the disabled path is verified by tests only. The `<ui-tests>` in the
  POML are the manual script.
- **The To Do Contact search** not sending `access=file` is verified by reading `App.tsx`; there is no `App` test
  harness.
- **#1079 (ISS-015)** was found during this task, reported by UAC-r2. `sprk_invoice` has no `sprk_invoicename`
  column, so the Office **invoice quick-create always fails** (`OfficeService.cs:1916`). It is unrelated to this
  task's purpose, so it is filed and tasked separately.

# Task 021 — NFR-02 live timing run (dev, 2026-11-05)

> Run 2026-11-05 14:10:14-14:11:42Z against `spaarke-bff-dev` (master 909754869f, live since 04:16:30Z) and `spaarkedev1`, from one machine with one delegated token per resource (the owner's admin account). The owner approved about 40 test-matter creates in the main session on 2026-11-05. Creates only, no update, delete, metadata change or deploy. No token is recorded here.
> Spec NFR-02: the Create Matter wizard's create p95 may rise by at most **+500 ms**. Above that: STOP, and the owner decides (for example, caching).
> Method stub: `nfr02-matter-p95.md` on `origin/task/fm-021`. This file holds the live results; that stub's results table can point here.

## Verdict: FAIL. The STOP fired.

| Run | What is timed | n | p50 (ms, nearest rank, 10th) | p95 (ms, nearest rank, 19th) | max (ms) |
|---|---|---|---|---|---|
| Before | the old wizard's 4 calls in sequence (nav-prop metadata, Matter Type code, BFF profile GET, Dataverse `POST sprk_matters`) | 20 | **550.1** | **1272.4** | 1542.7 |
| After | `POST /api/v1/child-records/sprk_matter?mappingSourceEntity=sprk_project&mappingSourceId=…` | 20 | **3433.8** | **3906.8** | 3987.8 |
| **Delta** | | | +2883.7 | **+2634.4** | +2445.1 |

**The p95 rose by +2634 ms, more than five times the +500 ms budget. Per the brief I stopped after the run.** No caching and no re-run was attempted. That is the owner's decision.

## Deviations from the stub's method (read before quoting the numbers)

1. **Scripted HTTP, not a browser.** There is no browser here. Each sample is the same network sequence replayed in Python (`http.client`), with one keep-alive connection per host, which the browser also does. Client JavaScript time is identical on both sides and is excluded.
   - The "before" sequence comes from the removed code (`matterService.ts` and `FieldMappingService.applyFieldMappings` at 5f443eaae0).
   - The "after" call is the fm-021 bundle's single call (`createRecordViaBffWithWarnings`). The route is live on master; the fm-021 bundle is not deployed.
   - The sample runs from the first request's start to the create's response end.
2. **No active field-mapping profile onto `sprk_matter` exists on dev.** The stub's control ("a parent with an ACTIVE Mapping Profile onto `sprk_matter`") could not be met without an unapproved write. The coordinator chose option (A): run with no profile and bound the gap.
   - The parent is project PRJ-000044 "FMSWP-019 B" (`1cccf07d-0e0a-43f9-8ea0-f40533d5d691`).
   - "Before": the profile GET answers 404, so no source read follows.
   - "After": the server finds no profile. Trace: `FieldMapping: 0 column(s) mapped`.
   - See "Bounding the profile gap" below. A profile would make the delta **larger**, not smaller.
3. **The nav-prop read** (`discoverNavProps`) stays in the new wizard: it is module-cached per page load and runs before the BFF call. Per the brief it is counted on the "before" side only. Adding it to "after" (+82 ms median) widens the delta.
4. **Not in either window:** the BU-default lookup (`resolveUserBuDefaults`), which runs when the wizard opens. The old payload's cascaded value (`sprk_searchindexname` = "spaarke-files-index", the caller's BU) is in the "before" body. The caller's BU has no `sprk_ai_search_index`, so no bind was sent.
5. **The old client number** (`LITG-` plus 6 random digits) is in the "before" body, as the old code built it.
6. **The windows are not like for like, and this matters for the owner's decision.** The "after" create runs the I-12 L1 "Assigned *" grant inside the request (PostCreate, median 1.33 s). The old wizard ran that grant **after** the measured window (`syncAssignedAccess`), and the new wizard still calls it (an idempotent repeat).
   - So about 1.3 s of the delta is moved from after the window into it, not added to the user's wait.
   - Even without PostCreate, "after" is about 2.1 s against "before" at 0.55 s: still about +1.5 s over budget.
   - I did not replay `syncAssignedAccess` on either side. It writes grants, and the stub's definition ends when the record id is known.
7. Interleaving was ABBA (pair 1 before→after, pair 2 after→before, …), after one discarded warm-up each. There was no deploy or restart during the run: App Service `lastModifiedTimeUtc` was unchanged before and after, and App Insights has no host-start trace.

## Where the "after" time goes (server share)

- **App Insights `requests`** (sampled: 11 of 21 child-records POSTs have a row): p50 **3284 ms**, p95 3903 ms. Against the client's p50 of 3434 ms, the server is about **95 %** of the after sample. The network adds about 80-250 ms.
- **Server step durations** (gaps between the `[RECORD-CREATE] sprk_matter step …` traces, 11 sampled requests, median):

| Step | Median (ms) | What it does |
|---|---|---|
| Principal | 81 | WhoAmI as the caller (also the auth middleware) |
| TableRules | 302 | the column policy |
| FieldMapping | 176 | resolve the named source; the profile lookup finds none |
| CallerChecks | **983** (max 1268) | Create/Append privilege and AppendTo on each bound record (Matter Type, Practice Area, contact) |
| Owner | 261 | the resolver's BU team |
| Create | 136 | the one app-only create |
| PostCreate | **1331** (max 1474) | the I-12 L1 grant for attorney1 (one `sprk_assignedaccess` row plus sharing), and the membership event |

For comparison, the old direct create (`POST sprk_matters` as the user) took a median of **121 ms** (p95 251). The old BFF profile GET (404) took a median of 264 ms, p95 1002 ms, and it caused the two "before" outliers (1236 and 1002 ms).

## Bounding the profile gap (read-only, 10 GETs each after a warm-up, same machine and token)

| Call a profile would add | Median (ms) | Max (ms) |
|---|---|---|
| Caller probe: `RetrievePrincipalAccess` on the source | 79 | 111 |
| Source read, 8 assigned columns (the server's app-only read; the FLS mask is one more caller read of the same shape) | 78 | 116 |
| Target attribute metadata read (`TargetAttributesAsync`) | 85 | 97 |
| The old wizard's browser source read (with annotations) | 77 | 102 |

- **With a profile, "after"** adds roughly probe + read + mask read + metadata, about 320 ms sequentially (plus the OBO user lookup). It also adds an **AppendTo check for every mapped lookup** in CallerChecks.
  - Live evidence from the 2026-11-05 event create: with 6 mapped lookups (9 in all), CallerChecks took 2.35 s, against about 1.0 s here with 3 lookups. That is about +220 ms per mapped lookup.
  - The same event's FieldMapping step took 1.36 s with a 6-column profile, against 0.18 s with none.
- **With a profile, "before"** adds one browser source read (77 ms). The profile GET is already in the sequence (200 instead of 404).
- **So a profile widens the delta** by roughly +0.25 s to +1.5 s, depending on how many lookups it maps. The verdict does not depend on the missing profile.

## Raw samples

All 42 rows the run created, in name order. "Steps" are the "before" side's four calls.

| Name | Kind | ms (timed sample) | Steps (nav / type / profile / create, ms) | Number | sprk_matter id |
|---|---|---|---|---|---|
| NFR02-after-00-warmup | after | 3995.6 (warm-up, discarded) | — / — / — / 3996 | MAT-000035 | `954e9ff2-88af-444e-a26d-5b508c2d3b39` |
| NFR02-after-01 | after | 3906.8 | — / — / — / 3907 | MAT-000036 | `caaef458-7f38-4560-b951-bf6921437c11` |
| NFR02-after-02 | after | 3752.5 | — / — / — / 3753 | MAT-000037 | `c9e82574-4c03-4048-9241-861a43ff1ab7` |
| NFR02-after-03 | after | 3500.3 | — / — / — / 3500 | MAT-000038 | `d957ce5b-0d3d-415b-8ebc-bb038e38622c` |
| NFR02-after-04 | after | 3683.4 | — / — / — / 3683 | MAT-000039 | `4d3d0d1d-54e2-46e4-9329-d8bdf942c5ee` |
| NFR02-after-05 | after | 3518.6 | — / — / — / 3519 | MAT-000040 | `274a78c9-a37d-45fb-ac1a-d888c0b24145` |
| NFR02-after-06 | after | 3664.3 | — / — / — / 3664 | MAT-000041 | `2320f7a5-80d0-4426-8e13-ee183d0c916d` |
| NFR02-after-07 | after | 3331.8 | — / — / — / 3332 | MAT-000042 | `2c4a893e-f148-49c4-8a1a-37f28141bb35` |
| NFR02-after-08 | after | 3528.2 | — / — / — / 3528 | MAT-000043 | `f2ac88fd-a1aa-4fcc-8009-a58a608eb629` |
| NFR02-after-09 | after | 3304.8 | — / — / — / 3305 | MAT-000044 | `a7e80c18-5437-472c-ba4f-b42427675d9c` |
| NFR02-after-10 | after | 3518.4 | — / — / — / 3518 | MAT-000045 | `9df4bb39-9f04-4afb-a2a5-e2c3198f415b` |
| NFR02-after-11 | after | 3339.1 | — / — / — / 3339 | MAT-000046 | `f64d1ce8-59fa-4d88-86e7-ef7a669cca10` |
| NFR02-after-12 | after | 3321.7 | — / — / — / 3322 | MAT-000047 | `fdf5424d-9ab6-4a04-a57f-bbe6b50cac46` |
| NFR02-after-13 | after | 3561.3 | — / — / — / 3561 | MAT-000048 | `f9cb3ffc-7506-4f4a-93a1-1edd6ad27e29` |
| NFR02-after-14 | after | 3339.5 | — / — / — / 3340 | MAT-000049 | `91dc1f1c-c420-4ec5-965b-a310f0f1c82f` |
| NFR02-after-15 | after | 3330.4 | — / — / — / 3330 | MAT-000050 | `5d923cd9-6d7a-4acc-82b5-b8297463ef8f` |
| NFR02-after-16 | after | 3987.8 | — / — / — / 3988 | MAT-000051 | `1f3c3fd0-0e13-463f-9397-ccb0090c9603` |
| NFR02-after-17 | after | 3433.8 | — / — / — / 3434 | MAT-000052 | `3b940169-19bd-4e86-b5fb-35bc2fefdf5d` |
| NFR02-after-18 | after | 3369.7 | — / — / — / 3370 | MAT-000053 | `8df84f23-0581-4842-8a39-fae00fbae461` |
| NFR02-after-19 | after | 3239.9 | — / — / — / 3240 | MAT-000054 | `9bee4d23-4dc8-4956-a41b-f7e4264e91f6` |
| NFR02-after-20 | after | 3417.6 | — / — / — / 3418 | MAT-000055 | `3041278d-8624-4b3b-919d-4d8d8915cbc4` |
| NFR02-before-00-warmup | before | 1383.0 (warm-up, discarded) | 319 / 111 / 634 / 303 | LITG-390502 | `8d1e509c-22d9-f111-84e8-00224829aef9` |
| NFR02-before-01 | before | 710.6 | 81 / 96 / 276 / 257 | LITG-610796 | `3af1479c-22d9-f111-84e8-002248359bb2` |
| NFR02-before-02 | before | 544.9 | 85 / 75 / 264 / 121 | LITG-430050 | `80bc86a4-22d9-f111-84e8-7ced8dda31f8` |
| NFR02-before-03 | before | 600.2 | 83 / 71 / 318 / 129 | LITG-470845 | `48aa89a3-22d9-f111-84e8-002248359bb2` |
| NFR02-before-04 | before | 1542.7 | 82 / 74 / 1236 / 151 | LITG-890643 | `faa966a9-22d9-f111-84e8-00224829aef9` |
| NFR02-before-05 | before | 524.0 | 82 / 71 / 265 / 107 | LITG-199829 | `aae5ddab-22d9-f111-84e8-7ced8dda31f8` |
| NFR02-before-06 | before | 575.4 | 86 / 69 / 273 / 146 | LITG-353803 | `59c296af-22d9-f111-84e8-00224829aef9` |
| NFR02-before-07 | before | 724.0 | 131 / 79 / 263 / 251 | LITG-313674 | `5bc296af-22d9-f111-84e8-00224829aef9` |
| NFR02-before-08 | before | 577.7 | 89 / 75 / 278 / 135 | LITG-851913 | `69c296af-22d9-f111-84e8-00224829aef9` |
| NFR02-before-09 | before | 540.8 | 86 / 75 / 258 / 122 | LITG-841836 | `49d0a4b5-22d9-f111-84e8-00224829aef9` |
| NFR02-before-10 | before | 552.4 | 89 / 75 / 274 / 114 | LITG-442334 | `807348b8-22d9-f111-84e8-7ced8dda31f8` |
| NFR02-before-11 | before | 580.7 | 88 / 72 / 310 / 111 | LITG-231473 | `8c7348b8-22d9-f111-84e8-7ced8dda31f8` |
| NFR02-before-12 | before | 522.9 | 81 / 71 / 254 / 117 | LITG-978049 | `f60648be-22d9-f111-84e8-7ced8dda31f8` |
| NFR02-before-13 | before | 550.1 | 96 / 72 / 253 / 128 | LITG-424306 | `35664bbc-22d9-f111-84e8-00224829aef9` |
| NFR02-before-14 | before | 563.1 | 78 / 87 / 260 / 137 | LITG-558636 | `819802c3-22d9-f111-84e8-00224829aef9` |
| NFR02-before-15 | before | 1272.4 | 77 / 76 / 1002 / 118 | LITG-219380 | `849802c3-22d9-f111-84e8-00224829aef9` |
| NFR02-before-16 | before | 538.7 | 77 / 73 / 270 / 119 | LITG-987698 | `8c9802c3-22d9-f111-84e8-00224829aef9` |
| NFR02-before-17 | before | 522.5 | 82 / 71 / 255 / 114 | LITG-320409 | `038f40c9-22d9-f111-84e8-00224829aef9` |
| NFR02-before-18 | before | 531.1 | 87 / 75 / 260 / 109 | LITG-638701 | `0d8f40c9-22d9-f111-84e8-00224829aef9` |
| NFR02-before-19 | before | 527.1 | 82 / 73 / 256 / 115 | LITG-436321 | `0f8f40c9-22d9-f111-84e8-00224829aef9` |
| NFR02-before-20 | before | 543.8 | 75 / 78 / 259 / 131 | LITG-248862 | `a3345cd1-22d9-f111-84e8-002248359bb2` |

In run order (ms), before: 710.6, 544.9, 600.2, 1542.7, 524.0, 575.4, 724.0, 577.7, 540.8, 552.4, 580.7, 522.9, 550.1, 563.1, 1272.4, 538.7, 522.5, 531.1, 527.1, 543.8.
After: 3906.8, 3752.5, 3500.3, 3683.4, 3518.6, 3664.3, 3331.8, 3528.2, 3304.8, 3518.4, 3339.1, 3321.7, 3561.3, 3339.5, 3330.4, 3987.8, 3433.8, 3369.7, 3239.9, 3417.6.

Read-back by GET (all 42 rows):
- **after**: owner team "Spaarke" (the caller's BU default team), `createdby` `# mi-bff-api-dev`, `sprk_createdbyperson` Ralph Schroeder, numbers `MAT-000035`-`MAT-000055` from the platform autonumber, and no search index (#2502). No response carried `warnings`.
- **before**: owner = the user (Ralph Schroeder), `createdby` = the user, no creator person, a client number `LITG-nnnnnn`, and `sprk_searchindexname` = "spaarke-files-index". This is the old behaviour, now stored as test data.

## Rows created — the owner's cleanup list

- **`sprk_matter` ×42:** every row named `NFR02-before-*` (21) and `NFR02-after-*` (21). The ids are in the table above.
- **`sprk_assignedaccess` ×21, side effects of the "after" creates** (the attorney1 grant, one per after matter). Each is named "sprk_assignedattorney1 → contact:ac6d7b68-…". The "before" matters got none, because `syncAssignedAccess` was not replayed. Ids:
- `921e509c-22d9-f111-84e8-00224829aef9` (created 2026-11-05T14:10:18Z)
- `f74100a3-22d9-f111-84e8-00224829aef9` (created 2026-11-05T14:10:22Z)
- `084200a3-22d9-f111-84e8-00224829aef9` (created 2026-11-05T14:10:26Z)
- `094200a3-22d9-f111-84e8-00224829aef9` (created 2026-11-05T14:10:31Z)
- `f3a966a9-22d9-f111-84e8-00224829aef9` (created 2026-11-05T14:10:34Z)
- `1faa66a9-22d9-f111-84e8-00224829aef9` (created 2026-11-05T14:10:40Z)
- `57c296af-22d9-f111-84e8-00224829aef9` (created 2026-11-05T14:10:44Z)
- `61c296af-22d9-f111-84e8-00224829aef9` (created 2026-11-05T14:10:48Z)
- `66c296af-22d9-f111-84e8-00224829aef9` (created 2026-11-05T14:10:52Z)
- `4cd0a4b5-22d9-f111-84e8-00224829aef9` (created 2026-11-05T14:10:56Z)
- `4dd0a4b5-22d9-f111-84e8-00224829aef9` (created 2026-11-05T14:11:00Z)
- `10664bbc-22d9-f111-84e8-00224829aef9` (created 2026-11-05T14:11:04Z)
- `34664bbc-22d9-f111-84e8-00224829aef9` (created 2026-11-05T14:11:07Z)
- `4a664bbc-22d9-f111-84e8-00224829aef9` (created 2026-11-05T14:11:12Z)
- `809802c3-22d9-f111-84e8-00224829aef9` (created 2026-11-05T14:11:15Z)
- `879802c3-22d9-f111-84e8-00224829aef9` (created 2026-11-05T14:11:21Z)
- `899802c3-22d9-f111-84e8-00224829aef9` (created 2026-11-05T14:11:25Z)
- `098f40c9-22d9-f111-84e8-00224829aef9` (created 2026-11-05T14:11:29Z)
- `0c8f40c9-22d9-f111-84e8-00224829aef9` (created 2026-11-05T14:11:32Z)
- `fc1d54cf-22d9-f111-84e8-00224829aef9` (created 2026-11-05T14:11:37Z)
- `fd1d54cf-22d9-f111-84e8-00224829aef9` (created 2026-11-05T14:11:40Z)

The part 1 rows (event, event log, analysis, `FMSWP-019 020 matter` and its grant) are listed in `task-019-live-checks.md`, under "2026-11-05 live checks for 020 / 031 / 042b".

Numbers consumed: `MAT-000035` to `MAT-000055`. Deleting the rows does not return them.

## For the owner

1. **NFR-02 is exceeded: p95 +2634 ms against +500 ms.** Where the time goes:
   - CallerChecks, about 1.0 s: AppendTo per bound record.
   - PostCreate, about 1.3 s: the in-request "Assigned *" grant. The old flow ran it after the record id was known.
   - TableRules plus Owner, about 0.56 s.
2. **Options to weigh (owner decision; none taken):**
   - caching (the stub names profile caching, but the profile lookup is only about 0.18 s here);
   - moving the L1 grant out of the request, or not counting it, since the wizard repeats it anyway;
   - parallelising or caching the per-record AppendTo probes;
   - or amending NFR-02's window to the user-perceived flow up to "matter opened", which includes `syncAssignedAccess` on both sides.
3. **A run under a real profile onto `sprk_matter`** needs a profile to be created first (option B, not yet approved). By the bound above, it would widen the delta.

---

# Re-measure after 049, with a Project → Matter profile (dev, 2026-11-06)

> The owner approved the Project → Matter profile check and the 021 re-measure in the main session on 2026-11-06 (decisions.md, "Owner rulings: 044 scope, …"). Dev ran master 20669df6a9 (049 deployed 16:44Z). Same machine, method and token handling as above. No token is recorded here.

## Verdict: FAIL. NFR-02 is still exceeded, by about +2 s in the best case.

| Run (UTC) | What is timed | n | p50 (ms) | p95 (ms) | max (ms) |
|---|---|---|---|---|---|
| ABBA 20:46:52-20:50:32, before | old wizard: nav-props, Matter Type code, profile GET (200), ONE source read, `POST sprk_matters` with the mapped binds | 20 | **665.0** | **1369.2** | 1615.6 |
| ABBA, after | `POST /api/v1/child-records/sprk_matter?mappingSourceEntity=sprk_project…` (Assigned To (internal) mapped from the project) | 20 | **9227.8** | **11771.2** | 12016.7 |
| 21:11:51-21:12:24, after (queued) | the same call with Assigned To (internal) set by the wizard to another contact | 10 | **2660.2** (6th) | 3508.8 (= max, n=10) | 3508.8 |

- **Delta, as the wizard runs when the project assigns the maker:** p95 **+10.4 s**.
- **Delta when the grant is queued:** about **+2.0 s** at p50, against the +500 ms budget. The "after (queued)" run is after-only. It is compared with the ABBA "before" of 20 minutes earlier.

## The profile check: PASS

- **The profile.** "Project to Matter (Attorney Matrix)" (`227f3f93-22da-f111-84e8-002248359bb2`) has 8 Copy rules (`sprk_assigned*`, Parent to Child, Manual Refresh). It mirrors "Matter to Project (Attorney Matrix)" and is owned by the configuration owners team. Created 20:42:37Z.
- **The check.** `FMSWP-021-profile-check-01` (MAT-000056, `a06d57c3-affa-453f-9f55-a43ba2b11ee1`) was created from project FMSWP-019 B.
  - All 6 lookups set on the project landed on the matter.
  - The 2 lookups empty on the project stayed empty.
  - The owner is the caller's BU team.
  - No warnings were returned.
- **The first call took 12.6 s.** This is a cold start after the profile was created; the warm-ups below absorb it.

**Telemetry caveat (#2158):** App Insights on the dev BFF drops whole requests, with their traces and dependencies, so it has only 12 of the 33 server creates in these windows. The client's 33 logged 201s and the Dataverse read-back (33 `FMSWP-021-*` matters created by the BFF identity) are the evidence for the counts. The step timings below come from the operations that were kept, and the access gate for these windows reads "no failure in the observed telemetry" (UAC-r2).

## Where the time goes

**1. The grant runs inside the request when the maker is the matter's Assigned To.**
- Mechanism: 049 queues the assigned-access pass, except when the created root's "for" person (`sprk_assignedtointernal` on Matter) is the maker's own contact (049 fix round 1, UAC-r2 ruling). Then the pass runs inline.
- What happened here: the inline pass covered **every** assigned subject, not just the maker: `[ASSIGNED-ACCESS] Inline sprk_matter …: 5 assigned subject(s), 11 write(s)`. That includes the share grants, the assigned-access rows and two external-access grants. PostCreate took about 6.6 s.
- Why it matters: the project assigns the maker as Assigned To (internal), and a wizard that defaults Assigned To to the person creating the matter hits this branch on every create.

**2. Without the inline pass, the pre-create steps still cost about 2.3 s** (sampled telemetry):

| Step | ms (sampled) |
|---|---|
| FieldMapping | ~900 (against 176 with no profile, 2026-11-05) |
| CallerChecks | ~555 (concurrent since 049; 1.0 s before) |
| Principal | ~350 |
| TableRules | ~330 |
| Owner | ~300 |
| Create | ~200 |
| PostCreate | ~350 (queue, stamp, inherited permission) |

**3. The queued grants complete within seconds.** For the sampled "after (queued)" matters, the 6 `sprk_assignedaccess` rows and the 2 external grants were written 2-7 s after the response.

## For the owner (decision; none taken)

1. Whether the inline pass for "the maker is Assigned To" should grant **only the maker's own subject** inline and queue the rest. This needs UAC-r2's ruling; it owns the 049 rule.
2. With that fixed, NFR-02 still fails by about +2 s. The remaining cost is in FieldMapping (~0.9 s with a profile), CallerChecks, TableRules and Owner. The options from 2026-11-05 still apply:
   - cache the profile and target metadata;
   - run FieldMapping concurrently with TableRules and Owner;
   - or amend NFR-02's window to the user-perceived flow, which ends when the record opens.
3. The 021 wizard deploy keeps waiting on this decision.

## Rows created: the owner's cleanup list

- `sprk_fieldmappingprofile` ×1 and `sprk_fieldmappingrule` ×8 (the profile above). These are kept as dev configuration unless the owner says otherwise.
- `sprk_matter` ×54:
  - `FMSWP-021-profile-check-01`;
  - 42 rows named `FMSWP-021-before-*` and `FMSWP-021-after-*`;
  - 11 rows named `FMSWP-021-afterq-*`.
  - Numbers `MAT-000056` onwards were consumed by the "after" rows.
- **Side effects of the "after" creates:** `sprk_assignedaccess` rows (up to 6 per after matter), external-access grants (2 per after matter), and record shares.
  - They are found by matter: every `sprk_assignedaccess` and `sprk_externalrecordaccess` whose matter is one of the ids below.
  - The "before" matters got none, because `syncAssignedAccess` was not replayed.

| Name | Kind | ms | Client number | sprk_matter id |
|---|---|---|---|---|
| FMSWP-021-profile-check-01 | check | 12615 (cold) | — | `a06d57c3-affa-453f-9f55-a43ba2b11ee1` |
| FMSWP-021-before-00-warmup | before | 1176.7 (warm-up, discarded) | LITG-717358 | `a8b25930-23da-f111-84e8-00224829aef9` |
| FMSWP-021-after-00-warmup | after | 10405.4 (warm-up, discarded) | — | `94cbb69b-214d-4c30-ad9f-74a8eb4a6902` |
| FMSWP-021-before-01 | before | 774.9 | LITG-563298 | `4c6c9e33-23da-f111-84e8-002248359bb2` |
| FMSWP-021-after-01 | after | 11771.2 | — | `b77024ec-4a55-4f70-9494-9421e0cc15f3` |
| FMSWP-021-after-02 | after | 9893.9 | — | `b66ec15b-d3fb-46a7-baab-58f849100a83` |
| FMSWP-021-before-02 | before | 1011.6 | LITG-831487 | `c794c343-23da-f111-84e8-7ced8dda31f8` |
| FMSWP-021-before-03 | before | 715.3 | LITG-131123 | `cf94c343-23da-f111-84e8-7ced8dda31f8` |
| FMSWP-021-after-03 | after | 8755.8 | — | `68af29b4-9580-425f-9011-d9c8a7ea9b8f` |
| FMSWP-021-after-04 | after | 9303.3 | — | `cc1a484e-0c59-40ee-966a-8c3352308ef6` |
| FMSWP-021-before-04 | before | 1369.2 | LITG-983924 | `9a168c4c-23da-f111-84e8-002248359bb2` |
| FMSWP-021-before-05 | before | 645.4 | LITG-509935 | `94c65e4f-23da-f111-84e8-00224829aef9` |
| FMSWP-021-after-05 | after | 8571.9 | — | `f0f00656-5361-4db2-b9bd-4a669058c2fa` |
| FMSWP-021-after-06 | after | 9413.8 | — | `21804b33-05ff-438b-beaa-8e89b56649bf` |
| FMSWP-021-before-06 | before | 650.3 | LITG-388753 | `4fe60858-23da-f111-84e8-7ced8dda31f8` |
| FMSWP-021-before-07 | before | 688.3 | LITG-319799 | `59e60858-23da-f111-84e8-7ced8dda31f8` |
| FMSWP-021-after-07 | after | 9090.7 | — | `6bdfc849-839c-43da-b8f8-443eaab9c017` |
| FMSWP-021-after-08 | after | 8658.7 | — | `3fa0d616-1183-4088-930e-109fdc981810` |
| FMSWP-021-before-08 | before | 665.0 | LITG-532693 | `c4495962-23da-f111-84e8-00224829aef9` |
| FMSWP-021-before-09 | before | 607.7 | LITG-446683 | `80c0f963-23da-f111-84e8-7ced8dda31f8` |
| FMSWP-021-after-09 | after | 8907.0 | — | `d6e64904-4360-4a7a-b998-dec07709a93e` |
| FMSWP-021-after-10 | after | 10853.9 | — | `1d2aaf1f-3d15-467e-bae3-e0b3d852bcd4` |
| FMSWP-021-before-10 | before | 656.5 | LITG-737463 | `257eda6e-23da-f111-84e8-00224820fd20` |
| FMSWP-021-before-11 | before | 616.9 | LITG-990289 | `0ee82175-23da-f111-84e8-00224820fd20` |
| FMSWP-021-after-11 | after | 9393.1 | — | `5f38f1fb-4db8-409c-b581-8c0d60551bb9` |
| FMSWP-021-after-12 | after | 10728.6 | — | `4baab881-5cce-4d0b-9dd9-4a419764c45e` |
| FMSWP-021-before-12 | before | 621.3 | LITG-639963 | `ce62d37f-23da-f111-84e8-002248359bb2` |
| FMSWP-021-before-13 | before | 1615.6 | LITG-967132 | `13e7e381-23da-f111-84e8-7ced8ddb6f85` |
| FMSWP-021-after-13 | after | 9125.6 | — | `b7c8084f-3177-4323-9f78-3f083764911c` |
| FMSWP-021-after-14 | after | 9227.8 | — | `3d460840-0ecd-4130-a828-6fa271731b73` |
| FMSWP-021-before-14 | before | 677.6 | LITG-423323 | `add5c98d-23da-f111-84e8-002248359bb2` |
| FMSWP-021-before-15 | before | 617.3 | LITG-233239 | `60d46488-23da-f111-84e8-7ced8dda31f8` |
| FMSWP-021-after-15 | after | 8794.6 | — | `04fd827f-aa82-45f8-8ec7-d6af46b0e5a6` |
| FMSWP-021-after-16 | after | 12016.7 | — | `16730c7c-f64c-4098-8b1f-7af9b548d6bf` |
| FMSWP-021-before-16 | before | 1204.9 | LITG-471133 | `5a491597-23da-f111-84e8-00224829aef9` |
| FMSWP-021-before-17 | before | 663.8 | LITG-628940 | `5c491597-23da-f111-84e8-00224829aef9` |
| FMSWP-021-after-17 | after | 9085.0 | — | `a26383b6-d66f-4449-a7b3-7601af102aeb` |
| FMSWP-021-after-18 | after | 10161.0 | — | `6441675d-8ecd-486c-8fe7-d65e2dbf1567` |
| FMSWP-021-before-18 | before | 680.9 | LITG-616212 | `92ec67a4-23da-f111-84e8-00224829aef9` |
| FMSWP-021-before-19 | before | 666.4 | LITG-907922 | `f29b6aa6-23da-f111-84e8-7ced8dda31f8` |
| FMSWP-021-after-19 | after | 9556.4 | — | `ab3e52c8-d6f0-449e-859f-caf08898bc58` |
| FMSWP-021-after-20 | after | 8978.1 | — | `4ca1a5d0-4d69-48a2-b106-603284c92a0b` |
| FMSWP-021-before-20 | before | 618.1 | LITG-487478 | `53e169b2-23da-f111-84e8-7ced8dda31f8` |
| FMSWP-021-afterq-00-warmup | after (queued) | 4730.7 (warm-up, discarded) | — | `a952e6df-078c-4767-87bb-c554c9dd192e` |
| FMSWP-021-afterq-01 | after (queued) | 2734.5 | — | `905cd2b0-ecd2-4635-8f30-754adb983392` |
| FMSWP-021-afterq-02 | after (queued) | 2872.1 | — | `060f3398-95ee-4a8d-a1b7-72d6e8f64d9a` |
| FMSWP-021-afterq-03 | after (queued) | 2660.2 | — | `40a8a932-7fa6-4e13-8a49-2e912d5d696b` |
| FMSWP-021-afterq-04 | after (queued) | 2614.0 | — | `39f5baa9-fb64-406d-a589-1b9789bc7e95` |
| FMSWP-021-afterq-05 | after (queued) | 2673.7 | — | `01dc6905-0606-456f-adba-6e9c649337fb` |
| FMSWP-021-afterq-06 | after (queued) | 2593.3 | — | `93b0634a-4a5c-4ef8-b3be-9df1c237805f` |
| FMSWP-021-afterq-07 | after (queued) | 3508.8 | — | `3adea8fb-c8db-4fd9-8286-b87cd795c682` |
| FMSWP-021-afterq-08 | after (queued) | 2615.2 | — | `f5827e16-e777-44fd-a34d-6e14d4cb56ca` |
| FMSWP-021-afterq-09 | after (queued) | 2584.5 | — | `876cd928-4f96-4197-980e-cacdc4ee50f3` |
| FMSWP-021-afterq-10 | after (queued) | 2611.0 | — | `2550c21e-48c9-4749-8094-acbf7d01d802` |

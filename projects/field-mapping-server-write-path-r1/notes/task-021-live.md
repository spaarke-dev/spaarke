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

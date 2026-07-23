# Lessons Learned — spaarke-daily-update-service-r5

> One lesson per entry: a one-line summary, then why it mattered and how to apply it. Both corrections and confirmed non-obvious approaches. Appended at project close (2026-07-10).

---

### L1 — A "successful" BFF deploy can ship stale code: `dotnet publish` reuses cached `obj/bin`

**Summary**: When a BFF change "doesn't take" after a green deploy, the build itself was stale — not the deploy.

**Why it mattered**: Two consecutive `bff-deploy` runs shipped a DLL that lacked freshly-edited DTO fields + collector logic. The deploy's own SHA-256 hash-verify PASSED ("4/4 files match") because it compares the *local publish* to remote — but the local publish was itself stale (incremental `obj/bin` reuse), so local==remote==old. Cost ~an hour of misdiagnosis (I chased caching, multi-instance, run-from-package) before proving it.

**How to apply**: When a deployed BFF change doesn't reflect in behavior, `rm -rf src/server/api/Sprk.Bff.Api/{obj,bin,publish}` and redeploy BEFORE any other diagnosis. Hash-verify is necessary but NOT sufficient — it proves transport, not freshness. (Memory: `bff-deploy-stale-incremental-build`.)

---

### L2 — `spaarke-bff-dev` continuously deploys from master; branch BFF deploys are transient

**Summary**: A manual `bff-deploy` from a feature branch survives only until the next master merge redeploys the BFF from master.

**Why it mattered**: A BFF change verified live via `/render`, then silently reverted ~13 min later — a docs-only PR (#610) merged to master and triggered `deploy-bff-api.yml`, redeploying the BFF *from master* (without the branch's changes). The operator saw features "come and go," which read as flakiness. Client/code-page changes did NOT revert (their workflow is path-filtered).

**How to apply**: BFF changes can only be stably UAT'd on dev AFTER merging to master. Don't promise a stable pre-merge BFF test on dev in this multi-project repo; either merge (recommended once verified correct) or accept a fragile minutes-long window. (Memory: `bff-dev-continuous-deploy-from-master`.)

---

### L3 — Dataverse `distinct='true'` FetchXml drops record ids unless the PK is projected

**Summary**: `distinct='true'` dedupes on projected columns and does NOT return the record id unless the primary key is in the ColumnSet — so downstream code that reads ids gets empty and silently drops every row.

**Why it mattered**: This was THE briefing-completeness bug. The membership resolver's FetchXml used `distinct='true'` without projecting the PK → empty ids → `MaterializeResults` dropped them → membership resolved to **0** for a user owning 45 matters → the briefing silently omitted every membership-scoped record. "Accuracy" here means *completeness*, not just no-hallucination — attorneys rely on it. Removing `distinct` took it 0 → 49 live. Guarded now by `ResolveAsync_GeneratedFetchXml_MustNotUseDistinct_SoRecordIdsAreReturned`.

**How to apply**: Never use `distinct` in a Dataverse query whose results are keyed/joined on record id unless the PK is explicitly projected. Prefer no-distinct + de-dup in code (which R5 also added via the collector de-dup task).

---

### L4 — In this env, user→contact is `systemuser.sprk_primarycontact`; `contact` has no `azureactivedirectoryobjectid`

**Summary**: To resolve a user's contact identity (for assigned-attorney/paralegal membership), read `systemuser.sprk_primarycontact` — do NOT rely on `contact.azureactivedirectoryobjectid` (absent here).

**Why it mattered**: The assigned-attorney/paralegal matters weren't surfacing because `sprk_assigned*` fields are contact-typed, and the resolver had no user→contact link. The correct link is a lookup ON the user record (`sprk_primarycontact`), not an AAD-oid match on the contact — a data-model fact that took operator correction to land.

**How to apply**: For contact-typed membership fields, resolve `ContactId` from `systemuser.sprk_primarycontact` in `IdentityNormalizationService`. Verify against live metadata, not assumptions, before proposing an identity-join strategy.

---

### L5 — You can inspect the deployed BFF's exact JSON with a user token for the app's own resource

**Summary**: `az account get-access-token --resource "api://<BFF-AzureAd-ClientId>"` yields a token the BFF accepts; POSTing `/api/ai/daily-briefing/render` renders as your az user.

**Why it mattered**: Code inspection alone couldn't distinguish "empty data" from "stale build" — the deployed `/render` response was the only ground truth. This trick (client id via `az webapp config appsettings list ... AzureAd__ClientId`) turned an hour of speculation into a 2-minute empirical check that pinpointed the stale-build root cause.

**How to apply**: For any authenticated BFF endpoint diagnosis, get a direct token for the app's App ID URI and hit the endpoint. Ground-truth the response shape rather than reasoning about it. (Memory: `bff-deploy-stale-incremental-build`.)

---

### L6 — Accuracy-by-construction (deterministic rows + deterministic-fact TL;DR) delivered; no groundedness threshold needed

**Summary**: The project's core bet — zero LLM in item rows, TL;DR asserting only deterministically-computed facts with non-resolving anchors dropped — held up in operator UAT.

**Why it mattered**: The operator's hard requirement was "100% accurate — including completeness." Making the rows/counts/descriptions/titles all source-field-derived (no LLM) meant the only remaining risk surface was the single TL;DR sentence, bounded by dropped anchors. No probabilistic groundedness gate was needed (operator ruling 2026-07-08), and it verified clean. The maker-editable TL;DR prompt (`sprk_analysisactions` / `BRIEF-NARRATE-TLDR` / `sprk_systemprompt`) tunes voice only — it cannot change the deterministic data.

**How to apply**: For "must-be-accurate" AI surfaces, push determinism as far down as possible and shrink the LLM's job to the smallest bounded assertion. Existence/completeness should never be probabilistic.

---

### L7 — Reuse-first (§11) paid off twice: shared file-preview modal + email dialog

**Summary**: The Documents file-preview reused the shared `RichFilePreviewDialog` (same modal as the Semantic Search PCF) and email reused `SendEmailDialog` — no parallel component trees.

**Why it mattered**: The operator explicitly asked for "the same modal you already have." Reusing the shared component made file-preview a ~1-file wiring job (a `fetchPreviewUrl` callback to the existing `/api/documents/{id}/preview-url`) instead of rebuilding a viewer + SPE preview plumbing. Confirms the §11 default-to-reuse instinct: check the shared lib first.

**How to apply**: Before building any UI affordance, grep the shared lib (`@spaarke/ui-components`, `@spaarke/*`) for an existing component. A callback-shaped seam (like `fetchPreviewUrl`) is usually all a new consumer needs.

---

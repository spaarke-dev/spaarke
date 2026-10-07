# Task 078 — FR-05: one unified app package for Outlook AND Word

> **Date**: 2026-09-30 · **Task**: `tasks/078-resolve-manifest-contradiction.poml` · FULL · opus @ high
> **Owner direction**: *"what is the best long term solution — take that path now"*
> **Commits**: `e2e50a965` (package) · `6e590d012` (fix: the merge module was gitignored) · `d17ac8152` (docs) · `34e105ed1` (fix: permissions, §5b)

---

## 1. What is actually live — read this before touching any manifest

| Host | Production registration | Id | Version |
|---|---|---|---|
| Outlook | **XML** `outlook/outlook-manifest.xml` | `5e4d66d0-2603-44ea-acbc-400d3b881c90` | 1.0.22.0 |
| Word | **XML** `word/word-manifest.xml` | `b3965ea0-6942-4f17-81b3-2c645bd05ebf` | 1.0.9.0 |

**Neither host runs a unified (JSON) manifest in production.** `outlook/manifest.json` and `word/manifest.json`
were dev-sideload only.

⚠️ **The filename trap.** Outlook's production XML is `/outlook/outlook-manifest.xml`; `/outlook/manifest.xml`
404s. This project had recorded that (current-task.md, 042-uat-results §83) — and this task still walked into it
once, probing the wrong path and concluding *"Outlook already runs unified JSON in production"*. That claim, and
the follow-on *"uploading the Word JSON would replace the Outlook add-in"*, were **wrong** and were corrected
with the owner the same session. `c1258e2d-…` (the id webpack stamped into both dev JSON manifests) is the Entra
client id; it is not the id of any production app.

**Rule for the next reader: inspect the BUILT/DEPLOYED file, and check this project's notes for a known trap
before drawing a conclusion from a 404.**

## 2. Why one combined package is the long-term answer

Evidence (Microsoft Learn, verified 2026-09-30; researcher memory
`.claude/agent-memory/researcher/word-unified-manifest-ga-status-2026-09-30.md`):

| Fact | Source |
|---|---|
| Unified manifest **GA for Word** since 2026-07-16: web ✅, Windows **≥ 2501**, Mac **≥ 16.103**; perpetual / mobile / iPad ❌ | unified-manifest-overview (2026-09-24) |
| Latest released schema **1.30** | root schema reference |
| **One app may target several hosts** — ONE extension, `requirements.scopes` any subset of mail/document/…, each runtime & ribbon scoped via nested `requirements` ("each app supports only one extension") | element-extensions (2026-08-11); specify-office-hosts (2026-09-10) |
| Microsoft's own example: Script Lab merged its separate Outlook and Word/Excel/PowerPoint add-ins into one app | GA blog post |
| Admin center: upload a unified add-in as **App type "Teams app"**, a zip holding the manifest + icons | admin deploy doc (2026-08-20) |
| The unified app needs a **different GUID** from the XML `<Id>`; hide the XML via `alternates[].hide.customOfficeAddin.officeAddinId` | manage-both-versions; convert guide |
| 🔴 **office-js #6938 (open)**: `hide.customOfficeAddin` works in Outlook, **not in Word** (desktop or web) — both ribbons show | GitHub issue, 2026-09-10 |
| Outlook on Mac does not support the unified manifest | unified-manifest-overview |

**Why ONE package rather than two:** Spaarke's add-ins are already one product — one codebase, one build, one
hosted site, one Entra registration, one BFF. Per-customer provisioning (Model 1 / Model 2) uploads and consents
the add-in once per tenant: one package means one upload and one consent per customer instead of two. It is
also Microsoft's stated direction. The cost — a Word change re-ships the Outlook half — is already true of the
single build and single hosted site. The researcher leaned toward two packages until #6938 is fixed; #6938
affects hiding the Word XML in **either** layout, so it does not distinguish them.

## 3. What was built

| File | Role |
|---|---|
| `src/client/office-addins/packaging/mergeUnifiedManifest.js` | **Pure** merge of the two host manifests into one package (details below) |
| `src/client/office-addins/packaging/__tests__/mergeUnifiedManifest.test.ts` | 8 tests on the REAL source manifests + XML ids (incl. permission parity, §5b); registered in `ci-gated-suites.txt` |
| `src/client/office-addins/webpack.config.js` | `SpaarkeUnifiedPackagePlugin` emits `dist/spaarke/`: `manifest.json`, `manifest.test.json`, `color.png` (192), `outline.png` (32). Package-id config separated from the Entra id. Standalone `word/manifest.json` output **retired** |
| `src/client/office-addins/word/commands/index.ts` | registers `quickSaveDocument` (the package's Word action) alongside `quickSave` (still used by the Word XML) |
| `src/client/office-addins/generate-icons.mjs` + `shared/assets/icon-color-192.png` | 192×192 color icon **rendered from the owner's vector logo**; the generator can now render single targets, so adding one icon does not re-encode the ones the live add-in serves |
| `scripts/Package-OfficeAddinUnified.ps1` | zips `dist/spaarke/` into the production and TEST packages, OUTSIDE `dist/` (so the public site does not serve them); **exits 1** on a wrong-size icon |
| `.github/workflows/deploy-office-addins.yml` | runs the packaging script and uploads both zips as the run artifact `spaarke-addin-unified-package` |

**The merge, rule by rule:**
- **Outlook ids kept exactly as authored** — `outlook/manifest.json` is the precedent FR-05 names; the package is a strict superset of it.
- **Word ids namespaced where they collide** (`TaskpaneRuntime` → `WordTaskpaneRuntime`, `SaveButton` → `WordSaveButton`, …).
- **executeFunction ids are never renamed blindly.** The id is the function name `Office.actions.associate` registers; renaming only the manifest ships a dead button. A colliding id must be listed in `WORD_FUNCTION_RENAMES` (today `quickSave` → `quickSaveDocument`), and a test proves `word/commands/index.ts` registers every Word action the package declares.
- **Each runtime and ribbon is scoped to one host**; host-specific capabilities (`Mailbox 1.8`; `WordApi 1.3`, `CustomXmlParts 1.1`, `DialogApi 1.1`) moved from the extension onto those nodes — an extension-level `WordApi` requirement would stop the package installing in Outlook.
- **`alternates.hide` names BOTH XML add-ins**, with ids read from the XML files at build time so they cannot drift from what is registered.
- **Package id `e68f3cb1-3702-4a58-8c02-972e7d1667eb`** — its OWN GUID, stable for the life of the package (override: `ADDIN_APP_ID`). The Entra client id goes in `webApplicationInfo.id` only. The merge **refuses** a package id equal to the client id or either XML id.
- **Icons repaired**: the Outlook JSON referenced 9 icons (`save-*`, `share-*`, `grant-*`) that were never created — **404 on the hosted site** today. The architecture doc records that the admin center fails validation on a 404 icon; the merge maps each to the same-size Spaarke icon, and the build fails if no fallback exists.
- **3-part version** `1.1.0`; 4-part is rejected (the unified manifest does not accept it).
- **TEST variant** (id `b490de25-d155-44cd-8825-6e125102dd84`, override `ADDIN_TEST_APP_ID`): "(TEST)" name and ribbon labels, and **no `alternates`** — a test copy that hid the XML add-ins would hide the tester's working Spaarke ribbon, and leave none if the test build were broken (the POML's escalation trigger 1).

## 4. Verification (real output)

| Check | Result |
|---|---|
| Both manifests against **Microsoft's published v1.30 schema** (ajv, draft-04) | **VALID** — and two deliberately broken controls (missing `id`; invalid scope) **rejected**, so the validator discriminates. The ajv `$ref`-sibling warnings were inspected: every ignored sibling is `description`/`default`/redundant `type` — no constraint lost |
| Merge tests | **8 / 8** (the 8th is §5b's permission parity); seeded: deleting the `quickSaveDocument` registration → the cross-file test red; disabling the id guard → the conflation test red; both reverted byte-identical |
| Packaging script | exit **1** with a 128px color icon, **0** with 192px (real `pwsh` exit codes, not a pipe's) |
| Zips | each holds exactly `manifest.json` + `color.png` + `outline.png` at the root; CRC-valid |
| Gated jest | **58 / 58 suites, 773 tests** |
| Lint / tsc | clean / **68** (the pinned baseline) |
| Production build | clean |
| **Fresh-checkout build** | see §5 |

## 5. 🔴 A defect this task introduced and caught — the merge module was never committed

The module first lived in `build/`. The **repo-root `.gitignore` ignores every `build/` folder**, so the first
commit (`e2e50a965`) contained a `webpack.config.js` that required a module **not in git**. On any fresh checkout
— CI's deploy included — the add-in build would have failed with "Cannot find module", and the gated-jest job
would have gone red on a listed suite that did not exist. It worked locally only because the files existed in
this working copy. Caught by reading the commit's staged-file list; fixed in `6e590d012` by renaming to
`packaging/` (not force-adding: the next file anyone created there would be silently ignored again). Proven by
building from a **fresh worktree at the fixed commit** (`C:\wt078f`, `6e590d012`), exactly as CI does:

| Fresh-checkout check | Result |
|---|---|
| `packaging/mergeUnifiedManifest.js` present in the checkout | **True** |
| a `build/` folder present (must be False) | **False** |
| `npm install` (Spaarke.Auth, then the add-ins) | 451 + 1,371 packages |
| `npm run build` (production, CI's env) | **exit 0**; `dist/spaarke/` = color.png, manifest.json, manifest.test.json, outline.png |
| `Package-OfficeAddinUnified.ps1` | **exit 0**; both zips produced |

Also added to the module CLAUDE.md: *do not name a folder `build/` here*.

## 5b. 🔴 Code review (Step 9.5) — a Critical: the package granted NO permissions

Schema-valid and green on every test, the package would still have **installed and then failed at the features it
exists for**. Its `authorization.permissions.resourceSpecific` was **empty** — inherited from the dev-only Outlook
JSON, which was never uploaded, so nobody had ever discovered it — and `validDomains` was missing. The live XML
add-ins grant:

| Host | XML `<Permissions>` | Unified equivalent (now granted) | Without it |
|---|---|---|---|
| Outlook | `ReadWriteItem` | `MailboxItem.ReadWrite.User` (Delegated) | cannot read the email or its attachments |
| Word | `ReadWriteDocument` | `Document.ReadWrite.User` (Delegated) | cannot read the `.docx` or write the FR-02 identity stamp |

**Why nothing caught it:** the v1.30 schema types `resourceSpecific[].name` as a free string (maxLength 128), so
no schema check can verify a permission name — a wrong or missing one fails only at install or runtime.

**Fix (`34e105ed1`), built so it cannot recur silently:** each host's `manifest.json` declares its permission and
its `validDomains` (the XML `<AppDomains>`, verbatim as full `https://` origins — what Microsoft's converter emits);
the merge takes the union; and webpack reads each live XML's `<Permissions>` so the merge **refuses to build a
package granting less than the XML add-in it replaces** (`XML_PERMISSION_TO_RSC`). Names verified against Microsoft
Learn ("Requesting permissions", 2026-06-30; "Understanding Outlook add-in permissions", 2025-12-02) and the output of
Microsoft's `office-addin-manifest-converter`.

**Stated honestly — two things the docs do not settle:** mixing a mail and a document permission in one package is
**undocumented either way** (the converter's parser ignores the other family, which suggests it is safe — code
behaviour, not a documented guarantee); and the `validDomains` format is inferred from Microsoft's tooling, not
stated by the schema. **The TEST upload (§6 step 3) is what settles both** — watch for a permission error at install.

Other review checks, all clean: a merge failure inside webpack's `processAssets` hook fails `npm run build` with
**exit 2** even under `--no-bail` (seeded: a 4-part version); the Word `quickSaveDocument` alias is registered; the
TEST variant carries the permissions (so the test exercises the real access) but no `alternates`.

**Updated verification totals:** merge tests **8 / 8** (adds permission parity, whose negative case strips Outlook's
permission and asserts the build refuses); gated jest **58 / 58 suites, 774 tests**; lint exit 0; tsc 68; both
manifests VALID against v1.30 with both controls rejected; packaging exit 0.

## 6. 👤 OWNER STEPS — the rollout (these need your hands)

1. **Deploy** so the site hosts this code: `gh workflow run deploy-office-addins.yml --ref work/spaarkeai-word-add-in-r1`.
   Download the run's artifact **`spaarke-addin-unified-package`** (two zips).
2. **Test first.** Microsoft 365 admin center → **Settings → Integrated apps → Upload custom apps** → App type
   **"Teams app"** → `spaarke-addin-1.1.0-TEST.zip` → assign to **Just me**. Propagation can take hours.
3. **Observe on each host, and record the host + build number:**
   - **Outlook desktop and Outlook on the web** — the "Spaarke (TEST)" group shows *Save to Spaarke*, *Create To Do*,
     *Quick Save* (read) and *Share from Spaarke*, *Grant Access* (compose); the pane opens; Quick Save runs.
   - **Word desktop (build ≥ 2501) and Word on the web** — the "Spaarke (TEST)" group shows *Save to Spaarke*,
     *Quick Save*, *Share*; the pane opens; Quick Save and Share run.
   - Your existing XML "Spaarke" ribbons must still be there and still work (the TEST package hides nothing).
   - **Watch the install for a permission prompt or error** — this is where §5b's two undocumented points get
     settled (a mail + a document permission in one package; the `validDomains` format).
4. **If all good → production.** Upload `spaarke-addin-1.1.0.zip` the same way; assign to your user groups.
   - **Outlook**: supported clients hide the XML add-in automatically (up to 24 h).
   - **Word**: office-js #6938 — both ribbons show. Retire the Word XML **manually**: remove its assignment for
     users on Word ≥ 2501 (or accept both ribbons until the bug is fixed).
5. **Remove the TEST app** from Integrated apps.
6. **Keep both XML add-ins deployed** for clients that cannot run the unified package — **Outlook on Mac**, and
   **Word older than 2501** — until those populations are gone. Do not delete them from the admin center.

Every later package change: bump `UNIFIED_PACKAGE.VERSION` in `webpack.config.js` (3-part) — the admin center
rejects a same-version update — and re-upload.

## 7. Acceptance criteria — honest status

| # | Criterion | Status |
|---|---|---|
| 1 | Which manifest the deployed add-in loads from, with evidence | ✅ §1 — both XML (and the trap that misled this task once) |
| 2 | JSON sideloaded and verified on desktop AND web, per host — OR attempt recorded + FR-05 amended | ⏳ **OWNER** — the package is built, schema-valid and CI-produced; the OBSERVED installation (§6 steps 2–3) cannot be done from here. FR-05 is **not** amended: the owner chose to ship |
| 3 | If amended: spec updated | N/A — not amended |
| 4 | If shipped: deploy path publishes the JSON; XML retired deliberately or kept with a reason | ✅ the workflow produces the package; **both XML manifests KEPT** — Outlook on Mac and Word < 2501 cannot run the unified package, and #6938 blocks auto-hiding in Word |
| 5 | Task 011's POML status corrected, ACs 6/7 marked with the real outcome | ✅ see 011 |
| 6 | The existing XML installation still works — verified, not assumed | ✅ **by construction + evidence**: both XML manifests are byte-for-byte unchanged by this task (`git diff` on `outlook/outlook-manifest.xml` and `word/word-manifest.xml` is empty), and the only commands-script change ADDS an alias — `quickSave` stays registered. The live-client check is §6 step 3's last bullet |

## 8. Stale documentation corrected by this task

- `docs/architecture/office-outlook-teams-integration-architecture.md` — its "Manifest rules" table stated XML rules
  (4-part version, no `FunctionFile`, single `VersionOverrides`) as if universal; the unified package takes a 3-part
  version and command runtimes.
- `src/client/office-addins/CLAUDE.md` — "no `FunctionFile`" (task 037 added one) and "4-part version" for JSON.
- `.claude/skills/office-addins-deploy/SKILL.md` — told operators to download `outlook/manifest.xml` (404).

## 8b. 👤 Rollout outcome (owner, 2026-10-03)

- **What the owner saw before the tidy-up:** three Spaarke groups in Word ("Spaarke (TEST)" and two "Spaarke"). That
  is the expected result of uploading BOTH zips while the Word XML add-in is still assigned (office-js #6938).
- **What the owner did:** *"i removed the TEST and the two xml Word and Outlook specific--and now not showing so
  should be ok"*. So the TEST app (§6 step 5) and **both XML add-ins** are removed. Only the unified package
  (`spaarke-addin-1.1.0.zip`) remains, and the extra groups are gone.
- **This differs from §6 step 6** (keep both XML add-ins for clients that cannot run the unified package). The owner
  decides this. The consequence is that **Outlook on Mac** and **Word older than 2501** now have no Spaarke add-in.
  If either population appears, re-upload the matching XML (`/outlook/outlook-manifest.xml`, `/word/manifest.xml`
  from the deployed site); both are still built and hosted.
- Criterion 2 (§7): the owner's 2026-10-03 UAT round (`042-uat-results.md` §10) exercised Quick Save and Share in
  Word. It is not recorded which package's ribbon was clicked (it may have been before the tidy-up). The UAT
  feedback is about behaviour, not about installation, so the observed install of the unified package on desktop
  and web is still the owner's to confirm.

## 9. Follow-ups (not done here)

- **Per-customer builds** (Model 1 / Model 2, D-13): `webApplicationInfo.resource` is per-customer BFF, so each
  customer gets its own build of the package. The package id can stay the same across tenants — ids are unique
  per tenant, not globally — but `customer-provisioning-orchestration-r1` should consume the artifact rather than
  the XML.
- **`outlook/manifest.json` standalone output** is still emitted for dev sideload and still stamps
  `ADDIN_CLIENT_ID` as its package id. Harmless (dev only), superseded by `dist/spaarke/`; retire it at cutover.

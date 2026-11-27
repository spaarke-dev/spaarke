# Owner upload checklist: Phase 0 test packages (task 020)

> **Program:** specialist-legal-agents-r1, Phase 0. **Written by:** task 020, 2026-11-26.
> **Status:** eight packages built and zipped, **none uploaded or installed**. A Claude session never clicks tenant or admin-center actions: every step below is the owner's.
> **Covers:** G1-NDA (6 packages), G5 (1) and G2 question 1 (1). Test READMEs: `../README.md` (G1), `../../g5/README.md`, `../../g2/README.md`.
> **Zips are gitignored** and live only on this machine. To rebuild one, see the "Test packages" section of the test README.

## Before you upload anything

1. **The R4 row for the package has been sent** to COPILOT-PLATFORM-COORDINATION (Issue #3006) by the main session, and the session has told you so. The rows are in the "R4 row (to send)" section of each test README. No row sent, no upload.
2. **Scope: owner and the test user `demo@` only, never All users, never an org-wide group (R7).** If the route you use cannot be limited to those two accounts, **do not upload**; tell the session where it would have landed.
3. **Dev tenant only.**
4. The G8 artifacts are removed (due 2026-11-30; this precedes the G1 run, task 022).
5. The dev BFF serves the spike tools (`d307d67e36`, live 2026-11-26 about 18:00Z). Other dev users can see the spike tools; tell them before the runs.
6. **Never change the OAuth registration** (A1), and never add anything to an action URL. Nothing in these packages needs either.

## Rules that apply to every package

- **First upload only.** Each package has a fresh app id and loads at once on its first upload (G8-P2 and V2: no delay on a new id).
- **Never upload a changed zip over an existing package.** An action change on an existing id can take the action offline for about 40 minutes or longer (BR-2, BR-3), and stacking updates hides the result. If a package does not load after its first upload, **stop and tell the session**. The session builds the fix on a fresh app id and adds a new R4 row.
- **Do not send test prompts yet.** The runs are tasks 021, 022 and 023. After each upload do only the check in "Check that it loaded".
- **Test documents (later):** only `corpus/nda-01.md` to `nda-10.md` from `phase0/g1-nda/corpus/` may ever be attached or opened in a test chat. Never a real NDA.
- If an approval card appears at any step, click **Cancel** and tell the session the step and the tool it names.

## The packages

Absolute folder: `C:\code_files\spaarke-wt-specialist-legal-agents-r1\projects\specialist-legal-agents-r1\phase0\`. Paths below are relative to it.

### Chat agents (org catalog, "Upload agent" route)

| # | Package | Zip | App id | Version | Install scope |
|---|---|---|---|---|---|
| 1 | G1 variant (a) agent `spaarke-legal-g1a-nda-test` | `g1-nda\packages\spaarke-legal-g1a-nda-1.0.0.zip` | `f80243d0-b9ef-409c-8a8f-787c184162ea` | 1.0.0 | owner + `demo@` only |
| 2 | G1 variant (b) agent `spaarke-legal-g1b-nda-test` | `g1-nda\packages\spaarke-legal-g1b-nda-1.0.0.zip` | `67f754d4-9327-4451-9c79-552047a17ed4` | 1.0.0 | owner + `demo@` only |
| 3 | G1 variant (c) agent `spaarke-legal-g1c-nda-test` | `g1-nda\packages\spaarke-legal-g1c-nda-1.0.0.zip` | `080ac94c-c863-4802-acf0-ec2f71dea925` | 1.0.0 | owner + `demo@` only |
| 4 | G5 area agent `spaarke-legal-g5-area-test` | `g5\spaarke-legal-g5-area-1.0.0.zip` | `f7af4143-02b0-4912-b3ee-e73b0e202422` | 1.0.0 | owner + `demo@` only |
| 5 | G2 question 1 agent plus marker skill `spaarke-legal-g2-q1-test` | `g2\spaarke-legal-g2-q1-1.0.0.zip` | `7bcab05e-3d88-4006-96c9-545edfc5a6ee` | 1.0.0 | owner + `demo@` only |

**Steps for each Chat agent** (the first-publish path that worked for `d38f8df8` and `cb5afd1c`):

1. Microsoft 365 admin center, **Agents**, upload the zip as a **new** agent.
2. The first-publish wizard runs: **Upload**, **Publish to users**, **Security policy**, **Accept permissions**, **Review**.
   - Publish to users: **Specific users/groups**, and pick only the owner and `demo@`. Never Everyone.
   - Security policy: the default template, no custom policies.
   - Accept permissions: it should show "No required permissions".
3. Finish the wizard. Open the agent's details, then **Data & tools**.
4. Wait for the agent to appear for the two users. Each user may have to add it from the agents list.

### Cowork skills (plugin registry route)

| # | Package | Zip | App id | Version | Install scope |
|---|---|---|---|---|---|
| 6 | G1 variant (a) Cowork skill, plugin `spaarke-legal-g1a-cowork-test` (skill `spaarke-test-g1-nda-review-a`) | `g1-nda\packages\spaarke-legal-g1a-cowork-test-1.0.0.zip` | `9c52e606-2574-48f5-b557-1f7246c19414` | 1.0.0 | owner + `demo@` only |
| 7 | G1 variant (b) Cowork skill, plugin `spaarke-legal-g1b-cowork-test` (skill `spaarke-test-g1-nda-review-b`) | `g1-nda\packages\spaarke-legal-g1b-cowork-test-1.0.0.zip` | `6940e661-d067-4154-adb4-06366648c697` | 1.0.0 | owner + `demo@` only |
| 8 | G1 variant (c) Cowork skill, plugin `spaarke-legal-g1c-cowork-test` (skill `spaarke-test-g1-nda-review-c`) | `g1-nda\packages\spaarke-legal-g1c-cowork-test-1.0.0.zip` | `391d8de8-e48b-4a66-84ba-ef9c764222fc` | 1.0.0 | owner + `demo@` only |

These carry **no action**: they use the tenant connector "Spaarke MCP", which each of the two users must already have connected (Copilot, Settings, Sources, Spaarke MCP, Connect; the same sign-in as before).

**Steps for each Cowork plugin** (as for `spaarke-plugin.zip` in the connector runbook):

1. Microsoft 365 admin center, **Agents**, **Tools**, **Registry**: upload the zip.
2. Install it for **Specific users** (the owner and `demo@`), not All users. If the Registry offers only All users, **do not upload** and tell the session.
3. Approve it there if asked.
4. Wait: a plugin appeared under Browse plugins 10 to 30 minutes after upload last time.
5. Each of the two users turns it on once in **Cowork, Customize**. After the first turn-on the skill can fail for a few minutes while its content syncs; retry in a new task.
6. You can upload all three and switch on all three. The run prompts name the skill (`Use the spaarke-test-g1-nda-review-a skill.`), so they stay unambiguous. Switch on only the one under test if you prefer.

## Check that it loaded (do this once per package, then stop)

**Chat agents (rows 1 to 5):**

1. On the agent's **Data & tools** tab, look for the banner "This MCP server uses other tools, which may be changed by the publisher: ..." naming the package's action. Screenshot it. (Dynamic discovery shows this banner; V2 did.)
2. Open the agent in Chat, in a new chat, as the owner. Send exactly: `List every tool and source you can use in this conversation`
   - **G1 agents and G5:** the list must include `spike_get_profile` and `spike_review_nda`. It may also list `spaarke_show_decisions`, `spike_review_agreement` and `spike_review_legal_hold` (on dev since 2026-11-27).
   - **G5 only:** the list must also include `spike_review_agreement` and `spike_review_legal_hold`.
   - **G2 agent:** same tools, and the skill `spaarke-test-g2-marker` may be named.
3. If the tools are missing, **wait 10 minutes and ask once more in a new chat**. If they are still missing, stop and tell the session. Do not upload a corrected zip over it.
4. Repeat step 2 as `demo@`. If `demo@` is asked to sign in, sign in with the demo account; that is expected on a first use.

**Cowork plugins (rows 6 to 8):** in a new Cowork task, type `/` and check that the skill (`spaarke-test-g1-nda-review-a`, `-b` or `-c`) is listed under the plugin's name. Screenshot it. Do not run it yet.

## Tell the session when you are done

For each package: the time of the upload (to the minute, UTC), the screenshot of the Data & tools banner or the `/` list, and the answer to the "List every tool" prompt. The session records the install date in `phase0/README.md` and checks App Insights for the `tools/list` call. It will not mark anything installed until you confirm it.

**G5 tools (closed 2026-11-27):** all three function tools are on dev since about 13:14Z on 2026-11-27 (spike `5cd40984b0`): `spike_review_nda` plus the routing stubs `spike_review_agreement` and `spike_review_legal_hold`. The G5 R4 row was sent the same day, so row 4 can be uploaded like the others. See `g5/README.md`, "Status of the function tools".

## Removal (later, at task 031)

Each package comes out of the admin center (Agents for rows 1 to 5, Tools, Registry for rows 6 to 8), and the session then tells COPILOT-PLATFORM-COORDINATION so the R4 rows can be closed. Removal date for every row: at task 031.

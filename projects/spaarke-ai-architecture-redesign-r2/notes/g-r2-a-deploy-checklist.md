# G-R2-A Deploy Checklist → spaarkedev1 (prerequisite for the 049 UAT)

> **Goal**: get the G-R2-A code (Wave J + Wave K + task 044) + its catalog seeds live on spaarkedev1 so the operator can run `g-r2-a-uat-script.md`.
> **Status of code**: all merged to master (PRs #595/#596/#598). No code work remains — this is a deploy + seed.

## What must be live

| # | Artifact | What | Who runs | I can prep |
|---|---|---|---|---|
| 1 | **BFF** | Publish `Sprk.Bff.Api` (Release) → deploy to the **spaarkedev1 App Service** (carries the gate engine, Completion Engine, trace read endpoint, UI-ack, capability endpoint, task-044 live gate) | operator / CI pipeline | ✅ I can run `dotnet publish -c Release` + verify size, and hand you the artifact + the exact deploy command for your pipeline |
| 2 | **SpaarkeAi code page** | Build + deploy the SpaarkeAi React code page (OutcomeCard component, progressive render, trace view, UI-ack client, capability hook) to spaarkedev1 Power Platform | operator / CI | ✅ I can run the prod build + point to the deploy step |
| 3 | **Catalog seed — create-matter** | Seed the `CREATE-MATTER@v1` Action row + `sprk_playbookconsumer` Binding row + activate `ConsumerTypes.CreateMatter` (DEF-003 / #593; 7-step sequence in `notes/jps/create-matter-binding-row-pending-seed.json`) → then flip golden-utterances GU-065/066/067 `planned`→`existing` | operator (live Dataverse write) | ✅ I can generate the seed commands / MCP calls; the live write to spaarkedev1 Dataverse is your call |
| 4 | **Choice member — compose disposition** | Ensure the `sprk_disposition` global choice has the `compose = 100000006` member (so a deployed Binding can carry it; compose-r2 dependency) | operator | ✅ I can generate the choice-column update |
| 5 | **Health verify** | `GET /healthz` on spaarkedev1 BFF → **Healthy** (confirms ConsumerTypes/catalog parity — a mismatch flips it Unhealthy) | operator (quick check) | — |

## Sequence
1. I prep: `dotnet publish` the BFF (verify ≤60 MB), prod-build SpaarkeAi, and generate the seed + choice-member commands.
2. You (or CI): deploy the BFF + SpaarkeAi to spaarkedev1; run the seeds against spaarkedev1 Dataverse.
3. You: hit `/healthz` → Healthy.
4. You: run `g-r2-a-uat-script.md` (the 10 scenarios) in the browser.
5. On full PASS: ADR-041 Proposed → Accepted; G-R2-A closes.

## What I need from you to prep the deploy
- **How do deploys reach spaarkedev1?** (a) a CI/CD pipeline I should trigger/point you to, (b) a script (e.g. `Deploy-*.ps1`) you run, or (c) manual az/pac push. Tell me which, and I'll prepare exactly that artifact + command set rather than guessing.
- Whether you want the **create-matter catalog live now** (needed for S1's create flow to use the *cataloged* capability; without it, create-task/create-matter still work via the generic gated path, so the UAT is runnable either way — the seed just adds the capability-specific description + eval coverage).

*Note: nothing here changes code — it's deploy + seed of already-merged, already-verified work.*

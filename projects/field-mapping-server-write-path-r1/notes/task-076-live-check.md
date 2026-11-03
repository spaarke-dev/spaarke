# Task 076 — W7 deploy and live Family check (dev)

## Step 1 — BFF (R3)

- R3 deployed master `9f4afe82ac` to `spaarke-bff-dev`. ProductVersion `1.0.0+9f4afe82ac9fef2aaf725464f46bae09eadf33db`, read back at 2026-10-30 14:40:39Z. `healthz` and `healthz/dataverse` returned 200; an anonymous `/api/me` returned 401.
- It contains W7: 070–074 and the #2470 name-column fix (`63a3daa576`), plus UAC #2471.
- Our check of this SHA before the deploy:
  - `Spaarke.sln` built with 0 errors.
  - The filtered suites ran 6119 passed, 0 failed.
  - ArchTests passed 1857/1858. The one failure was ISS-005, the known flaky test (#2469); it passes when run on its own.

## Step 2 — client deploys

| Component | Version | How | Read-back |
|---|---|---|---|
| TrackingFieldTrio PCF (074, Family icon + `RecordFamilyModal`) | 1.0.55 | `pcf-deploy`: `Invoke-PcfBuildProd.ps1` (bundle 1,092,856 bytes, byte-identical to 074's measurement), `pack.ps1`, `Import-SolutionScoped.ps1` on `spaarkedev1` | `solution.version` = 1.0.55, modified 2026-10-30 10:47; the newest importjob shows progress 100 |
| Create Work Assignment wizard (038) | ships in the Console bundle | Not redeployed here. The owner exercised the WA create live on 2026-10-29: it saved, with a number and Assigned To. | Confirm in step 3 |
| `sprk_fieldmapping_push.js`, RecordFamily code page | — | Not deployed. They ship with 040 (owner, 2026-10-28). | — |

## Steps 3–4 — live checks (owner, then a narrower test user)

Every step is read-only except 3c, which is the 038 work-assignment create the POML asks for.

**Test records (dev, read 2026-10-30):**
- Non-secure matter **CMRCL-342387** "Test New Matter via Workspace" (`375fa95a-3060-f111-ab0b-7c1e521b425f`): 2 pair-filed work assignments.
- Secure matter **PAT-908068** (`5d46df87-e762-f111-ab0c-000d3a4d8152`): 1 work assignment.
- No project on dev is pair-filed yet. Project rows appear in the Family view only after UAC 195's backfill. Until then, the Project form's Family tool shows the project's own family.

**3a. Owner, Family tool.**
1. Hard-refresh (Ctrl+Shift+R). The TrackingFieldTrio footer should read v1.0.55.
2. On the Matter, Project, Work Assignment and Event forms, open the Family icon.
3. Each list should show the five columns: name, record number, record type, status, modified on.
4. On CMRCL-342387 it should list both work assignments with their numbers.
5. Check the browser console for errors.

**3b.** On a Matter, click **Push Updates**. Until 040 ships it still pushes as it does today (owner 2026-10-28, FR-35); note the behaviour.

**3c. 038.** Create a work assignment from CMRCL-342387 twice:
- with Assigned To chosen: the record should keep the chosen contact;
- with Assigned To blank: the record should get the matter's Assigned To Internal.

**4. Narrower user** (no read on PAT-908068):
- open the family of a record in a family that includes the secure matter;
- the secure branch should be absent, with no count, no placeholder row and no error that hints at it.
- **Escalation:** if a row appears that this user cannot open, stop. Ask R3 to roll back, and tell UAC-r2.

## Step 5 — App Insights

Query for `/api/records/*/family` since the 14:40Z deploy: count of 5xx (expect 0) and p95 duration. To run after the owner's pass.

## Results

(pending the owner's pass)

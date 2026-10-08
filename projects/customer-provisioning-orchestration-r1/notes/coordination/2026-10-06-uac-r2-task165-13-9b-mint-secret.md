# Coordination: UAC-r2 task 165 §13.9(b) / §14.9(b) must NOT mint a secret on `bfac7f6e` (2026-10-06)

**To**: unified-access-control-r2 (main session) — via the owner or a #1094 comment (ask the owner first).
**From**: customer-provisioning-orchestration-r1.

UAC-r2's `notes/task-165-admin-surfaces.md` §13.9(b) — carried unchanged into §14.9(b) — plans, for dev SPE config
`68f9a952…`:

> 2. `… -MintClientSecret -Apply` (adds a client secret to app `bfac7f6e…` "Spaarke SPE Model 1 Owner" …)

**Do not run step 2.** Reasons:

1. **Binding here**: no secret or certificate is ever added to the `Spaarke Model 1` owning app `bfac7f6e` (owner D16;
   `.claude/constraints/provisioning.md` "SPE owning app — MI-FIC, nothing stored": "never add a client secret to an owning
   app"). L2 signs in as the owning app through its UAMI's federated credential (T248), with nothing stored.
2. **No longer needed**: since sdap-SPE-admin-app-r2 merged (2026-10-04), SPE Admin authenticates as the BFF's own identity
   or delegated — `SpeAdminTokenProvider` / `SpeAdminGraphService` construct no confidential client
   (`tests/Spaarke.ArchTests/CredentialCensusTests.cs`, "REMOVED 2026-10-04"). The config's `sprk_keyvaultsecretname` has no
   reader that needs a secret; T250's rule stands: no secret-based `sprk_specontainertypeconfig` row for a Model 1 type.
3. **State already changed**: on 2026-10-06 the dev config's `sprk_keyvaultsecretname` went from `"null"` to empty (owner
   option A). Re-read it before any repair.

What UAC-r2 can still do safely: `Test-SpeConfigSecretNames.ps1 -Verify` (read-only), and decide whether an EMPTY secret name
is acceptable to its `spe-owning-app-` prefix rule (round 35 item 3) now that no SPE Admin path reads a secret.

Status: **handed to the owner for relay on 2026-10-07**, as part of one message to UAC-r2 (with issues #1363 and #1364).

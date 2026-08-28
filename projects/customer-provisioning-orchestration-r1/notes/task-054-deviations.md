# Task 054 (H11) — Deviations from POML literal wording

Per root CLAUDE.md §6.5 ADR Conflict Resolution Protocol + task-execute Step 8's
"directional vs prescriptive" guidance (POML `<steps mode="directional">` — goal +
acceptance-criteria + constraints bind, sequence adaptable; a resolved ambiguity
is documented, not silently improvised).

## 1. Test file location — Path C (pivot to comply with established repo convention)

**POML said**: `Handlers/H11UserProvisioningHandler.Tests.cs` (colocated with the
handler under `src/server/services/Sprk.Provisioning.ControlPlane/Handlers/`).

**Actual**: `src/server/services/Sprk.Provisioning.ControlPlane.Tests/Handlers/H11UserProvisioningHandlerTests.cs`
— a separate test project, matching ALL prior Wave-C4 handler test files
(H0PreflightHandlerTests.cs ... H10DataverseAppUserGraphParityHandlerTests.cs —
same precedent + rationale as task 053's deviation note item 1).

## 2. B2BGuest branch scope — resolved <goal> prose vs <steps>/<acceptance-criteria> ambiguity

**Tension observed**: the POML `<goal>` reads: "(b) for each user in
parameters.users, invokes GraphUserService pattern to create user with correct
UPN + license assignment; (c) for B2BGuest preset, **additionally** invokes B2B
invitation + consent-verification gate" — read literally, "additionally" could
mean BOTH branches run CreateUser+AssignLicense AND B2BGuest also runs the
invitation + gate on top.

**Resolution**: implemented B2BGuest as an ALTERNATIVE branch (invitation +
consent gate only, no CreateUser/AssignLicense call), not an additive one. Basis:

1. The POML's own `<steps>` scope each collaborator call explicitly per branch:
   `order="3"` — "For each user: **NativeAccount branch** → GraphUserService.CreateUser
   + AssignLicense"; `order="4"` — "For each user: **B2BGuest branch** → B2B
   invitation via Graph; queue consent-verification gate." Two distinct branches,
   not a shared step followed by a B2B addendum.
2. Every `<acceptance-criteria>` entry that mentions license assignment is scoped
   to `identityPreset == "NativeAccount"` only (criterion 1); the B2BGuest
   criterion (criterion 2) mentions ONLY invitations + the consent gate, with no
   license-assignment expectation.
3. The real-world Microsoft Graph B2B model provisions guests by invitation
   (`POST /invitations`), not by a direct `POST /users` with a password profile —
   a B2B guest calling both endpoints for the same identity is not a coherent
   Graph operation (the invitation flow creates the guest's directory object
   itself; a second `POST /users` would either collide or create an unrelated
   second identity).
4. design.md D6 states the two presets are alternatives ("Identity handler
   branches at user-creation only") — not a base flow plus an add-on.

**Reviewer note**: if this reading is judged incorrect, the fix is additive (call
`IGraphUserProvisioner.CreateUserAsync`/`AssignLicenseAsync` inside the B2BGuest
branch too, guarded by a design.md amendment clarifying D6) — flagged here for
explicit reviewer visibility per CLAUDE.md §6.5 rather than silently picking one
reading.

## 3. InterStepState controlled schema extension

**Added**: `InterStepState.ProvisionedUsers` (`IList<ProvisionedUserRecord>?`,
JSON key `provisionedUsers`), plus the `ProvisionedUserRecord` POCO
(`Handlers/UserProvisioning/ProvisionedUserRecord.cs`) recording `userId` +
`upnOrEmail` + `identityPreset` per user.

**Rationale**: design.md §6.2's enumerated `interStepState` keys have no slot for
H11's user-provisioning output, and the POML goal item (d) requires "writes
provisioned userIds to Cosmos interStepState." Follows the CONTROLLED SCHEMA
EXTENSION precedent established by task 049 (`ImportedSolutions`), task 050
(`SpeContainerId`), and task 053 (`BffAppRegSystemUserId`) — a deliberate,
documented type extension, not an ad-hoc dictionary insert. Populated on BOTH
the terminal-success write (NativeAccount, or B2BGuest with consent Verified)
AND the B2BGuest WaitingOnGate write, so an operator can see who was invited
while consent is pending.

## 4. `usersJson` run-parameter encoding — JSON array embedded in a NonSecret string value

**Design gap**: `RunParameters.NonSecret` is a flat `IDictionary<string,string>`
by construction (see `RunParameters.cs` header — deliberately NOT a JSON-fragment
hole, to keep the secret-safety guarantee simple). Neither spec.md nor design.md
specifies a wire shape for "`parameters.users`" (the POML goal's phrasing) against
that flat-dict constraint.

**Resolution**: `usersJson` is a single `NonSecret` entry holding a JSON-array-
encoded string (`[{"firstName":...,"lastName":...,"email":...,"companyName":...}]`),
deserialized via `System.Text.Json` (camelCase, case-insensitive — same
`JsonSerializerOptions` shape as `H05ConsentCaptureHandler`'s `ConsentCapturePayload`
parsing). This is explicitly sanctioned by `RunParameters.cs`'s own doc comment
("richer scalar types can be encoded as strings") and contains no secret fields
(name/email are not secrets), so it does not open the JSON-fragment hole
`RunParameters.cs` guards against. No user list needed structured typed storage
beyond simple string fields, so a single encoded key was preferred over adding N
loosely-related flat-dict keys.

## 5. NFR-09 "Graph SDK calls catch ODataError" — Path C (pivot to comply in spirit)

Same rationale as task 053 item 3 (H10's file-header "NFR-09 IMPLEMENTATION
NOTE"): H11's three Graph collaborators (`GraphRestUserProvisioner`,
`GraphRestB2BInvitationClient`, `GraphRestB2BConsentVerifier`) use raw
`HttpClient` + `DefaultAzureCredential` against the Graph REST surface directly
— NOT the `Microsoft.Graph` SDK package (L2 does not reference it; per-task
dispatcher context explicitly directed "match established L2 collaborator
pattern from H10"). Non-success HTTP status codes are caught and the status
code + response body are surfaced in the failure diagnostic — the functional
equivalent of NFR-09's `ODataError` catch, without a new SDK dependency.

## 6. TASK-INDEX.md — not touched (dispatcher-owned in parallel mode)

Per the Batch 3F dispatcher instructions, `TASK-INDEX.md` is owned by the
dispatcher in parallel execution and was intentionally NOT edited by this task
(POML step 10 nominally asks for it; the dispatcher supersedes for parallel-mode
tasks). Task 054's own `<metadata><status>` was flipped to `completed` in its
POML file instead.

## Quality gates (Step 9.5) summary

See the commit message / final task report for the concrete numbers (build
exit code, test pass count, code-review + adr-check verdicts) — captured after
the shared `Sprk.Provisioning.ControlPlane.csproj` build cleared the concurrent
Batch 3F sibling (task 073 / H14 `IntegrationWiring`) in-flight compile state.

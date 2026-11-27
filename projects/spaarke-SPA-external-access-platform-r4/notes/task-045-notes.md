# Task 045 — contact store log labels, faithful double, gate tenant read (FR-30, #2499)

## Ontology agreement (POML constraint "project coordination")

Asked: 2026-11-27, spa-r4 → ontology-r1 (cross-session message), with the change summary for
`Services/Signals/AssignTaskRecipientGate.cs` `ComposePortalPrincipalAsync`: compose the WorkforceHome principal with the
stored home tenant (`ReadHomeTenantAsync`, as `HomeLinkAsync` does); an unreadable tenant → Unverified, never an
empty-tenant principal.

Answer: 2026-11-27, ontology-r1 (cross-session message), **agree**, with conditions:

> 1. Missing, unparseable or throwing ReadHomeTenantAsync ends Unverified with a fail-closed warning and no email, and never composes an empty-tenant principal. Cancellation still propagates, under the same `when` filter HomeLinkAsync uses.
> 2. If one decision runs both HomeLinkAsync and ComposePortalPrincipalAsync, read the tenant once if that's simple. If not, two reads are acceptable; say which in the PR.
> 3. The CIAM branch, the seams and the refusal reasons stay unchanged. Add your readable-tenant and unreadable-tenant cases to the existing tests/integration/data-mutation/Signals gate tests. If the test double's GetContactAsync change exposes a gate test that depended on ExternalTenantId, fix that test, not the double.
> 4. Run `--filter "FullyQualifiedName~Signals"` (it includes the contract baselines) and ArchTests before pushing. Send me the PR: the file is ontology-owned, so I review it before it merges.

Consequence: the PR that carries 045 needs ontology-r1's review before it merges, in addition to the owner's go.

## Execution record (2026-11-27)

### Changes
- `ContactIdentityStore.cs`: fixed labels `"guest systemuser lookup"` / `"home contact lookup"` on the two unlabelled reads. `GetAsync`'s failed-read warning now carries the status and the Dataverse error code only, for every caller, labelled or not (it logged up to 400 body characters for an unlabelled read before); an unlabelled read still logs its path. `ReadProofAsync`'s exception message carries "HTTP {status}" and the error code only (no body, no transport text). New pure helper `ErrorCodeOfError(string?)` beside `ErrorCodeOf`: reads `error.code` out of the store's `"{status}: {body}"` string, tolerant of a body cut at 400 characters, and returns null unless the code is a short code-shaped token. The returned `error` value is unchanged.
- `SecuredColumnReadProof.cs`: NOT edited. It logs the exception the store throws, so (b) is fixed at the source.
- `IsMembershipRefusal` (task 047 review K1): now compares the parsed `error.code` exactly (via `ErrorCodeOfError`) under exactly 403, not a substring of the error text. A body cut after the code still parses (deliberate: the code comes first in a Dataverse error and the error string is cut at 400 characters); an unparseable error is false.
- `AssignTaskRecipientGate.ComposePortalPrincipalAsync`: a WorkforceHome principal is composed with the STORED tenant (`ReadHomeTenantAsync`), never `RawExternalTenantId`. A missing / unparseable / empty / throwing read throws the private `HomeTenantUnreadableException`, which `DecideExternalAsync` turns into `Refuse(Unverified)` (fail-closed warning, no email). Same `when` filter as `HomeLinkAsync`, so cancellation propagates. CIAM branch, seam signatures and refusal reasons unchanged.
- `InMemoryContactIdentityStore.GetContactAsync`: the row carries no tenant (`with { RawExternalTenantId = null }`); `Row(c)` and every other read are untouched (`FindHomeContactsAsync` still returns the tenant).

### One read or two (ontology condition 2)
TWO reads. `HomeLinkAsync(contact, ct)` and `ComposePortalPrincipalAsync(contact, plane, ct)` are virtual seams whose signatures must stay unchanged (condition 3), so the tenant cannot be handed from one to the other; each reads it. Cost: one extra single-row `contacts(id)?$select=sprk_externaltenantid` per WorkforceHome external decision.

### Tests the corrected double exposed (fixed in the test, not the double)
With the original tests, the corrected double and the new gate, three `AssignTaskRecipientGatePlaneTests` failed:
- `ThePlane_IsWorkforceHomeOnly...(workforce-home)`: composed through `Mock.Of<IContactIdentityStore>()`, which cannot answer the tenant read. Now `Identities(row).Object`.
- `AWorkforceHomeContact_AsRegistrationStoresIt_...(linked true / false)`: `Guid.Parse(row.RawExternalTenantId!)` on the `GetContactAsync` row. Now asserts the row carries no tenant and reads the stored tenant with `ReadHomeTenantAsync`.
Also `DataverseContactIdentityStoreTests.AFailingHomeLinkRead_...`: its "not relabelled" control used `FindHomeContactsAsync`, which is now labelled. The control is now `FindContactsByOidAsync` (still logs its path) and also asserts no body. (File outside the brief's edit list; a direct consequence of the labelling.)

### New tests
- `tests/integration/regression/Issue2499_ContactStoreLogLabelTests.cs` (24 cases): label on a failed and on a thrown read for both reads; no body for 7 reads (labelled and not); non-error body; membership refusal still recognised while the log carries no body; the proof's logged exception; `ErrorCodeOfError` cases.
- `AssignTaskRecipientGatePlaneTests`: readable-tenant composition carries the tenant; readable tenant end to end is emailed (2 cases); unreadable second read (Failed, ColumnMissing, Empty, NotAGuid, Throws) is Unverified with one fail-closed warning, nothing composed; cancellation propagates.
- `IdentityLinkReconciliationTests.IsMembershipRefusal_ComparesTheParsedErrorCodeExactly_UnderExactly403` (8 cases).

### Seeding proofs (failure counts)
- Label removed from `FindSystemUsersByGuestObjectIdAsync`: Issue2499 tests 2 failed / 22 passed (the failed-status and thrown-read cases for that label). Restored.
- Body put back in the `GetAsync` failed-read log line: 14 failed / 10 passed. Restored.
- Body put back in the `ReadProofAsync` exception message: 1 failed / 23 passed (the proof test). Restored.
- Gate reverted to HEAD: 7 failed / 27 passed in `AssignTaskRecipientGatePlaneTests` (the readable composition, the cancellation case and the five unreadable-read cases). Restored.
- `IsMembershipRefusal` reverted to the substring test: 3 failed / 5 passed. Restored.
- Body put back in the `WriteAsync` failed-write log line: 1 failed / 26 passed (the failed-write test). Restored.
- Label removed from `FindContactsByOidAsync`: 2 failed / 25 passed (the failed-read and thrown-read cases for `contacts by oid`). Restored.

### Added at the coordinator's request (same NFR-04 class, same file)
- `WriteAsync`'s failed-write warning (`ContactIdentityStore.cs:2521`) logs the status and the Dataverse error code (`ErrorCodeOfError`) only, never the body. The returned write error text is unchanged. Grep of the file: every remaining `Trim(body)` is a returned `Error` value, none is logged.
- `FindContactsByOidAsync` (`:1638`) passes the fixed label `"contacts by oid"`, so the oid is no longer logged through its path. The controls in `DataverseContactIdentityStoreTests` (the "not relabelled keeps its path" checks, two places) now use `GetContactAsync` (contact-id path, which stays).
- Tests added to `Issue2499_ContactStoreLogLabelTests.cs`: a failed write logs no body marker (and still returns the body in its error); the by-oid read joins the failed-read and thrown-read label theories (no label-less path, no oid).

### Found, not fixed
- `GetAsync`'s thrown-read warning logs the whole exception (type, message, stack); a token-source fault's text is logged as before. The coordinator files it.
- Reads that stay unlabelled still log their request path: `GetContactAsync` and the removed-marker and invite-marker reads (contact ids only, not in NFR-04's list).

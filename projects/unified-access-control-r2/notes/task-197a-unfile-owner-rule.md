# Task 197a: the unfile owner rule (#2722) and a document under an unfiled Direct message (#2728)

Owner answers 2026-11-12: both #2722 choices YES; #2728 option (a), option (b) rejected for now.

## 197a-1 (#2722): what changed

`RecordOwnershipResolver` (`src/server/api/Sprk.Bff.Api/Services/Dataverse/`):

| Member | Role |
|---|---|
| `UnfiledCommunicationFacts` (record) | Holds what was read before the change: the creator systemuser id, or a keep reason. |
| `RecordOwnershipContext.UnfiledFrom`, `RowBeforeChange.Unfiled` | Carry the facts, so the first decision and `SettleOwnerAsync` / `ConfirmUnconditionalSecureWriteAsync` give the same answer. |
| `ReadUnfiledFactsAsync` | The only maker of the facts. It does one committed read per direct parent left (owning unit; `sprk_issecure` on matter, project and work assignment; `sprk_accesspermission` where the table carries it) and checks the message itself. Any fault → Keep. |
| `NonDirectUnfileAnswerAsync` | The only reader of the creator. Keep or no creator → Unchanged. Otherwise `ResolveFromUserAsync` (191c rule included); refused or faulted → Unchanged. |
| EventId 5420 `communication.unfile_keeps_owner` | Logs a kept owner with ids only. |

- **Path (a), the regarding-only clear** (a link column cleared, no parent left): this path now settles through `SettleOwnerAsync` as well. Before, it answered Unchanged.
- **Path (b), parents become empty:** this path carries the facts into the settle.
- **Creator:** read from `sprk_createdbyperson` only (`RecordCreatorPerson`), never `createdby`.

### Tests

- `tests/unit/domain/Dataverse/RecordOwnershipResolverUnfileOwnerTests.cs`: 17 tests.
  - Must-fire: a standard matter → the creator's team, written by the settle; the regarding-only clear settles; an External Collaborators creator → the customer's unit.
  - Must-not-fire: the message leaves a Secure, Restricted or Secure-team-owned parent; the message is itself Secure-team owned, flagged secure, Restricted, or has no owning unit; a parent read faults or returns nothing; no recorded creator; the creator is disabled, unknown, or external with no customer unit.
- `RecordOwnershipResolverDirectUnfileTests`: one test renamed (the pending-#2722 email case now pins "no recorded creator keeps its owner"). Otherwise unchanged.
- `tests/Spaarke.ArchTests/RefileWriterUnfileRuleGuardTests.cs` pins four things, with must-fire and must-not-fire controls (`UnfileFactsControl*`):
  - one reader of the creator (`NonDirectUnfileAnswerAsync`);
  - one maker of the facts (`ReadUnfiledFactsAsync`);
  - the decision goes through `ResolveFromUserAsync`;
  - both re-derivations pass `RowBeforeChange.Unfiled`.

### Perturbation proofs

Each change was reverted afterwards.

| Perturbation | Result |
|---|---|
| The creator team is never returned | 4 must-fire tests fail |
| The settle does not pass the facts | 4 unit tests fail, and the guard fails |
| The Secure-unit check is disabled | The Secure-owned message and parent tests fail |
| The flag check is disabled | The flagged-secure message test fails |
| The Restricted check is removed | The Restricted tests fail |

## Adversarial pass 1 (Opus): fixed

| Finding | Fix | Proof |
|---|---|---|
| F1: a regarding-only clear moved a message still linked to another record (for example, one of two contacts cleared) | `ReadUnfiledFactsAsync` first asks the Direct rule's own test, `DirectMessageOwnerStep.WhyFiledUnderARecord`, of the row as the change leaves it. Still linked → Keep, with no read. This applies on both paths, so a matter cleared from an email still linked to a contact keeps its owner too. | 2 tests; removing the check fails both |
| F1: a path (a) settle failure answered an error for a change already saved, and a retry could not repair it | Catch everything but cancellation, keep the former owner, log a Warning with ids only, and return Unchanged with `ParentsUnchanged = false` | 1 test (the owner write faults); removing the catch fails it |
| F3: the parent `sprk_issecure` check had no test | Two parent cases: flagged secure in an ordinary unit, and an empty flag (task 150) | Accepting an empty flag fails 1; ignoring the parent flag fails 2 |
| Doc drift | Moved the `DirectUnfileAnswerAsync` summary back onto its method; replaced the "Unchanged pending #2722" text in `DirectMessageOwnerStep` and the four writers | — |

Pass-1 known limits that were kept:
- **The settle reuses the facts read before the change.** A message made Secure or Restricted concurrently, between the decision and the settle, is not re-checked. A concurrent move into the Secure unit is still caught by `WouldLeaveSecureBusinessUnitAsync`.
- **Direct parents only.** Their Restricted state is read directly. A grandparent's state reaches the message only through the message's own inherited Access Permission value.
- **The creator is read through field security.** If the app user cannot read `sprk_createdbyperson`, the unfile keeps its owner (safe).
- **A fault while reading the facts logs twice:** a Warning and then an Information entry.

## #2728: root cause (for 197a-2)

The issue's premise is false: `sprk_relatedcommunication` is already a document link. It is in `DocumentLinkFields.All` (Models.cs), and it is not in `SecureChildLineage.DocumentLinksThatAreNotFiling`, so it counts as filing.

The real cause is the #2468 rule in `ResolveOwnerOnlyAsync`: a document whose primary parent is a communication filed under nothing demotes that communication to a Secure-only parent, so the document never follows its unfiled Direct message.

The fix is option (a): skip the demotion only when the communication's owner is positively recognized as the Direct message owner team. "May be" is not enough.

197a-1 changes no reader of the document-link list.

## Known limits

- **Stale document owner after a #2722 move.** When an unfiled non-Direct communication moves to its creator's team, a document filed under it keeps its previous non-secure owner. That team keeps access it no longer needs (a stale owner, not a leak to anyone outside the earlier audience). Option (b), making the document follow, was rejected for now; the coordinator files the issue.
- **No recorded creator.** A message created where `sprk_createdbyperson` is not stamped (for example, directly in the model-driven app) keeps its former owner on unfile, by owner decision: never fall back to `createdby`.
- **Path (a) settle failure.** If the settle on a regarding-only clear fails, the link change stays written and the former owner is kept, logged with ids only (5420, Warning). The change is not refused: it has landed, and a retry would clear no link, so it could not move the owner either. The owner is not retried later.

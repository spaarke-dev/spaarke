# Task 056a — Private messages in a record's context: design note

> Status (2026-11-22): **056a APPROVED by UAC-r2 at 51419e7768**; 056b in progress (task/fm-056b). C1–C6 and UAC-r2's three wording clarifications are in this note. The owner's
> answers are in (`notes/decisions.md`, 2026-11-22): all as recommended, except Q2.4 (either behaviour allowed; the
> easier one, no revoke, is kept) and Q3 (the refusal text is the owner's). No code until UAC-r2 has checked this revision.
> Issue: #2923. Starts from: MR-1 in `projects/messages-enhancements-r1/requirements.md`, which holds UAC-r2's access
> rules of 2026-11-09. This note keeps those rules and states how the current code meets them.
> Code references were mapped on 2026-11-22 from `work/field-mapping-server-write-path-r1` at 89c9efb43b; line numbers drift.

## 1. The requirement and the shape chosen

**Requirement (owner, 2026-11-20).** A user can send a private message to a named user in the context of a record.
Only the sender and the named recipient read it. Other readers of the record and its threads do not read it.

**Shape (MR-1, recommended).**
- A private message is a **Direct message**: an internal Message row filed in the pair's Direct thread, stamped with
  `sprk_sentby` + `sprk_directrecipient`, owned by the Direct team and shared with the pair (054, UAC-r2 task 198).
- It also carries a **non-filing context reference** to one core record (matter, project or work assignment).
- The reference decides where the message is **shown**. It never decides who **reads** it.

Nothing about ownership, grants or the Direct thread changes. The work is:
- the reference column;
- one send-path branch that sets it, after checking both participants;
- one per-viewer read;
- the record-surface UI.

### Owner question Q0 — "in the record's thread": a display or a storage location?

The owner's wording (2026-11-20, #2923) is "inside a communication thread associated with a record". This note reads
that as a requirement on **what the two participants see**, not on which thread row holds the message.

**Recommendation: the message lives in the pair's Direct thread and is shown in the record's conversation.** For the
sender and recipient only, it appears in the record's conversation view, marked "Private · you and Jane". Storing it in
the record's own thread was considered and rejected for three reasons:

1. **Ownership.**
   - The thread lookup `sprk_communicationthread` is a filing lookup for `sprk_communication`
     (`SecureChildLineage.Children`, `LookupOrder`).
   - A message filed in a record's thread therefore has an ownership parent. `RecordOwnershipResolver` gives it the
     record team's owner.
   - The Direct owner step requires "no ownership parent" (`DirectMessageOwnerStep.WhyNotDirect`).
   - Making it work would need a second owner rule for one kind of message inside UAC-r2's ownership core. That breaks
     "one server-side owner per invariant".
2. **Record-scoped readers select by thread or by filing column.**
   - The by-regarding read selects by the thread and the typed regarding columns.
   - The external plane's app-only reads select by regarding and by core-ancestor stamps
     (`CommunicationThreadReadService.ReadRegardingAppOnlyAsync`, `ReadRecentForRecordsAppOnlyAsync`).
   - Any of these would see the row.
   - Row security would still hide the content from an internal caller. Thread-level facts would remain, such as a
     message count, the last-message time, and unread state.
3. **Filing later becomes ambiguous.** A message that is already in the record's thread has nothing left to "file".

If the owner means storage in the record's thread, this note goes back for a new design. That shape needs changes in
UAC-r2's ownership files (POML 056 escalation trigger 1).

## 2. Answers to the six questions (POML 056 step 1)

### Q1. What other readers of the record and its threads see

**Recommendation: nothing.** No row, no count, no placeholder.

- The message has no filing lookup. The by-regarding read, the thread reads and the external plane's app-only reads
  never select it.
- A placeholder ("a private message between A and B") would need a reader that selects by the context reference
  outside the pair. That is exactly the reader MR-1 rule 3 forbids. A placeholder also tells record readers that the
  pair talk about this record.

### Q2. Secure and Restricted records

**Recommendation: allowed, on MR-1 rule 2's terms.** Every check runs before anything is sent.

1. **Both participants hold Read on the referenced record.**
   - The sender is checked as the caller (`CallerRecordAccessProbe.GetCallerRightsAsync`).
   - The recipient is checked as the named principal (`CallerRecordAccessProbe.GetPrincipalRightsAppOnlyOrUnknownAsync`,
     `RetrievePrincipalAccess` for that systemuser). An unanswered check gives 503 (Q2.3); nothing is sent.
   - Precedent: `AssignTaskRecipientGate`.
   - **U1, accepted by UAC-r2.** `RetrievePrincipalAccess` for the named systemuser is Dataverse's answer for that
     principal. No `CallerId`-impersonated read is needed.
   - **C1 (No Access wall).** Dataverse's rights answer can lag a No Access entry until the share reconcile removes the
     share. So when the record is secure, or files under a secure parent, BOTH participants must also pass
     `SecureShareNoAccessGuard.CheckRecordAndSecureParentsAsync`. Walled gives the refusal text; the guard's Unverifiable outcome gives 503 (Q2.3). The
     guard is reused as is, not forked (callers today: `InternalUserShareCore`, `GrantAccessNotifier`).
2. **Restricted record (C2).**
   - A participant flagged external is refused (`InternalShareEndpoints.IsBarredOnRestricted`).
   - Both participants are checked.
   - The flag checked is the record's **effective** Restricted flag through its filing (`EffectiveRootFlags`, round
     84), not only its own stored value.
   - The Restricted flag decides only for an external participant. When a participant is external and the effective
     flag or the external flag cannot be read, the answer is 503 (Q2.3) and nothing is sent. When both participants are
     internal, an unreadable Restricted flag does not change the answer, and the send goes ahead on the Read and No
     Access checks.
3. **One refusal for every recipient-side cause.** The text is the owner's: "Recipient does not have required record
   access".
   - It covers the recipient not reading the record, the No Access wall, and an external recipient on a Restricted
     record. It never says which.
   - The sender learns only that this person lacks access to this record. That is what any refusal of this send tells
     them, and nothing about the record's flags.
   - **Sender side (UAC-r2 clarification, 2026-11-22).** C1 and C2 apply to the sender too. Every sender-side cause gives
     "You do not have required record access". The causes are: no Read, walled, external on a Restricted record, and an
     unknown or nonexistent record id (NFR-01: unknown and denied give the same answer).
   - **Order.** The sender's checks run in full before any recipient check, so a sender who fails learns nothing about
     the recipient's access. A test pins this order.
   - **Unanswered checks.** A check that cannot be answered gives 503 "try again": a rights question with no answer, the
     guard's Unverifiable outcome, or an unreadable flag. Walled is an answer and gives the refusal text. Either way
     nothing is sent. Configuration problems keep their existing 409.
4. **After the send (C6, Q2.4).** The owner allows either behaviour ("not required if easier to remove access"), so the
   easier one is chosen: nothing revokes. Removing access would need new machinery that watches record and user access
   changes. None of these take the message from either
   participant:
   - the record later becoming Restricted;
   - a later No Access entry;
   - a participant losing Read on the record.

   The message is the pair's own correspondence. It has no filing parent, so UAC-r2 task 198 revokes nothing on it. The
   record reference (name, number, link) is resolved **as the viewer** and hidden from a viewer who can no longer read
   the record.

Secure records need no extra rule. A private message is never filed (Q5), so the 2026-11-09 rule ("participants keep
their shares when a Direct message is filed, except on secure or Restricted targets") never applies to it.

### Q3. A recipient who cannot read the record

**Owner (2026-11-22): refuse the send** with "Recipient does not have required record access" (Q2.3).

- **Optional:** the client offers "Send as a direct message instead". That sends the same text with no context
  reference through the existing Direct path. It is built only if it is a small addition in 056b.
- Rejected: allowing the message and hiding the reference from the recipient. The sender would believe they are
  discussing the record with someone who cannot see it. That is the case MR-1 rule 2 refuses.

### Q4. Ownership

**Recommendation: reuse the Direct-team model unchanged.** Owner: the Direct team (054 piece 3). Audience: the stamped
pair. Grants: UAC-r2 task 198.

- The context reference is neither a typed regarding column nor an ownership parent. The send stays an outbound Direct
  send (`IsOutboundDirectSend`, `DirectMessageRecipient.CarriesATypedRegarding`), and `DirectMessageOwnerStep` applies
  as it does today.
- **Fail closed (AC3).** The reference never widens the audience, whatever happens after the send.
  - **Before the send.** The context checks, the pair resolution and the Direct-team check (`CheckBeforeSendAsync`)
    each refuse with nothing sent: neutral 4xx, 409 or 503.
  - **After the send.** If the owner step or a grant fails, the message keeps its prior owner, as every Direct message
    does today. That owner is not the record's team, and the row has no filing lookup, so record readers never gain
    access. The existing recovery paths apply: the 2-minute share reconcile and the 054c script.
- A private message is always `sprk_isinternalonly = true`. The pair are internal users.

### Q5. Filing to the record later

**Owner (2026-11-22): never.** "let's just solve the 1:1 which is it always stays 1:1 - no filing". This supersedes the
earlier recommendation (filing allowed, explicit, behind a confirmation), which the owner had first approved.

- **The rule.** A private message is never filed and never moved to another thread, by any writer: the filing route
  (`PATCH /api/communications/{id}/filing`), the AI update tools, push, Office reconcile, thread moves and inbound
  association. Its audience is its two participants for its whole life.
- **Why.** Filing makes a message a record message, readable by everyone who reads the record, and the reference would
  go with the row. Field security on the reference columns governs writes only (the 054b readers profile is on every
  business unit's default team). So the pair-only audience of the row is what keeps the reference private, and filing
  would end it.
- **Where it is enforced.** One refusal in the re-file core, reached by every writer, with a stable code. A reference
  column that is absent from metadata passes (no private message can exist), and a read fault refuses (fail closed).
  Placement and authorship are agreed with UAC-r2 before code.
- **The reads.** The `about` reads stay caller-impersonated, so row security decides who reads. The sender/recipient and
  no-filing filters only narrow what is displayed (U2, agreed by UAC-r2, on C3).
- **A participant who wants the record's team to see something** sends an ordinary message on the record.
- **A standalone Direct message cannot gain a reference later.** The reference is written once, at create, by the BFF.
- **Follow-up (not built here):** invite-only conversations. Participants could add named people, each passing the
  record-access check, so a conversation widens only by invitation. Filed for `messages-enhancements-r1`.

### Q6. Where private messages surface

| Surface | Shown to | How |
|---|---|---|
| The record's conversation view (record panel PCF, Communications widget, conversation page with a record filter) | sender and recipient only | A per-viewer read **as the caller**: messages whose reference is this record, with the caller as `sprk_sentby` or `sprk_directrecipient`, and no filing lookup. They are grouped per counterpart as a "Private · you and Jane" entry in the record's thread list, never mixed into a record thread. |
| Messages view (055) | sender and recipient | Unchanged. The message is in the pair's thread. The bubble shows an "About Matter X" chip, resolved as the viewer and hidden if they cannot read the record. |
| Record counts, previews, unread, timeline, external plane, notifications about the record | nobody | None of them read the reference (§3.4 guard). |
| AI tools | the pair | `DataverseReadQueryHandler` is user-OBO only, so row security limits it to the pair. No communication content is indexed for RAG today; a future index carries the pair as its access list or leaves the reference out (MR-1 rule 3). |
| Notification to the recipient | recipient | The existing Direct-message arrival, with no record named in the toast. |

## 3. The build (056b), for review now

### 3.1 Schema: three new lookups on `sprk_communication`

`sprk_privatecontextmatter`, `sprk_privatecontextproject` and `sprk_privatecontextworkassignment`, one per core table.
- At most one is set.
- The BFF writes them at create only.
- They are field-secured with the 054b columns: add them to `scripts/Set-ThreadFlagFieldSecurity.ps1` `$Targets` and
  `$Lookups`, and to `InheritedAccessPermission.BffOwnedCommunicationColumns` (kept equal by
  `CommunicationBffOwnedColumnRefusalTests`).
- They are created by a schema script in the `Set-DirectRecipientSchema.ps1` pattern.
- Root §11 justification:
  1. **Existing component.** The typed `sprk_regarding*` lookups.
  2. **Why not extend it.** Any of them files the message, which makes it record-visible and takes it out of the
     Direct owner rule (`CarriesATypedRegarding`).
  3. **Cost of doing nothing.** A Direct message cannot point at a record without becoming a record message, so AC1
     and AC2 cannot both hold.
- **Why three typed columns, not one polymorphic lookup or a text id.** Typed lookups get Dataverse referential
  integrity and field security per column, and they match the repo's pattern. A free-text id would bypass field
  security on the target.
- **Relationship cascade (C4).** Each lookup creates a 1:N relationship from matter, project or work assignment to
  `sprk_communication`.
  - Its cascade is Assign, Share, Unshare and Reparent = **NoCascade** (referential), and Delete = **RemoveLink**. A
    cascading Share or Reparent would push the record's shares onto private messages.
  - The schema script sets this. Its `-Verify` reads the relationship metadata back and fails when any of those
    cascades is Cascade, with a must-fire control.
  - The relationship's navigation is hidden from the record forms. This is cosmetic only, since row security already
    limits it to the pair.
- **Live order** (owner's go for each environment): create the columns → `Set-ThreadFlagFieldSecurity.ps1 -Apply` /
  `-Verify` → BFF. A BFF that finds the columns missing refuses a private send, 409 with the script named, as 054 does
  for `sprk_directrecipient`.

### 3.2 Send path: one branch in `CommunicationService`

- **Request.** `SendCommunicationRequest` gains `PrivateContext { EntityType, Id }`.
  - It is valid only with `CommunicationType.Message`, exactly one internal recipient in To/Cc/Bcc, no `Associations`,
    no `InheritRegardingFromCommunicationId`, and no `ThreadId` or the pair's own thread id.
  - Anything else is a 400.
- **Before the send.** The Q2 checks for both participants and the Restricted check, then the existing Direct
  pre-checks.
- **Row build.** The reference column is set at row build. The existing Direct path then runs unchanged: pair thread,
  stamps, owner step, grants.
- **Files.** `CommunicationService.cs` and its DTO, plus one helper for the participant checks.
  - **Not touched:** `ThreadResolver` (no new thread key; the reference is on the message, as MR-1 recommends), and
    every UAC-r2 access file.

### 3.3 Read path: extend the 055 list and the thread read; no new endpoint

- `GET /api/communications/threads?kind=direct&about={entityType}:{id}` lists the caller's pair threads that hold at
  least one private message about the record. It is caller-impersonated and narrowed like 055.
- `GET /threads/{id}/messages?about=…` returns that thread's messages about the record, with a link to the whole
  conversation.
- Both filter on the caller being the stamped sender or recipient and on no filing lookup.
- **Single reader.** These are the only reads of the reference columns.

### 3.4 Guards (ArchTest, with must-fire and must-not-fire controls)

1. **Registries.** The three columns appear in none of: `RegardingFieldMap`, `SecureChildLineage.Children` lookups,
   `RecordFamilyEdges`, `InvoiceLinks`, `ScopeDimension`, `RootLinks`. This keeps them out of `FilingParentOf`,
   `DirectMessageParticipantShares.FilingColumns` and task 198's grant and revoke.
2. **Allowed readers and writers.** Only an allowlist of source files names the columns:
   - the send-path writer;
   - the per-viewer reader;
   - the field-security list and the refusal test;
   - the schema script.

   The scan follows constant aliases to a fixed point, as `ThreadFlagUpdateWriterGuardTests` does.
3. **More selectors (C5).** The registry guard also asserts the three columns are absent from:
   - `CoreAncestorResolver`'s ancestor map for `sprk_communication` (the external plane selects by core-ancestor
     stamps);
   - the communication scope in `Tier2ScopeFilterInjector` and `ExternalModuleRegistry`.

   Covered by derivation, nothing more to add:
   - Task 198's grant and revoke select by the Direct-team owner, the content rows and
     `DirectMessageParticipantShares.FilingColumns`. `FilingColumns` is derived from
     `SecureChildLineage.Children[sprk_communication].LineageColumns`, not a literal list, so rule 1 covers it.
   - Runtime metadata discovery of lookups: `MembershipFieldDiscoveryService` keeps only lookups whose targets are
     identity tables (systemuser, team, account, contact). The three columns target core records, so it ignores them.
     056b adds a must-fire check that it does not return them.
   - The field-mapping relationship read (`FieldMappingEndpoints.ReadManyToOneRelationshipsAsync`) only lists choices
     for an admin. A mapped write into the columns is refused for every identity, the push included, once they join
     `BffOwnedCommunicationColumns` (`ParentLineage.RefuseBffOwnedCommunicationColumn`). 056b adds a must-fire test that
     a Copy rule targeting one writes nothing on create or push.

### 3.5 UI (Spaarke.UI.Components)

- The record's conversation view gains a **"Send privately…"** action. It reuses the `NewDirectMessageModal` person
  picker, with the record fixed as context, and sends with `privateContext`.
- The thread list gains the per-counterpart "Private · you and Jane" entries (§3.3).
- The neutral refusal shows the "Send as a direct message instead" offer (Q3).
- The bubble shows the context chip.

### 3.6 Tests (KEEP paths)

- **Contract.** The 400 shapes and the neutral refusal.
- **Data-mutation and auth.**
  - AC1: the pair reads.
  - AC2: a third user who reads the record and its threads gets nothing from the by-regarding read, the thread reads or
    the `about` read.
  - AC3: each pre-send check refuses with nothing sent; the post-send owner fault leaves no record reader with access.
  - AC4: an ordinary record send is byte-identical.
- **Seam.** The `about` list and read.
- **RTL.** The "Send privately" flow.
- **Guards.** §3.4.

## 4. ADR and boundary check

- **ADR-002.** No plugin. One owner per invariant: the Direct owner step (unchanged) owns ownership, the send path owns
  the reference, and the per-viewer read owns display. The client only previews.
- **ADR-028.** The recipient check is a named-principal rights question, with no new credential (U1).
- **ADR-008 path A.** The payload-dependent checks are in the service, as for the other create checks in this project.
- **UAC-r2 access files.** No edit is needed if U2 is "leave the reference on filing".
- **2026-11-08 record-reader rule** ("a message filed under a record is readable by whoever can read the record").
  Unchanged, because a private message is not filed.

No ADR conflict found.

**Known limit carried.** A private message is a Direct message, so #2959 applies to it as to any other: a capped
Direct listing in `SecureChildShareReconciliationJob` moves its watermark to the horizon with no overlap. That is UAC-r2's
code, and it is open.

## 5. Questions

| # | For | Question | Answer (owner 2026-11-22; UAC-r2) |
|---|---|---|---|
| Q0 | owner | "In the record's thread": shown in the record's conversation, or stored in the record's thread row? | Shown there, stored in the pair's Direct thread (§1) |
| Q1 | owner | What other readers see | Nothing |
| Q2 | owner | Secure and Restricted records | Allowed if both participants read the record and pass the No Access check; external participants refused on an effectively Restricted record; one refusal text for the recipient-side causes |
| Q2.4 | owner | After the send: does a later Restricted flag, No Access entry or lost Read take the message from either participant? | Either is acceptable (owner); the easier is kept: no. It stays theirs, and the record reference is hidden from a viewer who cannot read the record (C6) |
| Q3 | owner | Recipient cannot read the record | Refuse with "Recipient does not have required record access"; the "Send as a direct message instead" offer only if small |
| Q4 | owner | Ownership | Direct team + the stamped pair, unchanged |
| Q5 | owner | Filing later | **Never** (owner, 2026-11-22, superseding "explicit, confirmed"): a private message always stays 1:1 and is never filed or moved to another thread; one refusal in the re-file core. No reference is added to an existing message |
| Q6 | owner | Where it surfaces | Record conversation view (pair only, per counterpart) and the Messages view |
| U1 | UAC-r2 | Recipient check | **Answered:** `RetrievePrincipalAccess` accepted, with C1 (the No Access wall) |
| U2 | UAC-r2 | Reference left as is on filing | **Answered:** agreed, with C3 (impersonated reads). Superseded by Q5 "never": the field security on the columns governs writes only, so the reference stays private because the row is never filed |
| U3 | UAC-r2 | Guard coverage | **Answered:** covered, with C4 (relationship cascade) and C5 (more selectors) |

## 6. As built (056b): "never filed" enforcement, deploy order, known limits

**One predicate.** `PrivateContextColumns.IsPrivate(row)` (any reference column holds a non-empty lookup) is the only test of "private message". Every site below uses it.

**Where "never filed" is enforced.**
- **Writes through the BFF.**
  - `RecordOwnershipResolver.ReparentAsync` refuses a re-file or thread move of a private message (`sdap.communication.private.not_fileable`, 409).
  - `PrivateMessageWriteGuard` is asked before every generic update of a communication that names a filing column, a typed regarding, the regarding pair, the thread or the owner. Its callers are the filing route (`OwnedChildWrite.RefileAsync`), the update handler, the AI update tool, the field-mapping push, Repair and inbound association.
  - A read fault refuses with `sdap.communication.private.check_unavailable` (503).
  - An ArchTest census keeps every generic writer on the guard.
- **Links set outside the BFF.** The 2-minute Direct-message share reconcile clears any filing columns that exist on a private message, and the pair's display columns, app-only, on a row-version condition.
- **Until the clear runs, these never act on a private message:**
  - the secure reconcile (skips the row);
  - the app-only record reads (excluded in the query);
  - the core-ancestor stamp (no ancestor; a held stamp is cleared);
  - the secure-child share mirror (placed under no root).
- **Column presence.** Every security check asks metadata without remembering a "missing" answer. Only the caller-impersonated thread read, which uses the answer only to decide which columns to select, remembers it for 5 minutes.

**Deploy order, per environment.** Run the schema script (`Set-PrivateContextSchema.ps1`), then field security (`Set-ThreadFlagFieldSecurity.ps1 -Apply`, then `-Verify`), then the BFF. Each step needs the owner's go.
After `-Verify` passes and before the BFF goes live, run a read-only count of `sprk_communication` rows that hold any `sprk_privatecontext*` value. It must be 0, because no deployed BFF writes those columns yet. Send the count to unified-access-control-r2. A nonzero count holds the deploy (UAC-r2 approval of 056b, 2026-11-23).

**Known limits.**
- A refused private send can leave the pair's Direct thread with no message in it.
- If the message row is created and a later send step fails, a retried echo saves an ordinary Direct message without the reference.
- With both participants internal, an unreadable Restricted flag does not block the send (Q2, rule 2).
- After a participant loses access to the record, the reference stays visible to them until the next share reconcile.
- A failed out-of-band clear is retried on the next reconcile pass that reaches the row.
- The secure-child share mirror reads the reference columns on rows it reads itself. For rows another job hands it, the secure reconcile's private-row skip applies.
- Assigning a Direct message to another owner in the model-driven app follows the platform's Assign semantics, for every Direct message. This is tracked in #2979, not in 056.
- Invite-only conversations are a follow-up: #2977.
- On a private message, a write that clears a filing link is refused too. The out-of-band clear removes it instead. Filing links decide nothing for a private message.

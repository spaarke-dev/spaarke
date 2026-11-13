# uac-r2: owner test checklist (2026-11-13)

Dev BFF `b100effac3` (master `c2f366bf52` + mcp spike), live since 2026-11-13 20:39Z. Its 30-minute server gate passed.
Tick each box, or tell the uac-r2 session what failed: the record, what you did, what you saw.

## 1. Nothing to click (server-verified today)

- **#2761 identity-link job.** The job now skips the ACME customer's registry row instead of failing on it every 5 minutes. Verified in the logs: 6 skips, 0 failures.

## 2. Quick browser checks on the new build

- [ ] **#2722: unfiling a communication (197a).**
  - [ ] On an ordinary (not Secure, not Restricted) matter, open a communication filed on it and clear its regarding record. Its **Owner** becomes your business unit's team, not the matter's team.
  - [ ] Repeat on a **Secure** matter. The owner stays the Secure Record Owners team, because the message keeps its secure owner.
  - [ ] Repeat on a **Restricted** matter. The owner stays as it was.
  - If all three pass, tell uac-r2 and #2722 is closed.
- [ ] **Your E2E retest** with spa-r4, paused earlier today.
  - If an email send fails, it now shows a visible "send failed" error instead of retrying silently (ontology 191, your decision D-223). That error is expected and not a defect.

## 3. Open UAT items (from 2026-11-11)

Full steps: `notes/wrap-up-uat-and-decisions-2026-11-11.md` §B.

- [ ] **U-1:** sign in to the external portal as a contact. You see only granted records; Restricted, Secure and No Access apply.
- [ ] **U-2:** a Collaborate contact grants a same-org colleague at or below their level, cannot grant other orgs; View Only cannot grant. Needs four portal identities.
- [ ] **U-3:** No Access, Assigned-To, Make Secure, filing children, Restricted: the forms behave as designed. Passing it unlocks decision D-10.
- [ ] **U-4:** the red access banner, the "Secure" pill and the Manage Access message bar. As testuser1 in InPrivate: no red banner and no sign-in error.
- [ ] **U-5:** documents: Word edit on a secure record; a contact opening documents on a secure container.
- [ ] **U-6:** SPE Admin app: list, open and edit your config; bulk delete on a throwaway container.
- [ ] **U-7:** Manage Access expiry: pick a date, apply, add a contact; the expiry shows and saves.
- [ ] **U-8:** a user's write to a record they can only read is refused; their reads match Dataverse.
- [ ] **U-9:** a workforce first sign-in registers; a second sign-in binds to the same contact.
- [ ] **U-10:** the remaining form checks (listed per task in the UAT note).
- [ ] **U-11:** Direct 1:1 messages. **Blocked:** it needs the D-2 step 3 go first (§4).
- [ ] **U-12:** workforce intake is accepted; a licensed user signs in to the SPA and still resolves as a systemuser.
- [ ] **U-13:** move a test matter to another business unit; its children follow.
- [ ] **TrackingFieldTrio 1.0.63:**
  - Email members on PRJ-000046 and on a To Do.
  - As a user without Write+Share.
  - The bell.
  - The To Do empty-state wording.
- [ ] **Outlook add-in checks 3–7:** repeat them on this build (word-add-in-r1 holds the list).

## 4. Waiting on your go (decisions, not tests)

| Item | What it does | Note |
|---|---|---|
| **D-2 step 3: Direct message setup + the two `DirectMessage:*` settings** | Turns on Direct 1:1 message ownership on dev | Restarts the BFF. Unblocks U-11, the #2728 browser check and fm's 054c |
| **#2764** | Decide how the share engine treats a transient "undecided" answer | Must be decided before the 191c share settings go on |
| **191i live steps** | The flagged-external migration on dev, one script at a time | Each step is its own go |
| **193 PCF import on dev** | Manage Access "Add person" | — |
| **D-10** | Job revoke-on-change setting | After U-3 passes; restarts the BFF |
| **#2786 part 2** | Change a grant's level inline in Manage Access | Your scope call: build now, or later |
| **#2799 field lock `-Apply`** | Locks the invoice family links so only the BFF writes them | In build now. The go comes after its PR merges and the BFF that writes as the app is deployed |

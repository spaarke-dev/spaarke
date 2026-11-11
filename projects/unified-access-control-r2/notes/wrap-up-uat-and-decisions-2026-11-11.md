# uac-r2 wrap-up: owner tests (UAT) and decisions (2026-11-11)

The steps for each test are in [`owner-hands-on-checklist-2026-10-08.md`](owner-hands-on-checklist-2026-10-08.md) ("CL §n"). All of this code is merged to master and running on dev, apart from the 198 chain (see D-2). Under round 90, these browser checks do not hold their tasks open. A failed test becomes a defect that is fixed or filed.

## A. Decisions

| # | Decision | Options | Recommendation |
|---|---|---|---|
| D-1 | Close the tasks whose code is merged, deployed and server-gated, and move their browser checks to this table (036, 099, 135, 136, 037, 039, 013, 141, 140, 153, 165, 171) | (a) close now, UAT carried here; (b) keep each open until its UAT passes | **(a).** This is the round-90 rule. Each failed UAT is fixed or filed as a defect |
| D-2 | Dev deploy order for the 198 chain and the ebilling batch. Master now carries 198, so any dev deploy from master deploys it | (a) deploy master to dev now, and keep 198's Direct pass inert until 054b is verified; (b) hold every dev deploy until field-mapping's 054b field security is applied (fm says that is not hours away) | **(a).** 198 grants only on messages owned by the Direct owner team. That team is named by `DirectMessage:BusinessUnitName` / `DirectMessage:OwnerTeamName`, and neither is set on dev. Unset counts as "not Direct" (`IsDirectOwnerTeamAsync`: NotConfigured → false), so nothing is granted and nothing is assigned to Direct. The rule is: **those two settings stay unset on dev until 054b's field security verifies.** Setting them belongs to the Direct role setup step, which needs your go in uac-r2. The order is: (1) dev BFF from master, now; (2) 054b -DryRun / -Apply / -Verify (your go in fm's session); (3) the Direct setup plus the two settings (your go here); (4) 054c Stamp; (5) piece 3; (6) 054c Move. The deploy must also be coordinated with mcp-r1, because dev runs their spike `755987b385`, which master does not contain |
| D-3 | #2033: a user disabled in Dataverse but active in Entra can still send mail | (a) refuse the send; (b) allow it and record it | **(a) refuse.** It fails closed (NFR-01). Filed outside the round-90 scope, so it lands as a small follow-up fix |
| D-4 | #2245: BFF-owned document columns (`sprk_driveitemid`, `sprk_filepath`, …) are writable by users | (a) field-secure them; (b) move readers to locked copies (`sprk_graphitemidbound` exists for the item id); (c) a follow-on project | **(c), with (b) as the design.** Field security would break the create paths that write these columns as the user. The item id is already guarded by the round-72 bound check |
| D-5 | #2250: app-only playbook writers can set `sprk_assigned*`, which causes an Assigned-To grant with no grantor | (a) refuse on the app-only playbook writers; (b) accept it | **(a).** It is a narrow guard on those writers only |
| D-6 | #2270 / #2278: contacts created in a foreign environment are ownerless (you chose a customer-BU column) | (a) build it in uac-r2 now; (b) carry it to the follow-on secure-family project | **(b).** It needs a schema column and a backfill, and nothing is exposed meanwhile (a contact is not a security principal) |
| D-7 | #2203: `sprk_agreement` becomes a core record | (a) in uac-r2; (b) roadmap | **(b) roadmap.** It is a model change outside the release scope |
| D-8 | 036 gate (iii) needs a User-depth (Basic) read role on dev | (a) create the role and test; (b) accept the unit proof, since (iii) cannot happen on dev | **(b).** Dev users are root-BU, so the check proves nothing there |
| D-9 | 184 G3 "ExistingAccount" proof | (a) fixture contact; (b) provisioning task T240d, the real case; (c) offline proof | **(b).** It is the real path |
| D-10 | 142 G-8: turn on the job revoke-on-change setting once G-7 passes | (a) set it after G-7; (b) leave it off | **(a)**, after the 142 UAT (U-3) passes. Changing the setting restarts the BFF, so it needs your go |
| D-11 | 166 (f): a non-admin without Create | (a) a live test with such a user; (b) accept the tests as proof | **(b).** Server tests cover it, and it is a UI-only negative |
| D-12 | ebilling task 116, condition 1, revised: a redraft uses the same rights a reject needs, checked as the user; then the app writes. No role or team change | (a) accept; (b) give admins Local-depth Write on the rule tables | **(a).** (b) widens Write to published rule versions across the unit through the form. ebilling is putting this to you |

## B. Owner tests (UAT)

| # | Area (task) | What to check | Where |
|---|---|---|---|
| U-1 | External portal sign-in session (135, 136, 037, 039, about 20 min) | A CIAM contact sees only granted records. A None-rights root shows nothing, document content included. Restricted and Secure apply to contacts. A No Access entry hides the record | CL §14 |
| U-2 | Contact grants colleagues (140) | A Collaborate contact grants a same-org colleague at or below their own level. It cannot grant other-org contacts, and View Only cannot grant at all. Needs four CIAM identities | CL §4 "140" (a)–(h) |
| U-3 | No Access, Assigned-To, Make Secure, filing children, Restricted (154, 143, 142, 150, 147/168/169, 114) | The forms behave as designed. The 114 extras: Share is hidden on Restricted; the "External user, no access" label; the no-internal-reader and owner-is-external admin reports | CL §1; `batch5-live-gates-2026-10-07.md:62-65` |
| U-4 | Access-status banner (153) | A red text banner on Secure or No Access project/matter/work-assignment forms; the "Secure" pill; the Manage Access message bar. Also as testuser1 in InPrivate: no red banner, no AADSTS50058 | CL §7; `batch5-live-gates-2026-10-08.md:553` |
| U-5 | Documents broker-only (171) | Word edit on a secure record (JIT writer); a contact opening documents on a secure container (E); D3/B2/J4 | CL §3, §4 "171 E", §5 |
| U-6 | SPE Admin app (165 d) | List, open and edit your own config; bulk delete on a throwaway container | CL §5 |
| U-7 | Manage Access expiration picker (099c) | Pick a date, Apply, add a contact. The expiry shows and saves | `batch7-live-gates-2026-10-17.md:142` |
| U-8 | Read set + write checks (036, ii/iv/v/vi) | End-to-end: a user's write to a record they can only read is refused; reads match Dataverse | `batch7-live-gates-2026-10-17.md:605-609` |
| U-9 | Workforce sign-in binding (141 G-7/G-8, 013) | First sign-in with a home-directory account, then registration; a second sign-in binds to the same contact; no email-based hijack | `task-141-identity-binding.md:346-347` |
| U-10 | Remaining form checks (162, 163, 164, 166, 171 J1, 137, 157, 105, 067, 173, 175, 180, 185, 187) | As listed per task | CL §2, §4, §6, §8–§13 |
| U-11 | Direct 1:1 messages (198, after the D-2 deploy) | Send a 1:1 message: both people see it, nobody else does. File it on a record: both keep access. File it on a secure record: Secure rules apply. Unfile: back to the pair only | — (198 notes §7) |

## C. Agent work left (no owner input needed)

- **090 wrap-up:**
  - the TASK-INDEX reconcile;
  - the defer/issue disposition (the hand-off list comes back to you for one batch confirmation);
  - `/test-diet`;
  - the H-8a stale comments;
  - the docs absorbed from 058/082/087/089;
  - lessons learned;
  - README → Complete.
- 198 deploy gates after D-2.
- Then the full code review you asked for.

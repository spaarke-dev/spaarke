# Owner decisions carried over from uac-r2 (verbatim excerpts)

Copied on 2026-10-14 from `projects/unified-access-control-r2/notes/session27-owner-decisions-and-research.md`. That file stays the record; this is a snapshot for design work. Where they differ, the uac-r2 file wins.

## Round 87 (2026-10-09): BINDING. OWNER decision: the parent sets a FLOOR; a child may be stricter (refines round 84)

Asked during task 175 (PR #1503). Verifier pass 1 had found F1: a re-file un-secured a work assignment without F3. Owner answers, verbatim where quoted:

1. **Inheritance:** "a child created on a secured (or whatever access level) [parent] is inherited by its children; if the secure parent is made unsecure then the children are changed to unsecure; but a user can update the child of an unsecure back to secure."
2. **Own vs inherited:** a value the user set ON the child stays when the parent later loosens. Only INHERITED values follow the parent down. (Example: the child is made secure by hand, the parent is later secured and then un-secured, and the child stays secure.)
3. **Access Permission** works the same way as Secure. The parent's value is the minimum. A user may set a child stricter by hand; looser than the parent is refused (locked). When the parent loosens, inherited values follow it down.
4. **Who may:** the same as today. Anyone who can manage access may make a child stricter, as Make Secure works now. Removing the extra strictness, never below the parent, needs Full Access or the creator (F3).
5. **Re-file:** a secure work assignment with no parent, filed under an ordinary matter, STAYS secure. A re-file never loosens a child; it can only raise it to the new parent's floor. This closes verifier F1.
6. **Lock scope:** Secure and Access Permission only. People can still be granted or shared access on a child within the parent's limits. `/grant`, `/share-user` and Update Access stay available.
7. **Documents:** "if it is technically easier for documents to then remain in both containers that is acceptable". Owner rule to confirm against the build: "in a secure 'family' of records all documents added to any of the members of the family will go to the same secure container".

**Supersedes:** round 84's "always equal parent / locked" where it conflicts. The parent is a floor, not an equality. Round 84's un-secure cascade still applies, to INHERITED values only.

## Round 88 (2026-10-09): BINDING. OWNER decision: one SPE container per secure family (#1506)

Asked after a build check showed that every secure record gets its own container, including inherited-secure ones (task 158). The consequence: uploads under a secure work assignment filed under a secure matter are refused with `container_ancestor_ambiguous` (#1506).

1. **Family container:** the TOP-most secure record in the chain owns the one container. Every record secure because of it uses that container: work assignments, projects, and their To Do / Event / Message / Document records. No new container is created for inherited-secure records. A record secured on its own, with no secure ancestor, starts a new family with its own container.
2. **Joining a family:** new files go to the family container. Existing files may stay where they are. The owner adds: "is it more efficient and easier that if a record already has a secure container, and a new family member is added then that secure container becomes the family's container". In other words, adopt an existing secure container as the family's container instead of creating a new one, where that works. The owner also asked whether search is container-constrained, i.e. whether a search can only be directed at one container. That is being researched; the answer goes to the owner before the design is fixed.
3. **Un-secure:** the files MOVE to the business-unit container. They do not stay in the old secure container. This replaces round 87 item 7's "remain in both containers is acceptable" for the un-secure case.

**Status:** the design goes in a new task (container routing for secure families). It must not land in task 175, which leaves container routing untouched.

## Round 94 (2026-10-12): OWNER: external file access to secure records, and the secure-family model

**External file access** (the investigation is at origin/master `91cbac7b8`): contacts never hold an SPE permission. Every byte is streamed by the BFF, app-only, after the evaluator decides, through `/api/v1/external` routes in `ExternalProjectDataEndpoints.cs`. Decisions on the gaps:
- **G1 (owner restatement): a file is filed to exactly ONE record.** A document in a secure container is reachable only by those with access to that secure document. File bytes that also exist outside the secure container belong to a separate Document, controlled by its own record. Consequence: the external document routes authorize by the document's OWN filing record (the one whose container holds it), not by `_sprk_project_value` alone, and no document may confer access through a second record.
- **G2: enforce document-level Restricted on external routes.**
- **G3: approved as recommended.** On Make Secure, Restricted, a No Access entry or a revoke, enumerate and delete the item's sharing-link permissions app-only. Delete relocated sources permanently, not soft-delete. Retiring links altogether (#1350) is still acceptable.
- **G4: approved as recommended.** Read the container type's sharing setting, set `isSharingRestricted = true`, and add that check to the provisioning verifier. Changing it is a tenant configuration write, so the owner runs it or approves it explicitly.
- **G5: approved as recommended.** Check the recipients of secure or Restricted attachments: a recipient without a grant, or on the No Access list, is refused or sent a link instead.
- **G6 (#1072/056) and G7 (#1566):** stay as filed.
- **G8:** live proof with a real CIAM sign-in through the External SPA, run by the owner from steps the agent provides. It needs the four CIAM identities from task 140.

**Secure family (direction, design note pending, not yet built).** The owner rejects one business unit or owner team per secure family because it doesn't scale. Preferred direction:
- a `sprk_securefamily` table, with a lookup of the same name on every family table, as the canonical "umbrella" relationship;
- a single lookup, so a child belongs to at most one family; a write that would put a child under two families is refused;
- ownership stays with Secure Record Owners (isolation);
- one Dataverse **access team** per family, which each family row is shared with once; internal access is team membership;
- external contacts are unaffected: they are still BFF-evaluated grants, and the family gives the evaluator a one-hop key.

The owner asked for research on the practical and technical limits of access teams and POA growth (running), and asked whether the umbrella family serves other aggregation needs ("update all related records", "all decisions across related records").

## Round 95 (2026-10-12): OWNER: ontology records join the standard child contract; secure-family team type

1. **Approved: ontology Signals and Decision Records adopt the standard child-record contract.** They get polymorphic regarding plus typed subject lookups, core-ancestor stamps re-derived on re-file, resolver ownership through the single write path, lineage rows, reconciler and share-mirror reach, and the census guard.
   - **D-34 split:** the decision context (`sprk_corerecordid`, `sprk_corerecordtype`, the policy version, the snapshot) is immutable. Filing, stamps, owner and shares follow the subject's current family.
   - **Split of work:** ontology implements the schema delta and the writers; UAC adds the lineage rows and the census guard (F4), reviews F8, and reviews every ontology PR.
   - Contract, acceptance criteria and sequencing: [ontology-standard-child-contract-2026-10-12.md](../../unified-access-control-r2/notes/ontology-standard-child-contract-2026-10-12.md).
2. **Secure family: research done.** It supersedes round 94's "access team per family" as the recommendation. **The agent now recommends an owner team per family** (zero POA, no per-row share, a one-query drift check, isolation between families by construction, `ReassignObjectsOwner` per family), conditional on seven spikes. The owner has not yet chosen between an owner team and an access team. Full comparison: [secure-family-design-research-2026-10-12.md](secure-family-design-research-2026-10-12.md).
3. **#1746 suppression walk (peer question).**
   - Start from the subject row itself, so the subject's own owner never stops the walk.
   - An ordinary-team-owned *intermediate* stops it. That is the resolver's own rule, bounded by the recent-changes pass, which writes by default.
   - An Undetermined, Failed or faulted walk counts as secure, or the hit is skipped. It is never ordinary.

## Round 99 (2026-10-14): OWNER: external stale-stamp fix shape (task 187, #1857)

- **The finding** (ontology-r1, mapped by uac-r2 on `630373cbb`): external read surfaces admit a child row by its COPIED core-ancestor stamp only. A stale copy, or a "carrier" row (its own pair names ordinary M; another lookup puts it under secure X; never restamped), lists behind the D-123 wall to an external reader of M. The surfaces affected are to-dos, events, PATCH/DELETE on to-dos, messages, notifications, invoice threads and document related links. `ReconciledEmailFiling` was refuted as an exposure: it hides rows, it does not expose them.
- **Options offered:** (A) an owner-team check: cheap and fail-closed, but it over-hides carriers; (B) a live-parent check: precise, with a bounded extra read; (A then B).
- **The owner chose (B) "Live-parent check (precise)".** Each external row read resolves the live roots of its source and carrier parents and evaluates them alongside the stamped roots. The walk is bounded and fails closed. A row whose live parents the caller can read stays visible.

## Round 100 (2026-10-14): OWNER: task 187 scope questions

1. **Invoice message threads:** "Yes, match the family rule". The owner asked whether the secure family already covers this. It does not: the family is the decided direction (rounds 88/94/95: `sprk_securefamily` as the one-hop key) but is not built, and the team type is still open. Until it exists, 187 walks each invoice-thread message's OTHER parents, and a message also filed under secure X is withheld. The invoice itself is not re-walked, so option A (2026-10-11, "inherit") still holds for ordinary messages. **Follow-up:** when the secure family lands, 187's walk is replaced by the family key (same rule, one hop).
2. **Invoices module `/fetch`:** "Yes, apply it". An invoice whose live parents include a secure root the caller can't read is withheld.
3. **Document delete-own:** "Yes, same check". It answers 403 behind the wall and 503 on a read failure, as to-dos and events do.

## Round 100b (2026-10-14): OWNER: secure-family team type = owner team per family (C)

- **The owner:** "owner team is my understanding". It had not been recorded: round 95 left it open. The owner asked for an independent technical judgement, not agreement.
- **The agent's assessment:** C stands on its own merits. The research recommended C **against** the owner's own round 94 suggestion (B, an access team). It matches Microsoft's guidance for a recurring user set. It has no per-row grant, a one-query drift check, isolation between families by construction, and `ReassignObjectsOwner` per family. The family key itself is the more direct design: today membership is a computed lineage walk, which is the root of #1506, #1857 (task 187) and the share-mirror drift. With the key, the walk runs once at write time, in one owner, and every reader uses one hop.
- **Not more design research.** The §7 spikes verify platform behaviour and are the build's first phase, with go/no-go criteria. Spike 1 is dropped (B only). Spike 3 (BU depth through team membership) would hit B equally, so it can't flip the choice. Genuinely open design items: nested families (task 158), and whether to generalise to a "record family" (§8).
- **Recorded:** C is decided, conditional on spikes 2–7. Not built; it needs its own task or project. Task 187's walk ships now and is replaced by the family key later.

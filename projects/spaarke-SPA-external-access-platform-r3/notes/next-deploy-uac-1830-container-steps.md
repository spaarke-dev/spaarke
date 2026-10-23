# Next dev deploy: UAC #1830 / task 192 container steps (from UAC, 2026-10-22)

UAC checked these read-only on dev against origin/master ab7cf4fc94. **None of this is approved in R3's session yet.** Name every step explicitly when asking the owner to approve the deploy by SHA.

**Cause.** Secure project 65a3fab2's container `b!MVasATu…` is dead: its site was deleted. That id is listed in dev's `SharePointEmbedded__OwnedContainerIds`, so the membership sweep logs "Invalid hostname for this tenancy" on every tick. The code fix is UAC PR #2268, which reports it as a warning.

**No exposure window.** A secure root with a null container fails closed: 409 `secure_record_container_missing`, with no BU fallback (`RecordContainerResolver.cs:442-465`, `:854-871`; `SecureContainerDecision.cs:62-90`). 65a3fab2 has no documents, events or todos, and 2 `sprk_assignedaccess` rows.

## Step 0: pre-check (read-only)
GET `sprk_projects(65a3fab2-77a5-f111-aaad-70a8a590c51c)` and expect:
- etag `W/"27392369"`;
- `sprk_containerid` = `b!MVasATu_GE6Lqs6JOGaeghG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN`;
- owner team `6eabc7f9…` (Secure Record Owners);
- `sprk_issecure` = true;
- exactly one share: createdby `1d02f31c…`, mask 262167.

Stop if anything differs.

## Step 1: clear the dead id
The **owner or an admin person** runs this with a delegated Dataverse token, not a BFF app-only writer.
- `PATCH https://spaarkedev1.crm.dynamics.com/api/data/v9.2/sprk_projects(65a3fab2-77a5-f111-aaad-70a8a590c51c)`
- Header: `If-Match: W/"27392369"`
- Body: `{"sprk_containerid": null}`
- Read back: the container id is null; owner, issecure and the share are unchanged.
- Rollback: PATCH the old id back.

## Step 2: re-provision, straight after Step 1, as the same admin
- `POST https://spaarke-bff-dev.azurewebsites.net/api/v1/external-access/provision-project`
- Token: delegated, resource `api://1e40baad-e065-4aea-a8d4-4b7ab273458c`.
- Body: `{"recordType":"project","recordId":"65a3fab2-77a5-f111-aaad-70a8a590c51c","projectRef":"Test New Matter via Workspace"}`
- Expect 200 with `resumed: true`, a new `speContainerId` (not `b!MVas…`), and `sharedToCreatorSystemUserId` = `1d02f31c…`. The share is not rewritten.
- Read back: the new id is held by exactly one root and no BU; owner and share unchanged. Logs show `[PROVISION] … RESUMING` and `Provisioning complete … Resumed=True`.
- The new container is BFF-created (it carries the marker), so it does NOT go into OwnedContainerIds.
- On `container_creation_failed` or `children_incomplete`, call again. A 409 `already_provisioned` means Step 1 didn't land. **Never delete the new container**, even on rollback.

Steps 1–2 don't depend on the deploy. They can run whenever the owner approves them.

## Step 3: at the deploy (owner approves by SHA)
- Remove `b!MVasATu…` from `SharePointEmbedded__OwnedContainerIds` and keep `b!vzGD…`.
- Rollback: re-add it.
- Batch it with #2253, #2258 and #2268 for a single restart.

## Gate after the deploy (with #2268 in the build)
- 0 "Invalid hostname for this tenancy" exceptions from the sweep over 30 minutes.
- The JIT heartbeat shows failed=0 and unresolvable=0. If Step 3 is done but 65a3fab2 isn't re-provisioned yet, it shows a NotOwned warning instead.

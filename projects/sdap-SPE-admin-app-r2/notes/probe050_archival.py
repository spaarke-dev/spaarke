"""Task 050 — does SPE archival work now that the opt-in has had time to replicate?

History (notes/task-050-findings.md §5, §8): archive -> 403 notAllowed "this application does not
currently support archiving", both BEFORE and immediately AFTER the operator set
Set-SPOContainerTypeConfiguration -ContainerTypeId 8a6ce34c-... -IsArchiveEnabled $true (2026-08-28).
Hypothesis 1 = replication lag (up to 24 h). This probe re-runs the identical sequence weeks later.

NFR-07: runs ONLY against a throwaway container it creates, and tears it down on every path.
Identity: app-only as the dev owning app (same precedent as the other probes in this folder). The
original probe used this identity, so the comparison is like-for-like.

Re-created 2026-10-07: the original lived in a session scratchpad lost in the 2026-08-31 worktree wipe.
"""
import json
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

TENANT = "a221a95e-6abc-4434-aecc-e48338a1b2f2"
APP = "170c98e1-d486-4355-bcbe-170454e0207c"
CONTAINER_TYPE = "8a6ce34c-6055-4681-8f87-2f4f9f921c06"
B = "https://graph.microsoft.com/beta/storage/fileStorage"

secret = subprocess.run(
    "az keyvault secret show --vault-name sprk-prod-kv --name spe-owning-app-secret --query value -o tsv",
    capture_output=True, text=True, shell=True).stdout.strip()
TOKEN = json.load(urllib.request.urlopen(urllib.request.Request(
    f"https://login.microsoftonline.com/{TENANT}/oauth2/v2.0/token",
    data=urllib.parse.urlencode({
        "client_id": APP, "client_secret": secret,
        "scope": "https://graph.microsoft.com/.default",
        "grant_type": "client_credentials"}).encode())))["access_token"]
del secret


def call(method, url, body=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method, headers={
        "Authorization": f"Bearer {TOKEN}", "Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req) as r:
            raw = r.read().decode()
            return r.status, (json.loads(raw) if raw else None)
    except urllib.error.HTTPError as e:
        raw = e.read().decode(errors="replace")
        try:
            return e.code, json.loads(raw)
        except ValueError:
            return e.code, raw[:300]


def show(label, status, body, keys=None):
    if isinstance(body, dict) and keys:
        body = {k: body.get(k) for k in keys}
    elif isinstance(body, dict) and "error" in body:
        body = {"code": body["error"].get("code"), "message": body["error"].get("message")}
    print(f"{label:<44} -> {status}  {json.dumps(body) if body is not None else ''}")


cid = None
try:
    st, c = call("POST", f"{B}/containers", {
        "displayName": f"ZZ-Task050-ArchivalProbe-{int(time.time())}",
        "description": "Throwaway — task 050 archival probe. Safe to delete if found orphaned.",
        "containerTypeId": CONTAINER_TYPE})
    show("POST containers", st, c, ["id", "status"])
    if st != 201:
        sys.exit(1)
    cid = c["id"]

    st, b = call("POST", f"{B}/containers/{cid}/activate")
    show("POST activate", st, b)

    st, b = call("POST", f"{B}/containers/{cid}/archive")
    show("POST archive", st, b)
    archived = st in (200, 202, 204)

    st, g = call("GET", f"{B}/containers/{cid}?$select=id,status,archivalDetails")
    show("GET status,archivalDetails", st, g, ["status", "archivalDetails"])

    if archived:
        # AC-2: unarchive returns it toward active. Async — poll briefly, report what is SEEN.
        st, b = call("POST", f"{B}/containers/{cid}/unarchive")
        show("POST unarchive", st, b)
        for i in range(12):
            time.sleep(10)
            st, g = call("GET", f"{B}/containers/{cid}?$select=id,status,archivalDetails")
            show(f"GET after unarchive (+{(i + 1) * 10}s)", st, g, ["status", "archivalDetails"])
            details = (g or {}).get("archivalDetails") if isinstance(g, dict) else None
            if not details or details.get("archiveStatus") not in ("reactivating", "recentlyArchived", "fullyArchived"):
                break
finally:
    if cid:
        st, b = call("DELETE", f"{B}/containers/{cid}")
        show("TEARDOWN DELETE container", st, b)
        st, b = call("DELETE", f"{B}/deletedContainers/{cid}")
        show("TEARDOWN DELETE deletedContainers", st, b)
        st, b = call("GET", f"{B}/deletedContainers/{cid}")
        print(f"{'TEARDOWN verify (expect 404)':<44} -> {st}")

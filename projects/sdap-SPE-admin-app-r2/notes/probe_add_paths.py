"""Live-verify the three unverified "+ Add" write paths against real Graph.

Question being answered (UAT 2026-08-28): do the + Add functions actually work? Contract tests can
only prove the request we SEND is well-formed. Only a real call proves Graph ACCEPTS it.

The payload shapes below are transcribed from SpeAdminGraphService so this probes OUR shapes, not
idealised ones:
  - Column   -> POST   /containers/{id}/columns      (BuildColumnDefinition, :~3390)
  - Property -> PATCH  /containers/{id}              ({customProperties:{K:{value,isSearchable}}}, :2642)
  - Permission -> POST /containers/{id}/permissions  ({roles:[r], grantedToV2:{user:{id}}}, :3154)

THE ONE THAT MATTERS is step 3: add property A, then send ONLY property B, and see whether A
survives. The BFF exposes this as PUT (replace semantics) while Graph underneath is PATCH (merge
semantics). If that mismatch drops properties, a successful-looking save silently destroys data --
the same defect class as the settings form fixed earlier this session.

NFR-07: provisions and tears down its OWN container. Nothing pre-existing is touched.
Owner path is NEGATIVE-ONLY by operator decision -- no grant is sent against the real container type.
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
CT = "8a6ce34c-6055-4681-8f87-2f4f9f921c06"
B = "https://graph.microsoft.com/beta"

secret = subprocess.run(
    "az keyvault secret show --vault-name sprk-prod-kv --name spe-owning-app-secret "
    "--query value -o tsv",
    capture_output=True, text=True, shell=True).stdout.strip()
tok = json.load(urllib.request.urlopen(urllib.request.Request(
    f"https://login.microsoftonline.com/{TENANT}/oauth2/v2.0/token",
    data=urllib.parse.urlencode({
        "client_id": APP, "client_secret": secret,
        "scope": "https://graph.microsoft.com/.default",
        "grant_type": "client_credentials"}).encode())))["access_token"]
del secret


def call(method, url, payload=None):
    hdr = {"Authorization": f"Bearer {tok}", "Accept": "application/json"}
    body = None
    if payload is not None:
        hdr["Content-Type"] = "application/json"
        body = json.dumps(payload).encode()
    try:
        resp = urllib.request.urlopen(
            urllib.request.Request(url, method=method, headers=hdr, data=body))
        text = resp.read().decode()
        return resp.status, (json.loads(text) if text.strip() else None)
    except urllib.error.HTTPError as e:
        text = e.read().decode()
        try:
            return e.code, json.loads(text)
        except Exception:
            return e.code, text[:400]


def err(b):
    if isinstance(b, dict) and "error" in b:
        return f"{b['error'].get('code')}: {b['error'].get('message')}"
    return json.dumps(b)[:300] if not isinstance(b, str) else b[:300]


FAIL = []


def check(label, cond, detail=""):
    print(f"   {'PASS' if cond else 'FAIL'}  {label}{(' - ' + detail) if detail else ''}")
    if not cond:
        FAIL.append(label)


# ── Provision the throwaway ──────────────────────────────────────────────────
name = f"ZZ-AddPathProbe-{int(time.time())}"
s, b = call("POST", f"{B}/storage/fileStorage/containers",
            {"displayName": name,
             "description": "Probe for + Add write paths. Throwaway.",
             "containerTypeId": CT})
if s >= 400:
    print("CREATE failed:", err(b))
    sys.exit(1)
cid = b["id"]
call("POST", f"{B}/storage/fileStorage/containers/{cid}/activate", {})
print(f"THROWAWAY container: {name}\n  {cid}\n")

C = f"{B}/storage/fileStorage/containers/{urllib.parse.quote(cid, safe='')}"

try:
    # ── 1. Add Column ────────────────────────────────────────────────────────
    print("1. ADD COLUMN  POST /columns")
    col_body = {
        "name": "ProbeMatterRef",
        "displayName": "Probe Matter Ref",
        "description": "Written by probe_add_paths.py",
        "required": False,
        "indexed": False,
        "text": {},
    }
    s, b = call("POST", f"{C}/columns", col_body)
    print("   ->", s, "" if s < 400 else err(b))
    check("Graph ACCEPTS our column payload", s in (200, 201), f"status {s}")
    col_id = (b or {}).get("id") if s < 400 else None

    if col_id:
        s, b = call("GET", f"{C}/columns")
        names = [c.get("name") for c in (b or {}).get("value", [])]
        check("column is READ BACK after create", "ProbeMatterRef" in names,
              f"columns now: {names}")

    # ── 2. Add Property ──────────────────────────────────────────────────────
    print("\n2. ADD PROPERTY  PATCH container {customProperties:{...}}")
    s, b = call("PATCH", C, {"customProperties": {
        "ProbeAlpha": {"value": "first-value", "isSearchable": False}}})
    print("   ->", s, "" if s < 400 else err(b))
    check("Graph ACCEPTS our customProperties payload", s in (200, 204), f"status {s}")

    s, b = call("GET", f"{C}/customProperties")
    got = list((b or {}).keys()) if isinstance(b, dict) else []
    got = [k for k in got if not k.startswith("@")]
    check("property is READ BACK after write", "ProbeAlpha" in got, f"properties: {got}")

    # ── 3. THE ONE THAT MATTERS — does a second write DROP the first? ────────
    print("\n3. MERGE-vs-REPLACE  <<< the data-loss question >>>")
    print("   Sending ONLY ProbeBeta. Does ProbeAlpha survive?")
    s, b = call("PATCH", C, {"customProperties": {
        "ProbeBeta": {"value": "second-value", "isSearchable": False}}})
    print("   ->", s, "" if s < 400 else err(b))

    s, b = call("GET", f"{C}/customProperties")
    after = [k for k in (list(b.keys()) if isinstance(b, dict) else []) if not k.startswith("@")]
    alpha_survived = "ProbeAlpha" in after
    print(f"   properties after partial write: {after}")
    check("partial write MERGES (does not silently drop the untouched property)",
          alpha_survived,
          "MERGE - safe" if alpha_survived
          else "REPLACE - a partial save DESTROYS existing properties. The BFF exposes this "
               "as PUT; if the UI ever sends a delta, data is lost silently.")

    # ── 4. Add Permission ────────────────────────────────────────────────────
    print("\n4. ADD PERMISSION  POST /permissions {roles, grantedToV2.user.id}")
    s, b = call("GET", "https://graph.microsoft.com/v1.0/users?$top=1&$select=id,userPrincipalName")
    subject = ((b or {}).get("value") or [{}])[0]
    if subject.get("id"):
        print(f"   grant subject (throwaway container only): {subject.get('userPrincipalName')}")
        s, b = call("POST", f"{C}/permissions", {
            "roles": ["reader"],
            "grantedToV2": {"user": {"id": subject["id"]}}})
        print("   ->", s, "" if s < 400 else err(b))
        check("Graph ACCEPTS our permission payload", s in (200, 201), f"status {s}")
        perm_id = (b or {}).get("id") if s < 400 else None

        if perm_id:
            s, b = call("GET", f"{C}/permissions")
            ids = [p.get("id") for p in (b or {}).get("value", [])]
            check("permission is READ BACK after grant", perm_id in ids)
            s, _ = call("DELETE", f"{C}/permissions/{perm_id}")
            check("permission REVOKE succeeds", s in (200, 204), f"status {s}")
    else:
        print("   SKIP - could not resolve any user to grant to")

    # ── 5. Add Owner — NEGATIVE ONLY (operator decision) ─────────────────────
    print("\n5. ADD OWNER  negative path only - no grant is sent")
    print("   Our code resolves a UPN via GET /users/{upn} BEFORE granting.")
    s, b = call("GET",
                "https://graph.microsoft.com/v1.0/users/"
                "definitely-not-a-real-user-zz@spaarke.com")
    print("   ->", s, err(b) if s >= 400 else "")
    check("a bogus UPN resolves to 404, so no doomed grant is ever sent", s == 404,
          f"status {s} - this is the guard AddOwner_WhenTheUpnResolvesToNobody depends on")

finally:
    print("\n6. TEARDOWN (production verb)")
    s1, _ = call("DELETE", C)
    print(f"   soft-delete: {s1}")
    s2, rb = call("DELETE",
                  f"{B}/storage/fileStorage/deletedContainers/{urllib.parse.quote(cid, safe='')}")
    print(f"   permanent-delete: {s2}" + ("" if s2 in (200, 202, 204) else f"  {err(rb)}"))
    if s1 not in (200, 202, 204) or s2 not in (200, 202, 204):
        print("   *** TEARDOWN INCOMPLETE:", cid)

print("\n" + "=" * 66)
print("RESULT:", "ALL CHECKS PASSED" if not FAIL else f"{len(FAIL)} FAILED -> {FAIL}")

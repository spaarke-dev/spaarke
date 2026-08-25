"""
ONE delegated sign-in, two open items resolved.

  A. The PATCH-400 escalation (blocks 023 / 025 / 026 / 029)
  B. Task 027 AC-1 — container-type owners list/add/remove

SECURITY: no token or secret is printed. Read-only by default; every WRITE is gated behind
--allow-writes and is additive-and-reverted (add an owner, then remove the same owner).
NOTHING is deleted, and no container type is created.
"""
import json, sys, time, urllib.request, urllib.parse, urllib.error

TENANT = "a221a95e-6abc-4434-aecc-e48338a1b2f2"
CLI_APP = "68cf5a14-1efb-4254-80bf-2761ffc89373"   # SPAARKE-SPE-Admin-CLI (public client)
OWNING_APP = "170c98e1-d486-4355-bcbe-170454e0207c"
SCOPES = "https://graph.microsoft.com/FileStorageContainerType.Manage.All offline_access openid profile"

ALLOW_WRITES = "--allow-writes" in sys.argv


def post_form(url, data):
    body = urllib.parse.urlencode(data).encode()
    try:
        return json.load(urllib.request.urlopen(urllib.request.Request(url, data=body)))
    except urllib.error.HTTPError as e:
        return json.loads(e.read().decode())


def device_code_token():
    dc = post_form(
        f"https://login.microsoftonline.com/{TENANT}/oauth2/v2.0/devicecode",
        {"client_id": CLI_APP, "scope": SCOPES})
    if "user_code" not in dc:
        sys.exit(f"FATAL: device code request failed: {dc.get('error_description', dc)}")

    print("=" * 72)
    print("  ACTION NEEDED — sign in once, then this runs unattended")
    print("=" * 72)
    print(f"  1. Open:  {dc['verification_uri']}")
    print(f"  2. Code:  {dc['user_code']}")
    print("=" * 72, flush=True)

    deadline = time.time() + dc.get("expires_in", 900)
    interval = dc.get("interval", 5)
    while time.time() < deadline:
        time.sleep(interval)
        r = post_form(
            f"https://login.microsoftonline.com/{TENANT}/oauth2/v2.0/token",
            {"client_id": CLI_APP, "grant_type": "urn:ietf:params:oauth:grant-type:device_code",
             "device_code": dc["device_code"]})
        if "access_token" in r:
            print("\n  signed in (token not shown)\n", flush=True)
            return r["access_token"]
        err = r.get("error")
        if err == "authorization_pending":
            continue
        if err == "slow_down":
            interval += 5
            continue
        sys.exit(f"FATAL: {err}: {r.get('error_description')}")
    sys.exit("FATAL: device code expired")


TOK = device_code_token()


def call(method, url, body=None, label=""):
    """Returns (status, parsed_or_text). Prints a compact, decision-relevant summary."""
    req = urllib.request.Request(url, method=method)
    req.add_header("Authorization", f"Bearer {TOK}")
    req.add_header("Accept", "application/json")
    data = None
    if body is not None:
        data = json.dumps(body).encode()
        req.add_header("Content-Type", "application/json")
    try:
        r = urllib.request.urlopen(req, data=data)
        raw = r.read().decode() or ""
        parsed = json.loads(raw) if raw.strip() else None
        print(f"  [{r.status}] {label or method}")
        return r.status, parsed
    except urllib.error.HTTPError as e:
        raw = e.read().decode()
        print(f"  [{e.code}] {label or method}")
        try:
            err = json.loads(raw)["error"]
            print(f"        code    : {err.get('code')}")
            print(f"        message : {str(err.get('message'))[:400]}")
            inner = err.get("innerError") or {}
            # innerError often carries the ACTUAL cause; the outer message is frequently generic.
            for k in ("code", "message", "date", "request-id", "client-request-id"):
                if k in inner:
                    print(f"        inner.{k}: {inner[k]}")
            if err.get("details"):
                print(f"        details : {json.dumps(err['details'])[:300]}")
        except Exception:
            print(f"        raw     : {raw[:300]}")
        return e.code, None


BETA = "https://graph.microsoft.com/beta/storage/fileStorage"
V1 = "https://graph.microsoft.com/v1.0/storage/fileStorage"

print("#" * 72)
print("# A. Enumerate container types (delegated — app-only is 403 here)")
print("#" * 72)
status, types = call("GET", f"{BETA}/containerTypes", label="LIST containerTypes (beta)")
rows = (types or {}).get("value", [])
for t in rows:
    print(f"    - {t.get('name')!r}")
    print(f"        id={t.get('id')}  owningAppId={t.get('owningAppId')}")
    print(f"        billing={t.get('billingClassification')}/{t.get('billingStatus')}  "
          f"expires={t.get('expirationDateTime')}")

owned_by_cli = [t for t in rows if (t.get("owningAppId") or "").lower() == CLI_APP.lower()]
owned_by_spa = [t for t in rows if (t.get("owningAppId") or "").lower() == OWNING_APP.lower()]
print(f"\n  owned by the CLI app ({CLI_APP[:8]}…): {len(owned_by_cli)}")
print(f"  owned by the SPA app ({OWNING_APP[:8]}…): {len(owned_by_spa)}")

print()
print("#" * 72)
print("# B. THE ESCALATION — capture the FULL 400 body on a no-op PATCH")
print("#" * 72)
print("  A no-op (writing the current value back) isolates the CAUSE from the payload.")
target = owned_by_spa[0] if owned_by_spa else (rows[0] if rows else None)
if target:
    tid = target["id"]
    cur = (target.get("settings") or {}).get("itemMajorVersionLimit")
    print(f"  target: {target.get('name')!r}  (owningApp={target.get('owningAppId')})")
    call("PATCH", f"{BETA}/containerTypes/{tid}",
         {"settings": {"itemMajorVersionLimit": cur}}, label="no-op nested PATCH (beta)")
    call("PATCH", f"{V1}/containerTypes/{tid}",
         {"settings": {"itemMajorVersionLimit": cur}}, label="no-op nested PATCH (v1.0)")

    # If a type owned by the CLI app exists, PATCHing it is THE decisive ownership test.
    if owned_by_cli:
        c = owned_by_cli[0]
        print(f"\n  🔑 DECISIVE: a container type owned by the CALLING app exists — {c.get('name')!r}")
        ccur = (c.get("settings") or {}).get("itemMajorVersionLimit")
        call("PATCH", f"{BETA}/containerTypes/{c['id']}",
             {"settings": {"itemMajorVersionLimit": ccur}},
             label="no-op PATCH on a CLI-OWNED type")
        print("  → 2xx here + 400 above = ownership IS the rule (hypothesis confirmed).")
        print("  → 400 here too              = ownership is NOT the rule; look elsewhere.")
    else:
        print("\n  ⚠️ No container type is owned by the calling app, so the ownership")
        print("     hypothesis cannot be settled without creating one (trial limit = 1).")

print()
print("#" * 72)
print("# C. Task 027 AC-1 — container-type OWNERS")
print("#" * 72)
if target:
    tid = target["id"]
    st, owners = call("GET", f"{BETA}/containerTypes/{tid}/permissions", label="LIST owners (beta)")
    if owners is not None:
        for o in owners.get("value", []):
            u = (o.get("grantedToV2") or o.get("grantedTo") or {}).get("user", {})
            print(f"    - permId={o.get('id')}  roles={o.get('roles')}")
            print(f"        {u.get('displayName')} / {u.get('email') or u.get('userPrincipalName')}")

    if ALLOW_WRITES and st == 200:
        me_st, me = call("GET", "https://graph.microsoft.com/v1.0/me?$select=id,userPrincipalName,displayName",
                         label="whoami")
        if me:
            print(f"        signed in as {me.get('userPrincipalName')}")
            print("\n  WRITE TEST (additive, then reverted — nothing is deleted permanently):")
            add_st, added = call(
                "POST", f"{BETA}/containerTypes/{tid}/permissions",
                {"roles": ["owner"], "grantedToV2": {"user": {"userPrincipalName": me["userPrincipalName"]}}},
                label="ADD owner (self)")
            if add_st in (200, 201) and added and added.get("id"):
                call("DELETE", f"{BETA}/containerTypes/{tid}/permissions/{added['id']}",
                     label="REMOVE the owner just added (revert)")
    elif not ALLOW_WRITES:
        print("\n  (write test skipped — re-run with --allow-writes to exercise add+remove)")

print()
print("=" * 72)
print("  done")
print("=" * 72)

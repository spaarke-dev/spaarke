"""
THE decisive test: is the PATCH-400 caused by a missing `etag` BODY property?

Microsoft's own doc (filestoragecontainertype-update) lists `etag` as **Required** in the request
body, and its "Example 2: Update without ETag" documents the response as `400 Bad Request`.

That matches our symptom exactly. Every earlier attempt sent `If-Match` as an HTTP HEADER, which is
a different thing from an `etag` property in the BODY.

All PATCHes below are NO-OPs: they write the value that is already there.
Also completes task 027 AC-1 (owner add -> remove, self-reverting).
"""
import io, json, os, sys, time, urllib.request, urllib.parse, urllib.error

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

TENANT = "a221a95e-6abc-4434-aecc-e48338a1b2f2"
CLI_APP = "68cf5a14-1efb-4254-80bf-2761ffc89373"
SCOPES = "https://graph.microsoft.com/FileStorageContainerType.Manage.All offline_access openid profile"
CODE_FILE = "CURRENT-DEVICE-CODE.txt"
BAR = "=" * 72
PAYGO = "8a6ce34c-6055-4681-8f87-2f4f9f921c06"
TRIAL = "ef8e5d5b-f9c1-4cdb-9b4f-8ca50d070255"


def post_form(url, data):
    body = urllib.parse.urlencode(data).encode()
    try:
        return json.load(urllib.request.urlopen(urllib.request.Request(url, data=body)))
    except urllib.error.HTTPError as e:
        return json.loads(e.read().decode())


def device_code_token(max_minutes=60):
    give_up = time.time() + max_minutes * 60
    attempt = 0
    while time.time() < give_up:
        attempt += 1
        dc = post_form("https://login.microsoftonline.com/" + TENANT + "/oauth2/v2.0/devicecode",
                       {"client_id": CLI_APP, "scope": SCOPES})
        if "user_code" not in dc:
            sys.exit("FATAL: " + str(dc.get("error_description", dc)))
        print(BAR, flush=True)
        print("  SIGN IN  (code #%d - auto-renews)" % attempt, flush=True)
        print(BAR, flush=True)
        print("  1. Open:  " + dc["verification_uri"], flush=True)
        print("  2. Code:  " + dc["user_code"], flush=True)
        print(BAR, flush=True)
        with io.open(CODE_FILE, "w", encoding="utf-8") as fh:
            fh.write(dc["user_code"] + chr(10) + dc["verification_uri"] + chr(10))
        deadline = min(time.time() + dc.get("expires_in", 900), give_up)
        interval = dc.get("interval", 5)
        while time.time() < deadline:
            time.sleep(interval)
            r = post_form("https://login.microsoftonline.com/" + TENANT + "/oauth2/v2.0/token",
                          {"client_id": CLI_APP,
                           "grant_type": "urn:ietf:params:oauth:grant-type:device_code",
                           "device_code": dc["device_code"]})
            if "access_token" in r:
                print("", flush=True)
                print("  signed in (token not shown)", flush=True)
                print("", flush=True)
                try:
                    os.remove(CODE_FILE)
                except OSError:
                    pass
                return r["access_token"]
            err = r.get("error")
            if err == "authorization_pending":
                continue
            if err == "slow_down":
                interval += 5
                continue
            if err in ("expired_token", "code_expired"):
                print("  (code lapsed - issuing a fresh one)", flush=True)
                break
            sys.exit("FATAL: " + str(err) + ": " + str(r.get("error_description")))
    sys.exit("FATAL: no sign-in within the window")


TOK = device_code_token()
BETA = "https://graph.microsoft.com/beta/storage/fileStorage"
V1 = "https://graph.microsoft.com/v1.0/storage/fileStorage"


def call(method, url, body=None, label=""):
    req = urllib.request.Request(url, method=method)
    req.add_header("Authorization", "Bearer " + TOK)
    req.add_header("Accept", "application/json")
    data = None
    if body is not None:
        data = json.dumps(body).encode()
        req.add_header("Content-Type", "application/json")
    try:
        r = urllib.request.urlopen(req, data=data)
        raw = r.read().decode() or ""
        print("  [%s] %s" % (r.status, label or method), flush=True)
        return r.status, (json.loads(raw) if raw.strip() else None)
    except urllib.error.HTTPError as e:
        raw = e.read().decode()
        print("  [%s] %s" % (e.code, label or method), flush=True)
        try:
            err = json.loads(raw)["error"]
            print("        code=%s  msg=%s" % (err.get("code"), str(err.get("message"))[:220]), flush=True)
        except Exception:
            print("        raw=%s" % raw[:200], flush=True)
        return e.code, None


print("#" * 72)
print("# 1. GET the container type and capture its etag")
print("#" * 72)
st, ct = call("GET", BETA + "/containerTypes/" + PAYGO, label="GET 'Spaarke PAYGO 1' (beta)")
etag = (ct or {}).get("etag")
settings = (ct or {}).get("settings") or {}
print("      etag present: %s" % (repr(etag) if etag else "NO"))
print("      current itemMajorVersionLimit=%s  isSearchEnabled=%s"
      % (settings.get("itemMajorVersionLimit"), settings.get("isSearchEnabled")))

print()
print("#" * 72)
print("# 2. THE TEST - identical no-op PATCH, WITHOUT then WITH the etag body property")
print("#" * 72)
noop = {"settings": {"itemMajorVersionLimit": settings.get("itemMajorVersionLimit")}}

print("  2a. WITHOUT etag (expected 400 per Microsoft's Example 2):")
call("PATCH", BETA + "/containerTypes/" + PAYGO, dict(noop), label="no-op PATCH, no etag")

print()
print("  2b. WITH etag in the BODY (the documented requirement):")
if etag:
    with_etag = dict(noop)
    with_etag["etag"] = etag
    st2, updated = call("PATCH", BETA + "/containerTypes/" + PAYGO, with_etag,
                        label="no-op PATCH + etag")
    if st2 == 200:
        print("      *** ESCALATION RESOLVED - the cause was a missing etag, NOT ownership ***")
        us = (updated or {}).get("settings") or {}
        print("      read-back itemMajorVersionLimit=%s  new etag=%s"
              % (us.get("itemMajorVersionLimit"), repr((updated or {}).get("etag"))))
else:
    print("      SKIPPED - the GET returned no etag, which would itself be the finding")

print()
print("  2c. Same thing on v1.0, to check the version behaves identically:")
if etag:
    st3, u3 = call("GET", V1 + "/containerTypes/" + PAYGO, label="GET (v1.0)")
    e3 = (u3 or {}).get("etag")
    if e3:
        b3 = {"settings": {"itemMajorVersionLimit": ((u3.get("settings") or {}).get("itemMajorVersionLimit"))},
              "etag": e3}
        call("PATCH", V1 + "/containerTypes/" + PAYGO, b3, label="no-op PATCH + etag (v1.0)")

print()
print("#" * 72)
print("# 3. A REAL round-trip - change a value, read it back, then restore it")
print("#" * 72)
st, ct2 = call("GET", BETA + "/containerTypes/" + PAYGO, label="re-GET for a fresh etag")
if ct2 and ct2.get("etag"):
    cur = ((ct2.get("settings") or {}).get("itemMajorVersionLimit"))
    if isinstance(cur, int):
        changed = cur - 1 if cur > 1 else cur + 1
        st, r1 = call("PATCH", BETA + "/containerTypes/" + PAYGO,
                      {"settings": {"itemMajorVersionLimit": changed}, "etag": ct2["etag"]},
                      label="set itemMajorVersionLimit=%s" % changed)
        if st == 200:
            st, rb = call("GET", BETA + "/containerTypes/" + PAYGO, label="read back")
            got = ((rb or {}).get("settings") or {}).get("itemMajorVersionLimit")
            print("      wrote %s, read back %s  -> %s"
                  % (changed, got, "PERSISTED" if got == changed else "DID NOT PERSIST"))
            if rb and rb.get("etag"):
                st, _ = call("PATCH", BETA + "/containerTypes/" + PAYGO,
                             {"settings": {"itemMajorVersionLimit": cur}, "etag": rb["etag"]},
                             label="RESTORE original value %s" % cur)

print()
print("#" * 72)
print("# 4. Task 027 AC-1 - owner add, then revert")
print("#" * 72)
st, me = call("GET", "https://graph.microsoft.com/v1.0/me?$select=id,userPrincipalName", label="whoami")
if me:
    upn = me.get("userPrincipalName")
    print("      signed in as %s" % upn)
    add_st, added = call("POST", BETA + "/containerTypes/" + PAYGO + "/permissions",
                         {"roles": ["owner"], "grantedToV2": {"user": {"userPrincipalName": upn}}},
                         label="ADD owner (self)")
    if add_st in (200, 201) and added:
        print("      permission id=%s roles=%s" % (added.get("id"), added.get("roles")))
        st, after = call("GET", BETA + "/containerTypes/" + PAYGO + "/permissions", label="re-list owners")
        print("      owners now: %d" % len(((after or {}).get("value") or [])))
        if added.get("id"):
            call("DELETE", BETA + "/containerTypes/" + PAYGO + "/permissions/" + added["id"],
                 label="REVERT - remove the grant just added")
            st, final = call("GET", BETA + "/containerTypes/" + PAYGO + "/permissions", label="confirm reverted")
            print("      owners after revert: %d" % len(((final or {}).get("value") or [])))

print()
print("#" * 72)
print("# 5. Does the expired trial hold containers? (costs of deleting it, if ever needed)")
print("#" * 72)
q = urllib.parse.quote("containerTypeId eq " + TRIAL, safe="")
st, tc = call("GET", BETA + "/containers?$filter=" + q, label="containers on the expired trial")
if tc is not None:
    vals = tc.get("value", [])
    print("      container count: %d" % len(vals))
    for c in vals:
        print("        - %r" % c.get("displayName"))

print()
print(BAR)
print("  done")
print(BAR)

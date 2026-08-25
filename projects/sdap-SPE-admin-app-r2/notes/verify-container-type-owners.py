"""
Task 027 AC-1, live: owner list -> add -> confirm -> remove -> confirm removed.

Uses the CORRECTED payload. The first attempt sent `userPrincipalName` and got 400; Microsoft's
Create-permission reference says only the user's object `id` is accepted. This mirrors exactly what
SpeAdminGraphService now sends, so a pass here verifies the shipped code path, not a lookalike.

Self-reverting: the grant added is removed again. Nothing is left behind.
"""
import io, json, os, sys, time, urllib.request, urllib.parse, urllib.error

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

TENANT = "a221a95e-6abc-4434-aecc-e48338a1b2f2"
CLI_APP = "68cf5a14-1efb-4254-80bf-2761ffc89373"
SCOPES = "https://graph.microsoft.com/FileStorageContainerType.Manage.All openid profile offline_access"
CODE_FILE = "CURRENT-DEVICE-CODE.txt"
BAR = "=" * 72
PAYGO = "8a6ce34c-6055-4681-8f87-2f4f9f921c06"


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
PERMS = BETA + "/containerTypes/" + PAYGO + "/permissions"


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
            print("        code=%s  msg=%s" % (err.get("code"), str(err.get("message"))[:250]), flush=True)
        except Exception:
            print("        raw=%s" % raw[:250], flush=True)
        return e.code, None


def owner_count():
    st, b = call("GET", PERMS, label="list owners")
    return len(((b or {}).get("value") or [])), b


print("#" * 72)
print("# Task 027 AC-1 - owners: list -> add -> confirm -> remove -> confirm")
print("#" * 72)

st, me = call("GET", "https://graph.microsoft.com/v1.0/me?$select=id,userPrincipalName,displayName",
              label="whoami (resolve MY object id)")
if not me or not me.get("id"):
    sys.exit("FATAL: could not resolve the signed-in user's object id")
my_id = me["id"]
print("      %s  ->  objectId=%s" % (me.get("userPrincipalName"), my_id))

before, _ = owner_count()
print("      owners BEFORE: %d" % before)

print()
print("  ADD - using the CORRECTED payload (object id, not userPrincipalName):")
add_st, added = call("POST", PERMS,
                     {"roles": ["owner"], "grantedToV2": {"user": {"id": my_id}}},
                     label="POST owner grant")

if add_st in (200, 201) and added:
    perm_id = added.get("id")
    print("      *** AC-1 ADD VERIFIED - permission id=%s roles=%s ***"
          % (perm_id, added.get("roles")))

    after, body = owner_count()
    print("      owners AFTER add: %d" % after)
    for o in ((body or {}).get("value") or []):
        u = (o.get("grantedToV2") or o.get("grantedTo") or {}).get("user", {})
        print("        - %s / %s  roles=%s"
              % (u.get("displayName"), u.get("id"), o.get("roles")))

    print()
    print("  REMOVE - revert, so nothing is left behind:")
    if perm_id:
        del_st, _ = call("DELETE", PERMS + "/" + urllib.parse.quote(perm_id, safe=""),
                         label="DELETE the grant just added")
        final, _ = owner_count()
        print("      owners AFTER remove: %d" % final)
        if del_st in (200, 204) and final == before:
            print("      *** AC-1 REMOVE VERIFIED - list returned to its original state ***")
        else:
            print("      !! removal did not restore the original state - INVESTIGATE")
else:
    print("      ADD still failing. Payload sent was:")
    print("        " + json.dumps({"roles": ["owner"], "grantedToV2": {"user": {"id": my_id}}}))
    print("      Per Microsoft's reference, adding requires being an existing owner,")
    print("      a SharePoint Embedded Administrator, or a Global Administrator.")
    print("      There are currently ZERO owners, so this depends on the directory role.")

print()
print(BAR)
print("  done")
print(BAR)

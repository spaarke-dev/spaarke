"""Can the owningAppId hypothesis be tested with the credentials available to me?

UAT 2026-08-28: creating a container type fails with
  invalidRequest: One of the provided arguments is not acceptable.

Hypothesis: owningAppId is required (beta CSDL marks it Nullable="false"; the documented create body
carries it) and our code never sent it.

Container-type CREATE is DELEGATED-only -- task 010 established that an app-only token gets 403 on
the sibling LIST endpoint. If that also holds for CREATE, an app-only probe returns 403 accessDenied
and CANNOT distinguish the argument error, which means this fix is reasoned rather than probed and
UAT is the verification. Establish which, and say so plainly.

DELIBERATELY DESIGNED TO CREATE NOTHING:
  - request 1 omits owningAppId (the shape that already fails)
  - request 2 sends a MALFORMED owningAppId, which cannot succeed
Container types cannot be deleted, so no request here may be capable of succeeding.
"""
import json
import subprocess
import urllib.error
import urllib.parse
import urllib.request

TENANT = "a221a95e-6abc-4434-aecc-e48338a1b2f2"
APP = "170c98e1-d486-4355-bcbe-170454e0207c"
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


def post(url, payload):
    hdr = {"Authorization": f"Bearer {tok}", "Accept": "application/json",
           "Content-Type": "application/json"}
    try:
        r = urllib.request.urlopen(urllib.request.Request(
            url, method="POST", headers=hdr, data=json.dumps(payload).encode()))
        return r.status, json.loads(r.read().decode() or "{}")
    except urllib.error.HTTPError as e:
        t = e.read().decode()
        try:
            return e.code, json.loads(t)
        except Exception:
            return e.code, t[:300]


def err(b):
    if isinstance(b, dict) and "error" in b:
        return f"{b['error'].get('code')}: {b['error'].get('message')}"
    return json.dumps(b)[:250] if not isinstance(b, str) else b[:250]


URL = f"{B}/storage/fileStorage/containerTypes"

print("1. WITHOUT owningAppId (our shape before the fix)")
s1, b1 = post(URL, {"name": "ZZ-Probe-DoNotCreate", "billingClassification": "standard"})
print(f"   -> {s1}  {err(b1)}\n")

print("2. WITH a MALFORMED owningAppId (cannot succeed)")
s2, b2 = post(URL, {"name": "ZZ-Probe-DoNotCreate",
                    "billingClassification": "standard",
                    "owningAppId": "not-a-guid"})
print(f"   -> {s2}  {err(b2)}\n")

print("=" * 66)
if s1 == 403 or s2 == 403:
    print("VERDICT: app-only is REFUSED (403) on this endpoint, as on LIST (task 010).")
    print("The owningAppId hypothesis CANNOT be confirmed with these credentials.")
    print("It rests on: beta CSDL Nullable=\"false\" + Microsoft's documented create body.")
    print("UAT with a delegated token is the verification. Say so; do not claim it is proven.")
elif s1 == 400 and s2 != 400:
    print("VERDICT: the two shapes differ -> owningAppId is consumed and required. CONFIRMED.")
elif s1 == 400 and s2 == 400:
    print("VERDICT: both rejected. Compare the two messages above -- if #2 now complains about the")
    print("app id specifically, owningAppId is being read and the hypothesis holds.")
else:
    print(f"VERDICT: unexpected pair ({s1}, {s2}). Read the messages before concluding anything.")

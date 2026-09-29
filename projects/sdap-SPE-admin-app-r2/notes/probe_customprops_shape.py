"""Isolate WHY the custom-properties write was rejected.

The first probe sent our production shape -- PATCH on the CONTAINER with a {customProperties:{...}}
wrapper (SpeAdminGraphService.UpdateCustomPropertiesAsync :2652) -- and Graph answered
400 "Unsupported request body property: customProperties".

Before calling our code broken, test the alternative: Graph exposes customProperties as its own
sub-resource, so the write may belong at PATCH /containers/{id}/customProperties with the property
map as the BODY ROOT (no wrapper). If that shape succeeds, our endpoint is aimed at the wrong URL
and the defect is real and specific.

Then answer the merge-vs-replace question on whichever shape works, because that decides whether a
partial save silently destroys existing properties.
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


def props(url):
    s, b = call("GET", url)
    if not isinstance(b, dict):
        return []
    return sorted(k for k in b if not k.startswith("@"))


name = f"ZZ-CustomPropShape-{int(time.time())}"
s, b = call("POST", f"{B}/storage/fileStorage/containers",
            {"displayName": name, "description": "Custom-property shape probe. Throwaway.",
             "containerTypeId": CT})
if s >= 400:
    print("CREATE failed:", err(b))
    sys.exit(1)
cid = b["id"]
call("POST", f"{B}/storage/fileStorage/containers/{cid}/activate", {})
print(f"THROWAWAY container: {name}\n  {cid}\n")

C = f"{B}/storage/fileStorage/containers/{urllib.parse.quote(cid, safe='')}"
CP = f"{C}/customProperties"

winner = None
try:
    print("SHAPE A (ours today) - PATCH /containers/{id} with a customProperties wrapper")
    s, b = call("PATCH", C, {"customProperties": {
        "ShapeA": {"value": "a", "isSearchable": False}}})
    print(f"   -> {s} {'' if s < 400 else err(b)}")
    print(f"   properties now: {props(CP)}\n")
    if s < 400:
        winner = "A"

    print("SHAPE B - PATCH /containers/{id}/customProperties, map as body ROOT")
    s, b = call("PATCH", CP, {"ShapeB": {"value": "b", "isSearchable": False}})
    print(f"   -> {s} {'' if s < 400 else err(b)}")
    print(f"   properties now: {props(CP)}\n")
    if s < 400 and winner is None:
        winner = "B"

    if winner == "B":
        print("MERGE-vs-REPLACE on the working shape")
        print("   Writing ONLY ShapeC. Does ShapeB survive?")
        s, b = call("PATCH", CP, {"ShapeC": {"value": "c", "isSearchable": False}})
        print(f"   -> {s} {'' if s < 400 else err(b)}")
        after = props(CP)
        print(f"   properties now: {after}")
        if "ShapeB" in after:
            print("   => MERGE. A partial write preserves untouched properties. SAFE.")
        else:
            print("   => REPLACE. A partial write DESTROYS untouched properties.")
            print("      The BFF exposes PUT semantics; the UI must send the FULL set every time.")

        print("\n   Removal semantics - setting a property to null")
        s, b = call("PATCH", CP, {"ShapeB": None})
        print(f"   -> {s} {'' if s < 400 else err(b)}")
        print(f"   properties now: {props(CP)}")

finally:
    print("\nTEARDOWN")
    s1, _ = call("DELETE", C)
    s2, rb = call("DELETE",
                  f"{B}/storage/fileStorage/deletedContainers/{urllib.parse.quote(cid, safe='')}")
    print(f"   soft-delete: {s1}   permanent-delete: {s2}")
    if s1 not in (200, 202, 204) or s2 not in (200, 202, 204):
        print("   *** TEARDOWN INCOMPLETE:", cid)

print("\n" + "=" * 66)
print(f"WORKING SHAPE: {winner if winner else 'NEITHER'}")
if winner == "B":
    print("Our code uses SHAPE A, which Graph rejects. The write path is aimed at the wrong URL.")

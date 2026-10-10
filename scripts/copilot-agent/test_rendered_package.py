#!/usr/bin/env python3
"""Validate a rendered Spaarke Copilot agent package against Microsoft's published schemas (T257).

Usage: python3 test_rendered_package.py <package.zip> --bff-base-url URL --scope SCOPE --tenant-id GUID

Checks (exit 1 on any failure):
  - manifest.json, declarativeAgent.json and spaarke-api-plugin.json validate against the JSON schema named in their
    own "$schema" (fetched from developer.microsoft.com);
  - spaarke-bff-openapi.yaml parses as YAML; servers[0].url, the OAuth authorize/token URLs and the scope are the
    values given.

Needs: pip install jsonschema pyyaml. Network: the three schema URLs only. It reads nothing else and writes nothing.
Used by .github/workflows/publish-copilot-agent-template.yml on a sample render.
"""
import argparse
import json
import sys
import urllib.request
import zipfile

import jsonschema
import yaml


def fetch_schema(url):
    with urllib.request.urlopen(url, timeout=60) as r:  # noqa: S310 - fixed Microsoft schema host
        return json.load(r)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("package")
    ap.add_argument("--bff-base-url", required=True)
    ap.add_argument("--scope", required=True)
    ap.add_argument("--tenant-id", required=True)
    a = ap.parse_args()

    failures = []
    with zipfile.ZipFile(a.package) as z:
        names = set(z.namelist())
        for name in ("manifest.json", "declarativeAgent.json", "spaarke-api-plugin.json"):
            doc = json.loads(z.read(name).decode("utf-8"))
            url = doc.get("$schema", "")
            if not url.startswith("https://developer.microsoft.com/json-schemas/"):
                failures.append(f"{name}: $schema '{url}' is not a Microsoft schema URL")
                continue
            schema = fetch_schema(url)
            validator = jsonschema.validators.validator_for(schema)(schema)
            errors = sorted(validator.iter_errors(doc), key=lambda e: list(e.path))
            for e in errors:
                failures.append(f"{name}: {'/'.join(map(str, e.path)) or '(root)'}: {e.message[:300]}")
            print(f"{name}: validated against {url} ({len(errors)} error(s))")

        oa = yaml.safe_load(z.read("spaarke-bff-openapi.yaml").decode("utf-8"))
        if oa["servers"][0]["url"] != a.bff_base_url:
            failures.append(f"openapi servers[0].url is {oa['servers'][0]['url']!r}")
        flow = oa["components"]["securitySchemes"]["oAuth2AuthCode"]["flows"]["authorizationCode"]
        for key, ep in (("authorizationUrl", "authorize"), ("tokenUrl", "token")):
            want = f"https://login.microsoftonline.com/{a.tenant_id}/oauth2/v2.0/{ep}"
            if flow[key] != want:
                failures.append(f"openapi {key} is {flow[key]!r}, expected {want!r}")
        if list(flow["scopes"].keys()) != [a.scope]:
            failures.append(f"openapi scopes are {list(flow['scopes'].keys())}, expected [{a.scope!r}]")
        if oa.get("security") != [{"oAuth2AuthCode": [a.scope]}]:
            failures.append(f"openapi security is {oa.get('security')!r}")
        for icon in ("color.png", "outline.png"):
            if icon not in names:
                failures.append(f"{icon} missing")

    for f in failures:
        print("FAIL:", f)
    if failures:
        sys.exit(1)
    print("OK: rendered package matches Microsoft's schemas and carries the given values")


if __name__ == "__main__":
    main()

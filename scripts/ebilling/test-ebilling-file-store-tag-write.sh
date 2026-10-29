#!/usr/bin/env bash
# scripts/ebilling/test-ebilling-file-store-tag-write.sh
# ---------------------------------------------------------------------------------------------------------------------
# ebilling-r1 task 091, unified-access-control-r2 condition 2: LIVE proof that the identity holding the file-store custom
# role (modules/ebilling-file-store.bicep) can NOT write blob index tags - so it cannot forge Defender's malware-scan verdict.
#
# RUN IT AS THE IDENTITY, NOT AS AN OPERATOR: paste or upload it into the BFF App Service's SSH console (Linux,
#   az webapp ssh -g <rg> -n <app>   or   Portal > the App Service > SSH) and run
#   bash test-ebilling-file-store-tag-write.sh <storage-account> [container] [uami-client-id]
# The token comes from the App Service managed-identity endpoint (IDENTITY_ENDPOINT / IDENTITY_HEADER) for the UAMI whose
# client id is given, else the app's ManagedIdentity__ClientId. The token is never printed or written to disk.
#
# Steps and expected results (exit 0 only when every one matches):
#   1. Put Blob, no tags                         -> 201  control: the identity can write here at all
#   2. Set Blob Tags on that blob                -> 403  MUST be refused (no .../blobs/tags/write)
#   3. Put Blob with x-ms-tags (a second blob)   -> 403  MUST be refused (setting tags on upload needs tags/write too)
#   4. Get Blob Tags on the first blob           -> 200  control: .../blobs/tags/read is granted
#   5. Delete the probe blob(s)                  -> 202
# Probe names are _verify-091/<utc>-<random>-{a,b}.txt: outside the file store's {tenantId}/... names, so nothing serves them.
# Exit 0 = proof holds; 1 = a step returned something else (the role or the platform behaves differently - stop and report);
# 2 = could not run (no identity endpoint, no token, bad arguments).
# ---------------------------------------------------------------------------------------------------------------------
set -uo pipefail

ACCOUNT="${1:-}"
CONTAINER="${2:-invoice-files}"
if [[ -z "$ACCOUNT" ]]; then
    echo "usage: $0 <storage-account> [container (default invoice-files)] [uami-client-id]" >&2
    exit 2
fi

# The app's environment: present in the SSH shell on most images; otherwise read it from the app's init process.
envvar() {
    local name="$1" value="${!1:-}"
    if [[ -z "$value" && -r /proc/1/environ ]]; then
        value="$(tr '\0' '\n' < /proc/1/environ | sed -n "s/^${name}=//p" | head -n 1)"
    fi
    printf '%s' "$value"
}

CLIENT_ID="${3:-$(envvar ManagedIdentity__ClientId)}"
ID_ENDPOINT="$(envvar IDENTITY_ENDPOINT)"
ID_HEADER="$(envvar IDENTITY_HEADER)"
if [[ -z "$ID_ENDPOINT" || -z "$ID_HEADER" ]]; then
    echo "[FAIL] no managed-identity endpoint here (IDENTITY_ENDPOINT / IDENTITY_HEADER) - run this in the App Service SSH console" >&2
    exit 2
fi
if [[ -z "$CLIENT_ID" ]]; then
    echo "[FAIL] no UAMI client id (argument 3, or the app setting ManagedIdentity__ClientId)" >&2
    exit 2
fi

TOKEN="$(curl -s -H "X-IDENTITY-HEADER: $ID_HEADER" \
    "$ID_ENDPOINT?api-version=2019-08-01&resource=https%3A%2F%2Fstorage.azure.com%2F&client_id=$CLIENT_ID" \
    | sed -n 's/.*"access_token" *: *"\([^"]*\)".*/\1/p')"
if [[ -z "$TOKEN" ]]; then
    echo "[FAIL] the identity endpoint returned no storage token for that client id" >&2
    exit 2
fi
echo "[INFO] token acquired for the UAMI (client id not shown); account $ACCOUNT, container $CONTAINER"

BASE="https://$ACCOUNT.blob.core.windows.net/$CONTAINER"
PROBE="_verify-091/$(date -u +%Y%m%dT%H%M%SZ)-$RANDOM$RANDOM"
VERSION="2023-11-03"
TAG_KEY="Malware Scanning scan result"
TAG_VALUE="No threats found"
FAILED=0
LAST_BODY="$(mktemp)"
trap 'rm -f "$LAST_BODY"' EXIT

# call <method> <url> [extra curl args...] -> prints the HTTP status; the response body (never the token) goes to $LAST_BODY.
call() {
    local method="$1" url="$2"
    shift 2
    curl -s -o "$LAST_BODY" -w '%{http_code}' -X "$method" "$url" \
        -H "Authorization: Bearer $TOKEN" \
        -H "x-ms-version: $VERSION" \
        -H "x-ms-date: $(date -u '+%a, %d %b %Y %H:%M:%S GMT')" \
        "$@"
}

# expect <step> <expected status> <actual status>
expect() {
    local code
    code="$(sed -n 's/.*<Code>\([^<]*\)<\/Code>.*/\1/p' "$LAST_BODY" | head -n 1)"
    if [[ "$3" == "$2" ]]; then
        echo "[PASS] $1: HTTP $3${code:+ ($code)}"
    else
        echo "[FAIL] $1: expected HTTP $2, got HTTP $3${code:+ ($code)}"
        FAILED=1
    fi
}

TAGS_XML="<?xml version=\"1.0\" encoding=\"utf-8\"?><Tags><TagSet><Tag><Key>$TAG_KEY</Key><Value>$TAG_VALUE</Value></Tag></TagSet></Tags>"

status="$(call PUT "$BASE/$PROBE-a.txt" -H "x-ms-blob-type: BlockBlob" -H "Content-Type: text/plain" --data-binary "ebilling-r1 091 probe")"
expect "1 Put Blob (no tags) - control" 201 "$status"
if [[ "$status" != "201" ]]; then
    echo "[FAIL] the identity cannot write to $CONTAINER, so the tag checks below would prove nothing - stopping" >&2
    exit 1
fi

status="$(call PUT "$BASE/$PROBE-a.txt?comp=tags" -H "Content-Type: application/xml; charset=UTF-8" --data-binary "$TAGS_XML")"
expect "2 Set Blob Tags - must be refused" 403 "$status"

status="$(call PUT "$BASE/$PROBE-b.txt" -H "x-ms-blob-type: BlockBlob" -H "Content-Type: text/plain" \
    -H "x-ms-tags: Malware%20Scanning%20scan%20result=No%20threats%20found" --data-binary "ebilling-r1 091 probe")"
expect "3 Put Blob with x-ms-tags - must be refused" 403 "$status"
B_CREATED="$status"

status="$(call GET "$BASE/$PROBE-a.txt?comp=tags")"
expect "4 Get Blob Tags - control (tags/read granted)" 200 "$status"

status="$(call DELETE "$BASE/$PROBE-a.txt")"
expect "5 Delete probe a" 202 "$status"
if [[ "$B_CREATED" == "201" ]]; then
    status="$(call DELETE "$BASE/$PROBE-b.txt")"
    expect "5 Delete probe b (it should never have been created)" 202 "$status"
fi

if [[ "$FAILED" -ne 0 ]]; then
    echo "RESULT: FAIL - the file-store identity is not proved unable to write tags (uac-r2 condition 2). Do not enable downloads; report."
    exit 1
fi
echo "RESULT: PASS - Set Blob Tags and Put Blob with x-ms-tags are 403 for this identity; blob write and tag read work."
exit 0

#!/usr/bin/env python3
"""Decode JWT token and display claims"""
import base64
import json
import sys

def decode_jwt(token):
    """Decode JWT token (no signature verification)"""
    parts = token.strip().split('.')
    if len(parts) != 3:
        print(f"❌ Invalid JWT: expected 3 parts, got {len(parts)}")
        return None

    # Decode header
    header_b64 = parts[0]
    # Add padding if needed
    padding = '=' * (4 - len(header_b64) % 4)
    header_json = base64.urlsafe_b64decode(header_b64 + padding)
    header = json.loads(header_json)

    # Decode payload
    payload_b64 = parts[1]
    padding = '=' * (4 - len(payload_b64) % 4)
    payload_json = base64.urlsafe_b64decode(payload_b64 + padding)
    payload = json.loads(payload_json)

    return {
        'header': header,
        'payload': payload
    }

if __name__ == '__main__':
    if len(sys.argv) < 2:
        print("Usage: python decode-jwt.py <token-file>")
        sys.exit(1)

    token_file = sys.argv[1]
    with open(token_file, 'r') as f:
        token = f.read().strip()

    result = decode_jwt(token)
    if result:
        print("=" * 80)
        print("JWT HEADER")
        print("=" * 80)
        print(json.dumps(result['header'], indent=2))
        print()
        print("=" * 80)
        print("JWT PAYLOAD (CLAIMS)")
        print("=" * 80)
        print(json.dumps(result['payload'], indent=2))
        print()
        print("=" * 80)
        print("KEY CLAIMS VERIFICATION")
        print("=" * 80)
        payload = result['payload']
        print(f"✓ Audience (aud):    {payload.get('aud')}")
        print(f"✓ Issuer (iss):      {payload.get('iss')}")
        print(f"✓ Subject (sub):     {payload.get('sub', payload.get('oid'))}")
        print(f"✓ Client (appid):    {payload.get('appid', payload.get('azp'))}")
        print(f"✓ Scopes (scp):      {payload.get('scp', 'N/A')}")
        print(f"✓ Issued At (iat):   {payload.get('iat')}")
        print(f"✓ Expires (exp):     {payload.get('exp')}")
        print(f"✓ Name:              {payload.get('name', 'N/A')}")
        print(f"✓ UPN:               {payload.get('upn', payload.get('unique_name', 'N/A'))}")

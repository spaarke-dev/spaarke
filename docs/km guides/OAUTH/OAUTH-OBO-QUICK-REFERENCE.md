KM-1.01-OBO-OAUTH-OBO-QUICK-REFERENCE

# OAuth 2.0 OBO Flow - Quick Reference

## When to Use
```
Need to call downstream API on behalf of user?
├─ YES
│   ├─ Have user token? YES → Use OBO (this guide)
│   └─ No user token? → ERROR: OBO requires user token
└─ NO → Use Client Credentials instead
```

## Quick Checklist

Before implementing OBO:
- [ ] Middle-tier API is confidential client (has secret)
- [ ] Have valid user access token
- [ ] Token audience matches your API (`api://{your-api-id}`)
- [ ] Using `.default` scope: `https://graph.microsoft.com/.default`
- [ ] `knownClientApplications` configured in app manifest
- [ ] Error handling for `MsalServiceException`

## Common Errors → Quick Fixes

| Error | Fix |
|-------|-----|
| `AADSTS50013` | Token audience mismatch - validate incoming token |
| `AADSTS70011` | Wrong scope format - use `.default` |
| `AADSTS65001` | No consent - add `knownClientApplications` |
| `invalid_grant` | User token expired - user must re-auth |

## Files to Reference

- Implementation: `.claude/knowledge/oauth-obo-implementation.md`
- Anti-patterns: `.claude/knowledge/oauth-obo-anti-patterns.md`
- Testing: `.claude/knowledge/oauth-obo-testing.md`
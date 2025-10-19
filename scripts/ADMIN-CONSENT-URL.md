# Admin Consent URL for SharePoint Permissions

## The Problem

Azure Portal's "Grant admin consent" button is not working for SharePoint `FileStorageContainer.Selected` permission.

## The Solution

Use the **programmatic admin consent endpoint** to grant consent directly.

---

## Admin Consent URL

**Click this URL (you must be signed in as Global Admin or App Admin):**

```
https://login.microsoftonline.com/a221a95e-6abc-4434-aecc-e48338a1b2f2/v2.0/adminconsent?client_id=170c98e1-d486-4355-bcbe-170454e0207c&scope=https://graph.microsoft.com/.default https://spaarke.sharepoint.com/.default&redirect_uri=https://localhost&state=consent_request
```

### URL Breakdown

| Parameter | Value | Purpose |
|-----------|-------|---------|
| `tenant` | `a221a95e-6abc-4434-aecc-e48338a1b2f2` | Your tenant ID |
| `client_id` | `170c98e1-d486-4355-bcbe-170454e0207c` | PCF app (Spaarke DSM-SPE Dev 2) |
| `scope` | `https://graph.microsoft.com/.default https://spaarke.sharepoint.com/.default` | Request ALL permissions (Graph + SharePoint) |
| `redirect_uri` | `https://localhost` | Where to redirect after consent |
| `state` | `consent_request` | Optional state tracking |

---

## What Happens

1. **You click the URL above**
2. **Sign in** (if not already signed in) with admin account
3. **Review permissions** - You'll see:
   - Microsoft Graph permissions (FileStorageContainer.Selected, etc.)
   - SharePoint permissions (FileStorageContainer.Selected)
4. **Click "Accept"**
5. **Redirected to** `https://localhost?admin_consent=True&tenant=...`
6. Browser shows error (localhost not running) - **THIS IS OK!**
7. **Consent is granted** even though redirect fails

---

## After Consent is Granted

Check the URL in your browser after clicking Accept. You should see:

```
https://localhost/?admin_consent=True&tenant=a221a95e-6abc-4434-aecc-e48338a1b2f2&scope=...&state=consent_request
```

**Key:** `admin_consent=True` means it worked!

Then run the registration script:

```powershell
.\scripts\Register-BffApiWithContainerType.ps1
```

**Expected:** HTTP 200 OK (consent is now granted)

---

## Alternative: Using .default Scope

If the above URL doesn't work, try this simplified version using only `.default`:

```
https://login.microsoftonline.com/a221a95e-6abc-4434-aecc-e48338a1b2f2/v2.0/adminconsent?client_id=170c98e1-d486-4355-bcbe-170454e0207c&scope=https://spaarke.sharepoint.com/.default&redirect_uri=https://localhost
```

This requests consent for ONLY SharePoint permissions (not Graph).

---

## Troubleshooting

### "You do not have permission to grant consent"

**Cause:** Your account is not Global Admin or Privileged Role Admin

**Solution:** Ask someone with Global Admin role to click the URL

### "Invalid redirect_uri"

**Cause:** `https://localhost` is not in the app's redirect URIs

**Solution:** Add it temporarily:
1. Azure Portal → App Registrations → Spaarke DSM-SPE Dev 2
2. Authentication → Add URI → `https://localhost`
3. Save
4. Try consent URL again
5. Remove `https://localhost` after consent is granted

### Browser shows "can't reach this page" after consent

**This is NORMAL!** The consent was granted. Check the URL - if it contains `admin_consent=True`, you're good.

---

## Verify Consent Was Granted

After clicking the URL and seeing `admin_consent=True`:

**Option 1: Via Azure Portal**
1. Azure Portal → Enterprise Applications
2. Search for "Spaarke DSM-SPE Dev 2"
3. Click → Permissions (left menu)
4. Look for SharePoint permissions - should show "Granted"

**Option 2: Via Script**
```powershell
.\scripts\Debug-RegistrationFailure.ps1
```

Should show "✅ Token HAS Container.Selected permission" and then succeed at reading container type.

---

## Why This Works When Portal Button Doesn't

The Azure Portal "Grant admin consent" button sometimes fails for:
- Custom/preview permissions
- SharePoint resource permissions (non-Graph)
- Permissions added via manifest (not UI)

The **programmatic admin consent endpoint** bypasses the Portal UI and directly invokes the Microsoft Entra ID consent flow, which is more reliable.

---

## After Successful Consent

1. **Verify consent worked** (see above)
2. **Run registration script:**
   ```powershell
   .\scripts\Register-BffApiWithContainerType.ps1
   ```
3. **Expected:** Registration succeeds (HTTP 200)
4. **Test OBO upload** - 403 error should be gone!

---

**Ready to try?** Click the admin consent URL above!

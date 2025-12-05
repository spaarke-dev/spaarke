# Office File "Open in Desktop" Authorization Guide

## Overview

The Spaarke platform provides an "Open in Desktop" feature that allows users to open SharePoint Embedded documents directly in Microsoft Office desktop applications (Word, Excel, PowerPoint). This feature uses Office protocol handlers (`ms-word:`, `ms-excel:`, `ms-powerpoint:`) to launch the desktop applications.

## Requirement

For this feature to work, the SharePoint domain must be configured as a **Trusted Site** in Windows. Without this configuration, users will see the following error when clicking "Open in Desktop":

> **Unsafe Content**
> To protect you from unsafe content, Office has blocked opening this file because it is coming from a site in the Restricted Sites zone.

## Why This Configuration Is Needed

Microsoft Office desktop applications enforce security zones inherited from Windows/Internet Explorer settings. By default, SharePoint Embedded URLs may not be recognized as trusted, causing Office to block the file open request as a security precaution.

Adding the SharePoint domain to the Trusted Sites zone tells Windows and Office that files from this source are safe to open.

---

## Configuration Methods

### Method 1: Microsoft Intune (Recommended for Cloud-Managed Devices)

**Prerequisites:**
- Microsoft Intune admin access
- Devices enrolled in Intune

**Steps:**

1. Sign in to the [Microsoft Intune admin center](https://intune.microsoft.com)
2. Navigate to **Devices** → **Configuration profiles**
3. Click **Create profile**
4. Configure:
   - **Platform**: Windows 10 and later
   - **Profile type**: Settings Catalog
5. Click **Create**
6. Name the profile: `Spaarke - Trusted Sites Configuration`
7. In **Configuration settings**, click **Add settings**
8. Search for **"Site to Zone Assignment List"**
9. Select the setting under **Administrative Templates > Windows Components > Internet Explorer > Internet Control Panel > Security Page**
10. Enable the setting and add these entries:

| Site URL | Zone Value |
|----------|------------|
| `https://*.sharepoint.com` | `2` |
| `https://*.sharepoint-df.com` | `2` |

**Zone Values:**
- `1` = Local Intranet
- `2` = Trusted Sites (recommended)
- `3` = Internet Zone
- `4` = Restricted Sites

11. Click **Next** through the remaining steps
12. Assign the profile to the appropriate device groups
13. Click **Create**

**Verification:**
- Policy will apply at next device sync
- Users may need to restart Office applications

---

### Method 2: Group Policy (On-Premises Active Directory)

**Prerequisites:**
- Group Policy Management Console access
- Active Directory domain environment

**Steps:**

1. Open **Group Policy Management Console** (gpmc.msc)
2. Create a new GPO or edit an existing one
3. Navigate to:
   ```
   Computer Configuration
   └── Administrative Templates
       └── Windows Components
           └── Internet Explorer
               └── Internet Control Panel
                   └── Security Page
                       └── Site to Zone Assignment List
   ```
4. Double-click **Site to Zone Assignment List**
5. Select **Enabled**
6. Click **Show...** to open the value list
7. Add these entries:

| Value name | Value |
|------------|-------|
| `https://*.sharepoint.com` | `2` |
| `https://*.sharepoint-df.com` | `2` |

8. Click **OK** to save
9. Link the GPO to the appropriate Organizational Unit (OU)
10. Run `gpupdate /force` on client machines or wait for policy refresh

**Verification:**
```cmd
gpresult /r /scope computer
```
Look for the policy in the applied GPOs list.

---

### Method 3: Registry Deployment (Script-Based)

For environments using configuration management tools (SCCM, PDQ Deploy, etc.), deploy this registry configuration:

**Registry Path:**
```
HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\CurrentVersion\Internet Settings\ZoneMapKey
```

**PowerShell Script:**
```powershell
# Spaarke - Configure Trusted Sites for Office Desktop Integration
# Run as Administrator

$regPath = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\CurrentVersion\Internet Settings\ZoneMapKey"

# Create the key if it doesn't exist
if (-not (Test-Path $regPath)) {
    New-Item -Path $regPath -Force | Out-Null
}

# Add SharePoint domains as Trusted Sites (Zone 2)
Set-ItemProperty -Path $regPath -Name "https://*.sharepoint.com" -Value 2 -Type DWord
Set-ItemProperty -Path $regPath -Name "https://*.sharepoint-df.com" -Value 2 -Type DWord

Write-Host "Trusted Sites configured successfully for Spaarke Office integration."
```

**Batch Script Alternative:**
```batch
@echo off
REM Spaarke - Configure Trusted Sites for Office Desktop Integration
REM Run as Administrator

reg add "HKLM\SOFTWARE\Policies\Microsoft\Windows\CurrentVersion\Internet Settings\ZoneMapKey" /v "https://*.sharepoint.com" /t REG_DWORD /d 2 /f
reg add "HKLM\SOFTWARE\Policies\Microsoft\Windows\CurrentVersion\Internet Settings\ZoneMapKey" /v "https://*.sharepoint-df.com" /t REG_DWORD /d 2 /f

echo Trusted Sites configured successfully.
```

---

### Method 4: Manual Configuration (Individual Users)

For testing or individual user setup:

1. Press **Win + R**, type `inetcpl.cpl`, press Enter
2. Go to the **Security** tab
3. Select **Trusted sites**
4. Click **Sites**
5. Uncheck **"Require server verification (https:) for all sites in this zone"** (if needed)
6. Add these URLs:
   ```
   https://*.sharepoint.com
   https://*.sharepoint-df.com
   ```
7. Click **Add** for each URL
8. Click **Close**
9. Click **OK**
10. Restart any open Office applications

---

## Verification

After configuration, verify the setting is applied:

### Check via Internet Options
1. Open Internet Options (inetcpl.cpl)
2. Go to Security → Trusted sites → Sites
3. Verify the SharePoint URLs are listed

### Check via Registry
```powershell
Get-ItemProperty "HKLM:\SOFTWARE\Policies\Microsoft\Windows\CurrentVersion\Internet Settings\ZoneMapKey"
```

### Test the Feature
1. Open the Spaarke application
2. Navigate to a document record
3. Click **"Open in Desktop"**
4. The document should open in the appropriate Office application

---

## Troubleshooting

### Error: "Office has blocked opening this file"

**Cause:** SharePoint domain is not in Trusted Sites zone.

**Solution:** Follow one of the configuration methods above.

### Error: "The file could not be accessed"

**Cause:** Network connectivity or authentication issue.

**Solution:**
1. Verify the user is signed into Office with the correct Microsoft 365 account
2. Check network connectivity to SharePoint
3. Try signing out and back into Office

### Policy Not Applying

**For Intune:**
1. Check device compliance in Intune admin center
2. Force a device sync: Settings → Accounts → Access work or school → Info → Sync
3. Verify no conflicting policies

**For Group Policy:**
1. Run `gpresult /r` to check applied policies
2. Verify GPO is linked to the correct OU
3. Check for WMI filters or security filtering

### Office Still Blocking After Configuration

1. Restart all Office applications
2. Sign out and back into Office
3. Clear Office credential cache:
   ```
   %localappdata%\Microsoft\Office\16.0\Licensing
   ```
4. Restart the computer

---

## Security Considerations

- Adding domains to Trusted Sites reduces browser security for those domains
- Only add the specific SharePoint domains required
- This configuration is standard for enterprise SharePoint deployments
- The same configuration is often required for SharePoint Online and OneDrive

---

## Deployment Checklist

- [ ] Identify deployment method (Intune, GPO, Script, or Manual)
- [ ] Configure trusted sites with SharePoint domains
- [ ] Deploy configuration to pilot group
- [ ] Verify "Open in Desktop" feature works
- [ ] Deploy to production users
- [ ] Document in tenant setup procedures
- [ ] Include in new device provisioning process

---

## Related Documentation

- [Microsoft: Site to Zone Assignment List](https://learn.microsoft.com/en-us/deployedge/per-site-configuration-by-policy)
- [Microsoft: Deploy Trusted Sites via Intune](https://learn.microsoft.com/en-us/mem/intune/configuration/administrative-templates-windows)
- [Microsoft: Office Protocol Handlers](https://learn.microsoft.com/en-us/office/client-developer/office-uri-schemes)

---

## Document Information

| Field | Value |
|-------|-------|
| Created | December 2025 |
| Author | Spaarke Development Team |
| Version | 1.0 |
| Applies To | Spaarke FileViewer, Office Desktop Integration |

---
name: teams-app-upload-icon-invalid-and-add-disabled-2026-10-29
description: Teams custom app upload error "Provided image file color.png is invalid ... too small" (no doc/community source found) plus disabled Add / "Permissions needed" banner for admin-uploaded personal app
metadata:
  type: reference
---

## 2026-10-29: Teams custom app upload - icon "empty or too small" error + disabled Add
**Question**: What produces "Provided image file color.png/outline.png is invalid. The image is either empty or too small" on admin-center upload, and why is Add disabled ("Permissions needed") after the app appears?
**Findings**: The exact error string appears NOWHERE in Learn, msteams-docs, Q&A, TechCommunity or GitHub (gh search issues/code = 0 relevant hits). Documented icon rules are dimensions only: color 192x192 PNG, outline 32x32 PNG white-on-transparent (or transparent-on-white), no documented min/max byte size, bit depth, interlace or palette rule (schema root-icons; store-validation guidelines; design-teams-app-icon page). Zip-structure errors give different messages (manifest not found / "App package is invalid"), and since manifest parsed fine the failure is icon decode/validation -> cause unproven (inference: server-side image decode of the PNG bytes; check for CI byte corruption, e.g. CRLF/LFS/artifact re-zip, and test via Developer Portal Import). Add disabled: org-uploaded custom app is governed by Manage apps Status + app-centric "Available to" (Everyone / Specific users or groups / No one; guests excluded under Specific) + Org-wide app settings "Custom apps: Let users install and use available apps by default"; app permission policies deprecated post-ACM (auto-migrated from April 2025); changes take up to 24h (rarely 6 days). Setup-policy "Upload custom apps" governs USER sideloading only.
**Sources**:
- https://learn.microsoft.com/en-us/microsoft-365/extensibility/schema/root-icons
- https://learn.microsoft.com/en-us/microsoftteams/platform/concepts/design/design-teams-app-icon-store-appbar
- https://learn.microsoft.com/en-us/microsoftteams/platform/resources/troubleshoot
- https://learn.microsoft.com/en-us/microsoftteams/teams-custom-app-policies-and-settings
- https://learn.microsoft.com/en-us/microsoftteams/manage-apps
- https://learn.microsoft.com/en-us/microsoftteams/app-centric-management
- https://learn.microsoft.com/en-us/microsoftteams/teams-app-setup-policies
- https://learn.microsoft.com/en-us/answers/questions/909365/permissions-needed-ask-your-it-admin-to-add-app-to (community)
**Open questions**: actual validator and any min-size heuristic undocumented; whether Teams web in a team-channel context shows "Permissions needed" for a personal-only app (inferred, unconfirmed); whether the user is a B2B guest (guests blocked under Specific users/groups).

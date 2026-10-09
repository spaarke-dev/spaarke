# Spaarke AI for Microsoft Copilot — Onboarding Note for Your IT Team

> **Audience**: the customer's Microsoft 365 administrators.
> **Sent with**: your organization's package `spaarke-copilot-{your id}-{version}.zip`.
> **Last Updated**: 2026-10-09 (customer-provisioning-orchestration-r1 T257).
> **Spaarke-side procedure**: [`SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`](SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md) §7.12.

## What this is

Spaarke AI is an agent for Microsoft Copilot. It lets your staff ask Copilot about their Spaarke matters, documents,
tasks and playbooks, such as "What are my overdue tasks?". It answers from **your organization's Spaarke environment
only**.

The package was built for your organization alone. It connects only to your Spaarke environment, and no other
customer's package can.

## Before you start

- **Your staff have Spaarke accounts.** Spaarke invited them as guests to its tenant when your environment was set up.
  Only these people can sign in to the agent. Others in your organization can see it if you assign it to them, but
  their sign-in is refused.
- **Your Microsoft 365 plan includes Microsoft Copilot Chat.** Most business plans do, including Business Basic. The
  agent needs **no Copilot add-on licence and no pay-as-you-go metering**: it uses no Microsoft 365 knowledge (no mail,
  files, chats or people search). It uses only Spaarke's own API.
- **Your tenant allows custom agents to be uploaded** (Microsoft 365 admin center → Agents settings). You need the
  Global Administrator, AI Administrator or Teams Administrator role.

## Install (once)

1. Go to **Microsoft 365 admin center → Agents → All agents → Upload custom agent** and select the `.zip` we sent.
2. **Assign** it to the users or groups who have Spaarke accounts. Use the same staff you asked us to invite.
3. Within a few hours they see **Spaarke AI** in Microsoft Copilot, in Teams, Outlook and on the web.

The package declares **no Microsoft 365 permissions**: no Graph scopes, no bot and no access to your tenant's data. You
are not asked to grant consent for anything in your tenant.

## What your users see

The first time a user asks Spaarke AI something, it asks them to **sign in with their Spaarke account**. This is the
guest account Spaarke created for them, under the same email address. After that it stays signed in; it may ask again
from time to time. What they can see in Spaarke is exactly what their Spaarke role allows, the same as in the Spaarke
app.

## Updates

When Spaarke changes the agent, we send you a new package with a higher version number. Upload it over the existing
app: **Integrated apps → Spaarke AI → Update**. Your assignments stay, and users get the new version without being asked
anything. The app's identity never changes, so an update never adds a second Spaarke AI.

## Removing it

Delete the app in **Integrated apps**, then tell Spaarke. Spaarke removes the sign-in registration on its side.

## Troubleshooting

| What the user sees | Usual cause | What to do |
|---|---|---|
| Spaarke AI doesn't appear | Not assigned to this user yet, or the assignment hasn't propagated | Check the assignment; allow a few hours |
| Sign-in says the account doesn't exist in Spaarke's tenant (`AADSTS50020`) | The user has no Spaarke guest account | Ask Spaarke to invite the user |
| Sign-in is blocked by policy | Your Conditional Access or cross-tenant **outbound** access settings block sign-in to Spaarke's tenant | Allow outbound B2B collaboration to Spaarke's tenant for these users (Entra admin center → External Identities → Cross-tenant access settings) |
| "You don't have access" or empty answers | The user's Spaarke role doesn't cover that data | Ask Spaarke (or your Spaarke administrator) to check the user's role |
| The upload is refused | Custom agent upload is turned off in your tenant | Allow it in the Agents settings, or upload as a Global Administrator |

Contact your Spaarke representative for anything else. Include the user's email address and the time of the attempt.

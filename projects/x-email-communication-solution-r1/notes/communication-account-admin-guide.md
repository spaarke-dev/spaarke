# Communication Account Admin Guide

> **Entity**: `sprk_communicationaccount`
> **Purpose**: Manage mailbox accounts used for sending and receiving email communications within the Spaarke platform.
> **Environment**: make.powerapps.com (Dataverse model-driven app)

---

## 1. Form Layout

Create a main form for `sprk_communicationaccount` with the following sections. Navigate to **Tables > Communication Account > Forms** in make.powerapps.com and create a new **Main** form.

### Section 1: Core Identity

| Display Name     | Logical Name          | Type           | Notes                                                                                              |
|------------------|-----------------------|----------------|----------------------------------------------------------------------------------------------------|
| Name             | `sprk_name`           | Single Line    | Primary name column. Required.                                                                     |
| Email Address    | `sprk_emailaddress`   | Single Line    | The mailbox email address (e.g., mailbox-central@spaarke.com). Required.                           |
| Display Name     | `sprk_displayname`    | Single Line    | Friendly name shown in email "From" field.                                                         |
| Account Type     | `sprk_accounttype`    | Choice         | **Shared** = 100000000, **Service** = 100000001, **User** = 100000002. Determines mailbox behavior.|

### Section 2: Outbound Configuration

| Display Name       | Logical Name            | Type      | Notes                                                                 |
|--------------------|-------------------------|-----------|-----------------------------------------------------------------------|
| Send Enabled       | `sprk_sendenableds`     | Yes/No    | Whether this account can send emails. Note the trailing **s** in the logical name. |
| Is Default Sender  | `sprk_isdefaultsender`  | Yes/No    | If true, this account is used as the default "From" address for outbound emails.   |

### Section 3: Inbound Configuration

| Display Name        | Logical Name              | Type         | Notes                                                        |
|---------------------|---------------------------|--------------|--------------------------------------------------------------|
| Receive Enabled     | `sprk_receiveenabled`     | Yes/No       | Whether this account monitors for incoming email.            |
| Monitor Folder      | `sprk_monitorfolder`      | Single Line  | The mailbox folder to monitor. Default value: **Inbox**.     |
| Auto Create Records | `sprk_autocreaterecords`  | Yes/No       | If true, automatically creates Communication records from incoming mail. |

### Section 4: Graph Integration (Read-Only)

Mark both fields as **read-only** on the form. These are populated programmatically by the Graph subscription management process.

| Display Name         | Logical Name               | Type         | Notes                                                    |
|----------------------|----------------------------|--------------|----------------------------------------------------------|
| Subscription Id      | `sprk_subscriptionid`      | Single Line  | The Microsoft Graph change notification subscription ID. |
| Subscription Expiry  | `sprk_subscriptionexpiry`  | Date/Time    | When the Graph subscription expires and needs renewal.   |

### Section 5: Security

| Display Name         | Logical Name              | Type         | Notes                                                      |
|----------------------|---------------------------|--------------|-------------------------------------------------------------|
| Security Group Id    | `sprk_securitygroupid`    | Single Line  | Azure AD security group ID controlling access to this account. |
| Security Group Name  | `sprk_securitygroupname`  | Single Line  | Display name of the associated security group.              |

### Section 6: Verification (Read-Only)

Mark both fields as **read-only** on the form. These are updated by the verification background process.

| Display Name         | Logical Name               | Type     | Notes                                                                  |
|----------------------|----------------------------|----------|-------------------------------------------------------------------------|
| Verification Status  | `sprk_verificationstatus`  | Choice   | **Verified** = 100000000, **Failed** = 100000001, **Pending** = 100000002. |
| Last Verified        | `sprk_lastverified`        | Date/Time| Timestamp of the most recent successful verification.                   |

### Form Creation Steps

1. Go to **make.powerapps.com** > **Tables** > **Communication Account** > **Forms**.
2. Click **+ New form** > **Main form**.
3. Add a **1-column tab** for each section above.
4. Drag the columns listed in each section onto the form.
5. For the **Graph Integration** and **Verification** sections, select each field, open its properties, and check **Read-only**.
6. Set the **Monitor Folder** field default value to `Inbox`.
7. **Save and Publish** the form.

---

## 2. Views

Navigate to **Tables > Communication Account > Views** in make.powerapps.com to create each view.

### View 1: Active Communication Accounts

**Purpose**: Default view showing all active communication account records.

- **Filter**: `statecode eq 0` (Active)
- **Sort**: Name ascending

| Column           | Logical Name           | Width  |
|------------------|------------------------|--------|
| Name             | `sprk_name`            | 200px  |
| Email Address    | `sprk_emailaddress`    | 250px  |
| Account Type     | `sprk_accounttype`     | 150px  |
| Send Enabled     | `sprk_sendenableds`    | 120px  |
| Receive Enabled  | `sprk_receiveenabled`  | 130px  |
| Is Default       | `sprk_isdefaultsender` | 120px  |

### View 2: Send-Enabled Accounts

**Purpose**: Quick access to all accounts configured for outbound email.

- **Filter**: `sprk_sendenableds eq true` AND `statecode eq 0`
- **Sort**: Name ascending

| Column              | Logical Name              | Width  |
|---------------------|---------------------------|--------|
| Name                | `sprk_name`               | 200px  |
| Email Address       | `sprk_emailaddress`       | 250px  |
| Display Name        | `sprk_displayname`        | 200px  |
| Account Type        | `sprk_accounttype`        | 150px  |
| Is Default          | `sprk_isdefaultsender`    | 120px  |
| Verification Status | `sprk_verificationstatus` | 150px  |

### View 3: Receive-Enabled Accounts

**Purpose**: Quick access to all accounts configured for inbound email monitoring.

- **Filter**: `sprk_receiveenabled eq true` AND `statecode eq 0`
- **Sort**: Name ascending

| Column              | Logical Name              | Width  |
|---------------------|---------------------------|--------|
| Name                | `sprk_name`               | 200px  |
| Email Address       | `sprk_emailaddress`       | 250px  |
| Monitor Folder      | `sprk_monitorfolder`      | 150px  |
| Subscription Expiry | `sprk_subscriptionexpiry` | 180px  |
| Auto Create Records | `sprk_autocreaterecords`  | 160px  |

### View 4: Default Senders

**Purpose**: Identify which accounts are designated as default senders.

- **Filter**: `sprk_isdefaultsender eq true` AND `statecode eq 0`
- **Sort**: Name ascending

| Column       | Logical Name        | Width  |
|--------------|---------------------|--------|
| Name         | `sprk_name`         | 200px  |
| Email Address| `sprk_emailaddress` | 250px  |
| Display Name | `sprk_displayname`  | 200px  |
| Account Type | `sprk_accounttype`  | 150px  |

### View Creation Steps

1. Go to **make.powerapps.com** > **Tables** > **Communication Account** > **Views**.
2. Click **+ New view**, enter the view name.
3. Add columns from the tables above using **+ View column**.
4. Apply filters using **Edit filters** in the command bar.
5. Set sort order by clicking on the column header.
6. **Save and Publish** each view.
7. Set **Active Communication Accounts** as the default view.

---

## 3. Seed Data

After the form and views are published, create the initial communication account record.

### Record: Spaarke Central Mailbox

| Field                | Value                            |
|----------------------|----------------------------------|
| Name                 | Spaarke Central Mailbox          |
| Email Address        | mailbox-central@spaarke.com      |
| Display Name         | Spaarke Central                  |
| Account Type         | Shared Account (100000000)       |
| Send Enabled         | Yes                              |
| Is Default Sender    | Yes                              |
| Receive Enabled      | No (to be enabled in Phase 8)    |
| Monitor Folder       | Inbox (default)                  |
| Auto Create Records  | No                               |
| Verification Status  | Pending (100000002)              |

### Seed Data Steps

1. Open the model-driven app containing the Communication Account table.
2. Navigate to the **Communication Account** area.
3. Click **+ New** to open the form.
4. Enter the values from the table above.
5. Click **Save & Close**.
6. Verify the record appears in the **Active Communication Accounts** view.
7. Verify the record appears in the **Send-Enabled Accounts** view.
8. Verify the record appears in the **Default Senders** view.
9. Verify the record does **not** appear in the **Receive-Enabled Accounts** view (Receive Enabled is No).

---

## Notes

- The `sprk_sendenableds` logical name has a trailing **s** -- this is intentional and matches the existing schema. Do not confuse with `sprk_sendenabled` (which does not exist).
- Graph Integration and Verification fields are managed by background processes and should not be manually edited. They are marked read-only on the form to prevent accidental changes.
- Only one account should have `sprk_isdefaultsender` set to `Yes` at any time. If multiple defaults exist, the system will use the first one found alphabetically, which may cause unpredictable behavior.
- Receive functionality (Phase 8) will require enabling `sprk_receiveenabled` on the seed record and configuring a Graph subscription via the BFF API.

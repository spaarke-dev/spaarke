# Dataverse Entity Creation Guide

Follow these steps to create the required entities for Custom API Proxy in spaarkedev1.

---

## Step 1: Navigate to Power Apps Maker Portal

1. Open browser and go to: **https://make.powerapps.com**
2. Sign in with your credentials
3. Select environment: **SPAARKE DEV 1 (spaarkedev1)**
4. Click **Tables** in the left navigation (under Dataverse)

---

## Step 2: Create External Service Configuration Entity

### Basic Information

Click **+ New table** → **Create new table**

- **Display name**: `External Service Configuration`
- **Plural name**: `External Service Configurations`
- **Description**: `Configuration for external APIs accessed via Custom API Proxy`
- **Enable attachments**: No
- **Primary column**:
  - **Display name**: `Name`
  - **Schema name**: `sprk_name`
  - **Data type**: Single line of text
  - **Max length**: 100

Click **Save**

### Add Columns

After saving, click **+ New** → **Column** to add each column:

#### 1. Display Name
- **Display name**: `Display Name`
- **Schema name**: `sprk_displayname`
- **Data type**: Single line of text
- **Max length**: 200
- **Required**: No
- Click **Save**

#### 2. Base URL
- **Display name**: `Base URL`
- **Schema name**: `sprk_baseurl`
- **Data type**: Single line of text
- **Max length**: 500
- **Required**: Business required
- **Description**: `Base URL of the external API`
- Click **Save**

#### 3. Description
- **Display name**: `Description`
- **Schema name**: `sprk_description`
- **Data type**: Multiple lines of text
- **Max length**: 2000
- **Required**: No
- Click **Save**

#### 4. Authentication Type
- **Display name**: `Authentication Type`
- **Schema name**: `sprk_authtype`
- **Data type**: Choice
- **Required**: Business required
- **Choices**:
  - `0` - `None`
  - `1` - `Client Credentials`
  - `2` - `Managed Identity`
  - `3` - `API Key`
- **Default value**: `0` (None)
- Click **Save**

#### 5. Tenant ID
- **Display name**: `Tenant ID`
- **Schema name**: `sprk_tenantid`
- **Data type**: Single line of text
- **Max length**: 100
- **Required**: No
- Click **Save**

#### 6. Client ID
- **Display name**: `Client ID`
- **Schema name**: `sprk_clientid`
- **Data type**: Single line of text
- **Max length**: 100
- **Required**: No
- Click **Save**

#### 7. Client Secret (SECURED)
- **Display name**: `Client Secret`
- **Schema name**: `sprk_clientsecret`
- **Data type**: Single line of text
- **Max length**: 500
- **Required**: No
- **Enable column security**: Yes ✓ (IMPORTANT!)
- Click **Save**

#### 8. Scope
- **Display name**: `Scope`
- **Schema name**: `sprk_scope`
- **Data type**: Single line of text
- **Max length**: 200
- **Required**: No
- **Description**: `OAuth scope (e.g., api://app-id/.default)`
- Click **Save**

#### 9. API Key (SECURED)
- **Display name**: `API Key`
- **Schema name**: `sprk_apikey`
- **Data type**: Single line of text
- **Max length**: 500
- **Required**: No
- **Enable column security**: Yes ✓ (IMPORTANT!)
- Click **Save**

#### 10. API Key Header
- **Display name**: `API Key Header`
- **Schema name**: `sprk_apikeyheader`
- **Data type**: Single line of text
- **Max length**: 100
- **Required**: No
- **Description**: `HTTP header name for API key (e.g., X-API-Key)`
- Click **Save**

#### 11. Timeout
- **Display name**: `Timeout (seconds)`
- **Schema name**: `sprk_timeout`
- **Data type**: Whole number
- **Minimum value**: 1
- **Maximum value**: 600
- **Required**: No
- Click **Save**

#### 12. Retry Count
- **Display name**: `Retry Count`
- **Schema name**: `sprk_retrycount`
- **Data type**: Whole number
- **Minimum value**: 0
- **Maximum value**: 10
- **Required**: No
- Click **Save**

#### 13. Retry Delay
- **Display name**: `Retry Delay (ms)`
- **Schema name**: `sprk_retrydelay`
- **Data type**: Whole number
- **Minimum value**: 0
- **Maximum value**: 30000
- **Required**: No
- Click **Save**

#### 14. Is Enabled
- **Display name**: `Is Enabled`
- **Schema name**: `sprk_isenabled`
- **Data type**: Yes/No
- **Default value**: Yes
- **Required**: No
- Click **Save**

#### 15. Health Status
- **Display name**: `Health Status`
- **Schema name**: `sprk_healthstatus`
- **Data type**: Choice
- **Required**: No
- **Choices**:
  - `0` - `Healthy`
  - `1` - `Degraded`
  - `2` - `Unhealthy`
- Click **Save**

#### 16. Last Health Check
- **Display name**: `Last Health Check`
- **Schema name**: `sprk_lasthealthcheck`
- **Data type**: Date and Time
- **Format**: Date and Time
- **Behavior**: User Local
- **Required**: No
- Click **Save**

#### 17. Error Count
- **Display name**: `Error Count`
- **Schema name**: `sprk_errorcount`
- **Data type**: Whole number
- **Minimum value**: 0
- **Required**: No
- Click **Save**

### Enable Auditing

1. Click **Settings** (gear icon) in the table
2. Expand **Advanced options**
3. Enable **Audit changes to its data**
4. Click **Save**

---

## Step 3: Create Proxy Audit Log Entity

Click **+ New table** → **Create new table**

### Basic Information

- **Display name**: `Proxy Audit Log`
- **Plural name**: `Proxy Audit Logs`
- **Description**: `Audit log for Custom API Proxy operations`
- **Enable attachments**: No
- **Primary column**:
  - **Display name**: `Correlation ID`
  - **Schema name**: `sprk_correlationid`
  - **Data type**: Single line of text
  - **Max length**: 100

Click **Save**

### Add Columns

#### 1. Operation
- **Display name**: `Operation`
- **Schema name**: `sprk_operation`
- **Data type**: Single line of text
- **Max length**: 100
- **Required**: No
- Click **Save**

#### 2. Service Name
- **Display name**: `Service Name`
- **Schema name**: `sprk_servicename`
- **Data type**: Single line of text
- **Max length**: 100
- **Required**: No
- Click **Save**

#### 3. Execution Time
- **Display name**: `Execution Time`
- **Schema name**: `sprk_executiontime`
- **Data type**: Date and Time
- **Format**: Date and Time
- **Behavior**: User Local
- **Required**: No
- Click **Save**

#### 4. User ID
- **Display name**: `User`
- **Schema name**: `sprk_userid`
- **Data type**: Lookup
- **Target table**: User (systemuser)
- **Required**: No
- Click **Save**

#### 5. Request Payload
- **Display name**: `Request Payload`
- **Schema name**: `sprk_requestpayload`
- **Data type**: Multiple lines of text
- **Max length**: 10000
- **Required**: No
- Click **Save**

#### 6. Request Size
- **Display name**: `Request Size (bytes)`
- **Schema name**: `sprk_requestsize`
- **Data type**: Whole number
- **Required**: No
- Click **Save**

#### 7. Status Code
- **Display name**: `Status Code`
- **Schema name**: `sprk_statuscode`
- **Data type**: Whole number
- **Required**: No
- Click **Save**

#### 8. Response Payload
- **Display name**: `Response Payload`
- **Schema name**: `sprk_responsepayload`
- **Data type**: Multiple lines of text
- **Max length**: 10000
- **Required**: No
- Click **Save**

#### 9. Response Size
- **Display name**: `Response Size (bytes)`
- **Schema name**: `sprk_responsesize`
- **Data type**: Whole number
- **Required**: No
- Click **Save**

#### 10. Duration
- **Display name**: `Duration (ms)`
- **Schema name**: `sprk_duration`
- **Data type**: Whole number
- **Required**: No
- Click **Save**

#### 11. Success
- **Display name**: `Success`
- **Schema name**: `sprk_success`
- **Data type**: Yes/No
- **Required**: No
- Click **Save**

#### 12. Error Message
- **Display name**: `Error Message`
- **Schema name**: `sprk_errormessage`
- **Data type**: Multiple lines of text
- **Max length**: 5000
- **Required**: No
- Click **Save**

#### 13. Client IP
- **Display name**: `Client IP`
- **Schema name**: `sprk_clientip`
- **Data type**: Single line of text
- **Max length**: 50
- **Required**: No
- Click **Save**

#### 14. User Agent
- **Display name**: `User Agent`
- **Schema name**: `sprk_useragent`
- **Data type**: Single line of text
- **Max length**: 500
- **Required**: No
- Click **Save**

### Configure Security

1. Click **Settings** (gear icon) in the table
2. Under **Security**, set:
   - **Enable for this table**: Yes
   - **Read privilege**: Organization (only plugins can read)
   - **Create/Update/Delete**: Organization (only plugins can write)

### Enable Auditing

1. In table settings
2. Expand **Advanced options**
3. Enable **Audit changes to its data**
4. Click **Save**

---

## Step 4: Verify Entity Creation

1. Go back to **Tables** list
2. Verify both tables appear:
   - ✓ External Service Configuration (sprk_externalserviceconfig)
   - ✓ Proxy Audit Log (sprk_proxyauditlog)
3. Click each table and verify all columns are created

---

## Step 5: Test Entity Access

### Create Test Record for External Service Configuration

1. Open **External Service Configuration** table
2. Click **+ New**
3. Fill in test data:
   - **Name**: TestService
   - **Display Name**: Test Service
   - **Base URL**: https://httpbin.org
   - **Authentication Type**: None
   - **Is Enabled**: Yes
4. Click **Save & Close**
5. Verify record is created successfully

---

## Completion Checklist

- [ ] External Service Configuration entity created with 17 columns
- [ ] Client Secret and API Key columns have security enabled
- [ ] Proxy Audit Log entity created with 14 columns
- [ ] Both entities have auditing enabled
- [ ] Test record created successfully
- [ ] No errors in entity creation

---

## Notes

- **Secured Fields**: `sprk_clientsecret` and `sprk_apikey` MUST have column security enabled to encrypt data at rest
- **Audit Logs**: Both entities should have auditing enabled for compliance
- **Security Roles**: By default, System Administrator has full access. Adjust security roles as needed.

---

## Next Steps

After entities are created:
1. Proceed to Phase 3: Implement operation-specific plugins
2. Create Custom API definitions
3. Register plugins in Dataverse

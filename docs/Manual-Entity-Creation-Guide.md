# Manual Dataverse Entity Creation Guide

## Prerequisites
- Access to Power Platform admin center or maker portal
- System Administrator or System Customizer role in Dataverse
- Environment: `https://spaarkedev1.crm.dynamics.com`

## Step 1: Create Container Entity

1. **Navigate to Power Platform Maker Portal**
   - Go to `https://make.powerapps.com`
   - Select your environment: `spaarkedev1`

2. **Create sprk_container Entity**
   - Go to **Data** > **Tables**
   - Click **+ New table**
   - Set **Display name**: `Spaarke Container`
   - Set **Plural name**: `Spaarke Containers`
   - **Primary column**: Rename to `Container Name` (logical: `sprk_name`)
   - Click **Save**

3. **Add Container Fields**

   | Display Name | Logical Name | Type | Required | Max Length | Description |
   |-------------|--------------|------|----------|------------|-------------|
   | SPE Container ID | sprk_specontainerid | Text | Yes | 1000 | SharePoint Embedded Container ID |
   | Document Count | sprk_documentcount | Number | No | - | Number of documents (default: 0) |
   | Drive ID | sprk_driveid | Text | No | 1000 | SharePoint Embedded Drive ID |

## Step 2: Create Document Entity

1. **Create sprk_document Entity**
   - Click **+ New table**
   - Set **Display name**: `Spaarke Document`
   - Set **Plural name**: `Spaarke Documents`
   - **Primary column**: Rename to `Document Name` (logical: `sprk_name`)
   - Click **Save**

2. **Add Document Fields**

   | Display Name | Logical Name | Type | Required | Max Length | Description |
   |-------------|--------------|------|----------|------------|-------------|
   | Container | sprk_containerid | Lookup | Yes | - | Lookup to sprk_container |
   | Has File | sprk_hasfile | Yes/No | No | - | Default: No |
   | File Name | sprk_filename | Text | No | 255 | Name of file in storage |
   | File Size | sprk_filesize | Number | No | - | File size in bytes |
   | MIME Type | sprk_mimetype | Text | No | 100 | File MIME type |
   | Graph Item ID | sprk_graphitemid | Text | No | 1000 | SPE Graph Item ID |
   | Graph Drive ID | sprk_graphdriveid | Text | No | 1000 | SPE Graph Drive ID |

3. **Create Status Choice Field**
   - Add new column: **Status** (logical: `sprk_status`)
   - Type: **Choice**
   - Create new choice with these options:
     - Draft (421500001) - Blue (#0078D4)
     - Processing (421500002) - Orange (#FF8C00)
     - Active (421500003) - Green (#107C10)
     - Error (421500004) - Red (#D13438)

## Step 3: Configure Relationships

1. **Container to Documents (1:N)**
   - In sprk_container entity
   - Go to **Relationships**
   - Verify the relationship was created when you added the lookup field
   - Name should be: `sprk_container_sprk_document`

## Step 4: Security Roles

1. **Create Security Roles**
   - Go to **Settings** > **Security** > **Security Roles**
   - Create these roles:

   **Spaarke Document User**
   - sprk_document: Create, Read (User), Write (User), Delete (User)
   - sprk_container: Read (Business Unit)

   **Spaarke Document Manager**
   - sprk_document: Create, Read (Business Unit), Write (Business Unit), Delete (Business Unit)
   - sprk_container: Read (Business Unit)

   **Spaarke Container Admin**
   - sprk_document: Create, Read (Organization), Write (Organization), Delete (Organization)
   - sprk_container: Create, Read (Organization), Write (Organization), Delete (Organization)

## Step 5: Forms and Views

1. **Configure Main Forms**
   - For sprk_document:
     - Add all fields in logical groups
     - Hide Graph IDs from users (Admin view only)

   - For sprk_container:
     - Simple form with name and document count

2. **Configure Views**
   - Create views for different scenarios
   - Add proper filtering and sorting

## Step 6: Validation

1. **Test Entity Creation**
   ```bash
   # Navigate to your entities in maker portal
   # Create test records
   # Verify relationships work
   ```

2. **Test API Connection**
   ```bash
   # Once API is running:
   curl -X GET "https://localhost:7001/healthz/dataverse"
   ```

## Expected Results

After completing these steps:
- ✅ sprk_container entity exists with all fields
- ✅ sprk_document entity exists with all fields
- ✅ Lookup relationship between entities works
- ✅ Security roles configured
- ✅ API can connect and query entities

## Troubleshooting

**Common Issues:**
1. **Permission Denied**: Ensure you have System Administrator role
2. **Field Not Found**: Check field logical names match exactly
3. **Relationship Issues**: Verify lookup field was created correctly

**Next Steps:**
1. Run the API health check
2. Test CRUD operations through API
3. Proceed to Task 1.3 (API Endpoints)
# Create Dataverse columns for Office Integration tables
# Reference: projects/sdap-office-integration/notes/DATAVERSE-TABLE-SCHEMAS.md

param(
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com",
    [string]$SolutionName = "Office Add In"
)

$ErrorActionPreference = "Stop"

Write-Host "🔧 Creating Dataverse columns for Office Integration..." -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl" -ForegroundColor Gray
Write-Host "Solution: $SolutionName" -ForegroundColor Gray
Write-Host ""

# Get access token using Azure CLI
Write-Host "🔐 Authenticating..." -ForegroundColor Yellow
$token = az account get-access-token --resource "$EnvironmentUrl" --query accessToken -o tsv
if (-not $token) {
    throw "Failed to get access token"
}

$headers = @{
    "Authorization" = "Bearer $token"
    "Content-Type" = "application/json"
    "Accept" = "application/json"
    "OData-MaxVersion" = "4.0"
    "OData-Version" = "4.0"
}

$apiUrl = "$EnvironmentUrl/api/data/v9.2"

# Helper function to create a column
function New-DataverseColumn {
    param(
        [string]$TableLogicalName,
        [hashtable]$AttributeMetadata
    )

    try {
        $body = $AttributeMetadata | ConvertTo-Json -Depth 10
        $response = Invoke-RestMethod -Uri "$apiUrl/EntityDefinitions(LogicalName='$TableLogicalName')/Attributes" `
            -Method Post `
            -Headers $headers `
            -Body $body

        Write-Host "  ✅ Created: $($AttributeMetadata.SchemaName)" -ForegroundColor Green
        return $true
    }
    catch {
        $errorDetails = $_.ErrorDetails.Message | ConvertFrom-Json
        Write-Host "  ❌ Failed: $($AttributeMetadata.SchemaName) - $($errorDetails.error.message)" -ForegroundColor Red
        return $false
    }
}

# Helper function to create a lookup column
function New-DataverseLookup {
    param(
        [string]$TableLogicalName,
        [string]$SchemaName,
        [string]$DisplayName,
        [string]$TargetTable,
        [string]$Description = ""
    )

    $metadata = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.LookupAttributeMetadata"
        "SchemaName" = $SchemaName
        "DisplayName" = @{
            "@odata.type" = "Microsoft.Dynamics.CRM.Label"
            "LocalizedLabels" = @(
                @{
                    "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                    "Label" = $DisplayName
                    "LanguageCode" = 1033
                }
            )
        }
        "Description" = @{
            "@odata.type" = "Microsoft.Dynamics.CRM.Label"
            "LocalizedLabels" = @(
                @{
                    "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                    "Label" = $Description
                    "LanguageCode" = 1033
                }
            )
        }
        "RequiredLevel" = @{
            "Value" = "None"
        }
        "Targets" = @($TargetTable)
    }

    return New-DataverseColumn -TableLogicalName $TableLogicalName -AttributeMetadata $metadata
}

# Helper function to create a string column
function New-DataverseString {
    param(
        [string]$TableLogicalName,
        [string]$SchemaName,
        [string]$DisplayName,
        [int]$MaxLength,
        [bool]$Required = $false,
        [bool]$Searchable = $true,
        [string]$Description = ""
    )

    $metadata = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "SchemaName" = $SchemaName
        "DisplayName" = @{
            "@odata.type" = "Microsoft.Dynamics.CRM.Label"
            "LocalizedLabels" = @(
                @{
                    "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                    "Label" = $DisplayName
                    "LanguageCode" = 1033
                }
            )
        }
        "Description" = @{
            "@odata.type" = "Microsoft.Dynamics.CRM.Label"
            "LocalizedLabels" = @(
                @{
                    "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                    "Label" = $Description
                    "LanguageCode" = 1033
                }
            )
        }
        "RequiredLevel" = @{
            "Value" = if ($Required) { "ApplicationRequired" } else { "None" }
        }
        "MaxLength" = $MaxLength
        "FormatName" = @{
            "Value" = "Text"
        }
        "IsLocalizable" = $false
    }

    return New-DataverseColumn -TableLogicalName $TableLogicalName -AttributeMetadata $metadata
}

# Helper function to create a memo (multiline text) column
function New-DataverseMemo {
    param(
        [string]$TableLogicalName,
        [string]$SchemaName,
        [string]$DisplayName,
        [int]$MaxLength,
        [bool]$Required = $false,
        [string]$Description = ""
    )

    $metadata = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
        "SchemaName" = $SchemaName
        "DisplayName" = @{
            "@odata.type" = "Microsoft.Dynamics.CRM.Label"
            "LocalizedLabels" = @(
                @{
                    "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                    "Label" = $DisplayName
                    "LanguageCode" = 1033
                }
            )
        }
        "Description" = @{
            "@odata.type" = "Microsoft.Dynamics.CRM.Label"
            "LocalizedLabels" = @(
                @{
                    "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                    "Label" = $Description
                    "LanguageCode" = 1033
                }
            )
        }
        "RequiredLevel" = @{
            "Value" = if ($Required) { "ApplicationRequired" } else { "None" }
        }
        "MaxLength" = $MaxLength
        "Format" = "Text"
    }

    return New-DataverseColumn -TableLogicalName $TableLogicalName -AttributeMetadata $metadata
}

# Helper function to create a datetime column
function New-DataverseDateTime {
    param(
        [string]$TableLogicalName,
        [string]$SchemaName,
        [string]$DisplayName,
        [bool]$Required = $false,
        [string]$Description = ""
    )

    $metadata = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata"
        "SchemaName" = $SchemaName
        "DisplayName" = @{
            "@odata.type" = "Microsoft.Dynamics.CRM.Label"
            "LocalizedLabels" = @(
                @{
                    "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                    "Label" = $DisplayName
                    "LanguageCode" = 1033
                }
            )
        }
        "Description" = @{
            "@odata.type" = "Microsoft.Dynamics.CRM.Label"
            "LocalizedLabels" = @(
                @{
                    "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                    "Label" = $Description
                    "LanguageCode" = 1033
                }
            )
        }
        "RequiredLevel" = @{
            "Value" = if ($Required) { "ApplicationRequired" } else { "None" }
        }
        "Format" = "DateAndTime"
        "DateTimeBehavior" = @{
            "Value" = "UserLocal"
        }
    }

    return New-DataverseColumn -TableLogicalName $TableLogicalName -AttributeMetadata $metadata
}

# Helper function to create a boolean column
function New-DataverseBoolean {
    param(
        [string]$TableLogicalName,
        [string]$SchemaName,
        [string]$DisplayName,
        [bool]$Required = $false,
        [string]$Description = ""
    )

    $metadata = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.BooleanAttributeMetadata"
        "SchemaName" = $SchemaName
        "DisplayName" = @{
            "@odata.type" = "Microsoft.Dynamics.CRM.Label"
            "LocalizedLabels" = @(
                @{
                    "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                    "Label" = $DisplayName
                    "LanguageCode" = 1033
                }
            )
        }
        "Description" = @{
            "@odata.type" = "Microsoft.Dynamics.CRM.Label"
            "LocalizedLabels" = @(
                @{
                    "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                    "Label" = $Description
                    "LanguageCode" = 1033
                }
            )
        }
        "RequiredLevel" = @{
            "Value" = if ($Required) { "ApplicationRequired" } else { "None" }
        }
        "OptionSet" = @{
            "@odata.type" = "Microsoft.Dynamics.CRM.BooleanOptionSetMetadata"
            "TrueOption" = @{
                "Value" = 1
                "Label" = @{
                    "@odata.type" = "Microsoft.Dynamics.CRM.Label"
                    "LocalizedLabels" = @(
                        @{
                            "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                            "Label" = "Yes"
                            "LanguageCode" = 1033
                        }
                    )
                }
            }
            "FalseOption" = @{
                "Value" = 0
                "Label" = @{
                    "@odata.type" = "Microsoft.Dynamics.CRM.Label"
                    "LocalizedLabels" = @(
                        @{
                            "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                            "Label" = "No"
                            "LanguageCode" = 1033
                        }
                    )
                }
            }
        }
    }

    return New-DataverseColumn -TableLogicalName $TableLogicalName -AttributeMetadata $metadata
}

# Helper function to create an integer column
function New-DataverseInteger {
    param(
        [string]$TableLogicalName,
        [string]$SchemaName,
        [string]$DisplayName,
        [bool]$Required = $false,
        [string]$Description = "",
        [int]$MinValue = 0,
        [int]$MaxValue = 2147483647
    )

    $metadata = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.IntegerAttributeMetadata"
        "SchemaName" = $SchemaName
        "DisplayName" = @{
            "@odata.type" = "Microsoft.Dynamics.CRM.Label"
            "LocalizedLabels" = @(
                @{
                    "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                    "Label" = $DisplayName
                    "LanguageCode" = 1033
                }
            )
        }
        "Description" = @{
            "@odata.type" = "Microsoft.Dynamics.CRM.Label"
            "LocalizedLabels" = @(
                @{
                    "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                    "Label" = $Description
                    "LanguageCode" = 1033
                }
            )
        }
        "RequiredLevel" = @{
            "Value" = if ($Required) { "ApplicationRequired" } else { "None" }
        }
        "MinValue" = $MinValue
        "MaxValue" = $MaxValue
        "Format" = "None"
    }

    return New-DataverseColumn -TableLogicalName $TableLogicalName -AttributeMetadata $metadata
}

# Helper function to create a choice (picklist) column
function New-DataverseChoice {
    param(
        [string]$TableLogicalName,
        [string]$SchemaName,
        [string]$DisplayName,
        [hashtable[]]$Options,
        [bool]$Required = $false,
        [string]$Description = ""
    )

    $optionSetOptions = @()
    foreach ($opt in $Options) {
        $optionSetOptions += @{
            "Value" = $opt.Value
            "Label" = @{
                "@odata.type" = "Microsoft.Dynamics.CRM.Label"
                "LocalizedLabels" = @(
                    @{
                        "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                        "Label" = $opt.Label
                        "LanguageCode" = 1033
                    }
                )
            }
        }
    }

    $metadata = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
        "SchemaName" = $SchemaName
        "DisplayName" = @{
            "@odata.type" = "Microsoft.Dynamics.CRM.Label"
            "LocalizedLabels" = @(
                @{
                    "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                    "Label" = $DisplayName
                    "LanguageCode" = 1033
                }
            )
        }
        "Description" = @{
            "@odata.type" = "Microsoft.Dynamics.CRM.Label"
            "LocalizedLabels" = @(
                @{
                    "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                    "Label" = $Description
                    "LanguageCode" = 1033
                }
            )
        }
        "RequiredLevel" = @{
            "Value" = if ($Required) { "ApplicationRequired" } else { "None" }
        }
        "OptionSet" = @{
            "@odata.type" = "Microsoft.Dynamics.CRM.OptionSetMetadata"
            "IsGlobal" = $false
            "OptionSetType" = "Picklist"
            "Options" = $optionSetOptions
        }
    }

    return New-DataverseColumn -TableLogicalName $TableLogicalName -AttributeMetadata $metadata
}

# ====================================================================================
# TABLE 1: EmailArtifact (sprk_emailartifact)
# ====================================================================================

Write-Host "📧 Creating EmailArtifact columns..." -ForegroundColor Cyan

New-DataverseString -TableLogicalName "sprk_emailartifact" -SchemaName "sprk_subject" -DisplayName "Subject" -MaxLength 400 -Searchable $true -Description "Email subject line"
New-DataverseString -TableLogicalName "sprk_emailartifact" -SchemaName "sprk_sender" -DisplayName "Sender" -MaxLength 320 -Searchable $true -Description "Email address of sender"
New-DataverseMemo -TableLogicalName "sprk_emailartifact" -SchemaName "sprk_recipients" -DisplayName "Recipients" -MaxLength 10000 -Description "JSON array of recipient objects"
New-DataverseMemo -TableLogicalName "sprk_emailartifact" -SchemaName "sprk_ccrecipients" -DisplayName "CC Recipients" -MaxLength 10000 -Description "JSON array of CC recipient objects"
New-DataverseDateTime -TableLogicalName "sprk_emailartifact" -SchemaName "sprk_sentdate" -DisplayName "Sent Date" -Description "When email was sent"
New-DataverseDateTime -TableLogicalName "sprk_emailartifact" -SchemaName "sprk_receiveddate" -DisplayName "Received Date" -Description "When email was received"
New-DataverseString -TableLogicalName "sprk_emailartifact" -SchemaName "sprk_messageid" -DisplayName "Message ID" -MaxLength 256 -Searchable $false -Description "Internet message ID from headers"
New-DataverseString -TableLogicalName "sprk_emailartifact" -SchemaName "sprk_internetheadershash" -DisplayName "Headers Hash" -MaxLength 64 -Searchable $false -Description "SHA256 hash for duplicate detection"
New-DataverseString -TableLogicalName "sprk_emailartifact" -SchemaName "sprk_conversationid" -DisplayName "Conversation ID" -MaxLength 256 -Searchable $false -Description "Email conversation/thread ID"

# Create Importance choice field
$importanceOptions = @(
    @{ Value = 0; Label = "Low" },
    @{ Value = 1; Label = "Normal" },
    @{ Value = 2; Label = "High" }
)
New-DataverseChoice -TableLogicalName "sprk_emailartifact" -SchemaName "sprk_importance" -DisplayName "Importance" -Options $importanceOptions -Description "Email importance level"

New-DataverseBoolean -TableLogicalName "sprk_emailartifact" -SchemaName "sprk_hasattachments" -DisplayName "Has Attachments" -Description "Boolean flag"
New-DataverseMemo -TableLogicalName "sprk_emailartifact" -SchemaName "sprk_bodypreview" -DisplayName "Body Preview" -MaxLength 2000 -Description "First 2000 chars of email body"
New-DataverseLookup -TableLogicalName "sprk_emailartifact" -SchemaName "sprk_document" -DisplayName "Document" -TargetTable "sprk_document" -Description "Lookup to Document"

Write-Host ""

# ====================================================================================
# TABLE 2: AttachmentArtifact (sprk_attachmentartifact)
# ====================================================================================

Write-Host "📎 Creating AttachmentArtifact columns..." -ForegroundColor Cyan

New-DataverseString -TableLogicalName "sprk_attachmentartifact" -SchemaName "sprk_originalfilename" -DisplayName "Original Filename" -MaxLength 260 -Searchable $true -Description "Filename from email"
New-DataverseString -TableLogicalName "sprk_attachmentartifact" -SchemaName "sprk_contenttype" -DisplayName "Content Type" -MaxLength 100 -Searchable $false -Description "MIME type (e.g., application/pdf)"
New-DataverseInteger -TableLogicalName "sprk_attachmentartifact" -SchemaName "sprk_size" -DisplayName "Size" -Description "File size in bytes"
New-DataverseString -TableLogicalName "sprk_attachmentartifact" -SchemaName "sprk_contentid" -DisplayName "Content ID" -MaxLength 256 -Searchable $false -Description "For inline attachments (embedded images)"
New-DataverseBoolean -TableLogicalName "sprk_attachmentartifact" -SchemaName "sprk_isinline" -DisplayName "Is Inline" -Description "True for embedded images in HTML"
New-DataverseLookup -TableLogicalName "sprk_attachmentartifact" -SchemaName "sprk_emailartifact" -DisplayName "Email Artifact" -TargetTable "sprk_emailartifact" -Description "Lookup to EmailArtifact"
New-DataverseLookup -TableLogicalName "sprk_attachmentartifact" -SchemaName "sprk_document" -DisplayName "Document" -TargetTable "sprk_document" -Description "Lookup to Document"

Write-Host ""

# ====================================================================================
# TABLE 3: ProcessingJob (sprk_processingjob)
# ====================================================================================

Write-Host "⚙️ Creating ProcessingJob columns..." -ForegroundColor Cyan

# Create JobType choice field
$jobTypeOptions = @(
    @{ Value = 0; Label = "Document Save" },
    @{ Value = 1; Label = "Email Save" },
    @{ Value = 2; Label = "Share Links" },
    @{ Value = 3; Label = "Quick Create" },
    @{ Value = 4; Label = "Profile Summary" },
    @{ Value = 5; Label = "Indexing" },
    @{ Value = 6; Label = "Deep Analysis" }
)
New-DataverseChoice -TableLogicalName "sprk_processingjob" -SchemaName "sprk_jobtype" -DisplayName "Job Type" -Options $jobTypeOptions -Description "Type of processing job"

# Create Status choice field
$statusOptions = @(
    @{ Value = 0; Label = "Pending" },
    @{ Value = 1; Label = "In Progress" },
    @{ Value = 2; Label = "Completed" },
    @{ Value = 3; Label = "Failed" },
    @{ Value = 4; Label = "Cancelled" }
)
New-DataverseChoice -TableLogicalName "sprk_processingjob" -SchemaName "sprk_status" -DisplayName "Status" -Options $statusOptions -Required $true -Description "Job status"

New-DataverseMemo -TableLogicalName "sprk_processingjob" -SchemaName "sprk_stages" -DisplayName "Stages" -MaxLength 10000 -Description "JSON array of stage definitions"
New-DataverseString -TableLogicalName "sprk_processingjob" -SchemaName "sprk_currentstage" -DisplayName "Current Stage" -MaxLength 100 -Searchable $false -Description "Name of currently executing stage"
New-DataverseMemo -TableLogicalName "sprk_processingjob" -SchemaName "sprk_stagestatus" -DisplayName "Stage Status" -MaxLength 10000 -Description "JSON object tracking each stage's status"
New-DataverseInteger -TableLogicalName "sprk_processingjob" -SchemaName "sprk_progress" -DisplayName "Progress" -MinValue 0 -MaxValue 100 -Description "0-100 percentage"
New-DataverseDateTime -TableLogicalName "sprk_processingjob" -SchemaName "sprk_starteddate" -DisplayName "Started Date" -Description "When job began processing"
New-DataverseDateTime -TableLogicalName "sprk_processingjob" -SchemaName "sprk_completeddate" -DisplayName "Completed Date" -Description "When job finished (success or failure)"
New-DataverseString -TableLogicalName "sprk_processingjob" -SchemaName "sprk_errorcode" -DisplayName "Error Code" -MaxLength 50 -Searchable $false -Description "Error code if failed (e.g., OFFICE_001)"
New-DataverseMemo -TableLogicalName "sprk_processingjob" -SchemaName "sprk_errormessage" -DisplayName "Error Message" -MaxLength 2000 -Description "Detailed error message"
New-DataverseInteger -TableLogicalName "sprk_processingjob" -SchemaName "sprk_retrycount" -DisplayName "Retry Count" -Description "Number of retry attempts"
New-DataverseString -TableLogicalName "sprk_processingjob" -SchemaName "sprk_idempotencykey" -DisplayName "Idempotency Key" -MaxLength 64 -Searchable $false -Description "SHA256 hash for duplicate prevention"
New-DataverseString -TableLogicalName "sprk_processingjob" -SchemaName "sprk_correlationid" -DisplayName "Correlation ID" -MaxLength 36 -Searchable $false -Description "GUID for distributed tracing"
New-DataverseLookup -TableLogicalName "sprk_processingjob" -SchemaName "sprk_initiatedby" -DisplayName "Initiated By" -TargetTable "systemuser" -Description "Lookup to User"
New-DataverseLookup -TableLogicalName "sprk_processingjob" -SchemaName "sprk_document" -DisplayName "Document" -TargetTable "sprk_document" -Description "Lookup to Document"
New-DataverseMemo -TableLogicalName "sprk_processingjob" -SchemaName "sprk_payload" -DisplayName "Payload" -MaxLength 50000 -Description "JSON input data for the job"
New-DataverseMemo -TableLogicalName "sprk_processingjob" -SchemaName "sprk_result" -DisplayName "Result" -MaxLength 50000 -Description "JSON output data from the job"

Write-Host ""
Write-Host "✅ Column creation complete!" -ForegroundColor Green
Write-Host ""
Write-Host "⚠️ Manual steps still required:" -ForegroundColor Yellow
Write-Host "  1. Create indexes on EmailArtifact: MessageId, HeadersHash" -ForegroundColor Gray
Write-Host "  2. Create indexes on ProcessingJob: IdempotencyKey, Status" -ForegroundColor Gray
Write-Host "  3. Configure security roles for the 3 tables" -ForegroundColor Gray
Write-Host "  4. Verify all tables are in the 'Office Add In' solution" -ForegroundColor Gray

<#
.SYNOPSIS
    Import seed data for AI Document Intelligence analysis entities.
.DESCRIPTION
    Creates default Actions, Skills, Knowledge Deployments, and Knowledge Sources
    in Dataverse using the Web API. Requires an authenticated PAC CLI connection.
.NOTES
    Run with: pwsh -File Import-SeedData.ps1
#>

param(
    [string]$Environment = "https://spaarkedev1.crm.dynamics.com"
)

$ErrorActionPreference = "Stop"

# Get access token from PAC CLI
Write-Host "Getting access token from PAC CLI..." -ForegroundColor Cyan

# Run pac auth token and capture output to file
$tempFile = [System.IO.Path]::GetTempFileName()
Start-Process -FilePath "pac" -ArgumentList "auth", "token" -NoNewWindow -Wait -RedirectStandardOutput $tempFile
$tokenOutput = Get-Content $tempFile -Raw
Remove-Item $tempFile -Force

# Extract token from output (last non-empty line that looks like a token)
$lines = $tokenOutput -split "`n" | Where-Object { $_.Trim() -and $_ -notmatch "^Microsoft|^Version|^Online|^Feedback|^Connected" }
$token = ($lines | Select-Object -Last 1).Trim()

if (-not $token -or $token.Length -lt 100) {
    Write-Error "Failed to get access token. Ensure PAC CLI is authenticated. Output: $tokenOutput"
    exit 1
}

Write-Host "Token acquired successfully" -ForegroundColor Green

$headers = @{
    "Authorization" = "Bearer $token"
    "Content-Type" = "application/json"
    "OData-MaxVersion" = "4.0"
    "OData-Version" = "4.0"
    "Prefer" = "return=representation"
}

$baseUrl = "$Environment/api/data/v9.2"

function New-DataverseRecord {
    param(
        [string]$EntitySet,
        [hashtable]$Data
    )

    $url = "$baseUrl/$EntitySet"
    $body = $Data | ConvertTo-Json -Depth 10

    try {
        $response = Invoke-RestMethod -Uri $url -Method Post -Headers $headers -Body $body
        Write-Host "  Created: $($Data.sprk_name)" -ForegroundColor Green
        return $response
    }
    catch {
        if ($_.Exception.Response.StatusCode -eq 409) {
            Write-Host "  Skipped (exists): $($Data.sprk_name)" -ForegroundColor Yellow
        } else {
            Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
            throw
        }
    }
}

# ============================================
# 1. CREATE ANALYSIS ACTIONS (5 records)
# ============================================
Write-Host "`n=== Creating Analysis Actions ===" -ForegroundColor Cyan

$actions = @(
    @{
        sprk_name = "Summarize Document"
        sprk_description = "Generate a concise summary of the document's key points, main arguments, and conclusions."
        sprk_systemprompt = @"
You are a professional document analyst. Your task is to create a clear, concise summary of the provided document.

## Instructions
1. Read the document carefully
2. Identify the main purpose, key points, and conclusions
3. Create a structured summary with:
   - Executive Summary (2-3 sentences)
   - Key Points (bullet list)
   - Conclusions/Recommendations
4. Keep the summary professional and objective

## Output Format
Use markdown formatting. Be concise but comprehensive.
"@
        sprk_sortorder = 10
    },
    @{
        sprk_name = "Review Agreement"
        sprk_description = "Analyze a legal agreement for key terms, obligations, risks, and notable clauses."
        sprk_systemprompt = @"
You are a legal document analyst. Your task is to review the provided agreement and extract key information.

## Instructions
1. Identify the parties and effective date
2. Extract key terms and obligations for each party
3. Highlight important clauses (termination, liability, indemnification, etc.)
4. Note any unusual or potentially risky provisions
5. Summarize payment terms if applicable

## Output Format
Use markdown with clear sections:
- **Parties & Effective Date**
- **Key Terms**
- **Obligations by Party**
- **Notable Clauses**
- **Risk Assessment**
- **Recommendations**
"@
        sprk_sortorder = 20
    },
    @{
        sprk_name = "Extract Data"
        sprk_description = "Extract structured data from the document including entities, dates, amounts, and references."
        sprk_systemprompt = @"
You are a data extraction specialist. Your task is to extract structured information from the provided document.

## Instructions
1. Identify and extract:
   - Organizations and companies mentioned
   - People names and their roles
   - Dates and time periods
   - Monetary amounts and currencies
   - Reference numbers, IDs, or codes
   - Contact information (emails, phones, addresses)
2. Organize the extracted data clearly
3. Note any ambiguous or unclear data

## Output Format
Use markdown tables and lists:
- **Organizations**: table with name, role/type
- **People**: table with name, role, organization
- **Dates**: list of significant dates with context
- **Amounts**: list of monetary values with context
- **References**: list of IDs, codes, reference numbers
"@
        sprk_sortorder = 30
    },
    @{
        sprk_name = "Prepare Response"
        sprk_description = "Draft a professional response letter or email based on the source document."
        sprk_systemprompt = @"
You are a professional business writer. Your task is to draft a response to the provided document.

## Instructions
1. Analyze the source document to understand:
   - The sender's main points or requests
   - The tone and formality level
   - Any deadlines or action items
2. Draft an appropriate response that:
   - Acknowledges receipt
   - Addresses each point raised
   - Maintains professional tone
   - Includes clear next steps
3. Format appropriately for the response type (letter, email, memo)

## Output Format
Provide the complete draft response in markdown format.
Include placeholder text in [brackets] for any information that needs to be filled in.
"@
        sprk_sortorder = 40
    },
    @{
        sprk_name = "Compare Documents"
        sprk_description = "Compare the document against reference materials or previous versions to identify differences and changes."
        sprk_systemprompt = @"
You are a document comparison specialist. Your task is to compare the provided document against reference materials.

## Instructions
1. Identify the type of comparison needed (vs. template, vs. prior version, vs. standard)
2. Analyze key differences in:
   - Structure and organization
   - Key terms and definitions
   - Obligations and requirements
   - Numbers, dates, and specifications
3. Highlight additions, deletions, and modifications
4. Assess the significance of each change

## Output Format
Use markdown with clear sections:
- **Comparison Summary**: Overview of differences
- **Structural Changes**: Organization/format differences
- **Content Changes**: Table showing section, change type, significance
- **Recommendations**: Suggested actions based on comparison
"@
        sprk_sortorder = 50
    }
)

foreach ($action in $actions) {
    New-DataverseRecord -EntitySet "sprk_analysisactions" -Data $action
}

# ============================================
# 2. CREATE ANALYSIS SKILLS (10 records)
# ============================================
Write-Host "`n=== Creating Analysis Skills ===" -ForegroundColor Cyan

$skills = @(
    # TONE SKILLS (100000000)
    @{
        sprk_name = "Professional Tone"
        sprk_description = "Write in a formal, professional business tone suitable for executive communication."
        sprk_promptfragment = "Write in a formal, professional tone. Use business-appropriate language, avoid colloquialisms and casual expressions. Maintain objectivity and convey authority while remaining accessible."
        sprk_category = 100000000
    },
    @{
        sprk_name = "Friendly Tone"
        sprk_description = "Write in a warm, approachable tone suitable for client-facing communication."
        sprk_promptfragment = "Write in a warm, friendly tone while maintaining professionalism. Use approachable language, show empathy, and create a positive impression. Be personable but not overly casual."
        sprk_category = 100000000
    },
    # STYLE SKILLS (100000001)
    @{
        sprk_name = "Concise Writing"
        sprk_description = "Write with brevity and clarity. Get to the point quickly."
        sprk_promptfragment = "Be concise and direct. Eliminate unnecessary words and redundancies. Use short sentences and paragraphs. Get to the main points quickly while ensuring clarity. Avoid filler phrases."
        sprk_category = 100000001
    },
    @{
        sprk_name = "Detailed Explanation"
        sprk_description = "Provide comprehensive explanations with supporting details and context."
        sprk_promptfragment = "Provide thorough, detailed explanations. Include relevant context, examples, and supporting information. Explain concepts step-by-step. Anticipate and address potential questions. Ensure readers have a complete understanding."
        sprk_category = 100000001
    },
    @{
        sprk_name = "Action-Oriented"
        sprk_description = "Focus on actionable recommendations and clear next steps."
        sprk_promptfragment = "Focus on actionable outcomes. Provide clear, specific recommendations. Include concrete next steps with defined responsibilities. Use action verbs. Prioritize recommendations by urgency or impact."
        sprk_category = 100000001
    },
    # FORMAT SKILLS (100000002)
    @{
        sprk_name = "Executive Summary Format"
        sprk_description = "Structure output as an executive summary with key insights upfront."
        sprk_promptfragment = "Format the output as an executive summary. Lead with the most important findings or recommendations. Use a pyramid structure: conclusion first, then supporting details. Include bullet points for key takeaways. Keep it scannable for busy executives."
        sprk_category = 100000002
    },
    @{
        sprk_name = "Structured with Headers"
        sprk_description = "Organize content with clear section headers and logical flow."
        sprk_promptfragment = "Organize the output with clear markdown headers (##, ###). Create a logical section structure. Use bullet points and numbered lists where appropriate. Include a table of contents for longer documents. Ensure each section has a clear purpose."
        sprk_category = 100000002
    },
    # EXPERTISE SKILLS (100000003)
    @{
        sprk_name = "Legal Expertise"
        sprk_description = "Apply legal analysis framework and terminology appropriate for legal documents."
        sprk_promptfragment = "Apply legal analysis expertise. Use appropriate legal terminology and conventions. Consider statutory and regulatory implications. Identify potential legal risks and liabilities. Note standard vs. non-standard clauses. Provide analysis without giving legal advice - recommend consultation with legal counsel for specific advice."
        sprk_category = 100000003
    },
    @{
        sprk_name = "Financial Expertise"
        sprk_description = "Apply financial analysis framework for documents with monetary content."
        sprk_promptfragment = "Apply financial analysis expertise. Identify and analyze monetary terms, payment schedules, and financial obligations. Calculate totals and percentages where relevant. Note financial risks and opportunities. Use standard financial terminology. Consider cash flow implications."
        sprk_category = 100000003
    },
    @{
        sprk_name = "Technical Expertise"
        sprk_description = "Apply technical analysis for documents with technical specifications or requirements."
        sprk_promptfragment = "Apply technical analysis expertise. Identify technical specifications, requirements, and standards. Evaluate feasibility and implementation considerations. Note integration points and dependencies. Use appropriate technical terminology. Consider scalability, security, and maintainability aspects."
        sprk_category = 100000003
    }
)

foreach ($skill in $skills) {
    New-DataverseRecord -EntitySet "sprk_analysisskills" -Data $skill
}

# ============================================
# 3. CREATE KNOWLEDGE DEPLOYMENT (1 record)
# ============================================
Write-Host "`n=== Creating Knowledge Deployment ===" -ForegroundColor Cyan

# sprk_deploymentmodel choices: 100000000=Shared, 100000001=Dedicated, 100000002=CustomerOwned
$deployment = @{
    sprk_name = "Spaarke Shared Knowledge"
    sprk_description = "Shared knowledge index hosted by Spaarke. Contains common document templates, best practices, and reference materials available to all customers."
    sprk_indexname = "spaarke-knowledge-shared"
    sprk_deploymentmodel = 100000000  # Shared
}

$deploymentResult = New-DataverseRecord -EntitySet "sprk_knowledgedeployments" -Data $deployment

# ============================================
# 4. CREATE KNOWLEDGE SOURCES (5 records)
# ============================================
Write-Host "`n=== Creating Knowledge Sources ===" -ForegroundColor Cyan

# Get the deployment ID for the lookup
$deploymentsUrl = "$baseUrl/sprk_knowledgedeployments?`$filter=sprk_name eq 'Spaarke Shared Knowledge'&`$select=sprk_knowledgedeploymentid"
$existingDeployment = Invoke-RestMethod -Uri $deploymentsUrl -Method Get -Headers $headers

if ($existingDeployment.value.Count -gt 0) {
    $deploymentId = $existingDeployment.value[0].sprk_knowledgedeploymentid

    # sprk_type choices: 100000000=Document, 100000001=Rule, 100000002=Template, 100000003=RAG_Index
    $knowledgeSources = @(
        @{
            sprk_name = "Standard Contract Templates"
            sprk_description = "Library of standard contract templates including NDAs, service agreements, and employment contracts."
            "sprk_deploymentid@odata.bind" = "/sprk_knowledgedeployments($deploymentId)"
            sprk_type = 100000002  # Template
        },
        @{
            sprk_name = "Company Policies"
            sprk_description = "Internal company policies, procedures, and guidelines."
            "sprk_deploymentid@odata.bind" = "/sprk_knowledgedeployments($deploymentId)"
            sprk_type = 100000001  # Rule
        },
        @{
            sprk_name = "Business Writing Guidelines"
            sprk_description = "Style guides, formatting standards, and writing best practices for professional documents."
            "sprk_deploymentid@odata.bind" = "/sprk_knowledgedeployments($deploymentId)"
            sprk_type = 100000001  # Rule
        },
        @{
            sprk_name = "Legal Reference Materials"
            sprk_description = "Legal terminology glossaries, clause explanations, and regulatory reference materials."
            "sprk_deploymentid@odata.bind" = "/sprk_knowledgedeployments($deploymentId)"
            sprk_type = 100000000  # Document
        },
        @{
            sprk_name = "Example Analyses"
            sprk_description = "Examples of completed document analyses that can be used as reference for style and format."
            "sprk_deploymentid@odata.bind" = "/sprk_knowledgedeployments($deploymentId)"
            sprk_type = 100000003  # RAG_Index
        }
    )

    foreach ($source in $knowledgeSources) {
        New-DataverseRecord -EntitySet "sprk_analysisknowledges" -Data $source
    }
} else {
    Write-Host "  Warning: Could not find Knowledge Deployment to link sources" -ForegroundColor Yellow
}

# ============================================
# SUMMARY
# ============================================
Write-Host "`n=== Import Complete ===" -ForegroundColor Green
Write-Host "Created:"
Write-Host "  - 5 Analysis Actions"
Write-Host "  - 10 Analysis Skills"
Write-Host "  - 1 Knowledge Deployment"
Write-Host "  - 5 Knowledge Sources"

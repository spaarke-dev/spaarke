# Index documents to RAG via BFF API using job queue pattern
# This script uses the /api/ai/rag/enqueue-indexing endpoint which:
# - Validates X-Api-Key header for authentication
# - Enqueues jobs to Service Bus for async processing
# - Job handler uses app-only auth (Pattern 6) for SPE file access
# Use this for: background jobs, scheduled indexing, bulk operations, automated testing.

$bffApiUrl = "https://spe-api-dev-67e2xz.azurewebsites.net"

# API key for RAG enqueue endpoint (must match Rag:ApiKey config on server)
# In production, retrieve from secure storage (e.g., Key Vault, environment variable)
$ragApiKey = $env:RAG_API_KEY
if (-not $ragApiKey) {
    Write-Host "RAG_API_KEY environment variable not set" -ForegroundColor Yellow
    Write-Host "Please set the RAG API key:" -ForegroundColor Yellow
    Write-Host '  $env:RAG_API_KEY = "your-api-key-here"' -ForegroundColor Gray
    exit 1
}

$headers = @{
    "X-Api-Key" = $ragApiKey
    "Content-Type" = "application/json"
}

# Documents to index (from query results)
$documents = @(
    @{
        documentId = "6846bb40-6ed9-f011-8406-7c1e520aa4df"
        driveId = "b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50"
        itemId = "01LBYCMX7LTBUUWADTTNDIFF6CICG7WWPM"
        fileName = "093277-1353777 Pending Claims as of 03 July 2023.DOCX"
    },
    @{
        documentId = "e9847752-6fd9-f011-8406-7c1e520aa4df"
        driveId = "b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50"
        itemId = "01LBYCMX6ZMBBLQIBWRVFINQI4RYJOWIRN"
        fileName = "BOA.P0053US02 - U.S. Filing Receipt dated 26 October 2022.PDF"
    },
    @{
        documentId = "a83255ca-edda-f011-8406-7c1e520aa4df"
        driveId = "b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50"
        itemId = "01LBYCMXZYNBTIWWHODVD2AKXJTTZ7YW7Q"
        fileName = "Issue Notification  Client Ref BOAP0053US02  KTS Ref 093277-1353777-BOAP0053US02.eml"
    }
)

Write-Host "`nEnqueuing $($documents.Count) documents for RAG indexing...`n" -ForegroundColor Cyan

foreach ($doc in $documents) {
    Write-Host "Enqueuing: $($doc.fileName)" -ForegroundColor Gray

    $body = @{
        tenantId = "dae9d4d3-5d57-4d6e-866a-fd29359f6623"  # Customer tenant ID
        driveId = $doc.driveId
        itemId = $doc.itemId
        fileName = $doc.fileName
        documentId = $doc.documentId
    } | ConvertTo-Json

    try {
        # Use job queue endpoint (async processing)
        $uri = "$bffApiUrl/api/ai/rag/enqueue-indexing"
        Write-Host "  POST $uri" -ForegroundColor DarkGray
        $response = Invoke-WebRequest -Uri $uri -Headers $headers -Method Post -Body $body -ContentType "application/json" -UseBasicParsing
        $result = $response.Content | ConvertFrom-Json
        Write-Host "  ACCEPTED: JobId=$($result.jobId), IdempotencyKey=$($result.idempotencyKey)" -ForegroundColor Green
    }
    catch {
        $statusCode = $_.Exception.Response.StatusCode.value__
        $errorBody = ""
        try {
            $stream = $_.Exception.Response.GetResponseStream()
            $reader = New-Object System.IO.StreamReader($stream)
            $errorBody = $reader.ReadToEnd()
        } catch {}
        Write-Host "  FAILED ($statusCode): $errorBody" -ForegroundColor Red
    }
}

Write-Host "`nDone! Jobs enqueued to Service Bus. Check job handler logs for processing status." -ForegroundColor Cyan

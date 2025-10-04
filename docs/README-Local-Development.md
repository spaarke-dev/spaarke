# SDAP Local Development Setup

**Purpose:** Guide for setting up local development environment with production-parity infrastructure
**Last Updated:** October 3, 2025

---

## Overview

SDAP uses **Azure Service Bus** for background job processing in both development and production. This ensures:
- ✅ Production parity - test the actual code that runs in production
- ✅ Durable job queue - jobs survive application restarts
- ✅ Reliable message delivery - automatic retries, dead-letter queue
- ✅ Realistic testing - same behavior as production

**Local Development Strategy:**
- Use **Azure Service Bus Emulator** (Docker container)
- No code changes needed between dev and production
- Same `ServiceBusJobProcessor` runs in both environments

---

## Quick Start

### Prerequisites
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) installed and running
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Visual Studio 2022](https://visualstudio.microsoft.com/) or [VS Code](https://code.visualstudio.com/)

### 1. Start Service Bus Emulator

```bash
# From repository root
docker-compose up -d

# Verify it's running
docker ps
```

**Expected Output:**
```
CONTAINER ID   IMAGE                                                      STATUS
abc123def456   mcr.microsoft.com/azure-messaging/servicebus-emulator...   Up 30 seconds
```

### 2. Configure Application

The Service Bus emulator connection string is already configured in `appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "ServiceBus": "Endpoint=sb://localhost:5672;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=emulatorkey;UseDevelopmentEmulator=true"
  }
}
```

**No action needed** - this is already set up for you.

### 3. Run the API

```bash
cd src/api/Spe.Bff.Api
dotnet run
```

**Expected Startup Log:**
```
✓ Job processing configured with Service Bus (queue: sdap-jobs)
```

---

## Service Bus Emulator Details

### What is the Service Bus Emulator?

The Azure Service Bus Emulator is an **official Microsoft container** that emulates Azure Service Bus locally. It provides:
- Queues (used by SDAP)
- Topics and subscriptions
- Message batching
- Dead-letter queues
- Session support
- Transactions

**Official Docs:** https://learn.microsoft.com/en-us/azure/service-bus-messaging/overview-emulator

### Emulator Limitations

| Feature | Emulator | Production |
|---------|----------|------------|
| **Queues** | ✅ Supported | ✅ Supported |
| **Topics/Subscriptions** | ✅ Supported | ✅ Supported |
| **Dead-letter queue** | ✅ Supported | ✅ Supported |
| **Message persistence** | ⚠️ Container restart loses messages | ✅ Fully durable |
| **Geo-replication** | ❌ Not supported | ✅ Available (Premium tier) |
| **Virtual Network** | ❌ Not supported | ✅ Available |
| **Authentication** | ⚠️ Emulator key only | ✅ Managed Identity, SAS, etc. |

**For SDAP:** Emulator is sufficient for local development. All features we use are supported.

### Emulator Configuration

**docker-compose.yml:**
```yaml
services:
  servicebus:
    image: mcr.microsoft.com/azure-messaging/servicebus-emulator:latest
    container_name: sdap-servicebus-emulator
    ports:
      - "5672:5672"   # AMQP port
    environment:
      ACCEPT_EULA: "Y"
      MSSQL_SA_PASSWORD: "ServiceBus123!"
    volumes:
      - servicebus-data:/data
```

**Connection String Format:**
```
Endpoint=sb://localhost:5672;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=emulatorkey;UseDevelopmentEmulator=true
```

**Key Parts:**
- `Endpoint=sb://localhost:5672` - Emulator endpoint
- `SharedAccessKeyName=RootManageSharedAccessKey` - Default emulator key name
- `SharedAccessKey=emulatorkey` - Default emulator key (not secret in emulator)
- `UseDevelopmentEmulator=true` - Tells SDK to use emulator mode

---

## Managing the Emulator

### Start Emulator
```bash
docker-compose up -d
```

### Stop Emulator
```bash
docker-compose down
```

### View Emulator Logs
```bash
docker logs sdap-servicebus-emulator -f
```

### Restart Emulator (Clear Messages)
```bash
docker-compose restart servicebus
```

### Remove Emulator and Data
```bash
docker-compose down -v  # -v removes volumes (clears persisted data)
```

---

## Testing Job Processing

### 1. Submit a Job via API

```bash
# Example: Submit a document processing job
curl -X POST http://localhost:5000/api/jobs/submit \
  -H "Content-Type: application/json" \
  -d '{
    "jobType": "DocumentProcessing",
    "containerId": "test-container-id",
    "path": "/test-document.docx"
  }'
```

### 2. Watch Application Logs

```bash
cd src/api/Spe.Bff.Api
dotnet run
```

**Expected Output:**
```
info: Job test-job-123 submitted to Service Bus queue: sdap-jobs
info: Processing job test-job-123 of type DocumentProcessing
info: Job test-job-123 completed successfully
```

### 3. Verify Queue Behavior

**Test Scenarios:**

| Test | Steps | Expected Behavior |
|------|-------|-------------------|
| **Job Processing** | Submit job → Check logs | Job processed successfully |
| **Idempotency** | Submit same JobId twice | Second submission rejected (already processed) |
| **Retry Logic** | Job handler throws exception | Job retried up to MaxAttempts (3x) |
| **Dead-Letter Queue** | Job fails 3 times | Job moved to dead-letter queue |
| **Durability** | Submit job → Restart API → Check logs | Job still processed (queue survived restart) |

---

## Troubleshooting

### Issue: "ConnectionStrings:ServiceBus is required"

**Cause:** Service Bus emulator not running or connection string not configured.

**Solution:**
```bash
# Check if emulator is running
docker ps | grep servicebus

# If not running, start it
docker-compose up -d

# Verify logs
docker logs sdap-servicebus-emulator
```

### Issue: "The messaging entity 'sb://localhost:5672/sdap-jobs' could not be found"

**Cause:** Queue doesn't exist yet. Service Bus emulator auto-creates queues on first use.

**Solution:** Wait a few seconds and retry. The queue will be created automatically.

### Issue: Docker Desktop not running

**Symptoms:**
```
Cannot connect to the Docker daemon at unix:///var/run/docker.sock
```

**Solution:**
1. Open Docker Desktop
2. Wait for it to fully start (whale icon in system tray should be stable)
3. Retry `docker-compose up -d`

### Issue: Port 5672 already in use

**Cause:** Another service (e.g., RabbitMQ) is using port 5672.

**Solution:**

**Option 1:** Stop conflicting service
```bash
# Find process using port 5672
netstat -ano | findstr :5672  # Windows
lsof -i :5672                 # macOS/Linux

# Stop the process
```

**Option 2:** Change emulator port in docker-compose.yml
```yaml
ports:
  - "5673:5672"  # Map host port 5673 to container port 5672
```

Then update connection string in `appsettings.Development.json`:
```json
"ServiceBus": "Endpoint=sb://localhost:5673;..."
```

---

## Alternative: Use Real Azure Service Bus

If you prefer not to use the emulator, you can use a **dev Service Bus namespace** in Azure.

### Option 1: Shared Dev Namespace (Recommended for Teams)

**Cost:** ~$10/month for Basic tier (shared across entire team)

**Setup:**
1. Create Service Bus namespace in Azure Portal
2. Create queue named `sdap-jobs`
3. Get connection string from "Shared access policies"
4. Store in **user secrets** (NOT in appsettings.Development.json):

```bash
cd src/api/Spe.Bff.Api
dotnet user-secrets set "ConnectionStrings:ServiceBus" "Endpoint=sb://your-dev-namespace.servicebus.windows.net/;SharedAccessKeyName=...;SharedAccessKey=..."
```

### Option 2: Personal Dev Namespace

**Cost:** ~$10/month per developer (if everyone wants their own namespace)

**Pros:**
- Isolated testing environment
- No conflicts with other developers

**Cons:**
- Higher cost ($10/month per dev)
- More setup overhead

---

## Production Configuration

In production, SDAP uses **Azure Service Bus** with:
- **Standard tier** (supports topics, dead-letter queues)
- **Managed Identity** authentication (no connection strings)
- **Virtual Network** integration
- **Azure Monitor** alerts and metrics

**Connection String in Production:**
Stored in Azure Key Vault as `ServiceBus-ConnectionString`:
```json
{
  "ConnectionStrings": {
    "ServiceBus": "@Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/ServiceBus-ConnectionString)"
  }
}
```

---

## Background Job Processing Architecture

### Before (Removed)
```
┌──────────────────────────────────────┐
│  Development: In-Memory JobProcessor │  ❌ Not durable, code duplication
└──────────────────────────────────────┘
┌──────────────────────────────────────┐
│  Production: ServiceBusJobProcessor  │  ✅ Durable, production-ready
└──────────────────────────────────────┘
```

### After (Current)
```
┌──────────────────────────────────────┐
│  All Environments:                   │
│  ServiceBusJobProcessor              │  ✅ Production parity everywhere
│  - Dev: Service Bus Emulator         │
│  - Prod: Azure Service Bus           │
└──────────────────────────────────────┘
```

**Benefits:**
- ✅ Single implementation to maintain
- ✅ Test production code path locally
- ✅ No "works in dev, fails in prod" surprises
- ✅ Realistic retry/DLQ behavior in dev

---

## Common Development Workflows

### Daily Development

```bash
# Morning: Start infrastructure
docker-compose up -d

# Run API
cd src/api/Spe.Bff.Api
dotnet run

# Evening: Stop infrastructure (optional - can leave running)
docker-compose down
```

### Running Tests

```bash
# Unit tests (don't require Service Bus)
dotnet test tests/unit/Spe.Bff.Api.Tests

# Integration tests (require Service Bus emulator)
docker-compose up -d
dotnet test tests/integration/Spe.Integration.Tests
```

### Debugging Jobs

```bash
# Enable detailed job logging
# In appsettings.Development.json:
{
  "Logging": {
    "LogLevel": {
      "Spe.Bff.Api.Services.Jobs": "Debug"  # Set to Debug for detailed logs
    }
  }
}

# Run with logs
dotnet run

# Submit test job and watch logs
```

---

## FAQ

### Q: Do I need to run the emulator every time I develop?
**A:** Yes, if you're working on features that submit background jobs. If you're only working on API endpoints that don't use jobs, you can skip it.

### Q: Can I use Azure Service Bus emulator in CI/CD?
**A:** Yes! The docker-compose.yml can be used in GitHub Actions, Azure DevOps, or other CI/CD systems.

### Q: Does the emulator support all Service Bus features?
**A:** Most features - queues, topics, dead-letter queues, sessions, transactions. Missing: geo-replication, VNet, some authentication methods. See [Limitations](#emulator-limitations).

### Q: What happens to messages when I restart the emulator?
**A:** Messages are lost. The emulator is for development/testing only. Production Azure Service Bus is fully durable.

### Q: Can I use RabbitMQ or other message brokers instead?
**A:** No. SDAP uses Azure Service Bus SDK features (dead-letter queues, sessions, etc.) that are Service Bus-specific. Changing to another broker would require significant code changes.

### Q: How do I monitor the emulator?
**A:** Check Docker logs: `docker logs sdap-servicebus-emulator -f`. The emulator doesn't have a UI like Azure Portal.

---

## Related Documentation

- [Azure Service Bus Emulator Docs](https://learn.microsoft.com/en-us/azure/service-bus-messaging/overview-emulator)
- [Docker Compose Docs](https://docs.docker.com/compose/)
- [SDAP ADR-004: Async Job Contract](../README-ADRs.md#adr-004-async-job-contract)
- [JobSubmissionService.cs](../src/api/Spe.Bff.Api/Services/Jobs/JobSubmissionService.cs)
- [ServiceBusJobProcessor.cs](../src/api/Spe.Bff.Api/Services/Jobs/ServiceBusJobProcessor.cs)

---

## Change Log

| Date | Change | Reason |
|------|--------|--------|
| 2025-10-03 | Removed in-memory JobProcessor | Eliminate code duplication, production parity |
| 2025-10-03 | Added Service Bus emulator setup | Enable local dev with production code path |
| 2025-10-03 | Removed `Jobs:UseServiceBus` flag | Single implementation strategy |

---

**Questions or Issues?**
- Check troubleshooting section above
- Review Docker logs: `docker logs sdap-servicebus-emulator -f`
- Check API logs for Service Bus connection errors
- Verify emulator is running: `docker ps`

**Need Help?**
Open an issue in the repository with:
- Steps to reproduce
- Docker logs
- API logs
- Environment details (OS, Docker version)

# Spaarke.Dataverse

**Dataverse integration for SDAP**

Provides unified `IDataverseService` interface with dual implementation:
- ✅ **DataverseServiceClientImpl** (Production) - ServiceClient SDK, Singleton lifetime
- ⚠️ **DataverseWebApiService** (Alternative) - REST/OData API, HttpClient-based
- ✅ **DataverseAccessDataSource** (Production) - User access queries for authorization

## Documentation

📚 **[Technical Overview](docs/TECHNICAL-OVERVIEW.md)** - Complete technical documentation for production implementation

⚠️ **[Web API Documentation](docs/TECHNICAL-OVERVIEW-WEB-API.md)** - Alternative REST/OData implementation (not currently used)

## Quick Links

- [Current Production Setup](docs/TECHNICAL-OVERVIEW.md#current-production-setup) - ServiceClient as Singleton
- [ServiceClient vs Web API Comparison](docs/TECHNICAL-OVERVIEW.md#serviceclient-vs-web-api-comparison)
- [Configuration](docs/TECHNICAL-OVERVIEW.md#configuration)
- [Switching Implementations](docs/TECHNICAL-OVERVIEW.md#switching-between-implementations)

## Components

| Component | Status | Purpose |
|-----------|--------|---------|
| `DataverseServiceClientImpl` | ✅ Production | Dataverse CRUD operations (ServiceClient SDK) |
| `DataverseWebApiService` | ⚠️ Alternative | Dataverse CRUD operations (REST/OData) |
| `DataverseAccessDataSource` | ✅ Production | User access queries for authorization |
| `IDataverseService` | ✅ Interface | Shared abstraction (16 methods) |

## Status

✅ Production-Ready | Last Updated: 2025-12-03

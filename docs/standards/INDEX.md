# Coding Standards & Patterns

> **Purpose**: Cross-cutting coding standards, anti-patterns, and integration contracts for Spaarke
> **Audience**: Developers, AI coding agents
> **Last Reviewed**: 2026-04-05

## Overview

This directory contains cross-cutting coding standards, anti-pattern catalog, integration contracts between subsystems, and authentication patterns.

## Standards Index

### General Coding Standards

- **[CODING-STANDARDS.md](CODING-STANDARDS.md)** - Naming conventions, file organization, formatting, error handling, and language-specific rules (.NET/C#, TypeScript, PCF, Code Pages)
- **[ANTI-PATTERNS.md](ANTI-PATTERNS.md)** - Catalog of patterns to avoid across BFF API, PCF controls, plugins, auth, and AI integration with corrective examples
- **[INTEGRATION-CONTRACTS.md](INTEGRATION-CONTRACTS.md)** - Cross-module integration contracts: API → PCF, PCF → BFF, plugin → API, job contracts, event shapes, and versioning rules

### Authentication & Security

- [../guides/DATAVERSE-AUTHENTICATION-GUIDE.md](../guides/DATAVERSE-AUTHENTICATION-GUIDE.md) - OAuth patterns for Dataverse
- [oauth-obo-patterns.md](oauth-obo-patterns.md) - Common OAuth mistakes to avoid and correct patterns
- [oauth-obo-errors.md](oauth-obo-errors.md) - OAuth error handling patterns

## For AI Agents

**Loading strategy**: Load relevant standards when implementing authentication or data access patterns.

**When to reference**:
- Implementing OAuth flows → Load oauth-obo-patterns.md and oauth-obo-errors.md
- Dataverse authentication → Load DATAVERSE-AUTHENTICATION-GUIDE.md
- Code review → Check against anti-patterns


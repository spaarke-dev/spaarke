# Coding Standards & Patterns

> **Purpose**: Coding standards, conventions, and best practices for Spaarke development
> **Audience**: Developers, AI coding agents
> **Status**: Under review - content needs systematic revision

## Overview

This directory contains standards for authentication, data access patterns, and platform-specific coding conventions.

## Standards Index

<!-- TODO: Phase 3 - Review and organize -->

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

## Phase 3 TODO

- [ ] Review for current accuracy
- [ ] Add general coding standards (naming, formatting, etc.)
- [ ] Create domain-specific constraint files for `.claude/constraints/`
- [ ] Add examples and code snippets
- [ ] Cross-reference with relevant ADRs
- [ ] Identify gaps in standards coverage

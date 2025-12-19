# Coding Standards & Patterns

> **Purpose**: Coding standards, conventions, and best practices for Spaarke development
> **Audience**: Developers, AI coding agents
> **Status**: Under review - content needs systematic revision

## Overview

This directory contains standards for authentication, data access patterns, and platform-specific coding conventions.

## Standards Index

<!-- TODO: Phase 3 - Review and organize -->

### Authentication & Security

- [dataverse-oauth-authentication.md](dataverse-oauth-authentication.md) - OAuth patterns for Dataverse
- [oauth-obo-anti-patterns.md](oauth-obo-anti-patterns.md) - Common OAuth mistakes to avoid
- [oauth-obo-errors.md](oauth-obo-errors.md) - OAuth error handling patterns
- [oauth-obo-implementation.md](oauth-obo-implementation.md) - On-Behalf-Of flow implementation

## For AI Agents

**Loading strategy**: Load relevant standards when implementing authentication or data access patterns.

**When to reference**:
- Implementing OAuth flows → Load oauth-obo-*.md files
- Dataverse authentication → Load dataverse-oauth-authentication.md
- Code review → Check against anti-patterns

## Phase 3 TODO

- [ ] Review for current accuracy
- [ ] Add general coding standards (naming, formatting, etc.)
- [ ] Create domain-specific constraint files for `.claude/constraints/`
- [ ] Add examples and code snippets
- [ ] Cross-reference with relevant ADRs
- [ ] Identify gaps in standards coverage

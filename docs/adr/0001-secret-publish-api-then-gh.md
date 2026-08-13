# 0001. Secret Publish uses the GitHub API first, then gh

Date: 2026-08-13

## Status
Accepted

## Context
The desktop tool must write Action Secrets. Two adapters exist: the Actions Secrets HTTP API (libsodium sealed box) and the `gh secret set` CLI.

## Decision
Try the HTTP API with a PAT or `gh auth token`. If that fails and `gh` is installed, fall back to `gh secret set`.

## Consequences
Callers only see one Secret Publish interface. Tests can exercise encrypt / repo parsing without the CLI. Machines without `gh` still work with a PAT.

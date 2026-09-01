# Deployment profiles

Environment-specific **site metadata values** only. One profile per environment, selected at upload:

```powershell
pac pages upload `
  --path ".\powerpages\outcome-testing---outcometesting" `
  --modelVersion Enhanced `
  --deploymentProfile "test"
```

## What belongs here

Site URLs, support email address, environment banner, feature flags, non-production notification labels, environment-specific content snippets.

## What must never go here

Secrets, client secrets, tokens, connection strings, passwords, tenant or environment GUIDs, or personal data. Deployment profiles are committed to git and are not a secret store (build guide section 7.4). Use environment variables and connection references for anything environment-bound, and an approved secret store for anything sensitive.

## Before adopting a schema

The profile file format must be validated against the actual downloaded site format and the current Microsoft documentation for the installed PAC CLI version, rather than copied from an example. No profile files are committed yet for that reason — they are created in Phase 1 once the format is confirmed against PAC 2.11.2.

Whichever profile is used for a release must be recorded in that release's manifest.

# Security Policy

## Reporting a vulnerability
Please report vulnerabilities in AppSec Ascent's **own** code privately, through GitHub's **[private vulnerability reporting](https://github.com/JJWren/appsec-ascent/security/advisories/new)**. Don't open a public issue.

These are in scope:
- the **Engine** (`engine/`), including the CLI, content tooling, sealing and Flag handling
- **CI/CD workflows** (`.github/workflows/`) and repository automation
- **Supply-chain issues**: dependencies, pinned actions and released packages

We aim to acknowledge reports within 7 days.

## Out of scope: the Throughline System
The **Throughline System** (`throughline/`, plus the Lab Modules unsealed at runtime) is **deliberately vulnerable**. It is a training target, and its weaknesses are the point. Reports of vulnerabilities in the Throughline System are out of scope. Use it only on your own machine or your own Azure subscription, and never expose it to the internet beyond what a Lab instructs.

Also out of scope:
- Spoilers for Sealed content. Sealing is a spoiler shield, not a security boundary.
- Findings that require a learner to attack their own environment.

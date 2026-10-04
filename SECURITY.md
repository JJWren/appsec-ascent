# Security Policy

## Reporting a vulnerability
Please report vulnerabilities in AppSec Ascent's **own** code privately, through GitHub's **[private vulnerability reporting](https://github.com/JJWren/appsec-ascent/security/advisories/new)**. Don't open a public issue.

These are in scope:
- the **Engine** (`engine/`), including the CLI, content tooling, sealing and Flag handling
- **CI/CD workflows** (`.github/workflows/`) and repository automation
- **Supply-chain issues**: dependencies, pinned actions and released packages

We aim to acknowledge reports within 7 days.

## Sealed Bundle signing key
Every Sealed Bundle is signed with the maintainer's ECDSA P-256 key (ADR 0005). The Engine trusts exactly one public key, `engine/src/Ascent.Sealing/Keys/maintainer.pub.pem`. Its SHA-256 fingerprint (over the SubjectPublicKeyInfo) is:

```
238a32b42af4ef0ff50bf6df670e595fa4585e5265e3c5b7f58aa3ae379709a7
```

`ascent verify-bundles` prints the fingerprint of the key your Engine trusts. If you run the Engine from a fork, compare that fingerprint with the one above: a fork can embed a different key. If the key is ever rotated, this section and the release notes will say so.

## Out of scope: the Throughline System
The **Throughline System** (`throughline/`, plus the Lab Modules unsealed at runtime) is **deliberately vulnerable**. It is a training target, and its weaknesses are the point. Reports of vulnerabilities in the Throughline System are out of scope. Use it only on your own machine or your own Azure subscription, and never expose it to the internet beyond what a Lab instructs.

Also out of scope:
- Spoilers for Sealed content. Sealing is a spoiler shield, not a security boundary.
- Findings that require a learner to attack their own environment.

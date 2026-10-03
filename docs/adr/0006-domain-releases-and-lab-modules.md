# Per-Domain Releases with isolated Lab Modules

Each Domain starts from a canonical **Release** of the Throughline System. A Release already includes the reference fixes for every earlier Domain. Story-wise: "the team shipped everyone's work".

Within a Domain, each Lab's vulnerability lives in its own **Lab Module**. A Lab Module can be a code module, a config overlay, an IaC module, or a pipeline file.

This design has three benefits:
- A Learner's fixes accumulate within a Domain without merge conflicts.
- Every Lab is reproducible, which the CI proofs require.
- No Learner falls behind because of an earlier mistake.

The Learner's own fixes are archived on `portfolio/d<n>` branches when they move on. Releases after the first stay Sealed until the previous Domain is completed or explicitly skipped, because they contain earlier reference fixes.

## Considered Options
- **Patching one cumulative codebase.** Rejected: Learners' differing fixes cause merge conflicts.
- **A snapshot per Lab.** Rejected: fixes never accumulate.
- **Feature-flagged vulnerabilities.** Rejected: reading the code reveals every vulnerability.

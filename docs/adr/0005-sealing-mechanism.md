# Two-tier sealing: private plaintext, public signed bundles, dynamic Flags

Sealed items are written as plaintext only in a separate private repository, `appsec-ascent-sealed`. It is cloned outside the public working tree and outside the builder's editor workspace, and background agents do the writing, so the builder never encounters this content while reviewing or chatting. Sealed items are:
- Lab Module code
- Lab security tests
- fixes
- reference exploits
- Deliverable reference answers
- the Question Bank
- the Simulation pool
- Releases after the first

The public repository ships each item only as a **Sealed Bundle**:
- **Encrypted** per item with AES-256-GCM, using a key derived via HKDF from a tier key that ships with the Engine.
- **Signed** with an ECDSA P-256 key held by the maintainer. The Engine refuses any bundle that has been tampered with.

**Flags** are generated at random each time a Lab starts and planted into the running system. The Engine stores only a salted hash. Each later tier (tests, fix, reference answers, Releases) is released only after:
- the Engine verifies the Flag, or
- the Learner submits a Deliverable, or
- the Learner completes a Domain.

Reference exploits never leave the private repository. Its **Proof Pipeline** proves every Lab and reports only pass or fail.

Because the tier keys ship with the Engine, this is a spoiler shield against accidental exposure, not DRM against a determined Learner.

## Considered Options
- **Obfuscation only.** Rejected: trivially reversible, and sealed content could still leak during the build.
- **Sealed content only in the private repository.** Rejected: other Learners would get no Labs or questions.
- **A hosted verification service.** Rejected: hosting costs money, needs connectivity and accounts, and breaks the $0, offline path.

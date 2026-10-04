# The Learner workspace is its own repository

A Learner's work lives in `my-work/`, which the public repository ignores. It holds the current Release in `throughline/`, the Lab Modules unpacked into it, and the Learner's Deliverables and drill answers. The Engine offers to make `my-work/` its own git repository. It lists the exact git commands first and runs nothing until the Learner agrees.

When the Learner moves to the next Release, the Engine archives their Domain's work to a `portfolio/d<n>` branch in that repository. It then replaces `throughline/` with the new Release and commits that as the new baseline. Planted Flags go to `throughline/.flags/`, which the workspace's own ignore file excludes, so a Flag is never committed. What the Learner chooses to show the world goes to the fork's tracked `portfolio/` folder: `PORTFOLIO.md` and any Deliverables they opt in.

This keeps Sealed-derived code out of the Learner's public fork. That code includes the Lab Modules, and later Releases that carry the reference fixes. It also means pulling upstream updates to the Program never conflicts with the Learner's fixes. The Learner may still push the workspace repository to any remote they choose, such as a private one.

## Considered Options
- **Work directly in the fork's tracked `throughline/`.** Rejected: Lab Modules and later Releases would be committed to a public fork, and every upstream update would conflict with the Learner's fixes.
- **One branch per Domain in the fork.** Rejected: the same publication risk, and switching Releases would rewrite tracked files under the Learner.
- **A plain folder without git.** Kept only as the fallback when the Learner declines a repository. The Release still switches, but nothing archives the Domain's work.

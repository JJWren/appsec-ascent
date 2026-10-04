# Engine rules

These are the rules the `ascent` Engine plays by. Each has an ID, and the Engine's tests are tagged with the IDs they check, so every rule here is backed by at least one test.

"Local day" and "week" use your time zone (`ascent config timeZone`), and weeks are ISO weeks starting on Monday.

## XP and Ranks

| ID | Rule |
|---|---|
| XP-01 | XP is awarded once per activity, using the table below. Running a command again never pays twice. |
| XP-02 | Bonus XP counts toward your total, but bonus content is never needed for a Rank. |
| XP-03 | Penalties are negative XP. They only take away XP you've already earned: your total never goes below 0, and no debt carries forward. |
| RNK-01 | Ranks need a share of the XP the core path can earn (`CoreXpMax`): Developer 0%, Security Champion 10%, AppSec Engineer 35%, Senior AppSec Engineer 60%, Principal AppSec Engineer 85%. |
| RNK-02 | Senior also needs a best Boss Fight score of at least 70% in all 8 Domains. Principal also needs a complete Capstone. |
| RNK-03 | Ranks never go down, not even when a penalty lands or new Curriculum raises `CoreXpMax`. |
| RNK-04 | Every Rank can be reached on the free path, on any operating system. Principal needs at most 90% of `CoreXpMax`, and no bonus content. |

**XP awards**

| Source | XP |
|---|---|
| Lesson and its Teach-back | 10 |
| Drill | 15 |
| Lab Red / Blue / Explain | 25 / 40 / 10 |
| Deliverable submitted | 40 |
| Self-score at or above the pass mark | +10 |
| Stand-up | 5 per day |
| Weekly goal met | 20 |
| Week streak | +5 × min(streak − 1, 4) |
| Boss Fight passed on the first attempt | 100 |
| Boss Fight passed after a failed attempt | 60 (once) |
| Confirmed Content Bug | 25 |
| **Bonus:** Cloud Stage completed | 30 |
| **Bonus:** teardown before expiry | +10, or −20 for an overrun |
| **Bonus:** Deep Dive | 75 |
| **Bonus:** AI-reviewer injection Flag | 30 (once per Deliverable) |

## Weekly goal

| ID | Rule |
|---|---|
| WG-01 | Your weekly goal is the number of days per week with a completed Stand-up. It's 5 unless you change it (1 to 7). |
| WG-02 | A day counts once you've answered every question the Stand-up showed you. If nothing was due, the day counts once you confirm you reviewed your notes. |
| WG-03 | Meeting the goal pays 20 XP once a week. Your streak is how many weeks in a row you've met it; missing a week resets it to 0. |
| WG-04 | `ascent status` shows this week's progress and your streak. |

## Stand-up

| ID | Rule |
|---|---|
| SU-01 | Due cards come first, oldest first, up to 20. Then up to 5 new cards a day, from Objectives whose lesson you've finished. Domains waiting for a Boss Fight rematch come first. |
| SU-02 | Only practice questions appear. Diagnostic and Simulation questions never do. |
| SU-03 | After each answer you see why every option is right or wrong, and the sources. |
| SU-04 | Scheduling uses FSRS: a right answer counts as Good, a wrong one as Again, aiming for 90% retention. |
| SU-05 | A question you've reported as a Content Bug is paused until the bug is resolved. |

## Diagnostic

| ID | Rule |
|---|---|
| DX-01 | 40 questions from the Diagnostic pool, spread by exam weight. It's untimed, there's no feedback until the end, and its questions never become review cards. |
| DX-02 | Your study plan gives each Domain weeks in proportion to its exam weight. Domains where you scored under 60% get a quarter more. |
| DX-03 | Study weeks = the estimated hours ÷ your weekly hours, rounded up. The suggested exam window starts after the plan plus 2 Simulation weeks. |

## Boss Fights

| ID | Rule |
|---|---|
| BF-01 | 30 practice questions from the Domain, shared equally across its Objectives. Any extras go to the Objectives you know least. |
| BF-02 | You have 45 minutes. Questions left unanswered at the deadline count as wrong. There's no feedback until the end; then you see every rationale and source. |
| BF-03 | 70% passes. A first-attempt pass earns 100 XP; passing after a failed attempt earns 60 XP, once. |
| BF-04 | Retries are unlimited and never lock anything. When a Domain has at least 60 questions, a retry leaves out the previous attempt's questions. |
| BF-05 | A failed attempt marks the Domain for a rematch, and your Stand-ups favour it until you pass. |
| BF-06 | Boss Fight answers update your review cards, adding any question you hadn't seen. |

## Simulations

| ID | Rule |
|---|---|
| SIM-01 | Simulations unlock once every Boss Fight's best score is at least 70%. Until then, the Engine names the Domains still below. |
| SIM-02 | There are two fixed forms, A and B: 125 questions each, split by exam weight from a reserved pool. |
| SIM-03 | You have 3 hours from the start. The clock keeps running if you stop, and you can resume until the deadline. At the deadline, unanswered questions count as wrong. |
| SIM-04 | There's no feedback during a Simulation. Afterwards a review shows every rationale and source. The score is a plain percentage against a 70% target; it doesn't imitate the exam's scaled scoring. |
| SIM-05 | You can retake a form, but only the first attempts of A and B count toward *Exam Ready*. |
| SIM-06 | Simulation questions are never used anywhere else and never become review cards. |

## Sealed content

| ID | Rule |
|---|---|
| SEAL-01 | Before opening a Sealed item, the Engine checks the maintainer's signature over the whole bundle. If the check fails, it refuses and names only the item. |
| SEAL-02 | Each item has its own key, derived from a published shield key with versioned labels. Encryption also covers the item's header, so editing the header breaks decryption. |
| SEAL-03 | Each kind of Sealed item opens only at the right moment: Lab files when you start the Lab (after accepting the rules of engagement), fixes and tests once you've captured the Flag, reference answers once you've submitted your work, practice questions only when they're served, and Simulation questions only during your Simulation. |
| SEAL-04 | Opened items go to a private, owner-only folder under `.ascent/`, are wiped when the command ends, and are never logged. |
| SEAL-05 | Every time an item is opened for the first time, the Engine records it. |
| SEAL-06 | This is a spoiler shield, not DRM. The keys ship with the Engine, so a determined Learner could read ahead; the point is that you don't by accident. |

## Flags

| ID | Rule |
|---|---|
| FLAG-01 | Starting a Lab creates a new random Flag and replaces any earlier one for that Lab. |
| FLAG-02 | Flags are stored only as salted hashes and checked in constant time. |
| FLAG-03 | A wrong Flag gets no hint about how close it was. 5 wrong tries within 10 minutes start a 60-second cooldown. |
| FLAG-04 | The Flag is planted in your Local Stage: in a file under `my-work/throughline/.flags/`, which git ignores, or handed to the Lab's planter on standard input. The Engine never prints a Flag. |

## Labs

| ID | Rule |
|---|---|
| LABE-01 | A Lab goes `NotStarted` → `Started` → `FlagCaptured` → `Fixed` → `Explained`, one step at a time. |
| LABE-02 | `ascent verify` unlocks once you've captured the Flag, because the security tests only open then. It moves the Lab to `Fixed` when every test passes, including the test that plants a fresh Flag and checks it can no longer be captured. |
| LABE-03 | `Explained` needs a Teach-back of 30–150 words. Each step's XP is paid once per Lab. |
| LABE-04 | Running `ascent lab up` again plants a fresh Flag and keeps your progress and your files. Only `ascent lab reset` puts the module's original files back, after you confirm. |
| LABE-05 | Windows-only and paid Labs are bonus: skipping them never blocks a Quest, a Release or a Rank. |

## Cloud Stages

| ID | Rule |
|---|---|
| CLD-01 | No Cloud Stage deploys until the Azure CLI is signed in and budget alerts of $5, $10 and $20 and the guardrail Policy allow-list exist. `ascent guardrails apply` sets them up after you confirm. |
| CLD-02 | Before deploying, you see the estimate from the Lab's manifest, refreshed from the Azure Retail Prices API if you ask (`--refresh-estimate`). A paid side-quest needs you to type the Lab's ID. |
| CLD-03 | Deployments are tagged `ascent:lab=<id>` and `expires-on=<UTC time>`, which is the deployment time plus the Lab's teardown window. |
| CLD-04 | `ascent teardown` deletes every resource group tagged `ascent:lab`, then checks nothing is left. Tearing down before `expires-on` earns 10 bonus XP. |
| CLD-05 | A tagged deployment found past `expires-on` (checked by `status`, `teardown` and `lab up`) costs 20 XP, once, and you're asked to tear it down. |

## Deliverables

| ID | Rule |
|---|---|
| DLE-01 | Your work lives in `my-work/deliverables/<id>.yaml`, which git ignores. The first `ascent deliver <id>` creates it from the template. |
| DLE-02 | Your work is checked against the template's required sections and fields. Anything missing or wrong is listed exactly, and no XP is paid until it's fixed. |
| DLE-03 | You score your work against the rubric, 0–4 per criterion, weighted. Submitting pays 40 XP, plus 10 at or above the pass mark, and then shows the reference answer. |
| DLE-04 | The AI reviewer is optional and never changes your XP or score. Each review has a random secret: get the reviewer to reveal it and you earn the Prompt Breaker bonus Flag (30 XP, once per Deliverable). |

## Releases and the portfolio

| ID | Rule |
|---|---|
| REL-01 | The next Release unlocks once every Quest of your current Domain is complete and you've attempted its Boss Fight (Orientation has no Boss Fight). |
| REL-02 | `ascent release next --skip` moves on early after you type the Domain's ID. Unfinished Labs go to Season 2 and their XP is forfeited; the next Release includes the reference fixes. |
| REL-03 | Before switching Releases, your work is archived to a `portfolio/d<n>` branch in `my-work/`. Every git command is listed, and nothing runs until you confirm. |
| PORT-01 | `ascent portfolio` writes `portfolio/PORTFOLIO.md`: your Rank, badges, Domains cleared, whether you're Exam Ready, and the Deliverables you chose to include. |
| PORT-02 | The portfolio never includes Teach-backs, answers to questions, scores beyond pass/fail badges, Sealed content or reference answers. |
| PORT-03 | `ascent portfolio --include <id>` copies one of your submitted Deliverables into `portfolio/` for you to commit. |

## Season 2, the exam, privacy, Content Bugs and backups

| ID | Rule |
|---|---|
| S2-01 | Skipped Deep Dives and Labs are listed under Season 2 in `ascent status`. |
| EXM-01 | When Season 1 is complete and you haven't set an exam date, `ascent start` asks for one and shows your plan's suggested window. `--later` asks again tomorrow. |
| PRV-01 | The Engine only goes online when you ask it to, and only to GitHub, the Azure Retail Prices API or your own AI endpoint. There's no telemetry. |
| PRV-02 | If your AI endpoint isn't on this machine, the Engine warns that your Deliverable's text will be sent to it, and asks once for that endpoint. |
| BUG-01 | `ascent bug <id>` opens a Content Bug report that's already filled in with the item's ID and, if you give one, the citation's ID. Nothing personal is in it. |
| BUG-02 | `ascent sync` reads your public issues marked `content-bug:confirmed` without signing in, and pays 25 XP once per confirmed bug. A paused review card resumes once its bug is closed. |
| BAK-01 | `ascent progress export` writes all your progress to a private JSON file, and `ascent progress import` restores it after backing up what's there. Flags aren't exported. |

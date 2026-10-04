# Engine rules

These are the rules the `ascent` Engine plays by. Each has an ID, and the Engine's tests are tagged with the IDs they check, so every rule here is backed by at least one test.

"Local day" and "week" use your time zone (`ascent config timeZone`), and weeks are ISO weeks starting on Monday.

Rules for Labs, Cloud Stages, Deliverables, Releases and the portfolio will be added here as those features land.

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

## Labs

| ID | Rule |
|---|---|
| LABE-05 | Windows-only and paid Labs are bonus: skipping them never blocks a Quest or a Rank. |

## Season 2, the exam, privacy and backups

| ID | Rule |
|---|---|
| S2-01 | Skipped Deep Dives and Labs are listed under Season 2 in `ascent status`. |
| EXM-01 | When Season 1 is complete and you haven't set an exam date, `ascent start` asks for one and shows your plan's suggested window. `--later` asks again tomorrow. |
| PRV-01 | The Engine only goes online when you ask it to, and only to GitHub, the Azure Retail Prices API or your own AI endpoint. There's no telemetry. |
| BAK-01 | `ascent progress export` writes all your progress to a private JSON file, and `ascent progress import` restores it after backing up what's there. Flags aren't exported. |

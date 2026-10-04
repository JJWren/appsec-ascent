# AppSec Ascent

> **Work in progress.** Season 1 is still being built, and nothing here is ready to study from yet.

**An unofficial study companion for the ISC2 CSSLP® exam.**

AppSec Ascent teaches the secure software lifecycle by having you harden one deliberately vulnerable telehealth system, built with .NET and Azure, across all eight exam domains. A gamified command-line tool, `ascent`, runs your daily review, checks your labs, and tracks your progress up a ladder of AppSec career ranks.

## Status
Planning is complete, the repository foundation and curriculum framework are in place, and the Engine is being built:
- [`CONTEXT.md`](CONTEXT.md) defines the project's shared vocabulary.
- [`docs/adr/`](docs/adr/) records the key design decisions.
- [`curriculum/outline/`](curriculum/outline/) maps the exam outline's 8 Domains and 58 Objectives, with ISC2's AI-guidance topics.
- [`schemas/`](schemas/) defines the content formats.

### For maintainers
The Engine runs from source until its first package release. It needs the .NET 10 SDK.

```bash
./ascent lint                 # check content against the framework's rules
./ascent coverage             # coverage against the exam outline (counts only)
./ascent exceptions check     # fail on expired security exceptions
```

On Windows, use `ascent.cmd` in place of `./ascent`. To report a problem with the content, see [CONTRIBUTING.md](CONTRIBUTING.md). To report a security issue, see [SECURITY.md](SECURITY.md).

## Using the Engine
The Engine, `ascent`, runs from source and needs the .NET 10 SDK. Run `./ascent` (or `ascent.cmd` on Windows). The first run builds it into `.ascent/bin/`, later runs start straight away, and `./ascent --rebuild` forces a build.

| To… | Run |
|---|---|
| Set up and see what's next | `ascent start`, then `ascent rules` |
| Study a Quest | `ascent next`, `ascent quest <id>`, `ascent teachback <id>` |
| Review every day | `ascent standup` |
| Play a Lab | `ascent lab up <id>`, `ascent flag <id>`, `ascent verify <id>`, `ascent teachback <lab-id>` |
| Use a Cloud Stage | `ascent guardrails check`, `ascent lab up <id> --cloud`, `ascent teardown` |
| Do a Deliverable or a drill | `ascent deliver <id>`, and optionally `ascent review <id>` |
| Test yourself | `ascent diagnostic`, `ascent boss <D1–D8>`, `ascent sim` |
| Move to the next Domain | `ascent release next` |
| Check your setup | `ascent doctor` |
| See where you stand | `ascent status`, `ascent portfolio` |
| Back up your progress | `ascent progress export`, `ascent progress import <file>` |

The game's rules, each with its ID, are in [`docs/engine/rules.md`](docs/engine/rules.md).

**Plain output.** Add `--plain`, set `NO_COLOR`, or run `ascent config plainMode true` for output without color or animation, which suits screen readers and logs. Results always say PASS, FAIL, WARN or INFO in words.

**Your work and your privacy.**
- Your progress stays on your machine in `.ascent/`, and your Lab work in `my-work/` ([ADR 0007](docs/adr/0007-learner-workspace-repository.md)). Git ignores both.
- There's no telemetry. The Engine only goes online when you ask it to: `sync` (GitHub), a refreshed cost estimate (Azure Retail Prices), Cloud Stage commands, and the optional AI reviewer at the endpoint you configure.

## How this was built
Content and code are drafted with AI assistance (Claude), following an AI-driven development lifecycle. Every claim in the curriculum cites a source and is checked by a human learner. The process records are kept in a private repository.

## Licenses
- **Code**: MIT. See [`LICENSE`](LICENSE).
- **Content** (curriculum, questions, documentation): CC BY-NC-SA 4.0. See [`LICENSE-CONTENT`](LICENSE-CONTENT).

## Disclaimer
CSSLP® is a registered certification mark of ISC2, Inc. AppSec Ascent is an independent project. It is not affiliated with, endorsed by, sponsored by, or reviewed by ISC2. It contains no ISC2 exam questions: every practice question is original and written from the public exam outline.

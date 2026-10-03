# AppSec Ascent

> **Work in progress.** Season 1 is still being built, and nothing here is ready to study from yet.

**An unofficial study companion for the ISC2 CSSLP® exam.**

AppSec Ascent teaches the secure software lifecycle by having you harden one deliberately vulnerable telehealth system, built with .NET and Azure, across all eight exam domains. A gamified command-line tool, `ascent`, runs your daily review, checks your labs, and tracks your progress up a ladder of AppSec career ranks.

## Status
Planning is complete. The repository foundation and curriculum framework are now being built:
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

## How this was built
Content and code are drafted with AI assistance (Claude), following an AI-driven development lifecycle. Every claim in the curriculum cites a source and is checked by a human learner. The process records are kept in a private repository.

## Licenses
- **Code**: MIT. See [`LICENSE`](LICENSE).
- **Content** (curriculum, questions, documentation): CC BY-NC-SA 4.0. See [`LICENSE-CONTENT`](LICENSE-CONTENT).

## Disclaimer
CSSLP® is a registered certification mark of ISC2, Inc. AppSec Ascent is an independent project. It is not affiliated with, endorsed by, sponsored by, or reviewed by ISC2. It contains no ISC2 exam questions: every practice question is original and written from the public exam outline.

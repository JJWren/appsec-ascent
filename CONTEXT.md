# AppSec Ascent

AppSec Ascent is an unofficial, gamified study program for the ISC2 CSSLP® exam. A Learner works through the exam's Objectives by hardening one deliberately vulnerable telehealth product. This file is the shared language for the project.

## Language

### Learning structure

**Program**:
The whole AppSec Ascent package: the Curriculum, the Labs and the Engine.
_Avoid_: course, app

**Curriculum**:
The Quests' lessons plus the Question Bank.
_Avoid_: content, material

**Domain**:
One of ISC2's eight CSSLP exam domains.
_Avoid_: chapter, module

**Objective**:
A numbered sub-item of a Domain in the exam outline, such as 4.4.
_Avoid_: topic, sub-domain

**Quest**:
The unit of study for one Objective: a lesson, its Labs, Drills or Deliverables, and its questions.
_Avoid_: task, lesson, module

**AI Lens**:
The AI-security part of a Domain, mapped to ISC2's AI exam guidance. It is exam material.
_Avoid_: AI track, AI module

**Deep Dive**:
An optional hands-on extension of a Quest that goes beyond exam depth, such as adversarial ML.
_Avoid_: bonus lab, extra credit

**Orientation**:
The opening unit: setup, a fundamentals refresher, and the Diagnostic.
_Avoid_: Module 0, intro

**Diagnostic**:
The placement test in Orientation that shifts study time toward weak Domains.
_Avoid_: pre-test

**Capstone**:
The closing unit, where the Learner assesses the whole Throughline System end to end.
_Avoid_: final project

**Season 2**:
The post-exam continuation. It holds skipped Deep Dives and the polish for other Learners.
_Avoid_: phase 2, v2

### Practice

**Lab**:
A hands-on .NET/Azure exercise whose success is checked by automated tests.
_Avoid_: exercise, assignment

**Lab Brief**:
The public, spoiler-free description of a Lab: its Objective, scenario and acceptance criteria.
_Avoid_: spec, instructions

**Local Stage**:
The part of a Lab that runs on the Learner's own machine at $0, using emulators.
_Avoid_: dev environment

**Cloud Stage**:
The part of a Lab that deploys to the Learner's own Azure subscription.
_Avoid_: environment, sandbox

**Deliverable**:
A non-code practical exercise that produces a security work product, such as a threat model, risk register or incident playbook.
_Avoid_: paper exercise, document

**Throughline System**:
The single fictional telehealth product, deliberately vulnerable, that the Learner hardens across every Domain.
_Avoid_: sample app, demo app, vulnerable app

**Red → Blue → Explain**:
The three-step Lab loop. Red: exploit the vulnerability and capture the Flag. Blue: fix it until the security tests pass. Explain: write the Teach-back.
_Avoid_: attack/defend

**Flag**:
A secret string captured by exploiting a Lab's vulnerability. It proves the Red step is done.
_Avoid_: token, answer, key

**Teach-back**:
An explanation of at most 150 words, in the Learner's own words, written after a lesson.
_Avoid_: summary, notes

### Assessment

**Question Bank**:
The pool of original questions, each cited and tagged with its Objective. It feeds Stand-ups, Boss Fights and Simulations.
_Avoid_: test bank, quiz bank

**Stand-up**:
The daily ~10-minute spaced-repetition review that keeps the Learner's streak going.
_Avoid_: flashcards, daily quiz

**Boss Fight**:
The timed, exam-style test that closes a Domain. A score below 70% schedules a rematch; it never locks progress.
_Avoid_: quiz, domain exam

**Simulation**:
A full 125-question, 3-hour mock exam, drawn from a reserved pool that never appears in practice.
_Avoid_: practice exam, mock

### Game and people

**Learner**:
An experienced developer using the Program to pass the CSSLP and move into AppSec or DevSecOps.
_Avoid_: student, beginner, user

**Engine**:
The `ascent` tool that runs Quests, verifies Labs and keeps score.
_Avoid_: platform, app

**Rank**:
The Learner's level on the AppSec career ladder: Developer → Security Champion → AppSec Engineer → Senior → Principal.
_Avoid_: level, tier

**Teardown Bonus**:
XP for deleting Cloud Stage resources promptly. Overruns cost XP.
_Avoid_: cleanup reward

**Content Bug**:
An error a Learner finds in the Curriculum, filed as an issue and rewarded with XP.
_Avoid_: typo, issue

**Sealed**:
Describes build artifacts the builder deliberately doesn't review, to avoid spoilers: vulnerable-code specifics, fixes, Flags, reference answers, and the Question Bank. CI proves each one works, and the Learner verifies it the first time they meet it while studying.
_Avoid_: hidden, secret

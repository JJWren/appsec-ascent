-- AppSec Ascent progress store, schema version 1.
-- Tables follow the U2 domain entities. Every instant is UTC text (yyyy-MM-ddTHH:mm:ss.fffZ).

CREATE TABLE meta (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
) STRICT;

CREATE TABLE profile (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
) STRICT;

-- XP ledger: (kind, ref_id) is unique, so every award is idempotent (XP-01, REL-U2-04).
CREATE TABLE xp_events (
    id           INTEGER PRIMARY KEY,
    occurred_utc TEXT NOT NULL,
    kind         TEXT NOT NULL,
    ref_id       TEXT NOT NULL,
    points       INTEGER NOT NULL,
    bonus        INTEGER NOT NULL CHECK (bonus IN (0, 1)),
    UNIQUE (kind, ref_id)
) STRICT;

-- Ranks reached; Ranks never go down (RNK-03).
CREATE TABLE rank_history (
    rank        TEXT PRIMARY KEY,
    reached_utc TEXT NOT NULL
) STRICT;

CREATE TABLE standups (
    local_date     TEXT PRIMARY KEY,
    completed_utc  TEXT NOT NULL,
    items_reviewed INTEGER NOT NULL
) STRICT;

CREATE TABLE quest_progress (
    quest_id             TEXT PRIMARY KEY,
    status               TEXT NOT NULL CHECK (status IN ('NotStarted', 'InProgress', 'Complete')),
    lesson_completed_utc TEXT,
    completed_utc        TEXT
) STRICT;

CREATE TABLE teachbacks (
    ref_id     TEXT PRIMARY KEY,
    path       TEXT NOT NULL,
    word_count INTEGER NOT NULL,
    saved_utc  TEXT NOT NULL
) STRICT;

CREATE TABLE review_cards (
    item_id         TEXT PRIMARY KEY,
    objective_id    TEXT NOT NULL,
    exam_domain     TEXT NOT NULL,
    fsrs_state      TEXT NOT NULL,
    due_utc         TEXT NOT NULL,
    introduced_utc  TEXT NOT NULL,
    last_review_utc TEXT,
    reps            INTEGER NOT NULL DEFAULT 0,
    lapses          INTEGER NOT NULL DEFAULT 0,
    suspended       INTEGER NOT NULL DEFAULT 0 CHECK (suspended IN (0, 1))
) STRICT;

CREATE INDEX ix_review_cards_due ON review_cards (suspended, due_utc);

CREATE TABLE reviews (
    id           INTEGER PRIMARY KEY,
    item_id      TEXT NOT NULL,
    reviewed_utc TEXT NOT NULL,
    context      TEXT NOT NULL CHECK (context IN ('StandUp', 'Quest', 'BossFight')),
    correct      INTEGER NOT NULL CHECK (correct IN (0, 1)),
    rating       TEXT NOT NULL CHECK (rating IN ('Good', 'Again')),
    elapsed_ms   INTEGER NOT NULL
) STRICT;

CREATE TABLE diagnostic (
    id          INTEGER PRIMARY KEY,
    started_utc TEXT NOT NULL,
    taken_utc   TEXT,
    item_ids    TEXT NOT NULL,
    per_domain  TEXT,
    plan        TEXT
) STRICT;

CREATE TABLE boss_attempts (
    id            INTEGER PRIMARY KEY,
    exam_domain   TEXT NOT NULL,
    kind          TEXT NOT NULL CHECK (kind IN ('First', 'Rematch')),
    started_utc   TEXT NOT NULL,
    deadline_utc  TEXT NOT NULL,
    finished_utc  TEXT,
    item_ids      TEXT NOT NULL,
    correct       INTEGER,
    total         INTEGER,
    score_percent INTEGER,
    passed        INTEGER CHECK (passed IN (0, 1))
) STRICT;

-- At most one unfinished Boss Fight (P17).
CREATE UNIQUE INDEX ux_boss_attempts_active ON boss_attempts (finished_utc IS NULL) WHERE finished_utc IS NULL;

CREATE TABLE simulation_attempts (
    id            INTEGER PRIMARY KEY,
    form          TEXT NOT NULL CHECK (form IN ('A', 'B')),
    started_utc   TEXT NOT NULL,
    deadline_utc  TEXT NOT NULL,
    status        TEXT NOT NULL CHECK (status IN ('InProgress', 'Finished', 'Expired')),
    item_ids      TEXT NOT NULL,
    finished_utc  TEXT,
    correct       INTEGER,
    total         INTEGER,
    score_percent INTEGER,
    seen_form     INTEGER NOT NULL CHECK (seen_form IN (0, 1))
) STRICT;

-- At most one Simulation in progress (P17).
CREATE UNIQUE INDEX ux_simulation_attempts_active ON simulation_attempts (status) WHERE status = 'InProgress';

-- Write-ahead answers: one row per answer, saved as it is given (P15, REL-U2-02).
CREATE TABLE attempt_answers (
    attempt_kind TEXT NOT NULL CHECK (attempt_kind IN ('Diagnostic', 'BossFight', 'Simulation')),
    attempt_id   INTEGER NOT NULL,
    item_id      TEXT NOT NULL,
    answer       TEXT NOT NULL,
    answered_utc TEXT NOT NULL,
    PRIMARY KEY (attempt_kind, attempt_id, item_id)
) STRICT;

CREATE TABLE lab_state (
    lab_id            TEXT PRIMARY KEY,
    state             TEXT NOT NULL CHECK (state IN ('NotStarted', 'Started', 'FlagCaptured', 'Fixed', 'Explained')),
    flag_salt         BLOB,
    flag_hash         BLOB,
    started_utc       TEXT,
    flag_captured_utc TEXT,
    fixed_utc         TEXT,
    explained_utc     TEXT,
    verify_attempts   INTEGER NOT NULL DEFAULT 0,
    wrong_flag_times  TEXT NOT NULL DEFAULT '[]'
) STRICT;

CREATE TABLE cloud_deployments (
    id             INTEGER PRIMARY KEY,
    lab_id         TEXT NOT NULL,
    resource_group TEXT NOT NULL,
    deployed_utc   TEXT NOT NULL,
    expires_utc    TEXT NOT NULL,
    torn_down_utc  TEXT,
    outcome        TEXT NOT NULL DEFAULT 'None' CHECK (outcome IN ('Bonus', 'Penalty', 'None'))
) STRICT;

CREATE TABLE releases (
    exam_domain  TEXT PRIMARY KEY,
    unlocked_utc TEXT NOT NULL,
    skipped      INTEGER NOT NULL CHECK (skipped IN (0, 1))
) STRICT;

CREATE TABLE season2 (
    item_id   TEXT PRIMARY KEY,
    reason    TEXT NOT NULL CHECK (reason IN ('SkippedDeepDive', 'SkippedLab')),
    added_utc TEXT NOT NULL
) STRICT;

CREATE TABLE deliverables (
    dlv_id                 TEXT PRIMARY KEY,
    work_path              TEXT NOT NULL,
    validated_utc          TEXT,
    self_score_percent     INTEGER,
    submitted_utc          TEXT,
    reference_released_utc TEXT,
    published              INTEGER NOT NULL DEFAULT 0 CHECK (published IN (0, 1))
) STRICT;

-- Every key release is recorded once (SEAL-05).
CREATE TABLE key_releases (
    item_id      TEXT NOT NULL,
    tier         TEXT NOT NULL,
    released_utc TEXT NOT NULL,
    reason       TEXT NOT NULL CHECK (reason IN ('LabStarted', 'FlagVerified', 'DeliverableSubmitted', 'Served', 'SimulationStarted', 'ReleaseUnlocked')),
    PRIMARY KEY (item_id, tier)
) STRICT;

CREATE TABLE content_bugs (
    issue_number  INTEGER PRIMARY KEY,
    item_id       TEXT NOT NULL,
    confirmed_utc TEXT NOT NULL
) STRICT;

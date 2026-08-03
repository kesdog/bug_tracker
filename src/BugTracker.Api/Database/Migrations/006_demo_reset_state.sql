CREATE TABLE demo_reset_state (
    singleton_id INTEGER PRIMARY KEY CHECK (singleton_id = 1),
    generation INTEGER NOT NULL DEFAULT 0 CHECK (generation >= 0),
    last_reset_at TEXT,
    last_environment TEXT
);

INSERT INTO demo_reset_state (singleton_id, generation)
VALUES (1, 0);

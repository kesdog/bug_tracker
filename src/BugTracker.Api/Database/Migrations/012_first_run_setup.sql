CREATE TABLE first_run_setup (
    singleton_id INTEGER PRIMARY KEY CHECK (singleton_id = 1),
    phase TEXT NOT NULL CHECK (phase IN ('not_bootstrapped', 'password_change_required', 'project_required', 'ttl_required', 'complete')),
    root_admin_user_id TEXT,
    first_project_id TEXT,
    human_token_ttl_minutes INTEGER,
    agent_oath_ttl_days INTEGER,
    updated_at TEXT NOT NULL DEFAULT (datetime('now')),
    FOREIGN KEY (root_admin_user_id) REFERENCES users(user_id),
    FOREIGN KEY (first_project_id) REFERENCES projects(project_id)
);

INSERT INTO first_run_setup (singleton_id, phase, updated_at)
SELECT 1,
       CASE WHEN EXISTS (SELECT 1 FROM users WHERE role = 'admin' AND user_type = 'human' AND is_active = 1)
            THEN 'complete'
            ELSE 'not_bootstrapped'
       END,
       datetime('now')
WHERE NOT EXISTS (SELECT 1 FROM first_run_setup WHERE singleton_id = 1);

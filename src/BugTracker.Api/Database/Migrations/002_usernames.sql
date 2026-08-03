ALTER TABLE users ADD COLUMN username TEXT NOT NULL DEFAULT '';

-- Assign short deterministic placeholders to existing rows. New users receive
-- readable email-derived usernames and admins can update either form.
WITH ranked AS (
    SELECT user_id, row_number() OVER (ORDER BY lower(email), user_id) AS position
    FROM users
)
UPDATE users
SET username = 'user_' || printf('%06d', (
    SELECT position FROM ranked WHERE ranked.user_id = users.user_id
));

CREATE UNIQUE INDEX ux_users_username_nocase ON users (username COLLATE NOCASE);

CREATE TRIGGER trg_users_username_required_insert
BEFORE INSERT ON users
FOR EACH ROW WHEN length(trim(NEW.username)) = 0
BEGIN
    SELECT RAISE(ABORT, 'username is required');
END;

CREATE TRIGGER trg_users_username_required_update
BEFORE UPDATE OF username ON users
FOR EACH ROW WHEN length(trim(NEW.username)) = 0
BEGIN
    SELECT RAISE(ABORT, 'username is required');
END;

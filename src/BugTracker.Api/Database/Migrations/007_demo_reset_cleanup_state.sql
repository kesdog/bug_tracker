ALTER TABLE demo_reset_state
ADD COLUMN cleanup_pending INTEGER NOT NULL DEFAULT 0 CHECK (cleanup_pending IN (0, 1));

ALTER TABLE demo_reset_state
ADD COLUMN wal_checkpoint_completed INTEGER NOT NULL DEFAULT 1 CHECK (wal_checkpoint_completed IN (0, 1));

ALTER TABLE demo_reset_state
ADD COLUMN audit_file_cleanup_completed INTEGER NOT NULL DEFAULT 1 CHECK (audit_file_cleanup_completed IN (0, 1));

ALTER TABLE users ADD COLUMN IF NOT EXISTS full_name VARCHAR(100);
ALTER TABLE users ADD COLUMN IF NOT EXISTS is_verified BOOLEAN NOT NULL DEFAULT FALSE;

UPDATE users
SET is_verified = TRUE
WHERE status = 'Approved' AND is_verified = FALSE;

CREATE INDEX IF NOT EXISTS idx_site_invites_email
    ON site_invites (email);

CREATE TABLE IF NOT EXISTS site_users (
    site_id UUID NOT NULL REFERENCES sites(id) ON DELETE CASCADE,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (site_id, user_id)
);

CREATE INDEX IF NOT EXISTS idx_site_users_user_id
    ON site_users (user_id);
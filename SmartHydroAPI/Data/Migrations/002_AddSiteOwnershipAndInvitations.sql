-- ==============================================================================
-- 002_AddSiteOwnershipAndInvitations.sql
-- Site ownership and Worker invitation support
-- ==============================================================================

ALTER TABLE sites
    ADD COLUMN IF NOT EXISTS owner_email VARCHAR(255);

CREATE INDEX IF NOT EXISTS idx_sites_owner_email
    ON sites (owner_email);

ALTER TABLE structures
    ADD COLUMN IF NOT EXISTS owner_email VARCHAR(255);

UPDATE sites s
SET owner_email = owners.owner_email
FROM (
    SELECT site_id, MIN(owner_email) AS owner_email
    FROM structures
    WHERE site_id IS NOT NULL
      AND owner_email IS NOT NULL
      AND owner_email <> ''
    GROUP BY site_id
    HAVING COUNT(DISTINCT owner_email) = 1
) owners
WHERE s.id = owners.site_id
  AND (s.owner_email IS NULL OR s.owner_email = '');

CREATE TABLE IF NOT EXISTS site_invites (
    id BIGSERIAL PRIMARY KEY,
    site_id UUID NOT NULL REFERENCES sites(id) ON DELETE CASCADE,
    email VARCHAR(255) NOT NULL,
    token_hash VARCHAR(512) NOT NULL,
    code_hash VARCHAR(512) NOT NULL,
    expires_at TIMESTAMPTZ NOT NULL,
    attempts INTEGER NOT NULL DEFAULT 0,
    created_by_email VARCHAR(255) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_site_invites_site_email
    ON site_invites (site_id, email);

CREATE INDEX IF NOT EXISTS idx_site_invites_expires_at
    ON site_invites (expires_at);

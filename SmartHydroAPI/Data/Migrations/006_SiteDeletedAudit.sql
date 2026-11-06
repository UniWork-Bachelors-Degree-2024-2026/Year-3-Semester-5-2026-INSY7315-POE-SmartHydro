-- ==============================================================================
-- 006_SiteDeletedAudit.sql
-- Records when a site was deleted and by whom, so deleted sites can be listed and
-- restored together with the structures that were deleted along with them
-- ==============================================================================

ALTER TABLE sites
    ADD COLUMN IF NOT EXISTS deleted_at TIMESTAMPTZ;

ALTER TABLE sites
    ADD COLUMN IF NOT EXISTS deleted_by_email VARCHAR(255);

-- Sites deleted before this migration take the time their structures were deleted with them
UPDATE sites s
SET deleted_at = d.deleted_at
FROM (
    SELECT site_id, MAX(deleted_at) AS deleted_at
    FROM structures
    WHERE is_deleted = TRUE
      AND deleted_at IS NOT NULL
      AND site_id IS NOT NULL
    GROUP BY site_id
) d
WHERE s.id = d.site_id
  AND s.is_deleted = TRUE
  AND s.deleted_at IS NULL;

CREATE INDEX IF NOT EXISTS idx_sites_is_deleted_deleted_at
    ON sites (is_deleted, deleted_at);

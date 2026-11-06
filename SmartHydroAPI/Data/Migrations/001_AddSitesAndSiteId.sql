-- 001_AddSitesAndSiteId.sql
-- SmartHydro Multi-Site Architecture & Structure Migration

-- 1. Create Sites Table
CREATE TABLE IF NOT EXISTS sites (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(60) NOT NULL,
    location VARCHAR(100),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE
);

-- Index for unique site name lookups among active sites
CREATE INDEX IF NOT EXISTS idx_sites_name_deleted ON sites (name, is_deleted);

-- 2. Add Site ID Column to Structures Table
DO $$ 
BEGIN 
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'structures' AND column_name = 'site_id'
    ) THEN 
        ALTER TABLE structures 
        ADD COLUMN site_id UUID REFERENCES sites(id) ON DELETE SET NULL;
    END IF; 
END $$;

-- 3. Create Performance Index on structures.site_id
CREATE INDEX IF NOT EXISTS idx_structures_site_id ON structures (site_id);
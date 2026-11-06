ALTER TABLE devices ADD COLUMN IF NOT EXISTS reported_mode integer;
ALTER TABLE devices ADD COLUMN IF NOT EXISTS reported_preferred_mode integer;
ALTER TABLE devices ADD COLUMN IF NOT EXISTS reported_mode_reason integer;
ALTER TABLE devices ADD COLUMN IF NOT EXISTS mode_reported_at timestamptz;
ALTER TABLE devices ADD COLUMN IF NOT EXISTS last_telemetry_transport text;
ALTER TABLE devices ADD COLUMN IF NOT EXISTS mode_request_confirmed_at timestamptz;

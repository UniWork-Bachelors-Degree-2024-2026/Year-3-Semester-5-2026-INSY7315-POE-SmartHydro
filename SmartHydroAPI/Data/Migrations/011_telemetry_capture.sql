ALTER TABLE telemetry_realtime ADD COLUMN IF NOT EXISTS sample_sequence bigint;
ALTER TABLE telemetry_realtime ADD COLUMN IF NOT EXISTS received_at timestamptz;
ALTER TABLE telemetry_realtime ADD COLUMN IF NOT EXISTS capture_time_uncertain boolean NOT NULL DEFAULT false;
ALTER TABLE telemetry_realtime ADD COLUMN IF NOT EXISTS relay_bitmask integer;
ALTER TABLE telemetry_realtime ADD COLUMN IF NOT EXISTS operating_mode integer;
CREATE UNIQUE INDEX IF NOT EXISTS idx_telemetry_device_sequence ON telemetry_realtime(device_mac, sample_sequence);
CREATE TABLE IF NOT EXISTS telemetry_receipts (
    device_mac varchar(17) NOT NULL,
    sample_sequence bigint NOT NULL,
    received_at timestamptz NOT NULL,
    PRIMARY KEY(device_mac, sample_sequence)
);
CREATE INDEX IF NOT EXISTS idx_telemetry_receipt_time ON telemetry_receipts(received_at);
ALTER TABLE telemetry_daily_history ADD COLUMN IF NOT EXISTS sum_ph numeric;
ALTER TABLE telemetry_daily_history ADD COLUMN IF NOT EXISTS count_ph integer;
ALTER TABLE telemetry_daily_history ADD COLUMN IF NOT EXISTS sum_ec numeric;
ALTER TABLE telemetry_daily_history ADD COLUMN IF NOT EXISTS count_ec integer;
ALTER TABLE telemetry_daily_history ADD COLUMN IF NOT EXISTS sum_light numeric;
ALTER TABLE telemetry_daily_history ADD COLUMN IF NOT EXISTS count_light integer;
ALTER TABLE telemetry_daily_history ADD COLUMN IF NOT EXISTS sum_temperature numeric;
ALTER TABLE telemetry_daily_history ADD COLUMN IF NOT EXISTS count_temperature integer;
ALTER TABLE telemetry_daily_history ADD COLUMN IF NOT EXISTS sum_humidity numeric;
ALTER TABLE telemetry_daily_history ADD COLUMN IF NOT EXISTS count_humidity integer;

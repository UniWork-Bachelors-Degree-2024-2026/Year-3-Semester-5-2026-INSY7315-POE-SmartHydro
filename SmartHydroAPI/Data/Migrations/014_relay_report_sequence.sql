-- Run before deploying the API that reads this column. Existing rows stay compatible.
ALTER TABLE actuator_states ADD COLUMN IF NOT EXISTS reported_sequence bigint;

ALTER TABLE users ADD COLUMN IF NOT EXISTS status VARCHAR(20) NOT NULL DEFAULT 'Approved';
ALTER TABLE users ADD COLUMN IF NOT EXISTS farm_name VARCHAR(60);

UPDATE users
SET status = 'PendingApproval'
WHERE status = 'Pending';

CREATE INDEX IF NOT EXISTS idx_users_status_role
    ON users (status, role);
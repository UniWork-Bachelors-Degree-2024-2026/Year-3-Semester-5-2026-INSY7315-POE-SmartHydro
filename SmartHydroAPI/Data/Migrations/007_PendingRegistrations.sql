CREATE TABLE IF NOT EXISTS pending_registrations (
    id BIGSERIAL PRIMARY KEY,
    email VARCHAR(255) NOT NULL,
    password_hash VARCHAR(255) NOT NULL,
    farm_name VARCHAR(60) NOT NULL,
    verification_token_hash VARCHAR(255) NOT NULL,
    code_hash VARCHAR(255) NOT NULL,
    expires_at TIMESTAMPTZ NOT NULL,
    attempts INTEGER NOT NULL DEFAULT 0,
    send_count INTEGER NOT NULL DEFAULT 0,
    last_sent_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_pending_registrations_email
    ON pending_registrations (email);

CREATE INDEX IF NOT EXISTS idx_pending_registrations_expires_at
    ON pending_registrations (expires_at);

CREATE TABLE IF NOT EXISTS email_rate_limits (
    limit_key VARCHAR(320) PRIMARY KEY,
    hits INTEGER NOT NULL DEFAULT 0,
    window_start TIMESTAMPTZ NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_email_rate_limits_window_start
    ON email_rate_limits (window_start);
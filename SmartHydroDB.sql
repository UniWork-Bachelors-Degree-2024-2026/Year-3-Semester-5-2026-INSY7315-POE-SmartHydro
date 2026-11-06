-- 1. STRUCTURES (Physical Locations / Vertical Towers / Fodder Trays)
CREATE TABLE IF NOT EXISTS structures (
    id SERIAL PRIMARY KEY,
    name VARCHAR(60) NOT NULL,
    structure_type VARCHAR(20) NOT NULL,       -- Strictly 'VERTICAL_TOWER' or 'FODDER_TRAY'
    label_colour VARCHAR(20) NOT NULL,         -- e.g. '#2ECC71' (Green) or '#E67E22' (Orange)
    location_text VARCHAR(200) NULL,           
    latitude NUMERIC(9, 6) NULL,               
    longitude NUMERIC(9, 6) NULL,              
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- 2. DEVICE REGISTRY (Hardware Microcontroller Nodes)
CREATE TABLE IF NOT EXISTS devices (
    mac_address VARCHAR(17) PRIMARY KEY,
    structure_id INTEGER NULL REFERENCES structures(id) ON DELETE SET NULL,
    device_name VARCHAR(100) NOT NULL,
    device_type VARCHAR(50) NOT NULL,          -- 'SENSOR_NODE', 'CAMERA_NODE', 'CONTROLLER_NODE'
    local_ip VARCHAR(45) NULL,
    last_seen_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_devices_structure
    ON devices(structure_id);

-- 3. REAL-TIME TELEMETRY (High-Frequency Sensor Streams & Edge ML)
CREATE TABLE IF NOT EXISTS telemetry_realtime (
    id BIGSERIAL PRIMARY KEY,
    device_mac VARCHAR(17) NOT NULL REFERENCES devices(mac_address) ON DELETE CASCADE,
    ph_level NUMERIC(4, 2) NOT NULL,
    ec_ms NUMERIC(4, 2) NOT NULL,              -- Electrical Conductivity (mS/cm), calibrated from edge ADC
    light_lux NUMERIC(7, 1) NOT NULL,
    water_level_raw INTEGER NOT NULL,          -- 12-bit ADC (0-4095)
    water_level_detected BOOLEAN NOT NULL DEFAULT FALSE,
    temperature_c NUMERIC(5, 2) NOT NULL,
    humidity_percent NUMERIC(5, 2) NOT NULL,
    
    -- Edge ML Random Forest Predictions
    ml_ph_prediction INTEGER NOT NULL DEFAULT 0,         -- 0 = Optimal, 1 = Dose pH Up, 2 = Dose pH Down
    ml_ec_prediction INTEGER NOT NULL DEFAULT 0,         -- 0 = Optimal, 1 = Dose Nutrient Up, 2 = Dose Dilution Water
    ml_temp_prediction INTEGER NOT NULL DEFAULT 0,       -- 0 = Normal, 1 = High Temp (Circulation Fan On)
    ml_humidity_prediction INTEGER NOT NULL DEFAULT 0,   -- 0 = Normal, 1 = High Humidity (Extractor Fan On)
    
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_telemetry_realtime_mac_time
    ON telemetry_realtime(device_mac, recorded_at DESC);

-- 4. DAILY HISTORICAL ROLLUP (Aggregated Daily Metrics)
CREATE TABLE IF NOT EXISTS telemetry_daily_history (
    id BIGSERIAL PRIMARY KEY,
    device_mac VARCHAR(17) NOT NULL REFERENCES devices(mac_address) ON DELETE CASCADE,
    summary_date DATE NOT NULL,
    avg_ph NUMERIC(4, 2) NOT NULL,
    avg_ec NUMERIC(4, 2) NOT NULL,              -- Daily average Electrical Conductivity (mS/cm)
    avg_light NUMERIC(7, 1) NOT NULL,
    avg_temperature_c NUMERIC(5, 2) NOT NULL,
    avg_humidity_percent NUMERIC(5, 2) NOT NULL,
    total_samples INTEGER NOT NULL,
    calculated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT uq_daily_history_mac_date UNIQUE (device_mac, summary_date)
);

-- 5. ACTUATOR STATES (Current Relay States, 1 Row Per Device)
CREATE TABLE IF NOT EXISTS actuator_states (
    device_mac VARCHAR(17) PRIMARY KEY REFERENCES devices(mac_address) ON DELETE CASCADE,
    -- 230V Appliance Relay Module
    grow_light BOOLEAN NOT NULL DEFAULT FALSE,
    circulation_fan BOOLEAN NOT NULL DEFAULT FALSE,
    main_water_pump BOOLEAN NOT NULL DEFAULT FALSE,
    extractor_fan BOOLEAN NOT NULL DEFAULT FALSE,
    -- 12V Dosing Pump Relay Module
    ph_up_pump BOOLEAN NOT NULL DEFAULT FALSE,
    ph_down_pump BOOLEAN NOT NULL DEFAULT FALSE,
    nutrient_up_pump BOOLEAN NOT NULL DEFAULT FALSE,
    dilution_down_pump BOOLEAN NOT NULL DEFAULT FALSE,
    spray_system BOOLEAN NOT NULL DEFAULT FALSE,         -- Specialized for Fodder Trays
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- 6. ACTUATOR HISTORY (Audit Trail / Activity Log)
CREATE TABLE IF NOT EXISTS actuator_history (
    id BIGSERIAL PRIMARY KEY,
    device_mac VARCHAR(17) NOT NULL REFERENCES devices(mac_address) ON DELETE CASCADE,
    actuator_name VARCHAR(50) NOT NULL,        
    new_state BOOLEAN NOT NULL,
    changed_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    changed_by VARCHAR(50) NOT NULL DEFAULT 'System'
);

CREATE INDEX IF NOT EXISTS idx_actuator_history_mac_time
    ON actuator_history(device_mac, changed_at DESC);

-- 7. CAMERA FRAME STORAGE (Binary Snapshot Storage & Plant Gallery)
CREATE TABLE IF NOT EXISTS camera_captures (
    id BIGSERIAL PRIMARY KEY,
    device_mac VARCHAR(17) NULL REFERENCES devices(mac_address) ON DELETE SET NULL,
    structure_id INTEGER NULL REFERENCES structures(id) ON DELETE SET NULL,
    image_data BYTEA NOT NULL,
    content_type VARCHAR(50) NOT NULL DEFAULT 'image/jpeg',
    image_size_bytes INTEGER NOT NULL,
    plant VARCHAR(100) NULL,                   
    label VARCHAR(100) NULL,                   
    camera_label VARCHAR(50) NULL,             
    is_favourite BOOLEAN NOT NULL DEFAULT FALSE,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    deleted_at TIMESTAMPTZ NULL,
    deleted_by_email VARCHAR(255) NULL,
    captured_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_camera_captures_structure_time
    ON camera_captures(structure_id, captured_at DESC);

CREATE INDEX IF NOT EXISTS idx_camera_captures_mac_time
    ON camera_captures(device_mac, captured_at DESC);

-- Standard column index for date range queries (No functions, 100% pure B-Tree)
CREATE INDEX IF NOT EXISTS idx_camera_captures_captured_at
    ON camera_captures(captured_at DESC);

-- 8. CAMERA COMMANDS (Asynchronous Hardware Trigger Queue)
CREATE TABLE IF NOT EXISTS camera_commands (
    id BIGSERIAL PRIMARY KEY,
    device_mac VARCHAR(17) NOT NULL REFERENCES devices(mac_address) ON DELETE CASCADE,
    is_pending BOOLEAN NOT NULL DEFAULT TRUE,
    requested_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    completed_at TIMESTAMPTZ NULL
);

CREATE INDEX IF NOT EXISTS idx_camera_commands_pending
    ON camera_commands(device_mac, is_pending);

-- 9. ALERTS (System Anomaly & Threshold Warnings)
CREATE TABLE IF NOT EXISTS alerts (
    id BIGSERIAL PRIMARY KEY,
    structure_id INTEGER NOT NULL REFERENCES structures(id) ON DELETE CASCADE,
    device_mac VARCHAR(17) NULL REFERENCES devices(mac_address) ON DELETE SET NULL,
    component VARCHAR(50) NOT NULL,            
    severity VARCHAR(20) NOT NULL,             -- 'INFO', 'WARNING', 'CRITICAL'
    value VARCHAR(50) NULL,                    
    detail VARCHAR(300) NULL,
    occurred_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    is_resolved BOOLEAN NOT NULL DEFAULT FALSE,
    cleared_by VARCHAR(50) NULL,
    resolved_at TIMESTAMPTZ NULL
);

CREATE INDEX IF NOT EXISTS idx_alerts_structure_resolved
    ON alerts(structure_id, is_resolved);

-- 10. THRESHOLDS (Safe Environmental Ranges for VERTICAL_TOWER and FODDER_TRAY)
CREATE TABLE IF NOT EXISTS thresholds (
    id SERIAL PRIMARY KEY,
    structure_type VARCHAR(20) NOT NULL,       -- 'VERTICAL_TOWER' or 'FODDER_TRAY'
    sensor_type VARCHAR(30) NOT NULL,          -- 'PH', 'EC', 'TEMPERATURE', 'HUMIDITY'
    min_value NUMERIC(7, 2) NOT NULL,
    max_value NUMERIC(7, 2) NOT NULL,
    CONSTRAINT uq_threshold_type_sensor UNIQUE (structure_type, sensor_type)
);



-- MIGRATION: OFFLINE MODE, STRUCTURE TYPES, AND DOSING QUEUE

-- 1. Extend Devices table with offline provisioning and structure profile
ALTER TABLE devices ADD COLUMN IF NOT EXISTS structure_type VARCHAR(50) NULL;
ALTER TABLE devices ADD COLUMN IF NOT EXISTS is_offline_mode BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE devices ADD COLUMN IF NOT EXISTS pending_offline_transition BOOLEAN NOT NULL DEFAULT FALSE;

CREATE INDEX IF NOT EXISTS idx_devices_structure_type ON devices(structure_type);

-- 2. Dosing Commands Queue (One-shot volumetric peristaltic dosing)
CREATE TABLE IF NOT EXISTS dosing_commands (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    device_mac VARCHAR(17) NOT NULL REFERENCES devices(mac_address) ON DELETE CASCADE,
    dosing_type VARCHAR(30) NOT NULL,          -- 'PH_UP', 'PH_DOWN', 'NUTRIENT_UP', 'DILUTION_DOWN'
    target_volume_ml NUMERIC(6, 2) NOT NULL,   -- Volumetric requirement in mL
    calculated_duration_ms INTEGER NOT NULL,   -- e.g. 8000ms for 10mL (at 1.25 mL/s)
    status VARCHAR(20) NOT NULL DEFAULT 'PENDING', -- 'PENDING', 'DISPATCHED', 'COMPLETED', 'FAILED'
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    executed_at TIMESTAMPTZ NULL,
    issued_by VARCHAR(100) NULL
);

CREATE INDEX IF NOT EXISTS idx_dosing_commands_mac_status
    ON dosing_commands(device_mac, status);

-- 3. Extend Camera Captures table with soft-delete columns and date-picker index
ALTER TABLE camera_captures ADD COLUMN IF NOT EXISTS is_deleted BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE camera_captures ADD COLUMN IF NOT EXISTS deleted_at TIMESTAMPTZ NULL;
ALTER TABLE camera_captures ADD COLUMN IF NOT EXISTS deleted_by_email VARCHAR(255) NULL;

-- Standard column index for date range queries (No functions, 100% pure B-Tree)
CREATE INDEX IF NOT EXISTS idx_camera_captures_captured_at
    ON camera_captures(captured_at DESC);

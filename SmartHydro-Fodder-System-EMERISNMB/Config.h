#pragma once
#include <Arduino.h>
#include "SmartHydroFrame.h"

// ==============================================================================
// HARDWARE PIN DEFINITIONS (Heltec WiFi LoRa 32 V3 - ESP32-S3)
// ==============================================================================

// Heltec V3 Vext Power Control Pin (Must set LOW to power OLED & external sensors)
#define PIN_VEXT                 36
// Heltec V3 VBAT Divider Control Pin (Must set LOW to isolate GPIO 1 from 4.2V battery divider)
#define PIN_ADC_CTRL             37

// On-board SSD1306 0.96" OLED Display (Hardware I2C)
#define PIN_OLED_SDA             17  // ESP32-S3 SDA
#define PIN_OLED_SCL             18  // ESP32-S3 SCL
#define PIN_OLED_RST             21  // OLED Hardware Reset

// Semtech SX1262 LoRa Radio (Dedicated Hardware SPI Bus)
#define LORA_CS                  8   // NSS
#define LORA_SCK                 9   // SCK
#define LORA_MOSI                10  // MOSI
#define LORA_MISO                11  // MISO
#define LORA_RST                 12  // NRESET
#define LORA_BUSY                13  // BUSY
#define LORA_DIO1                14  // DIO1 Interrupt

// Long-Distance Sub-GHz LoRa RF Parameters (Matched to SenseLoRa 4G Gateway & ICASA 1% Duty-Cycle Limit)
#define LORA_FREQUENCY           868.1f   // 868.1 MHz Channel 0 (South Africa ICASA 868.0-868.6 MHz SRD Band)
#define LORA_BANDWIDTH           125.0f   // 125.0 kHz
#define LORA_SPREAD_FACTOR       7        // SF7 for fast ~60-150 ms Time-on-Air & <0.4% hourly duty cycle at 15s cadence
#define LORA_CODING_RATE         5        // 4/5 Coding Rate for Forward Error Correction
#define LORA_SYNC_WORD           0x12     // Private LoRa Sync Word (Matching SenseLoRa Gateway)
#define LORA_OUTPUT_POWER        5        // +5 dBm in DC-DC buck mode (~18 mA TX draw, prevents 3.3V LDO drop / POR reset while keeping >75 dB link margin; ICASA max is +14 dBm)
#define LORA_PREAMBLE_LEN        8
#define LORA_TCXO_VOLTAGE        1.8f     // Heltec V3 SX1262 1.8V TCXO supply

// Periodic LoRa Telemetry Cadence (15 seconds + instant event-driven TX on relay change)
#define LORA_REPORT_INTERVAL_MS  15000UL

// Analog Sensor Inputs (Single ADC1 Architecture on ESP32-S3 — Zero Wi-Fi Conflicts)
#define PIN_PH                   1   // ADC1_CH0 (Analog pH Sensor Probe)
#define PIN_EC                   2   // ADC1_CH1 (Analog TDS / EC Sensor Probe)
#define PIN_LIGHT                3   // ADC1_CH2 (Gravity DFR0026 V2.1 Ambient Light Sensor)
#define PIN_WATER                4   // ADC1_CH3 (Resistive Water Level Sensor)
#define WATER_THRESHOLD_HIGH     350 // 12-bit ADC threshold for OK status
#define WATER_THRESHOLD_LOW      200 // 12-bit ADC threshold for Low status
#define WATER_THRESHOLD_RAW      350

// Gravity DFR0026 V2.1 reports relative ADC signal percentage, not calibrated lux.

// Sensor Calibration & Tuning Offsets (TDS Sensor Acting as Calibrated EC Sensor in mS/cm)
#define PH_CALIBRATION_OFFSET    0.0f
#define TDS_SUPPLY_VOLTAGE       5.0f
#define EC_CALIBRATION_FACTOR    1.0f

// Digital Sensors
#define PIN_DHT                  5   // GPIO 5 for DHT22 Temperature & Humidity Sensor

// Relay Module 1: 230V AC Mains Appliances (Active-LOW: LOW = ON, HIGH = OFF)
#define PIN_RELAY_SPRAY          38  // Header J3 Pin 11  // Relay 1: Fine Misting Spray Solenoid/Pump (`s`)
#define PIN_RELAY_FAN_CIRC       39  // Header J3 Pin 10  // Relay 2: Climate Circulation Fan (`fc`)
#define PIN_RELAY_FAN_EXT        40  // Header J3 Pin 9   // Relay 3: Climate Extractor Fan (`fe`)
#define PIN_RELAY_LIGHT          41  // Header J3 Pin 8   // Relay 4: Grow Light LED (`l`)

// Relay Module 2: 12V DC Peristaltic Dosing Pumps (Active-LOW: LOW = ON, HIGH = OFF)
#define PIN_DOSE_PH_UP           42  // Header J3 Pin 7   // Relay 5: pH Up Peristaltic Pump (`pu`)
#define PIN_DOSE_PH_DOWN         6   // Header J3 Pin 17  // Relay 6: pH Down Peristaltic Pump (`pd`)
#define PIN_DOSE_EC_UP           7   // Header J3 Pin 18  // Relay 7: Nutrient Up Pump (`nu`)
#define PIN_DOSE_EC_DOWN         26  // Header J2 Pin 15  // Relay 8: Fresh Water Dilution Down Pump (`nd`)

// Relay Inrush & Brownout Suppression
#define RELAY_STAGGER_MS         150

// Fodder Sprouting Timing: Strict 10-second spray burst every 3 hours (10s ON / 2h 59m 50s OFF)
#define FODDER_SPRAY_DURATION_MS (10UL * 1000UL)
#define FODDER_SPRAY_INTERVAL_MS (3UL * 60UL * 60UL * 1000UL)

// Grow Light Photoperiod Schedule (Strict 16 hours ON / 8 hours OFF)
#define LIGHT_ON_DURATION_MS     (16UL * 60UL * 60UL * 1000UL)
#define LIGHT_CYCLE_MS           (24UL * 60UL * 60UL * 1000UL)

// ==============================================================================
// FODDER SEED ANTI-pH SHOCK & DUAL-TIER PERISTALTIC DOSING CONSTANTS
// ==============================================================================
// 1.25 mL/s flow rate -> 800 ms micro-pulse dispenses 1.0 mL safely in autonomous mode
#define MS_PER_ML                800UL
#define MICRO_DOSE_PULSE_MS      800UL
#define MANUAL_DEFAULT_DOSE_MS   800UL
#define MANUAL_MAX_DOSE_MS       20000UL
// Mandatory 3-minute (180,000 ms) reservoir diffusion lockout after autonomous doses
#define DIFFUSION_LOCKOUT_MS     (3UL * 60UL * 1000UL)
// Maximum 6 autonomous micro-doses per rolling 2-hour window
#define MAX_DAILY_DOSES          6
#define DAILY_WINDOW_MS          (2UL * 60UL * 60UL * 1000UL)

// Sensor Plausibility Interlock Bounds (For autonomous closed-loop dosing only)
#define PH_PLAUSIBLE_MIN         2.0f
#define PH_PLAUSIBLE_MAX         12.5f
#define EC_PLAUSIBLE_MIN         0.0f

// Safety Override Hold Duration (10 minutes in milliseconds)
#define MANUAL_OVERRIDE_HOLD_MS  (60UL * 60UL * 1000UL)

// Canonical Tri-Mode Operational States (0 = LoRa+BLE, 1 = Online Wi-Fi Cloud, 2 = Emergency SoftAP)
#undef MODE_LORA_BLE
#undef MODE_WIFI_CLOUD
#undef MODE_EMERGENCY_SOFTAP
#define MODE_LORA_BLE            0   // Primary Default: Wi-Fi OFF, 868.1 MHz LoRa + NimBLE 5.0 Active
#define MODE_WIFI_CLOUD          1   // Online Cloud Mode: Wi-Fi + SignalR WebSockets + LoRa + NimBLE Active
#define MODE_EMERGENCY_SOFTAP    2   // Emergency Local Hotspot (Triggered by 3s PRG/BOOT hold, MVC, or fallback)

// Bluetooth Low Energy (NimBLE 5.0) Definitions
#define BLE_DEVICE_NAME          "SmartHydro-Fodder"
#define BLE_SERVICE_UUID         "0000ff00-0000-1000-8000-00805f9b34fb"
#define BLE_CHAR_TELEMETRY_UUID  "0000ff01-0000-1000-8000-00805f9b34fb"
#define BLE_CHAR_COMMAND_UUID    "0000ff02-0000-1000-8000-00805f9b34fb"

// Manual BOOT / PRG Button (Heltec WiFi LoRa 32 V3 has PRG button on GPIO 0)
// CRITICAL: Must be GPIO 0, NEVER GPIO 9 (GPIO 9 is LORA_SCK to SX1262 SPI bus)
#define MANUAL_TRIGGER_PIN       0
#define BOOT_HOLD_TRIGGER_MS     3000UL

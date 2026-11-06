#pragma once
#include <Arduino.h>
#include "SmartHydroFrame.h"

// ==============================================================================
// HARDWARE PIN DEFINITIONS (DFRobot FireBeetle 2 ESP32-C6)
// ==============================================================================

// 1. Canonical Operational Mode Definitions (Persisted in NVS Flash & Controllable via MVC / BOOT Button)
//    - MODE_LORA_BLE (0): Primary Default! Zero Wi-Fi, 868.1 MHz LoRa Bridge + NimBLE 5.0
//    - MODE_WIFI_CLOUD (1): Enterprise/Cloud Wi-Fi + SignalR WebSockets + LoRa + NimBLE Active
//    - MODE_EMERGENCY_SOFTAP (2): Local Farm Wi-Fi Hotspot + HTTP Server + LoRa + BLE
#undef MODE_LORA_BLE
#undef MODE_WIFI_CLOUD
#undef MODE_EMERGENCY_SOFTAP
#define MODE_LORA_BLE           0
#define MODE_WIFI_CLOUD         1
#define MODE_EMERGENCY_SOFTAP   2

// 2. Analog Sensors (Single ADC1 Architecture on ESP32-C6 — Zero RF Conflicts)
//    Breadboard 1 & 2 Common Sensor GND Reference
#define PIN_LIGHT               1   // ADC1_CH1 (GPIO 1 / D6 - Gravity DFR0026 V2.1 Ambient Light Sensor on 3.3V)
#define PIN_EC                  2   // ADC1_CH2 (GPIO 2 / A1 - DFRobot Analog EC Probe on 5.0V)
#define PIN_PH                  3   // ADC1_CH3 (GPIO 3 / A2 - Analog pH Sensor Probe on 5.0V)
#define PIN_WATER               4   // ADC1_CH4 (GPIO 4 / A3 - Resistive Water Level on 5.0V)
#define WATER_THRESHOLD_HIGH    350 // 12-bit ADC threshold for OK status
#define WATER_THRESHOLD_LOW     200 // 12-bit ADC threshold for Low status
#define WATER_THRESHOLD_RAW     350 // Legacy compatibility fallback

// Gravity DFR0026 V2.1 reports relative ADC signal percentage, not calibrated lux.

// Sensor Calibration & Tuning Offsets
#define PH_CALIBRATION_OFFSET   0.0f
#define TDS_SUPPLY_VOLTAGE      5.0f
#define EC_CALIBRATION_FACTOR   1.0f

// 3. Digital Environmental Bus (Breadboard 1 - 3.3V Rail)
//    Assigned to GPIO 15 (D13) so Hardware UART1 RX on GPIO 17 has zero bus conflict
#define PIN_DHT                 15

// 4. Mini 0.96" SSD1306 OLED Display (Breadboard 3 - Hardware I2C, Address 0x3C)
#define PIN_OLED_SDA            19  // ESP32-C6 Hardware SDA
#define PIN_OLED_SCL            20  // ESP32-C6 Hardware SCL

// 5. Inter-MCU Hardware UART1 Bridge to Seeed XIAO ESP32S3 + Wio-SX1262 (115,200 baud)
//    - FireBeetle C6 GPIO 16 (TX1) --> XIAO ESP32S3 Pin D7 (GPIO 44 / RX1)
//    - FireBeetle C6 GPIO 17 (RX1) <-- XIAO ESP32S3 Pin D6 (GPIO 43 / TX1)
#define PIN_LORA_UART_TX        16
#define PIN_LORA_UART_RX        17
#define LORA_UART_BAUD          115200

// 6. Relay Board 1: 230V AC Mains Appliances (Active-LOW: LOW=ON, HIGH=OFF)
//    Galvanically Isolated via Breadboard 4 (5V JD-VCC & Isolated GND, Yellow Jumper Removed)
#define PIN_RELAY_WATER_PUMP    7   // Relay 1 (IN1): Main Tower Irrigation Pump (15m ON / 45m OFF)
#define PIN_RELAY_LIGHT         5   // Relay 2 (IN2): LED Grow Light (Strict 16h ON / 8h OFF)
#define PIN_RELAY_FAN_CIRC      6   // Relay 3 (IN3): Circulation Fan (Temp ML + User Threshold)
#define PIN_RELAY_FAN_EXT       14  // Relay 4 (IN4): Climate Extractor Fan (Humidity ML > 75% + Temp)

// 7. Relay Board 2: 12V DC Peristaltic Dosing Pumps (Active-LOW: LOW=ON, HIGH=OFF)
//    Galvanically Isolated via Breadboard 4 (Calibrated Micro-Dosing: 800ms = 1.0 mL)
#define PIN_DOSE_PH_UP          18  // Relay 5 (IN1): pH Up Dosing Pump (`pu`)
#define PIN_DOSE_PH_DOWN        21  // Relay 6 (IN2): pH Down Dosing Pump (`pd`)
#define PIN_DOSE_EC_UP          22  // Relay 7 (IN3): Nutrient A+B Up Pump (`nu`)
#define PIN_DOSE_EC_DOWN        23  // Relay 8 (IN4): Fresh Dilution Water Down Pump (`nd`)

// 8. Electrical & Agronomic Safety Constants (25-30L Vertical Tower Reservoir)
#define RELAY_STAGGER_MS        150                   // 150ms minimum stagger between relay coil activations
#define MS_PER_ML               800UL                 // 1.25 mL/sec = 800 ms per 1 mL micro-dose
#define MICRO_DOSE_MS           800UL                 // Single 1.0 mL micro-dose pulse duration
#define MANUAL_DEFAULT_DOSE_MS  800UL
#define MANUAL_MAX_DOSE_MS      20000UL
#define DIFFUSION_LOCKOUT_MS    (3UL * 60UL * 1000UL) // Mandatory 3-minute (180,000 ms) reservoir mixing lockout (Autonomous only)
#define MAX_DAILY_DOSES         6                     // Rolling autonomous micro-doses per 2-hour window
#define DAY_WINDOW_MS           (2UL * 60UL * 60UL * 1000UL)

// Plausibility & Fault Gates (For autonomous closed-loop dosing only)
#define PH_PLAUSIBLE_MIN        2.0f
#define PH_PLAUSIBLE_MAX        12.5f
#define EC_PLAUSIBLE_MIN        0.0f
#define EC_PLAUSIBLE_MAX        15.0f

// Schedule & Telemetry Intervals
#define LORA_TELEMETRY_INTERVAL_MS 17500UL            // 17.5-second desynchronized nominal LoRa cadence (coprime with Fodder 15s)
#define SENSOR_POLL_INTERVAL_MS    3000UL             // 3-second local sensor & OLED refresh
#define OLED_PAGE_INTERVAL_MS      10000UL            // 10-second dual-screen OLED page flip
#define MANUAL_OVERRIDE_HOLD_MS    (60UL * 60UL * 1000UL)

// Grow Light Photoperiod: Strict 16 Hours ON / 8 Hours OFF
#define LIGHT_ON_DURATION_MS       (16UL * 60UL * 60UL * 1000UL)
#define LIGHT_CYCLE_TOTAL_MS       (24UL * 60UL * 60UL * 1000UL)

// Irrigation Pump Schedule: 15 Minutes ON / 45 Minutes OFF
#define PUMP_ON_DURATION_MS        (15UL * 60UL * 1000UL)
#define PUMP_CYCLE_TOTAL_MS        (60UL * 60UL * 1000UL)

// Bluetooth Low Energy (NimBLE 5.0) UUID Definitions
#define BLE_DEVICE_NAME          "SmartHydro-Tower"
#define BLE_SERVICE_UUID         "0000ff00-0000-1000-8000-00805f9b34fb"
#define BLE_CHAR_TELEMETRY_UUID  "0000ff01-0000-1000-8000-00805f9b34fb"
#define BLE_CHAR_COMMAND_UUID    "0000ff02-0000-1000-8000-00805f9b34fb"

// Relay states cache structure
struct RelayStates {
  bool growLight = false;
  bool circulationFan = false;
  bool waterPump = false;
  bool extractorFan = false;
  bool phUp = false;
  bool phDown = false;
  bool ecUp = false;
  bool ecDown = false;
};
extern RelayStates relays;

#pragma once
#include <Arduino.h>
#include "SmartHydroFrame.h"

// ==============================================================================
// HARDWARE PIN DEFINITIONS (Seeed Studio XIAO ESP32S3 + Wio-SX1262 B2B Expansion)
// ==============================================================================

// 1. Inter-MCU Hardware UART1 Bridge to FireBeetle 2 ESP32-C6 (115,200 baud)
//    - XIAO D7 (GPIO 44 / RX1) <-- FireBeetle C6 GPIO 16 (TX1)
//    - XIAO D6 (GPIO 43 / TX1) --> FireBeetle C6 GPIO 17 (RX1)
#define BRIDGE_UART_BAUD     115200
#define BRIDGE_UART_RX_PIN   44  // XIAO ESP32S3 D7 (RX)
#define BRIDGE_UART_TX_PIN   43  // XIAO ESP32S3 D6 (TX)

// 2. Seeed Studio Wio-SX1262 B2B Expansion Board SPI Pinout
#define LORA_SCK             7   // XIAO D8 (GPIO 7)  - SPI SCK
#define LORA_MISO            8   // XIAO D9 (GPIO 8)  - SPI MISO
#define LORA_MOSI            9   // XIAO D10 (GPIO 9) - SPI MOSI
#define LORA_CS              41  // B2B Connector GPIO 41 - NSS (Chip Select)
#define LORA_RST             42  // B2B Connector GPIO 42 - NRESET
#define LORA_BUSY            40  // B2B Connector GPIO 40 - BUSY
#define LORA_DIO1            39  // B2B Connector GPIO 39 - DIO1 Interrupt
#define LORA_RF_SW           38  // B2B Connector GPIO 38 - RF Switch RXEN Control

// 3. Sub-GHz LoRa P2P RF Parameters (Matched to SenseLoRa 4G Gateway & Heltec V3)
#define LORA_FREQUENCY       868.1f   // 868.1 MHz Sub-GHz Channel (South Africa ICASA 868.0-868.6 MHz SRD Band)
#define LORA_BANDWIDTH       125.0f   // 125.0 kHz Bandwidth
#define LORA_SPREAD_FACTOR   7        // SF7 Fast Modulation (~60-150 ms ToA, <0.4% hourly duty cycle)
#define LORA_CODING_RATE     5        // 4/5 Forward Error Correction
#define LORA_SYNC_WORD       0x12     // Private Network Sync Word
#define LORA_OUTPUT_POWER    5        // +5 dBm DC-DC Buck Mode (~18 mA TX draw, prevents 3.3V LDO drop / POR reset; ICASA max is +14 dBm)
#define LORA_PREAMBLE_LEN    8
#define LORA_TCXO_VOLTAGE    1.8f     // 1.8V TCXO Reference for Wio-SX1262

// 4. Onboard Status LED & Watchdog Timeout
#define PIN_STATUS_LED       21       // XIAO ESP32S3 Built-in User LED (Active-LOW)
#define WDT_TIMEOUT_SEC      30       // 30-Second Hardware Task Watchdog Timer

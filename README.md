# <img width="25" height="25" alt="logo" src="https://github.com/user-attachments/assets/8126cb21-f267-4f98-a5b9-57a87622683a"/> Smart Hydro 2026 <img width="25" height="25" alt="logo" src="https://github.com/user-attachments/assets/8126cb21-f267-4f98-a5b9-57a87622683a"/>

An IoT monitoring and control system for hydroponic crop production and tray based fodder growing in remote, arid, water scarce environments.

**Client:** Emeris (a university whose outreach initiatives support rural communities in Namibia)

**Team:**
- Kyle Frederick Lötter, ST10336577
- Max Daniels, ST10434398
- Cameron Van Heerden, ST10435031
- Maxine Palm, ST10436964
- Megan Britnor, ST10439223
- Reshaan Govender, ST10433877

## 💧 Overview
> [!IMPORTANT] 
>
> SmartHydro demonstrates how an IoT enabled system can support local food and fodder production in areas with limited water, unreliable connectivity, and little access to advanced technology, without replacing existing agricultural practices. It combines:
> - **Hydroponic structures** for produce cultivation.
> - **Fodder structures** for tray based livestock feed production.
> 
>The system is built around an **offline first edge architecture**: monitoring, automation, hardware control, and data storage all run locally at each site, with no dependency on an internet connection. Where there is no Wi-Fi at the box, readings go by **LoRa radio** to a **4G gateway** that sends them on to the cloud. A farmer at the box can also connect to it directly over **Bluetooth** or the box's own hotspot.
> 
> A key design principle for AI/IoT systems in resource poor agricultural settings ("Off the Grid"), is **user agency**, meaning the farmer stays in control, with the ability to manually switch devices and dose nutrients or pH rather than being dictated to by automation or AI.


## 🌐 Live deployment
> [!IMPORTANT] 
> | Component | Where it runs | Link |
> | --- | --- | --- |
> | Web app (MVC) | Azure App Service, South Africa North | [smarthydromvc…azurewebsites.net](https://smarthydromvc-eddch7ajapdyb8f4.southafricanorth-01.azurewebsites.net) |
> | Web API | Azure App Service, South Africa North | [smarthydroapi…azurewebsites.net](https://smarthydroapi-exhjg0b2f9bqe4e6.southafricanorth-01.azurewebsites.net) |
> | API docs (Scalar) | Served by the Web API | [smarthydroapi…/scalar/v1](https://smarthydroapi-exhjg0b2f9bqe4e6.southafricanorth-01.azurewebsites.net/scalar/v1) |
> | User guide | Azure App Service, South Africa North | [smarthydrouserguide…azurewebsites.net](https://smarthydrouserguide-h8argycde4cfeygg.southafricanorth-01.azurewebsites.net) |
> | Database | Neon serverless PostgreSQL | Private, reached only by the API |
>
> All three web apps are deployed automatically from `main` by GitHub Actions (see [Utilisation of GitHub Actions](#️-utilisation-of-github-actions)).

## 👥 Who it's for

> [!NOTE]
>
> | Role | What they do | Main app |
> | --- | --- | --- |
> | **Admin** | Approves or denies new Operators, manages every user and site, changes users' roles (but never their own), restores or permanently purges deleted sites. | Web app and Android app |
> | **Operator** (farmer / site owner) | Registers, creates sites and structures, pairs sensor boxes, controls devices, edits safe ranges and invites Workers. | Android app (primary), web app |
> | **Worker** | Joins a site through an emailed invite link or code, then monitors the structures on that site, switches equipment and adds doses. On Android, Workers get **Simple Mode** (one screen, no menus) by default. | Web app and Android app |
> | **Offline use** (no account) | Not a role: anyone can use the Android app without signing in, add their own structures, pair with their box over Bluetooth or its hotspot, and see readings and control it. | Android app |
>
> Farmers mostly use the **Android app**, because phones are cheaper, more portable and less dependent on stable power than a laptop. The **web app** is used by the development team and admins to monitor, test and manage the system, and works as a desktop client for sites with reliable laptop access. The **user guide** site explains setup, everyday use and troubleshooting in plain language for owners and workers.

## ✨ Features

**Monitoring**
- Readings per structure: pH, EC (mS/cm), temperature, humidity, light and water level. The tower box reads its sensors every 3 seconds, and the fodder box sends a reading every 15 seconds.
- Gauges coloured against each structure's own safe range, plus a clear "no reading" state when a box goes quiet.
- A faulty or unplugged sensor shows as a fault instead of a fake number.
- 7-day trend chart on each sensor page in the web app and the Android app.

**Connectivity**
- LoRa + 4G gateway: boxes with no Wi-Fi send readings by LoRa radio to a gateway, which forwards them to the API over 4G.
- Each box switches between LoRa, Wi-Fi and its own hotspot when a link drops, and switches back when it recovers. Modes can also be changed by an Operator or Admin from the web app, or from the Android app over Bluetooth.
- The web app shows how each box is currently connected.
- Readings stay queued on the box and gateway until the database confirms them, so nothing is lost when the link drops. Duplicates are ignored.

**Control**
- Switch grow light, circulation fan, extractor fan, water pump and spray system on or off, either continuously or for a set time.
- Manual dosing for pH up, pH down, nutrient up and dilution water. Each tap sends one fixed dose.
- The apps wait for the box to confirm a command before showing it as done, and show the reason if the box refuses one.
- The box doses automatically within safety limits: small doses, a 15 minute wait after pH (10 after nutrients), an hourly cap on pH, and no dosing when a sensor is faulty. Manual doses follow the same limits.
- An activity log shows what ran on a box and why.

**Alerts and thresholds**
- A rule engine checks each reading for pH, EC and temperature outside the structure's safe ranges, and for a depleted water level, and raises Warning or Critical alerts.
- Operators can edit safe ranges per structure; sensible defaults apply until they do.
- Alerts can be filtered, resolved, deleted and restored.

**Camera and gallery**
- ESP32-CAM node (OV2640) that takes 1280x1024 JPEG snapshots and uploads them to the API. There is no live video stream; the node only takes photos.
- A snapshot is taken when a user presses capture in the web or Android app, once when the node boots, and automatically every 24 hours.
- The web app can also take a photo with the laptop or phone camera, or upload an existing image.
- Photo gallery per structure with labels, favourites, search and a calendar view, also available offline on Android. Anyone with access to the site can view, edit and favourite its photos; only the Operator (or an Admin) can delete and restore them, and only an Admin can permanently purge one.

**Sites, teams and accounts**
- Multi-site support with soft delete, restore and admin purge.
- Operator registration with email verification and admin approval.
- Worker invitations by email link or one-time code.
- Two-step sign-in (password plus emailed one-time code), forgot password and password reset.
- Admins can change other users' roles, but not their own.

**Offline first (Android)**
- Bluetooth pairing: pair once using the 6-digit code shown on the box's screen, then see live readings and control the box with no network. A signed-in user can pair too, and the app then uses the box directly while Bluetooth is connected, which helps when the cloud is slow.
- Direct pairing with a sensor box over its own Wi-Fi hotspot, with no internet needed.
- Works without an account: structures added offline stay on the phone through sign-in and sign-out.
- Readings, structures, sites, team lists and photos are saved in a local Room database, so the app and its charts still work offline.
- Simple Mode: one screen with no menus, on by default for Workers.
- Pull to refresh across the Android screens.

**User guide**
- A separate website with simple help pages for owners and workers, covering setup, everyday use and troubleshooting.

## 🧰 Tech stack
> [!TIP]
> | Layer | Technology |
> | --- | --- |
> | Web API | ASP.NET Core 10, Entity Framework Core 10, Npgsql, Dapper, SignalR, JWT Bearer, Scalar (OpenAPI), MailKit |
> | Web app | ASP.NET Core 10 MVC, Razor, Bootstrap, jQuery validation, SignalR JS client, Chart.js 4.4.4, cookie authentication |
> | User guide | ASP.NET Core 10 MVC, Razor (no database) |
> | Android | Kotlin 2.2, Jetpack Compose (Material 3), Navigation Compose, Retrofit + OkHttp + Gson, Room 2.8, Coil, Coroutines, Android Bluetooth LE |
> | Database | PostgreSQL on Neon (serverless) |
> | Hosting | Azure App Service (Linux, South Africa North), three apps: API, MVC and user guide |
> | Email | Brevo SMTP relay |
> | Firmware | Arduino C++ on DFRobot FireBeetle 2 ESP32-C6 (tower), Seeed XIAO ESP32-S3 with Wio-SX1262 (tower LoRa bridge), Heltec WiFi LoRa 32 V3 (fodder), Makerfabs SenseLoRa ESP32-S3 with SX1276 and a SIMCom 4G modem (gateway), AI-Thinker ESP32-CAM (camera); ArduinoJson, DHT, Adafruit GFX, Adafruit SSD1306, RadioLib, NimBLE, WebSockets_Generic |
> | Edge decisions | Four on-chip classifiers (EC, pH, temperature, humidity) exported to C++ in the Eloquent ML format, combined with each structure's safe ranges |
> | CI/CD | GitHub Actions to Azure Web Apps |

## 📂 Repository structure

> [!TIP]
>
> | Folder / file | What it holds |
> | --- | --- |
> | `SmartHydroAPI/` | ASP.NET Core Web API: controllers, services, entities, DTOs, SignalR hubs, design patterns and SQL migrations. |
> | `SmartHydroAPI_Tests/` | xUnit unit tests for the API (services, security helpers, dosing, commands and design patterns). |
> | `Group_1_SmartHydro_MVC/` | ASP.NET Core MVC web app: controllers, Razor views, API clients, middleware, filters and static assets. |
> | `Group_1_SmartHydro_MVC_Tests/` | xUnit unit tests for the web app (API client, threshold client and threshold catalogue). |
> | `SmartHydro_UserGuide_MVC/` | ASP.NET Core MVC user guide site. |
> | `SmartHydro_Android/` | Kotlin Jetpack Compose Android app with Room offline storage, Bluetooth pairing and unit tests. |
> | `SmartHydro-Vertical-Tower-EMERISNMB/` | Firmware for the vertical tower sensor and relay box. |
> | `SmartHydro-Tower-LoRa-Bridge-EMERISNMB/` | Firmware for the XIAO ESP32-S3 LoRa radio that the tower box talks to over UART. |
> | `SmartHydro-Fodder-System-EMERISNMB/` | Firmware for the fodder tray box (Heltec V3) with the misting spray cycle. |
> | `SmartHydro-SenseLoRa-Gateway/` | Firmware for the LoRa to 4G gateway. |
> | `SmartHydro-SenseLoRa-Gateway-HomeTester/` | Bench version of the gateway that uses home Wi-Fi instead of 4G, for testing. |
> | `SmartHydro-ESP32-CAM-EMERISNMB/` | Firmware for the camera node. |
> | `SmartHydroDB.sql` | Base PostgreSQL schema for the hardware, telemetry, control, camera and alert tables. |
> | `.github/workflows/` | CI/CD workflow that tests the API, web app and Android app, lints the Android app, builds the user guide, and deploys the API, web app and user guide to Azure. |
> | `Documentation/` | POE documentation for each part. |
> | `Presentation/` | Part 2 implementation presentation and Part 3 EXPO presentation. |
> | `Attendance and Meeting Records/` | Attendance registers and meeting notes. |
> | `SmartHydro.slnx` | Solution containing the API and MVC projects and their two test projects. |


## 🚀 Getting started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A PostgreSQL database (a free [Neon](https://neon.tech) project works)
- [Android Studio](https://developer.android.com/studio) with JDK 17 or newer, for the Android app
- [Arduino IDE 2](https://www.arduino.cc/en/software) with the ESP32 board package, for the firmware
- An SMTP account for verification emails
- For LoRa: a SenseLoRa gateway with a data SIM (the gateway is set up for Telkom SA, APN `internet`)

### 1. Clone the repository

```bash
git clone https://github.com/EMECPE/insy7315-2026-wil-kylelotter.git
cd insy7315-2026-wil-kylelotter
```

### 2. Create the database

Run the base schema against your PostgreSQL database. The API brings the rest of the schema up to date on start-up, and the scripts in `SmartHydroAPI/Data/Migrations` are there if you prefer to apply each change by hand.

```bash
psql "<your-connection-string>" -f SmartHydroDB.sql
```

### 3. Configure and run the API

The API will not start until these four secrets are set. Use user secrets locally and App Service settings in Azure.

| Setting | Environment variable | Notes |
| --- | --- | --- |
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` | PostgreSQL connection string |
| `Jwt:Key` | `Jwt__Key` | At least 32 bytes long |
| `Security:HmacPepper` | `Security__HmacPepper` | Random secret used to hash one-time codes |
| `Smtp:Password` | `Smtp__Password` | SMTP password or API key |

```bash
cd SmartHydroAPI
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<your-connection-string>"
dotnet user-secrets set "Jwt:Key" "<a-long-random-key>"
dotnet user-secrets set "Security:HmacPepper" "<a-random-pepper>"
dotnet user-secrets set "Smtp:Password" "<your-smtp-password>"
dotnet run
```

The API listens on `http://localhost:5043`. Open `http://localhost:5043/scalar/v1` to browse and try every endpoint.

The boxes and gateway also need a device key. Use the same key on the API and in each board's `secrets.h`.

Optional: set `Gateway:SharedSecret` (`Gateway__SharedSecret`) to the same random value on both the API and the web app. The web app then forwards each visitor's real IP address, so sign-in and registration rate limits apply per person instead of to everyone using the web app together.

The Admin account is configured under `Authentication:AllowedUsers` in `SmartHydroAPI/appsettings.json` (email, role and a `PBKDF2-SHA256$iterations$salt$hash` password hash). For your own environment, replace it with your own Admin email and password hash.


### 4. Run the web app

`Group_1_SmartHydro_MVC/appsettings.json` already points the web app at a local API on `http://localhost:5043/`, and `appsettings.Production.json` points it at the Azure API. To use a different API, change `Api:BaseUrl`:

```json
{
  "Api": {
    "BaseUrl": "http://localhost:5043/"
  }
}
```

```bash
cd Group_1_SmartHydro_MVC
dotnet run --launch-profile https
```

The web app opens on `https://localhost:7043`. Without `--launch-profile https`, it runs on `http://localhost:5089` instead.

### 5. Run the user guide

The user guide needs no database or secrets.

```bash
cd SmartHydro_UserGuide_MVC
dotnet run
```

### 6. Run the Android app

1. Open `SmartHydro_Android` in Android Studio and let Gradle sync.
2. The app talks to the live Azure API by default. To use a local API, change `BASE_URL` in `app/src/main/java/com/example/smarthydro/api/ApiClient.kt`, keeping the `/api/` at the end. From the Android emulator, a local API is at `http://10.0.2.2:5043/api/`.
3. Run on a device or emulator with Android 8.0 (API 26) or newer. Bluetooth pairing needs a real phone; the app asks for Bluetooth permission on the pairing screen.

### 7. Flash the firmware

1. Open the sketch folder for the board you are flashing in the Arduino IDE.
2. Install the libraries the sketches use: ArduinoJson, DHT sensor library, Adafruit GFX, Adafruit SSD1306, WebSockets_Generic, NimBLE-Arduino (tower and fodder), and RadioLib (LoRa bridge, fodder and gateway).
3. Copy `secrets.example.h` to `secrets.h` and fill it in, including the device key. Do not commit `secrets.h`; check `git status` before you commit.
   - The fodder `secrets.example.h` turns LoRa, Bluetooth and the Wi-Fi fallback **off** for an isolation test. Turn them back on for a normal box.
4. Select the right board (FireBeetle 2 ESP32-C6, XIAO ESP32-S3, Heltec WiFi LoRa 32 V3, SenseLoRa ESP32-S3 or AI-Thinker ESP32-CAM) and upload.
5. On first boot of a tower or fodder box, join the box's hotspot and finish setup in the captive portal, either with Wi-Fi details or in offline mode.
   - The box starts in LoRa + Bluetooth mode and falls back to Wi-Fi or its hotspot on its own. Hold BOOT for 3 seconds to switch between LoRa and hotspot by hand.
   - The camera node needs internet, because it uploads every photo to the API, so it has no offline mode. Join its `SmartHydro-CAM-Setup` hotspot and enter Wi-Fi details (WPA2-Personal, or WPA2-Enterprise/PEAP such as a university network). Until it connects, it stays in the setup portal.
   - The tower's LoRa bridge has no portal; it is wired to the tower box over UART.
   - The gateway connects over 4G with its SIM. For bench testing without a SIM, flash `SmartHydro-SenseLoRa-Gateway-HomeTester` instead, which uses home Wi-Fi.
6. In the app, link the box to a structure using its MAC address, then pair it over Bluetooth (enter the 6-digit code shown on the box's screen) or over its hotspot.


## 🔌 API overview

All endpoints sit under `/api`. Full request and response details are in the [Scalar docs](https://smarthydroapi-exhjg0b2f9bqe4e6.southafricanorth-01.azurewebsites.net/scalar/v1).

| Area | Base route | Highlights |
| --- | --- | --- |
| Authentication | `/api/auth` | Register, verify email, request and verify sign-in code, forgot and reset password, claim invite |
| Sites | `/api/sites` | Create, rename, soft delete, restore |
| Site invitations | `/api/sites/{siteId}/invitations` | Invite a Worker by email |
| Team | `/api/team` | List and remove site members |
| Structures | `/api/structures` | Create, edit, soft delete, restore |
| Devices | `/api/devices` | Register, rename, assign to a structure, change connection mode |
| Telemetry | `/api/telemetry` | Ingest from boxes and the LoRa gateway, latest reading, history, activity log |
| Actuators | `/api/actuators` | Current relay state, continuous and timed commands, history |
| Dosing | `/api/dosing` | Queue a dose, box callback, pending and history |
| Thresholds | `/api/thresholds` | Safe ranges per structure and per site |
| Alerts | `/api/alerts` | List, unresolved count, resolve, delete, restore |
| Camera | `/api/camera` | Photo upload from the node or the apps, trigger capture, command poll for the node, latest image per camera, view, edit, delete, restore and purge photos, favourites, gallery |
| Sync | `/api/sync` | Latest timestamp per device, batch upload of telemetry, alerts and overrides |
| Admin | `/api/admin` | Users and role changes, pending Operators, approvals, sites, purge |
| Real time | `/hubs/actuators` | SignalR hub for commands to boxes and live updates |

Errors come back as standard `ProblemDetails` with an extra `message` field, so both clients can show one readable sentence.

## 🗄️ Database

PostgreSQL, accessed through Entity Framework Core (24 tables). Main tables:

| Group | Tables |
| --- | --- |
| Places and hardware | `sites`, `structures`, `devices` |
| Readings | `telemetry_realtime`, `telemetry_daily_history`, `telemetry_receipts` |
| Control | `actuator_states`, `actuator_history`, `dosing_commands`, `device_commands`, `activity_logs` |
| Camera | `camera_captures`, `camera_commands` |
| Monitoring | `alerts`, `thresholds` |
| People and access | `users`, `site_users`, `site_invites`, `pending_registrations` |
| Security | `otp_codes`, `password_reset_codes`, `password_history`, `login_attempts`, `email_rate_limits` |

A background service runs nightly at 23:59:59 UTC, rolls each device's raw readings into one daily average row, and then clears the raw samples, which keeps the free database tier small.

## 🧩 Design patterns

| Pattern | Where | What it does |
| --- | --- | --- |
| Repository | API | `TelemetryRepository` and `ActuatorRepository` keep data access out of the services. |
| Strategy | API | Picks the telemetry parser by board type (FireBeetle C6 or ESP32-CAM) and the image store (database by default, or local disk when `Storage:UseLocalStorage` is `true`). |
| Specification | API | One rule class per check (pH, EC, temperature, water level) combined by a rule engine. |
| Factory | API | `AlertFactory` builds consistent alerts from rule results. |
| Observer | API | An in-memory event publisher fires `TelemetryIngestedEvent`, which the alert handler listens for. |
| Decorator | API | Cached wrappers around the telemetry and actuator services. |
| Singleton | Web app, Android | One shared, resilient HTTP client in the web app; one Retrofit client in the Android app. |
| Factory | Web app | `DashboardFactory` returns the hydroponic or fodder dashboard builder for a structure. |
| Observer | Web app | `SiteChangePublisher` notifies a SignalR observer and a logging observer when a site changes. |
| State machine | Android | The Bluetooth connection logic is a state machine kept separate from the Bluetooth calls, so it can be unit tested without a phone. |

## 🔒 Security

- **Passwords** hashed with PBKDF2-SHA256 (100,000 iterations, random salt), compared in constant time, with reuse of the last 5 passwords blocked.
- **Two-step sign-in** with an emailed one-time code; codes and invite tokens are stored only as HMAC-SHA256 hashes under a server-side pepper.
- **JWT** bearer tokens with issuer, audience, lifetime and signing key checks, plus a live account status check on every request, so disabled users are cut off at once.
- **Role-based authorisation** (Admin, Operator, Worker) and per-site checks when users read or change structures, devices and photos. The camera's upload and trigger-capture endpoints still accept requests without a user sign-in, so anyone who knows a camera's MAC address can ask it to take a photo. The latest-image endpoint is open too, but it only returns photos the signed-in caller is allowed to see.
- **Device key**: boxes and the gateway must send a secret device key with their data.
- **Certificate checks**: the tower, fodder and gateway only trust our server's real certificate.
- **Bluetooth**: the box can only be controlled after pairing with the 6-digit code on its screen.
- **Brute force protection**: 15 minute lockout per email and IP address, and rate limits on sign-in, registration, code checks, password reset and invites.
- **Web hardening**:
  - Web app: Content Security Policy (report-only in Development) that only allows scripts from the site itself, `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, `Permissions-Policy`, HSTS and anti-forgery tokens.
  - API: `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy` and a CORS allowlist.
- **Secrets** come only from environment variables or user secrets, and the API refuses to start without them.
- **Soft deletes** with who and when, so sites, structures, alerts and photos can be audited and restored.
- **Devices** send data over HTTPS, and Wi-Fi credentials are entered in the captive portal instead of being compiled into the firmware.
- **Still to do:** a separate device key for each box (all boxes share one today), and certificate checks plus signed-in upload on the camera node.

## ♿ Accessibility

- Built-in **read aloud** in the web app using the browser's speech synthesis, including on the sign-in pages.
- **TalkBack** semantics on Android gauges, charts and forms, with a shortcut in Settings.
- Screen reader text on the 7-day charts in the web app.
- Simple Mode on Android for users with little tech experience.
- **Dark/Light mode** and a persistent **high-contrast** theme on both clients.
- Large touch targets, clear empty and loading states, and protection against submitting a form twice.
- Keyboard navigation to move to next field.

## 🧪 Testing

> [!NOTE]
> SmartHydro has **903 automated test cases**: 345 for the API, 121 for the web app and 437 for the Android app. The .NET tests need no database, email account or secrets, so they run straight after cloning.
>
> **What is tested**
> - **Alerts:** pH, EC, temperature and water level rules raise the right alert, severity and message, and only use the structure's own safe ranges.
> - **Thresholds:** default ranges per structure type, saving and deleting ranges, and blocking pH on fodder trays.
> - **Access and accounts:** Operators only see their own sites, Workers can monitor, switch equipment and add doses on their own site but cannot change settings or how a box connects, and only approved accounts stay signed in.
> - **Security helpers:** PBKDF2 password hashing, HMAC one-time code hashing and rate-limit wait messages.
> - **Dosing and commands:** dose limits and command confirmations.
> - **Web app API client:** bearer tokens, signing out on a rejected token, a clear error when the API is down, and the right message for each refused save, tested against a small fake API.
> - **Android:** API contract parsing, box JSON, history and trend mapping, missing readings, Simple Mode and Bluetooth pairing, as local JVM unit tests (Android Developers, 2024).
> - **Firmware:** no automated tests yet; it is checked by hand on the real boards.

```bash
dotnet test SmartHydro.slnx                      # API and web app tests

cd SmartHydro_Android && ./gradlew testDebugUnitTest   # Android tests
```
<img width="518" height="172" alt="Screenshot 2026-11-02 110239" src="https://github.com/user-attachments/assets/c0e8f58e-51c8-44f5-a240-d70e9fa98f57" />


<br/>
<br/>

<img width="630" height="206" alt="Screenshot 2026-11-02 110644" src="https://github.com/user-attachments/assets/23595c92-02aa-40da-b6e2-1d4d3516d394" />


The API can also be tried end to end from `SmartHydroAPI/SmartHydroAPI.http` or the Scalar UI.

## 🔁 Utilisation of GitHub

> [!IMPORTANT]
> GitHub is the team's version control and collaboration platform (Coursera, 2025). Every commit in the shared repository shows what changed and who changed it, and any bad change can be traced and reverted (himanshuchauhan59, 2025).
>
> `main` is the release branch and always stays runnable. Each change is built on its own short-lived branch, such as `feature/threshold-screen`, `fix/dark-mode` or `bug-fix/dosing-and-camera-api-firmware-gap`, and reaches `main` only through a reviewed pull request (himanshuchauhan59, 2025). This keeps merge conflicts small and lets teammates catch problems before they land (Coursera, 2025).

## ⚙️ Utilisation of GitHub Actions

> [!IMPORTANT]
> GitHub Actions runs jobs automatically when the repository changes, so building, testing and deploying no longer depend on someone remembering to do it by hand (GitHub, 2026a). A single workflow, `ci.yml`, runs the whole pipeline on a clean Ubuntu machine and stops at the first failure. Each job only runs when its part of the repository changed.
>
> | Job | Runs on | What it does |
> | --- | --- | --- |
> | .NET tests | Every pull request and push to `main` touching the API, web app or their tests | Checkout, set up .NET 10, restore, build (Release), run the API and web app xUnit tests, upload results and code coverage |
> | User guide build | Every pull request and push to `main` touching `SmartHydro_UserGuide_MVC/**` | Checkout, set up .NET 10, publish the user guide and keep the output for the deploy |
> | Android tests | Every pull request and push to `main` touching `SmartHydro_Android/**` | Checkout, set up JDK 25, run the JVM unit tests, build a debug APK, upload the test report and APK |
> | Android lint | Same as Android tests | Checks the Android code for issues and uploads a report, without failing the build |
> | Deploy API | Push to `main` changing `SmartHydroAPI/**`, only after the .NET tests pass | Publish, deploy to the `smarthydroapi` web app |
> | Deploy MVC | Push to `main` changing `Group_1_SmartHydro_MVC/**`, only after the .NET tests pass | Publish, deploy to the `smarthydromvc` web app |
> | Deploy user guide | Push to `main` changing the user guide, only after the user guide builds | Deploy to the `smarthydrouserguide` web app |
>
> Test results show directly on each pull request, and the full reports are downloadable as artifacts. The Azure publish profiles are stored as GitHub secrets (`AZURE_WEBAPP_PUBLISH_PROFILE`, `AZURE_MVC_PUBLISH_PROFILE` and `AZURE_USERGUIDE_PUBLISH_PROFILE`), never in the code (GitHub, 2026b). Only one deploy per app runs at a time, so two pushes can't overwrite each other.
>
<img width="950" height="480" alt="Screenshot 2026-11-02 111238" src="https://github.com/user-attachments/assets/09fcc9ea-ba57-4a63-9635-db8adfb2a217" />
> 
> **Why it matters:** broken code never reaches the live apps. If a test fails, nothing is deployed and the red cross shows which commit caused it.

## 📚 References

- Android Developers, 2024. *Build local unit tests*. [online] Available at: <<https://developer.android.com/training/testing/local-tests>> [Accessed 20 September 2026].
- Coursera, 2025. *What Is GitHub? Use Cases and FAQ*. [online] Available at: <<https://www.coursera.org/articles/what-is-github>> [Accessed 17 September 2026].
- GitHub, 2026a. *Understanding GitHub Actions*. [online] Available at: <<https://docs.github.com/en/actions/get-started/understand-github-actions>> [Accessed 17 September 2026].
- GitHub, 2026b. *Using secrets in GitHub Actions*. [online] Available at: <<https://docs.github.com/en/actions/how-tos/write-workflows/choose-what-workflows-do/use-secrets>> [Accessed 20 September 2026].
- himanshuchauhan59, 2025. *Git & GitHub: What, How, and Why – Everything You Need to Know (EP-01)*. [online] Available at: <<https://dev.to/himanshu_chauhan_59/git-github-what-how-and-why-everything-you-need-to-know-ep-01-1772>> [Accessed 17 September 2026].

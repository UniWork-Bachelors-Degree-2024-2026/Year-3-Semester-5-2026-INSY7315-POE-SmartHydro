using SmartHydro_UserGuide_MVC.Models;

namespace SmartHydro_UserGuide_MVC.Services
{
    public partial class GuideContent
    {
        private static readonly GuideWhere AtTheBox = new GuideWhere { Icon = "memory", Label = "At the box" };

        // Adds the Getting set up tasks, in the order a farm sets up new equipment.
        private void AddSetupTasks()
        {
            Add(new GuideTask
            {
                Id = "choose",
                Icon = "alt_route",
                Title = "Choose your setup",
                Why = "There are two ways to set up Smart Hydro. Pick one before you start. You can move from offline-only to the full setup at any time.",
                Table = Table(
                    new[] { "", "Offline-only setup", "Full setup" },
                    new[] { "Best for", "Using your phone right next to the box, with no internet or airtime", "Checking your farm from anywhere, with warnings, history, photos and a team" },
                    new[] { "You need", "A sensor box, a tank with its liquids, and an Android phone", "Everything in What you need below" },
                    new[] { "Account", "Not needed. Tap Continue Offline.", "Farm owners register and a Smart Hydro helper approves them" },
                    new[] { "How the phone reaches the box", "Straight to the box over Bluetooth, or the box's own Wi-Fi as a backup", "Through the gateway and the internet" }),
                Steps =
                {
                    "Only want to use Smart Hydro at the box? Follow <a href='#offlinesetup'>Offline-only setup</a>.",
                    "Want everything? Do the tasks on this page in order, from <a href='#need'>What you need</a> to <a href='#app'>Put the app on your phone</a>. Then make your account (see <a href='~/Home/Account#register'>Make an account</a>)."
                }
            });

            Add(new GuideTask
            {
                Id = "need",
                Icon = "checklist",
                Title = "What you need",
                Why = "Check you have these before you start.",
                Table = Table(
                    new[] { "Item", "What you need", "Good to know" },
                    new[] { "Sensor box", "One for each tower or set of fodder trays, plugged into the wall (230 V)", "Keep the sticker code on the box. You need it to add the tower or tray." },
                    new[] { "Gateway (receiver box)", "One on the farm, plugged in, with a SIM card that has airtime or data", "Not needed for offline-only." },
                    new[] { "Phone", "An Android phone (Android 8.0 or newer)", "The app file (.apk) comes from your Smart Hydro contact." },
                    new[] { "Computer or browser", "Chrome, Edge or Firefox", "A phone browser works too." },
                    new[] { "Internet", "Wi-Fi or mobile data", "Needed to sign in. Not needed for offline-only." },
                    new[] { "Email address", "One you can open on your phone or computer", "Smart Hydro emails you sign-in codes and invitations." },
                    new[] { "Farm camera (optional)", "A Smart Hydro camera with a steady 5 V power supply and Wi-Fi that has internet", "It has its own sticker code." },
                    new[] { "Tank and liquids", "pH-up, pH-down, plant food and clean water, each joined to its pump", "Keep water in the tank. The box does not check it before adding liquid." })
            });

            Add(new GuideTask
            {
                Id = "offlinesetup",
                Icon = "wifi_off",
                Title = "Offline-only setup (no internet, no account)",
                Why = "Use this if you only want to use Smart Hydro on the farm, standing next to the box. You do not need the gateway, the camera, the website, an email address or airtime.",
                Where = PhoneApp,
                Steps =
                {
                    "Get the box ready. Fit the antenna, fill the tank with water and join each liquid to its pump. If the box still needs building, see <a href='#build'>Build the box</a> first.",
                    "Plug the box in. The screen shows the Smart Hydro logo, then <b>MODE: LORA + BLE</b> (a fodder box shows <b>LORA+BLE</b> next to its logo while it starts). BLE means Bluetooth.",
                    "Write down the sticker code on the box. It looks like <b>A4:CF:12:9B:3E:01</b>.",
                    "Put the Smart Hydro app on your phone (see <a href='#app'>Put the app on your phone</a>).",
                    "Open the app and tap <span class='ui'>Continue Offline</span>.",
                    "Tap <span class='ui'>+ Add a Structure</span>. Type a name, type the sticker code into <b>Controller MAC Address</b>, choose <b>Hydroponic Structure</b> or <b>Fodder Structure</b>, then tap <span class='ui'>Add a Structure</span>.",
                    "Turn on Bluetooth on your phone and connect to the box over Bluetooth with the 6-digit code shown on the box screen (see <a href='~/Home/Owners#pair'>Offline mode: connect your phone to the box</a>).",
                    "You can now check the readings, switch things on or off, add liquids and change how the box connects. From now on the app connects to the box by itself whenever you are close to it."
                },
                Notes =
                {
                    Note("good", "The box runs its lights, pump, fans and dosing by itself. You do not need to press any button on the box, because its Bluetooth is on in every mode."),
                    Note("info", "Bluetooth not working? Use the box's own Wi-Fi instead (see <a href='~/Home/Owners#pair-wifi'>Offline mode: use the box's own Wi-Fi</a>)."),
                    Note("warn", "Towers and trays you add in offline mode are kept on this phone only.")
                }
            });

            Add(new GuideTask
            {
                Id = "check",
                Icon = "fact_check",
                Title = "Check the box works",
                Why = "Box arrived already built? Just plug it in and check these.",
                Where = AtTheBox,
                Steps =
                {
                    "Plug the box in. The small screen lights up and shows the Smart Hydro logo.",
                    "Look at the screen. A new box starts on radio, so the top line of a tower shows <b>MODE: LORA + BLE</b>. A fodder box shows <b>LORA+BLE</b> next to its logo while it starts, then <b>GW:</b> on its bottom line.",
                    "After a few seconds the readings appear: <b>pH</b>, <b>EC</b>, <b>Tmp</b> (temperature), <b>Hum</b> (humidity), <b>Lgt</b> (light) and <b>H2O</b> (water). A fodder box shows <b>T:</b> and <b>H:</b> instead of <b>Tmp</b> and <b>Hum</b>.",
                    "Check the numbers make sense. A sensor that is out of the water or unplugged shows a strange number, like pH 0 or 14."
                },
                Table = Table(
                    new[] { "Tower screen", "Fodder screen", "What it means" },
                    new[] { "MODE: LORA + BLE", "GW: on the bottom line", "Radio and Bluetooth. The normal setting." },
                    new[] { "MODE: CLOUD WIFI", "WiFi: on the bottom line", "Online Mode. The box is using Wi-Fi with internet." },
                    new[] { "EMERGENCY SOFTAP", "No change. Look for SmartHydro-Fodder- in your phone's Wi-Fi list.", "The box's own Wi-Fi is on, for offline mode." }),
                Notes =
                {
                    Note("info", "To test the box's own Wi-Fi, hold <b>BOOT</b> (fodder box: <b>PRG</b>) for 3 seconds. A tower screen shows <b>EMERGENCY SOFTAP</b>. A fodder screen does not change, so look for its Wi-Fi on your phone. Hold it for 3 seconds again to go back to radio."),
                    Note("info", "Bluetooth stays on in every mode, so your phone can always reach the box when you are close to it.")
                }
            });

            Add(new GuideTask
            {
                Id = "gateway",
                Icon = "router",
                Title = "Set up the gateway",
                Why = "The gateway is the receiver box. It picks up the radio from every tower and tray, and sends the numbers to Smart Hydro over mobile data (4G).",
                Where = AtTheBox,
                Steps =
                {
                    "Fit both antennas (the LoRa antenna and the 4G antenna) <b>before</b> you switch it on.",
                    "Put in a SIM card with airtime or data. The gateway is set up for the Telkom network.",
                    "Plug it in. Put it up high, in the open and away from metal.",
                    "Wait a minute or two. The numbers from your boxes start to appear in Smart Hydro."
                },
                Notes =
                {
                    Note("info", "Keep the SIM card topped up. The gateway uses a little data all the time. If it loses signal, it keeps the numbers and sends them later.")
                }
            });

            Add(new GuideTask
            {
                Id = "camera-setup",
                Icon = "photo_camera",
                Title = "Set up the farm camera (optional)",
                Why = "The camera takes photos, not live video: one when it starts, one every 24 hours, and one whenever you ask. It needs Wi-Fi with internet.",
                Where = AtTheBox,
                Steps =
                {
                    "Fix the camera where it can see your plants. Keep it dry and out of the spray.",
                    "Plug it into a steady 5 V power supply. A weak supply makes it restart.",
                    "On your phone, open Wi-Fi settings and join <b>SmartHydro-CAM-Setup</b>. There is no password.",
                    "The setup page opens by itself. If it does not, open <b>192.168.4.1</b> in your phone's browser.",
                    "Type your Wi-Fi name in <b>Network SSID</b> and its password in <b>Wi-Fi Password</b>. Leave <b>Student Email or University ID</b> empty unless you use university Wi-Fi.",
                    "Tap <span class='ui'>SAVE &amp; CONNECT TO CLOUD</span>. The camera joins your Wi-Fi, restarts and takes its first photo.",
                    "When you add the tower or tray, type the camera's sticker code into <b>Camera MAC Address</b>."
                },
                Notes =
                {
                    Note("info", "Wi-Fi password changed? The camera opens <b>SmartHydro-CAM-Setup</b> again by itself. Join it and type the new details.")
                }
            });

            Add(new GuideTask
            {
                Id = "website",
                Icon = "language",
                Title = "Open the website",
                Where = Website,
                Steps =
                {
                    "Open your browser and go to <a href='https://smarthydromvc-eddch7ajapdyb8f4.southafricanorth-01.azurewebsites.net' target='_blank' rel='noopener'>the Smart Hydro website</a>.",
                    "Bookmark it so you can find it again. On an Android phone in Chrome, tap the three dots at the top right, then the star. On a computer, click the star at the right end of the address bar."
                }
            });

            Add(new GuideTask
            {
                Id = "app",
                Icon = "install_mobile",
                Title = "Put the app on your phone",
                Where = PhoneApp,
                Steps =
                {
                    "Ask your Smart Hydro contact for the app file. It ends in <b>.apk</b>.",
                    "Open the file on your phone.",
                    "If your phone asks, tap <span class='ui'>Settings</span> and switch on <b>Allow from this source</b>.",
                    "Tap <span class='ui'>Install</span>, wait, then tap <span class='ui'>Open</span>.",
                    "On the sign-in screen, tap <span class='ui'>Continue Offline</span> to use the box with no account, or <span class='ui'>Sign In</span> to go online."
                }
            });

            Add(new GuideTask
            {
                Id = "build",
                Icon = "construction",
                Title = "Build the box (for the person building it)",
                Why = "Only needed if your box did not arrive built. This is a short version. The full wiring tables are in the Word user guide.",
                Where = AtTheBox,
                Steps =
                {
                    "Load the code: install Arduino IDE 2, add the <b>esp32</b> board package, and the libraries ArduinoJson, DHT sensor library, Adafruit GFX Library, Adafruit SSD1306, WebSockets_Generic, RadioLib and NimBLE-Arduino. Open the sketch for your board, choose the board and port, and click <b>Upload</b>.",
                    "Tower box only: join the controller to the radio board. Controller GPIO 16 goes to radio D7, GPIO 17 goes to D6, and GND to GND. Fit the antenna.",
                    "Tower box only: join the screen. SDA to GPIO 19, SCL to GPIO 20, VCC to 3.3 V, GND to GND.",
                    "Join the sensors one at a time and test each one (see the table).",
                    "Join the two relay boards. Remove the JD-VCC to VCC jumper, give JD-VCC and GND 5 V from a breadboard supply, and join VCC to the controller's 5 V pin. Then join IN1 to IN4 (see the table).",
                    "Check every wire, plug in, and follow <a href='#check'>Check the box works</a>."
                },
                Table = Table(
                    new[] { "Part", "Tower box pin", "Fodder box pin" },
                    new[] { "Light sensor (3.3 V)", "GPIO 1", "GPIO 3" },
                    new[] { "EC (plant food) sensor (5 V)", "GPIO 2", "GPIO 2" },
                    new[] { "pH sensor (5 V)", "GPIO 3", "GPIO 1" },
                    new[] { "Water level sensor (5 V)", "GPIO 4", "GPIO 4" },
                    new[] { "Temperature and humidity (3.3 V)", "GPIO 15", "GPIO 5" },
                    new[] { "230 V relay IN1", "Water pump (GPIO 7)", "Spray (GPIO 38)" },
                    new[] { "230 V relay IN2", "Grow light (GPIO 5)", "Circulation fan (GPIO 39)" },
                    new[] { "230 V relay IN3", "Circulation fan (GPIO 6)", "Extractor fan (GPIO 40)" },
                    new[] { "230 V relay IN4", "Extractor fan (GPIO 14)", "Grow light (GPIO 41)" },
                    new[] { "12 V relay IN1", "pH Up pump (GPIO 18)", "pH Up pump (GPIO 42)" },
                    new[] { "12 V relay IN2", "pH Down pump (GPIO 21)", "pH Down pump (GPIO 6)" },
                    new[] { "12 V relay IN3", "Plant food pump (GPIO 22)", "Plant food pump (GPIO 7)" },
                    new[] { "12 V relay IN4", "Clean water pump (GPIO 23)", "Clean water pump (GPIO 26)" }),
                Notes =
                {
                    Note("stop", "The 230 V relay board carries mains electricity. Unplug the box before you open it, and let a qualified person do all the 230 V wiring."),
                    Note("warn", "Always switch the power off before you connect a part. Fit the LoRa antenna before you switch on, or the radio can be damaged."),
                    Note("info", "Use red jumpers for power, black for ground and blue for the signal. A relay that clicks when the box starts is normal.")
                }
            });

            _pageTasks["setup"] = new List<string> { "choose", "need", "offlinesetup", "check", "gateway", "camera-setup", "website", "app", "build" };
        }
    }
}
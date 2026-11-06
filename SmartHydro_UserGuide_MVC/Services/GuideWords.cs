using SmartHydro_UserGuide_MVC.Models;

namespace SmartHydro_UserGuide_MVC.Services
{
    public partial class GuideContent
    {
        // Builds the Words explained page: the word groups and the table of ways to make Smart Hydro easier to use.
        private void AddWordsPage()
        {
            var page = _pages["words"];

            page.TermGroups = BuildTermGroups();
            page.EaseOfUse = BuildEaseOfUse();
        }

        // Shortens creating one word, any other name it goes by, and what it means.
        private static GuideTerm Term(string word, string alsoCalled, string meaningHtml)
        {
            return new GuideTerm { Word = word, AlsoCalled = alsoCalled, MeaningHtml = meaningHtml };
        }

        // Lists every Smart Hydro word in plain language, grouped by where people meet it.
        private static List<GuideTermGroup> BuildTermGroups()
        {
            return new List<GuideTermGroup>
            {
                new GuideTermGroup
                {
                    Id = "words-farm",
                    Icon = "agriculture",
                    Title = "Your farm",
                    Intro = "Words about your farm, your towers and trays, and the box on each one.",
                    Terms =
                    {
                        // Smart Hydro's own term, defined from how the Smart Hydro system works.
                        Term("Site", "Farm", "One farm. Your first farm is made when a Smart Hydro helper checks and approves your account. You can have more than one."),
                        // Smart Hydro's own term, defined from how the Smart Hydro system works.
                        Term("Structure", "", "One tower, or one set of fodder trays. Each structure has its own sensor box."),
                        // Hydroponics defined from (Britannica Dictionary, 2026a; Dunn and Singh, 2017).
                        Term("Hydroponics", "Hydroponic", "Growing plants in water with plant food, instead of in soil."),
                        // Tower defined from the Smart Hydro structure type, using the meaning of hydroponics from (Britannica Dictionary, 2026a).
                        Term("Tower", "Vertical tower, hydroponic structure", "A tall stand where plants such as spinach grow in water instead of soil."),
                        // Fodder defined from (Heleba, 2016).
                        Term("Fodder trays", "Fodder structure", "Trays where seeds such as barley sprout into fresh feed for animals."),
                        // Smart Hydro's own term, defined from how the Smart Hydro system works.
                        Term("Sensor box", "Box, controller", "The small box on each tower or tray. It checks your plants, and switches the lights, fans, pump and spray on or off. It has a small screen."),
                        // MAC address defined from (Ubuntu, 2026).
                        Term("Sticker code", "MAC address, Controller MAC Address", "The code printed on the sticker on your box, like <span class='sh-mono'>A4:CF:12:9B:3E:01</span>. Every box has its own code. You type it in when you add a tower or tray."),
                        // Smart Hydro's own term, defined from how the Smart Hydro system works.
                        Term("Camera", "Farm camera, ESP32 Camera", "A small camera that takes a photo of your plants once a day, or when you ask for one. It has its own sticker code."),
                        // Nutrient tank defined from (Dunn and Singh, 2017).
                        Term("Water tank", "Reservoir", "The tank that holds the water and plant food for your plants.")
                    }
                },
                new GuideTermGroup
                {
                    Id = "words-readings",
                    Icon = "monitoring",
                    Title = "Numbers and warnings",
                    Intro = "Words you see on the Readings and Alerts screens.",
                    Terms =
                    {
                        // Smart Hydro's own term, defined from how the Smart Hydro system works.
                        Term("Reading", "Number", "Something the box measures, like the temperature. The box checks every few seconds."),
                        // pH defined from (U.S. Geological Survey, 2026). The 5.5 to 6.5 range is Smart Hydro's default healthy range for towers.
                        Term("pH", "", "How sour or soapy the water is. Plants in towers like a pH between 5.5 and 6.5."),
                        // EC defined from (Dunn and Singh, 2017).
                        Term("EC", "Plant food, nutrients", "How much plant food is in the water. A higher number means stronger plant food."),
                        // Everyday word, written in plain language. Smart Hydro shows it in degrees Celsius.
                        Term("Temperature", "", "How hot or cold it is, in degrees (°C)."),
                        // Humidity defined from (American Meteorological Society, 2024).
                        Term("Humidity", "", "How damp the air is, shown as a percentage (%)."),
                        // Smart Hydro's own term, defined from how the Smart Hydro system works.
                        Term("Light", "", "How much light the plants are getting."),
                        // Smart Hydro's own term, defined from how the Smart Hydro system works.
                        Term("Water level", "", "How much water is left in the tank."),
                        // Smart Hydro's own term, defined from how the Smart Hydro system works.
                        Term("Healthy range", "Threshold", "The lowest and highest good number for a reading. Outside it, you get a warning. Each tower or tray has its own."),
                        // Smart Hydro's own term, defined from how the Smart Hydro system works.
                        Term("Green, orange and red", "", "Green means all good. Orange means close to the edge, so keep an eye on it. Red means outside the healthy range, so act now."),
                        // Smart Hydro's own term, defined from how the Smart Hydro system works.
                        Term("Warning", "Alert", "A message that something needs your attention. A red <b>Alert</b> means act now. An orange <b>Caution</b> means watch it."),
                        // Smart Hydro's own term, defined from how the Smart Hydro system works.
                        Term("Acknowledge", "Resolve", "Tap this on a warning once you have sorted it out. The warning moves to <b>Resolved</b>."),
                        // Smart Hydro's own term, defined from how the Smart Hydro system works.
                        Term("Not Reporting", "Not connected, or not reporting", "Smart Hydro has heard nothing from the box for a while. See <a href='~/Home/Troubleshoot'>Fix a problem</a>."),
                        // Smart Hydro's own term, defined from how the Smart Hydro system works.
                        Term("History", "Chart", "The numbers from earlier days, shown as a line so you can see if things are going up or down.")
                    }
                },
                new GuideTermGroup
                {
                    Id = "words-care",
                    Icon = "science",
                    Title = "Looking after your plants",
                    Intro = "Words for the things you and the box do to keep plants healthy.",
                    Terms =
                    {
                        // Dosing defined from (Dunn and Singh, 2017) and the Smart Hydro dose buttons.
                        Term("Dose", "Dosing", "A small amount of liquid the box adds to the water, to fix the pH or the plant food."),
                        // pH up and pH down products defined from (Dunn and Singh, 2017).
                        Term("Raise pH and Lower pH", "pH up, pH down", "Buttons that add a liquid to make the water less sour (raise) or more sour (lower)."),
                        // Adding nutrient concentrate to raise EC defined from (Dunn and Singh, 2017).
                        Term("Add nutrients", "Nutrients (EC Up)", "A button that adds plant food to the water."),
                        // Diluting with water to lower EC defined from (Dunn and Singh, 2017).
                        Term("Add clean water", "Dilute (EC Down)", "A button that adds clean water, to make the plant food weaker. The phone app calls it Dilute."),
                        // Smart Hydro's own term, defined from how the Smart Hydro system works.
                        Term("Automatic", "", "Something the box does by itself, like turning the light on in the morning or adding a little pH liquid."),
                        // Smart Hydro's own term, defined from how the Smart Hydro system works.
                        Term("Switch", "Toggle", "An on and off button on the screen. It turns green when the thing is on."),
                        // Smart Hydro's own term, defined from how the Smart Hydro system works.
                        Term("Override", "", "When you switch something yourself, the box follows you for 1 hour before it goes back to doing things by itself."),
                        // Smart Hydro's own term, defined from how the Smart Hydro system works.
                        Term("Water pump", "Pump", "Moves water up the tower to the plants."),
                        // Smart Hydro's own term, defined from how the Smart Hydro system works.
                        Term("Spray", "Misting", "Sprays water over fodder trays to keep the seeds damp."),
                        // Circulation fans defined from (Bartok, 2026).
                        Term("Circulation fan", "", "Moves the air around the plants."),
                        // Exhaust fans defined from (University of Vermont, 2026).
                        Term("Extractor fan", "", "Pushes hot or damp air out."),
                        // Grow lights defined from (Weisenhorn and Hoidal, 2026).
                        Term("Grow light", "Lights", "A lamp that gives the plants light when there is not enough sun.")
                    }
                },
                new GuideTermGroup
                {
                    Id = "words-connect",
                    Icon = "cell_tower",
                    Title = "Connecting",
                    Intro = "Words about how your phone, the box and Smart Hydro talk to each other.",
                    Terms =
                    {
                        // Smart Hydro's own term, defined from how the Smart Hydro system works.
                        Term("Online", "", "Using Smart Hydro while signed in, with internet. You can check your farm from anywhere."),
                        // Smart Hydro's own term, defined from how the Smart Hydro system works.
                        Term("Offline mode", "Continue Offline", "Using the phone app with no account and no internet, standing near your box. Your phone talks straight to the box over Bluetooth, or over the box's own Wi-Fi as a backup. See <a href='~/Home/Account#offline'>Offline mode</a>."),
                        // Smart Hydro's own term, defined from how the Smart Hydro system works.
                        Term("Local structures", "", "The towers and trays you added in offline mode. They are kept on your phone only."),
                        // Internet defined from (Britannica Dictionary, 2026b).
                        Term("Internet", "", "What lets your phone or computer reach websites and Smart Hydro from anywhere. It comes from Wi-Fi or mobile data."),
                        // Wi-Fi defined from (Cisco, 2026).
                        Term("Wi-Fi", "", "A wireless connection. Some Wi-Fi has internet, like home Wi-Fi. The box's own Wi-Fi does not."),
                        // SoftAP (access point) mode defined from (Espressif Systems, 2026).
                        Term("Box's own Wi-Fi", "SoftAP, hotspot of the box", "A Wi-Fi the box makes by itself, so your phone can talk to it with no internet. Its name starts with <b>SmartHydro-</b> and it has no password."),
                        // Mobile data defined from (Android Help, 2026a).
                        Term("Mobile data", "Data, airtime", "Internet on your phone that comes from your SIM card. It costs airtime or a data bundle."),
                        // Hotspot defined from (Android Help, 2026a).
                        Term("Phone hotspot", "", "When your phone shares its mobile data as a Wi-Fi, so another device can use its internet."),
                        // LoRa defined from (The Things Network, 2017).
                        Term("LoRa", "Radio", "Long-range radio. The box uses it to send its numbers across the farm without Wi-Fi."),
                        // Gateway defined from (The Things Network, 2026).
                        Term("Gateway", "Receiver box", "A box on the farm that picks up the radio from every tower and tray, and passes the numbers on to Smart Hydro using its own SIM card."),
                        // Bluetooth defined from (Encyclopaedia Britannica, 2026).
                        Term("Bluetooth", "BLE", "A short-range wireless link that works when a phone is close to the box. The phone app uses it to talk to the box with no internet. The box screen shows it as BLE."),

                        Term("Pairing", "Pair with your box", "Linking your phone to a box over Bluetooth, once, by typing the 6-digit code shown on the box screen. See <a href='~/Home/Owners#pair'>Offline mode: connect your phone to the box</a>."),
                        // Smart Hydro's own term, defined from how the Smart Hydro system works.
                        Term("LoRa Mode, Online Mode, SoftAP Mode", "LoRa, Online, Hotspot", "Three buttons that choose how the box connects: on the website, and in the phone app over Bluetooth, where they are called LoRa, Online and Hotspot. See <a href='~/Home/Owners#modes'>How the box connects</a>."),
                        // Smart Hydro's own term, defined from how the Smart Hydro system works.
                        Term("BOOT button", "PRG", "A small button on the box. Hold it for 3 seconds to turn the box's own Wi-Fi on or off."),
                        // Smart Hydro's own term, defined from how the Smart Hydro system works.
                        Term("Simple Mode", "", "An easier version of the phone app with everything on one screen and big buttons. It is always on for workers. See <a href='~/Home/Workers#worker-simple'>Understanding Simple Mode on your phone</a>.")
                    }
                },
                new GuideTermGroup
                {
                    Id = "words-account",
                    Icon = "person",
                    Title = "Accounts and people",
                    Intro = "Words about signing in and the people on your team.",
                    Terms =
                    {
                        // Smart Hydro's own term, defined from how the Smart Hydro system works.
                        Term("Account", "", "Your own sign-in to Smart Hydro, made with your email address and a password."),
                        // Smart Hydro's own term, defined from how the Smart Hydro system works.
                        Term("Sign in", "Log in", "Opening your account by typing your email and password, then a code from your email."),
                        // Smart Hydro's own term, defined from how the Smart Hydro system works.
                        Term("Sign out", "Log out", "Closing your account on this phone or computer, so nobody else can use it."),
                        // One-time code defined from (Richards and Wigmore, 2023).
                        Term("Sign-in code", "One-time code", "A 6-number code Smart Hydro emails you. It proves it is really you. It works for 10 minutes."),
                        // Spam folder defined from (University of Michigan, 2026).
                        Term("Spam folder", "Junk folder", "A folder in your email where some emails land by mistake. Look here if the Smart Hydro email does not arrive."),
                        // Smart Hydro's own term, defined from how the Smart Hydro system works.
                        Term("Operator", "Farm owner", "The person who owns the farm. They can change things and invite workers."),
                        // Smart Hydro's own term, defined from how the Smart Hydro system works.
                        Term("Worker", "", "Someone who helps on the farm. They can look at everything, use the switches, add pH or plant food and add photos, but cannot change the farm's settings."),
                        // Smart Hydro's own term, defined from how the Smart Hydro system works.
                        Term("Smart Hydro helper", "Support team", "The Smart Hydro team. They check new farm owners and can help when something goes wrong."),
                        // Smart Hydro's own term, defined from how the Smart Hydro system works.
                        Term("Invitation", "Invite", "An email a farm owner sends to add a worker to the team. It works for 24 hours."),
                        // Smart Hydro's own term, defined from how the Smart Hydro system works.
                        Term("Access code", "Invite code", "The code in an invitation email, like <b>HYDR-9X2W</b>. A worker types it in to join the team.")
                    }
                },
                new GuideTermGroup
                {
                    Id = "words-phone",
                    Icon = "smartphone",
                    Title = "Phones, apps and websites",
                    Intro = "Everyday words for using a phone, an app or a website.",
                    Terms =
                    {
                        // Everyday word, written in plain language.
                        Term("App", "Phone app", "A program on your phone. The Smart Hydro app works on Android phones."),
                        // APK file defined from (Android Open Source Project, 2026).
                        Term("APK file", ".apk", "The file used to put the Smart Hydro app on your phone. Ask your Smart Hydro contact for it."),
                        // Everyday word, written in plain language.
                        Term("Website", "", "Smart Hydro on a computer, tablet or phone, opened in a web browser."),
                        // Browser defined from (MDN Web Docs, 2026).
                        Term("Browser", "Chrome, Edge, Firefox", "The program you use to open websites."),
                        // Bookmarks defined from (Google Chrome Help, 2026).
                        Term("Bookmark", "", "A saved link in your browser, so you can open the website again quickly."),
                        // Everyday word, written in plain language.
                        Term("Tap", "", "Touch the screen once with your finger, then lift it."),
                        // Smart Hydro's own term, defined from how the Smart Hydro system works.
                        Term("Refresh", "Pull down", "Get the newest numbers. In the app, slide your finger down from the top of the screen. On the website, press the round arrow."),
                        // Smart Hydro's own term, defined from how the Smart Hydro system works.
                        Term("Menu", "", "The list of pages. On a phone, tap the three lines at the top left to open it."),
                        // Smart Hydro's own term, defined from how the Smart Hydro system works.
                        Term("Settings", "", "The page in the app where you change things like dark mode, or sign in and out."),
                        // Dark theme defined from (Android Help, 2026b).
                        Term("Dark mode", "", "Shows the screen with a dark background, which is easier on the eyes at night."),
                        // High-contrast text defined from (Android Accessibility Help, 2026b).
                        Term("High contrast", "", "Makes text and buttons stand out more, so they are easier to read."),
                        // Screen reader defined from (Android Accessibility Help, 2026a).
                        Term("Read aloud", "Screen reader, TalkBack", "Lets the phone or computer read the words on the screen out loud.")
                    }
                }
            };
        }

        // Lists the ways to make Smart Hydro easier to see, hear and use, on the website and in the phone app (Android Accessibility Help, 2026a; Android Accessibility Help, 2026b; Android Help, 2026b).
        private static GuideTable BuildEaseOfUse()
        {
            return Table(
                new[] { "To", "On the website", "In the phone app" },
                new[] { "Hear the page read out", "Tap the speaking-person button at the top right to turn read aloud on. Then tap the speaker button to hear the page. To stop, tap the speaking-person button again.", "Settings, then Screen Reader (TalkBack)" },
                new[] { "Make it darker", "Tap the moon button at the top right.", "Settings, then Dark Mode" },
                new[] { "Make text stand out more", "Tap the half-circle button at the top right. It stays on next time.", "Settings, then High Contrast" },
                new[] { "Make text bigger", "On a computer, hold Ctrl and press +. On a phone, pinch the screen with two fingers to zoom in.", "Your phone's Settings, then Display, then Font size" },
                new[] { "Use a keyboard", "Press Tab to move from button to button. The first Tab shows Skip to main content.", "Use the Next key on the keyboard to move to the next box." });
        }
    }
}

/*
    Reference List:

    American Meteorological Society, 2024. Relative humidity. [online] Glossary of Meteorology. Available at: <https://glossary.ametsoc.org/wiki/Relative_humidity> [Accessed 24 October 2026].

    Android Accessibility Help, 2026a. Get started on Android with TalkBack. [online] Available at: <https://support.google.com/accessibility/android/answer/6283677> [Accessed 24 October 2026].

    Android Accessibility Help, 2026b. Change text & display settings. [online] Available at: <https://support.google.com/accessibility/android/answer/11183305> [Accessed 24 October 2026].

    Android Help, 2026a. Share a mobile connection by hotspot or tethering on Android. [online] Available at: <https://support.google.com/android/answer/9059108> [Accessed 24 October 2026].

    Android Help, 2026b. Change to dark theme or adjust the color scheme on your Android device. [online] Available at: <https://support.google.com/android/answer/9730472> [Accessed 24 October 2026].

    Android Open Source Project, 2026. Android platform glossary. [online] Available at: <https://source.android.com/docs/setup/start/glossary> [Accessed 24 October 2026].

    Bartok, J.W., 2026. Horizontal air flow is best for greenhouse air circulation. [online] UMass Extension Greenhouse Crops and Floriculture Program. Available at: <https://www.umass.edu/agriculture-food-environment/greenhouse-floriculture/fact-sheets/horizontal-air-flow-is-best-for-greenhouse-air-circulation> [Accessed 24 October 2026].

    Britannica Dictionary, 2026a. Hydroponics definition and meaning. [online] Available at: <https://www.britannica.com/dictionary/hydroponics> [Accessed 24 October 2026].

    Britannica Dictionary, 2026b. Internet definition and meaning. [online] Available at: <https://www.britannica.com/dictionary/internet> [Accessed 24 October 2026].

    Cisco, 2026. What is Wi-Fi? [online] Available at: <https://www.cisco.com/c/en/us/products/wireless/what-is-wifi.html> [Accessed 24 October 2026].

    Dunn, B. and Singh, H., 2017. Electrical conductivity and pH guide for hydroponics. [online] Oklahoma State University Extension. Available at: <https://extension.okstate.edu/fact-sheets/electrical-conductivity-and-ph-guide-for-hydroponics.html> [Accessed 24 October 2026].

    Encyclopaedia Britannica, 2026. What is Bluetooth? [online] Available at: <https://www.britannica.com/question/What-is-Bluetooth> [Accessed 24 October 2026].

    Espressif Systems, 2026. Wi-Fi API: Arduino ESP32 documentation. [online] Available at: <https://docs.espressif.com/projects/arduino-esp32/en/latest/api/wifi.html> [Accessed 24 October 2026].

    Google Chrome Help, 2026. Create, find and edit bookmarks in Chrome. [online] Available at: <https://support.google.com/chrome/answer/188842> [Accessed 24 October 2026].

    Heleba, D., 2016. Fodder systems on the certified organic dairy farm. [pdf] University of Vermont Extension Northwest Crops and Soils Program. Available at: <https://uvm.edu/d10-files/documents/2024-06/FAFOFodderSystems.pdf> [Accessed 24 October 2026].

    MDN Web Docs, 2026. Browser. [online] Available at: <https://developer.mozilla.org/en-US/docs/Glossary/Browser> [Accessed 24 October 2026].

    Richards, K. and Wigmore, I., 2023. What is a one-time password (OTP)? [online] TechTarget SearchSecurity. Available at: <https://www.techtarget.com/searchsecurity/definition/one-time-password-OTP> [Accessed 24 October 2026].

    The Things Network, 2017. LoRaWAN 101: An overview. [online] Available at: <https://www.thethingsnetwork.org/article/lorawan-101-an-overview> [Accessed 24 October 2026].

    The Things Network, 2026. LoRaWAN architecture. [online] Available at: <https://www.thethingsnetwork.org/docs/lorawan/architecture/> [Accessed 24 October 2026].

    U.S. Geological Survey, 2026. pH and water. [online] Water Science School. Available at: <https://www.usgs.gov/water-science-school/science/ph-and-water> [Accessed 24 October 2026].

    Ubuntu, 2026. What is a MAC address? [online] Ubuntu Desktop Guide. Available at: <https://help.ubuntu.com/stable/ubuntu-help/net-macaddress.html> [Accessed 24 October 2026].

    University of Michigan, 2026. Google: Mark/unmark email as spam. [online] Available at: <https://teamdynamix.umich.edu/TDClient/30/Portal/KB/ArticleDet?ID=9485> [Accessed 24 October 2026].

    University of Vermont, 2026. Ventilation in greenhouses and high tunnels. [online] Available at: <https://blog.uvm.edu/cwcallah/?p=2360> [Accessed 24 October 2026].

    Weisenhorn, J. and Hoidal, N., 2026. Lighting for indoor plants and starting seeds. [online] University of Minnesota Extension. Available at: <https://extension.umn.edu/planting-and-growing-guides/lighting-indoor-plants> [Accessed 24 October 2026].
*/
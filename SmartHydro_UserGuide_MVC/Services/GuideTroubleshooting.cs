using SmartHydro_UserGuide_MVC.Models;

namespace SmartHydro_UserGuide_MVC.Services
{
    public partial class GuideContent
    {
        // Builds the Fix a problem page: quick checks, the question by question troubleshooter, the messages table and the common questions.
        private void AddTroubleshootPage()
        {
            var page = _pages["troubleshoot"];

            page.QuickChecks = BuildQuickChecks();
            page.Questions = BuildQuestions();
            page.Fixes = BuildFixes();
            page.Messages = BuildMessages();
            page.FaqGroups = BuildFaqGroups();
        }

        // Shortens creating one answer button that leads to a question or a fix.
        private static GuideAnswer Answer(string icon, string label, string next)
        {
            return new GuideAnswer { Icon = icon, Label = label, Next = next };
        }

        // Shortens creating a fix with its steps and an optional link to the guide section that shows how.
        private static GuideFix Fix(string id, string title, string linkHref, string linkLabel, params string[] steps)
        {
            return new GuideFix { Id = id, Title = title, LinkHref = linkHref, LinkLabel = linkLabel, Steps = steps.ToList() };
        }

        // Shortens creating one common question and its answer.
        private static GuideFaq Faq(string question, string answerHtml)
        {
            return new GuideFaq { Question = question, AnswerHtml = answerHtml };
        }


        // Lists the few things worth checking before anything else, because they fix most problems.
        private static List<GuideNote> BuildQuickChecks()
        {
            return new List<GuideNote>
            {
                Note("info", "<b>Online or offline mode?</b> If you tapped <span class='ui'>Continue Offline</span>, you are in offline mode. Your phone must then be close to the box and connected over Bluetooth, or on the box's own Wi-Fi."),
                Note("info", "<b>Is the box on?</b> Look at the small screen on the box. If it is dark, the box has no power."),
                Note("info", "<b>What does the box screen say?</b> Look at the top line of a tower box, or the bottom line of a fodder box. It tells you how the box is sending its numbers."),
                Note("info", "<b>Is the radio receiver on?</b> Most boxes send their numbers by radio to a receiver box on the farm (the gateway). It needs power and airtime on its SIM card."),
                Note("info", "<b>Is your phone connected the right way?</b> For offline mode, turn on Bluetooth (or join the box's Wi-Fi as a backup). To sign in or use the website, you need internet."),
                Note("info", "<b>Is there water in the tank?</b> A low tank causes warnings. Fill it before you add pH liquid or plant food, because the box does not check the tank before it doses.")
            };
        }

        // Defines every question in the troubleshooter and which question or fix each answer leads to.
        private static List<GuideQuestion> BuildQuestions()
        {
            return new List<GuideQuestion>
            {
                new GuideQuestion
                {
                    Id = "start",
                    Text = "What is wrong?",
                    Answers =
                    {
                        Answer("wifi_off", "I'm using offline mode and something isn't working", "offline"),
                        Answer("bluetooth_disabled", "I can't connect my phone to the box over Bluetooth", "bt"),
                        Answer("lock", "I can't sign in", "signin"),
                        Answer("key", "I forgot my password", "f_forgot"),
                        Answer("sensors_off", "I can't see any numbers for my plants", "box"),
                        Answer("toggle_off", "I pressed a switch or a dose button and nothing happened", "control"),
                        Answer("notifications_active", "I keep getting warnings", "warn"),
                        Answer("swap_horiz", "I changed how the box connects and it didn't work", "mode"),
                        Answer("no_photography", "The camera photo does not come", "cam"),
                        Answer("group_off", "My worker can't join my team", "team"),
                        Answer("visibility_off", "I can't find a button", "missing")
                    }
                },
                new GuideQuestion
                {
                    Id = "offline",
                    Text = "What happens in offline mode?",
                    Answers =
                    {
                        Answer("search_off", "I can't see Pair with your box", "f_signedin"),
                        Answer("bluetooth_disabled", "I can't connect over Bluetooth", "bt"),
                        Answer("wifi_find", "I can't find the box's Wi-Fi on my phone", "f_nossid"),
                        Answer("portable_wifi_off", "The app says the box didn't answer", "f_noanswer"),
                        Answer("swap_horiz", "My phone keeps leaving the box's Wi-Fi", "f_mobile"),
                        Answer("directions_walk", "The numbers stop when I walk away", "f_range"),
                        Answer("notifications_off", "I don't get any warnings", "f_offalerts"),
                        Answer("folder_off", "My towers or photos are gone after I signed in", "f_offlocal")
                    }
                },
                new GuideQuestion
                {
                    Id = "bt",
                    Text = "What does the app say?",
                    Answers =
                    {
                        Answer("bluetooth_disabled", "Bluetooth is turned off", "f_btoff"),
                        Answer("block", "The app isn't allowed to use Bluetooth yet", "f_btperm"),
                        Answer("search_off", "No box found nearby", "f_btnobox"),
                        Answer("pin", "Couldn't connect, or it asks for a code", "f_btcode"),
                        Answer("cell_tower", "I can't see the mode buttons", "f_btmodes")
                    }
                },
                new GuideQuestion
                {
                    Id = "signin",
                    Text = "What happened when you tried to sign in?",
                    Answers =
                    {
                        Answer("mail", "The email with the code never came", "f_code"),
                        Answer("pin", "It says my code is wrong", "f_codewrong"),
                        Answer("password", "It says my email or password is wrong", "f_pass"),
                        Answer("timer", "It says too many attempts", "f_lock"),
                        Answer("hourglass_top", "I made an account but I still can't get in", "f_pending"),
                        Answer("link_off", "My invitation link does not work", "f_invite")
                    }
                },
                new GuideQuestion
                {
                    Id = "box",
                    Text = "Is the small screen on the box lit up?",
                    Answers =
                    {
                        Answer("power_off", "No, it is dark", "f_power"),
                        Answer("power", "Yes, it is on", "box2")
                    }
                },
                new GuideQuestion
                {
                    Id = "box2",
                    Text = "What does the box screen say?",
                    Answers =
                    {
                        Answer("cell_tower", "LORA + BLE (fodder box: GW:)", "f_lora"),
                        Answer("wifi", "CLOUD WIFI (fodder box: WiFi:)", "f_wifi"),
                        Answer("wifi_tethering", "EMERGENCY SOFTAP (tower box only)", "f_portal"),
                        Answer("help", "Something else, or I'm not at the farm", "box3")
                    }
                },
                new GuideQuestion
                {
                    Id = "box3",
                    Text = "What do you see instead of numbers?",
                    Answers =
                    {
                        Answer("computer", "The website says Not Reporting", "f_notreporting"),
                        Answer("smartphone", "The app says Not connected, or not reporting", "f_appnoreading"),
                        Answer("hourglass_empty", "It says No data yet", "f_nodata")
                    }
                },
                new GuideQuestion
                {
                    Id = "control",
                    Text = "What happened?",
                    Answers =
                    {
                        Answer("computer", "I used a switch on the website and the box did not change", "f_webcontrol"),
                        Answer("smartphone", "I used a switch in offline mode and nothing happened", "f_phonecontrol"),
                        Answer("undo", "The switch went back by itself later", "f_override"),
                        Answer("block", "The dose button said no, or did nothing", "f_doserefused"),
                        Answer("science", "I added a dose but the number did not change", "f_dosewait")
                    }
                },
                new GuideQuestion
                {
                    Id = "warn",
                    Text = "What is the warning about?",
                    Answers =
                    {
                        Answer("water_ph", "pH or plant food (EC)", "f_phec"),
                        Answer("device_thermostat", "Too hot, too cold or too damp", "f_climate"),
                        Answer("water_drop", "The water level", "f_water"),
                        Answer("error", "A number that looks impossible, like pH 0 or 14", "f_sensor")
                    }
                },
                new GuideQuestion
                {
                    Id = "mode",
                    Text = "Which button did you press?",
                    Answers =
                    {
                        Answer("wifi", "Online Mode (Online in the app)", "f_online"),
                        Answer("router", "SoftAP Mode (Hotspot in the app)", "f_softap"),
                        Answer("cell_tower", "LoRa Mode (LoRa in the app)", "f_loramode"),
                        Answer("error", "The website said Could not activate", "f_modefail"),
                        Answer("visibility_off", "I can't see the mode buttons in the app", "f_btmodes")
                    }
                },
                new GuideQuestion
                {
                    Id = "cam",
                    Text = "What is happening with the camera?",
                    Answers =
                    {
                        Answer("schedule", "I asked for a photo just now", "f_wait"),
                        Answer("history", "No new photos for a day or more", "f_camwifi"),
                        Answer("wifi_tethering", "My phone shows a Wi-Fi called SmartHydro-CAM-Setup", "f_camsetup")
                    }
                },
                new GuideQuestion
                {
                    Id = "team",
                    Text = "What does your worker see?",
                    Answers =
                    {
                        Answer("timer_off", "The invitation has expired", "f_invite"),
                        Answer("pin", "Invalid or expired access code", "f_invitecode"),
                        Answer("mail", "They never got the email", "f_invitemail"),
                        Answer("block", "I see Invite quota reached", "f_quota")
                    }
                },
                new GuideQuestion
                {
                    Id = "missing",
                    Text = "Which button can't you find?",
                    Answers =
                    {
                        Answer("tune", "Healthy ranges, the mode buttons or the team (workers)", "f_worker"),
                        Answer("view_agenda", "The bar of buttons at the bottom of the phone app", "f_simple"),
                        Answer("cell_tower", "LoRa Mode, Online Mode and SoftAP Mode", "f_nomodebuttons"),
                        Answer("monitoring", "Readings is grey", "f_choose"),
                        Answer("delete", "Delete for a farm", "f_deletesite")
                    }
                }
            };
        }

        // Defines every fix the troubleshooter can end on, with the steps to try and where to read more.
        private static List<GuideFix> BuildFixes()
        {
            return new List<GuideFix>
            {
                Fix("f_signedin", "Use offline mode", "~/Home/Account#offline", "Offline mode",
                    "<span class='ui'>Pair with your box</span> only shows in offline mode.",
                    "If you are signed in, go to <b>Settings</b> and tap <span class='ui'>Sign Out</span>.",
                    "On the sign-in screen, tap <span class='ui'>Continue Offline</span>.",
                    "Choose your tower or tray. It must have the code from the box sticker saved."),

                Fix("f_nossid", "Turn on the box's Wi-Fi", "~/Home/Owners#pair-wifi", "Offline mode: use the box's own Wi-Fi",
                    "Try Bluetooth first. It works without the box's Wi-Fi.",
                    "The box only makes its own Wi-Fi after you turn it on. Press and hold the <b>BOOT</b> button on the box for 3 seconds, then let go.",
                    "On a tower, check the box screen shows <b>EMERGENCY SOFTAP</b>. A fodder screen does not change. Stand close to the box and open your phone's Wi-Fi list again.",
                    "Look for a name that starts with <b>SmartHydro-Setup-</b> (tower box) or <b>SmartHydro-Fodder-</b> (fodder box). The finder in the guide shows the exact name."),

                Fix("f_noanswer", "Join the box's Wi-Fi", "~/Home/Owners#pair-wifi", "Offline mode: use the box's own Wi-Fi",
                    "On a tower, check the box screen shows <b>EMERGENCY SOFTAP</b>. On a fodder box, check its Wi-Fi is in your phone's list. If not, hold <b>BOOT</b> for 3 seconds.",
                    "On your phone, join the Wi-Fi that starts with <b>SmartHydro-</b>.",
                    "Go back to the app and tap <span class='ui'>Test Connection</span>.",
                    "Or skip the Wi-Fi and connect over Bluetooth instead (see <a href='~/Home/Owners#pair'>Offline mode: connect your phone to the box</a>)."),

                Fix("f_mobile", "Tell your phone to stay on the box's Wi-Fi", "", "",
                    "The box's Wi-Fi has no internet, so some phones jump away from it.",
                    "If your phone asks, tap <b>Stay connected</b> or <b>Keep Wi-Fi</b>.",
                    "Or switch off mobile data while you use the box."),

                Fix("f_range", "Stay close to the box", "~/Home/Owners#pair", "Offline mode: connect your phone to the box",
                    "In offline mode, the numbers come straight from the box over Bluetooth or its Wi-Fi.",
                    "When you walk away, your phone loses that link and the numbers stop.",
                    "Go back near the box and pull the screen down to get the newest numbers."),

                Fix("f_offalerts", "Warnings need you to be online", "~/Home/Account#offline", "Offline mode",
                    "Offline mode does not show warnings. You can still see if a number is green, orange or red.",
                    "To get warnings, sign in when you have internet: go to <b>Settings</b> and tap <span class='ui'>Sign In to Go Online</span>.",
                    "The box keeps looking after your plants either way."),

                Fix("f_offlocal", "Offline things stay on this phone", "~/Home/Account#offline", "Offline mode",
                    "Towers, trays and photos you add in offline mode are kept on this phone only.",
                    "They are not part of your online account, so you will not see them while signed in.",
                    "Sign out and tap <span class='ui'>Continue Offline</span> to see them again, or add the tower again while signed in."),

                Fix("f_code", "Look in your Spam folder", "~/Home/Account#signin", "Sign in",
                    "Open your email. Look in the <b>Spam</b> or <b>Junk</b> folder for an email from Smart Hydro.",
                    "Still nothing? Wait one minute, then tap <span class='ui'>Resend code</span>.",
                    "Check you typed your email address correctly."),

                Fix("f_codewrong", "Use the newest code", "~/Home/Account#signin", "Sign in",
                    "A code only works for 10 minutes. After that, tap <span class='ui'>Resend code</span>.",
                    "If you have more than one email, use the code in the newest one.",
                    "After 5 wrong tries the code stops working. Ask for a new one."),

                Fix("f_forgot", "Make a new password", "~/Home/Account#forgot", "I forgot my password",
                    "On the sign-in screen, tap <span class='ui'>Forgot password?</span>",
                    "Type your email address. Smart Hydro emails you a code.",
                    "Type the code from the email. It works for 10 minutes.",
                    "Choose a new password and type it twice. It can't be one of your last 5 passwords.",
                    "Sign in with your new password."),

                Fix("f_pass", "Check what you typed", "~/Home/Account#forgot", "I forgot my password",
                    "Check your email address is spelled correctly.",
                    "Check Caps Lock is off. Passwords care about capital letters.",
                    "Forgot your password? Tap <span class='ui'>Forgot password?</span>"),

                Fix("f_lock", "Wait 15 minutes", "", "",
                    "After 5 wrong passwords, Smart Hydro stops you signing in for 15 minutes. This keeps your farm safe.",
                    "Wait, then try again. Not sure of your password? Reset it instead."),

                Fix("f_pending", "Wait for the email", "~/Home/Account#register", "Make an account",
                    "A Smart Hydro helper checks every new farm owner first.",
                    "You will get an email when you can sign in.",
                    "Waiting a long time? Email <span class='sh-mono'>smarthydrosupport@gmail.com</span>."),

                Fix("f_invite", "Ask for a new invitation", "~/Home/Owners#invite", "Add a worker to your team",
                    "An invitation only works for 24 hours.",
                    "Ask the farm owner to send a new one. Use the newest email."),

                Fix("f_invitecode", "Check the code", "~/Home/Account#join", "Join your farm team",
                    "Type the code exactly as it is in the email, like <b>HYDR-9X2W</b>.",
                    "Use the same email address the invitation was sent to.",
                    "After 3 wrong tries, or after 24 hours, the code stops working. Ask for a new invitation."),

                Fix("f_invitemail", "Check the email address", "~/Home/Owners#invite", "Add a worker to your team",
                    "Ask your worker to look in their <b>Spam</b> or <b>Junk</b> folder.",
                    "Check the email address you typed. If it was wrong, send a new invitation."),

                Fix("f_quota", "Wait 10 minutes", "", "",
                    "You can send 5 invitations every 10 minutes.",
                    "Wait a little, then send the rest."),

                Fix("f_power", "Check the plug", "", "",
                    "Check the cable is pushed in at both ends, and the wall socket is switched on.",
                    "When the screen lights up, wait about a minute for the box to start.",
                    "Then pull the screen down in the app, or refresh the website."),

                Fix("f_lora", "The box is sending by radio", "~/Home/Owners#modes", "How the box connects",
                    "This is the normal setting. The box sends its numbers by radio to the receiver box on the farm (the gateway) every 15 seconds from a fodder box and every 17.5 seconds from a tower box.",
                    "Check the gateway is plugged in and its SIM card has airtime.",
                    "If the gateway loses signal, it keeps the numbers and sends them later.",
                    "Still nothing? Move the box and the gateway closer together, or higher up, away from metal.",
                    "On the farm you can also use offline mode to see the numbers straight from the box."),

                Fix("f_wifi", "Check the Wi-Fi", "~/Home/Owners#modes", "How the box connects",
                    "The box is using Wi-Fi. Check that Wi-Fi still has internet. Try it on your phone.",
                    "If the Wi-Fi name or password has changed, ask your Smart Hydro contact to update the box.",
                    "Or press <b>LoRa Mode</b> on the website, or tap <b>LoRa</b> in the phone app, to go back to radio."),

                Fix("f_portal", "The box has its own Wi-Fi turned on", "~/Home/Owners#pair-wifi", "Offline mode: use the box's own Wi-Fi",
                    "The box is ready for offline mode. Its numbers still go by radio too.",
                    "Join its Wi-Fi with your phone in offline mode, or hold <b>BOOT</b> for 3 seconds to turn its Wi-Fi off."),

                Fix("f_notreporting", "The website has not heard from the box", "~/Home/Owners#readings", "Check how your plants are doing",
                    "<b>Not Reporting</b> means no numbers have arrived in the last 2 minutes.",
                    "Ask someone at the farm to check the box screen is lit, then start these questions again.",
                    "Also check the receiver box on the farm (the gateway) has power and airtime."),

                Fix("f_appnoreading", "Check how the app is connected", "~/Home/Owners#pair", "Offline mode: connect your phone to the box",
                    "Signed in? The app needs internet. Pull the screen down to try again.",
                    "In offline mode? Your phone must be near the box and connected over Bluetooth, or on the box's own Wi-Fi.",
                    "Tap <span class='ui'>Connect your box</span> or <span class='ui'>Connect now</span> to connect again over Bluetooth."),

                Fix("f_nodata", "The box has never sent anything", "~/Home/Owners#structure", "Add a tower or tray",
                    "Check the code saved for the tower or tray matches the sticker on the box exactly.",
                    "Make sure the box is plugged in and has been on for a few minutes."),

                Fix("f_webcontrol", "Give it a few seconds", "~/Home/Owners#control", "Switch things on or off",
                    "Changes usually reach the box in a few seconds. The website shows what the box last reported, for example <b>Last reported ON</b>.",
                    "If the page says <b>Not Reporting</b>, or <b>Reports are stale or unavailable</b>, the box cannot hear the website. Fix that first.",
                    "Farm owners and workers can use the switches on their farms."),

                Fix("f_phonecontrol", "Check your phone is still connected to the box", "~/Home/Owners#pair", "Offline mode: connect your phone to the box",
                    "Look under the name of the tower or tray. It should say <b>Connected via Bluetooth</b> or <b>Connected via Hotspot</b>.",
                    "Not connected? Stand close to the box and tap <span class='ui'>Connect your box</span> or <span class='ui'>Connect now</span>.",
                    "Using the box's Wi-Fi? Check your phone is on the Wi-Fi that starts with <b>SmartHydro-</b>, then tap <span class='ui'>Test Connection</span>.",
                    "If the app says <b>The box is offline right now</b>, connect again and try once more."),

                Fix("f_override", "This is normal", "~/Home/Owners#control", "Switch things on or off",
                    "The box does what you say for 1 hour. Then it goes back to its own timers.",
                    "The water pump runs for 15 minutes when you switch it on, then turns off by itself.",
                    "If the box keeps changing something you don't want, change the healthy range instead."),

                Fix("f_doserefused", "Wait between doses", "~/Home/Owners#dose", "Fix the pH or nutrients",
                    "The box only runs one pump at a time. Wait for the first dose to finish.",
                    "After a pH dose the box waits about 15 minutes, and after plant food or clean water about 10 minutes. The website says <b>The waiting period after the previous dose has not finished.</b>",
                    "It adds no more than about 10 mL of pH liquid an hour. The website says <b>The hourly pH dose limit has been reached.</b>",
                    "A tower box will not dose while its pH or EC readings are faulty, too old or still settling. Check the sensors are in the water and plugged in.",
                    "If the website says <b>Dosing blocked</b>, Smart Hydro does not know if the last dose ran. Check the box and the tank before you try again."),

                Fix("f_dosewait", "Give the water time to mix", "~/Home/Owners#dose", "Fix the pH or nutrients",
                    "The liquid takes a few minutes to mix into the water.",
                    "Wait about 15 minutes after a pH dose, or 10 minutes after plant food or clean water, then check the number again before adding more.",
                    "Still no change? Check the bottle is not empty and its tube is not blocked."),

                Fix("f_phec", "Check the sensor, then add one dose", "~/Home/Owners#dose", "Fix the pH or nutrients",
                    "Check the pH and plant food sensors are under the water and their cables are plugged in.",
                    "Press the right dose button once, then wait a few minutes.",
                    "The box also fixes pH and plant food by itself in small amounts. Give it time."),

                Fix("f_climate", "Let the fans do their job", "~/Home/Owners#ranges", "Change the healthy range",
                    "The box turns its fans on by itself when it gets too hot or too damp.",
                    "On very hot days, open doors or add shade.",
                    "Check nothing is blocking the fans."),

                Fix("f_water", "Fill up the tank", "", "",
                    "Fill the water tank. The number turns green again on the next update.",
                    "Check the water level sensor is in place and plugged in.",
                    "Then tap <span class='ui'>Acknowledge</span> on the warning."),

                Fix("f_sensor", "Check the sensor", "", "",
                    "A strange number usually means a sensor is out of the water or unplugged.",
                    "Check the cable, rinse the sensor in clean water and put it back in the tank.",
                    "Do not add any liquid until the number looks normal again."),

                Fix("f_online", "Check the box joined the Wi-Fi", "~/Home/Owners#modes", "How the box connects",
                    "Wait about a minute, then check <b>Controller connection</b> on the Readings page, or the box screen.",
                    "A fodder box that cannot join the Wi-Fi goes back to radio after about 3 minutes, so it keeps working. A tower box keeps trying, and after 10 minutes turns on its own Wi-Fi.",
                    "A fodder box shows a Wi-Fi called <b>SmartHydro-Fodder-Setup</b>. Join it, type your Wi-Fi name and password, and tap <b>SAVE &amp; CONNECT TO CLOUD</b>.",
                    "For a tower, ask your Smart Hydro contact to set up the Wi-Fi."),

                Fix("f_softap", "Wait for the box to change", "~/Home/Owners#pair-wifi", "Offline mode: use the box's own Wi-Fi",
                    "The change can take a few seconds to reach the box.",
                    "Wait about a minute. A tower screen should then show <b>EMERGENCY SOFTAP</b>. A fodder box screen does not change, so look for its Wi-Fi on your phone.",
                    "If you are at the box, it is quicker to hold <b>BOOT</b> for 3 seconds."),

                Fix("f_loramode", "Wait for the box to change", "~/Home/Owners#modes", "How the box connects",
                    "Wait about a minute. The box screen should then show <b>LORA + BLE</b>.",
                    "The numbers then reach the website by radio, every 15 seconds from a fodder box and every 17.5 seconds from a tower box."),

                Fix("f_modefail", "Check the tower or tray", "~/Home/Owners#structure", "Add a tower or tray",
                    "The tower or tray must have the code from the box sticker saved.",
                    "Check your own internet, then try the button again.",
                    "Only the farm owner can change how the box connects."),

                Fix("f_wait", "Wait a few seconds", "", "",
                    "Photos take a few seconds to arrive. Refresh the page, then try again."),

                Fix("f_camwifi", "Check the camera's Wi-Fi", "", "",
                    "The camera needs Wi-Fi with internet. It takes one photo every day by itself.",
                    "Check the camera is plugged in and the farm Wi-Fi works.",
                    "Check the camera's sticker code is saved on the tower or tray."),

                Fix("f_camsetup", "Give the camera your Wi-Fi details", "", "",
                    "The camera shows this Wi-Fi when it cannot reach the internet.",
                    "On your phone, join <b>SmartHydro-CAM-Setup</b>. Type your Wi-Fi name and password on the page that opens.",
                    "The camera restarts and takes its first photo."),

                Fix("f_worker", "That is for the farm owner", "~/Home/Workers", "Farm workers",
                    "Workers can use the switches and add pH or plant food, but cannot change the healthy ranges, how the box connects, or the team.",
                    "Ask your farm owner to make the change."),

                Fix("f_simple", "You are in Simple Mode", "~/Home/Workers#worker-simple", "Understanding Simple Mode on your phone",
                    "Simple Mode keeps everything on one screen, so there is no bar of buttons at the bottom.",
                    "Workers always see Simple Mode in the phone app. Use the website for warnings, history and photos.",
                    "In offline mode, go to <b>Settings</b> and switch off <b>Simple Mode</b> to see the full app."),

                Fix("f_btoff", "Turn Bluetooth on", "~/Home/Owners#pair", "Offline mode: connect your phone to the box",
                    "Swipe down from the top of your phone's screen and tap the Bluetooth button so it lights up.",
                    "Go back to the app and tap <span class='ui'>Search again</span>."),

                Fix("f_btperm", "Allow the app to find nearby devices", "~/Home/Owners#pair", "Offline mode: connect your phone to the box",
                    "Tap <span class='ui'>Find my box</span> again and tap <span class='ui'>Allow</span> when your phone asks.",
                    "Or open your phone's Settings, then Apps, Smart Hydro, Permissions, and allow <b>Nearby devices</b> (or <b>Location</b> on older phones)."),

                Fix("f_btnobox", "Get closer to the box", "~/Home/Owners#pair", "Offline mode: connect your phone to the box",
                    "Check the box is on. Its small screen should be lit.",
                    "Stand right next to the box, then tap <span class='ui'>Search again</span>.",
                    "Still nothing? Use the box's own Wi-Fi instead (see <a href='~/Home/Owners#pair-wifi'>Offline mode: use the box's own Wi-Fi</a>)."),

                Fix("f_btcode", "Type the code from the box screen", "~/Home/Owners#pair", "Offline mode: connect your phone to the box",
                    "When your phone asks for a code, look at the box screen. It shows <b>BLE pairing code</b> and a 6-digit number.",
                    "Type that number exactly, then confirm.",
                    "Saw <b>Couldn't connect</b>? Tap <span class='ui'>Try again</span>."),

                Fix("f_btmodes", "Connect over Bluetooth first", "~/Home/Owners#modes-app", "Change how the box connects in the phone app",
                    "In the phone app, the mode buttons only show in offline mode, while your phone is connected to the box over Bluetooth.",
                    "Connect over Bluetooth, then look under <b>The box is using…</b>.",
                    "Signed in? Farm owners can change how the box connects on the website instead."),

                Fix("f_nomodebuttons", "Save the box code first", "~/Home/Owners#structure", "Add a tower or tray",
                    "On the website, these buttons are only for the farm owner.",
                    "They also need the code from the box sticker saved on the tower or tray.",
                    "In the phone app, the mode buttons only show in offline mode while connected to the box over Bluetooth."),

                Fix("f_choose", "Choose a tower or tray first", "~/Home/Owners#readings", "Check how your plants are doing",
                    "Tap <b>Structure</b> and choose one. Then <b>Readings</b> will work."),

                Fix("f_deletesite", "Empty the farm first", "~/Home/Owners#sites", "Your farms (sites)",
                    "You can only delete a farm when it has no towers or trays left.",
                    "Move or delete its towers and trays first. A Smart Hydro helper can bring a deleted farm back.")
            };
        }

        // Lists the messages people see most often, what each one means, and what to do about it.
        private static GuideTable BuildMessages()
        {
            return Table(
                new[] { "You see", "What it means", "What to do" },
                new[] { "Not Reporting (website)", "Nothing has arrived from the box for 2 minutes.", "Check the box is on, then the receiver box on the farm or the Wi-Fi." },
                new[] { "Not connected, or not reporting (app)", "The app is not getting numbers from this box.", "Pull the screen down. In offline mode, tap Connect your box or Connect now, or join the box's Wi-Fi." },
                new[] { "Bluetooth is turned off. Turn it on to find your box.", "Your phone's Bluetooth is off.", "Turn Bluetooth on, then tap Search again." },
                new[] { "The app isn't allowed to use Bluetooth yet.", "You did not allow the app to find nearby devices.", "Tap Find my box again and tap Allow, or allow Nearby devices in your phone's Settings." },
                new[] { "No box found nearby.", "The phone cannot hear the box.", "Check that the box is on, stand closer to it, then tap Search again." },
                new[] { "Couldn't connect", "Pairing did not finish, or the code was typed wrong.", "Tap Try again and type the 6-digit code shown on the box screen." },
                new[] { "No data yet", "The box has never sent anything.", "Check the sticker code saved on the tower or tray." },
                new[] { "The box didn't answer. Check you're connected to its network.", "Your phone is not on the box's Wi-Fi.", "Join the Wi-Fi that starts with SmartHydro- and try again." },
                new[] { "The box is offline right now, so this can't be done.", "The app cannot reach the box.", "Go closer to the box and join its Wi-Fi again." },
                new[] { "This structure has no controller board linked yet.", "The tower or tray has no box code saved.", "Edit it and add the code from the sticker." },
                new[] { "already linked to another structure", "That box code is already used.", "Check the sticker code. Each box can only be added once." },
                new[] { "You do not have permission to change controls.", "Your account is not on the team for this farm.", "Check that you chose the right farm, or ask the farm owner to invite you." },
                new[] { "The waiting period after the previous dose has not finished.", "The last dose is still mixing.", "Wait about 15 minutes after pH, or 10 minutes after plant food or clean water, then try again." },
                new[] { "The hourly pH dose limit has been reached.", "About 10 mL of pH liquid was added in the last hour.", "Wait up to an hour. If the pH still does not move, check the pH sensor and the bottle." },
                new[] { "The sensors are recovering, or The sensor readings are too old", "A tower box is waiting for good pH and EC readings before it doses.", "Check that the sensors are in the water and plugged in, then wait a minute." },
                new[] { "Dosing blocked", "Smart Hydro does not know if the last dose ran.", "Check that the box is on and connected, and look at the tank. Ask your Smart Hydro contact if it stays blocked." },
                new[] { "Reports are stale or unavailable. Controls need a reachable controller.", "The website has not heard from the box lately.", "Check the box is on, then the gateway and the Wi-Fi." },
                new[] { "Command failed. Please try again. (Simple Mode)", "The box did not take the change.", "Check that the box is connected and try again." },
                new[] { "Too many attempts. Please try again in ...", "Too many tries in a short time.", "Wait the time shown, then try again." },
                new[] { "Invalid or expired access code.", "The invitation code is wrong, used up or older than 24 hours.", "Check the code, or ask for a new invitation." },
                new[] { "Invite quota reached. Please wait before inviting more workers.", "5 invitations were sent in 10 minutes.", "Wait 10 minutes." },
                new[] { "Could not activate online mode on the device.", "The website could not send the change.", "Check the box code is saved, then try again." });
        }

        // Groups the common questions by topic so people can find theirs quickly.
        private static List<GuideFaqGroup> BuildFaqGroups()
        {
            return new List<GuideFaqGroup>
            {
                new GuideFaqGroup
                {
                    Id = "faq-offline",
                    Icon = "wifi_off",
                    Title = "Offline mode",
                    Items =
                    {
                        Faq("What is offline mode?",
                            "A way to use the phone app with no account and no internet. Your phone talks straight to the box over Bluetooth, or over the box's own Wi-Fi as a backup, while you stand near it. See <a href='~/Home/Account#offline'>Offline mode</a>."),
                        Faq("How do I start offline mode?",
                            "Open the app and tap <span class='ui'>Continue Offline</span> at the bottom of the sign-in screen. Then connect to the box over Bluetooth. See <a href='~/Home/Owners#pair'>Offline mode: connect your phone to the box</a>."),
                        Faq("How do I know I am in offline mode?",
                            "The <b>Structure</b> screen says <b>Local structures</b>, and <b>Settings</b> shows a <span class='ui'>Sign In to Go Online</span> button."),
                        Faq("What can I do in offline mode?",
                            "See the numbers, switch the lights, fans, pump and spray on or off, add pH liquid or plant food, change how the box connects, use Simple Mode, and keep photos on your phone."),
                        Faq("What can't I do in offline mode?",
                            "You can't see your farm from far away, get warnings, change the healthy ranges, invite workers or use the farm camera. For those, sign in when you have internet."),
                        Faq("Does it cost data or airtime?",
                            "No. Offline mode uses Bluetooth or the box's own Wi-Fi, which have no internet. Talking to the box does not use your data or airtime."),
                        Faq("Do I need to pair my phone every time?",
                            "No. You pair once, and then the app connects to the box by itself whenever you are close to it. Another phone needs to be paired too. To remove a box from your phone, tap <span class='ui'>Forget this box</span>."),
                        Faq("Is offline mode the same as having no signal?",
                            "No. If you are signed in and lose signal, the app shows what it saw last and updates when the signal comes back. Offline mode is when you choose <span class='ui'>Continue Offline</span> and talk to the box directly."),
                        Faq("How do I go back online?",
                            "Go to <b>Settings</b> and tap <span class='ui'>Sign In to Go Online</span>. Towers and photos you added in offline mode stay on this phone only.")
                    }
                },
                new GuideFaqGroup
                {
                    Id = "faq-box",
                    Icon = "router",
                    Title = "The box",
                    Items =
                    {
                        Faq("Do I need internet?",
                            "No. The box looks after your plants by itself, and you can use offline mode at the box. You need internet to sign in, to see your farm from far away, and for the camera."),
                        Faq("What is the gateway?",
                            "A receiver box on the farm. It picks up the radio signal from every tower and tray and sends their numbers to Smart Hydro using its own SIM card. If it loses signal, it keeps the numbers and sends them later."),
                        Faq("Which way should the box connect?",
                            "Leave it on <b>LoRa Mode</b> (radio) most of the time. You do not need to change it to use the phone app at the box, because Bluetooth works in every mode. Use <b>SoftAP Mode</b> only if Bluetooth is not working. Use <b>Online Mode</b> when the box is near Wi-Fi with internet. See <a href='~/Home/Owners#modes'>How the box connects</a>."),
                        Faq("Can the box use my phone's hotspot?",
                            "Yes. In <b>Online Mode</b> the box can join farm or home Wi-Fi, or a phone hotspot. A fodder box asks for the Wi-Fi name and password on a Wi-Fi called <b>SmartHydro-Fodder-Setup</b>. For a tower, ask your Smart Hydro contact."),
                        Faq("How often do the numbers change?",
                            "The box checks your plants every few seconds. By radio it sends the numbers every 15 seconds from a fodder box and every 17.5 seconds from a tower box. The website shows <b>Not Reporting</b> after 2 minutes with nothing."),
                        Faq("Why did the box change how it connects by itself?",
                            "If the way it is using stops getting through, the box tries another way and goes back when it can. This is normal. See <a href='~/Home/Owners#modes'>How the box connects</a>."),
                        Faq("What does the BOOT button do?",
                            "Hold it for 3 seconds to turn the box's own Wi-Fi on or off for offline mode. On a fodder box the button can say <b>PRG</b>."),
                        Faq("Can I see the numbers without the app?",
                            "Yes. Join the box's own Wi-Fi, then open <span class='sh-mono'>192.168.4.1</span> in your phone's web browser. You will see the latest numbers and a link to download the app.")
                    }
                },
                new GuideFaqGroup
                {
                    Id = "faq-plants",
                    Icon = "eco",
                    Title = "Plants and automatic care",
                    Items =
                    {
                        Faq("Will the box add liquid without me?",
                            "Yes. It fixes pH and plant food by itself, a small amount at a time, and stops after 6 doses in 2 hours (the doses you add count too). Every dose, automatic or yours, waits for the last one to mix first (about 15 minutes after pH, 10 minutes after plant food or clean water). Add one dose at a time."),
                        Faq("What does the box switch on by itself?",
                            "A tower keeps its light on for 16 hours a day and runs its water pump for 15 minutes every hour. Its fans come on when it is too hot or too damp. A fodder box sprays the trays for 10 seconds every 3 hours."),
                        Faq("I switched something and it changed back. Why?",
                            "The box does what you say for 1 hour, then goes back to its own timers. See <a href='~/Home/Owners#control'>Switch things on or off</a>."),
                        Faq("What healthy range should I use?",
                            "The starting numbers suit most crops. Only change them if you know your crop needs something different. See <a href='~/Home/Owners#ranges'>Change the healthy range</a>."),
                        Faq("Why do I get a warning when the plants look fine?",
                            "A warning means a number is outside the healthy range. Check the sensors are in the water, then check the range suits your crop. Tap <span class='ui'>Acknowledge</span> once it is sorted.")
                    }
                },
                new GuideFaqGroup
                {
                    Id = "faq-account",
                    Icon = "person",
                    Title = "Accounts and your team",
                    Items =
                    {
                        Faq("Do I need an account?",
                            "Not for offline mode at the box. You need one to see your farm from far away, get warnings and invite workers."),
                        Faq("How long does a sign-in code last?",
                            "10 minutes. Use the code in the newest email."),
                        Faq("Why do I have to wait after I make an account?",
                            "A Smart Hydro helper checks every new farm owner to keep farms safe. Workers do not wait, because their farm owner invites them."),
                        Faq("How many workers can I invite?",
                            "As many as you need. You can send 5 invitations every 10 minutes. Each one works for 24 hours."),
                        Faq("I deleted something by mistake",
                            "Farms, towers, trays, warnings and photos can be brought back. Ask a Smart Hydro helper.")
                    }
                },
                new GuideFaqGroup
                {
                    Id = "faq-help",
                    Icon = "support_agent",
                    Title = "Getting help",
                    Items =
                    {
                        Faq("Who can I ask for help?",
                            "Email <span class='sh-mono'>smarthydrosupport@gmail.com</span>, or tap <b>Contact support</b> on the website's sign-in page."),
                        Faq("What should I put in my message?",
                            "The code on the sticker on your box, what the top of the box screen says, what you tried, and a photo of any message you see.")
                    }
                }
            };
        }
    }
}
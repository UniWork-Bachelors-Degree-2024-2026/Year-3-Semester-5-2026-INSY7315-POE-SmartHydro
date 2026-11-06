using SmartHydro_UserGuide_MVC.Models;

namespace SmartHydro_UserGuide_MVC.Services
{
    public partial class GuideContent
    {
        private static readonly GuideWhere Website = new GuideWhere { Icon = "computer", Label = "Website" };
        private static readonly GuideWhere PhoneApp = new GuideWhere { Icon = "smartphone", Label = "Phone app" };
        private static readonly GuideWhere Both = new GuideWhere { Icon = "devices", Label = "Website and phone app" };

        private readonly Dictionary<string, GuideTask> _tasks = new();
        private readonly Dictionary<string, HelpPageViewModel> _pages = new();
        private readonly Dictionary<string, List<string>> _pageTasks = new();

        public GuideContent()
        {
            AddAccountTasks();
            AddFarmTasks();
            AddSetupTasks();
            AddPages();
            AddTroubleshootPage();
            AddWordsPage();
        }

        // Builds a fresh page model for the given page key, filling in its task cards in order.
        public HelpPageViewModel GetPage(string key)
        {
            var page = _pages[key];

            var model = new HelpPageViewModel
            {
                Title = page.Title,
                Icon = page.Icon,
                Intro = page.Intro,
                CanDo = page.CanDo,
                CannotDo = page.CannotDo,
                QuickChecks = page.QuickChecks,
                Questions = page.Questions,
                Fixes = page.Fixes,
                Messages = page.Messages,
                FaqGroups = page.FaqGroups,
                TermGroups = page.TermGroups,
                EaseOfUse = page.EaseOfUse
            };

            if (_pageTasks.ContainsKey(key))
            {
                foreach (var taskId in _pageTasks[key])
                {
                    model.Tasks.Add(_tasks[taskId]);
                }
            }

            return model;
        }

        // Stores a task so pages can list it by its id.
        private void Add(GuideTask task)
        {
            _tasks[task.Id] = task;
        }

        // Shortens creating a note of the given kind (info, warn, stop or good).
        private static GuideNote Note(string kind, string html)
        {
            return new GuideNote { Kind = kind, Html = html };
        }

        // Shortens creating a table from a header row and data rows.
        private static GuideTable Table(string[] headers, params string[][] rows)
        {
            var table = new GuideTable { Headers = headers.ToList() };

            foreach (var row in rows)
            {
                table.Rows.Add(row.ToList());
            }

            return table;
        }

        private const string WaysList =
            "<ul class='sh-ways'>" +
            "<li><b>LoRa long-range radio</b>, the normal way. The box sends its readings by radio to the farm's gateway, which passes them on to Smart Hydro over mobile data (4G). No farm Wi-Fi is needed.</li>" +
            "<li><b>Through Wi-Fi with internet</b> (Online Mode), when the box is near farm or home Wi-Fi, or a phone hotspot. Changes you make on the website reach the box straight away.</li>" +
            "<li><b>Bluetooth</b>, when a phone is close to the box. This is how the phone app talks to the box when there is no internet, and it is quicker than the internet when the connection is slow. You pair your phone once, using a code on the box's screen, and then you can see the readings, use the switches, add liquids and change how the box connects.</li>" +
            "<li><b>Through the box's own Wi-Fi</b> (SoftAP Mode), the backup for offline mode. You stand at the box with no internet, and your phone joins the box's Wi-Fi to talk to it directly.</li>" +
            "</ul>";

        // Adds the tasks about making an account, joining a team and signing in.
        private void AddAccountTasks()
        {
            Add(new GuideTask
            {
                Id = "register",
                Icon = "how_to_reg",
                Title = "Make an account (farm owners)",
                Why = "Farm owners make their own account. A Smart Hydro helper then checks it.",
                Where = Both,
                Steps =
                {
                    "On the sign-in screen, tap <span class='ui'>Create one</span> (website) or <span class='ui'>Register as an Operator</span> (phone app).",
                    "Type your name, your email, and a password. The password must be at least 8 characters long.",
                    "Type a name for your farm, for example <i>Jane's Farm</i>.",
                    "Tap <span class='ui'>Continue</span>.",
                    "Open your email. You will find a 6-number code from Smart Hydro. Type it in and tap <span class='ui'>Verify Email</span>.",
                    "You will see <b>Registration received</b>. A Smart Hydro helper will check your account and send you an email when you can sign in."
                }
            });

            Add(new GuideTask
            {
                Id = "join",
                Icon = "group_add",
                Title = "Join your farm team (farm workers)",
                Why = "Your farm owner sends you an email invitation. You have 24 hours to accept it.",
                Where = Both,
                Steps =
                {
                    "Open the email from Smart Hydro.",
                    "Tap the link in the email. Or, on the sign-in screen, tap <span class='ui'>Have an invite code?</span> or <span class='ui'>Join your team</span>, then type your email and the code from the email, which looks like <b>HYDR-9X2W</b>.",
                    "Type your name and choose a password. Type the password again to check it.",
                    "Tap <span class='ui'>Create Account</span> if you used the link, or <span class='ui'>Join Team</span> if you typed the code. You are now on the team."
                },
                Notes =
                {
                    Note("warn", "More than 24 hours passed? Ask your farm owner to send a new invitation. A code also stops working after 3 wrong tries.")
                }
            });

            Add(new GuideTask
            {
                Id = "signin",
                Icon = "login",
                Title = "Sign in",
                Why = "Signing in has two parts: your password, then a code from your email. This keeps your farm safe.",
                Where = Both,
                Steps =
                {
                    "Type your email and your password.",
                    "Tap <span class='ui'>Sign In</span>.",
                    "Open your email. Find the newest message from Smart Hydro with a 6-number code.",
                    "Go back to Smart Hydro and type the 6 numbers. You are signed in."
                },
                Notes =
                {
                    Note("info", "No email? Look in your <b>Spam</b> or <b>Junk</b> folder. Tap <span class='ui'>Resend code</span> if you need a new one. Each code works for 10 minutes."),
                    Note("warn", "Typed the wrong password too many times? Wait 15 minutes and try again.")
                }
            });

            Add(new GuideTask
            {
                Id = "forgot",
                Icon = "key",
                Title = "I forgot my password",
                Where = Both,
                Steps =
                {
                    "On the sign-in screen, tap <span class='ui'>Forgot password?</span>",
                    "Type your email.",
                    "Open your email and find the code from Smart Hydro. Type it in.",
                    "Choose a new password and type it twice. You cannot use one of your last 5 passwords.",
                    "Sign in with your new password."
                }
            });

            Add(new GuideTask
            {
                Id = "offline",
                Icon = "wifi_off",
                Title = "Offline mode: use the app with no account and no internet",
                Why = "Offline mode is for using your phone right next to your box. You do not need an account, internet or airtime.",
                Where = PhoneApp,
                Steps =
                {
                    "Open the Smart Hydro app.",
                    "At the bottom of the sign-in screen, tap <span class='ui'>Continue Offline</span>.",
                    "Add your tower or tray. You only need the code from the sticker on the box (see <a href='~/Home/Owners#structure'>Add a tower or tray</a>).",
                    "Go to the box and connect your phone to it over Bluetooth (see <a href='~/Home/Owners#pair'>Offline mode: connect your phone to the box</a>).",
                    "Now you can see the numbers, switch things on or off, add liquids and change how the box connects, straight from the box."
                },
                Table = Table(
                    new[] { "In offline mode you can", "In offline mode you can't" },
                    new[] { "See the numbers from the box while you stand near it", "See your farm from far away" },
                    new[] { "Switch the lights, fans, pump and spray on or off", "Get warnings (alerts)" },
                    new[] { "Add pH liquid or plant food", "Change the healthy ranges" },
                    new[] { "See earlier numbers saved on your phone", "Invite workers or use the farm camera" },
                    new[] { "Take photos and keep them on your phone", "Use the website" },
                    new[] { "Change how the box connects with the mode buttons", "" },
                    new[] { "Turn on Simple Mode in Settings", "" }),
                Notes =
                {
                    Note("info", "<b>How do I know I am in offline mode?</b> The <b>Structure</b> screen says <b>Local structures</b>, and <b>Settings</b> shows a <span class='ui'>Sign In to Go Online</span> button."),
                    Note("warn", "Towers, trays and photos you add in offline mode are kept on this phone only. They are not added to an online account."),
                    Note("info", "Want to go online later? Go to <b>Settings</b> and tap <span class='ui'>Sign In to Go Online</span>."),
                    Note("info", "Talking straight to the box only works in offline mode. If you are signed in, go to <b>Settings</b>, tap <span class='ui'>Sign Out</span>, then tap <span class='ui'>Continue Offline</span>.")
                }
            });
        }

        // Adds the tasks for running the farm: sites, structures, readings, controls, alerts, photos and the team.
        private void AddFarmTasks()
        {
            Add(new GuideTask
            {
                Id = "sites",
                Icon = "holiday_village",
                Title = "Your farms (sites)",
                Why = "A <b>site</b> is one farm. You can have more than one.",
                Where = Both,
                Steps =
                {
                    "To see a different farm, tap the farm name at the top of the phone app, or at the bottom of the menu on the website.",
                    "To add a farm, tap <span class='ui'>Add New Site</span> (phone) or <span class='ui'>Create a site</span> (website), and type its name.",
                    "To change a farm's name, tap the pencil next to its name. In the phone app this opens <span class='ui'>Rename Site</span>."
                },
                Notes =
                {
                    Note("info", "You can only delete a farm when it has no towers or trays left. A Smart Hydro helper can bring a deleted farm back.")
                }
            });

            Add(new GuideTask
            {
                Id = "structure",
                Icon = "add_box",
                Title = "Add a tower or tray",
                Why = "Smart Hydro calls each tower or set of fodder trays a <b>structure</b>.",
                Where = Both,
                Steps =
                {
                    "Tap <b>Structure</b>, then the <b>+</b> button (phone) or <span class='ui'>Add a Structure</span> (website).",
                    "Find the sticker on the sensor box. Copy the code that looks like <b>A4:CF:12:9B:3E:01</b> into <b>Controller MAC Address</b>.",
                    "If there is a camera, copy the code from the camera's sticker into <b>Camera MAC Address</b>. If not, leave it empty.",
                    "Give it a name you will recognise, like <i>Spinach Tower 1</i>.",
                    "Choose <b>Hydroponic Structure</b> for a tower, or <b>Fodder Structure</b> for trays.",
                    "Choose the farm (<b>Site</b>), then tap <span class='ui'>Add a Structure</span>."
                },
                Notes =
                {
                    Note("stop", "Each box can only be added once. If you see <b>already linked to another structure</b>, check the code on the sticker again.")
                }
            });

            Add(new GuideTask
            {
                Id = "pair",
                Icon = "wifi_tethering",
                Title = "Offline mode: connect your phone to the box",
                Why = "In offline mode your phone talks straight to the box over Bluetooth. No internet is needed. Stand close to the box.",
                Where = PhoneApp,
                Steps =
                {
                    "Make sure the box is on and your phone's Bluetooth is on. You do not need to press any button on the box, because Bluetooth is on in every mode.",
                    "Open the Smart Hydro app, tap <span class='ui'>Continue Offline</span>, and choose your tower or tray. Tap <span class='ui'>Not connected · Pair with your box</span>.",
                    "Under <b>Pair over Bluetooth</b>, tap <span class='ui'>Find my box</span>. If your phone asks to allow <b>Nearby devices</b> (or <b>Location</b> on older phones), tap <span class='ui'>Allow</span>.",
                    "When the app finds your box, tap <span class='ui'>Connect</span>. If it shows a list, pick the box whose name ends with the last 6 letters and numbers of the sticker code. Tap <span class='ui'>Not this box</span> to skip one that is not yours.",
                    "The box screen shows <b>BLE pairing code</b> and a 6-digit number. Type that number when your phone asks for it.",
                    "When you see <b>Connected</b>, tap <span class='ui'>Done</span>. The top of the screen now says <b>Connected via Bluetooth</b>."
                },
                Notes =
                {
                    Note("good", "You only pair once. After that the app connects to the box by itself whenever you open it near the box. Under <b>Saved box</b> you can tap <span class='ui'>Connect now</span>, or <span class='ui'>Forget this box</span> to remove it."),
                    Note("info", "Bluetooth not working? Use the box's own Wi-Fi instead (see <a href='~/Home/Owners#pair-wifi'>Offline mode: use the box's own Wi-Fi</a>).")
                }
            });

            Add(new GuideTask
            {
                Id = "pair-wifi",
                Icon = "wifi_tethering",
                Title = "Offline mode: use the box's own Wi-Fi (backup)",
                Why = "If Bluetooth does not work, your phone can join the box's own Wi-Fi instead. No internet is needed. Stand close to the box.",
                Where = PhoneApp,
                Steps =
                {
                    "Turn on the box's own Wi-Fi: press and hold the <b>BOOT</b> button on the box for 3 seconds, then let go (on a fodder box the button can say <b>PRG</b>). A tower screen then shows <b>EMERGENCY SOFTAP</b>. A fodder screen does not change, but its Wi-Fi appears on your phone.",
                    "Open the Smart Hydro app in offline mode (tap <span class='ui'>Continue Offline</span>) and choose your tower or tray. Tap <span class='ui'>Not connected · Pair with your box</span>.",
                    "Under <b>Or use the box's Wi-Fi hotspot</b>, tap <span class='ui'>Open WiFi Settings</span>.",
                    "In the list of Wi-Fi names, tap the one that starts with <b>SmartHydro-Setup-</b> (tower box) or <b>SmartHydro-Fodder-</b> (fodder box). There is no password. Not sure which one? Type the sticker code into the finder below.",
                    "Go back to the Smart Hydro app. Tap <span class='ui'>Test Connection</span>.",
                    "When you see <b>Connected! The box is answering.</b>, tap <span class='ui'>Done</span>.",
                    "When you have finished, hold <b>BOOT</b> for 3 seconds again. The box goes back to sending its numbers by radio."
                },
                ShowHotspotFinder = true,
                Notes =
                {
                    Note("warn", "While your phone is on the box's Wi-Fi, it has no internet. When you finish, connect back to your normal Wi-Fi or mobile data."),
                    Note("info", "If you walk away from the box, your phone loses its Wi-Fi and the numbers stop. Come back closer and pull the screen down to refresh."),
                    Note("info", "The farm owner can also turn on the box's own Wi-Fi from the website with <b>SoftAP Mode</b> (see <a href='~/Home/Owners#modes'>How the box connects</a>).")
                }
            });

            Add(new GuideTask
            {
                Id = "readings",
                Icon = "monitoring",
                Title = "Check how your plants are doing",
                Why = "Each box checks your plants every few seconds.",
                Where = Both,
                Steps =
                {
                    "Tap <b>Structure</b> and choose your tower or tray.",
                    "Tap <b>Readings</b>. You see one box for each thing being measured.",
                    "Look at the colours. Green is good. Orange means watch it. Red means do something now.",
                    "Tap any box to see more, like a chart of the last few days.",
                    "To get the newest numbers, pull the screen down (phone) or refresh the page (website)."
                },
                ShowLights = true,
                Notes =
                {
                    Note("info", "<b>No reading</b> means the box is not sending anything. See <a href='~/Home/Troubleshoot'>Fix a problem</a>."),
                    Note("info", "In offline mode, the numbers come straight from the box, so your phone must be connected to the box over Bluetooth or its own Wi-Fi (see <a href='~/Home/Owners#pair'>Offline mode: connect your phone to the box</a>).")
                }
            });

            Add(new GuideTask
            {
                Id = "control",
                Icon = "toggle_on",
                Title = "Switch things on or off",
                Why = "You can switch the lights, fans, water pump and spray on or off yourself.",
                Where = Both,
                Steps =
                {
                    "Open <b>Readings</b> for your tower or tray.",
                    "Tap the box for the thing you want to change, for example <b>Sunlight</b> for the lights.",
                    "Tap its switch. It turns green when it is on. The website shows what the box last reported, for example <b>Last reported ON</b>.",
                    "The box follows your choice for 1 hour, even if it would have done something different by itself. After that it goes back to its own timers and healthy ranges."
                },
                Notes =
                {
                    Note("info", "When you are signed in, farm owners and workers can use the switches on their farms. In offline mode, the switches work for anyone whose phone is connected to the box over Bluetooth or its own Wi-Fi."),
                    Note("info", "Switched on the water pump? It runs for 15 minutes and then turns off by itself.")
                }
            });

            Add(new GuideTask
            {
                Id = "dose",
                Icon = "science",
                Title = "Fix the pH or nutrients",
                Why = "If pH or nutrients are red or orange, you can add a set amount of liquid. The amounts are the same on the website, in the phone app and in offline mode.",
                Where = Both,
                Table = Table(
                    new[] { "Button", "What it does", "How much" },
                    new[] { "Lower pH", "Adds pH-down liquid", "2 mL" },
                    new[] { "Raise pH", "Adds pH-up liquid", "2 mL" },
                    new[] { "Add nutrients", "Adds plant food", "20 mL" },
                    new[] { "Add clean water on the website, or Dilute in the app", "Adds clean water", "25 mL" }),
                Steps =
                {
                    "On <b>Readings</b>, tap the <b>Clean Water</b> (pH) or <b>Nutrients (EC)</b> box.",
                    "Press the button you need <b>one time only</b>.",
                    "Wait until the box has finished adding the liquid. The phone app asks you to confirm first, because it runs a real pump.",
                    "Wait for the liquid to mix: about 15 minutes after a pH dose, or 10 minutes after plant food or clean water. Then check the number again before you add more."
                },
                Notes =
                {
                    Note("stop", "Press the button one time only. Every dose, including the ones you send, goes through the box's safety checks. After a pH dose the box waits about 15 minutes, and after plant food or clean water about 10 minutes, before it adds more. It adds no more than about 10 mL of pH liquid in an hour, and a tower box will not dose while its pH or EC readings are faulty, too old or still settling. Both boxes also stop after 6 doses in 2 hours, and the doses you send count too."),
                    Note("info", "<b>To stop a dose:</b> on the website's Readings page, switch off the <b>Clean Water</b> or <b>Nutrients (EC)</b> switch. This stops both pumps for that reading."),
                    Note("warn", "<b>Dosing blocked:</b> if the website says <b>Dosing blocked</b> and that the outcome of a dose is not confirmed, Smart Hydro does not know whether the last dose ran. The dose buttons stay off until this is sorted out. Check the box and the tank before you try again."),
                    Note("info", "When you are signed in, farm owners and workers can add doses."),
                    Note("warn", "The box does not check the water tank before it adds liquid. Make sure there is water in the tank first.")
                }
            });

            Add(new GuideTask
            {
                Id = "ranges",
                Icon = "tune",
                Title = "Change the healthy range",
                Why = "Each tower or tray has a healthy range for every reading. Outside it, you get a warning.",
                Where = Both,
                Table = Table(
                    new[] { "Reading", "Tower", "Fodder trays" },
                    new[] { "pH", "5.5 to 6.5", "5.5 to 6.5 (a new box uses 5.8 to 6.4 until a range is saved)" },
                    new[] { "Plant food (EC)", "1.0 to 2.5", "0.5 to 1.5" },
                    new[] { "Temperature", "18 to 28 °C", "18 to 26 °C" },
                    new[] { "Humidity", "50 to 80 %", "60 to 80 %" }),
                Steps =
                {
                    "Tap <b>Thresholds</b> (website menu, or the Structure screen in the app).",
                    "Find your tower or tray and the reading you want to change.",
                    "Type the lowest and highest healthy number.",
                    "Tap the <b>Save</b> button (website) or <span class='ui'>Save range</span> (phone app). To go back to the numbers in the table, tap <span class='ui'>Reset</span> (phone app)."
                },
                Notes =
                {
                    Note("good", "Not sure? Leave the numbers as they are. They are good for most crops.")
                }
            });

            Add(new GuideTask
            {
                Id = "alerts",
                Icon = "notifications",
                Title = "Warnings (alerts)",
                Why = "Smart Hydro warns you when something needs attention.",
                Where = Both,
                Steps =
                {
                    "Tap <b>Alerts</b>. The number on it shows how many warnings are new.",
                    "Red <b>Alert</b> means act now. Orange <b>Caution</b> means keep an eye on it.",
                    "Fix the problem on the farm.",
                    "Tap <span class='ui'>Acknowledge</span>. The warning moves to <b>Resolved</b>."
                }
            });

            Add(new GuideTask
            {
                Id = "camera",
                Icon = "photo_library",
                Title = "Photos of your plants",
                Where = Both,
                Steps =
                {
                    "Tap <b>Camera</b>.",
                    "Tap <span class='ui'>Add photo</span>.",
                    "On the website, choose <b>Upload a file</b> for a photo you already have, <b>Take photo</b> to use this device's camera, or <b>ESP32 Camera</b> to use the farm camera. In the phone app, choose <b>Take a photo</b> or <b>Upload a photo</b>.",
                    "Choose the date, the tower or tray, and the plant.",
                    "Tap <span class='ui'>Save photo</span>."
                },
                Notes =
                {
                    Note("info", "To find a photo, type in the search box or tap a day on the calendar.")
                }
            });

            Add(new GuideTask
            {
                Id = "invite",
                Icon = "person_add",
                Title = "Add a worker to your team",
                Why = "Workers can see your farm, use the switches and add pH or plant food, but cannot change its settings.",
                Where = Both,
                Steps =
                {
                    "Tap <b>Invite Worker</b> (website) or open <b>Manage Team &amp; Workers</b> on the Structure screen (phone) and tap <span class='ui'>Invite a Worker</span>.",
                    "Choose which farm they will help with.",
                    "Type their email address.",
                    "Tap <span class='ui'>Send Invitation</span> (website) or <span class='ui'>Send Invite</span> (phone app). They get an email with a link and an access code, and have 24 hours to accept."
                },
                Table = Table(
                    new[] { "You see", "It means" },
                    new[] { "Invited", "They have not accepted yet" },
                    new[] { "Joined", "They are on your team" },
                    new[] { "Expired", "24 hours passed. Send a new invitation." })
            });

            Add(new GuideTask
            {
                Id = "modes",
                Icon = "cell_tower",
                Title = "How the box connects",
                Why = "Your Smart Hydro box can send its information in four ways:",
                Where = Both,
                ExtraHtml = WaysList +
                    "<p class='sh-task-para'>Three buttons choose how the box connects: on the website's <b>Readings</b> page, and in the phone app when your phone is connected to the box over Bluetooth (see <a href='~/Home/Owners#modes-app'>Change how the box connects in the phone app</a>). You can usually leave it on <b>LoRa Mode</b>. On the website only the farm owner can change this.</p>",
                Table = Table(
                    new[] { "Button", "What the box does", "Use it when" },
                    new[] { "LoRa Mode", "Turns its Wi-Fi off. Sends readings by LoRa radio to the farm gateway, which passes them on over mobile data. Bluetooth stays on.", "Most of the time. A new box starts in this mode." },
                    new[] { "Online Mode", "Joins a Wi-Fi network that has internet and talks to Smart Hydro directly, so switches and doses arrive straight away. Radio and Bluetooth stay on as a backup.", "The box is in reach of Wi-Fi with internet, such as farm or home Wi-Fi or a phone hotspot" },
                    new[] { "SoftAP Mode", "Makes its own Wi-Fi so your phone can talk to it with no internet. Radio and Bluetooth stay on.", "You are standing at the box and Bluetooth is not working" }),
                Notes =
                {
                    Note("info", "After you press a button, give it about a minute, then check <b>Controller connection</b> at the top of the Readings page, or the box screen (the top line on a tower, the bottom line on a fodder box). The box does not need to restart."),
                    Note("info", "Holding <b>BOOT</b> on the box for 3 seconds does the same as <b>SoftAP Mode</b>. Hold it again to go back to LoRa."),
                    Note("warn", "If <b>Online Mode</b> cannot connect, a fodder box goes back to LoRa after about 3 minutes and opens a Wi-Fi called <b>SmartHydro-Fodder-Setup</b> where you can type the Wi-Fi name and password. A tower box keeps trying, and after 10 minutes turns on its own Wi-Fi so you can still reach it."),
                    Note("info", "<b>The box can change mode by itself.</b> If a box on LoRa gets no answer from the gateway for 5 minutes, it tries Online with its saved Wi-Fi. A tower box with no saved Wi-Fi turns on its own Wi-Fi straight away. If nothing gets through Online for 10 minutes, the box turns on its own Wi-Fi. It goes back to LoRa once the radio works again, unless you chose its own Wi-Fi yourself."),
                    Note("info", "The website only tells the box which way to connect. It does not connect to the box by Bluetooth itself.")
                }
            });

            Add(new GuideTask
            {
                Id = "modes-app",
                Icon = "settings_bluetooth",
                Title = "Change how the box connects in the phone app",
                Why = "The phone app can change how the box connects while your phone is connected to the box over Bluetooth, in offline mode or when you are signed in.",
                Where = PhoneApp,
                Steps =
                {
                    "Connect to your box over Bluetooth (see <a href='~/Home/Owners#pair'>Offline mode: connect your phone to the box</a>).",
                    "Under <b>The box is using…</b>, you see three buttons: <b>LoRa</b> (Long range, needs no Wi-Fi), <b>Online</b> (Uses the box's saved Wi-Fi) and <b>Hotspot</b> (The box makes its own Wi-Fi). The one the box is using says <b>In use</b>.",
                    "Tap the one you want. For <b>Online</b> and <b>Hotspot</b> the app asks first, so tap <span class='ui'>Switch</span>.",
                    "The app shows <b>Switching to…</b> until the box gets there."
                },
                Notes =
                {
                    Note("info", "The mode buttons only show when your phone is connected to the box over Bluetooth."),
                    Note("info", "A button that says <b>Wanted</b> is the mode the box moved away from because it was not getting through. The box goes back to it when it can.")
                }
            });

            Add(new GuideTask
            {
                Id = "simple",
                Icon = "view_agenda",
                Title = "Simple Mode in the phone app",
                Why = "Simple Mode is an easier version of the phone app. Everything is on one screen, with big pictures, big numbers and big switches, and there is no bar of buttons at the bottom.",
                Where = PhoneApp,
                Steps =
                {
                    "<b>Workers:</b> Simple Mode turns on by itself when you sign in.",
                    "<b>Offline mode:</b> go to <b>Settings</b> and switch on <b>Simple Mode</b> (Larger icons and direct controls). Switch it off the same way.",
                    "If you have more than one tower or tray, choose one from the list at the top. With only one, its name is shown.",
                    "Each reading has its own large card with a picture, the number and a coloured label. A card with a switch shows <b>ON</b> or <b>OFF</b> in large letters. Tap it to switch.",
                    "If the box is not answering, tap the big <span class='ui'>Connect your box</span> button."
                },
                Notes =
                {
                    Note("info", "Simple Mode does not show warnings, history, photos or healthy ranges. Workers can see these on the website."),
                    Note("info", "The <b>Settings</b> gear is at the top right.")
                }
            });

            Add(new GuideTask
            {
                Id = "worker-simple",
                Icon = "view_agenda",
                Title = "Understanding Simple Mode on your phone",
                Why = "When a worker signs into the Smart Hydro Android app, the app automatically opens in Simple Mode. Simple Mode was designed specifically for real-world farm conditions in rural areas like Namibia:",
                Where = PhoneApp,
                ExtraHtml =
                    "<ul class='sh-ways'>" +
                    "<li>The bottom navigation bar is completely removed. There are no confusing tabs to get lost in.</li>" +
                    "<li>All essential growing conditions are displayed on one scrolling screen using high-contrast cards with large numbers and clear icons.</li>" +
                    "<li>Every pump and fan has a large toggle switch that is easy to tap with wet hands, dirty fingers, or working gloves.</li>" +
                    "<li>Bold color accents make water levels, pH, temperature, and sunlight readable even under direct, harsh sunlight.</li>" +
                    "</ul>",
                Notes =
                {
                    Note("info", "<b>Note on Alerts and Photos:</b> The phone app in Simple Mode does not show an Alerts inbox, historical graphs, or a camera photo gallery. This keeps the field interface fast, uncluttered, and battery-friendly. If a worker needs to review captured crop photos or view the full alerts list, they can sign in to the Smart Hydro website on a computer or tablet.")
                }
            });

            Add(new GuideTask
            {
                Id = "worker-rights",
                Icon = "rule",
                Title = "What you can and cannot do",
                Where = Both,
                ExtraHtml =
                    "<h4 class='sh-task-subhead'>On the Phone App:</h4>" +
                    CanCannotTable(
                        new[] { "Switch between assigned growing structures", "Add, edit or delete sites or structures" },
                        new[] { "See readings, detail pages and history", "Change how the box connects" },
                        new[] { "Switch things on or off", "Delete or restore alerts" },
                        new[] { "Add pH or nutrients", "Delete photos" },
                        new[] { "Toggle Dark Mode and High Contrast in Settings", "Change healthy ranges or invite people" },
                        new[] { "", "See and acknowledge alerts" }) +
                    "<h4 class='sh-task-subhead'>On the Website:</h4>" +
                    CanCannotTable(
                        new[] { "View all assigned structures, live readings, and historical data charts.", "Add, edit or delete sites or structures" },
                        new[] { "View and acknowledge active warnings in the Alerts menu.", "Change how the box connects" },
                        new[] { "Switch things on or off", "Delete or restore alerts" },
                        new[] { "Add pH or nutrients", "Delete photos" },
                        new[] { "Toggle Dark Mode and High Contrast in Settings", "Change healthy ranges or invite people" },
                        new[] { "Browse the Camera gallery and view the latest crop photos.", "" })
            });

            Add(new GuideTask
            {
                Id = "daily",
                Icon = "wb_sunny",
                Title = "The morning check",
                Why = "Every morning, perform this quick physical inspection using Simple Mode on your phone:",
                Where = PhoneApp,
                Steps =
                {
                    "<b>Open the Smart Hydro App:</b> The app opens straight into Simple Mode. Tap the structure name at the top to select the tower or tray you are inspecting today.",
                    "<b>Check the Four Core Cards:</b> Glance down the screen to verify all four vital signs:" +
                        "<ul class='sh-step-list'>" +
                        "<li><b>Water Card:</b> Verify the water reservoir is full.</li>" +
                        "<li><b>Clean Water (pH) Card:</b> Check the acidity reading. A healthy reading displays around 5.5 to 6.5 pH.</li>" +
                        "<li><b>Temperature Card:</b> Check the air and water warmth. Ideal temperatures sit between 18 C and 26 C.</li>" +
                        "<li><b>Sunlight Card:</b> Check that grow lights or sunlight levels match the requirements.</li>" +
                        "</ul>",
                    "<b>Perform Scheduled Farm Chores:</b> Use the large switches directly below each card to toggle equipment as instructed by the farm owner, for example tap <span class='ui'>Water Pump</span> to run an irrigation cycle.",
                    "<b>Look for Offline or Warning Signs:</b> If a reading shows two dashes (--), it means the sensor box has not reported for over 2 minutes. Check that the box is powered on and the green light is blinking.",
                    "<b>Tell the Farm Owner:</b> Tell the farm owner immediately if:" +
                        "<ul class='sh-step-list'>" +
                        "<li>Any number is unusually high or low.</li>" +
                        "<li>A card displays dashes (--) instead of a live number.</li>" +
                        "<li>Any pump does not turn on when tapped.</li>" +
                        "<li>The water tank is visibly low or plants appear wilted.</li>" +
                        "</ul>"
                }
            });
        }

        // Builds a two-column "You can / You cannot" table as HTML. An empty string leaves that cell blank.
        private static string CanCannotTable(params string[][] rows)
        {
            var html = "<div class='sh-table-scroll'><table class='sh-table'><thead><tr><th scope='col'>You can</th><th scope='col'>You cannot</th></tr></thead><tbody>";

            foreach (var row in rows)
            {
                html += "<tr><td>" + row[0] + "</td><td>" + row[1] + "</td></tr>";
            }

            return html + "</tbody></table></div>";
        }

        // Defines each page of the guide and which tasks appear on it, in order.
        private void AddPages()
        {
            _pages["home"] = new HelpPageViewModel { Title = "Start here", Icon = "home", Intro = "Welcome to the Smart Hydro help guide." };
            _pages["setup"] = new HelpPageViewModel { Title = "Getting set up", Icon = "inventory_2", Intro = "Do these once, when your Smart Hydro equipment arrives. Start with <b>Choose your setup</b>." };
            _pages["troubleshoot"] = new HelpPageViewModel { Title = "Fix a problem", Icon = "build", Intro = "Answer one question at a time and we will show you what to do." };
            _pages["words"] = new HelpPageViewModel { Title = "Words explained", Icon = "menu_book", Intro = "Smart Hydro words in plain language, and ways to make the guide easier to use." };

            _pages["account"] = new HelpPageViewModel
            {
                Title = "Your account",
                Icon = "person",
                Intro = "How to make an account, join a team and sign in. No account? You can still use <b>offline mode</b> at your box."
            };
            _pageTasks["account"] = new List<string> { "register", "join", "signin", "forgot", "offline", "simple" };

            _pages["owners"] = new HelpPageViewModel
            {
                Title = "Farm owners",
                Icon = "agriculture",
                Intro = "For the person who owns the farm. Smart Hydro calls you an <b>Operator</b>. No internet at the box? Use <b>offline mode</b> in the phone app.",
                CanDo = new List<string> { "See readings", "Switch things on and off", "Add pH and plant food", "Change healthy ranges", "Add workers" }
            };
            _pageTasks["owners"] = new List<string> { "sites", "structure", "pair", "pair-wifi", "readings", "control", "dose", "ranges", "alerts", "camera", "invite", "modes", "modes-app" };

            _pages["workers"] = new HelpPageViewModel
            {
                Title = "Guide for Farm Workers",
                Icon = "badge",
                Intro = "<i><b>Example:</b> John Doe got an email from Jane Doe this morning inviting him to Smart Hydro. Five minutes later he is on the team and doing his first morning check.</i>" +
                    "<span class='sh-guide-intro-para'>Workers handle the day-to-day physical farming chores: inspecting crop health, running irrigation pumps, and switching on ventilation fans. To protect the farm, workers cannot change safety settings, delete structures, or edit healthy ranges. This ensures full operational safety while keeping control with the farm owner.</span>"
            };
            _pageTasks["workers"] = new List<string> { "worker-simple", "worker-rights", "daily" };
        }
    }
}
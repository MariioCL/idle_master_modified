using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using IdleMaster.Properties;
using Newtonsoft.Json;
using Steamworks;
using HtmlDocument = HtmlAgilityPack.HtmlDocument;
using System.Globalization;
using System.Security.Principal;
using System.Runtime.InteropServices;

namespace IdleMaster
{
    public partial class frmMain : Form
    {
        //Prevent Sleep Code
        public static void PreventSleep()
        {
            SetThreadExecutionState(ExecutionState.EsContinuous | ExecutionState.EsSystemRequired);
        }
        public static void AllowSleep()
        {
            SetThreadExecutionState(ExecutionState.EsContinuous);
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern ExecutionState SetThreadExecutionState(ExecutionState esFlags);
        [FlagsAttribute]
        private enum ExecutionState : uint
        {
            EsAwaymodeRequired = 0x00000040,
            EsContinuous = 0x80000000,
            EsDisplayRequired = 0x00000002,
            EsSystemRequired = 0x00000001
        }

        public List<Badge> AllBadges { get; set; }

        public IEnumerable<Badge> CanIdleBadges
        {
            //get { return AllBadges.Where(b => b.RemainingCard != 0); }
            get { return AllBadges; }
        }

        public bool IsCookieReady;
        public bool IsSteamReady;
        public int TimeLeft = 900;
        public int TimeSet = 300;
        public int RetryCount = 0;
        public int ReloadCount = 0;
        public int CardsRemaining { get { return CanIdleBadges.Sum(b => b.RemainingCard); } }
        public int GamesRemaining { get { return CanIdleBadges.Count(); } }
        public Badge CurrentBadge;

        internal void UpdateStateInfo()
        {
            // Update totals
            if (ReloadCount == 0)
            {
                lblIdle.Text = string.Format("{0} " + localization.strings.games_left_to_idle, GamesRemaining);
                lblDrops.Text = CardsRemaining + " " + localization.strings.card_drops_remaining;
                lblIdle.Visible = GamesRemaining != 0;
                lblDrops.Visible = CardsRemaining != 0;
            }
        }

        private void CopyResource(string resourceName, string file)
        {
            using (var resource = GetType().Assembly.GetManifestResourceStream(resourceName))
            {
                if (resource == null)
                {
                    return;
                }
                using (Stream output = File.OpenWrite(file))
                {
                    resource.CopyTo(output);
                }
            }
        }

        public void SortBadges(string method)
        {
            lblDrops.Text = localization.strings.sorting_results;
            switch (method)
            {
                case "mostcards":
                    AllBadges = AllBadges.OrderByDescending(b => b.RemainingCard).ToList();
                    break;
                case "leastcards":
                    AllBadges = AllBadges.OrderBy(b => b.RemainingCard).ToList();
                    break;
                default:
                    return;
            }
        }

        public void UpdateIdleProcesses()
        {
            // JN: Loop through all badges that can be idled (still has card drops)
            //foreach (var badge in CanIdleBadges.Where(b => !Equals(b, CurrentBadge)))
            foreach (var badge in CanIdleBadges)
            {
                if(!Settings.Default.fastMode)
                {
                    // JN: Original idle mode
                    // uwu if (badge.HoursPlayed >= 2 && badge.InIdle)
                    //    badge.StopIdle();

                    if (badge.HoursPlayed < 2 && CanIdleBadges.Count(b => b.InIdle) < 30)
                        badge.Idle();
                }
                else
                {
                    // JN: Fast mode (still limit to 30 (?))
                    if (CanIdleBadges.Count(b => b.InIdle) < 30)
                        badge.Idle();
                }
                
            }

            RefreshGamesStateListView();

            if (!CanIdleBadges.Any(b => b.InIdle))
                NextIdle();

            UpdateStateInfo();
        }

        private void NextIdle()
        {
            // Stop idling the current game
            StopIdle();

            // Check if user is authenticated and if any badge left to idle
            // There should be check for IsCookieReady, but property is set in timer tick, so it could take some time to be set.
            if (string.IsNullOrWhiteSpace(Settings.Default.sessionid) || !IsSteamReady)
            {
                ResetClientStatus();
            }
            else
            {
                if (CanIdleBadges.Any())
                {
                    // Give the user notification that the next game will start soon
                    lblCurrentStatus.Text = localization.strings.loading_next;

                    // Make a short but random amount of time pass
                    var rand = new Random();
                    var wait = rand.Next(3, 9);
                    wait = wait * 1000;

                    tmrStartNext.Interval = wait;
                    tmrStartNext.Enabled = true;

                    UpdateStateInfo();
                }
                else
                {
                    IdleComplete();
                }
            }
        }

        private void StartIdle() // aka1
        {
            // Kill all existing processes before starting any new ones
            // This prevents rogue processes from interfering with idling time and slowing card drops
            try 
            {
                String username = WindowsIdentity.GetCurrent().Name;
                foreach (var process in Process.GetProcessesByName("steam-idle"))
                {
                    ManagementObjectSearcher searcher = new ManagementObjectSearcher("Select * From Win32_Process Where ProcessID = " + process.Id);
                    ManagementObjectCollection processList = searcher.Get();

                    foreach (ManagementObject obj in processList)
                    {
                        string[] argList = new string[] { string.Empty, string.Empty };
                        int returnVal = Convert.ToInt32(obj.InvokeMethod("GetOwner", argList));
                        if (returnVal == 0)
                        {
                            if (argList[1] + "\\" + argList[0] == username)
                            {
                                process.Kill();
                            }
                        }
                    }
                    
                }
            }
            catch (Exception)
            {

            }
            
            // Check if user is authenticated and if any badge left to idle
            // There should be check for IsCookieReady, but property is set in timer tick, so it could take some time to be set.
            if (string.IsNullOrWhiteSpace(Settings.Default.sessionid) || !IsSteamReady)
            {
                ResetClientStatus();
            }
            else
            {
                if (ReloadCount != 0)
                {
                    return;
                }
                if (CanIdleBadges.Any())
                {

                    //var appidETS2 = 227300;
                    StartSoloIdle(CanIdleBadges.First());
                }
                else
                {
                    IdleComplete();
                }

                //UpdateStateInfo();
            }
        }

        public void StartSoloIdle(Badge badge) // aka2
        {
            // Set the currentAppID value
            CurrentBadge = badge;

            // Place user "In game" for card drops
            CurrentBadge.Idle();

            // Update game name
            lblGameName.Visible = true;
            lblGameName.Text = CurrentBadge.Name;

            GamesState.Visible = false;
            gameToolStripMenuItem.Enabled = true;

            // Update game image
            try
            {
                picApp.Load("http://cdn.akamai.steamstatic.com/steam/apps/" + CurrentBadge.StringId + "/header_292x136.jpg");
                picApp.Visible = true;
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "frmMain -> StartIdle -> load pic, for id = " + CurrentBadge.AppId);
            }

            // Update label controls
            lblCurrentStatus.Visible = true;
            lblCurrentStatus.Text = localization.strings.currently_ingame;
            lblHoursPlayed.Visible = true;
            lblHoursPlayed.Text = CurrentBadge.HoursPlayed + " " + localization.strings.hrs_on_record;

            // Start the animated "working" gif
            picIdleStatus.Image = Settings.Default.customTheme ? Resources.imgSpinInv : Resources.imgSpin;

            // Set the correct buttons on the form for pause / resume
            btnResume.Visible = false;
            btnPause.Visible = true;
            btnSkip.Visible = true;
            resumeIdlingToolStripMenuItem.Enabled = false;
            pauseIdlingToolStripMenuItem.Enabled = false;
            skipGameToolStripMenuItem.Enabled = false;

            var scale = CreateGraphics().DpiY * 3.9;
            Height = Convert.ToInt32(scale);
        }

        public void StartMultipleIdle()
        {
            // Start the idling processes
            UpdateIdleProcesses();

            // Update label controls
            lblCurrentStatus.Text = localization.strings.currently_ingame;

            lblGameName.Visible = false;
            lblHoursPlayed.Visible = false;
            ssFooter.Visible = true;
            gameToolStripMenuItem.Enabled = false;

            // Start the animated "working" gif
            picIdleStatus.Image = Settings.Default.customTheme ? Resources.imgSpinInv : Resources.imgSpin;

            // Start the timer that will check if drops remain
            tmrCardDropCheck.Enabled = true;

            // Reset the timer
            TimeLeft = 360;

            // Show game
            GamesState.Visible = true;
            picApp.Visible = false;
            RefreshGamesStateListView();

            // Set the correct buttons on the form for pause / resume
            btnResume.Visible = false;
            btnPause.Visible = false;
            btnSkip.Visible = false;
            resumeIdlingToolStripMenuItem.Enabled = false;
            pauseIdlingToolStripMenuItem.Enabled = false;
            skipGameToolStripMenuItem.Enabled = false;

            var scale = CreateGraphics().DpiY * 3.86;
            Height = Convert.ToInt32(scale);
        }

        private void RefreshGamesStateListView()
        {
            GamesState.Items.Clear();
            foreach (var badge in CanIdleBadges.Where(b => b.InIdle))
            {
                var line = new ListViewItem(badge.Name);
                line.SubItems.Add(badge.HoursPlayed.ToString());
                GamesState.Items.Add(line);
            }

            // JN: Recolor the listview
            GamesState.BackColor = Settings.Default.customTheme ? Settings.Default.colorBgd : Settings.Default.colorBgdOriginal;
            GamesState.ForeColor = Settings.Default.customTheme ? Settings.Default.colorTxt : Settings.Default.colorTxtOriginal;
        }

        public void StopIdle()
        {
            try
            {
                lblGameName.Visible = false;
                picApp.Image = null;
                picApp.Visible = false;
                GamesState.Visible = false;
                btnPause.Visible = false;
                btnSkip.Visible = false;
                lblCurrentStatus.Text = localization.strings.not_ingame;
                lblHoursPlayed.Visible = false;
                picIdleStatus.Image = null;

                // Hide the status bar
                ssFooter.Visible = false;

                // Resize the form
                var graphics = CreateGraphics();
                var scale = graphics.DpiY * 1.9583;
                Height = Convert.ToInt32(scale);

                // Kill the idling process
                foreach (var badge in AllBadges.Where(b => b.InIdle))
                    badge.StopIdle();
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "frmMain -> StopIdle");
            }
        }

        public void IdleComplete() //aka3
        {
            // Deactivate the timer control and inform the user that the program is finished
            tmrCardDropCheck.Enabled = false;
            lblCurrentStatus.Text = localization.strings.idling_complete;

            lblGameName.Visible = false;
            btnPause.Visible = false;
            btnSkip.Visible = true;

            // Llamamos a función que muestre juego para sumar horas

            // Resize the form
            var graphics = CreateGraphics();
            var scale = graphics.DpiY * 1.9583;
            Height = Convert.ToInt32(scale);
        }


        public async Task LoadBadgesAsync()
        {
            // Settings.Default.myProfileURL = http://steamcommunity.com/id/USER
            var profileLink = Settings.Default.myProfileURL + "/badges";
            var pages = new List<string>() { "?p=1" };
            var document = new HtmlDocument();
            int pagesCount = 1;

            // Adjust the spinner gif based on the current color theme
            picReadingPage.Image = Settings.Default.customTheme ? Resources.imgSpinInv : Resources.imgSpin;

            try
            {
                // Load Page 1 and check how many pages there are
                var pageURL = string.Format("{0}/?p={1}", profileLink, 1);
                var response = await CookieClient.GetHttpAsync(pageURL);
                // Response should be empty. User should be unauthorised.
                if (string.IsNullOrEmpty(response))
                {
                    RetryCount++;
                    if (RetryCount == 18)
                    {
                        ResetClientStatus();
                        return;
                    }
                    throw new Exception("");
                }
                document.LoadHtml(response);

                // If user is authenticated, check page count. If user is not authenticated, pages are different.
                var pageNodes = document.DocumentNode.SelectNodes("//a[@class=\"pagelink\"]");
                if (pageNodes != null)
                {
                    pages.AddRange(pageNodes.Select(p => p.Attributes["href"].Value).Distinct());
                    pages = pages.Distinct().ToList();
                }

                string lastpage = pages.Last().ToString().Replace("?p=", "");
                pagesCount = Convert.ToInt32(lastpage);

                // Get all badges from current page
                ProcessBadgesOnPage(document);

                // Load other pages
                for (var i = 2; i <= pagesCount; i++)
                {
                    lblDrops.Text = string.Format(localization.strings.reading_badge_page + " {0}/{1}, " + localization.strings.please_wait, i, pagesCount);

                    // Load Page 2+
                    pageURL = string.Format("{0}/?p={1}", profileLink, i);
                    response = await CookieClient.GetHttpAsync(pageURL);
                    // Response should be empty. User should be unauthorised.
                    if (string.IsNullOrEmpty(response))
                    {
                        RetryCount++;
                        if (RetryCount == 18)
                        {
                            ResetClientStatus();
                            return;
                        }
                        throw new Exception("");
                    }
                    document.LoadHtml(response);

                    // Get all badges from current page
                    ProcessBadgesOnPage(document);
                }
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "Badge -> LoadBadgesAsync, for profile = " + Settings.Default.myProfileURL);
                // badge page didn't load
                picReadingPage.Image = null;
                picIdleStatus.Image = null;
                lblDrops.Text = localization.strings.badge_didnt_load.Replace("__num__", "10");
                lblIdle.Text = "";

                // Set the form height
                var graphics = CreateGraphics();
                var scale = graphics.DpiY * 1.625;
                Height = Convert.ToInt32(scale);
                ssFooter.Visible = false;

                ReloadCount = 1;
                tmrBadgeReload.Enabled = true;
                return;
            }

            RetryCount = 0;
            SortBadges(Settings.Default.sort);

            picReadingPage.Visible = false;
            UpdateStateInfo();

            //if (CardsRemaining == 0)
            //{
            //    IdleComplete();
            //}
        }

        /// <summary>
        /// Processes all badges on page
        /// </summary>
        /// <param name="document">HTML document (1 page) from x</param>
        private void ProcessBadgesOnPage(HtmlDocument document) // AKA6
        {
            foreach (var badge in document.DocumentNode.SelectNodes("//div[@class=\"badge_row is_link\"]"))
            {
                var appIdNode = badge.SelectSingleNode(".//a[@class=\"badge_row_overlay\"]").Attributes["href"].Value;
                var appid = Regex.Match(appIdNode, @"gamecards/(\d+)/").Groups[1].Value;

                if (string.IsNullOrWhiteSpace(appid) || Settings.Default.blacklist.Contains(appid) || appid == "368020" || appid == "335590" || appIdNode.Contains("border=1"))
                {
                    continue;
                }

                var hoursNode = badge.SelectSingleNode(".//div[@class=\"badge_title_stats_playtime\"]");
                var hours = hoursNode == null ? string.Empty : Regex.Match(hoursNode.InnerText, @"[0-9\.,]+").Value;

                var nameNode = badge.SelectSingleNode(".//div[@class=\"badge_title\"]");
                var name = WebUtility.HtmlDecode(nameNode.FirstChild.InnerText).Trim();

                var cardNode = badge.SelectSingleNode(".//span[@class=\"progress_info_bold\"]");
                var cards = cardNode == null ? string.Empty : Regex.Match(cardNode.InnerText, @"[0-9]+").Value;

                var badgeInMemory = AllBadges.FirstOrDefault(b => b.StringId == appid);
                if (badgeInMemory != null)
                {
                    badgeInMemory.UpdateStats(cards, hours);
                }
                else
                {
                    // un 0 antes = desactivado
                    var ets2 = "227300";
                    var ats = "270880"; // en blacklist
                    var gtav = "271590"; // si no tiene cromos no pasa por la lista
                    var rocket = "0252950";
                    // Agregamos if para regular que juegos ingresan a la lista
                    // WhiteList
                    if (String.Equals(appid, gtav) || String.Equals(appid, ets2) || String.Equals(appid, ats) || String.Equals(appid, rocket))
                    {
                        AllBadges.Add(new Badge(appid, name, cards, hours));
                    }
                }
            }
        }

        // CONSTRUCTOR
        public frmMain()
        {
            InitializeComponent();
            AllBadges = new List<Badge>(); // aka7
        }

        private void frmMain_Load(object sender, EventArgs e)
        {
            // Copy external references to the output directory.  This allows ClickOnce install.
            if (File.Exists(Environment.CurrentDirectory + "\\steam_api.dll") == false)
            {
                CopyResource("IdleMaster.Resources.steam_api.dll", Environment.CurrentDirectory + @"\steam_api.dll");
            }
            if (File.Exists(Environment.CurrentDirectory + "\\CSteamworks.dll") == false)
            {
                CopyResource("IdleMaster.Resources.CSteamworks.dll", Environment.CurrentDirectory + @"\CSteamworks.dll");
            }
            if (File.Exists(Environment.CurrentDirectory + "\\steam-idle.exe") == false)
            {
                CopyResource("IdleMaster.Resources.steam-idle.exe", Environment.CurrentDirectory + @"\steam-idle.exe");
            }

            // Update the settings, if needed.  When the application updates, settings will persist.
            if (Settings.Default.updateNeeded)
            {
                Settings.Default.Upgrade();
                Settings.Default.updateNeeded = false;
                Settings.Default.Save();
            }

            // Set the interface language from the settings
            if (Settings.Default.language != "")
            {
                string language_string = "";
                switch (Settings.Default.language)
                {
                    case "English":
                        language_string = "en";
                        break;
                    case "Spanish":
                        language_string = "es";
                        break;
                    default:
                        language_string = "es";
                        break;
                }
                Thread.CurrentThread.CurrentUICulture = new CultureInfo(language_string);                
            }            

            // Localize form elements
            fileToolStripMenuItem.Text = localization.strings.file;
            gameToolStripMenuItem.Text = localization.strings.game;
            helpToolStripMenuItem.Text = localization.strings.help;
            settingsToolStripMenuItem.Text = localization.strings.settings;
            blacklistToolStripMenuItem.Text = localization.strings.blacklist;
            exitToolStripMenuItem.Text = localization.strings.exit;
            pauseIdlingToolStripMenuItem.Text = localization.strings.pause_idling;
            resumeIdlingToolStripMenuItem.Text = localization.strings.resume_idling;
            skipGameToolStripMenuItem.Text = localization.strings.skip_current_game;
            aboutToolStripMenuItem.Text = localization.strings.about;
            lnkSignIn.Text = "(" + localization.strings.sign_in + ")";
            lnkResetCookies.Text = "(" + localization.strings.sign_out + ")";
            
            lblSignedOnAs.Text = localization.strings.signed_in_as;
            GamesState.Columns[0].Text = localization.strings.name;
            GamesState.Columns[1].Text = localization.strings.hours;

            // Set the form height
            var graphics = CreateGraphics();
            var scale = graphics.DpiY * 1.625;
            Height = Convert.ToInt32(scale);

            // Set the location of certain elements so that they scale correctly for different DPI settings
            var point = new Point(Convert.ToInt32(graphics.DpiX * 1.14), Convert.ToInt32(lblGameName.Location.Y));
            lblGameName.Location = point;
            point = new Point(Convert.ToInt32(graphics.DpiX * 2.35), Convert.ToInt32(lnkSignIn.Location.Y));
            lnkSignIn.Location = point;
            point = new Point(Convert.ToInt32(graphics.DpiX * 2.15), Convert.ToInt32(lnkResetCookies.Location.Y));
            lnkResetCookies.Location = point;

            runtimeCustomThemeMain(); // JN: Apply the dark theme


            //Prevent Sleep
            if (Settings.Default.NoSleep == true)
            {
                PreventSleep();
            }
        }

        /// <summary>
        /// Changes the color of the main window components to match a Steam-like dark theme
        /// </summary> 
        private void runtimeCustomThemeMain()
        {
            // Read settings
            var customTheme = Settings.Default.customTheme;
            var whiteIcons = Settings.Default.whiteIcons;

            // Define colors
            FlatStyle buttonStyle = customTheme ? FlatStyle.Flat : FlatStyle.Standard;
            Color colorBgd = customTheme ? Settings.Default.colorBgd : Settings.Default.colorBgdOriginal;
            Color colorTxt = customTheme ? Settings.Default.colorTxt : Settings.Default.colorTxtOriginal;

            // --------------------------
            // -- APPLY THEME SETTINGS --
            // --------------------------

            // Main frame window
            this.BackColor = colorBgd;
            this.ForeColor = colorTxt;

            // Link colors
            lnkSignIn.LinkColor = lnkResetCookies.LinkColor = lblGameName.LinkColor = customTheme ? Color.GhostWhite : Color.Blue;

            // ToolStripMenu Top
            mnuTop.BackColor = colorBgd;
            mnuTop.ForeColor = colorTxt;

            // ToolStripMenuItem and the ToolStripMenuItem dropdowns
            foreach (ToolStripMenuItem item in mnuTop.Items)
            {
                // Menu item coloring
                item.BackColor = colorBgd;
                item.ForeColor = colorTxt;

                // Dropdown coloring
                item.DropDown.BackColor = colorBgd;
                item.DropDown.ForeColor = colorTxt;
            }

            // Game state list (needs to be colored in RefreshGamesStateListView)
            GamesState.BackColor = colorBgd;
            GamesState.ForeColor = colorTxt;

            // Footer
            ssFooter.BackColor = colorBgd;

            // Buttons
            btnPause.FlatStyle = btnResume.FlatStyle = btnSkip.FlatStyle = buttonStyle;
            btnPause.BackColor = btnResume.BackColor = btnSkip.BackColor = colorBgd;
            btnPause.ForeColor = btnResume.ForeColor = btnSkip.ForeColor = colorTxt;

            // Icon images
            runtimeCustomIcons();
        }

        /// <summary>
        /// Replaces the main frame window images with white ones for the dark theme
        /// </summary> 
        private void runtimeCustomIcons()
        {
            var customTheme = Settings.Default.customTheme;
            var whiteIcons = Settings.Default.whiteIcons;

            // TOOL STRIP MENU ITEMS
            // File
            settingsToolStripMenuItem.Image = whiteIcons ? Resources.imgSettings_w : Resources.imgSettings;
            blacklistToolStripMenuItem.Image = whiteIcons ? Resources.imgBlacklist_w : Resources.imgBlacklist;
            exitToolStripMenuItem.Image = whiteIcons ? Resources.imgExit_w : Resources.imgExit;
            // Game
            pauseIdlingToolStripMenuItem.Image = whiteIcons ? Resources.imgPause_w : Resources.imgPause;
            resumeIdlingToolStripMenuItem.Image = whiteIcons ? Resources.imgPlay_w : Resources.imgPlay;
            skipGameToolStripMenuItem.Image = whiteIcons ? Resources.imgSkip_w : Resources.imgSkip;
            // Help
            aboutToolStripMenuItem.Image = whiteIcons ? Resources.imgInfo_w : Resources.imgInfo;

            // STATUS
            // Handled in respective tick drawing functions

            // BUTTONS
            btnPause.Image = whiteIcons ? Resources.imgPauseSmall_w : Resources.imgPauseSmall;
            btnResume.Image = whiteIcons ? Resources.imgPlaySmall_w : Resources.imgPlaySmall;
            btnSkip.Image = whiteIcons ? Resources.imgSkipSmall_w : Resources.imgSkipSmall;

            // LOADING GIF
            //
        }

        private void frmMain_FormClose(object sender, FormClosedEventArgs e)
        {
            StopIdle();
        }

        private void tmrCheckCookieData_Tick(object sender, EventArgs e)
        {
            // JN: White icons
            var whiteIcons = Settings.Default.whiteIcons;
            var imgFalse = whiteIcons ? Resources.imgFalse_w : Resources.imgFalse;
            var imgTrue = whiteIcons ? Resources.imgTrue_w : Resources.imgTrue;
            runtimeCustomThemeMain();

            var connected = !string.IsNullOrWhiteSpace(Settings.Default.sessionid) && !string.IsNullOrWhiteSpace(Settings.Default.steamLoginSecure);

            var colorGreen = Settings.Default.customTheme ? Settings.Default.colorSteamGreen : Color.Green; // Adjust the green depending on the theme
            
            lblCookieStatus.Text = connected ? localization.strings.idle_master_connected : localization.strings.idle_master_notconnected;
            lblCookieStatus.ForeColor = connected ? colorGreen : this.ForeColor; // JN: Changed the color of "not connected" message
            picCookieStatus.Image = connected ? imgTrue : imgFalse; // JN: Supports dark theme
            lnkSignIn.Visible = !connected;
            lnkResetCookies.Visible = connected;
            IsCookieReady = connected;
        }

        private void tmrCheckSteam_Tick(object sender, EventArgs e)
        {
            // JN: White icons
            var whiteIcons = Settings.Default.whiteIcons;
            var imgFalse = whiteIcons ? Resources.imgFalse_w : Resources.imgFalse;
            var imgTrue = whiteIcons ? Resources.imgTrue_w : Resources.imgTrue;

            var colorGreen = Settings.Default.customTheme ? Settings.Default.colorSteamGreen : Color.Green; // Adjust the green depending on the theme
            
            var isSteamRunning = SteamAPI.IsSteamRunning() || Settings.Default.ignoreclient;
            lblSteamStatus.Text = isSteamRunning ? (Settings.Default.ignoreclient ? localization.strings.steam_ignored : localization.strings.steam_running) : localization.strings.steam_notrunning;
            lblSteamStatus.ForeColor = isSteamRunning ? colorGreen : this.ForeColor; // JN: Changed color of the not connected status
            picSteamStatus.Image = isSteamRunning ? imgTrue : imgFalse; // JN: Supports dark theme
            tmrCheckSteam.Interval = isSteamRunning ? 5000 : 500;
            skipGameToolStripMenuItem.Enabled = isSteamRunning;
            pauseIdlingToolStripMenuItem.Enabled = isSteamRunning;
            IsSteamReady = isSteamRunning;
        }

        private void lblGameName_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e) // este abre una ventana a la url del juego en steam
        {
            Process.Start("https://store.steampowered.com/app/" + CurrentBadge.AppId);
        }

        private void lnkResetCookies_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            ResetClientStatus();
        }

        /// <summary>
        /// Performs reset to initial state
        /// </summary>
        private void ResetClientStatus()
        {
            // Clear the settings
            Settings.Default.sessionid = string.Empty;
            Settings.Default.steamLogin = string.Empty;
            Settings.Default.steamLoginSecure = string.Empty;
            Settings.Default.myProfileURL = string.Empty;
            Settings.Default.steamparental = string.Empty;
            Settings.Default.Save();

            // Stop the steam-idle process
            StopIdle();

            // Clear the badges list
            AllBadges.Clear();

            // Resize the form
            var graphics = CreateGraphics();
            var scale = graphics.DpiY * 1.625;
            Height = Convert.ToInt32(scale);

            // Set timer intervals
            tmrCheckSteam.Interval = 500;
            tmrCheckCookieData.Interval = 500;

            // Hide signed user name
            if (Settings.Default.showUsername)
            {
                lblSignedOnAs.Text = String.Empty;
                lblSignedOnAs.Visible = false;
            }

            // Hide spinners
            picReadingPage.Visible = false;

            // Hide lblDrops and lblIdle
            lblDrops.Visible = false;
            lblIdle.Visible = false;

            // Set IsCookieReady to false
            IsCookieReady = false;

            // Re-enable tmrReadyToGo
            tmrReadyToGo.Enabled = true;
        }

        private void lnkSignIn_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            var frm = new frmBrowser();
            frm.ShowDialog();
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }

        private async void tmrReadyToGo_Tick(object sender, EventArgs e)
        {
            if (!IsCookieReady || !IsSteamReady)
                return;

            // Update the form elements
            if (Settings.Default.showUsername)
            {
                lblSignedOnAs.Text = SteamProfile.GetSignedAs();
                lblSignedOnAs.Visible = true;
            }

            lblDrops.Visible = true;
            lblDrops.Text = localization.strings.reading_badge_page + ", " + localization.strings.please_wait;
            lblIdle.Visible = false;
            picReadingPage.Visible = true;

            tmrReadyToGo.Enabled = false;

            // Call the loadBadges() function asynchronously
            await LoadBadgesAsync();

            StartIdle();
        }


        private async void tmrCardDropCheck_Tick(object sender, EventArgs e)
        {
            if (TimeLeft <= 0)
            {
                tmrCardDropCheck.Enabled = false;
                if (CurrentBadge != null)
                {
                    CurrentBadge.Idle();
                }

                var isMultipleIdle = CanIdleBadges.Any(b => !Equals(b, CurrentBadge) && b.InIdle);
                if (isMultipleIdle)
                {
                    lblDrops.Visible = true;
                    lblDrops.Text = localization.strings.reading_badge_page + ", " + localization.strings.please_wait;
                    lblIdle.Visible = false;
                    picReadingPage.Visible = true;
                    await LoadBadgesAsync();
                }

                // Check if user is authenticated and if any badge left to idle
                // There should be check for IsCookieReady, but property is set in timer tick, so it could take some time to be set.
                tmrCardDropCheck.Enabled = !string.IsNullOrWhiteSpace(Settings.Default.sessionid) && IsSteamReady && CanIdleBadges.Any() && TimeLeft != 0;
            }
            else
            {
                TimeLeft = TimeLeft - 1;
            }
        }

        private async void btnSkip_Click(object sender, EventArgs e)
        {
            if (!IsSteamReady)
                return;

            lblCurrentStatus.Visible = false;
            StopIdle();
            AllBadges.RemoveAll(b => Equals(b, CurrentBadge));
            
            if(!CanIdleBadges.Any())
            {
                // If there are no more games to idle, reload the badges
                picReadingPage.Visible = true;
                lblIdle.Visible = false;
                lblDrops.Visible = true;
                lblDrops.Text = localization.strings.reading_badge_page + ", " + localization.strings.please_wait;
                await LoadBadgesAsync();
            }

            StartIdle();
        }

        private void btnPause_Click(object sender, EventArgs e)
        {
            if (!IsSteamReady)
                return;

            // Stop the steam-idle process
            StopIdle();

            // Indicate to the user that idling has been paused
            lblCurrentStatus.Text = localization.strings.idling_paused;

            // Set the correct button visibility
            btnResume.Visible = true;
            btnPause.Visible = false;
            pauseIdlingToolStripMenuItem.Enabled = false;
            resumeIdlingToolStripMenuItem.Enabled = true;

            // Focus the resume button
            btnResume.Focus();
        }

        private void btnResume_Click(object sender, EventArgs e)
        {
            // Resume idling
            StartIdle();

            pauseIdlingToolStripMenuItem.Enabled = true;
            resumeIdlingToolStripMenuItem.Enabled = false;
        }

        private void settingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // Show the form
            String previous = Settings.Default.sort;
            Boolean previous_behavior = Settings.Default.OnlyOneGameIdle;
            Boolean previous_behavior2 = Settings.Default.OneThenMany;
            Form frm = new frmSettings();
            frm.ShowDialog();

            if (previous != Settings.Default.sort || previous_behavior != Settings.Default.OnlyOneGameIdle || previous_behavior2 != Settings.Default.OneThenMany)
            {
                StopIdle();
                AllBadges.Clear();
                tmrReadyToGo.Enabled = true;
            }

            if (Settings.Default.showUsername && IsCookieReady)
            {
                lblSignedOnAs.Text = SteamProfile.GetSignedAs();
                lblSignedOnAs.Visible = Settings.Default.showUsername;
            }            
        }

        private void pauseIdlingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            btnPause.PerformClick();
        }

        private void resumeIdlingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            btnResume.PerformClick();
        }

        private void skipGameToolStripMenuItem_Click(object sender, EventArgs e)
        {
            btnSkip.PerformClick();
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var frm = new frmAbout();
            frm.ShowDialog();
        }

        private void frmMain_Resize(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized)
            {
                if (Settings.Default.minToTray)
                {
                    notifyIcon1.Visible = true;
                    Hide();
                }
            }
            else if (WindowState == FormWindowState.Normal)
            {
                notifyIcon1.Visible = false;
            }
        }

        private void notifyIcon1_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            Show();
            WindowState = FormWindowState.Normal;
        }

        private void lblCurrentRemaining_Click(object sender, EventArgs e)
        {
            if (TimeLeft > 2)
            {
                TimeLeft = 2;
            }
        }

        private void blacklistToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var frm = new frmBlacklist();
            frm.ShowDialog();

            if (Settings.Default.blacklist.Cast<string>().Any(appid => appid == CurrentBadge.StringId))
                btnSkip.PerformClick();
        }

        private void tmrStartNext_Tick(object sender, EventArgs e)
        {
            tmrStartNext.Enabled = false;
            StartIdle();
        }


        private void tmrBadgeReload_Tick(object sender, EventArgs e)
        {
            ReloadCount = ReloadCount + 1;
            lblDrops.Text = localization.strings.badge_didnt_load.Replace("__num__", (10 - ReloadCount).ToString());
            
            if (ReloadCount == 10)
            {
                tmrBadgeReload.Enabled = false;
                tmrReadyToGo.Enabled = true;
                ReloadCount = 0;
            }
        }

        private void tmrStatistics_Tick(object sender, EventArgs e) // BORRAR
        {
            //statistics.increaseMinutesIdled();
            //statistics.checkCardRemaining((uint)CardsRemaining);
        }

        private void Form1_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            //Restore Sleep Settings on close
            if (Settings.Default.NoSleep == true)
            {
                AllowSleep();
            }
            this.Close();
        }
    }
}
/* Copyright (c) 2024 Rick (rick 'at' gibbed 'dot' us)
 *
 * This software is provided 'as-is', without any express or implied
 * warranty. In no event will the authors be held liable for any damages
 * arising from the use of this software.
 *
 * Permission is granted to anyone to use this software for any purpose,
 * including commercial applications, and to alter it and redistribute it
 * freely, subject to the following restrictions:
 *
 * 1. The origin of this software must not be misrepresented; you must not
 *    claim that you wrote the original software. If you use this software
 *    in a product, an acknowledgment in the product documentation would
 *    be appreciated but is not required.
 *
 * 2. Altered source versions must be plainly marked as such, and must not
 *    be misrepresented as being the original software.
 *
 * 3. This notice may not be removed or altered from any source
 *    distribution.
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Xml;
using System.Xml.XPath;
using static SAM.Picker.InvariantShorthand;
using APITypes = SAM.API.Types;

namespace SAM.Picker
{
    internal partial class GamePicker : Form
    {
        private static readonly object _ScanLogLock = new();
        private static readonly string[] _LoadingSpinnerFrames = { "|", "/", "-", "\\" };

        private enum GameViewMode
        {
            Grid,
            List,
        }

        private enum GameSortMode
        {
            NameAscending,
            NameDescending,
            AchievementAscending,
            AchievementDescending,
        }

        private enum AchievementScanMode
        {
            Auto,
            LocalCheckAll,
        }

        private readonly API.Client _SteamClient;

        private readonly Dictionary<uint, GameInfo> _Games;
        private readonly List<GameInfo> _FilteredGames;

        private readonly object _LogoLock;
        private readonly HashSet<string> _LogosAttempting;
        private readonly HashSet<string> _LogosAttempted;
        private readonly ConcurrentQueue<GameInfo> _LogoQueue;

        private readonly API.Callbacks.AppDataChanged _AppDataChangedCallback;
        private readonly string _SteamWebApiKey;
        private Dictionary<string, string> _SteamCommunityCookies;

        private int _AchievementScanCompleted;
        private int _AchievementScanTotal;
        private bool _AchievementScanPending;
        private bool _ReloadGamesPending;
        private AchievementScanMode _CurrentAchievementScanMode;
        private readonly BackgroundWorker _UnlockAllWorker;
        private int _UnlockAllCompleted;
        private int _UnlockAllTotal;
        private int _LoadingSpinnerFrame;
        private GameViewMode _ViewMode;
        private GameSortMode _SortMode;

        private sealed class AchievementScanRequest
        {
            public readonly List<uint> GameIds;
            public readonly ulong SteamId;
            public readonly string SteamWebApiKey;
            public readonly Dictionary<string, string> CommunityCookies;
            public readonly AchievementScanMode Mode;

            public AchievementScanRequest(
                List<uint> gameIds,
                ulong steamId,
                string steamWebApiKey,
                Dictionary<string, string> communityCookies,
                AchievementScanMode mode)
            {
                this.GameIds = gameIds;
                this.SteamId = steamId;
                this.SteamWebApiKey = steamWebApiKey;
                this.CommunityCookies = communityCookies != null
                    ? new Dictionary<string, string>(communityCookies, StringComparer.OrdinalIgnoreCase)
                    : null;
                this.Mode = mode;
            }
        }

        private sealed class AchievementScanProgress
        {
            public readonly uint GameId;
            public readonly int Unlocked;
            public readonly int Total;

            public AchievementScanProgress(uint gameId, int unlocked, int total)
            {
                this.GameId = gameId;
                this.Unlocked = unlocked;
                this.Total = total;
            }
        }

        private sealed class UnlockAllRequest
        {
            public readonly List<uint> GameIds;

            public UnlockAllRequest(List<uint> gameIds)
            {
                this.GameIds = gameIds;
            }
        }

        private sealed class UnlockAllProgress
        {
            public readonly uint GameId;
            public readonly bool Success;
            public readonly int Changed;
            public readonly int SkippedProtected;
            public readonly int Unlocked;
            public readonly int Total;
            public readonly string Error;

            public UnlockAllProgress(
                uint gameId,
                bool success,
                int changed,
                int skippedProtected,
                int unlocked,
                int total,
                string error)
            {
                this.GameId = gameId;
                this.Success = success;
                this.Changed = changed;
                this.SkippedProtected = skippedProtected;
                this.Unlocked = unlocked;
                this.Total = total;
                this.Error = error;
            }
        }

        [DataContract]
        private sealed class PlayerAchievementsResponse
        {
            [DataMember(Name = "playerstats")]
            public PlayerStats PlayerStats { get; set; }
        }

        [DataContract]
        private sealed class PlayerStats
        {
            [DataMember(Name = "success")]
            public bool Success { get; set; }

            [DataMember(Name = "error")]
            public string Error { get; set; }

            [DataMember(Name = "achievements")]
            public PlayerAchievement[] Achievements { get; set; }
        }

        [DataContract]
        private sealed class PlayerAchievement
        {
            [DataMember(Name = "achieved")]
            public int Achieved { get; set; }
        }

        [DataContract]
        private sealed class AppListResponse
        {
            [DataMember(Name = "applist")]
            public AppListPayload AppList { get; set; }
        }

        [DataContract]
        private sealed class AppListPayload
        {
            [DataMember(Name = "apps")]
            public AppListEntry[] Apps { get; set; }
        }

        [DataContract]
        private sealed class AppListEntry
        {
            [DataMember(Name = "appid")]
            public uint AppId { get; set; }
        }

        private sealed class OwnedGameInfo
        {
            public uint AppId { get; set; }
            public string Name { get; set; }
            public bool HasStatsLink { get; set; }
            public int AchievementUnlocked { get; set; } = -1;
            public int AchievementTotal { get; set; } = -1;
            public bool HasAchievementProgress => this.AchievementUnlocked >= 0 && this.AchievementTotal >= 0;
        }

        [DataContract]
        private sealed class OwnedGamesResponse
        {
            [DataMember(Name = "response")]
            public OwnedGamesPayload Response { get; set; }
        }

        [DataContract]
        private sealed class OwnedGamesPayload
        {
            [DataMember(Name = "games")]
            public OwnedGameEntry[] Games { get; set; }
        }

        [DataContract]
        private sealed class OwnedGameEntry
        {
            [DataMember(Name = "appid")]
            public uint AppId { get; set; }

            [DataMember(Name = "name")]
            public string Name { get; set; }
        }

        [DataContract]
        private sealed class PickerPreferences
        {
            [DataMember(Name = "view_mode")]
            public string ViewMode { get; set; }

            [DataMember(Name = "sort_mode")]
            public string SortMode { get; set; }
        }

        [DataContract]
        private sealed class AchievementStatusDatabase
        {
            [DataMember(Name = "generated_utc")]
            public string GeneratedUtc { get; set; }

            [DataMember(Name = "steam_id")]
            public ulong SteamId { get; set; }

            [DataMember(Name = "scan_mode")]
            public string ScanMode { get; set; }

            [DataMember(Name = "games")]
            public AchievementStatusEntry[] Games { get; set; }
        }

        [DataContract]
        private sealed class AchievementStatusEntry
        {
            [DataMember(Name = "app_id")]
            public uint AppId { get; set; }

            [DataMember(Name = "name")]
            public string Name { get; set; }

            [DataMember(Name = "type")]
            public string Type { get; set; }

            [DataMember(Name = "achievement_unlocked")]
            public int AchievementUnlocked { get; set; }

            [DataMember(Name = "achievement_total")]
            public int AchievementTotal { get; set; }

            [DataMember(Name = "has_progress")]
            public bool HasProgress { get; set; }

            [DataMember(Name = "has_incomplete_achievements")]
            public bool HasIncompleteAchievements { get; set; }
        }

        public GamePicker(API.Client client)
        {
            this._Games = new();
            this._FilteredGames = new();
            this._LogoLock = new();
            this._LogosAttempting = new();
            this._LogosAttempted = new();
            this._LogoQueue = new();

            this.InitializeComponent();
            this.ApplyModernTheme();
            this.UpdatePickerWindowTitle();

            this._UnlockAllWorker = new BackgroundWorker()
            {
                WorkerReportsProgress = true,
                WorkerSupportsCancellation = true,
            };
            this._UnlockAllWorker.DoWork += this.DoUnlockAllGames;
            this._UnlockAllWorker.ProgressChanged += this.OnUnlockAllGamesProgress;
            this._UnlockAllWorker.RunWorkerCompleted += this.OnUnlockAllGamesCompleted;

            Bitmap blank = new(this._LogoImageList.ImageSize.Width, this._LogoImageList.ImageSize.Height);
            using (var g = Graphics.FromImage(blank))
            {
                g.Clear(Color.DimGray);
            }

            this._LogoImageList.Images.Add("Blank", blank);

            Bitmap blankSmall = new(this._LogoSmallImageList.ImageSize.Width, this._LogoSmallImageList.ImageSize.Height);
            using (var g = Graphics.FromImage(blankSmall))
            {
                g.Clear(Color.DimGray);
            }

            this._LogoSmallImageList.Images.Add("Blank", blankSmall);

            this._SteamClient = client;
            this._SteamWebApiKey = GetSteamWebApiKey();
            this._SteamCommunityCookies = GetSteamCommunityCookies();
            this._GameListView.Sorting = SortOrder.None;
            this._ViewMode = GameViewMode.Grid;
            this._SortMode = GameSortMode.NameAscending;

            this.LoadPickerPreferences();
            this.ApplyViewMode(this._ViewMode, false);
            this.ApplySortMode(this._SortMode, false, false);

            this._AppDataChangedCallback = client.CreateAndRegisterCallback<API.Callbacks.AppDataChanged>();
            this._AppDataChangedCallback.OnRun += this.OnAppDataChanged;

            this.AddGames();
        }

        private void UpdatePickerWindowTitle()
        {
            string version = Application.ProductVersion;
            int metadataSeparator = version?.IndexOf('+') ?? -1;
            if (metadataSeparator > 0)
            {
                version = version.Substring(0, metadataSeparator);
            }
            if (Version.TryParse(version, out Version parsed) == true)
            {
                version = _($"{parsed.Major}.{parsed.Minor}.{parsed.Build}");
            }

            this.Text = _($"Steam Achievement Manager {version} | Pick a game... Any game...");
        }

        private void ApplyModernTheme()
        {
            this.SuspendLayout();

            this.BackColor = Color.FromArgb(242, 242, 247);
            this.ForeColor = Color.FromArgb(24, 24, 28);
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
            this.MinimumSize = new Size(1024, 640);
            if (this.ClientSize.Width < 1024 || this.ClientSize.Height < 640)
            {
                this.ClientSize = new Size(1120, 720);
            }

            this._PickerToolStrip.BackColor = Color.FromArgb(252, 252, 253);
            this._PickerToolStrip.ForeColor = this.ForeColor;
            this._PickerToolStrip.GripStyle = ToolStripGripStyle.Hidden;
            this._PickerToolStrip.Padding = new Padding(10, 6, 10, 6);
            this._PickerToolStrip.AutoSize = false;
            this._PickerToolStrip.Height = 44;
            this._PickerToolStrip.RenderMode = ToolStripRenderMode.System;

            this.StyleToolStripButton(this._RefreshGamesButton);
            this.StyleToolStripButton(this._AddGameButton);
            this.StyleToolStripButton(this._ConfigureAuthButton);
            this.StyleToolStripButton(this._CheckAllButton);
            this.StyleToolStripButton(this._UnlockAllButton);
            this.StyleToolStripButton(this._UnlockSelectedButton);

            this._AddGameTextBox.AutoSize = false;
            this._AddGameTextBox.Size = new Size(96, 28);
            this._AddGameTextBox.BorderStyle = BorderStyle.FixedSingle;
            this._AddGameTextBox.BackColor = Color.White;
            this._AddGameTextBox.ForeColor = this.ForeColor;

            this._FindGamesLabel.ForeColor = Color.FromArgb(110, 110, 116);
            this._FindGamesLabel.Margin = new Padding(6, 0, 0, 0);
            this._FindGamesLabel.Text = "Search";

            this._SearchGameTextBox.AutoSize = false;
            this._SearchGameTextBox.Size = new Size(180, 28);
            this._SearchGameTextBox.BorderStyle = BorderStyle.FixedSingle;
            this._SearchGameTextBox.BackColor = Color.White;
            this._SearchGameTextBox.ForeColor = this.ForeColor;

            this._FilterDropDownButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
            this._FilterDropDownButton.Image = null;
            this._FilterDropDownButton.Text = "Filters";
            this._FilterDropDownButton.ForeColor = this.ForeColor;
            this._FilterDropDownButton.Padding = new Padding(8, 0, 8, 0);
            this._FilterDropDownButton.Margin = new Padding(4, 0, 0, 0);

            foreach (ToolStripItem item in this._PickerToolStrip.Items)
            {
                if (item is ToolStripSeparator separator)
                {
                    separator.Margin = new Padding(6, 0, 6, 0);
                }
            }

            foreach (ToolStripItem item in this._FilterDropDownButton.DropDownItems)
            {
                item.Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
            }

            this._FilterLoadingLabel.ForeColor = Color.FromArgb(110, 110, 116);
            this._FilterLoadingLabel.Alignment = ToolStripItemAlignment.Right;

            this._GameListView.BackColor = Color.White;
            this._GameListView.ForeColor = this.ForeColor;
            this._GameListView.BorderStyle = BorderStyle.None;
            this._GameListView.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);

            this._PickerStatusStrip.SizingGrip = false;
            this._PickerStatusStrip.BackColor = Color.FromArgb(248, 248, 250);
            this._PickerStatusStrip.ForeColor = Color.FromArgb(110, 110, 116);
            this._PickerStatusLabel.ForeColor = Color.FromArgb(110, 110, 116);
            this._DownloadStatusLabel.ForeColor = Color.FromArgb(110, 110, 116);

            this.ResumeLayout(true);
        }

        private void StyleToolStripButton(ToolStripButton button)
        {
            if (button == null)
            {
                return;
            }

            button.Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
            button.ForeColor = this.ForeColor;
            button.TextImageRelation = TextImageRelation.ImageBeforeText;
            button.Padding = new Padding(8, 0, 8, 0);
            button.Margin = new Padding(0, 0, 4, 0);

            if (button.Image == null)
            {
                button.DisplayStyle = ToolStripItemDisplayStyle.Text;
                return;
            }

            button.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
        }

        private void SetPickerStatusTextSafe(string text)
        {
            if (this.IsDisposed == true ||
                this.Disposing == true ||
                this._PickerStatusStrip.IsDisposed == true)
            {
                return;
            }

            if (this._PickerStatusStrip.IsHandleCreated == false)
            {
                return;
            }

            if (this._PickerStatusStrip.InvokeRequired == true)
            {
                try
                {
                    this._PickerStatusStrip.BeginInvoke((MethodInvoker)(() =>
                    {
                        if (this.IsDisposed == true ||
                            this.Disposing == true ||
                            this._PickerStatusStrip.IsDisposed == true)
                        {
                            return;
                        }

                        this._PickerStatusLabel.Text = text ?? "";
                    }));
                }
                catch (InvalidOperationException)
                {
                }

                return;
            }

            this._PickerStatusLabel.Text = text ?? "";
        }

        private static bool TryStartBackgroundWorker(BackgroundWorker worker)
        {
            if (worker == null || worker.IsBusy == true)
            {
                return false;
            }

            try
            {
                worker.RunWorkerAsync();
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static bool TryStartBackgroundWorker(BackgroundWorker worker, object argument)
        {
            if (worker == null || worker.IsBusy == true)
            {
                return false;
            }

            try
            {
                worker.RunWorkerAsync(argument);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private void OnAppDataChanged(APITypes.AppDataChanged param)
        {
            if (param.Result == false)
            {
                return;
            }

            if (this._Games.TryGetValue(param.Id, out var game) == false)
            {
                return;
            }

            game.Name = this._SteamClient.SteamApps001.GetAppData(game.Id, "name");

            this.AddGameToLogoQueue(game);
            this.DownloadNextLogo();
        }

        private void DoDownloadList(object sender, DoWorkEventArgs e)
        {
            this.SetPickerStatusTextSafe("Downloading owned game list...");

            var steamId = this._SteamClient.SteamUser.GetSteamId();
            Dictionary<uint, OwnedGameInfo> mergedOwnedGames = new();
            bool hasNetworkOwnedGames = false;
            bool hasCommunityCookies = (this._SteamCommunityCookies?.Count ?? 0) > 0;

            if (string.IsNullOrWhiteSpace(this._SteamWebApiKey) == false &&
                TryDownloadOwnedGamesFromWebApi(steamId, this._SteamWebApiKey, out var webApiGames) == true)
            {
                this.SetPickerStatusTextSafe("Merging owned games from Steam Web API...");
                MergeOwnedGameInfos(mergedOwnedGames, webApiGames);
                hasNetworkOwnedGames = true;
            }

            // When no Web API list is available, try Steam Community before doing
            // expensive local ownership checks over large app-id sets.
            if (hasNetworkOwnedGames == false)
            {
                if (TryDownloadOwnedGamesFromCommunity(steamId, this._SteamCommunityCookies, out var communityGames) == true)
                {
                    this.SetPickerStatusTextSafe(
                        hasCommunityCookies == true
                            ? "Merging owned games from Steam Community..."
                            : "Merging owned games from Steam Community (public profile)...");
                    MergeOwnedGameInfos(mergedOwnedGames, communityGames);
                    hasNetworkOwnedGames = true;
                }

                // Keep HTML parsing as a fallback path when XML endpoint isn't usable.
                if (hasNetworkOwnedGames == false &&
                    TryDownloadOwnedGamesFromCommunityHtml(steamId, this._SteamCommunityCookies, out var communityHtmlGames) == true)
                {
                    this.SetPickerStatusTextSafe(
                        hasCommunityCookies == true
                            ? "Merging owned games from Steam Community HTML..."
                            : "Merging owned games from Steam Community HTML (public profile)...");
                    MergeOwnedGameInfos(mergedOwnedGames, communityHtmlGames);
                    hasNetworkOwnedGames = true;
                }
            }

            if (mergedOwnedGames.Count > 0)
            {
                this.SetPickerStatusTextSafe("Loading merged owned games...");
                foreach (var game in mergedOwnedGames.Values
                             .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                             .ThenBy(item => item.AppId))
                {
                    var info = this.AddOwnedGame(game.AppId, "normal", game.Name);
                    ApplyOwnedGameProgress(info, game);
                }

                AppendAchievementScanLog(0, $"Merged owned game list contains {mergedOwnedGames.Count} apps.");
                AppendAchievementScanLog(0, "Using owned-game list from network source; skipping supplemental local ownership sweep.");

                return;
            }

            this.SetPickerStatusTextSafe("Downloading game list...");

            if (TryDownloadGamesXml(out var pairs) == false)
            {
                AppendAchievementScanLog(0, "Failed to download games.xml from gib.me. Falling back to ISteamApps/GetAppList.");
                this.SetPickerStatusTextSafe("Primary list unavailable, trying Steam app list...");

                if (TryDownloadAppListFromSteam(out pairs) == false)
                {
                    throw new InvalidOperationException("Failed to download game list from both primary and fallback endpoints.");
                }
            }

            this.SetPickerStatusTextSafe("Checking game ownership...");
            int startCount = this._Games.Count;
            foreach (var kv in pairs)
            {
                this.AddGame(kv.Key, kv.Value);
            }
            int added = this._Games.Count - startCount;
            AppendAchievementScanLog(0, $"Ownership filter added {added} games from {pairs.Count} candidates.");
        }

        private static void MergeOwnedGameInfos(
            IDictionary<uint, OwnedGameInfo> destination,
            IEnumerable<OwnedGameInfo> source)
        {
            if (destination == null || source == null)
            {
                return;
            }

            foreach (var item in source)
            {
                if (item == null || item.AppId == 0)
                {
                    continue;
                }

                if (destination.TryGetValue(item.AppId, out var existing) == false)
                {
                    destination[item.AppId] = new OwnedGameInfo()
                    {
                        AppId = item.AppId,
                        Name = item.Name,
                        HasStatsLink = item.HasStatsLink,
                        AchievementUnlocked = item.AchievementUnlocked,
                        AchievementTotal = item.AchievementTotal,
                    };
                    continue;
                }

                if (string.IsNullOrWhiteSpace(existing.Name) == true &&
                    string.IsNullOrWhiteSpace(item.Name) == false)
                {
                    existing.Name = item.Name;
                }

                if (item.HasStatsLink == true)
                {
                    existing.HasStatsLink = true;
                }

                if (item.HasAchievementProgress == false)
                {
                    continue;
                }

                if (existing.HasAchievementProgress == false ||
                    item.AchievementTotal > existing.AchievementTotal ||
                    (item.AchievementTotal == existing.AchievementTotal &&
                     item.AchievementUnlocked > existing.AchievementUnlocked))
                {
                    existing.AchievementUnlocked = item.AchievementUnlocked;
                    existing.AchievementTotal = item.AchievementTotal;
                }
            }
        }

        private static bool TryDownloadGamesXml(out List<KeyValuePair<uint, string>> pairs)
        {
            pairs = null;
            const string url = "https://gib.me/sam/games.xml";

            try
            {
                AppendAchievementScanLog(0, $"HTTP GET {url}");
                byte[] bytes;
                using (WebClient downloader = new())
                {
                    bytes = downloader.DownloadData(new Uri(url));
                }

                List<KeyValuePair<uint, string>> items = new();
                using (MemoryStream stream = new(bytes, false))
                {
                    XPathDocument document = new(stream);
                    var navigator = document.CreateNavigator();
                    var nodes = navigator.Select("/games/game");
                    while (nodes.MoveNext() == true)
                    {
                        string type = nodes.Current.GetAttribute("type", "");
                        if (string.IsNullOrEmpty(type) == true)
                        {
                            type = "normal";
                        }
                        items.Add(new((uint)nodes.Current.ValueAsLong, type));
                    }
                }

                if (items.Count == 0)
                {
                    return false;
                }

                AppendAchievementScanLog(0, $"games.xml returned {items.Count} ids.");
                pairs = items;
                return true;
            }
            catch (WebException ex)
            {
                AppendAchievementScanLog(0, $"games.xml download failed: {ex.Message}");
                return false;
            }
            catch (XPathException ex)
            {
                AppendAchievementScanLog(0, $"games.xml parse failed: {ex.Message}");
                return false;
            }
            catch (InvalidOperationException ex)
            {
                AppendAchievementScanLog(0, $"games.xml read failed: {ex.Message}");
                return false;
            }
        }

        private static bool TryDownloadAppListFromSteam(out List<KeyValuePair<uint, string>> pairs)
        {
            pairs = null;
            const string url = "https://api.steampowered.com/ISteamApps/GetAppList/v2/";

            try
            {
                AppendAchievementScanLog(0, $"HTTP GET {url}");
                byte[] bytes;
                using (WebClient downloader = new())
                {
                    bytes = downloader.DownloadData(new Uri(url));
                }

                using MemoryStream stream = new(bytes, false);
                DataContractJsonSerializer serializer = new(typeof(AppListResponse));
                if (serializer.ReadObject(stream) is not AppListResponse payload ||
                    payload.AppList?.Apps == null ||
                    payload.AppList.Apps.Length == 0)
                {
                    return false;
                }

                pairs = payload.AppList.Apps
                    .Where(app => app != null && app.AppId > 0)
                    .Select(app => new KeyValuePair<uint, string>(app.AppId, "normal"))
                    .ToList();
                AppendAchievementScanLog(0, $"GetAppList returned {pairs.Count} ids.");
                return pairs.Count > 0;
            }
            catch (WebException ex)
            {
                AppendAchievementScanLog(0, $"GetAppList download failed: {ex.Message}");
                return false;
            }
            catch (SerializationException ex)
            {
                AppendAchievementScanLog(0, $"GetAppList JSON parse failed: {ex.Message}");
                return false;
            }
            catch (InvalidCastException ex)
            {
                AppendAchievementScanLog(0, $"GetAppList response cast failed: {ex.Message}");
                return false;
            }
        }

        private static bool TryDownloadOwnedGamesFromWebApi(
            ulong steamId,
            string steamWebApiKey,
            out List<OwnedGameInfo> games)
        {
            games = null;
            if (string.IsNullOrWhiteSpace(steamWebApiKey) == true)
            {
                return false;
            }

            string url =
                $"https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/?key={Uri.EscapeDataString(steamWebApiKey)}&steamid={steamId}&include_appinfo=1&include_played_free_games=1";

            try
            {
                AppendAchievementScanLog(0, $"HTTP GET {url}");

                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Timeout = 12000;
                request.ReadWriteTimeout = 12000;
                request.UserAgent = "SteamAchievementManager";
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

                using var response = (HttpWebResponse)request.GetResponse();
                using var stream = response.GetResponseStream();
                if (stream == null)
                {
                    AppendAchievementScanLog(0, "GetOwnedGames returned no content.");
                    return false;
                }

                DataContractJsonSerializer serializer = new(typeof(OwnedGamesResponse));
                if (serializer.ReadObject(stream) is not OwnedGamesResponse payload ||
                    payload.Response?.Games == null ||
                    payload.Response.Games.Length == 0)
                {
                    AppendAchievementScanLog(0, "GetOwnedGames returned empty payload.");
                    return false;
                }

                games = payload.Response.Games
                    .Where(entry => entry != null && entry.AppId > 0)
                    .Select(entry => new OwnedGameInfo()
                    {
                        AppId = entry.AppId,
                        Name = entry.Name,
                        HasStatsLink = true,
                    })
                    .ToList();

                AppendAchievementScanLog(0, $"GetOwnedGames returned {games.Count} owned apps.");
                return games.Count > 0;
            }
            catch (WebException ex)
            {
                if (TryReadWebExceptionBody(ex, out var body) == true)
                {
                    AppendAchievementScanLog(0, $"GetOwnedGames request failed: {body}");
                }
                else
                {
                    AppendAchievementScanLog(0, $"GetOwnedGames request failed: {ex.Message}");
                }
                return false;
            }
            catch (SerializationException ex)
            {
                AppendAchievementScanLog(0, $"GetOwnedGames JSON parse failed: {ex.Message}");
                return false;
            }
            catch (InvalidCastException ex)
            {
                AppendAchievementScanLog(0, $"GetOwnedGames response cast failed: {ex.Message}");
                return false;
            }
        }

        private static bool TryDownloadOwnedGamesFromCommunity(
            ulong steamId,
            IDictionary<string, string> cookies,
            out List<OwnedGameInfo> games)
        {
            games = null;
            string url = $"https://steamcommunity.com/profiles/{steamId}/games/?tab=all&xml=1";
            try
            {
                AppendAchievementScanLog(0, $"HTTP GET {url}");

                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Timeout = 12000;
                request.ReadWriteTimeout = 12000;
                request.UserAgent = "SteamAchievementManager";
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                if (cookies != null && cookies.Count > 0)
                {
                    request.CookieContainer = CreateSteamCommunityCookieContainer(cookies);
                }

                using var response = (HttpWebResponse)request.GetResponse();
                using var stream = response.GetResponseStream();
                if (stream == null)
                {
                    AppendAchievementScanLog(0, "Community owned games endpoint returned no content.");
                    return false;
                }

                string payload;
                using (StreamReader reader = new(stream, Encoding.UTF8))
                {
                    payload = reader.ReadToEnd();
                }

                if (string.IsNullOrWhiteSpace(payload) == true)
                {
                    AppendAchievementScanLog(0, "Community owned games endpoint returned empty content.");
                    return false;
                }

                string trimmed = payload.TrimStart();
                if (trimmed.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) == true ||
                    trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase) == true)
                {
                    AppendAchievementScanLog(0, "Community owned games endpoint returned HTML (private profile, authentication required, or invalid cookies).");
                    return false;
                }

                using StringReader stringReader = new(payload);
                XPathDocument document = new(stringReader);
                var navigator = document.CreateNavigator();
                var nodes = navigator.Select("/gamesList/games/game");
                if (nodes.Count == 0)
                {
                    nodes = navigator.Select("//games/game");
                }

                List<OwnedGameInfo> items = new();
                while (nodes.MoveNext() == true)
                {
                    string appIdText = nodes.Current.SelectSingleNode("appID")?.Value ??
                                       nodes.Current.SelectSingleNode("appid")?.Value;
                    if (uint.TryParse(appIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint appId) == false ||
                        appId == 0)
                    {
                        continue;
                    }

                    string name = nodes.Current.SelectSingleNode("name")?.Value;
                    string statsLink = nodes.Current.SelectSingleNode("statsLink")?.Value ??
                                       nodes.Current.SelectSingleNode("statslink")?.Value;
                    items.Add(new OwnedGameInfo()
                    {
                        AppId = appId,
                        Name = name,
                        HasStatsLink = string.IsNullOrWhiteSpace(statsLink) == false,
                    });
                }

                games = items
                    .GroupBy(item => item.AppId)
                    .Select(group => group.First())
                    .ToList();

                AppendAchievementScanLog(0, $"Community games XML returned {games.Count} owned apps.");
                return games.Count > 0;
            }
            catch (WebException ex)
            {
                if (TryReadWebExceptionBody(ex, out var body) == true)
                {
                    AppendAchievementScanLog(0, $"Community owned games request failed: {body}");
                }
                else
                {
                    AppendAchievementScanLog(0, $"Community owned games request failed: {ex.Message}");
                }
                return false;
            }
            catch (XPathException ex)
            {
                AppendAchievementScanLog(0, $"Community owned games XML parse failed: {ex.Message}");
                return false;
            }
            catch (XmlException ex)
            {
                AppendAchievementScanLog(0, $"Community owned games XML parse failed: {ex.Message}");
                return false;
            }
        }

        private static bool TryDownloadOwnedGamesFromCommunityHtml(
            ulong steamId,
            IDictionary<string, string> cookies,
            out List<OwnedGameInfo> games)
        {
            games = null;
            string url = $"https://steamcommunity.com/profiles/{steamId}/games?tab=all&sort=achievements";
            try
            {
                AppendAchievementScanLog(0, $"HTTP GET {url}");

                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Timeout = 12000;
                request.ReadWriteTimeout = 12000;
                request.UserAgent = "SteamAchievementManager";
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                if (cookies != null && cookies.Count > 0)
                {
                    request.CookieContainer = CreateSteamCommunityCookieContainer(cookies);
                }

                using var response = (HttpWebResponse)request.GetResponse();
                using var stream = response.GetResponseStream();
                if (stream == null)
                {
                    AppendAchievementScanLog(0, "Community games HTML endpoint returned no content.");
                    return false;
                }

                string html;
                using (StreamReader reader = new(stream, Encoding.UTF8))
                {
                    html = reader.ReadToEnd();
                }

                if (string.IsNullOrWhiteSpace(html) == true)
                {
                    AppendAchievementScanLog(0, "Community games HTML endpoint returned empty content.");
                    return false;
                }

                if (html.IndexOf("<title>Sign In", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    AppendAchievementScanLog(0, "Community games HTML endpoint returned sign-in page (private profile or invalid/expired cookies).");
                    return false;
                }

                if (TryExtractGamesFromCommunityHtml(html, out games) == false)
                {
                    AppendAchievementScanLog(0, "Community games HTML parser could not extract rgGames payload.");
                    return false;
                }

                AppendAchievementScanLog(0, $"Community games HTML returned {games.Count} owned apps.");
                return games.Count > 0;
            }
            catch (WebException ex)
            {
                if (TryReadWebExceptionBody(ex, out var body) == true)
                {
                    AppendAchievementScanLog(0, $"Community games HTML request failed: {body}");
                }
                else
                {
                    AppendAchievementScanLog(0, $"Community games HTML request failed: {ex.Message}");
                }
                return false;
            }
        }

        private static bool TryExtractGamesFromCommunityHtml(string html, out List<OwnedGameInfo> games)
        {
            games = null;

            if (TryExtractJsonArrayAfterMarker(html, "var rgGames =", out var json) == false &&
                TryExtractJsonArrayAfterMarker(html, "rgGames =", out json) == false &&
                TryExtractJsonArrayAfterMarker(html, "g_rgGames =", out json) == false)
            {
                return false;
            }

            JavaScriptSerializer serializer = new()
            {
                MaxJsonLength = int.MaxValue,
            };

            object parsed;
            try
            {
                parsed = serializer.DeserializeObject(json);
            }
            catch (ArgumentException)
            {
                return false;
            }

            if (parsed is not object[] entries || entries.Length == 0)
            {
                return false;
            }

            List<OwnedGameInfo> items = new();
            foreach (var entry in entries)
            {
                if (entry is not Dictionary<string, object> map)
                {
                    continue;
                }

                if (TryReadUInt(map, "appid", out uint appId) == false || appId == 0)
                {
                    continue;
                }

                var item = new OwnedGameInfo()
                {
                    AppId = appId,
                    Name = ReadString(map, "name"),
                    HasStatsLink = HasAchievementsLink(map),
                };

                if (TryReadAchievementCounts(map, out int unlocked, out int total) == true)
                {
                    item.AchievementUnlocked = unlocked;
                    item.AchievementTotal = total;
                }
                else if (TryParseAchievementCountsFromText(ReadString(map, "achievements"), out unlocked, out total) == true)
                {
                    item.AchievementUnlocked = unlocked;
                    item.AchievementTotal = total;
                }

                if (item.HasAchievementProgress == false &&
                    item.HasStatsLink == false)
                {
                    item.AchievementUnlocked = 0;
                    item.AchievementTotal = 0;
                }

                items.Add(item);
            }

            games = items
                .GroupBy(item => item.AppId)
                .Select(group => group.First())
                .ToList();
            return games.Count > 0;
        }

        private static bool TryExtractJsonArrayAfterMarker(string input, string marker, out string json)
        {
            json = null;
            if (string.IsNullOrEmpty(input) == true || string.IsNullOrEmpty(marker) == true)
            {
                return false;
            }

            int markerIndex = input.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
            {
                return false;
            }

            int arrayStart = input.IndexOf('[', markerIndex);
            if (arrayStart < 0)
            {
                return false;
            }

            int depth = 0;
            bool inString = false;
            bool escaping = false;
            for (int i = arrayStart; i < input.Length; i++)
            {
                char ch = input[i];

                if (inString == true)
                {
                    if (escaping == true)
                    {
                        escaping = false;
                    }
                    else if (ch == '\\')
                    {
                        escaping = true;
                    }
                    else if (ch == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (ch == '"')
                {
                    inString = true;
                    continue;
                }

                if (ch == '[')
                {
                    depth++;
                }
                else if (ch == ']')
                {
                    depth--;
                    if (depth == 0)
                    {
                        json = input.Substring(arrayStart, i - arrayStart + 1);
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool HasAchievementsLink(IDictionary<string, object> map)
        {
            if (map.TryGetValue("availStatLinks", out var value) == false ||
                value is not Dictionary<string, object> links)
            {
                return false;
            }

            return links.ContainsKey("achievements");
        }

        private static bool TryReadAchievementCounts(
            IDictionary<string, object> map,
            out int unlocked,
            out int total)
        {
            unlocked = -1;
            total = -1;

            if (TryReadInt(map, "achievements_unlocked", out unlocked) == true &&
                TryReadInt(map, "achievements_total", out total) == true &&
                unlocked >= 0 &&
                total >= 0)
            {
                return true;
            }

            if (TryReadInt(map, "unlocked_achievements", out unlocked) == true &&
                TryReadInt(map, "total_achievements", out total) == true &&
                unlocked >= 0 &&
                total >= 0)
            {
                return true;
            }

            return false;
        }

        private static bool TryReadInt(IDictionary<string, object> map, string key, out int value)
        {
            value = -1;
            if (map.TryGetValue(key, out var raw) == false || raw == null)
            {
                return false;
            }

            switch (raw)
            {
                case int i:
                    value = i;
                    return true;
                case long l:
                    if (l < int.MinValue || l > int.MaxValue)
                    {
                        return false;
                    }
                    value = (int)l;
                    return true;
                case double d:
                    if (d < int.MinValue || d > int.MaxValue)
                    {
                        return false;
                    }
                    value = (int)d;
                    return true;
                case string s:
                    return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
                default:
                    return false;
            }
        }

        private static bool TryReadUInt(IDictionary<string, object> map, string key, out uint value)
        {
            value = 0;
            if (map.TryGetValue(key, out var raw) == false || raw == null)
            {
                return false;
            }

            switch (raw)
            {
                case uint u:
                    value = u;
                    return true;
                case int i when i >= 0:
                    value = (uint)i;
                    return true;
                case long l when l >= 0 && l <= uint.MaxValue:
                    value = (uint)l;
                    return true;
                case double d when d >= 0 && d <= uint.MaxValue:
                    value = (uint)d;
                    return true;
                case string s:
                    return uint.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
                default:
                    return false;
            }
        }

        private static string ReadString(IDictionary<string, object> map, string key)
        {
            if (map.TryGetValue(key, out var raw) == false || raw == null)
            {
                return null;
            }

            return WebUtility.HtmlDecode(raw.ToString());
        }

        private static bool TryParseAchievementCountsFromText(
            string text,
            out int unlocked,
            out int total)
        {
            unlocked = -1;
            total = -1;
            if (string.IsNullOrWhiteSpace(text) == true)
            {
                return false;
            }

            Match slashMatch = Regex.Match(text, @"(\d+)\s*/\s*(\d+)");
            if (slashMatch.Success == true &&
                int.TryParse(slashMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out unlocked) == true &&
                int.TryParse(slashMatch.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out total) == true)
            {
                return true;
            }

            Match ofMatch = Regex.Match(text, @"(\d+)\s+of\s+(\d+)", RegexOptions.IgnoreCase);
            if (ofMatch.Success == true &&
                int.TryParse(ofMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out unlocked) == true &&
                int.TryParse(ofMatch.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out total) == true)
            {
                return true;
            }

            return false;
        }

        private void OnDownloadList(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Error != null || e.Cancelled == true)
            {
                this.AddDefaultGames();
                MessageBox.Show(e.Error.ToString(), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            if (this._ReloadGamesPending == true)
            {
                this._ReloadGamesPending = false;
                this.AddGames();
                return;
            }

            this.RefreshGames();
            this._RefreshGamesButton.Enabled = true;
            this.StartAchievementScan();
            this.DownloadNextLogo();
            this.UpdateLoadingIndicator();
        }

        private void RefreshGames()
        {
            HashSet<uint> selectedGameIds = this.GetSelectedVisibleGameIds();

            var nameSearch = this._SearchGameTextBox.Text.Length > 0
                ? this._SearchGameTextBox.Text
                : null;

            var wantNormals = this._FilterGamesMenuItem.Checked == true;
            var wantDemos = this._FilterDemosMenuItem.Checked == true;
            var wantMods = this._FilterModsMenuItem.Checked == true;
            var wantJunk = this._FilterJunkMenuItem.Checked == true;
            var wantIncompleteAchievements = this._FilterIncompleteAchievementsMenuItem.Checked == true;

            this._FilteredGames.Clear();
            foreach (var info in this.GetSortedGames())
            {
                if (nameSearch != null &&
                    info.Name.IndexOf(nameSearch, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                bool wanted = info.Type switch
                {
                    "normal" => wantNormals,
                    "demo" => wantDemos,
                    "mod" => wantMods,
                    "junk" => wantJunk,
                    _ => true,
                };
                if (wanted == false)
                {
                    continue;
                }

                if (wantIncompleteAchievements == true &&
                    info.HasIncompleteAchievements == false)
                {
                    continue;
                }

                this._FilteredGames.Add(info);
            }

            this._GameListView.VirtualListSize = this._FilteredGames.Count;
            this.RestoreVisibleSelection(selectedGameIds);
            this.ResizeListColumn();
            this.UpdatePickerStatus();
            this.UpdateUnlockSelectedButtonVisibility();
        }

        private HashSet<uint> GetSelectedVisibleGameIds()
        {
            HashSet<uint> selectedGameIds = new();
            foreach (int selectedIndex in this._GameListView.SelectedIndices)
            {
                if (selectedIndex < 0 || selectedIndex >= this._FilteredGames.Count)
                {
                    continue;
                }

                selectedGameIds.Add(this._FilteredGames[selectedIndex].Id);
            }

            return selectedGameIds;
        }

        private List<uint> GetSelectedVisibleGameIdsInDisplayOrder()
        {
            List<uint> selectedGameIds = new();
            HashSet<uint> seen = new();
            foreach (int selectedIndex in this._GameListView.SelectedIndices)
            {
                if (selectedIndex < 0 || selectedIndex >= this._FilteredGames.Count)
                {
                    continue;
                }

                uint gameId = this._FilteredGames[selectedIndex].Id;
                if (seen.Add(gameId) == false)
                {
                    continue;
                }

                selectedGameIds.Add(gameId);
            }

            return selectedGameIds;
        }

        private void RestoreVisibleSelection(HashSet<uint> selectedGameIds)
        {
            this._GameListView.SelectedIndices.Clear();
            if (selectedGameIds == null || selectedGameIds.Count == 0)
            {
                return;
            }

            for (int i = 0; i < this._FilteredGames.Count; i++)
            {
                if (selectedGameIds.Contains(this._FilteredGames[i].Id) == false)
                {
                    continue;
                }

                this._GameListView.SelectedIndices.Add(i);
            }
        }

        private void UpdateUnlockSelectedButtonVisibility()
        {
            if (this._UnlockSelectedButton == null)
            {
                return;
            }

            int selectedCount = this.GetSelectedVisibleGameIds().Count;
            this._UnlockSelectedButton.Visible = selectedCount > 0;
            this._UnlockSelectedButton.Enabled = selectedCount > 0;
            this._UnlockSelectedButton.Text = $"Unlock Selected ({selectedCount})";
        }

        private void UpdatePickerStatus()
        {
            string message = $"Displaying {this._FilteredGames.Count} games. Total {this._Games.Count} games.";
            if (this._UnlockAllWorker.IsBusy == true)
            {
                message += $" Unlocking achievements {this._UnlockAllCompleted}/{this._UnlockAllTotal}...";
            }
            else if (this._AchievementWorker.IsBusy == true)
            {
                string mode = this.GetActiveScanModeLabel();
                message += $" Checking achievements ({mode}) {this._AchievementScanCompleted}/{this._AchievementScanTotal}...";
            }
            else if (this._FilterIncompleteAchievementsMenuItem.Checked == true &&
                     this._Games.Values.Any(info => info.HasAchievementProgress) == false)
            {
                message += string.IsNullOrEmpty(this._SteamWebApiKey) == false
                    ? " Could not read achievement data from Steam Web API."
                    : " Could not read achievement data from Steam Community.";
            }

            this._PickerStatusLabel.Text = message;
            this.UpdateLoadingIndicator();
        }

        private string GetActiveScanModeLabel()
        {
            if (this._CurrentAchievementScanMode == AchievementScanMode.LocalCheckAll)
            {
                return "local-check-all";
            }

            return string.IsNullOrEmpty(this._SteamWebApiKey) == false
                ? "web-api"
                : (this._SteamCommunityCookies?.Count ?? 0) > 0
                    ? "community-auth"
                    : "community";
        }

        private void UpdateLoadingIndicator()
        {
            bool loading = this._ListWorker.IsBusy == true ||
                           this._AchievementWorker.IsBusy == true ||
                           this._UnlockAllWorker.IsBusy == true;
            if (loading == false)
            {
                this._LoadingSpinnerTimer.Enabled = false;
                this._FilterLoadingLabel.Visible = false;
                return;
            }

            this._FilterLoadingLabel.Visible = true;
            if (this._LoadingSpinnerTimer.Enabled == false)
            {
                this._LoadingSpinnerFrame = 0;
                this._FilterLoadingLabel.Text = _($"[{_LoadingSpinnerFrames[this._LoadingSpinnerFrame]}]");
                this._LoadingSpinnerTimer.Enabled = true;
            }
        }

        private void OnLoadingSpinnerTick(object sender, EventArgs e)
        {
            bool loading = this._ListWorker.IsBusy == true ||
                           this._AchievementWorker.IsBusy == true ||
                           this._UnlockAllWorker.IsBusy == true;
            if (loading == false)
            {
                this._LoadingSpinnerTimer.Enabled = false;
                this._FilterLoadingLabel.Visible = false;
                return;
            }

            this._LoadingSpinnerFrame++;
            if (this._LoadingSpinnerFrame >= _LoadingSpinnerFrames.Length)
            {
                this._LoadingSpinnerFrame = 0;
            }

            this._FilterLoadingLabel.Text = _($"[{_LoadingSpinnerFrames[this._LoadingSpinnerFrame]}]");
        }

        private void OnGameListViewRetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            if (e.ItemIndex < 0 || e.ItemIndex >= this._FilteredGames.Count)
            {
                e.Item = new ListViewItem();
                return;
            }

            var info = this._FilteredGames[e.ItemIndex];
            e.Item = new()
            {
                Text = info.DisplayName,
                ImageIndex = info.ImageIndex,
            };

            if (info.ImageIndex <= 0)
            {
                this.AddGameToLogoQueue(info);
                this.DownloadNextLogo();
            }
        }

        private void OnGameListViewSearchForVirtualItem(object sender, SearchForVirtualItemEventArgs e)
        {
            if (e.Direction != SearchDirectionHint.Down || e.IsTextSearch == false)
            {
                return;
            }

            var count = this._FilteredGames.Count;
            if (count < 2)
            {
                return;
            }

            var text = e.Text;
            int startIndex = e.StartIndex;

            Predicate<GameInfo> predicate;
            /*if (e.IsPrefixSearch == true)*/
            {
                predicate = gi => gi.Name != null && gi.Name.StartsWith(text, StringComparison.CurrentCultureIgnoreCase);
            }
            /*else
            {
                predicate = gi => gi.Name != null && string.Compare(gi.Name, text, StringComparison.CurrentCultureIgnoreCase) == 0;
            }*/

            int index;
            if (e.StartIndex >= count)
            {
                // starting from the last item in the list
                index = this._FilteredGames.FindIndex(0, startIndex - 1, predicate);
            }
            else if (startIndex <= 0)
            {
                // starting from the first item in the list
                index = this._FilteredGames.FindIndex(0, count, predicate);
            }
            else
            {
                index = this._FilteredGames.FindIndex(startIndex, count - startIndex, predicate);
                if (index < 0)
                {
                    index = this._FilteredGames.FindIndex(0, startIndex - 1, predicate);
                }
            }

            e.Index = index < 0 ? -1 : index;
        }

        private static string GetSteamWebApiKey()
        {
            string key = Environment.GetEnvironmentVariable("SAM_STEAM_WEB_API_KEY");
            if (string.IsNullOrWhiteSpace(key) == false)
            {
                return key.Trim();
            }

            var path = Path.Combine(Application.StartupPath, "steam-webapi-key.txt");
            if (File.Exists(path) == false)
            {
                return null;
            }

            try
            {
                key = File.ReadAllText(path).Trim();
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }

            return string.IsNullOrWhiteSpace(key) == false ? key : null;
        }

        private static Dictionary<string, string> GetSteamCommunityCookies()
        {
            Dictionary<string, string> cookies = new(StringComparer.OrdinalIgnoreCase);

            AddSteamCommunityCookieFromEnvironment(cookies, "sessionid", "SAM_STEAM_SESSIONID");
            AddSteamCommunityCookieFromEnvironment(cookies, "steamLoginSecure", "SAM_STEAM_LOGIN_SECURE");
            AddSteamCommunityCookieFromEnvironment(cookies, "steamParental", "SAM_STEAM_PARENTAL");
            AddSteamCommunityCookieFromEnvironment(cookies, "steamMachineAuth", "SAM_STEAM_MACHINE_AUTH");

            var path = Path.Combine(Application.StartupPath, "steam-community-cookies.txt");
            if (File.Exists(path) == true)
            {
                try
                {
                    foreach (var rawLine in File.ReadAllLines(path, Encoding.UTF8))
                    {
                        string line = rawLine?.Trim();
                        if (string.IsNullOrEmpty(line) == true ||
                            line.StartsWith("#", StringComparison.Ordinal) == true ||
                            line.StartsWith("//", StringComparison.Ordinal) == true)
                        {
                            continue;
                        }

                        int equalsIndex = line.IndexOf('=');
                        if (equalsIndex <= 0 || equalsIndex >= line.Length - 1)
                        {
                            continue;
                        }

                        string name = line.Substring(0, equalsIndex).Trim();
                        string value = line.Substring(equalsIndex + 1).Trim();
                        if (string.IsNullOrWhiteSpace(name) == true ||
                            string.IsNullOrWhiteSpace(value) == true)
                        {
                            continue;
                        }

                        cookies[name] = value;
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            return cookies.Count > 0 ? cookies : null;
        }

        private void OnConfigureAuth(object sender, EventArgs e)
        {
            using SteamCommunityAuthDialog dialog = new(this._SteamCommunityCookies);
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            Dictionary<string, string> cookies = new(StringComparer.OrdinalIgnoreCase)
            {
                ["sessionid"] = dialog.SessionId.Trim(),
                ["steamLoginSecure"] = dialog.SteamLoginSecure.Trim(),
            };

            if (string.IsNullOrWhiteSpace(dialog.SteamParental) == false)
            {
                cookies["steamParental"] = dialog.SteamParental.Trim();
            }

            if (string.IsNullOrWhiteSpace(dialog.SteamMachineAuth) == false)
            {
                cookies["steamMachineAuth"] = dialog.SteamMachineAuth.Trim();
            }

            this._SteamCommunityCookies = cookies;

            if (dialog.SaveToFile == true)
            {
                SaveSteamCommunityCookies(this._SteamCommunityCookies);
            }

            this.AddGames();
        }

        private static void SaveSteamCommunityCookies(IDictionary<string, string> cookies)
        {
            if (cookies == null || cookies.Count == 0)
            {
                return;
            }

            try
            {
                StringBuilder builder = new();
                foreach (var pair in cookies)
                {
                    if (string.IsNullOrWhiteSpace(pair.Key) == true ||
                        string.IsNullOrWhiteSpace(pair.Value) == true)
                    {
                        continue;
                    }

                    builder.Append(pair.Key.Trim());
                    builder.Append('=');
                    builder.AppendLine(pair.Value.Trim());
                }

                if (builder.Length == 0)
                {
                    return;
                }

                var path = Path.Combine(Application.StartupPath, "steam-community-cookies.txt");
                File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static void AddSteamCommunityCookieFromEnvironment(
            IDictionary<string, string> cookies,
            string cookieName,
            string variableName)
        {
            string value = Environment.GetEnvironmentVariable(variableName);
            if (string.IsNullOrWhiteSpace(value) == true)
            {
                return;
            }

            cookies[cookieName] = value.Trim();
        }

        private static string GetPickerPreferencesPath()
        {
            return Path.Combine(Application.StartupPath, "sam-picker-preferences.json");
        }

        private static string GetAchievementStatusDatabasePath()
        {
            return Path.Combine(Application.StartupPath, "data", "games", "achievements", "status.json");
        }

        private void SaveAchievementStatusDatabase()
        {
            try
            {
                string path = GetAchievementStatusDatabasePath();
                string directory = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(directory) == false)
                {
                    Directory.CreateDirectory(directory);
                }

                AchievementStatusEntry[] entries = this._Games.Values
                    .OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase)
                    .Select(game => new AchievementStatusEntry()
                    {
                        AppId = game.Id,
                        Name = game.Name,
                        Type = game.Type,
                        AchievementUnlocked = game.AchievementUnlocked,
                        AchievementTotal = game.AchievementTotal,
                        HasProgress = game.HasAchievementProgress,
                        HasIncompleteAchievements = game.HasIncompleteAchievements,
                    })
                    .ToArray();

                AchievementStatusDatabase payload = new()
                {
                    GeneratedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    SteamId = this._SteamClient.SteamUser.GetSteamId(),
                    ScanMode = this.GetActiveScanModeLabel(),
                    Games = entries,
                };

                using FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.Read);
                DataContractJsonSerializer serializer = new(typeof(AchievementStatusDatabase));
                serializer.WriteObject(stream, payload);

                AppendAchievementScanLog(0, $"Saved achievement database to {path}.");
            }
            catch (IOException ex)
            {
                AppendAchievementScanLog(0, $"Failed to save achievement database: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                AppendAchievementScanLog(0, $"Failed to save achievement database: {ex.Message}");
            }
            catch (SerializationException ex)
            {
                AppendAchievementScanLog(0, $"Failed to serialize achievement database: {ex.Message}");
            }
        }

        private void LoadPickerPreferences()
        {
            string path = GetPickerPreferencesPath();
            if (File.Exists(path) == false)
            {
                return;
            }

            try
            {
                using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                DataContractJsonSerializer serializer = new(typeof(PickerPreferences));
                if (serializer.ReadObject(stream) is not PickerPreferences preferences)
                {
                    return;
                }

                if (Enum.TryParse(preferences.ViewMode, true, out GameViewMode viewMode) == true)
                {
                    this._ViewMode = viewMode;
                }

                if (Enum.TryParse(preferences.SortMode, true, out GameSortMode sortMode) == true)
                {
                    this._SortMode = sortMode;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (SerializationException)
            {
            }
            catch (InvalidCastException)
            {
            }
        }

        private void SavePickerPreferences()
        {
            try
            {
                PickerPreferences preferences = new()
                {
                    ViewMode = this._ViewMode.ToString(),
                    SortMode = this._SortMode.ToString(),
                };

                using FileStream stream = new(GetPickerPreferencesPath(), FileMode.Create, FileAccess.Write, FileShare.Read);
                DataContractJsonSerializer serializer = new(typeof(PickerPreferences));
                serializer.WriteObject(stream, preferences);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (SerializationException)
            {
            }
        }

        private void ApplyViewMode(GameViewMode mode, bool persist)
        {
            this._ViewMode = mode;
            this._ViewGridMenuItem.Checked = mode == GameViewMode.Grid;
            this._ViewListMenuItem.Checked = mode == GameViewMode.List;

            this._GameListView.BeginUpdate();
            if (mode == GameViewMode.Grid)
            {
                this._GameListView.OwnerDraw = true;
                // WinForms does not allow Tile view while VirtualMode is enabled.
                this._GameListView.View = View.LargeIcon;
                this._GameListView.HeaderStyle = ColumnHeaderStyle.Clickable;
                this._GameListView.TileSize = new Size(184, 69);
                this._GameListView.FullRowSelect = false;
            }
            else
            {
                this._GameListView.OwnerDraw = false;
                this._GameListView.View = View.Details;
                this._GameListView.HeaderStyle = ColumnHeaderStyle.None;
                this._GameListView.FullRowSelect = true;
                this.EnsureListColumn();
                this.ResizeListColumn();
            }
            this._GameListView.EndUpdate();

            if (persist == true)
            {
                this.SavePickerPreferences();
            }

            this._GameListView.Invalidate();
        }

        private void ApplySortMode(GameSortMode mode, bool persist, bool refresh)
        {
            this._SortMode = mode;

            this._SortNameAscendingMenuItem.Checked = mode == GameSortMode.NameAscending;
            this._SortNameDescendingMenuItem.Checked = mode == GameSortMode.NameDescending;
            this._SortAchievementAscendingMenuItem.Checked = mode == GameSortMode.AchievementAscending;
            this._SortAchievementDescendingMenuItem.Checked = mode == GameSortMode.AchievementDescending;

            if (persist == true)
            {
                this.SavePickerPreferences();
            }

            if (refresh == true)
            {
                this.RefreshGames();
            }
        }

        private void EnsureListColumn()
        {
            if (this._GameListView.Columns.Count > 0)
            {
                return;
            }

            this._GameListView.Columns.Add("Game", 120);
        }

        private void ResizeListColumn()
        {
            if (this._ViewMode != GameViewMode.List ||
                this._GameListView.Columns.Count == 0)
            {
                return;
            }

            int width = this._GameListView.ClientSize.Width - 4;
            this._GameListView.Columns[0].Width = width > 80 ? width : 80;
        }

        private IEnumerable<GameInfo> GetSortedGames()
        {
            StringComparer comparer = StringComparer.CurrentCultureIgnoreCase;
            return this._SortMode switch
            {
                GameSortMode.NameDescending =>
                    this._Games.Values
                        .OrderByDescending(gi => gi.Name, comparer)
                        .ThenBy(gi => gi.Id),
                GameSortMode.AchievementAscending =>
                    this._Games.Values
                        .OrderBy(gi => GetAchievementSortValue(gi, ascending: true))
                        .ThenBy(gi => gi.Name, comparer)
                        .ThenBy(gi => gi.Id),
                GameSortMode.AchievementDescending =>
                    this._Games.Values
                        .OrderByDescending(gi => GetAchievementSortValue(gi, ascending: false))
                        .ThenBy(gi => gi.Name, comparer)
                        .ThenBy(gi => gi.Id),
                _ =>
                    this._Games.Values
                        .OrderBy(gi => gi.Name, comparer)
                        .ThenBy(gi => gi.Id),
            };
        }

        private static double GetAchievementSortValue(GameInfo info, bool ascending)
        {
            if (info.HasAchievementProgress == false || info.AchievementTotal <= 0)
            {
                return ascending == true ? 2.0 : -1.0;
            }

            return (double)info.AchievementUnlocked / info.AchievementTotal;
        }

        private void StartAchievementScan()
        {
            if (this._UnlockAllWorker.IsBusy == true)
            {
                this._AchievementScanPending = true;
                return;
            }

            if (this._AchievementWorker.IsBusy == true)
            {
                this._AchievementScanPending = true;
                return;
            }

            var gameIds = this._Games.Values
                .Where(info => info.HasAchievementProgress == false)
                .Select(info => info.Id)
                .ToList();
            if (gameIds.Count == 0)
            {
                this._AchievementScanPending = false;
                this._AchievementScanCompleted = 0;
                this._AchievementScanTotal = 0;
                this.UpdatePickerStatus();
                return;
            }

            this._AchievementScanPending = false;
            this._AchievementScanCompleted = 0;
            this._AchievementScanTotal = gameIds.Count;
            this._CurrentAchievementScanMode = AchievementScanMode.Auto;
            this.UpdatePickerStatus();

            var steamId = this._SteamClient.SteamUser.GetSteamId();
            if (TryStartBackgroundWorker(
                    this._AchievementWorker,
                    new AchievementScanRequest(
                        gameIds,
                        steamId,
                        this._SteamWebApiKey,
                        this._SteamCommunityCookies,
                        AchievementScanMode.Auto)) == false)
            {
                this._AchievementScanPending = true;
                return;
            }
            this.UpdateLoadingIndicator();
        }

        private void StartCheckAllScan()
        {
            if (this._AchievementWorker.IsBusy == true || this._UnlockAllWorker.IsBusy == true)
            {
                MessageBox.Show(
                    this,
                    "Achievement scanning is already in progress.",
                    "Info",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            var gameIds = this._Games.Values
                .Select(info => info.Id)
                .Distinct()
                .ToList();
            if (gameIds.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "No games are available to scan.",
                    "Info",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            this._AchievementScanPending = false;
            this._AchievementScanCompleted = 0;
            this._AchievementScanTotal = gameIds.Count;
            this._CurrentAchievementScanMode = AchievementScanMode.LocalCheckAll;
            this.UpdatePickerStatus();

            var steamId = this._SteamClient.SteamUser.GetSteamId();
            if (TryStartBackgroundWorker(
                    this._AchievementWorker,
                    new AchievementScanRequest(
                        gameIds,
                        steamId,
                        this._SteamWebApiKey,
                        this._SteamCommunityCookies,
                        AchievementScanMode.LocalCheckAll)) == false)
            {
                MessageBox.Show(
                    this,
                    "Achievement worker is currently busy. Please try again.",
                    "Info",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }
            this.UpdateLoadingIndicator();
        }

        private void StartUnlockAllVisibleGames()
        {
            var gameIds = this._FilteredGames
                .Select(info => info.Id)
                .Distinct()
                .ToList();

            this.StartUnlockGames(gameIds, "Unlock All", "No visible games to unlock.");
        }

        private void StartUnlockSelectedVisibleGames()
        {
            var gameIds = this.GetSelectedVisibleGameIdsInDisplayOrder();

            this.StartUnlockGames(gameIds, "Unlock Selected", "No selected games to unlock.");
        }

        private void StartUnlockGames(List<uint> gameIds, string operationName, string emptyMessage)
        {
            if (this._ListWorker.IsBusy == true ||
                this._AchievementWorker.IsBusy == true ||
                this._UnlockAllWorker.IsBusy == true)
            {
                MessageBox.Show(
                    this,
                    "Another operation is in progress. Please wait for it to finish.",
                    "Info",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (gameIds == null || gameIds.Count == 0)
            {
                MessageBox.Show(
                    this,
                    emptyMessage,
                    "Info",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            DialogResult result = MessageBox.Show(
                this,
                $"{operationName} will process {gameIds.Count} games sequentially. Continue?",
                operationName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (result != DialogResult.Yes)
            {
                return;
            }

            this._UnlockAllCompleted = 0;
            this._UnlockAllTotal = gameIds.Count;
            this.UpdatePickerStatus();
            if (TryStartBackgroundWorker(this._UnlockAllWorker, new UnlockAllRequest(gameIds)) == false)
            {
                MessageBox.Show(
                    this,
                    "Unlock worker is currently busy. Please try again.",
                    "Info",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }
            this.UpdateLoadingIndicator();
        }

        private void DoUnlockAllGames(object sender, DoWorkEventArgs e)
        {
            if (sender is not BackgroundWorker worker ||
                e.Argument is not UnlockAllRequest request)
            {
                return;
            }

            int completed = 0;
            foreach (uint gameId in request.GameIds)
            {
                if (worker.CancellationPending == true)
                {
                    e.Cancel = true;
                    return;
                }

                bool success = TryUnlockAllAchievementsFromLocalProcess(
                    gameId,
                    out int changed,
                    out int skippedProtected,
                    out int unlocked,
                    out int total,
                    out string error);

                int progress = Interlocked.Increment(ref completed);
                worker.ReportProgress(
                    progress,
                    new UnlockAllProgress(
                        gameId,
                        success,
                        changed,
                        skippedProtected,
                        unlocked,
                        total,
                        error));
            }
        }

        private void OnUnlockAllGamesProgress(object sender, ProgressChangedEventArgs e)
        {
            if (e.UserState is not UnlockAllProgress progress)
            {
                return;
            }

            this._UnlockAllCompleted = e.ProgressPercentage;

            if (this._Games.TryGetValue(progress.GameId, out var game) == true &&
                progress.Unlocked >= 0 &&
                progress.Total >= 0)
            {
                game.AchievementUnlocked = progress.Unlocked;
                game.AchievementTotal = progress.Total;
            }

            if (progress.Success == true)
            {
                AppendAchievementScanLog(
                    progress.GameId,
                    $"Unlock-all completed: changed={progress.Changed}, skipped_protected={progress.SkippedProtected}, progress={progress.Unlocked}/{progress.Total}.");
            }
            else
            {
                AppendAchievementScanLog(
                    progress.GameId,
                    $"Unlock-all failed: {progress.Error ?? "unknown"}");
            }

            if (this._FilterIncompleteAchievementsMenuItem.Checked == true)
            {
                this.RefreshGames();
                return;
            }

            this.UpdatePickerStatus();
            this._GameListView.Invalidate();
        }

        private void OnUnlockAllGamesCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            this._UnlockAllCompleted = this._UnlockAllTotal;

            if (e.Error != null)
            {
                MessageBox.Show(
                    this,
                    _($"Unlock-all operation failed:{Environment.NewLine}{e.Error.Message}"),
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }

            if (this._FilterIncompleteAchievementsMenuItem.Checked == true)
            {
                this.RefreshGames();
            }
            else
            {
                this.UpdatePickerStatus();
                this._GameListView.Invalidate();
            }

            this.SaveAchievementStatusDatabase();
            this.UpdatePickerStatus();

            if (this._AchievementScanPending == true)
            {
                this.StartAchievementScan();
            }
        }

        private void DoScanAchievements(object sender, DoWorkEventArgs e)
        {
            if (e.Argument is not AchievementScanRequest request)
            {
                return;
            }

            if (sender is not BackgroundWorker worker)
            {
                return;
            }

            if (request.Mode == AchievementScanMode.LocalCheckAll)
            {
                DoScanAchievementsWithLocalProcess(worker, e, request);
                return;
            }

            if (string.IsNullOrWhiteSpace(request.SteamWebApiKey) == false)
            {
                DoScanAchievementsWithWebApi(worker, e, request);
                return;
            }

            DoScanAchievementsWithCommunity(worker, e, request);
        }

        private static void DoScanAchievementsWithCommunity(
            BackgroundWorker worker,
            DoWorkEventArgs e,
            AchievementScanRequest request)
        {
            int completed = 0;
            ParallelOptions options = new()
            {
                MaxDegreeOfParallelism = 8,
            };

            Parallel.ForEach(request.GameIds, options, (gameId, state) =>
            {
                if (worker.CancellationPending == true)
                {
                    state.Stop();
                    return;
                }

                if (TryGetAchievementProgressFromCommunity(
                        request.SteamId,
                        gameId,
                        request.CommunityCookies,
                        out int unlocked,
                        out int total) == false)
                {
                    unlocked = -1;
                    total = -1;
                }

                int progress = Interlocked.Increment(ref completed);
                worker.ReportProgress(progress, new AchievementScanProgress(gameId, unlocked, total));
            });

            if (worker.CancellationPending == true)
            {
                e.Cancel = true;
            }
        }

        private static void DoScanAchievementsWithLocalProcess(
            BackgroundWorker worker,
            DoWorkEventArgs e,
            AchievementScanRequest request)
        {
            int completed = 0;
            foreach (var gameId in request.GameIds)
            {
                if (worker.CancellationPending == true)
                {
                    e.Cancel = true;
                    return;
                }

                if (TryGetAchievementProgressFromLocalProcess(
                        gameId,
                        out int unlocked,
                        out int total) == false)
                {
                    unlocked = -1;
                    total = -1;
                }

                int progress = Interlocked.Increment(ref completed);
                worker.ReportProgress(progress, new AchievementScanProgress(gameId, unlocked, total));
            }
        }

        private static void DoScanAchievementsWithWebApi(
            BackgroundWorker worker,
            DoWorkEventArgs e,
            AchievementScanRequest request)
        {
            int completed = 0;
            ParallelOptions options = new()
            {
                MaxDegreeOfParallelism = 8,
            };

            Parallel.ForEach(request.GameIds, options, (gameId, state) =>
            {
                if (worker.CancellationPending == true)
                {
                    state.Stop();
                    return;
                }

                if (TryGetAchievementProgressFromWebApi(
                        request.SteamId,
                        request.SteamWebApiKey,
                        gameId,
                        out int unlocked,
                        out int total) == false)
                {
                    unlocked = -1;
                    total = -1;
                }

                int progress = Interlocked.Increment(ref completed);
                worker.ReportProgress(progress, new AchievementScanProgress(gameId, unlocked, total));
            });

            if (worker.CancellationPending == true)
            {
                e.Cancel = true;
            }
        }

        private void OnScanAchievementsProgress(object sender, ProgressChangedEventArgs e)
        {
            if (e.UserState is not AchievementScanProgress progress)
            {
                return;
            }

            this._AchievementScanCompleted = e.ProgressPercentage;

            if (this._Games.TryGetValue(progress.GameId, out var game) == true)
            {
                game.AchievementUnlocked = progress.Unlocked;
                game.AchievementTotal = progress.Total;
            }

            if (this._FilterIncompleteAchievementsMenuItem.Checked == true)
            {
                this.RefreshGames();
                return;
            }

            this.UpdatePickerStatus();
            this._GameListView.Invalidate();
        }

        private void OnScanAchievementsCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            this._AchievementScanCompleted = this._AchievementScanTotal;

            if (this._FilterIncompleteAchievementsMenuItem.Checked == true)
            {
                this.RefreshGames();
            }
            else
            {
                this.UpdatePickerStatus();
                this._GameListView.Invalidate();
            }

            if (this._AchievementScanPending == true)
            {
                this.StartAchievementScan();
            }

            this.SaveAchievementStatusDatabase();
            this._CurrentAchievementScanMode = AchievementScanMode.Auto;
            this.UpdatePickerStatus();
        }

        private static bool TryGetAchievementProgressFromLocalProcess(
            uint gameId,
            out int unlocked,
            out int total)
        {
            unlocked = -1;
            total = -1;

            string arguments = _($"--achievement-progress {gameId.ToString(CultureInfo.InvariantCulture)}");
            ProcessStartInfo startInfo = new()
            {
                FileName = "SAM.Game.exe",
                Arguments = arguments,
                WorkingDirectory = Application.StartupPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            try
            {
                AppendAchievementScanLog(gameId, $"PROCESS SAM.Game.exe {arguments}");
                using Process process = Process.Start(startInfo);
                if (process == null)
                {
                    AppendAchievementScanLog(gameId, "Local process did not start.");
                    return false;
                }

                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                if (process.WaitForExit(25000) == false)
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (InvalidOperationException)
                    {
                    }

                    AppendAchievementScanLog(gameId, "Local process timed out.");
                    return false;
                }

                if (TryParseLocalProcessOutput(stdout, out unlocked, out total) == true)
                {
                    AppendAchievementScanLog(gameId, $"Local process progress {unlocked}/{total}.");
                    return true;
                }

                string reason = string.IsNullOrWhiteSpace(stderr) == false ? stderr.Trim() : stdout.Trim();
                if (string.IsNullOrWhiteSpace(reason) == false)
                {
                    AppendAchievementScanLog(gameId, $"Local process failed: {reason}");
                }
                else
                {
                    AppendAchievementScanLog(gameId, _($"Local process exited with code {process.ExitCode}."));
                }
                return false;
            }
            catch (Win32Exception ex)
            {
                AppendAchievementScanLog(gameId, $"Local process start failed: {ex.Message}");
                return false;
            }
            catch (InvalidOperationException ex)
            {
                AppendAchievementScanLog(gameId, $"Local process failed: {ex.Message}");
                return false;
            }
        }

        private static bool TryUnlockAllAchievementsFromLocalProcess(
            uint gameId,
            out int changed,
            out int skippedProtected,
            out int unlocked,
            out int total,
            out string error)
        {
            changed = 0;
            skippedProtected = 0;
            unlocked = -1;
            total = -1;
            error = null;

            string arguments = _($"--unlock-all {gameId.ToString(CultureInfo.InvariantCulture)}");
            ProcessStartInfo startInfo = new()
            {
                FileName = "SAM.Game.exe",
                Arguments = arguments,
                WorkingDirectory = Application.StartupPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            try
            {
                AppendAchievementScanLog(gameId, $"PROCESS SAM.Game.exe {arguments}");
                using Process process = Process.Start(startInfo);
                if (process == null)
                {
                    error = "process_not_started";
                    return false;
                }

                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                if (process.WaitForExit(45000) == false)
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (InvalidOperationException)
                    {
                    }

                    error = "timeout";
                    return false;
                }

                if (TryParseLocalUnlockAllOutput(
                        stdout,
                        out changed,
                        out skippedProtected,
                        out unlocked,
                        out total,
                        out error) == true)
                {
                    return true;
                }

                if (string.IsNullOrWhiteSpace(error) == true)
                {
                    error = string.IsNullOrWhiteSpace(stderr) == false
                        ? stderr.Trim()
                        : stdout.Trim();
                }

                if (string.IsNullOrWhiteSpace(error) == true)
                {
                    error = _($"exit_code_{process.ExitCode}");
                }

                return false;
            }
            catch (Win32Exception ex)
            {
                error = _($"start_failed_{ex.Message}");
                return false;
            }
            catch (InvalidOperationException ex)
            {
                error = _($"process_failed_{ex.Message}");
                return false;
            }
        }

        private static bool TryParseLocalUnlockAllOutput(
            string output,
            out int changed,
            out int skippedProtected,
            out int unlocked,
            out int total,
            out string error)
        {
            changed = 0;
            skippedProtected = 0;
            unlocked = -1;
            total = -1;
            error = null;

            if (string.IsNullOrWhiteSpace(output) == true)
            {
                error = "empty_output";
                return false;
            }

            string[] lines = output
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0)
            {
                error = "empty_output";
                return false;
            }

            string lastLine = lines[lines.Length - 1].Trim();
            string[] parts = lastLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                error = "invalid_output";
                return false;
            }

            if (string.Equals(parts[0], "ERR", StringComparison.OrdinalIgnoreCase) == true)
            {
                error = parts.Length > 1
                    ? string.Join("_", parts.Skip(1))
                    : "unlock_failed";
                return false;
            }

            if (string.Equals(parts[0], "OK", StringComparison.OrdinalIgnoreCase) == false)
            {
                error = "invalid_output";
                return false;
            }

            if (parts.Length < 5)
            {
                error = "invalid_output";
                return false;
            }

            bool parsed = int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out changed) == true &&
                          int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out skippedProtected) == true &&
                          int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out unlocked) == true &&
                          int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out total) == true;
            if (parsed == false)
            {
                error = "invalid_output";
            }

            return parsed;
        }

        private static bool TryParseLocalProcessOutput(string output, out int unlocked, out int total)
        {
            unlocked = -1;
            total = -1;

            if (string.IsNullOrWhiteSpace(output) == true)
            {
                return false;
            }

            string[] lines = output
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0)
            {
                return false;
            }

            string lastLine = lines[lines.Length - 1].Trim();
            if (lastLine.StartsWith("ERR", StringComparison.OrdinalIgnoreCase) == true)
            {
                return false;
            }

            string[] parts = lastLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                return false;
            }

            return int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out unlocked) == true &&
                   int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out total) == true;
        }

        private static bool TryGetAchievementProgressFromWebApi(
            ulong steamId,
            string steamWebApiKey,
            uint gameId,
            out int unlocked,
            out int total)
        {
            unlocked = -1;
            total = -1;

            string url =
                $"https://api.steampowered.com/ISteamUserStats/GetPlayerAchievements/v1/?key={Uri.EscapeDataString(steamWebApiKey)}&steamid={steamId}&appid={gameId}";

            try
            {
                AppendAchievementScanLog(gameId, $"HTTP GET {url}");
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Timeout = 8000;
                request.ReadWriteTimeout = 8000;
                request.UserAgent = "SteamAchievementManager";
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

                using var response = (HttpWebResponse)request.GetResponse();
                using var stream = response.GetResponseStream();
                if (stream == null)
                {
                    AppendAchievementScanLog(gameId, "Web API returned no content.");
                    return false;
                }

                var payload = DeserializePlayerAchievements(stream);
                if (payload?.PlayerStats == null)
                {
                    AppendAchievementScanLog(gameId, "Web API response was invalid.");
                    return false;
                }

                if (payload.PlayerStats.Success == false)
                {
                    if (IsNoAchievementsResponse(payload.PlayerStats.Error) == true)
                    {
                        unlocked = 0;
                        total = 0;
                        return true;
                    }

                    AppendAchievementScanLog(
                        gameId,
                        $"Web API returned failure: {payload.PlayerStats.Error ?? "unknown error"}");
                    return false;
                }

                var achievements = payload.PlayerStats.Achievements;
                if (achievements == null || achievements.Length == 0)
                {
                    unlocked = 0;
                    total = 0;
                    return true;
                }

                total = achievements.Length;
                unlocked = achievements.Count(a => a != null && a.Achieved != 0);
                AppendAchievementScanLog(gameId, $"Web API progress {unlocked}/{total}.");
                return true;
            }
            catch (WebException ex)
            {
                if (TryReadWebExceptionBody(ex, out var body) == true)
                {
                    if (IsNoAchievementsResponse(body) == true)
                    {
                        unlocked = 0;
                        total = 0;
                        return true;
                    }

                    AppendAchievementScanLog(gameId, $"Web API request failed: {body}");
                }
                else
                {
                    AppendAchievementScanLog(gameId, $"Web API request failed: {ex.Message}");
                }
                return false;
            }
            catch (SerializationException ex)
            {
                AppendAchievementScanLog(gameId, $"Web API JSON parse failed: {ex.Message}");
                return false;
            }
            catch (InvalidCastException ex)
            {
                AppendAchievementScanLog(gameId, $"Web API response cast failed: {ex.Message}");
                return false;
            }
        }

        private static PlayerAchievementsResponse DeserializePlayerAchievements(Stream stream)
        {
            DataContractJsonSerializer serializer = new(typeof(PlayerAchievementsResponse));
            return serializer.ReadObject(stream) as PlayerAchievementsResponse;
        }

        private static bool IsNoAchievementsResponse(string message)
        {
            if (string.IsNullOrWhiteSpace(message) == true)
            {
                return false;
            }

            return message.IndexOf("no stats", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("no achievements", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("has no stats", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryReadWebExceptionBody(WebException ex, out string body)
        {
            body = null;
            if (ex.Response is not HttpWebResponse response)
            {
                return false;
            }

            try
            {
                using (response)
                {
                    using Stream stream = response.GetResponseStream();
                    if (stream == null)
                    {
                        return false;
                    }

                    using StreamReader reader = new(stream);
                    body = reader.ReadToEnd().Trim();
                }
            }
            catch (IOException)
            {
                return false;
            }

            return string.IsNullOrWhiteSpace(body) == false;
        }

        private static bool TryGetAchievementProgressFromCommunity(
            ulong steamId,
            uint gameId,
            Dictionary<string, string> cookies,
            out int unlocked,
            out int total)
        {
            unlocked = -1;
            total = -1;

            string url = $"https://steamcommunity.com/profiles/{steamId}/stats/{gameId}/?xml=1";

            try
            {
                AppendAchievementScanLog(gameId, $"HTTP GET {url}");
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Timeout = 8000;
                request.ReadWriteTimeout = 8000;
                request.UserAgent = "SteamAchievementManager";
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                if (cookies != null && cookies.Count > 0)
                {
                    request.CookieContainer = CreateSteamCommunityCookieContainer(cookies);
                }

                using var response = (HttpWebResponse)request.GetResponse();
                using var stream = response.GetResponseStream();
                if (stream == null)
                {
                    AppendAchievementScanLog(gameId, "Steam Community returned no content.");
                    return false;
                }

                XPathDocument document = new(stream);
                var navigator = document.CreateNavigator();

                string error = navigator.SelectSingleNode("//error")?.Value;
                if (string.IsNullOrWhiteSpace(error) == false)
                {
                    if (IsNoAchievementsResponse(error) == true)
                    {
                        unlocked = 0;
                        total = 0;
                        return true;
                    }

                    AppendAchievementScanLog(gameId, $"Steam Community returned error: {error.Trim()}");
                    return false;
                }

                var nodes = navigator.Select("//achievements/achievement");
                int found = 0;
                int achieved = 0;
                while (nodes.MoveNext() == true)
                {
                    found++;

                    string closed = nodes.Current.GetAttribute("closed", "");
                    if (string.IsNullOrEmpty(closed) == true)
                    {
                        closed = nodes.Current.SelectSingleNode("closed")?.Value;
                    }

                    if (string.Equals(closed, "1", StringComparison.Ordinal) == true)
                    {
                        achieved++;
                    }
                }

                if (found > 0)
                {
                    unlocked = achieved;
                    total = found;
                    AppendAchievementScanLog(gameId, $"Steam Community progress {unlocked}/{total}.");
                    return true;
                }

                // Valid stats document but no achievements list.
                if (navigator.SelectSingleNode("//playerstats") != null)
                {
                    unlocked = 0;
                    total = 0;
                    return true;
                }

                AppendAchievementScanLog(gameId, "Steam Community response did not contain expected stats nodes.");
                return false;
            }
            catch (WebException ex)
            {
                if (TryReadWebExceptionBody(ex, out var body) == true)
                {
                    AppendAchievementScanLog(gameId, $"Steam Community request failed: {body}");
                }
                else
                {
                    AppendAchievementScanLog(gameId, $"Steam Community request failed: {ex.Message}");
                }
                return false;
            }
            catch (XPathException ex)
            {
                AppendAchievementScanLog(gameId, $"Steam Community XML parse failed: {ex.Message}");
                return false;
            }
        }

        private static CookieContainer CreateSteamCommunityCookieContainer(IDictionary<string, string> cookies)
        {
            CookieContainer container = new();
            if (cookies == null || cookies.Count == 0)
            {
                return container;
            }

            string loginSecure = null;
            if (cookies.TryGetValue("steamLoginSecure", out var value) == true &&
                string.IsNullOrWhiteSpace(value) == false)
            {
                loginSecure = value.Trim();
            }

            string machineAuthCookieName = GetSteamMachineAuthCookieName(loginSecure);
            foreach (var pair in cookies)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) == true ||
                    string.IsNullOrWhiteSpace(pair.Value) == true)
                {
                    continue;
                }

                string cookieName = pair.Key.Trim();
                if (string.Equals(cookieName, "steamMachineAuth", StringComparison.OrdinalIgnoreCase) == true &&
                    string.Equals(machineAuthCookieName, "steamMachineAuth", StringComparison.OrdinalIgnoreCase) == false)
                {
                    cookieName = machineAuthCookieName;
                }

                var cookie = new Cookie(cookieName, pair.Value.Trim(), "/", "steamcommunity.com")
                {
                    Secure = true,
                };
                container.Add(cookie);
            }
            return container;
        }

        private static string GetSteamMachineAuthCookieName(string steamLoginSecure)
        {
            if (string.IsNullOrWhiteSpace(steamLoginSecure) == true)
            {
                return "steamMachineAuth";
            }

            string decoded = WebUtility.UrlDecode(steamLoginSecure);
            int separator = decoded.IndexOf('|');
            if (separator > 0)
            {
                decoded = decoded.Substring(0, separator);
            }

            if (decoded.Length >= 17)
            {
                return "steamMachineAuth" + decoded.Substring(0, 17);
            }

            return "steamMachineAuth";
        }

        private static void AppendAchievementScanLog(uint gameId, string message)
        {
            try
            {
                var path = GetAchievementScanLogPath();
                string sanitized = RedactSensitiveContent(message);
                string line = _($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] App {gameId}: {sanitized}{Environment.NewLine}");
                lock (_ScanLogLock)
                {
                    File.AppendAllText(path, line, Encoding.UTF8);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static string GetAchievementScanLogPath()
        {
            return Path.Combine(Application.StartupPath, "sam-picker-scan.log");
        }

        private static void ClearAchievementScanLog()
        {
            try
            {
                string path = GetAchievementScanLogPath();
                lock (_ScanLogLock)
                {
                    using FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.Read);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static string RedactSensitiveContent(string text)
        {
            if (string.IsNullOrEmpty(text) == true)
            {
                return text;
            }

            return RedactQueryParameter(text, "key");
        }

        private static string RedactQueryParameter(string text, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(text) == true ||
                string.IsNullOrWhiteSpace(parameterName) == true)
            {
                return text;
            }

            string token = parameterName + "=";
            int searchStart = 0;
            while (true)
            {
                int index = text.IndexOf(token, searchStart, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                {
                    break;
                }

                int valueStart = index + token.Length;
                int valueEnd = text.IndexOf('&', valueStart);
                if (valueEnd < 0)
                {
                    valueEnd = text.Length;
                }

                text = text.Substring(0, valueStart) + "***" + text.Substring(valueEnd);
                searchStart = valueStart + 3;
            }

            return text;
        }

        private void DoDownloadLogo(object sender, DoWorkEventArgs e)
        {
            var info = (GameInfo)e.Argument;

            this._LogosAttempted.Add(info.ImageUrl);

            using (WebClient downloader = new())
            {
                try
                {
                    var data = downloader.DownloadData(new Uri(info.ImageUrl));
                    using (MemoryStream stream = new(data, false))
                    {
                        Bitmap bitmap = new(stream);
                        e.Result = new LogoInfo(info.Id, bitmap);
                    }
                }
                catch (Exception)
                {
                    e.Result = new LogoInfo(info.Id, null);
                }
            }
        }

        private void OnDownloadLogo(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Error != null || e.Cancelled == true)
            {
                return;
            }

            if (e.Result is LogoInfo logoInfo &&
                logoInfo.Bitmap != null &&
                this._Games.TryGetValue(logoInfo.Id, out var gameInfo) == true)
            {
                this._GameListView.BeginUpdate();
                var imageIndex = this._LogoImageList.Images.Count;
                this._LogoImageList.Images.Add(gameInfo.ImageUrl, logoInfo.Bitmap);
                this._LogoSmallImageList.Images.Add(gameInfo.ImageUrl, logoInfo.Bitmap);
                gameInfo.ImageIndex = imageIndex;
                this._GameListView.EndUpdate();
            }

            this.DownloadNextLogo();
        }

        private void DownloadNextLogo()
        {
            lock (this._LogoLock)
            {

                if (this._LogoWorker.IsBusy == true)
                {
                    return;
                }

                GameInfo info;
                while (true)
                {
                    if (this._LogoQueue.TryDequeue(out info) == false)
                    {
                        this._DownloadStatusLabel.Visible = false;
                        return;
                    }

                    if (this._FilteredGames.Contains(info) == false)
                    {
                        this._LogosAttempting.Remove(info.ImageUrl);
                        continue;
                    }

                    break;
                }

                this._DownloadStatusLabel.Text = $"Downloading {1 + this._LogoQueue.Count} game icons...";
                this._DownloadStatusLabel.Visible = true;

                if (TryStartBackgroundWorker(this._LogoWorker, info) == false)
                {
                    this._LogoQueue.Enqueue(info);
                    return;
                }
            }
        }

        private string GetGameImageUrl(uint id)
        {
            string candidate;

            var currentLanguage = this._SteamClient.SteamApps008.GetCurrentGameLanguage();

            candidate = this._SteamClient.SteamApps001.GetAppData(id, _($"small_capsule/{currentLanguage}"));
            if (string.IsNullOrEmpty(candidate) == false)
            {
                return _($"https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/{id}/{candidate}");
            }

            if (currentLanguage != "english")
            {
                candidate = this._SteamClient.SteamApps001.GetAppData(id, "small_capsule/english");
                if (string.IsNullOrEmpty(candidate) == false)
                {
                    return _($"https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/{id}/{candidate}");
                }
            }

            candidate = this._SteamClient.SteamApps001.GetAppData(id, "logo");
            if (string.IsNullOrEmpty(candidate) == false)
            {
                return _($"https://cdn.steamstatic.com/steamcommunity/public/images/apps/{id}/{candidate}.jpg");
            }

            return null;
        }

        private void AddGameToLogoQueue(GameInfo info)
        {
            if (info.ImageIndex > 0)
            {
                return;
            }

            var imageUrl = GetGameImageUrl(info.Id);
            if (string.IsNullOrEmpty(imageUrl) == true)
            {
                return;
            }

            info.ImageUrl = imageUrl;

            int imageIndex = this._LogoImageList.Images.IndexOfKey(imageUrl);
            if (imageIndex >= 0)
            {
                info.ImageIndex = imageIndex;
            }
            else if (
                this._LogosAttempting.Contains(imageUrl) == false &&
                this._LogosAttempted.Contains(imageUrl) == false)
            {
                this._LogosAttempting.Add(imageUrl);
                this._LogoQueue.Enqueue(info);
            }
        }

        private bool OwnsGame(uint id)
        {
            return this._SteamClient.SteamApps008.IsSubscribedApp(id);
        }

        private static void ApplyOwnedGameProgress(GameInfo info, OwnedGameInfo game)
        {
            if (info == null || game == null)
            {
                return;
            }

            if (game.HasAchievementProgress == true)
            {
                info.AchievementUnlocked = game.AchievementUnlocked;
                info.AchievementTotal = game.AchievementTotal;
                return;
            }

            if (game.HasStatsLink == false)
            {
                info.AchievementUnlocked = 0;
                info.AchievementTotal = 0;
            }
        }

        private GameInfo AddOwnedGame(uint id, string type, string explicitName)
        {
            if (this._Games.TryGetValue(id, out var existing) == true)
            {
                return existing;
            }

            GameInfo info = new(id, type);
            if (string.IsNullOrWhiteSpace(explicitName) == false)
            {
                info.Name = explicitName.Trim();
            }
            else
            {
                info.Name = this._SteamClient.SteamApps001.GetAppData(info.Id, "name");
            }

            this._Games.Add(id, info);
            return info;
        }

        private void AddGame(uint id, string type)
        {
            if (this._Games.ContainsKey(id) == true)
            {
                return;
            }

            if (this.OwnsGame(id) == false)
            {
                return;
            }

            this.AddOwnedGame(id, type, null);
        }

        private void AddGames()
        {
            ClearAchievementScanLog();
            if (this._ListWorker.IsBusy == true)
            {
                this._ReloadGamesPending = true;
                return;
            }

            this._ReloadGamesPending = false;
            this._AchievementScanPending = false;
            this._UnlockAllCompleted = 0;
            this._UnlockAllTotal = 0;
            if (this._UnlockAllWorker.IsBusy == true)
            {
                this._UnlockAllWorker.CancelAsync();
            }
            if (this._AchievementWorker.IsBusy == true)
            {
                this._AchievementWorker.CancelAsync();
            }

            this._AchievementScanCompleted = 0;
            this._AchievementScanTotal = 0;
            this._Games.Clear();
            this._RefreshGamesButton.Enabled = false;
            if (TryStartBackgroundWorker(this._ListWorker) == false)
            {
                this._ReloadGamesPending = true;
                return;
            }
            this.UpdateLoadingIndicator();
        }

        private void AddDefaultGames()
        {
            this.AddGame(480, "normal"); // Spacewar
        }

        private void OnTimer(object sender, EventArgs e)
        {
            this._CallbackTimer.Enabled = false;
            this._SteamClient.RunCallbacks(false);
            this._CallbackTimer.Enabled = true;
        }

        private void OnActivateGame(object sender, EventArgs e)
        {
            var focusedItem = (sender as MyListView)?.FocusedItem;
            var index = focusedItem != null ? focusedItem.Index : -1;
            if (index < 0 || index >= this._FilteredGames.Count)
            {
                return;
            }

            var info = this._FilteredGames[index];
            if (info == null)
            {
                return;
            }

            try
            {
                string managerPath = Path.Combine(Application.StartupPath, "SAM.Game.exe");
                Process.Start(new ProcessStartInfo()
                {
                    FileName = managerPath,
                    Arguments = info.Id.ToString(CultureInfo.InvariantCulture),
                    WorkingDirectory = Application.StartupPath,
                    UseShellExecute = true,
                });
            }
            catch (Win32Exception)
            {
                MessageBox.Show(
                    this,
                    "Failed to start SAM.Game.exe from the application directory.",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void OnRefresh(object sender, EventArgs e)
        {
            this._AddGameTextBox.Text = "";
            this.AddGames();
        }

        private void OnCheckAllAchievements(object sender, EventArgs e)
        {
            if (this._Games.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "No games are loaded yet.",
                    "Info",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            var result = MessageBox.Show(
                this,
                "Check All will scan every game sequentially and may take a long time. Continue?",
                "Check All",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (result != DialogResult.Yes)
            {
                return;
            }

            this.StartCheckAllScan();
        }

        private void OnUnlockAllAchievements(object sender, EventArgs e)
        {
            if (this._FilteredGames.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "No visible games are available.",
                    "Info",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            this.StartUnlockAllVisibleGames();
        }

        private void OnUnlockSelectedAchievements(object sender, EventArgs e)
        {
            this.StartUnlockSelectedVisibleGames();
        }

        private void OnGameSelectionChanged(object sender, EventArgs e)
        {
            this.UpdateUnlockSelectedButtonVisibility();
        }

        private void OnAddGame(object sender, EventArgs e)
        {
            uint id;

            if (uint.TryParse(this._AddGameTextBox.Text, out id) == false)
            {
                MessageBox.Show(
                    this,
                    "Please enter a valid game ID.",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            if (this.OwnsGame(id) == false)
            {
                MessageBox.Show(this, "You don't own that game.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            while (this._LogoQueue.TryDequeue(out var logo) == true)
            {
                // clear the download queue because we will be showing only one app
                this._LogosAttempted.Remove(logo.ImageUrl);
            }

            this._AchievementScanPending = false;
            this._UnlockAllCompleted = 0;
            this._UnlockAllTotal = 0;
            if (this._UnlockAllWorker.IsBusy == true)
            {
                this._UnlockAllWorker.CancelAsync();
            }
            if (this._AchievementWorker.IsBusy == true)
            {
                this._AchievementWorker.CancelAsync();
            }
            this._AchievementScanCompleted = 0;
            this._AchievementScanTotal = 0;

            this._AddGameTextBox.Text = "";
            this._Games.Clear();
            this.AddGame(id, "normal");
            this._FilterGamesMenuItem.Checked = true;
            this.RefreshGames();
            this.StartAchievementScan();
            this.DownloadNextLogo();
        }

        private void OnFilterUpdate(object sender, EventArgs e)
        {
            if (this._FilterIncompleteAchievementsMenuItem.Checked == true)
            {
                this.StartAchievementScan();
            }

            this.RefreshGames();

            // Compatibility with _GameListView SearchForVirtualItemEventHandler (otherwise _SearchGameTextBox loose focus on KeyUp)
            this._SearchGameTextBox.Focus();
        }

        private void OnViewGrid(object sender, EventArgs e)
        {
            this.ApplyViewMode(GameViewMode.Grid, true);
            this.RefreshGames();
        }

        private void OnViewList(object sender, EventArgs e)
        {
            this.ApplyViewMode(GameViewMode.List, true);
            this.RefreshGames();
        }

        private void OnSortNameAscending(object sender, EventArgs e)
        {
            this.ApplySortMode(GameSortMode.NameAscending, true, true);
        }

        private void OnSortNameDescending(object sender, EventArgs e)
        {
            this.ApplySortMode(GameSortMode.NameDescending, true, true);
        }

        private void OnSortAchievementAscending(object sender, EventArgs e)
        {
            this.ApplySortMode(GameSortMode.AchievementAscending, true, true);
        }

        private void OnSortAchievementDescending(object sender, EventArgs e)
        {
            this.ApplySortMode(GameSortMode.AchievementDescending, true, true);
        }

        private void OnShowLogs(object sender, EventArgs e)
        {
            string path = GetAchievementScanLogPath();
            try
            {
                if (File.Exists(path) == false)
                {
                    File.WriteAllText(path, "");
                }

                Process.Start(new ProcessStartInfo()
                {
                    FileName = "notepad.exe",
                    Arguments = _($"\"{path}\""),
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    _($"Could not open log file:{Environment.NewLine}{ex.Message}"),
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            this.ResizeListColumn();
        }

        private void OnGameListViewDrawItem(object sender, DrawListViewItemEventArgs e)
        {
            if (e.ItemIndex < 0 || e.ItemIndex >= this._FilteredGames.Count)
            {
                e.DrawDefault = true;
                return;
            }

            var info = this._FilteredGames[e.ItemIndex];
            if (info.ImageIndex <= 0)
            {
                this.AddGameToLogoQueue(info);
                this.DownloadNextLogo();
            }

            if (this._ViewMode != GameViewMode.Grid)
            {
                e.DrawDefault = true;
                return;
            }

            if (e.Item.Bounds.IntersectsWith(this._GameListView.ClientRectangle) == false)
            {
                e.DrawDefault = false;
                return;
            }

            e.DrawDefault = false;
            Rectangle cardRect = Rectangle.Inflate(e.Bounds, -4, -4);
            if (cardRect.Width < 20 || cardRect.Height < 20)
            {
                return;
            }

            bool selected = this._GameListView.SelectedIndices.Contains(e.ItemIndex);
            Color cardColor = selected == true
                ? Color.FromArgb(220, 233, 255)
                : Color.White;
            Color borderColor = selected == true
                ? Color.FromArgb(72, 124, 244)
                : Color.FromArgb(226, 228, 233);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = CreateRoundedRectanglePath(cardRect, 10))
            using (SolidBrush fillBrush = new(cardColor))
            using (Pen borderPen = new(borderColor))
            {
                e.Graphics.FillPath(fillBrush, path);
                e.Graphics.DrawPath(borderPen, path);
            }

            Rectangle imageRect = new(
                cardRect.X + 8,
                cardRect.Y + 8,
                cardRect.Width - 16,
                Math.Max(26, cardRect.Height - 36));

            if (info.ImageIndex >= 0 && info.ImageIndex < this._LogoImageList.Images.Count)
            {
                Image image = this._LogoImageList.Images[info.ImageIndex];
                if (image != null)
                {
                    e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    e.Graphics.DrawImage(image, imageRect);
                }
            }

            Rectangle textRect = new(
                cardRect.X + 10,
                cardRect.Bottom - 24,
                cardRect.Width - 20,
                18);

            using (StringFormat format = new()
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap,
            })
            using (SolidBrush textBrush = new(selected == true
                ? Color.FromArgb(19, 52, 128)
                : Color.FromArgb(30, 30, 34)))
            {
                e.Graphics.DrawString(info.DisplayName, this._GameListView.Font, textBrush, textRect, format);
            }
        }

        private static GraphicsPath CreateRoundedRectanglePath(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            GraphicsPath path = new();

            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();

            return path;
        }
    }
}

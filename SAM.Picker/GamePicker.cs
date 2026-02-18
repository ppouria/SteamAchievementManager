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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
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

        private readonly API.Client _SteamClient;

        private readonly Dictionary<uint, GameInfo> _Games;
        private readonly List<GameInfo> _FilteredGames;

        private readonly object _LogoLock;
        private readonly HashSet<string> _LogosAttempting;
        private readonly HashSet<string> _LogosAttempted;
        private readonly ConcurrentQueue<GameInfo> _LogoQueue;

        private readonly API.Callbacks.AppDataChanged _AppDataChangedCallback;
        private readonly string _SteamWebApiKey;
        private readonly Dictionary<string, string> _SteamCommunityCookies;

        private int _AchievementScanCompleted;
        private int _AchievementScanTotal;
        private bool _AchievementScanPending;
        private int _LoadingSpinnerFrame;
        private GameViewMode _ViewMode;
        private GameSortMode _SortMode;

        private sealed class AchievementScanRequest
        {
            public readonly List<uint> GameIds;
            public readonly ulong SteamId;
            public readonly string SteamWebApiKey;
            public readonly Dictionary<string, string> CommunityCookies;

            public AchievementScanRequest(
                List<uint> gameIds,
                ulong steamId,
                string steamWebApiKey,
                Dictionary<string, string> communityCookies)
            {
                this.GameIds = gameIds;
                this.SteamId = steamId;
                this.SteamWebApiKey = steamWebApiKey;
                this.CommunityCookies = communityCookies != null
                    ? new Dictionary<string, string>(communityCookies, StringComparer.OrdinalIgnoreCase)
                    : null;
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

        [DataContract]
        private sealed class PickerPreferences
        {
            [DataMember(Name = "view_mode")]
            public string ViewMode { get; set; }

            [DataMember(Name = "sort_mode")]
            public string SortMode { get; set; }
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

            Bitmap blank = new(this._LogoImageList.ImageSize.Width, this._LogoImageList.ImageSize.Height);
            using (var g = Graphics.FromImage(blank))
            {
                g.Clear(Color.DimGray);
            }

            this._LogoImageList.Images.Add("Blank", blank);

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
            this._PickerStatusLabel.Text = "Downloading game list...";

            if (TryDownloadGamesXml(out var pairs) == false)
            {
                AppendAchievementScanLog(0, "Failed to download games.xml from gib.me. Falling back to ISteamApps/GetAppList.");
                this._PickerStatusLabel.Text = "Primary list unavailable, trying Steam app list...";

                if (TryDownloadAppListFromSteam(out pairs) == false)
                {
                    throw new InvalidOperationException("Failed to download game list from both primary and fallback endpoints.");
                }
            }

            this._PickerStatusLabel.Text = "Checking game ownership...";
            foreach (var kv in pairs)
            {
                this.AddGame(kv.Key, kv.Value);
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

        private void OnDownloadList(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Error != null || e.Cancelled == true)
            {
                this.AddDefaultGames();
                MessageBox.Show(e.Error.ToString(), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            this.RefreshGames();
            this._RefreshGamesButton.Enabled = true;
            this.StartAchievementScan();
            this.DownloadNextLogo();
            this.UpdateLoadingIndicator();
        }

        private void RefreshGames()
        {
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
            this.ResizeListColumn();
            this.UpdatePickerStatus();

            if (this._GameListView.Items.Count > 0)
            {
                this._GameListView.Items[0].Selected = true;
                this._GameListView.Select();
            }
        }

        private void UpdatePickerStatus()
        {
            string message = $"Displaying {this._FilteredGames.Count} games. Total {this._Games.Count} games.";
            if (this._AchievementWorker.IsBusy == true)
            {
                string mode = string.IsNullOrEmpty(this._SteamWebApiKey) == false
                    ? "web-api"
                    : (this._SteamCommunityCookies?.Count ?? 0) > 0
                        ? "community-auth"
                        : "community";
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

        private void UpdateLoadingIndicator()
        {
            bool loading = this._ListWorker.IsBusy == true || this._AchievementWorker.IsBusy == true;
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
            bool loading = this._ListWorker.IsBusy == true || this._AchievementWorker.IsBusy == true;
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
            var info = this._FilteredGames[e.ItemIndex];
            e.Item = info.Item = new()
            {
                Text = info.DisplayName,
                ImageIndex = info.ImageIndex,
            };
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
            this.UpdatePickerStatus();

            var steamId = this._SteamClient.SteamUser.GetSteamId();
            this._AchievementWorker.RunWorkerAsync(
                new AchievementScanRequest(gameIds, steamId, this._SteamWebApiKey, this._SteamCommunityCookies));
            this.UpdateLoadingIndicator();
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
            foreach (var pair in cookies)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) == true ||
                    string.IsNullOrWhiteSpace(pair.Value) == true)
                {
                    continue;
                }

                var cookie = new Cookie(pair.Key.Trim(), pair.Value.Trim(), "/", ".steamcommunity.com")
                {
                    Secure = true,
                };
                container.Add(cookie);
            }
            return container;
        }

        private static void AppendAchievementScanLog(uint gameId, string message)
        {
            try
            {
                var path = Path.Combine(Application.StartupPath, "sam-picker-scan.log");
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

                    if (info.Item == null)
                    {
                        continue;
                    }

                    if (this._FilteredGames.Contains(info) == false ||
                        info.Item.Bounds.IntersectsWith(this._GameListView.ClientRectangle) == false)
                    {
                        this._LogosAttempting.Remove(info.ImageUrl);
                        continue;
                    }

                    break;
                }

                this._DownloadStatusLabel.Text = $"Downloading {1 + this._LogoQueue.Count} game icons...";
                this._DownloadStatusLabel.Visible = true;

                this._LogoWorker.RunWorkerAsync(info);
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

            GameInfo info = new(id, type);
            info.Name = this._SteamClient.SteamApps001.GetAppData(info.Id, "name");
            this._Games.Add(id, info);
        }

        private void AddGames()
        {
            this._AchievementScanPending = false;
            if (this._AchievementWorker.IsBusy == true)
            {
                this._AchievementWorker.CancelAsync();
            }

            this._AchievementScanCompleted = 0;
            this._AchievementScanTotal = 0;
            this._Games.Clear();
            this._RefreshGamesButton.Enabled = false;
            this._ListWorker.RunWorkerAsync();
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
                Process.Start("SAM.Game.exe", info.Id.ToString(CultureInfo.InvariantCulture));
            }
            catch (Win32Exception)
            {
                MessageBox.Show(
                    this,
                    "Failed to start SAM.Game.exe.",
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
            string path = Path.Combine(Application.StartupPath, "sam-picker-scan.log");
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
            e.DrawDefault = true;

            if (e.Item.Bounds.IntersectsWith(this._GameListView.ClientRectangle) == false)
            {
                return;
            }

            var info = this._FilteredGames[e.ItemIndex];
            if (info.ImageIndex <= 0)
            {
                this.AddGameToLogoQueue(info);
                this.DownloadNextLogo();
            }
        }
    }
}

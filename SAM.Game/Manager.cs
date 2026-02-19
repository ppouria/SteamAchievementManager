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
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Windows.Forms;
using static SAM.Game.InvariantShorthand;
using APITypes = SAM.API.Types;

namespace SAM.Game
{
    internal partial class Manager : Form
    {
        private readonly long _GameId;
        private readonly API.Client _SteamClient;

        private readonly WebClient _IconDownloader = new();

        private readonly List<Stats.AchievementInfo> _IconQueue = new();
        private readonly List<Stats.StatDefinition> _StatDefinitions = new();

        private readonly List<Stats.AchievementDefinition> _AchievementDefinitions = new();

        private readonly BindingList<Stats.StatInfo> _Statistics = new();

        private readonly API.Callbacks.UserStatsReceived _UserStatsReceivedCallback;

        //private API.Callback<APITypes.UserStatsStored> UserStatsStoredCallback;

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

        public Manager(long gameId, API.Client client)
        {
            this.InitializeComponent();
            this.Text = BuildManagerWindowTitle();
            this.ApplyModernTheme();
            this._AutoSelectCountTextBox.Text = "25";

            this._MainTabControl.SelectedTab = this._AchievementsTabPage;
            //this.statisticsList.Enabled = this.checkBox1.Checked;

            this._AchievementImageList.Images.Add("Blank", new Bitmap(64, 64));

            this._StatisticsDataGridView.AutoGenerateColumns = false;

            this._StatisticsDataGridView.Columns.Add("name", "Name");
            this._StatisticsDataGridView.Columns[0].ReadOnly = true;
            this._StatisticsDataGridView.Columns[0].Width = 200;
            this._StatisticsDataGridView.Columns[0].DataPropertyName = "DisplayName";

            this._StatisticsDataGridView.Columns.Add("value", "Value");
            this._StatisticsDataGridView.Columns[1].ReadOnly = this._EnableStatsEditingCheckBox.Checked == false;
            this._StatisticsDataGridView.Columns[1].Width = 90;
            this._StatisticsDataGridView.Columns[1].DataPropertyName = "Value";

            this._StatisticsDataGridView.Columns.Add("extra", "Extra");
            this._StatisticsDataGridView.Columns[2].ReadOnly = true;
            this._StatisticsDataGridView.Columns[2].Width = 200;
            this._StatisticsDataGridView.Columns[2].DataPropertyName = "Extra";

            this._StatisticsDataGridView.DataSource = new BindingSource()
            {
                DataSource = this._Statistics,
            };

            this._GameId = gameId;
            this._SteamClient = client;

            this._IconDownloader.DownloadDataCompleted += this.OnIconDownload;

            string name = this._SteamClient.SteamApps001.GetAppData((uint)this._GameId, "name");
            if (name != null)
            {
                base.Text += " | " + name;
            }
            else
            {
                base.Text += " | " + this._GameId.ToString(CultureInfo.InvariantCulture);
            }

            this._UserStatsReceivedCallback = client.CreateAndRegisterCallback<API.Callbacks.UserStatsReceived>();
            this._UserStatsReceivedCallback.OnRun += this.OnUserStatsReceived;

            //this.UserStatsStoredCallback = new API.Callback(1102, new API.Callback.CallbackFunction(this.OnUserStatsStored));

            this.RefreshStats();
        }

        private void ApplyModernTheme()
        {
            this.SuspendLayout();

            this.BackColor = Color.FromArgb(242, 242, 247);
            this.ForeColor = Color.FromArgb(24, 24, 28);
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
            this.MinimumSize = new Size(980, 620);
            if (this.ClientSize.Width < 980 || this.ClientSize.Height < 620)
            {
                this.ClientSize = new Size(1080, 700);
            }

            this._MainToolStrip.BackColor = Color.FromArgb(252, 252, 253);
            this._MainToolStrip.ForeColor = this.ForeColor;
            this._MainToolStrip.GripStyle = ToolStripGripStyle.Hidden;
            this._MainToolStrip.Padding = new Padding(10, 6, 10, 6);
            this._MainToolStrip.AutoSize = false;
            this._MainToolStrip.Height = 44;
            this._MainToolStrip.RenderMode = ToolStripRenderMode.System;

            this._AchievementsToolStrip.BackColor = Color.FromArgb(252, 252, 253);
            this._AchievementsToolStrip.ForeColor = this.ForeColor;
            this._AchievementsToolStrip.GripStyle = ToolStripGripStyle.Hidden;
            this._AchievementsToolStrip.Padding = new Padding(8, 6, 8, 6);
            this._AchievementsToolStrip.AutoSize = false;
            this._AchievementsToolStrip.Height = 40;
            this._AchievementsToolStrip.RenderMode = ToolStripRenderMode.System;

            this.StyleToolStripButton(this._StoreButton, false);
            this.StyleToolStripButton(this._ReloadButton, false);
            this.StyleToolStripButton(this._ResetButton, false);
            this.StyleToolStripButton(this._LockAllButton, false);
            this.StyleToolStripButton(this._InvertAllButton, false);
            this.StyleToolStripButton(this._UnlockAllButton, false);
            this.StyleToolStripButton(this._DisplayLockedOnlyButton, true);
            this.StyleToolStripButton(this._DisplayUnlockedOnlyButton, true);
            this.StyleToolStripButton(this._AutoSelectApplyButton, true);

            this._DisplayLabel.ForeColor = Color.FromArgb(110, 110, 116);
            this._MatchingStringLabel.ForeColor = Color.FromArgb(110, 110, 116);
            this._AutoSelectLabel.ForeColor = Color.FromArgb(110, 110, 116);

            this._MatchingStringTextBox.AutoSize = false;
            this._MatchingStringTextBox.Size = new Size(180, 26);
            this._MatchingStringTextBox.BorderStyle = BorderStyle.FixedSingle;
            this._MatchingStringTextBox.BackColor = Color.White;
            this._MatchingStringTextBox.ForeColor = this.ForeColor;

            this._AutoSelectCountTextBox.AutoSize = false;
            this._AutoSelectCountTextBox.Size = new Size(70, 26);
            this._AutoSelectCountTextBox.BorderStyle = BorderStyle.FixedSingle;
            this._AutoSelectCountTextBox.BackColor = Color.White;
            this._AutoSelectCountTextBox.ForeColor = this.ForeColor;

            foreach (ToolStripItem item in this._MainToolStrip.Items)
            {
                if (item is ToolStripSeparator separator)
                {
                    separator.Margin = new Padding(6, 0, 6, 0);
                }
            }

            foreach (ToolStripItem item in this._AchievementsToolStrip.Items)
            {
                if (item is ToolStripSeparator separator)
                {
                    separator.Margin = new Padding(6, 0, 6, 0);
                }
            }

            this._MainTabControl.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
            this._MainTabControl.Padding = new Point(18, 8);

            this._AchievementsTabPage.BackColor = Color.FromArgb(247, 247, 250);
            this._StatisticsTabPage.BackColor = Color.FromArgb(247, 247, 250);

            this._AchievementListView.BackColor = Color.White;
            this._AchievementListView.ForeColor = this.ForeColor;
            this._AchievementListView.BorderStyle = BorderStyle.None;
            this._AchievementListView.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
            this._AchievementListView.GridLines = false;

            this._MainStatusStrip.SizingGrip = false;
            this._MainStatusStrip.BackColor = Color.FromArgb(248, 248, 250);
            this._MainStatusStrip.ForeColor = Color.FromArgb(110, 110, 116);
            this._CountryStatusLabel.ForeColor = Color.FromArgb(110, 110, 116);
            this._GameStatusLabel.ForeColor = Color.FromArgb(110, 110, 116);
            this._DownloadStatusLabel.ForeColor = Color.FromArgb(110, 110, 116);

            this._StatisticsDataGridView.BackgroundColor = Color.White;
            this._StatisticsDataGridView.BorderStyle = BorderStyle.None;
            this._StatisticsDataGridView.GridColor = Color.FromArgb(232, 234, 239);
            this._StatisticsDataGridView.EnableHeadersVisualStyles = false;
            this._StatisticsDataGridView.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            this._StatisticsDataGridView.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(246, 247, 251);
            this._StatisticsDataGridView.ColumnHeadersDefaultCellStyle.ForeColor = this.ForeColor;
            this._StatisticsDataGridView.DefaultCellStyle.BackColor = Color.White;
            this._StatisticsDataGridView.DefaultCellStyle.ForeColor = this.ForeColor;
            this._StatisticsDataGridView.DefaultCellStyle.SelectionBackColor = Color.FromArgb(223, 231, 246);
            this._StatisticsDataGridView.DefaultCellStyle.SelectionForeColor = this.ForeColor;
            this._StatisticsDataGridView.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 251, 255);
            this._StatisticsDataGridView.RowHeadersVisible = false;

            this._EnableStatsEditingCheckBox.ForeColor = Color.FromArgb(90, 90, 98);

            this.ResumeLayout(true);
        }

        private void StyleToolStripButton(ToolStripButton button, bool textOnly)
        {
            if (button == null)
            {
                return;
            }

            button.Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
            button.ForeColor = this.ForeColor;
            button.Padding = new Padding(8, 0, 8, 0);
            button.Margin = new Padding(0, 0, 4, 0);

            if (textOnly == true || button.Image == null)
            {
                button.DisplayStyle = ToolStripItemDisplayStyle.Text;
                return;
            }

            button.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
        }

        private void AddAchievementIcon(Stats.AchievementInfo info, Image icon)
        {
            if (icon == null)
            {
                info.ImageIndex = 0;
            }
            else
            {
                info.ImageIndex = this._AchievementImageList.Images.Count;
                this._AchievementImageList.Images.Add(info.IsAchieved == true ? info.IconNormal : info.IconLocked, icon);
            }
        }

        private void OnIconDownload(object sender, DownloadDataCompletedEventArgs e)
        {
            if (this.IsDisposed == true || this.Disposing == true)
            {
                return;
            }

            if (this._AchievementListView.IsHandleCreated == false)
            {
                return;
            }

            if (this._AchievementListView.InvokeRequired == true)
            {
                try
                {
                    this._AchievementListView.BeginInvoke((MethodInvoker)(() =>
                    {
                        this.ProcessIconDownload(e);
                    }));
                }
                catch (InvalidOperationException)
                {
                }

                return;
            }

            this.ProcessIconDownload(e);
        }

        private void ProcessIconDownload(DownloadDataCompletedEventArgs e)
        {
            if (e.Error == null && e.Cancelled == false)
            {
                var info = (Stats.AchievementInfo)e.UserState;

                Bitmap bitmap;
                try
                {
                    using (MemoryStream stream = new())
                    {
                        stream.Write(e.Result, 0, e.Result.Length);
                        bitmap = new(stream);
                    }
                }
                catch (Exception)
                {
                    bitmap = null;
                }

                this.AddAchievementIcon(info, bitmap);
                this._AchievementListView.Invalidate();
            }

            this.DownloadNextIcon();
        }

        private void DownloadNextIcon()
        {
            if (this._IconQueue.Count == 0)
            {
                this._DownloadStatusLabel.Visible = false;
                return;
            }

            if (this._IconDownloader.IsBusy == true)
            {
                return;
            }

            this._DownloadStatusLabel.Text = $"Downloading {this._IconQueue.Count} icons...";
            this._DownloadStatusLabel.Visible = true;

            var info = this._IconQueue[0];
            this._IconQueue.RemoveAt(0);


            this._IconDownloader.DownloadDataAsync(
                new Uri(_($"https://cdn.steamstatic.com/steamcommunity/public/images/apps/{this._GameId}/{(info.IsAchieved == true ? info.IconNormal : info.IconLocked)}")),
                info);
        }

        private static string TranslateError(int id) => id switch
        {
            2 => "generic error -- this usually means you don't own the game",
            _ => _($"{id}"),
        };

        private static string GetLocalizedString(KeyValue kv, string language, string defaultValue)
        {
            var name = kv[language].AsString("");
            if (string.IsNullOrEmpty(name) == false)
            {
                return name;
            }

            if (language != "english")
            {
                name = kv["english"].AsString("");
                if (string.IsNullOrEmpty(name) == false)
                {
                    return name;
                }
            }

            name = kv.AsString("");
            if (string.IsNullOrEmpty(name) == false)
            {
                return name;
            }

            return defaultValue;
        }

        private bool LoadUserGameStatsSchema()
        {
            string path;
            try
            {
                string fileName = _($"UserGameStatsSchema_{this._GameId}.bin");
                path = API.Steam.GetInstallPath();
                path = Path.Combine(path, "appcache", "stats", fileName);
                if (File.Exists(path) == false)
                {
                    return false;
                }
            }
            catch (Exception)
            {
                return false;
            }

            var kv = KeyValue.LoadAsBinary(path);
            if (kv == null)
            {
                return false;
            }

            var currentLanguage = this._SteamClient.SteamApps008.GetCurrentGameLanguage();

            this._AchievementDefinitions.Clear();
            this._StatDefinitions.Clear();

            var stats = kv[this._GameId.ToString(CultureInfo.InvariantCulture)]["stats"];
            if (stats.Valid == false || stats.Children == null)
            {
                return false;
            }

            foreach (var stat in stats.Children)
            {
                if (stat.Valid == false)
                {
                    continue;
                }

                var rawType = stat["type_int"].Valid
                                  ? stat["type_int"].AsInteger(0)
                                  : stat["type"].AsInteger(0);
                var type = (APITypes.UserStatType)rawType;
                switch (type)
                {
                    case APITypes.UserStatType.Invalid:
                    {
                        break;
                    }

                    case APITypes.UserStatType.Integer:
                    {
                        var id = stat["name"].AsString("");
                        string name = GetLocalizedString(stat["display"]["name"], currentLanguage, id);

                        this._StatDefinitions.Add(new Stats.IntegerStatDefinition()
                        {
                            Id = stat["name"].AsString(""),
                            DisplayName = name,
                            MinValue = stat["min"].AsInteger(int.MinValue),
                            MaxValue = stat["max"].AsInteger(int.MaxValue),
                            MaxChange = stat["maxchange"].AsInteger(0),
                            IncrementOnly = stat["incrementonly"].AsBoolean(false),
                            SetByTrustedGameServer = stat["bSetByTrustedGS"].AsBoolean(false),
                            DefaultValue = stat["default"].AsInteger(0),
                            Permission = stat["permission"].AsInteger(0),
                        });
                        break;
                    }

                    case APITypes.UserStatType.Float:
                    case APITypes.UserStatType.AverageRate:
                    {
                        var id = stat["name"].AsString("");
                        string name = GetLocalizedString(stat["display"]["name"], currentLanguage, id);

                        this._StatDefinitions.Add(new Stats.FloatStatDefinition()
                        {
                            Id = stat["name"].AsString(""),
                            DisplayName = name,
                            MinValue = stat["min"].AsFloat(float.MinValue),
                            MaxValue = stat["max"].AsFloat(float.MaxValue),
                            MaxChange = stat["maxchange"].AsFloat(0.0f),
                            IncrementOnly = stat["incrementonly"].AsBoolean(false),
                            DefaultValue = stat["default"].AsFloat(0.0f),
                            Permission = stat["permission"].AsInteger(0),
                        });
                        break;
                    }

                    case APITypes.UserStatType.Achievements:
                    case APITypes.UserStatType.GroupAchievements:
                    {
                        if (stat.Children != null)
                        {
                            foreach (var bits in stat.Children.Where(
                                b => string.Compare(b.Name, "bits", StringComparison.InvariantCultureIgnoreCase) == 0))
                            {
                                if (bits.Valid == false ||
                                    bits.Children == null)
                                {
                                    continue;
                                }

                                foreach (var bit in bits.Children)
                                {
                                    string id = bit["name"].AsString("");
                                    string name = GetLocalizedString(bit["display"]["name"], currentLanguage, id);
                                    string desc = GetLocalizedString(bit["display"]["desc"], currentLanguage, "");

                                    this._AchievementDefinitions.Add(new()
                                    {
                                        Id = id,
                                        Name = name,
                                        Description = desc,
                                        IconNormal = bit["display"]["icon"].AsString(""),
                                        IconLocked = bit["display"]["icon_gray"].AsString(""),
                                        IsHidden = bit["display"]["hidden"].AsBoolean(false),
                                        Permission = bit["permission"].AsInteger(0),
                                    });
                                }
                            }
                        }

                        break;
                    }

                    default:
                    {
                        throw new InvalidOperationException("invalid stat type");
                    }
                }
            }

            return true;
        }

        private void OnUserStatsReceived(APITypes.UserStatsReceived param)
        {
            if (param.Result != 1)
            {
                this._GameStatusLabel.Text = $"Error while retrieving stats: {TranslateError(param.Result)}";
                this.EnableInput();
                return;
            }

            if (this.LoadUserGameStatsSchema() == false)
            {
                this._GameStatusLabel.Text = "Failed to load schema.";
                this.EnableInput();
                return;
            }

            try
            {
                this.GetAchievements();
            }
            catch (Exception e)
            {
                this._GameStatusLabel.Text = "Error when handling achievements retrieval.";
                this.EnableInput();
                MessageBox.Show(
                    "Error when handling achievements retrieval:\n" + e,
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            try
            {
                this.GetStatistics();
            }
            catch (Exception e)
            {
                this._GameStatusLabel.Text = "Error when handling stats retrieval.";
                this.EnableInput();
                MessageBox.Show(
                    "Error when handling stats retrieval:\n" + e,
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            this._GameStatusLabel.Text = $"Retrieved {this._AchievementListView.Items.Count} achievements and {this._StatisticsDataGridView.Rows.Count} statistics.";
            this.EnableInput();
        }

        private void RefreshStats()
        {
            this._AchievementListView.Items.Clear();
            this._StatisticsDataGridView.Rows.Clear();

            var steamId = this._SteamClient.SteamUser.GetSteamId();

            // This still triggers the UserStatsReceived callback, in addition to the callresult.
            // No need to implement callresults for the time being.
            var callHandle = this._SteamClient.SteamUserStats.RequestUserStats(steamId);
            if (callHandle == API.CallHandle.Invalid)
            {
                MessageBox.Show(this, "Failed.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            this._GameStatusLabel.Text = "Retrieving stat information...";
            this.DisableInput();
        }

        private bool _IsUpdatingAchievementList;

        private void GetAchievements()
        {
            var textSearch = this._MatchingStringTextBox.Text.Length > 0
                ? this._MatchingStringTextBox.Text
                : null;

            this._IsUpdatingAchievementList = true;

            this._AchievementListView.Items.Clear();
            this._AchievementListView.BeginUpdate();
            //this.Achievements.Clear();

            bool wantLocked = this._DisplayLockedOnlyButton.Checked == true;
            bool wantUnlocked = this._DisplayUnlockedOnlyButton.Checked == true;

            foreach (var def in this._AchievementDefinitions)
            {
                if (string.IsNullOrEmpty(def.Id) == true)
                {
                    continue;
                }

                if (this._SteamClient.SteamUserStats.GetAchievementAndUnlockTime(
                    def.Id,
                    out bool isAchieved,
                    out var unlockTime) == false)
                {
                    continue;
                }

                bool wanted = (wantLocked == false && wantUnlocked == false) || isAchieved switch
                {
                    true => wantUnlocked,
                    false => wantLocked,
                };
                if (wanted == false)
                {
                    continue;
                }

                if (textSearch != null)
                {
                    if (def.Name.IndexOf(textSearch, StringComparison.OrdinalIgnoreCase) < 0 &&
                        def.Description.IndexOf(textSearch, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }
                }

                Stats.AchievementInfo info = new()
                {
                    Id = def.Id,
                    IsAchieved = isAchieved,
                    UnlockTime = isAchieved == true && unlockTime > 0
                        ? DateTimeOffset.FromUnixTimeSeconds(unlockTime).LocalDateTime
                        : null,
                    IconNormal = string.IsNullOrEmpty(def.IconNormal) ? null : def.IconNormal,
                    IconLocked = string.IsNullOrEmpty(def.IconLocked) ? def.IconNormal : def.IconLocked,
                    Permission = def.Permission,
                    Name = def.Name,
                    Description = def.Description,
                };

                bool isProtected = (def.Permission & 3) != 0;
                ListViewItem item = new()
                {
                    Checked = isAchieved,
                    Tag = info,
                    Text = info.Name,
                    BackColor = isProtected == true
                        ? Color.FromArgb(255, 245, 246)
                        : Color.White,
                    ForeColor = isProtected == true
                        ? Color.FromArgb(126, 24, 36)
                        : Color.FromArgb(28, 28, 33),
                };

                info.Item = item;

                if (item.Text.StartsWith("#", StringComparison.InvariantCulture) == true)
                {
                    item.Text = info.Id;
                    item.SubItems.Add("");
                }
                else
                {
                    item.SubItems.Add(info.Description);
                }

                item.SubItems.Add(info.UnlockTime.HasValue == true
                    ? info.UnlockTime.Value.ToString()
                    : "");

                info.ImageIndex = 0;

                this.AddAchievementToIconQueue(info, false);
                this._AchievementListView.Items.Add(item);
            }

            this._AchievementListView.EndUpdate();
            this._IsUpdatingAchievementList = false;
            this.ResizeAchievementColumns();

            this.DownloadNextIcon();
        }

        private void GetStatistics()
        {
            this._Statistics.Clear();
            foreach (var stat in this._StatDefinitions)
            {
                if (string.IsNullOrEmpty(stat.Id) == true)
                {
                    continue;
                }

                if (stat is Stats.IntegerStatDefinition intStat)
                {
                    if (this._SteamClient.SteamUserStats.GetStatValue(intStat.Id, out int value) == false)
                    {
                        continue;
                    }
                    this._Statistics.Add(new Stats.IntStatInfo()
                    {
                        Id = intStat.Id,
                        DisplayName = intStat.DisplayName,
                        IntValue = value,
                        OriginalValue = value,
                        IsIncrementOnly = intStat.IncrementOnly,
                        Permission = intStat.Permission,
                    });
                }
                else if (stat is Stats.FloatStatDefinition floatStat)
                {
                    if (this._SteamClient.SteamUserStats.GetStatValue(floatStat.Id, out float value) == false)
                    {
                        continue;
                    }
                    this._Statistics.Add(new Stats.FloatStatInfo()
                    {
                        Id = floatStat.Id,
                        DisplayName = floatStat.DisplayName,
                        FloatValue = value,
                        OriginalValue = value,
                        IsIncrementOnly = floatStat.IncrementOnly,
                        Permission = floatStat.Permission,
                    });
                }
            }
        }

        private void ResizeAchievementColumns()
        {
            if (this._AchievementListView?.Columns == null ||
                this._AchievementListView.Columns.Count < 3)
            {
                return;
            }

            int width = this._AchievementListView.ClientSize.Width - 8;
            if (width <= 0)
            {
                return;
            }

            int unlockWidth = 170;
            int nameWidth = Math.Max(220, (int)(width * 0.30));
            int descriptionWidth = width - nameWidth - unlockWidth;

            if (descriptionWidth < 200)
            {
                descriptionWidth = 200;
                nameWidth = Math.Max(180, width - unlockWidth - descriptionWidth);
            }

            this._AchievementNameColumnHeader.Width = nameWidth;
            this._AchievementDescriptionColumnHeader.Width = Math.Max(180, descriptionWidth);
            this._AchievementUnlockTimeColumnHeader.Width = unlockWidth;
        }

        private void AddAchievementToIconQueue(Stats.AchievementInfo info, bool startDownload)
        {
            int imageIndex = this._AchievementImageList.Images.IndexOfKey(
                info.IsAchieved == true ? info.IconNormal : info.IconLocked);

            if (imageIndex >= 0)
            {
                info.ImageIndex = imageIndex;
            }
            else
            {
                this._IconQueue.Add(info);

                if (startDownload == true)
                {
                    this.DownloadNextIcon();
                }
            }
        }

        private int StoreAchievements()
        {
            if (this._AchievementListView.Items.Count == 0)
            {
                return 0;
            }

            List<Stats.AchievementInfo> achievements = new();
            foreach (ListViewItem item in this._AchievementListView.Items)
            {
                if (item.Tag is not Stats.AchievementInfo achievementInfo ||
                    achievementInfo.IsAchieved == item.Checked)
                {
                    continue;
                }

                achievementInfo.IsAchieved = item.Checked;
                achievements.Add(achievementInfo);
            }

            if (achievements.Count == 0)
            {
                return 0;
            }

            foreach (var info in achievements)
            {
                if (this._SteamClient.SteamUserStats.SetAchievement(info.Id, info.IsAchieved) == false)
                {
                    MessageBox.Show(
                        this,
                        $"An error occurred while setting the state for {info.Id}, aborting store.",
                        "Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return -1;
                }
            }

            return achievements.Count;
        }

        private int StoreStatistics()
        {
            if (this._Statistics.Count == 0)
            {
                return 0;
            }

            var statistics = this._Statistics.Where(stat => stat.IsModified == true).ToList();
            if (statistics.Count == 0)
            {
                return 0;
            }

            foreach (var stat in statistics)
            {
                if (stat is Stats.IntStatInfo intStat)
                {
                    if (this._SteamClient.SteamUserStats.SetStatValue(
                        intStat.Id,
                        intStat.IntValue) == false)
                    {
                        MessageBox.Show(
                            this,
                            $"An error occurred while setting the value for {stat.Id}, aborting store.",
                            "Error",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                        return -1;
                    }
                }
                else if (stat is Stats.FloatStatInfo floatStat)
                {
                    if (this._SteamClient.SteamUserStats.SetStatValue(
                        floatStat.Id,
                        floatStat.FloatValue) == false)
                    {
                        MessageBox.Show(
                            this,
                            $"An error occurred while setting the value for {stat.Id}, aborting store.",
                            "Error",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                        return -1;
                    }
                }
                else
                {
                    throw new InvalidOperationException("unsupported stat type");
                }
            }

            return statistics.Count;
        }

        private void DisableInput()
        {
            this._ReloadButton.Enabled = false;
            this._StoreButton.Enabled = false;
        }

        private void EnableInput()
        {
            this._ReloadButton.Enabled = true;
            this._StoreButton.Enabled = true;
        }

        private void OnTimer(object sender, EventArgs e)
        {
            this._CallbackTimer.Enabled = false;
            this._SteamClient.RunCallbacks(false);
            this._CallbackTimer.Enabled = true;
        }

        private void OnRefresh(object sender, EventArgs e)
        {
            this.RefreshStats();
        }

        private void OnLockAll(object sender, EventArgs e)
        {
            foreach (ListViewItem item in this._AchievementListView.Items)
            {
                if (IsProtectedAchievement(item) == true)
                {
                    continue;
                }

                item.Checked = false;
            }
        }

        private void OnInvertAll(object sender, EventArgs e)
        {
            foreach (ListViewItem item in this._AchievementListView.Items)
            {
                if (IsProtectedAchievement(item) == true)
                {
                    continue;
                }

                item.Checked = !item.Checked;
            }
        }

        private void OnUnlockAll(object sender, EventArgs e)
        {
            foreach (ListViewItem item in this._AchievementListView.Items)
            {
                if (IsProtectedAchievement(item) == true)
                {
                    continue;
                }

                item.Checked = true;
            }
        }

        private static bool IsProtectedAchievement(ListViewItem item)
        {
            if (item?.Tag is not Stats.AchievementInfo info)
            {
                return false;
            }

            return (info.Permission & 3) != 0;
        }

        private void OnAutoSelectCountApply(object sender, EventArgs e)
        {
            this.AutoSelectFromCountInput();
        }

        private void OnAutoSelectCountKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            e.Handled = true;
            e.SuppressKeyPress = true;
            this.AutoSelectFromCountInput();
        }

        private void AutoSelectFromCountInput()
        {
            if (int.TryParse(this._AutoSelectCountTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int requestedCount) == false ||
                requestedCount <= 0)
            {
                MessageBox.Show(
                    this,
                    "Enter a positive number.",
                    "Invalid Input",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                this._AutoSelectCountTextBox.Focus();
                this._AutoSelectCountTextBox.SelectAll();
                return;
            }

            List<ListViewItem> candidates = new();
            foreach (ListViewItem item in this._AchievementListView.Items)
            {
                if (item.Tag is not Stats.AchievementInfo info)
                {
                    continue;
                }

                if (item.Checked == true)
                {
                    continue;
                }

                if ((info.Permission & 3) != 0)
                {
                    continue;
                }

                candidates.Add(item);
            }

            int selectedCount = Math.Min(requestedCount, candidates.Count);
            if (selectedCount == 0)
            {
                MessageBox.Show(
                    this,
                    "No selectable locked achievements are available in the current view.",
                    "Information",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            this._AchievementListView.BeginUpdate();
            try
            {
                for (int i = 0; i < selectedCount; i++)
                {
                    candidates[i].Checked = true;
                }
            }
            finally
            {
                this._AchievementListView.EndUpdate();
            }

            this._GameStatusLabel.Text = $"Auto-selected {selectedCount} achievements (requested {requestedCount}, available {candidates.Count}).";
        }

        private bool Store()
        {
            if (this._SteamClient.SteamUserStats.StoreStats() == false)
            {
                MessageBox.Show(
                    this,
                    "An error occurred while storing, aborting.",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return false;
            }

            return true;
        }

        private static string GetAchievementStatusDatabasePath()
        {
            return Path.Combine(Application.StartupPath, "data", "games", "achievements", "status.json");
        }

        private bool TryGetCurrentAchievementProgress(out int unlocked, out int total)
        {
            unlocked = -1;
            total = -1;

            try
            {
                uint achievementCount = this._SteamClient.SteamUserStats.GetNumAchievements();
                if (achievementCount == 0)
                {
                    unlocked = 0;
                    total = 0;
                    return true;
                }

                int found = 0;
                int achieved = 0;
                for (uint i = 0; i < achievementCount; i++)
                {
                    string achievementId = this._SteamClient.SteamUserStats.GetAchievementName(i);
                    if (string.IsNullOrWhiteSpace(achievementId) == true)
                    {
                        continue;
                    }

                    found++;
                    if (this._SteamClient.SteamUserStats.GetAchievement(achievementId, out bool isAchieved) == true &&
                        isAchieved == true)
                    {
                        achieved++;
                    }
                }

                unlocked = achieved;
                total = found;
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private void SaveAchievementStatusDatabaseEntry()
        {
            if (this._GameId <= 0 || this._GameId > uint.MaxValue)
            {
                return;
            }

            if (this.TryGetCurrentAchievementProgress(out int unlocked, out int total) == false)
            {
                return;
            }

            uint appId = (uint)this._GameId;
            string gameName = this._SteamClient.SteamApps001.GetAppData(appId, "name");
            if (string.IsNullOrWhiteSpace(gameName) == true)
            {
                gameName = "App " + appId.ToString(CultureInfo.InvariantCulture);
            }

            try
            {
                string path = GetAchievementStatusDatabasePath();
                string directory = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(directory) == false)
                {
                    Directory.CreateDirectory(directory);
                }

                AchievementStatusDatabase database = new();
                if (File.Exists(path) == true)
                {
                    try
                    {
                        using FileStream input = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        DataContractJsonSerializer deserializer = new(typeof(AchievementStatusDatabase));
                        if (deserializer.ReadObject(input) is AchievementStatusDatabase existing)
                        {
                            database = existing;
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

                List<AchievementStatusEntry> games = database.Games?
                    .Where(item => item != null)
                    .ToList() ?? new List<AchievementStatusEntry>();

                int index = games.FindIndex(item => item.AppId == appId);
                AchievementStatusEntry entry = index >= 0 ? games[index] : new AchievementStatusEntry();
                entry.AppId = appId;
                entry.Name = gameName;
                entry.Type = string.IsNullOrWhiteSpace(entry.Type) == false ? entry.Type : "normal";
                entry.AchievementUnlocked = unlocked;
                entry.AchievementTotal = total;
                entry.HasProgress = true;
                entry.HasIncompleteAchievements = total > 0 && unlocked < total;

                if (index >= 0)
                {
                    games[index] = entry;
                }
                else
                {
                    games.Add(entry);
                }

                database.GeneratedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                database.SteamId = this._SteamClient.SteamUser.GetSteamId();
                database.ScanMode = string.IsNullOrWhiteSpace(database.ScanMode) == false
                    ? database.ScanMode
                    : "manager-update";
                database.Games = games
                    .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(item => item.AppId)
                    .ToArray();

                using FileStream output = new(path, FileMode.Create, FileAccess.Write, FileShare.Read);
                DataContractJsonSerializer serializer = new(typeof(AchievementStatusDatabase));
                serializer.WriteObject(output, database);
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

        private void OnStore(object sender, EventArgs e)
        {
            int achievements = this.StoreAchievements();
            if (achievements < 0)
            {
                this.RefreshStats();
                return;
            }

            int stats = this.StoreStatistics();
            if (stats < 0)
            {
                this.RefreshStats();
                return;
            }

            if (this.Store() == false)
            {
                this.RefreshStats();
                return;
            }

            this.SaveAchievementStatusDatabaseEntry();

            MessageBox.Show(
                this,
                $"Stored {achievements} achievements and {stats} statistics.",
                "Information",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            this.RefreshStats();
        }

        private void OnStatDataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            if (e.Context != DataGridViewDataErrorContexts.Commit)
            {
                return;
            }

            var view = (DataGridView)sender;
            if (e.Exception is Stats.StatIsProtectedException)
            {
                e.ThrowException = false;
                e.Cancel = true;
                view.Rows[e.RowIndex].ErrorText = "Stat is protected! -- you can't modify it";
            }
            else
            {
                e.ThrowException = false;
                e.Cancel = true;
                view.Rows[e.RowIndex].ErrorText = "Invalid value";
            }
        }

        private void OnStatAgreementChecked(object sender, EventArgs e)
        {
            this._StatisticsDataGridView.Columns[1].ReadOnly = this._EnableStatsEditingCheckBox.Checked == false;
        }

        private void OnStatCellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            var view = (DataGridView)sender;
            view.Rows[e.RowIndex].ErrorText = "";
        }

        private void OnResetAllStats(object sender, EventArgs e)
        {
            if (MessageBox.Show(
                "Are you absolutely sure you want to reset stats?",
                "Warning",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) == DialogResult.No)
            {
                return;
            }

            bool achievementsToo = DialogResult.Yes == MessageBox.Show(
                "Do you want to reset achievements too?",
                "Question",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (MessageBox.Show(
                "Really really sure?",
                "Warning",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Error) == DialogResult.No)
            {
                return;
            }

            if (this._SteamClient.SteamUserStats.ResetAllStats(achievementsToo) == false)
            {
                MessageBox.Show(this, "Failed.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            this.SaveAchievementStatusDatabaseEntry();
            this.RefreshStats();
        }

        private static string BuildManagerWindowTitle()
        {
            string version = Application.ProductVersion;
            if (Version.TryParse(version, out Version parsed) == true)
            {
                version = $"{parsed.Major}.{parsed.Minor}.{parsed.Build}";
            }

            return $"Steam Achievement Manager {version}";
        }

        private void OnCheckAchievement(object sender, ItemCheckEventArgs e)
        {
            if (sender != this._AchievementListView)
            {
                return;
            }

            if (this._IsUpdatingAchievementList == true)
            {
                return;
            }

            if (this._AchievementListView.Items[e.Index].Tag is not Stats.AchievementInfo info)
            {
                return;
            }

            if ((info.Permission & 3) != 0)
            {
                MessageBox.Show(
                    this,
                    "Sorry, but this is a protected achievement and cannot be managed with Steam Achievement Manager.",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                e.NewValue = e.CurrentValue;
            }
        }

        private void OnDisplayUncheckedOnly(object sender, EventArgs e)
        {
            if ((sender as ToolStripButton).Checked == true)
            {
                this._DisplayLockedOnlyButton.Checked = false;
            }

            this.GetAchievements();
        }

        private void OnDisplayCheckedOnly(object sender, EventArgs e)
        {
            if ((sender as ToolStripButton).Checked == true)
            {
                this._DisplayUnlockedOnlyButton.Checked = false;
            }

            this.GetAchievements();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            this.ResizeAchievementColumns();
        }

        private void OnFilterUpdate(object sender, KeyEventArgs e)
        {
            this.GetAchievements();
        }
    }
}

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
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Windows.Forms;
using Microsoft.Win32;
using static SAM.Game.InvariantShorthand;
using APITypes = SAM.API.Types;

namespace SAM.Game
{
    internal partial class Manager : Form
    {
        private enum ThemeMode
        {
            System,
            Light,
            Dark,
        }

        private readonly long _GameId;
        private readonly API.Client _SteamClient;

        private readonly WebClient _IconDownloader = new();

        private readonly Queue<string> _IconQueue = new();
        private readonly Dictionary<string, List<Stats.AchievementInfo>> _PendingIconConsumers =
            new(StringComparer.Ordinal);
        private readonly List<Stats.StatDefinition> _StatDefinitions = new();

        private readonly List<Stats.AchievementDefinition> _AchievementDefinitions = new();

        private readonly BindingList<Stats.StatInfo> _Statistics = new();

        private readonly API.Callbacks.UserStatsReceived _UserStatsReceivedCallback;
        private ThemeMode _ThemeMode;
        private bool _IsDarkThemeActive;
        private ToolStripRenderer _DarkToolStripRenderer;
        private ToolStripRenderer _DarkStatusStripRenderer;
        private Color _AchievementItemBackColor;
        private Color _AchievementItemForeColor;
        private Color _ProtectedAchievementBackColor;
        private Color _ProtectedAchievementForeColor;
        private Color _TabSelectedBackColor;
        private Color _TabSelectedForeColor;
        private Color _TabUnselectedBackColor;
        private Color _TabUnselectedForeColor;
        private Color _TabBorderColor;
        private readonly Random _TimedUnlockRandom = new();
        private readonly List<string> _TimedUnlockPendingAchievementIds = new();
        private readonly List<TimeSpan> _TimedUnlockIntervals = new();
        private TimeSpan _TimedUnlockTargetDuration;
        private DateTime _TimedUnlockStartedUtc;
        private DateTime _TimedUnlockNextUnlockUtc;
        private DateTime _TimedUnlockLastCountdownUpdateUtc;
        private int _TimedUnlockTotalPlanned;
        private int _TimedUnlockCompleted;
        private bool _TimedUnlockRunning;
        private bool _TimedUnlockBusy;

        //private API.Callback<APITypes.UserStatsStored> UserStatsStoredCallback;

        [DataContract]
        private sealed class PickerPreferences
        {
            [DataMember(Name = "theme_mode")]
            public string ThemeMode { get; set; }
        }

        [DataContract]
        private sealed class LegacyThemePreferences
        {
            [DataMember(Name = "theme_mode")]
            public string ThemeMode { get; set; }
        }

        private sealed class DarkToolStripColorTable : ProfessionalColorTable
        {
            private readonly Color _toolStripBackColor;
            private readonly Color _menuBackColor;
            private readonly Color _menuBorderColor;
            private readonly Color _menuSelectedColor;
            private readonly Color _menuPressedColor;
            private readonly Color _separatorColor;

            public DarkToolStripColorTable(
                Color toolStripBackColor,
                Color menuBackColor,
                Color menuBorderColor,
                Color menuSelectedColor,
                Color menuPressedColor,
                Color separatorColor)
            {
                this._toolStripBackColor = toolStripBackColor;
                this._menuBackColor = menuBackColor;
                this._menuBorderColor = menuBorderColor;
                this._menuSelectedColor = menuSelectedColor;
                this._menuPressedColor = menuPressedColor;
                this._separatorColor = separatorColor;
            }

            public override Color ToolStripDropDownBackground => this._menuBackColor;
            public override Color MenuBorder => this._menuBorderColor;
            public override Color MenuItemBorder => this._menuBorderColor;
            public override Color MenuItemSelected => this._menuSelectedColor;
            public override Color MenuItemSelectedGradientBegin => this._menuSelectedColor;
            public override Color MenuItemSelectedGradientEnd => this._menuSelectedColor;
            public override Color MenuItemPressedGradientBegin => this._menuPressedColor;
            public override Color MenuItemPressedGradientMiddle => this._menuPressedColor;
            public override Color MenuItemPressedGradientEnd => this._menuPressedColor;
            public override Color CheckBackground => this._menuSelectedColor;
            public override Color CheckSelectedBackground => this._menuSelectedColor;
            public override Color CheckPressedBackground => this._menuPressedColor;
            public override Color ImageMarginGradientBegin => this._menuBackColor;
            public override Color ImageMarginGradientMiddle => this._menuBackColor;
            public override Color ImageMarginGradientEnd => this._menuBackColor;
            public override Color SeparatorDark => this._separatorColor;
            public override Color SeparatorLight => this._separatorColor;
            public override Color ToolStripGradientBegin => this._toolStripBackColor;
            public override Color ToolStripGradientMiddle => this._toolStripBackColor;
            public override Color ToolStripGradientEnd => this._toolStripBackColor;
            public override Color ToolStripBorder => this._toolStripBackColor;
            public override Color MenuStripGradientBegin => this._toolStripBackColor;
            public override Color MenuStripGradientEnd => this._toolStripBackColor;
            public override Color StatusStripGradientBegin => this._toolStripBackColor;
            public override Color StatusStripGradientEnd => this._toolStripBackColor;
            public override Color ButtonSelectedBorder => this._menuBorderColor;
            public override Color ButtonSelectedGradientBegin => this._menuSelectedColor;
            public override Color ButtonSelectedGradientMiddle => this._menuSelectedColor;
            public override Color ButtonSelectedGradientEnd => this._menuSelectedColor;
            public override Color ButtonPressedGradientBegin => this._menuPressedColor;
            public override Color ButtonPressedGradientMiddle => this._menuPressedColor;
            public override Color ButtonPressedGradientEnd => this._menuPressedColor;
            public override Color ButtonPressedBorder => this._menuBorderColor;
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

            [DataMember(Name = "achievement_unlock_blocked")]
            public bool AchievementUnlockBlocked { get; set; }
        }

        public Manager(long gameId, API.Client client)
        {
            this._ThemeMode = LoadThemeModeFromPickerPreferences();
            this.InitializeComponent();
            this.Text = BuildManagerWindowTitle();
            this.ApplyModernTheme();
            this._AutoSelectCountTextBox.Text = "25";
            this._TimedUnlockHoursTextBox.Text = "72";
            this._MainTabControl.DrawItem += this.OnMainTabControlDrawItem;
            this.UpdateTimedUnlockControlsState();

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

        private static string GetPickerPreferencesPath()
        {
            return Path.Combine(Application.StartupPath, "sam-picker-preferences.json");
        }

        private static string GetLegacyThemePreferencesPath()
        {
            return Path.Combine(Application.StartupPath, "sam-theme-preferences.json");
        }

        private static bool TryLoadLegacyThemeModePreference(out ThemeMode mode)
        {
            mode = ThemeMode.System;
            string path = GetLegacyThemePreferencesPath();
            if (File.Exists(path) == false)
            {
                return false;
            }

            try
            {
                using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                DataContractJsonSerializer serializer = new(typeof(LegacyThemePreferences));
                if (serializer.ReadObject(stream) is not LegacyThemePreferences preferences ||
                    Enum.TryParse(preferences.ThemeMode, true, out ThemeMode parsedMode) == false)
                {
                    return false;
                }

                mode = parsedMode;
                return true;
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

            return false;
        }

        private static ThemeMode LoadThemeModeFromPickerPreferences()
        {
            string path = GetPickerPreferencesPath();
            if (File.Exists(path) == false)
            {
                return TryLoadLegacyThemeModePreference(out ThemeMode legacyMode) == true
                    ? legacyMode
                    : ThemeMode.System;
            }

            try
            {
                using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                DataContractJsonSerializer serializer = new(typeof(PickerPreferences));
                if (serializer.ReadObject(stream) is not PickerPreferences preferences ||
                    Enum.TryParse(preferences.ThemeMode, true, out ThemeMode mode) == false)
                {
                    return TryLoadLegacyThemeModePreference(out ThemeMode legacyMode) == true
                        ? legacyMode
                        : ThemeMode.System;
                }

                return mode;
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

            return TryLoadLegacyThemeModePreference(out ThemeMode fallbackMode) == true
                ? fallbackMode
                : ThemeMode.System;
        }

        private static bool IsSystemDarkThemeEnabled()
        {
            try
            {
                using RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                object value = key?.GetValue("AppsUseLightTheme");
                return value switch
                {
                    int intValue => intValue == 0,
                    long longValue => longValue == 0,
                    string textValue when int.TryParse(textValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) => parsed == 0,
                    _ => false,
                };
            }
            catch (Exception)
            {
                return false;
            }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

        private void ApplyWindowDarkTitleBar()
        {
            if (this.IsHandleCreated == false)
            {
                return;
            }

            int enabled = this._IsDarkThemeActive == true ? 1 : 0;
            try
            {
                DwmSetWindowAttribute(this.Handle, 20, ref enabled, sizeof(int));
                DwmSetWindowAttribute(this.Handle, 19, ref enabled, sizeof(int));
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }
        }

        private void ApplyScrollBarTheme(Control control)
        {
            if (control == null || control.IsHandleCreated == false)
            {
                return;
            }

            try
            {
                SetWindowTheme(control.Handle, this._IsDarkThemeActive == true ? "DarkMode_Explorer" : "Explorer", null);
            }
            catch (EntryPointNotFoundException)
            {
            }
            catch (DllNotFoundException)
            {
            }
        }

        private ThemeMode GetResolvedThemeMode()
        {
            if (this._ThemeMode == ThemeMode.System)
            {
                return IsSystemDarkThemeEnabled() == true
                    ? ThemeMode.Dark
                    : ThemeMode.Light;
            }

            return this._ThemeMode;
        }

        private void ApplyModernTheme()
        {
            this.SuspendLayout();

            this._IsDarkThemeActive = this.GetResolvedThemeMode() == ThemeMode.Dark;

            Color windowBackColor = this._IsDarkThemeActive == true ? Color.FromArgb(30, 32, 37) : Color.FromArgb(242, 242, 247);
            Color foregroundColor = this._IsDarkThemeActive == true ? Color.FromArgb(232, 235, 241) : Color.FromArgb(24, 24, 28);
            Color mutedTextColor = this._IsDarkThemeActive == true ? Color.FromArgb(168, 173, 184) : Color.FromArgb(110, 110, 116);
            Color inputBackColor = this._IsDarkThemeActive == true ? Color.FromArgb(46, 49, 56) : Color.White;
            Color toolStripBackColor = this._IsDarkThemeActive == true ? Color.FromArgb(38, 40, 46) : Color.FromArgb(252, 252, 253);
            Color statusBackColor = this._IsDarkThemeActive == true ? Color.FromArgb(34, 36, 41) : Color.FromArgb(248, 248, 250);
            Color tabPageBackColor = this._IsDarkThemeActive == true ? Color.FromArgb(34, 36, 42) : Color.FromArgb(247, 247, 250);
            Color listBackColor = this._IsDarkThemeActive == true ? Color.FromArgb(37, 39, 45) : Color.White;
            Color tabSelectedBackColor = this._IsDarkThemeActive == true ? Color.FromArgb(56, 60, 69) : Color.White;
            Color tabSelectedForeColor = this._IsDarkThemeActive == true ? Color.FromArgb(240, 244, 252) : Color.FromArgb(28, 34, 44);
            Color tabUnselectedBackColor = this._IsDarkThemeActive == true ? Color.FromArgb(39, 42, 49) : Color.FromArgb(239, 242, 248);
            Color tabUnselectedForeColor = this._IsDarkThemeActive == true ? Color.FromArgb(176, 182, 194) : Color.FromArgb(92, 101, 116);
            Color tabBorderColor = this._IsDarkThemeActive == true ? Color.FromArgb(78, 83, 95) : Color.FromArgb(207, 214, 226);
            Color listHeaderBackColor = this._IsDarkThemeActive == true ? Color.FromArgb(49, 53, 61) : Color.FromArgb(246, 247, 251);
            Color listHeaderForeColor = this._IsDarkThemeActive == true ? Color.FromArgb(236, 239, 246) : Color.FromArgb(27, 31, 39);
            Color listHeaderBorderColor = this._IsDarkThemeActive == true ? Color.FromArgb(79, 84, 96) : Color.FromArgb(218, 222, 230);

            this._AchievementItemBackColor = listBackColor;
            this._AchievementItemForeColor = foregroundColor;
            this._ProtectedAchievementBackColor = this._IsDarkThemeActive == true ? Color.FromArgb(78, 42, 48) : Color.FromArgb(255, 245, 246);
            this._ProtectedAchievementForeColor = this._IsDarkThemeActive == true ? Color.FromArgb(255, 189, 197) : Color.FromArgb(126, 24, 36);
            this._TabSelectedBackColor = tabSelectedBackColor;
            this._TabSelectedForeColor = tabSelectedForeColor;
            this._TabUnselectedBackColor = tabUnselectedBackColor;
            this._TabUnselectedForeColor = tabUnselectedForeColor;
            this._TabBorderColor = tabBorderColor;

            this.BackColor = windowBackColor;
            this.ForeColor = foregroundColor;
            this.MinimumSize = new Size(980, 620);
            if (this.ClientSize.Width < 980 || this.ClientSize.Height < 620)
            {
                this.ClientSize = new Size(1080, 700);
            }

            this._MainToolStrip.BackColor = toolStripBackColor;
            this._MainToolStrip.ForeColor = this.ForeColor;
            this._MainToolStrip.Dock = DockStyle.Top;
            this._MainToolStrip.GripStyle = ToolStripGripStyle.Hidden;
            this._MainToolStrip.Padding = new Padding(10, 6, 10, 6);
            this._MainToolStrip.AutoSize = false;
            this._MainToolStrip.Height = 44;
            if (this._IsDarkThemeActive == true)
            {
                DarkToolStripColorTable stripColorTable = new(
                    toolStripBackColor,
                    Color.FromArgb(43, 46, 54),
                    Color.FromArgb(68, 73, 84),
                    Color.FromArgb(67, 95, 150),
                    Color.FromArgb(78, 111, 173),
                    Color.FromArgb(56, 61, 70));
                this._DarkToolStripRenderer = new ToolStripProfessionalRenderer(
                    stripColorTable);
                this._DarkStatusStripRenderer = new ToolStripProfessionalRenderer(
                    new DarkToolStripColorTable(
                        statusBackColor,
                        Color.FromArgb(43, 46, 54),
                        Color.FromArgb(68, 73, 84),
                        Color.FromArgb(67, 95, 150),
                        Color.FromArgb(78, 111, 173),
                        Color.FromArgb(56, 61, 70)));
                this._MainToolStrip.Renderer = this._DarkToolStripRenderer;
                this._MainStatusStrip.Renderer = this._DarkStatusStripRenderer;
            }
            else
            {
                this._MainToolStrip.RenderMode = ToolStripRenderMode.System;
                this._MainStatusStrip.RenderMode = ToolStripRenderMode.System;
            }

            this._AchievementsToolStrip.BackColor = toolStripBackColor;
            this._AchievementsToolStrip.ForeColor = this.ForeColor;
            this._AchievementsToolStrip.GripStyle = ToolStripGripStyle.Hidden;
            this._AchievementsToolStrip.Padding = new Padding(8, 6, 8, 6);
            this._AchievementsToolStrip.AutoSize = false;
            this._AchievementsToolStrip.Height = 40;
            if (this._IsDarkThemeActive == true && this._DarkToolStripRenderer != null)
            {
                this._AchievementsToolStrip.Renderer = this._DarkToolStripRenderer;
            }
            else
            {
                this._AchievementsToolStrip.RenderMode = ToolStripRenderMode.System;
            }

            this.StyleToolStripButton(this._StoreButton, false);
            this.StyleToolStripButton(this._ReloadButton, false);
            this.StyleToolStripButton(this._ResetButton, false);
            this.StyleToolStripButton(this._LockAllButton, false);
            this.StyleToolStripButton(this._InvertAllButton, false);
            this.StyleToolStripButton(this._UnlockAllButton, false);
            this.StyleToolStripButton(this._DisplayLockedOnlyButton, true);
            this.StyleToolStripButton(this._DisplayUnlockedOnlyButton, true);
            this.StyleToolStripButton(this._AutoSelectApplyButton, true);
            this.StyleToolStripButton(this._TimedUnlockStartButton, true);
            this.StyleToolStripButton(this._TimedUnlockStopButton, true);

            this._DisplayLabel.ForeColor = mutedTextColor;
            this._MatchingStringLabel.ForeColor = mutedTextColor;
            this._AutoSelectLabel.ForeColor = mutedTextColor;
            this._TimedUnlockLabel.ForeColor = mutedTextColor;

            this._MatchingStringTextBox.AutoSize = false;
            this._MatchingStringTextBox.Size = new Size(180, 26);
            this._MatchingStringTextBox.BorderStyle = BorderStyle.FixedSingle;
            this._MatchingStringTextBox.BackColor = inputBackColor;
            this._MatchingStringTextBox.ForeColor = this.ForeColor;

            this._AutoSelectCountTextBox.AutoSize = false;
            this._AutoSelectCountTextBox.Size = new Size(70, 26);
            this._AutoSelectCountTextBox.BorderStyle = BorderStyle.FixedSingle;
            this._AutoSelectCountTextBox.BackColor = inputBackColor;
            this._AutoSelectCountTextBox.ForeColor = this.ForeColor;

            this._TimedUnlockHoursTextBox.AutoSize = false;
            this._TimedUnlockHoursTextBox.Size = new Size(70, 26);
            this._TimedUnlockHoursTextBox.BorderStyle = BorderStyle.FixedSingle;
            this._TimedUnlockHoursTextBox.BackColor = inputBackColor;
            this._TimedUnlockHoursTextBox.ForeColor = this.ForeColor;

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
            this._MainTabControl.DrawMode = TabDrawMode.OwnerDrawFixed;
            this._MainTabControl.BackColor = tabPageBackColor;
            this._MainTabControl.ForeColor = this.ForeColor;
            this._MainTabControl.Appearance = TabAppearance.Normal;

            this._AchievementsTabPage.BackColor = tabPageBackColor;
            this._AchievementsTabPage.ForeColor = this.ForeColor;
            this._AchievementsTabPage.UseVisualStyleBackColor = false;
            this._AchievementsTabPage.Padding = Padding.Empty;
            this._StatisticsTabPage.BackColor = tabPageBackColor;
            this._StatisticsTabPage.ForeColor = this.ForeColor;
            this._StatisticsTabPage.UseVisualStyleBackColor = false;
            this._StatisticsTabPage.Padding = Padding.Empty;

            this._AchievementsToolStrip.Dock = DockStyle.Top;
            this._AchievementListView.Dock = DockStyle.Fill;
            this._AchievementImageList.ColorDepth = ColorDepth.Depth32Bit;

            this._AchievementListView.BackColor = listBackColor;
            this._AchievementListView.ForeColor = this.ForeColor;
            this._AchievementListView.BorderStyle = BorderStyle.None;
            this._AchievementListView.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
            this._AchievementListView.GridLines = false;
            this._AchievementListView.UseDarkScrollBars = this._IsDarkThemeActive;
            this._AchievementListView.UseDarkColumnHeaders = this._IsDarkThemeActive;
            this._AchievementListView.SetColumnHeaderColors(
                listHeaderBackColor,
                listHeaderForeColor,
                listHeaderBorderColor);

            this._MainStatusStrip.SizingGrip = false;
            this._MainStatusStrip.Dock = DockStyle.Bottom;
            this._MainStatusStrip.BackColor = statusBackColor;
            this._MainStatusStrip.ForeColor = mutedTextColor;
            this._CountryStatusLabel.ForeColor = mutedTextColor;
            this._GameStatusLabel.ForeColor = mutedTextColor;
            this._DownloadStatusLabel.ForeColor = mutedTextColor;

            this._StatisticsDataGridView.BackgroundColor = listBackColor;
            this._StatisticsDataGridView.BorderStyle = BorderStyle.None;
            this._StatisticsDataGridView.GridColor = this._IsDarkThemeActive == true ? Color.FromArgb(76, 80, 90) : Color.FromArgb(232, 234, 239);
            this._StatisticsDataGridView.EnableHeadersVisualStyles = false;
            this._StatisticsDataGridView.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            this._StatisticsDataGridView.ColumnHeadersDefaultCellStyle.BackColor = this._IsDarkThemeActive == true ? Color.FromArgb(52, 55, 63) : Color.FromArgb(246, 247, 251);
            this._StatisticsDataGridView.ColumnHeadersDefaultCellStyle.ForeColor = this.ForeColor;
            this._StatisticsDataGridView.DefaultCellStyle.BackColor = listBackColor;
            this._StatisticsDataGridView.DefaultCellStyle.ForeColor = this.ForeColor;
            this._StatisticsDataGridView.DefaultCellStyle.SelectionBackColor = this._IsDarkThemeActive == true ? Color.FromArgb(71, 104, 168) : Color.FromArgb(223, 231, 246);
            this._StatisticsDataGridView.DefaultCellStyle.SelectionForeColor = this.ForeColor;
            this._StatisticsDataGridView.AlternatingRowsDefaultCellStyle.BackColor = this._IsDarkThemeActive == true ? Color.FromArgb(42, 45, 52) : Color.FromArgb(250, 251, 255);
            this._StatisticsDataGridView.RowHeadersVisible = false;

            this._EnableStatsEditingCheckBox.ForeColor = mutedTextColor;

            this.LayoutMainContent();
            this.ApplyWindowDarkTitleBar();
            this.ApplyScrollBarTheme(this._StatisticsDataGridView);
            this.ApplyAchievementListTheme();
            this._MainTabControl.Invalidate();
            this.UpdateTimedUnlockControlsState();
            this.ResumeLayout(true);
        }

        private void LayoutMainContent()
        {
            if (this._MainTabControl == null || this._MainToolStrip == null || this._MainStatusStrip == null)
            {
                return;
            }

            const int margin = 8;
            int top = this._MainToolStrip.Bottom + margin;
            int width = Math.Max(1, this.ClientSize.Width - (margin * 2));
            int height = Math.Max(1, this.ClientSize.Height - top - this._MainStatusStrip.Height - margin);
            this._MainTabControl.SetBounds(margin, top, width, height);
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

        private void ApplyAchievementListTheme()
        {
            foreach (ListViewItem item in this._AchievementListView.Items)
            {
                if (item.Tag is not Stats.AchievementInfo info)
                {
                    continue;
                }

                bool isProtected = (info.Permission & 3) != 0;
                item.BackColor = isProtected == true ? this._ProtectedAchievementBackColor : this._AchievementItemBackColor;
                item.ForeColor = isProtected == true ? this._ProtectedAchievementForeColor : this._AchievementItemForeColor;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            this.ApplyWindowDarkTitleBar();
            this.ApplyScrollBarTheme(this._StatisticsDataGridView);
            this._AchievementListView.UseDarkScrollBars = this._IsDarkThemeActive;
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);

            ThemeMode themeMode = LoadThemeModeFromPickerPreferences();
            if (themeMode == this._ThemeMode)
            {
                return;
            }

            this._ThemeMode = themeMode;
            this.ApplyModernTheme();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            this.StopTimedUnlock("Timed unlock stopped because window is closing.", false);
            base.OnFormClosing(e);
        }

        private void OnMainTabControlDrawItem(object sender, DrawItemEventArgs e)
        {
            if (sender is not TabControl tabControl ||
                e.Index < 0 ||
                e.Index >= tabControl.TabPages.Count)
            {
                return;
            }

            Rectangle headerBounds = new(
                0,
                0,
                tabControl.Width,
                Math.Max(tabControl.DisplayRectangle.Top + 1, 1));
            using (SolidBrush headerBrush = new(this._TabUnselectedBackColor))
            using (Pen headerBorderPen = new(this._TabBorderColor))
            {
                e.Graphics.FillRectangle(headerBrush, headerBounds);
                int lineY = Math.Max(0, headerBounds.Bottom - 1);
                e.Graphics.DrawLine(headerBorderPen, 0, lineY, Math.Max(0, tabControl.Width - 1), lineY);
            }

            Rectangle bounds = tabControl.GetTabRect(e.Index);
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

            Color backColor = selected == true ? this._TabSelectedBackColor : this._TabUnselectedBackColor;
            Color foreColor = selected == true ? this._TabSelectedForeColor : this._TabUnselectedForeColor;

            using SolidBrush backBrush = new(backColor);
            using Pen borderPen = new(this._TabBorderColor);
            e.Graphics.FillRectangle(backBrush, bounds);

            Rectangle borderRect = new(bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
            e.Graphics.DrawRectangle(borderPen, borderRect);

            TextRenderer.DrawText(
                e.Graphics,
                tabControl.TabPages[e.Index].Text,
                tabControl.Font,
                bounds,
                foreColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private void UpdateTimedUnlockControlsState()
        {
            bool running = this._TimedUnlockRunning == true;
            this._TimedUnlockStartButton.Enabled = running == false;
            this._TimedUnlockStopButton.Enabled = running == true;
            this._TimedUnlockHoursTextBox.Enabled = running == false;
        }

        private void OnTimedUnlockHoursKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            e.Handled = true;
            e.SuppressKeyPress = true;
            this.StartTimedUnlock();
        }

        private void OnStartTimedUnlock(object sender, EventArgs e)
        {
            this.StartTimedUnlock();
        }

        private void OnStopTimedUnlock(object sender, EventArgs e)
        {
            this.StopTimedUnlock("Timed unlock was stopped by user.", true);
        }

        private void StartTimedUnlock()
        {
            if (this._TimedUnlockRunning == true)
            {
                return;
            }

            if (TryParseTimedUnlockDuration(this._TimedUnlockHoursTextBox.Text, out TimeSpan targetDuration, out string parseError) == false)
            {
                MessageBox.Show(
                    this,
                    parseError ?? "Enter a valid duration.",
                    "Invalid Duration",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                this._TimedUnlockHoursTextBox.Focus();
                this._TimedUnlockHoursTextBox.SelectAll();
                return;
            }

            this.EnsureUnfilteredAchievementViewForTimedUnlock();

            List<string> candidateIds = this._AchievementListView.Items
                .OfType<ListViewItem>()
                .Where(item => item.Tag is Stats.AchievementInfo info &&
                               item.Checked == false &&
                               (info.Permission & 3) == 0)
                .Select(item => ((Stats.AchievementInfo)item.Tag).Id)
                .Where(id => string.IsNullOrWhiteSpace(id) == false)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (candidateIds.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "No unlockable locked achievements are available.",
                    "Information",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            List<TimeSpan> intervals = this.BuildTimedUnlockIntervals(candidateIds.Count, targetDuration);
            if (intervals.Count != candidateIds.Count)
            {
                MessageBox.Show(
                    this,
                    "Could not build a valid timed unlock plan.",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            this.ShuffleInPlace(candidateIds);

            this._TimedUnlockPendingAchievementIds.Clear();
            this._TimedUnlockPendingAchievementIds.AddRange(candidateIds);
            this._TimedUnlockIntervals.Clear();
            this._TimedUnlockIntervals.AddRange(intervals);

            this._TimedUnlockTargetDuration = targetDuration;
            this._TimedUnlockStartedUtc = DateTime.UtcNow;
            this._TimedUnlockCompleted = 0;
            this._TimedUnlockTotalPlanned = candidateIds.Count;
            this._TimedUnlockRunning = true;
            this._TimedUnlockBusy = false;
            this._TimedUnlockLastCountdownUpdateUtc = DateTime.MinValue;
            this._TimedUnlockNextUnlockUtc = this._TimedUnlockStartedUtc + this._TimedUnlockIntervals[0];

            this.UpdateTimedUnlockControlsState();

            this._GameStatusLabel.Text =
                $"Timed unlock started: {this._TimedUnlockTotalPlanned} achievements over {FormatDuration(targetDuration)}. " +
                $"First unlock in {FormatDuration(this._TimedUnlockIntervals[0])}.";
        }

        private void EnsureUnfilteredAchievementViewForTimedUnlock()
        {
            bool reloadNeeded = false;
            if (this._DisplayLockedOnlyButton.Checked == true || this._DisplayUnlockedOnlyButton.Checked == true)
            {
                this._DisplayLockedOnlyButton.Checked = false;
                this._DisplayUnlockedOnlyButton.Checked = false;
                reloadNeeded = true;
            }

            if (string.IsNullOrWhiteSpace(this._MatchingStringTextBox.Text) == false)
            {
                this._MatchingStringTextBox.Text = "";
                reloadNeeded = true;
            }

            if (reloadNeeded == true || this._AchievementListView.Items.Count == 0)
            {
                this.GetAchievements();
            }
        }

        private static bool TryParseTimedUnlockDuration(
            string input,
            out TimeSpan duration,
            out string error)
        {
            duration = TimeSpan.Zero;
            error = null;

            if (string.IsNullOrWhiteSpace(input) == true)
            {
                error = "Enter duration, for example: 72 (hours), 24h, 90m, or 2d.";
                return false;
            }

            string raw = input.Trim();
            string numericText = raw;
            double multiplier = 1.0; // hours by default

            char last = raw[raw.Length - 1];
            if (char.IsLetter(last) == true)
            {
                numericText = raw.Substring(0, raw.Length - 1).Trim();
                multiplier = char.ToLowerInvariant(last) switch
                {
                    'h' => 1.0,
                    'm' => 1.0 / 60.0,
                    'd' => 24.0,
                    _ => -1.0,
                };
                if (multiplier < 0)
                {
                    error = "Supported duration units are: h (hours), m (minutes), d (days).";
                    return false;
                }
            }

            if (double.TryParse(numericText, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) == false &&
                double.TryParse(numericText, NumberStyles.Float, CultureInfo.CurrentCulture, out value) == false)
            {
                error = "Duration format is invalid.";
                return false;
            }

            if (double.IsNaN(value) == true || double.IsInfinity(value) == true || value <= 0.0)
            {
                error = "Duration must be a positive number.";
                return false;
            }

            double totalHours = value * multiplier;
            if (totalHours <= 0.0)
            {
                error = "Duration is too small.";
                return false;
            }

            if (totalHours > 24.0 * 365.0)
            {
                error = "Duration is too large.";
                return false;
            }

            duration = TimeSpan.FromHours(totalHours);
            if (duration.TotalSeconds < 5.0)
            {
                error = "Duration must be at least 5 seconds.";
                return false;
            }

            return true;
        }

        private List<TimeSpan> BuildTimedUnlockIntervals(int unlockCount, TimeSpan totalDuration)
        {
            if (unlockCount <= 0)
            {
                return new List<TimeSpan>();
            }

            long totalTicks = Math.Max(1L, totalDuration.Ticks);
            long absoluteMinTicks = TimeSpan.FromSeconds(5).Ticks;
            long preferredMinTicks = TimeSpan.FromMinutes(5).Ticks;
            long preferredMaxTicks = TimeSpan.FromMinutes(40).Ticks;

            double averageTicks = totalTicks / (double)unlockCount;

            long minTicks;
            long maxTicks;
            if (averageTicks >= preferredMinTicks && averageTicks <= preferredMaxTicks)
            {
                minTicks = preferredMinTicks;
                maxTicks = preferredMaxTicks;
            }
            else if (averageTicks < preferredMinTicks)
            {
                minTicks = Math.Max(absoluteMinTicks, (long)Math.Floor(averageTicks * 0.35));
                maxTicks = Math.Max(minTicks + TimeSpan.FromSeconds(3).Ticks, (long)Math.Ceiling(averageTicks * 1.85));
            }
            else
            {
                minTicks = Math.Max(preferredMinTicks, (long)Math.Floor(averageTicks * 0.45));
                maxTicks = Math.Max(minTicks + TimeSpan.FromSeconds(10).Ticks, (long)Math.Ceiling(averageTicks * 1.75));
            }

            long averageFloor = Math.Max(absoluteMinTicks, (long)Math.Floor(averageTicks));
            if (minTicks > averageFloor)
            {
                minTicks = averageFloor;
            }

            long minFeasible = Math.Max(absoluteMinTicks, totalTicks / unlockCount);
            if (minTicks * unlockCount > totalTicks)
            {
                minTicks = minFeasible;
            }

            if (maxTicks * unlockCount < totalTicks)
            {
                maxTicks = (long)Math.Ceiling(totalTicks / (double)unlockCount);
            }

            if (maxTicks < minTicks)
            {
                maxTicks = minTicks;
            }

            List<TimeSpan> intervals = new(unlockCount);
            long remainingTicks = totalTicks;
            for (int index = 0; index < unlockCount; index++)
            {
                int remainingUnlocks = unlockCount - index;
                long minAllowedForCurrent = Math.Max(
                    minTicks,
                    remainingTicks - ((long)(remainingUnlocks - 1) * maxTicks));
                long maxAllowedForCurrent = Math.Min(
                    maxTicks,
                    remainingTicks - ((long)(remainingUnlocks - 1) * minTicks));

                if (minAllowedForCurrent > maxAllowedForCurrent)
                {
                    long forced = Math.Max(1L, remainingTicks / remainingUnlocks);
                    minAllowedForCurrent = forced;
                    maxAllowedForCurrent = forced;
                }

                long currentTicks = index == unlockCount - 1
                    ? remainingTicks
                    : this.GetRandomTickInRange(minAllowedForCurrent, maxAllowedForCurrent);

                currentTicks = Math.Max(1L, currentTicks);
                intervals.Add(TimeSpan.FromTicks(currentTicks));
                remainingTicks -= currentTicks;
            }

            if (remainingTicks != 0 && intervals.Count > 0)
            {
                int lastIndex = intervals.Count - 1;
                long adjusted = Math.Max(1L, intervals[lastIndex].Ticks + remainingTicks);
                intervals[lastIndex] = TimeSpan.FromTicks(adjusted);
            }

            return intervals;
        }

        private long GetRandomTickInRange(long minInclusive, long maxInclusive)
        {
            if (maxInclusive <= minInclusive)
            {
                return minInclusive;
            }

            double bell = (this._TimedUnlockRandom.NextDouble() + this._TimedUnlockRandom.NextDouble()) * 0.5;
            long span = maxInclusive - minInclusive;
            long offset = (long)Math.Round(span * bell, MidpointRounding.AwayFromZero);
            long value = minInclusive + offset;
            if (value < minInclusive)
            {
                return minInclusive;
            }

            return value > maxInclusive ? maxInclusive : value;
        }

        private void ShuffleInPlace(IList<string> ids)
        {
            for (int i = ids.Count - 1; i > 0; i--)
            {
                int swapIndex = this._TimedUnlockRandom.Next(i + 1);
                (ids[i], ids[swapIndex]) = (ids[swapIndex], ids[i]);
            }
        }

        private void StopTimedUnlock(string reason, bool userInitiated)
        {
            if (this._TimedUnlockRunning == false)
            {
                return;
            }

            this._TimedUnlockRunning = false;
            this._TimedUnlockBusy = false;
            this._TimedUnlockPendingAchievementIds.Clear();
            this._TimedUnlockIntervals.Clear();

            this.UpdateTimedUnlockControlsState();

            if (string.IsNullOrWhiteSpace(reason) == false)
            {
                this._GameStatusLabel.Text = reason;
                if (userInitiated == true)
                {
                    MessageBox.Show(
                        this,
                        reason,
                        "Timed Unlock",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
        }

        private void ProcessTimedUnlockTick()
        {
            if (this._TimedUnlockRunning == false || this._TimedUnlockBusy == true)
            {
                return;
            }

            DateTime utcNow = DateTime.UtcNow;
            if (utcNow < this._TimedUnlockNextUnlockUtc)
            {
                if (utcNow - this._TimedUnlockLastCountdownUpdateUtc >= TimeSpan.FromSeconds(1))
                {
                    TimeSpan left = this._TimedUnlockNextUnlockUtc - utcNow;
                    this._GameStatusLabel.Text =
                        $"Timed unlock running: {this._TimedUnlockCompleted}/{this._TimedUnlockTotalPlanned}. " +
                        $"Next unlock in {FormatDuration(left)}.";
                    this._TimedUnlockLastCountdownUpdateUtc = utcNow;
                }

                return;
            }

            this._TimedUnlockBusy = true;
            try
            {
                if (this._TimedUnlockPendingAchievementIds.Count == 0)
                {
                    this.StopTimedUnlock(
                        $"Timed unlock completed: {this._TimedUnlockCompleted}/{this._TimedUnlockTotalPlanned}.",
                        false);
                    this.RefreshStats();
                    return;
                }

                string id = this._TimedUnlockPendingAchievementIds[0];
                this._TimedUnlockPendingAchievementIds.RemoveAt(0);

                if (this.TryUnlockAchievementNow(id, out string displayName, out string error) == false)
                {
                    this.StopTimedUnlock(
                        $"Timed unlock stopped. Failed on {id}: {error}",
                        true);
                    return;
                }

                this._TimedUnlockCompleted++;
                this.UpdateAchievementVisualAfterTimedUnlock(id);
                this.SaveAchievementStatusDatabaseEntry();

                if (this._TimedUnlockCompleted >= this._TimedUnlockTotalPlanned ||
                    this._TimedUnlockPendingAchievementIds.Count == 0)
                {
                    TimeSpan elapsed = DateTime.UtcNow - this._TimedUnlockStartedUtc;
                    this.StopTimedUnlock(
                        $"Timed unlock completed: {this._TimedUnlockCompleted}/{this._TimedUnlockTotalPlanned} in {FormatDuration(elapsed)} " +
                        $"(target {FormatDuration(this._TimedUnlockTargetDuration)}).",
                        false);
                    this.RefreshStats();
                    return;
                }

                TimeSpan nextDelay = this._TimedUnlockIntervals[this._TimedUnlockCompleted];
                this._TimedUnlockNextUnlockUtc = DateTime.UtcNow + nextDelay;
                this._TimedUnlockLastCountdownUpdateUtc = DateTime.MinValue;
                this._GameStatusLabel.Text =
                    $"Unlocked: {displayName}. Progress {this._TimedUnlockCompleted}/{this._TimedUnlockTotalPlanned}. " +
                    $"Next in {FormatDuration(nextDelay)}.";
            }
            finally
            {
                this._TimedUnlockBusy = false;
            }
        }

        private bool TryUnlockAchievementNow(string achievementId, out string displayName, out string error)
        {
            displayName = achievementId;
            error = null;

            if (string.IsNullOrWhiteSpace(achievementId) == true)
            {
                error = "invalid achievement id";
                return false;
            }

            ListViewItem item = this._AchievementListView.Items
                .OfType<ListViewItem>()
                .FirstOrDefault(candidate =>
                    candidate.Tag is Stats.AchievementInfo info &&
                    string.Equals(info.Id, achievementId, StringComparison.Ordinal));
            if (item?.Tag is Stats.AchievementInfo infoFromItem)
            {
                displayName = string.IsNullOrWhiteSpace(infoFromItem.Name) == false
                    ? infoFromItem.Name
                    : achievementId;
            }

            if (this._SteamClient.SteamUserStats.GetAchievement(achievementId, out bool isAlreadyUnlocked) == true &&
                isAlreadyUnlocked == true)
            {
                return true;
            }

            if (this._SteamClient.SteamUserStats.SetAchievement(achievementId, true) == false)
            {
                error = "SetAchievement failed";
                return false;
            }

            if (this._SteamClient.SteamUserStats.StoreStats() == false)
            {
                error = "StoreStats failed";
                return false;
            }

            return true;
        }

        private void UpdateAchievementVisualAfterTimedUnlock(string achievementId)
        {
            foreach (ListViewItem item in this._AchievementListView.Items)
            {
                if (item.Tag is not Stats.AchievementInfo info ||
                    string.Equals(info.Id, achievementId, StringComparison.Ordinal) == false)
                {
                    continue;
                }

                this._IsUpdatingAchievementList = true;
                try
                {
                    item.Checked = true;
                    info.IsAchieved = true;
                    info.UnlockTime = DateTime.Now;
                    if (item.SubItems.Count > 2)
                    {
                        item.SubItems[2].Text = info.UnlockTime.Value.ToString();
                    }

                    this.AddAchievementToIconQueue(info, true);
                }
                finally
                {
                    this._IsUpdatingAchievementList = false;
                }

                this._AchievementListView.Invalidate(item.Bounds);
                return;
            }
        }

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration < TimeSpan.Zero)
            {
                duration = TimeSpan.Zero;
            }

            if (duration.TotalDays >= 1.0)
            {
                return $"{(int)duration.TotalDays}d {duration.Hours}h {duration.Minutes}m";
            }

            if (duration.TotalHours >= 1.0)
            {
                return $"{(int)duration.TotalHours}h {duration.Minutes}m";
            }

            if (duration.TotalMinutes >= 1.0)
            {
                return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
            }

            return $"{Math.Max(0, duration.Seconds)}s";
        }

        private static string GetAchievementIconKey(Stats.AchievementInfo info)
        {
            if (info == null)
            {
                return null;
            }

            string key = info.IsAchieved == true ? info.IconNormal : info.IconLocked;
            return string.IsNullOrWhiteSpace(key) == true ? null : key;
        }

        private int AddAchievementIcon(string iconKey, Image icon)
        {
            if (icon == null || string.IsNullOrWhiteSpace(iconKey) == true)
            {
                return 0;
            }

            int existingIndex = this._AchievementImageList.Images.IndexOfKey(iconKey);
            if (existingIndex >= 0)
            {
                return existingIndex;
            }

            int imageIndex = this._AchievementImageList.Images.Count;
            this._AchievementImageList.Images.Add(iconKey, icon);
            return imageIndex;
        }

        private static Bitmap DecodeAchievementBitmap(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return null;
            }

            try
            {
                using MemoryStream stream = new(data, false);
                using Image decoded = Image.FromStream(stream, true, false);
                return new Bitmap(decoded);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void InvalidateVisibleAchievementItems(IEnumerable<Stats.AchievementInfo> infos)
        {
            if (infos == null || this._AchievementListView.IsHandleCreated == false)
            {
                return;
            }

            Rectangle viewport = this._AchievementListView.ClientRectangle;
            bool invalidatedAny = false;
            foreach (var info in infos)
            {
                ListViewItem item = info?.Item;
                if (item == null || item.ListView != this._AchievementListView)
                {
                    continue;
                }

                Rectangle bounds;
                try
                {
                    bounds = item.Bounds;
                }
                catch (InvalidOperationException)
                {
                    continue;
                }

                if (bounds.IsEmpty == true ||
                    bounds.IntersectsWith(viewport) == false)
                {
                    continue;
                }

                this._AchievementListView.Invalidate(bounds);
                invalidatedAny = true;
            }

            if (invalidatedAny == false)
            {
                this._AchievementListView.Invalidate();
            }
        }

        private void ResetIconDownloads()
        {
            if (this._IconDownloader.IsBusy == true)
            {
                this._IconDownloader.CancelAsync();
            }

            this._IconQueue.Clear();
            this._PendingIconConsumers.Clear();
            this._DownloadStatusLabel.Visible = false;
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
            string iconKey = e.UserState as string;
            if (string.IsNullOrWhiteSpace(iconKey) == true)
            {
                this.DownloadNextIcon();
                return;
            }

            if (this._PendingIconConsumers.TryGetValue(iconKey, out var consumers) == false ||
                consumers == null ||
                consumers.Count == 0)
            {
                this.DownloadNextIcon();
                return;
            }

            int imageIndex = 0;
            if (e.Error == null && e.Cancelled == false)
            {
                using Bitmap bitmap = DecodeAchievementBitmap(e.Result);
                imageIndex = this.AddAchievementIcon(iconKey, bitmap);
            }

            foreach (var info in consumers)
            {
                info.ImageIndex = imageIndex;
            }

            this._PendingIconConsumers.Remove(iconKey);
            this.InvalidateVisibleAchievementItems(consumers);
            this.DownloadNextIcon();
        }

        private void DownloadNextIcon()
        {
            while (true)
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

                string iconKey = this._IconQueue.Dequeue();
                if (string.IsNullOrWhiteSpace(iconKey) == true ||
                    this._PendingIconConsumers.ContainsKey(iconKey) == false)
                {
                    continue;
                }

                this._DownloadStatusLabel.Text = $"Downloading {1 + this._IconQueue.Count} icons...";
                this._DownloadStatusLabel.Visible = true;

                try
                {
                    this._IconDownloader.DownloadDataAsync(
                        new Uri(_($"https://cdn.steamstatic.com/steamcommunity/public/images/apps/{this._GameId}/{iconKey}")),
                        iconKey);
                }
                catch (Exception)
                {
                    if (this._PendingIconConsumers.TryGetValue(iconKey, out var consumers) == true)
                    {
                        foreach (var info in consumers)
                        {
                            info.ImageIndex = 0;
                        }
                        this._PendingIconConsumers.Remove(iconKey);
                    }

                    continue;
                }

                return;
            }
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
            this.SaveAchievementStatusDatabaseEntry();
            this.EnableInput();
        }

        private void RefreshStats()
        {
            this.ResetIconDownloads();
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
            this.ResetIconDownloads();

            this._AchievementListView.BeginUpdate();
            SortOrder originalSorting = this._AchievementListView.Sorting;
            this._AchievementListView.Sorting = SortOrder.None;

            try
            {
                this._AchievementListView.Items.Clear();

                bool wantLocked = this._DisplayLockedOnlyButton.Checked == true;
                bool wantUnlocked = this._DisplayUnlockedOnlyButton.Checked == true;
                List<ListViewItem> items = new(this._AchievementDefinitions.Count);

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

                    string name = def.Name ?? def.Id;
                    string description = def.Description ?? "";
                    if (textSearch != null)
                    {
                        if (name.IndexOf(textSearch, StringComparison.OrdinalIgnoreCase) < 0 &&
                            description.IndexOf(textSearch, StringComparison.OrdinalIgnoreCase) < 0)
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
                        Name = name,
                        Description = description,
                    };

                    bool isProtected = (def.Permission & 3) != 0;
                    ListViewItem item = new()
                    {
                        Checked = isAchieved,
                        Tag = info,
                        Text = info.Name,
                        BackColor = isProtected == true
                            ? this._ProtectedAchievementBackColor
                            : this._AchievementItemBackColor,
                        ForeColor = isProtected == true
                            ? this._ProtectedAchievementForeColor
                            : this._AchievementItemForeColor,
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
                    items.Add(item);
                }

                if (items.Count > 0)
                {
                    this._AchievementListView.Items.AddRange(items.ToArray());
                }
            }
            finally
            {
                this._AchievementListView.Sorting = originalSorting;
                if (originalSorting != SortOrder.None)
                {
                    this._AchievementListView.Sort();
                }

                this._AchievementListView.EndUpdate();
                this._IsUpdatingAchievementList = false;
            }

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
            string iconKey = GetAchievementIconKey(info);
            if (string.IsNullOrWhiteSpace(iconKey) == true)
            {
                info.ImageIndex = 0;
                return;
            }

            int imageIndex = this._AchievementImageList.Images.IndexOfKey(iconKey);
            if (imageIndex >= 0)
            {
                info.ImageIndex = imageIndex;
                return;
            }

            if (this._PendingIconConsumers.TryGetValue(iconKey, out var consumers) == false)
            {
                consumers = new List<Stats.AchievementInfo>();
                this._PendingIconConsumers.Add(iconKey, consumers);
                this._IconQueue.Enqueue(iconKey);
            }

            consumers.Add(info);

            if (startDownload == true)
            {
                this.DownloadNextIcon();
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
            this.ProcessTimedUnlockTick();
            this._CallbackTimer.Enabled = true;
        }

        private void OnRefresh(object sender, EventArgs e)
        {
            this.StopTimedUnlock("Timed unlock stopped because stats were refreshed.", false);
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

        private bool? GetCurrentAchievementUnlockBlockedStatus()
        {
            int total = 0;
            int protectedCount = 0;

            foreach (Stats.AchievementDefinition definition in this._AchievementDefinitions)
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.Id) == true)
                {
                    continue;
                }

                total++;
                if ((definition.Permission & 3) != 0)
                {
                    protectedCount++;
                }
            }

            if (total <= 0)
            {
                return null;
            }

            return protectedCount >= total;
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

            bool? unlockBlocked = this.GetCurrentAchievementUnlockBlockedStatus();

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
                entry.HasProgress = unlocked >= 0 && total >= 0;
                entry.HasIncompleteAchievements = entry.HasProgress == true && total > 0 && unlocked < total;
                if (unlockBlocked.HasValue == true)
                {
                    entry.AchievementUnlockBlocked = unlockBlocked.Value;
                }

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
            this.StopTimedUnlock("Timed unlock stopped because reset was requested.", false);

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
            int metadataSeparator = version?.IndexOf('+') ?? -1;
            if (metadataSeparator > 0)
            {
                version = version.Substring(0, metadataSeparator);
            }
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
            this.LayoutMainContent();
            this.ResizeAchievementColumns();
        }

        private void OnFilterUpdate(object sender, KeyEventArgs e)
        {
            this.GetAchievements();
        }
    }
}

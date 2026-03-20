using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace SAM.Picker
{
    internal partial class GamePicker
    {
        private const int GamesFilterPresetCustom = 0;
        private const int GamesFilterPresetAllTypes = 1;
        private const int GamesFilterPresetGamesOnly = 2;
        private const int GamesFilterPresetGamesAndDemos = 3;
        private const int GamesFilterPresetGamesAndMods = 4;
        private const int GamesFilterPresetGamesDemosAndMods = 5;
        private const int GamesFilterPresetIncompleteOnly = 6;

        private StatCard _OverviewTotalGamesCard;
        private StatCard _OverviewVisibleGamesCard;
        private StatCard _OverviewGroupsCard;
        private StatCard _OverviewIncompleteCard;
        private StatCard _OverviewAchievementsCard;
        private StatCard _OverviewUnlockedCard;
        private StatCard _OverviewScannedCard;
        private CardSurface _OverviewCompletionCard;
        private Label _OverviewCompletionLabel;
        private Label _OverviewCompletionValueLabel;
        private Label _OverviewCompletionDetailsLabel;
        private ProgressBarControl _OverviewCompletionProgressBar;

        private PremiumInput _GamesSearchInput;
        private PremiumDropdown _GamesFilterDropdown;
        private PremiumDropdown _GamesSortDropdown;
        private PremiumButton _GamesViewGridButton;
        private PremiumButton _GamesViewListButton;
        private PremiumButton _GamesCheckAllButton;
        private PremiumButton _GamesUnlockAllButton;
        private PremiumButton _GamesUnlockSelectedButton;
        private PremiumButton _GamesClearGroupFilterButton;
        private PremiumButton _GamesRefreshButton;
        private LoadingSkeleton _GamesLoadingSkeleton;
        private EmptyState _GamesEmptyState;
        private bool _SuppressGamesToolbarSync;

        private FlowLayoutPanel _GroupsPageCardsPanel;
        private EmptyState _GroupsPageEmptyState;
        private Label _GroupsPageHintLabel;
        private string _GroupsPageSelectedGroupId;
        private PremiumButton _GroupsPageCreateButton;
        private PremiumButton _GroupsPageManageButton;
        private PremiumButton _GroupsPageRenameButton;
        private PremiumButton _GroupsPageDeleteButton;
        private PremiumButton _GroupsPageAddSelectedButton;
        private PremiumButton _GroupsPageRemoveSelectedButton;
        private PremiumButton _GroupsPageShowOnlyButton;
        private PremiumButton _GroupsPageRunButton;
        private PremiumButton _GroupsPageClearFilterButton;

        private FlowLayoutPanel _SettingsSectionsFlow;
        private PremiumDropdown _SettingsThemeDropdown;
        private PremiumDropdown _SettingsViewDropdown;
        private PremiumDropdown _SettingsSortDropdown;
        private PremiumToggle _SettingsIncompleteToggle;
        private PremiumToggle _SettingsSidebarToggle;
        private PremiumButton _SettingsConfigureAuthButton;
        private Label _SettingsApiKeyStatusLabel;
        private Label _SettingsCookieStatusLabel;
        private bool _SuppressSettingsSync;
        private bool _AdjustingSettingsCardsLayout;

        private Panel CreateOverviewPage()
        {
            PageContainer container = new()
            {
                Padding = new Padding(24, 16, 24, 16),
            };

            FlowLayoutPanel grid = new()
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Padding = new Padding(0, 0, 0, 8),
            };

            this._OverviewTotalGamesCard = this.CreateOverviewStatCard("\uE8EF", "Total Games");
            this._OverviewVisibleGamesCard = this.CreateOverviewStatCard("\uE8A7", "Visible Games");
            this._OverviewGroupsCard = this.CreateOverviewStatCard("\uE902", "Groups");
            this._OverviewIncompleteCard = this.CreateOverviewStatCard("\uE7BA", "Incomplete");
            this._OverviewAchievementsCard = this.CreateOverviewStatCard("\uEB51", "Achievements");
            this._OverviewUnlockedCard = this.CreateOverviewStatCard("\uE73E", "Unlocked");
            this._OverviewScannedCard = this.CreateOverviewStatCard("\uE9D2", "Scanned");

            this._OverviewCompletionCard = new CardSurface()
            {
                Width = 440,
                Height = 124,
                HoverElevation = false,
                Padding = new Padding(14, 12, 14, 12),
            };
            this._OverviewCompletionLabel = new Label()
            {
                Dock = DockStyle.Top,
                Height = 22,
                Text = "Completion",
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point),
                Tag = "secondary",
            };
            this._OverviewCompletionValueLabel = new Label()
            {
                Dock = DockStyle.Top,
                Height = 40,
                Text = "0%",
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Semibold", 24f, FontStyle.Bold, GraphicsUnit.Point),
                Tag = "primary",
            };
            this._OverviewCompletionProgressBar = new ProgressBarControl()
            {
                Dock = DockStyle.Top,
                Height = 8,
            };
            this._OverviewCompletionDetailsLabel = new Label()
            {
                Dock = DockStyle.Top,
                Height = 20,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point),
                Tag = "muted",
            };

            this._OverviewCompletionCard.Controls.Add(this._OverviewCompletionDetailsLabel);
            this._OverviewCompletionCard.Controls.Add(this._OverviewCompletionProgressBar);
            this._OverviewCompletionCard.Controls.Add(this._OverviewCompletionValueLabel);
            this._OverviewCompletionCard.Controls.Add(this._OverviewCompletionLabel);

            grid.Controls.Add(this._OverviewTotalGamesCard);
            grid.Controls.Add(this._OverviewVisibleGamesCard);
            grid.Controls.Add(this._OverviewGroupsCard);
            grid.Controls.Add(this._OverviewIncompleteCard);
            grid.Controls.Add(this._OverviewAchievementsCard);
            grid.Controls.Add(this._OverviewUnlockedCard);
            grid.Controls.Add(this._OverviewScannedCard);
            grid.Controls.Add(this._OverviewCompletionCard);

            container.Controls.Add(grid);
            return container;
        }

        private StatCard CreateOverviewStatCard(string iconGlyph, string title)
        {
            return new StatCard()
            {
                IconGlyph = iconGlyph,
                Title = title,
                Value = "0",
            };
        }

        private Panel CreateGamesPage()
        {
            PageContainer container = new()
            {
                Padding = new Padding(24, 14, 0, 16),
            };

            Panel listHost = new()
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(0),
            };

            if (this._GameListView.Parent != null)
            {
                this._GameListView.Parent.Controls.Remove(this._GameListView);
            }

            this._GameListView.Dock = DockStyle.Fill;
            this._GameListView.Visible = true;

            this._GamesEmptyState = new EmptyState()
            {
                Visible = false,
                Title = "No games to display",
                Detail = "Try a different filter or search.",
            };

            this._GamesLoadingSkeleton = new LoadingSkeleton()
            {
                Visible = false,
            };

            listHost.Controls.Add(this._GamesLoadingSkeleton);
            listHost.Controls.Add(this._GamesEmptyState);
            listHost.Controls.Add(this._GameListView);

            container.Controls.Add(listHost);

            this.InitializeGamesTopbarControls();
            return container;
        }

        private void InitializeGamesTopbarControls()
        {
            this._GamesSearchInput = new PremiumInput()
            {
                Width = 196,
                Placeholder = "Search games",
                IconGlyph = "\uE721",
            };
            this._GamesSearchInput.TextValueChanged += this.OnGamesSearchTextChanged;

            this._GamesFilterDropdown = new PremiumDropdown()
            {
                Width = 164,
                IconGlyph = "\uE16E",
            };
            this._GamesFilterDropdown.SetItems(
                new[]
                {
                    "Custom",
                    "All types",
                    "Games only",
                    "Games + demos",
                    "Games + mods",
                    "Games + demos + mods",
                    "Incomplete only",
                },
                GamesFilterPresetAllTypes);
            this._GamesFilterDropdown.SelectedIndexChanged += this.OnGamesFilterPresetChanged;

            this._GamesSortDropdown = new PremiumDropdown()
            {
                Width = 170,
                IconGlyph = "\uE8CB",
            };
            this._GamesSortDropdown.SetItems(
                new[]
                {
                    "Sort: Name A-Z",
                    "Sort: Name Z-A",
                    "Sort: Progress low-high",
                    "Sort: Progress high-low",
                },
                0);
            this._GamesSortDropdown.SelectedIndexChanged += this.OnGamesSortChanged;

            this._GamesViewGridButton = new PremiumButton()
            {
                Width = 34,
                Height = 32,
                Text = "",
                IconGlyph = "\uECA5",
                Style = PremiumButtonStyle.Ghost,
            };
            this._GamesViewGridButton.Click += (_, _) =>
            {
                this.ApplyViewMode(GameViewMode.Grid, true);
                this.RefreshGames();
                this.SyncGamesToolbarState();
            };

            this._GamesViewListButton = new PremiumButton()
            {
                Width = 34,
                Height = 32,
                Text = "",
                IconGlyph = "\uE8A5",
                Style = PremiumButtonStyle.Ghost,
            };
            this._GamesViewListButton.Click += (_, _) =>
            {
                this.ApplyViewMode(GameViewMode.List, true);
                this.RefreshGames();
                this.SyncGamesToolbarState();
            };

            this._GamesCheckAllButton = new PremiumButton()
            {
                Width = 92,
                Height = 32,
                Text = "Check All",
                IconGlyph = "\uE9D2",
                Style = PremiumButtonStyle.Secondary,
            };
            this._GamesCheckAllButton.Click += this.OnCheckAllAchievements;

            this._GamesUnlockAllButton = new PremiumButton()
            {
                Width = 96,
                Height = 32,
                Text = "Unlock All",
                IconGlyph = "\uE73E",
                Style = PremiumButtonStyle.Secondary,
            };
            this._GamesUnlockAllButton.Click += this.OnUnlockAllAchievements;

            this._GamesUnlockSelectedButton = new PremiumButton()
            {
                Width = 136,
                Height = 32,
                Text = "Unlock Selected",
                IconGlyph = "\uE73E",
                Style = PremiumButtonStyle.Secondary,
                Visible = false,
            };
            this._GamesUnlockSelectedButton.Click += this.OnUnlockSelectedAchievements;

            this._GamesClearGroupFilterButton = new PremiumButton()
            {
                Width = 120,
                Height = 32,
                Text = "All Games",
                IconGlyph = "\uE894",
                Style = PremiumButtonStyle.Ghost,
                Visible = false,
            };
            this._GamesClearGroupFilterButton.Click += (_, _) =>
            {
                this.ClearGroupFilter();
                this.RefreshGames();
                this.SyncGamesToolbarState();
            };

            this._GamesRefreshButton = new PremiumButton()
            {
                Width = 92,
                Height = 32,
                Text = "Refresh",
                IconGlyph = "\uE72C",
                Style = PremiumButtonStyle.Secondary,
            };
            this._GamesRefreshButton.Click += this.OnRefresh;
        }

        private Panel CreateGroupsPage()
        {
            PageContainer container = new()
            {
                Padding = new Padding(24, 14, 0, 16),
            };

            this._GroupsPageHintLabel = new Label()
            {
                Dock = DockStyle.Top,
                Height = 28,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point),
                Tag = "muted",
                Text = "Groups let you batch games and run them for a total duration.",
            };

            this._GroupsPageCardsPanel = new FlowLayoutPanel()
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                WrapContents = true,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(0, 6, 0, 8),
            };
            this._GroupsPageCardsPanel.HandleCreated += (_, _) => this.ApplyScrollableControlTheme(this._GroupsPageCardsPanel);

            this._GroupsPageEmptyState = new EmptyState()
            {
                Visible = false,
                Title = "No groups yet",
                Detail = "Create a group, add games, then run it in one batch.",
            };

            this.InitializeGroupsPageTopbarButtons();

            container.Controls.Add(this._GroupsPageEmptyState);
            container.Controls.Add(this._GroupsPageCardsPanel);
            container.Controls.Add(this._GroupsPageHintLabel);
            return container;
        }

        private void InitializeGroupsPageTopbarButtons()
        {
            this._GroupsPageCreateButton = new PremiumButton()
            {
                Width = 108,
                Height = 32,
                Text = "Create",
                IconGlyph = "\uE710",
                Style = PremiumButtonStyle.Primary,
            };
            this._GroupsPageCreateButton.Click += (_, _) =>
            {
                this.CreateGroup(this);
                this.RefreshGroupsPage();
            };

            this._GroupsPageManageButton = new PremiumButton()
            {
                Width = 108,
                Height = 32,
                Text = "Manage",
                IconGlyph = "\uE70F",
                Style = PremiumButtonStyle.Secondary,
            };
            this._GroupsPageManageButton.Click += (_, _) =>
            {
                this.ShowGroupManagerDialog(this._GroupsPageSelectedGroupId);
                this.RefreshGroupsPage();
            };

            this._GroupsPageRenameButton = new PremiumButton()
            {
                Width = 104,
                Height = 32,
                Text = "Rename",
                IconGlyph = "\uE70F",
                Style = PremiumButtonStyle.Secondary,
            };
            this._GroupsPageRenameButton.Click += (_, _) =>
            {
                if (this.TryGetGroupsPageSelectedGroup(out GameGroup group) == false)
                {
                    return;
                }

                this.RenameGroup(this, group);
                this.RefreshGroupsPage();
            };

            this._GroupsPageDeleteButton = new PremiumButton()
            {
                Width = 96,
                Height = 32,
                Text = "Delete",
                IconGlyph = "\uE74D",
                Style = PremiumButtonStyle.Secondary,
            };
            this._GroupsPageDeleteButton.Click += (_, _) =>
            {
                if (this.TryGetGroupsPageSelectedGroup(out GameGroup group) == false)
                {
                    return;
                }

                this.DeleteGroup(this, group);
                this.RefreshGroupsPage();
            };

            this._GroupsPageAddSelectedButton = new PremiumButton()
            {
                Width = 136,
                Height = 32,
                Text = "Add Selected",
                IconGlyph = "\uECC8",
                Style = PremiumButtonStyle.Secondary,
            };
            this._GroupsPageAddSelectedButton.Click += (_, _) =>
            {
                if (this.TryGetGroupsPageSelectedGroup(out GameGroup group) == false)
                {
                    return;
                }

                this.AddGamesToGroup(this, group, this.GetSelectedVisibleGameIdsInDisplayOrder());
                this.RefreshGroupsPage();
            };

            this._GroupsPageRemoveSelectedButton = new PremiumButton()
            {
                Width = 154,
                Height = 32,
                Text = "Remove Selected",
                IconGlyph = "\uE74D",
                Style = PremiumButtonStyle.Secondary,
            };
            this._GroupsPageRemoveSelectedButton.Click += (_, _) =>
            {
                if (this.TryGetGroupsPageSelectedGroup(out GameGroup group) == false)
                {
                    return;
                }

                this.RemoveGamesFromGroup(this, group, this.GetSelectedVisibleGameIdsInDisplayOrder());
                this.RefreshGroupsPage();
            };

            this._GroupsPageShowOnlyButton = new PremiumButton()
            {
                Width = 114,
                Height = 32,
                Text = "Show Only",
                IconGlyph = "\uE8A7",
                Style = PremiumButtonStyle.Secondary,
            };
            this._GroupsPageShowOnlyButton.Click += (_, _) =>
            {
                if (this.TryGetGroupsPageSelectedGroup(out GameGroup group) == false)
                {
                    return;
                }

                this.ApplyGroupFilter(group);
                this.RefreshGroupsPage();
                this.ShowDashboardPage(DashboardPage.Games);
            };

            this._GroupsPageRunButton = new PremiumButton()
            {
                Width = 88,
                Height = 32,
                Text = "Run",
                IconGlyph = "\uE768",
                Style = PremiumButtonStyle.Primary,
            };
            this._GroupsPageRunButton.Click += (_, _) =>
            {
                if (this.TryGetGroupsPageSelectedGroup(out GameGroup group) == false)
                {
                    return;
                }

                this.RunGroup(this, group);
                this.UpdateGroupsPageState();
            };

            this._GroupsPageClearFilterButton = new PremiumButton()
            {
                Width = 126,
                Height = 32,
                Text = "Clear Filter",
                IconGlyph = "\uE894",
                Style = PremiumButtonStyle.Ghost,
            };
            this._GroupsPageClearFilterButton.Click += (_, _) =>
            {
                this.ClearGroupFilter();
                this.RefreshGroupsPage();
            };
        }

        private Panel CreateSettingsPage()
        {
            PageContainer container = new()
            {
                Padding = new Padding(24, 14, 0, 16),
            };

            this._SettingsSectionsFlow = new FlowLayoutPanel()
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(0, 6, 8, 8),
            };
            this._SettingsSectionsFlow.HandleCreated += (_, _) => this.ApplyScrollableControlTheme(this._SettingsSectionsFlow);
            this._SettingsSectionsFlow.SizeChanged += (_, _) => this.ResizeSettingsCards();
            this._SettingsSectionsFlow.Layout += (_, _) =>
            {
                if (this._AdjustingSettingsCardsLayout == false)
                {
                    this.ResizeSettingsCards();
                }
            };

            this.BuildSettingsSections();
            container.Controls.Add(this._SettingsSectionsFlow);
            this.ResizeSettingsCards();
            return container;
        }

        private void BuildSettingsSections()
        {
            this._SettingsThemeDropdown = new PremiumDropdown()
            {
                Width = 180,
                Height = 32,
                IconGlyph = "\uE706",
            };
            this._SettingsThemeDropdown.SetItems(new[] { "System", "Light", "Dark" }, 0);
            this._SettingsThemeDropdown.SelectedIndexChanged += (_, _) =>
            {
                if (this._SuppressSettingsSync == true)
                {
                    return;
                }

                ThemeMode mode = this._SettingsThemeDropdown.SelectedIndex switch
                {
                    1 => ThemeMode.Light,
                    2 => ThemeMode.Dark,
                    _ => ThemeMode.System,
                };
                this.ApplyThemeMode(mode, true);
            };

            this._SettingsSidebarToggle = new PremiumToggle()
            {
                Width = 50,
                Height = 28,
            };
            this._SettingsSidebarToggle.CheckedChanged += (_, _) =>
            {
                if (this._SuppressSettingsSync == true)
                {
                    return;
                }

                this._NavigationSidebarCollapsedByViewport = false;
                this.SetNavigationSidebarCollapsed(this._SettingsSidebarToggle.Checked, true);
            };

            CardSurface appearanceCard = this.CreateSettingsCard("Appearance", "Theme and layout behavior");
            AddSettingsRow(appearanceCard, this.CreateSettingsRow("Theme", "System, light or dark mode", this._SettingsThemeDropdown));
            AddSettingsRow(appearanceCard, this.CreateSettingsRow("Compact Sidebar", "Collapse the sidebar by default", this._SettingsSidebarToggle));
            this._SettingsSectionsFlow.Controls.Add(appearanceCard);

            this._SettingsViewDropdown = new PremiumDropdown()
            {
                Width = 180,
                Height = 32,
                IconGlyph = "\uE8A5",
            };
            this._SettingsViewDropdown.SetItems(new[] { "Grid", "List" }, 0);
            this._SettingsViewDropdown.SelectedIndexChanged += (_, _) =>
            {
                if (this._SuppressSettingsSync == true)
                {
                    return;
                }

                GameViewMode mode = this._SettingsViewDropdown.SelectedIndex == 1
                    ? GameViewMode.List
                    : GameViewMode.Grid;
                this.ApplyViewMode(mode, true);
                this.RefreshGames();
                this.SyncGamesToolbarState();
            };

            this._SettingsSortDropdown = new PremiumDropdown()
            {
                Width = 220,
                Height = 32,
                IconGlyph = "\uE8CB",
            };
            this._SettingsSortDropdown.SetItems(
                new[]
                {
                    "Name A-Z",
                    "Name Z-A",
                    "Progress low-high",
                    "Progress high-low",
                },
                0);
            this._SettingsSortDropdown.SelectedIndexChanged += (_, _) =>
            {
                if (this._SuppressSettingsSync == true)
                {
                    return;
                }

                GameSortMode mode = this._SettingsSortDropdown.SelectedIndex switch
                {
                    1 => GameSortMode.NameDescending,
                    2 => GameSortMode.AchievementAscending,
                    3 => GameSortMode.AchievementDescending,
                    _ => GameSortMode.NameAscending,
                };
                this.ApplySortMode(mode, true, true);
                this.SyncGamesToolbarState();
            };

            this._SettingsIncompleteToggle = new PremiumToggle()
            {
                Width = 50,
                Height = 28,
            };
            this._SettingsIncompleteToggle.CheckedChanged += (_, _) =>
            {
                if (this._SuppressSettingsSync == true)
                {
                    return;
                }

                this._FilterIncompleteAchievementsMenuItem.Checked = this._SettingsIncompleteToggle.Checked;
                this.OnFilterUpdate(this._FilterIncompleteAchievementsMenuItem, EventArgs.Empty);
                this.SyncGamesToolbarState();
            };

            CardSurface gamesCard = this.CreateSettingsCard("Games", "Default game listing behavior");
            AddSettingsRow(gamesCard, this.CreateSettingsRow("Default View", "Grid or list presentation", this._SettingsViewDropdown));
            AddSettingsRow(gamesCard, this.CreateSettingsRow("Sort Order", "Sorting used when opening the app", this._SettingsSortDropdown));
            AddSettingsRow(gamesCard, this.CreateSettingsRow("Incomplete Only", "Keep only games with incomplete achievements", this._SettingsIncompleteToggle));
            this._SettingsSectionsFlow.Controls.Add(gamesCard);

            this._SettingsApiKeyStatusLabel = new Label()
            {
                Width = 240,
                Height = 28,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point),
                Tag = "secondary",
            };
            this._SettingsCookieStatusLabel = new Label()
            {
                Width = 240,
                Height = 28,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point),
                Tag = "secondary",
            };
            this._SettingsConfigureAuthButton = new PremiumButton()
            {
                Width = 188,
                Height = 32,
                Text = "Configure Steam Auth",
                IconGlyph = "\uE72E",
                Style = PremiumButtonStyle.Secondary,
            };
            this._SettingsConfigureAuthButton.Click += this.OnConfigureAuth;

            CardSurface authCard = this.CreateSettingsCard("Steam", "Authentication and API integration");
            AddSettingsRow(authCard, this.CreateSettingsRow("Web API Key", "Used for fast achievement scanning", this._SettingsApiKeyStatusLabel));
            AddSettingsRow(authCard, this.CreateSettingsRow("Community Cookies", "Required for private profile fallback", this._SettingsCookieStatusLabel));
            AddSettingsRow(authCard, this.CreateSettingsRow("Authentication", "Manage Steam community credentials", this._SettingsConfigureAuthButton));
            this._SettingsSectionsFlow.Controls.Add(authCard);
        }

        private CardSurface CreateSettingsCard(string title, string subtitle)
        {
            CardSurface card = new()
            {
                Width = 860,
                Height = 162,
                HoverElevation = false,
                Padding = new Padding(16, 12, 16, 12),
                Margin = new Padding(0, 0, 0, 14),
            };

            Label titleLabel = new()
            {
                Dock = DockStyle.Top,
                Height = 26,
                Text = title,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Semibold", 12f, FontStyle.Bold, GraphicsUnit.Point),
                Tag = "primary",
            };
            Label subtitleLabel = new()
            {
                Dock = DockStyle.Top,
                Height = 24,
                Text = subtitle,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point),
                Tag = "muted",
                AutoEllipsis = true,
            };

            Panel rowsHost = new()
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 8, 0, 0),
                Tag = "rows-host",
            };

            card.Controls.Add(rowsHost);
            card.Controls.Add(subtitleLabel);
            card.Controls.Add(titleLabel);
            return card;
        }

        private static void AddSettingsRow(CardSurface card, Control row)
        {
            if (card == null || row == null)
            {
                return;
            }

            Control rowsHost = card.Controls.Cast<Control>().FirstOrDefault(control =>
                string.Equals(control.Tag as string, "rows-host", StringComparison.Ordinal));
            if (rowsHost == null)
            {
                return;
            }

            rowsHost.Controls.Add(row);
            row.BringToFront();
            UpdateSettingsCardHeight(card);
        }

        private Panel CreateSettingsRow(string title, string detail, Control valueControl)
        {
            Panel row = new()
            {
                Dock = DockStyle.Top,
                Height = 52,
                Padding = new Padding(0),
            };

            Panel textHost = new()
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(0),
            };
            Label titleLabel = new()
            {
                Dock = DockStyle.Top,
                Height = 24,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = title,
                Font = new Font("Segoe UI", 9.6f, FontStyle.Regular, GraphicsUnit.Point),
                Tag = "primary",
                AutoEllipsis = true,
            };
            Label detailLabel = new()
            {
                Dock = DockStyle.Top,
                Height = 22,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = detail,
                Font = new Font("Segoe UI", 8.6f, FontStyle.Regular, GraphicsUnit.Point),
                Tag = "muted",
                AutoEllipsis = true,
            };
            textHost.Controls.Add(detailLabel);
            textHost.Controls.Add(titleLabel);

            Panel valueHost = new()
            {
                Dock = DockStyle.Right,
                Width = Math.Max(128, Math.Min(206, valueControl.Width + 8)),
                Padding = new Padding(0),
                Name = "_SettingsValueHost",
            };
            valueHost.Tag = Math.Max(96, valueControl.Width + 8);
            valueControl.Dock = DockStyle.None;
            valueControl.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            valueControl.Margin = new Padding(0);
            valueHost.Controls.Add(valueControl);
            void AlignValueControl()
            {
                valueControl.Left = Math.Max(0, valueHost.ClientSize.Width - valueControl.Width);
                valueControl.Top = Math.Max(0, (valueHost.ClientSize.Height - valueControl.Height) / 2);
            }

            valueHost.SizeChanged += (_, _) => AlignValueControl();
            AlignValueControl();

            row.Controls.Add(textHost);
            row.Controls.Add(valueHost);

            return row;
        }

        private static void UpdateSettingsCardHeight(CardSurface card)
        {
            if (card == null)
            {
                return;
            }

            Control rowsHost = card.Controls.Cast<Control>().FirstOrDefault(control =>
                string.Equals(control.Tag as string, "rows-host", StringComparison.Ordinal));
            if (rowsHost == null)
            {
                return;
            }

            int rowsHeight = 0;
            foreach (Control row in rowsHost.Controls)
            {
                rowsHeight += row.Height + row.Margin.Vertical;
            }

            int contentHeight = card.Padding.Vertical + 26 + 24 + 8 + rowsHeight + 4;
            card.Height = Math.Max(168, contentHeight);
        }

        private void ResizeSettingsCards()
        {
            if (this._SettingsSectionsFlow == null || this._AdjustingSettingsCardsLayout == true)
            {
                return;
            }

            this._AdjustingSettingsCardsLayout = true;
            try
            {
                int usableWidth = this._SettingsSectionsFlow.DisplayRectangle.Width;
                if (usableWidth <= 0)
                {
                    usableWidth = this._SettingsSectionsFlow.ClientSize.Width;
                }

                int availableWidth = Math.Max(
                    260,
                    usableWidth - this._SettingsSectionsFlow.Padding.Horizontal);
                int width = Math.Max(300, availableWidth - 2);
                foreach (Control control in this._SettingsSectionsFlow.Controls)
                {
                    if (control is CardSurface card)
                    {
                        card.Width = width;
                        this.AdjustSettingsCardValueHosts(card);
                    }
                }
            }
            finally
            {
                this._AdjustingSettingsCardsLayout = false;
            }
        }

        private void AdjustSettingsCardValueHosts(CardSurface card)
        {
            if (card == null)
            {
                return;
            }

            Control rowsHost = card.Controls.Cast<Control>().FirstOrDefault(control =>
                string.Equals(control.Tag as string, "rows-host", StringComparison.Ordinal));
            if (rowsHost == null)
            {
                return;
            }

            int rowWidth = Math.Max(220, card.ClientSize.Width - card.Padding.Horizontal);
            foreach (Panel row in rowsHost.Controls.OfType<Panel>())
            {
                Panel valueHost = row.Controls
                    .OfType<Panel>()
                    .FirstOrDefault(panel => string.Equals(panel.Name, "_SettingsValueHost", StringComparison.Ordinal));
                if (valueHost == null)
                {
                    continue;
                }

                int preferredWidth = valueHost.Tag is int preferred && preferred > 0
                    ? preferred
                    : valueHost.Width;
                Control valueControl = valueHost.Controls.Count > 0 ? valueHost.Controls[0] : null;
                int minTextWidth = valueControl is PremiumToggle ? 170 : 120;
                int maxValueWidth = Math.Max(88, rowWidth - minTextWidth);
                valueHost.Width = Math.Max(88, Math.Min(preferredWidth, maxValueWidth));
            }
        }

        private void OnGamesSearchTextChanged(object sender, EventArgs e)
        {
            if (this._SuppressGamesToolbarSync == true || this._GamesSearchInput == null)
            {
                return;
            }

            string text = this._GamesSearchInput.Text ?? "";
            if (string.Equals(this._SearchGameTextBox.Text, text, StringComparison.Ordinal) == true)
            {
                return;
            }

            this._SearchGameTextBox.Text = text;
            this.OnFilterUpdate(this._SearchGameTextBox, EventArgs.Empty);
            this.UpdateGamesEmptyState();
        }

        private void OnGamesFilterPresetChanged(object sender, EventArgs e)
        {
            if (this._SuppressGamesToolbarSync == true || this._GamesFilterDropdown == null)
            {
                return;
            }

            this._SuppressGamesToolbarSync = true;
            try
            {
                switch (this._GamesFilterDropdown.SelectedIndex)
                {
                    case GamesFilterPresetAllTypes:
                        this._FilterGamesMenuItem.Checked = true;
                        this._FilterDemosMenuItem.Checked = true;
                        this._FilterModsMenuItem.Checked = true;
                        this._FilterJunkMenuItem.Checked = true;
                        this._FilterIncompleteAchievementsMenuItem.Checked = false;
                        break;

                    case GamesFilterPresetGamesOnly:
                        this._FilterGamesMenuItem.Checked = true;
                        this._FilterDemosMenuItem.Checked = false;
                        this._FilterModsMenuItem.Checked = false;
                        this._FilterJunkMenuItem.Checked = false;
                        this._FilterIncompleteAchievementsMenuItem.Checked = false;
                        break;

                    case GamesFilterPresetGamesAndDemos:
                        this._FilterGamesMenuItem.Checked = true;
                        this._FilterDemosMenuItem.Checked = true;
                        this._FilterModsMenuItem.Checked = false;
                        this._FilterJunkMenuItem.Checked = false;
                        this._FilterIncompleteAchievementsMenuItem.Checked = false;
                        break;

                    case GamesFilterPresetGamesAndMods:
                        this._FilterGamesMenuItem.Checked = true;
                        this._FilterDemosMenuItem.Checked = false;
                        this._FilterModsMenuItem.Checked = true;
                        this._FilterJunkMenuItem.Checked = false;
                        this._FilterIncompleteAchievementsMenuItem.Checked = false;
                        break;

                    case GamesFilterPresetGamesDemosAndMods:
                        this._FilterGamesMenuItem.Checked = true;
                        this._FilterDemosMenuItem.Checked = true;
                        this._FilterModsMenuItem.Checked = true;
                        this._FilterJunkMenuItem.Checked = false;
                        this._FilterIncompleteAchievementsMenuItem.Checked = false;
                        break;

                    case GamesFilterPresetIncompleteOnly:
                        this._FilterGamesMenuItem.Checked = true;
                        this._FilterDemosMenuItem.Checked = true;
                        this._FilterModsMenuItem.Checked = true;
                        this._FilterJunkMenuItem.Checked = true;
                        this._FilterIncompleteAchievementsMenuItem.Checked = true;
                        break;
                }
            }
            finally
            {
                this._SuppressGamesToolbarSync = false;
            }

            this.OnFilterUpdate(this._GamesFilterDropdown, EventArgs.Empty);
            this.SyncGamesToolbarState();
        }

        private void OnGamesSortChanged(object sender, EventArgs e)
        {
            if (this._SuppressGamesToolbarSync == true || this._GamesSortDropdown == null)
            {
                return;
            }

            GameSortMode mode = this._GamesSortDropdown.SelectedIndex switch
            {
                1 => GameSortMode.NameDescending,
                2 => GameSortMode.AchievementAscending,
                3 => GameSortMode.AchievementDescending,
                _ => GameSortMode.NameAscending,
            };

            this.ApplySortMode(mode, true, true);
            this.SyncSettingsPageControls();
        }

        private void ApplyThemeToPages()
        {
            DashboardThemeTokens theme = this._DashboardTheme;
            if (theme == null)
            {
                return;
            }

            if (this._OverviewPagePanel != null)
            {
                this._OverviewPagePanel.BackColor = theme.SurfaceBackground;
            }
            if (this._GamesPagePanel != null)
            {
                this._GamesPagePanel.BackColor = theme.SurfaceBackground;
            }
            if (this._GroupsPagePanel != null)
            {
                this._GroupsPagePanel.BackColor = theme.SurfaceBackground;
            }
            if (this._SettingsPagePanel != null)
            {
                this._SettingsPagePanel.BackColor = theme.SurfaceBackground;
            }
            if (this._SettingsSectionsFlow != null)
            {
                this._SettingsSectionsFlow.BackColor = theme.SurfaceBackground;
            }

            this._OverviewTotalGamesCard?.ApplyTheme(theme);
            this._OverviewVisibleGamesCard?.ApplyTheme(theme);
            this._OverviewGroupsCard?.ApplyTheme(theme);
            this._OverviewIncompleteCard?.ApplyTheme(theme);
            this._OverviewAchievementsCard?.ApplyTheme(theme);
            this._OverviewUnlockedCard?.ApplyTheme(theme);
            this._OverviewScannedCard?.ApplyTheme(theme);
            this._OverviewCompletionCard?.ApplyTheme(theme);
            this._OverviewCompletionProgressBar?.ApplyTheme(theme);

            this._GamesSearchInput?.ApplyTheme(theme);
            this._GamesFilterDropdown?.ApplyTheme(theme);
            this._GamesSortDropdown?.ApplyTheme(theme);
            this._GamesViewGridButton?.ApplyTheme(theme);
            this._GamesViewListButton?.ApplyTheme(theme);
            this._GamesCheckAllButton?.ApplyTheme(theme);
            this._GamesUnlockAllButton?.ApplyTheme(theme);
            this._GamesUnlockSelectedButton?.ApplyTheme(theme);
            this._GamesClearGroupFilterButton?.ApplyTheme(theme);
            this._GamesRefreshButton?.ApplyTheme(theme);
            this._GamesLoadingSkeleton?.ApplyTheme(theme);
            this._GamesEmptyState?.ApplyTheme(theme);

            this._GroupsPageCreateButton?.ApplyTheme(theme);
            this._GroupsPageManageButton?.ApplyTheme(theme);
            this._GroupsPageRenameButton?.ApplyTheme(theme);
            this._GroupsPageDeleteButton?.ApplyTheme(theme);
            this._GroupsPageAddSelectedButton?.ApplyTheme(theme);
            this._GroupsPageRemoveSelectedButton?.ApplyTheme(theme);
            this._GroupsPageShowOnlyButton?.ApplyTheme(theme);
            this._GroupsPageRunButton?.ApplyTheme(theme);
            this._GroupsPageClearFilterButton?.ApplyTheme(theme);
            this._GroupsPageEmptyState?.ApplyTheme(theme);

            this._SettingsThemeDropdown?.ApplyTheme(theme);
            this._SettingsViewDropdown?.ApplyTheme(theme);
            this._SettingsSortDropdown?.ApplyTheme(theme);
            this._SettingsIncompleteToggle?.ApplyTheme(theme);
            this._SettingsSidebarToggle?.ApplyTheme(theme);
            this._SettingsConfigureAuthButton?.ApplyTheme(theme);

            this.ApplyLabelThemeRecursive(this._OverviewPagePanel, theme);
            this.ApplyLabelThemeRecursive(this._GroupsPagePanel, theme);
            this.ApplyLabelThemeRecursive(this._SettingsPagePanel, theme);

            this.RefreshGroupsPage();
            this.SyncGamesToolbarState();
            this.SyncSettingsPageControls();
        }

        private void ApplyLabelThemeRecursive(Control root, DashboardThemeTokens theme)
        {
            if (root == null)
            {
                return;
            }

            foreach (Control control in root.Controls)
            {
                if (control is Label label)
                {
                    string role = label.Tag as string;
                    label.ForeColor = role switch
                    {
                        "muted" => theme.TextMuted,
                        "secondary" => theme.TextSecondary,
                        _ => theme.TextPrimary,
                    };
                }

                this.ApplyLabelThemeRecursive(control, theme);
            }
        }

        private void SyncGamesToolbarState()
        {
            if (this._GamesSearchInput == null ||
                this._GamesFilterDropdown == null ||
                this._GamesSortDropdown == null ||
                this._GamesViewGridButton == null ||
                this._GamesViewListButton == null)
            {
                return;
            }

            this._SuppressGamesToolbarSync = true;
            try
            {
                this._GamesSearchInput.Text = this._SearchGameTextBox.Text ?? "";
                this._GamesFilterDropdown.SelectedIndex = this.GetCurrentGamesFilterPreset();
                this._GamesSortDropdown.SelectedIndex = this._SortMode switch
                {
                    GameSortMode.NameDescending => 1,
                    GameSortMode.AchievementAscending => 2,
                    GameSortMode.AchievementDescending => 3,
                    _ => 0,
                };
                this._GamesViewGridButton.Selected = this._ViewMode == GameViewMode.Grid;
                this._GamesViewListButton.Selected = this._ViewMode == GameViewMode.List;
            }
            finally
            {
                this._SuppressGamesToolbarSync = false;
            }

            this.UpdateGamesEmptyState();
            this.UpdateUnlockSelectedButtonVisibility();
            this.UpdateGamesGroupFilterButtonState();
        }

        private void UpdateGamesGroupFilterButtonState()
        {
            if (this._GamesClearGroupFilterButton == null)
            {
                return;
            }

            GameGroup activeFilter = this.GetActiveGroupFilter();
            this._GamesClearGroupFilterButton.Visible = activeFilter != null;
            this._GamesClearGroupFilterButton.Enabled = activeFilter != null;
            this._GamesClearGroupFilterButton.Text = activeFilter != null
                ? $"All Games ({activeFilter.Name})"
                : "All Games";
        }

        private int GetCurrentGamesFilterPreset()
        {
            bool games = this._FilterGamesMenuItem.Checked == true;
            bool demos = this._FilterDemosMenuItem.Checked == true;
            bool mods = this._FilterModsMenuItem.Checked == true;
            bool junk = this._FilterJunkMenuItem.Checked == true;
            bool incomplete = this._FilterIncompleteAchievementsMenuItem.Checked == true;

            if (incomplete == true &&
                games == true &&
                demos == true &&
                mods == true &&
                junk == true)
            {
                return GamesFilterPresetIncompleteOnly;
            }

            if (incomplete == false &&
                games == true &&
                demos == true &&
                mods == true &&
                junk == true)
            {
                return GamesFilterPresetAllTypes;
            }

            if (incomplete == false &&
                games == true &&
                demos == false &&
                mods == false &&
                junk == false)
            {
                return GamesFilterPresetGamesOnly;
            }

            if (incomplete == false &&
                games == true &&
                demos == true &&
                mods == false &&
                junk == false)
            {
                return GamesFilterPresetGamesAndDemos;
            }

            if (incomplete == false &&
                games == true &&
                demos == false &&
                mods == true &&
                junk == false)
            {
                return GamesFilterPresetGamesAndMods;
            }

            if (incomplete == false &&
                games == true &&
                demos == true &&
                mods == true &&
                junk == false)
            {
                return GamesFilterPresetGamesDemosAndMods;
            }

            return GamesFilterPresetCustom;
        }

        private void UpdateGamesEmptyState()
        {
            if (this._GamesEmptyState == null)
            {
                return;
            }

            bool loading = this._GamesLoadingSkeleton?.Visible == true;
            bool showEmpty = loading == false && this._FilteredGames.Count == 0;
            this._GamesEmptyState.Visible = showEmpty;
            if (showEmpty == false)
            {
                return;
            }

            if (this._Games.Count == 0)
            {
                this._GamesEmptyState.Title = "No games loaded";
                this._GamesEmptyState.Detail = "Use Refresh to load games from Steam.";
            }
            else if (string.IsNullOrWhiteSpace(this._SearchGameTextBox.Text) == false)
            {
                this._GamesEmptyState.Title = "No match for this search";
                this._GamesEmptyState.Detail = "Try a different keyword or clear filters.";
            }
            else if (string.IsNullOrWhiteSpace(this._ActiveGroupFilterId) == false)
            {
                this._GamesEmptyState.Title = "No games in active group filter";
                this._GamesEmptyState.Detail = "Edit the group or clear the active filter.";
            }
            else
            {
                this._GamesEmptyState.Title = "No games match active filters";
                this._GamesEmptyState.Detail = "Adjust filter settings from the Games topbar.";
            }

            this._GamesEmptyState.BringToFront();
        }

        private void UpdateGamesLoadingState(bool loading)
        {
            if (this._GamesLoadingSkeleton == null)
            {
                return;
            }

            this._GamesLoadingSkeleton.Visible = loading;
            if (loading == true)
            {
                this._GamesLoadingSkeleton.BringToFront();
            }
            else
            {
                this.UpdateGamesEmptyState();
                if (this._GamesEmptyState?.Visible == true)
                {
                    this._GamesEmptyState.BringToFront();
                }
                else
                {
                    this._GameListView.BringToFront();
                }
            }
        }

        private bool TryGetGroupsPageSelectedGroup(out GameGroup group)
        {
            group = null;
            if (string.IsNullOrWhiteSpace(this._GroupsPageSelectedGroupId) == true)
            {
                MessageBox.Show(
                    this,
                    "Select a group first.",
                    "Info",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return false;
            }

            group = this._Groups.FirstOrDefault(item =>
                string.Equals(item.Id, this._GroupsPageSelectedGroupId, StringComparison.Ordinal));
            if (group != null)
            {
                return true;
            }

            MessageBox.Show(
                this,
                "Selected group no longer exists.",
                "Info",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return false;
        }

        private void RefreshGroupsPage()
        {
            if (this._GroupsPageCardsPanel == null)
            {
                return;
            }

            string selectedId = this._GroupsPageSelectedGroupId;
            if (string.IsNullOrWhiteSpace(selectedId) == true ||
                this._Groups.Any(item => string.Equals(item.Id, selectedId, StringComparison.Ordinal)) == false)
            {
                selectedId = this.GetActiveGroupFilter()?.Id;
            }
            if (string.IsNullOrWhiteSpace(selectedId) == true)
            {
                selectedId = this._Groups.FirstOrDefault()?.Id;
            }
            this._GroupsPageSelectedGroupId = selectedId;

            this._GroupsPageCardsPanel.SuspendLayout();
            this._GroupsPageCardsPanel.Controls.Clear();
            foreach (GameGroup group in this._Groups
                         .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                         .ThenBy(item => item.Id, StringComparer.Ordinal))
            {
                bool selected = string.Equals(group.Id, this._GroupsPageSelectedGroupId, StringComparison.Ordinal);
                bool activeFilter = string.Equals(group.Id, this._ActiveGroupFilterId, StringComparison.Ordinal);
                this._GroupsPageCardsPanel.Controls.Add(this.CreateGroupPageCard(group, selected, activeFilter));
            }
            this._GroupsPageCardsPanel.ResumeLayout(true);

            bool hasGroups = this._Groups.Count > 0;
            this._GroupsPageEmptyState.Visible = hasGroups == false;
            if (hasGroups == false)
            {
                this._GroupsPageEmptyState.BringToFront();
            }

            this.UpdateGroupsPageState();
        }

        private CardSurface CreateGroupPageCard(GameGroup group, bool selected, bool activeFilter)
        {
            CardSurface card = new()
            {
                Width = 286,
                Height = 162,
                HoverElevation = true,
                Padding = new Padding(14, 12, 14, 12),
                Margin = new Padding(0, 0, 14, 14),
            };

            if (this._DashboardTheme != null)
            {
                if (selected == true)
                {
                    card.FillColorOverride = DashboardDrawing.Blend(
                        this._DashboardTheme.PanelBackground,
                        this._DashboardTheme.Accent,
                        this._DashboardTheme.IsDark == true ? 0.18f : 0.08f);
                    card.BorderColorOverride = this._DashboardTheme.ActiveBorder;
                }
                else if (activeFilter == true)
                {
                    card.BorderColorOverride = this._DashboardTheme.ProgressFill;
                }
            }

            Label nameLabel = new()
            {
                Dock = DockStyle.Top,
                Height = 24,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold, GraphicsUnit.Point),
                Text = group.Name,
                Tag = "primary",
            };
            Label detailsLabel = new()
            {
                Dock = DockStyle.Top,
                Height = 22,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point),
                Text = $"{group.AppIds.Count}/{MaxGamesPerGroup} games",
                Tag = "muted",
            };
            Label previewLabel = new()
            {
                Dock = DockStyle.Top,
                Height = 40,
                TextAlign = ContentAlignment.TopLeft,
                Font = new Font("Segoe UI", 8.6f, FontStyle.Regular, GraphicsUnit.Point),
                Tag = "muted",
            };

            string preview = string.Join(
                ", ",
                group.AppIds
                    .Where(appId => appId != 0)
                    .Take(3)
                    .Select(this.GetGameDisplayName));
            previewLabel.Text = string.IsNullOrWhiteSpace(preview) == true
                ? "No games added yet."
                : preview;

            FlowLayoutPanel actionRow = new()
            {
                Dock = DockStyle.Bottom,
                Height = 34,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0),
                Margin = new Padding(0),
                BackColor = this._DashboardTheme?.PanelBackground ?? this.BackColor,
            };
            PremiumButton showOnlyButton = new()
            {
                Width = 108,
                Height = 28,
                Text = activeFilter == true ? "Filtered" : "Show Only",
                IconGlyph = "\uE8A7",
                Style = activeFilter == true ? PremiumButtonStyle.Primary : PremiumButtonStyle.Secondary,
            };
            showOnlyButton.Click += (_, _) =>
            {
                this._GroupsPageSelectedGroupId = group.Id;
                this.ApplyGroupFilter(group);
                this.RefreshGroupsPage();
                this.ShowDashboardPage(DashboardPage.Games);
            };

            PremiumButton runButton = new()
            {
                Width = 72,
                Height = 28,
                Text = "Run",
                IconGlyph = "\uE768",
                Style = PremiumButtonStyle.Secondary,
            };
            runButton.Click += (_, _) =>
            {
                this._GroupsPageSelectedGroupId = group.Id;
                this.RunGroup(this, group);
                this.UpdateGroupsPageState();
            };

            actionRow.Controls.Add(showOnlyButton);
            actionRow.Controls.Add(runButton);

            card.Click += (_, _) =>
            {
                this._GroupsPageSelectedGroupId = group.Id;
                this.RefreshGroupsPage();
            };
            nameLabel.Click += (_, _) =>
            {
                this._GroupsPageSelectedGroupId = group.Id;
                this.RefreshGroupsPage();
            };
            detailsLabel.Click += (_, _) =>
            {
                this._GroupsPageSelectedGroupId = group.Id;
                this.RefreshGroupsPage();
            };
            previewLabel.Click += (_, _) =>
            {
                this._GroupsPageSelectedGroupId = group.Id;
                this.RefreshGroupsPage();
            };

            card.Controls.Add(actionRow);
            card.Controls.Add(previewLabel);
            card.Controls.Add(detailsLabel);
            card.Controls.Add(nameLabel);

            if (this._DashboardTheme != null)
            {
                card.ApplyTheme(this._DashboardTheme);
                showOnlyButton.ApplyTheme(this._DashboardTheme);
                runButton.ApplyTheme(this._DashboardTheme);
            }
            this.ApplyLabelThemeRecursive(card, this._DashboardTheme ?? DashboardThemeTokens.CreateDark(Color.FromArgb(106, 173, 255)));
            return card;
        }

        private void UpdateGroupsPageState()
        {
            if (this._GroupsPageHintLabel == null)
            {
                return;
            }

            GameGroup selectedGroup = this._Groups.FirstOrDefault(item =>
                string.Equals(item.Id, this._GroupsPageSelectedGroupId, StringComparison.Ordinal));
            bool hasSelectedGroup = selectedGroup != null;
            bool hasGameSelection = this.GetSelectedVisibleGameIds().Count > 0;
            GameGroup activeFilter = this.GetActiveGroupFilter();

            if (this._GroupsPageCreateButton != null)
            {
                this._GroupsPageCreateButton.Enabled = true;
            }
            if (this._GroupsPageManageButton != null)
            {
                this._GroupsPageManageButton.Enabled = true;
            }
            if (this._GroupsPageRenameButton != null)
            {
                this._GroupsPageRenameButton.Enabled = hasSelectedGroup;
            }
            if (this._GroupsPageDeleteButton != null)
            {
                this._GroupsPageDeleteButton.Enabled = hasSelectedGroup;
            }
            if (this._GroupsPageAddSelectedButton != null)
            {
                this._GroupsPageAddSelectedButton.Enabled = hasSelectedGroup && hasGameSelection;
            }
            if (this._GroupsPageRemoveSelectedButton != null)
            {
                this._GroupsPageRemoveSelectedButton.Enabled = hasSelectedGroup && hasGameSelection;
            }
            if (this._GroupsPageShowOnlyButton != null)
            {
                this._GroupsPageShowOnlyButton.Enabled = hasSelectedGroup;
            }
            if (this._GroupsPageRunButton != null)
            {
                this._GroupsPageRunButton.Enabled = hasSelectedGroup && this.IsGroupRunBusy == false;
            }
            if (this._GroupsPageClearFilterButton != null)
            {
                this._GroupsPageClearFilterButton.Enabled = activeFilter != null;
                this._GroupsPageClearFilterButton.Text = activeFilter != null
                    ? $"Clear ({activeFilter.Name})"
                    : "Clear Filter";
            }

            if (selectedGroup != null)
            {
                bool isActiveFilter = string.Equals(selectedGroup.Id, this._ActiveGroupFilterId, StringComparison.Ordinal);
                this._GroupsPageHintLabel.Text =
                    $"Selected: {selectedGroup.Name}  |  {selectedGroup.AppIds.Count}/{MaxGamesPerGroup} games" +
                    (isActiveFilter == true ? "  |  active filter" : "");
            }
            else if (this._Groups.Count == 0)
            {
                this._GroupsPageHintLabel.Text = "No groups yet. Create a group to get started.";
            }
            else
            {
                this._GroupsPageHintLabel.Text = "Select a group card to edit, filter, or run.";
            }
        }

        private void SyncSettingsPageControls()
        {
            if (this._SettingsThemeDropdown == null ||
                this._SettingsViewDropdown == null ||
                this._SettingsSortDropdown == null ||
                this._SettingsIncompleteToggle == null ||
                this._SettingsSidebarToggle == null ||
                this._SettingsApiKeyStatusLabel == null ||
                this._SettingsCookieStatusLabel == null)
            {
                return;
            }

            this._SuppressSettingsSync = true;
            try
            {
                this._SettingsThemeDropdown.SelectedIndex = this._ThemeMode switch
                {
                    ThemeMode.Light => 1,
                    ThemeMode.Dark => 2,
                    _ => 0,
                };
                this._SettingsViewDropdown.SelectedIndex = this._ViewMode == GameViewMode.List ? 1 : 0;
                this._SettingsSortDropdown.SelectedIndex = this._SortMode switch
                {
                    GameSortMode.NameDescending => 1,
                    GameSortMode.AchievementAscending => 2,
                    GameSortMode.AchievementDescending => 3,
                    _ => 0,
                };
                this._SettingsIncompleteToggle.Checked = this._FilterIncompleteAchievementsMenuItem.Checked == true;
                this._SettingsSidebarToggle.Checked = this._NavigationSidebarCollapsed;
            }
            finally
            {
                this._SuppressSettingsSync = false;
            }

            this._SettingsApiKeyStatusLabel.Text = string.IsNullOrWhiteSpace(this._SteamWebApiKey) == true
                ? "Not configured"
                : "Configured";
            int cookieCount = this._SteamCommunityCookies?.Count ?? 0;
            this._SettingsCookieStatusLabel.Text = cookieCount > 0
                ? $"{cookieCount} cookies loaded"
                : "Not configured";
        }

        private void UpdateOverviewStats(
            int totalGames,
            int visibleGames,
            int scannedGames,
            int incompleteGames,
            long totalAchievements,
            long unlockedAchievements,
            string completionText)
        {
            if (this._OverviewTotalGamesCard == null)
            {
                return;
            }

            this._OverviewTotalGamesCard.Value = totalGames.ToString("N0", CultureInfo.CurrentCulture);
            this._OverviewVisibleGamesCard.Value = visibleGames.ToString("N0", CultureInfo.CurrentCulture);
            this._OverviewGroupsCard.Value = this._Groups.Count.ToString("N0", CultureInfo.CurrentCulture);
            this._OverviewIncompleteCard.Value = incompleteGames.ToString("N0", CultureInfo.CurrentCulture);
            this._OverviewAchievementsCard.Value = totalAchievements.ToString("N0", CultureInfo.CurrentCulture);
            this._OverviewUnlockedCard.Value = unlockedAchievements.ToString("N0", CultureInfo.CurrentCulture);
            this._OverviewScannedCard.Value = scannedGames.ToString("N0", CultureInfo.CurrentCulture);

            double completionRatio = totalAchievements > 0
                ? Math.Max(0.0, Math.Min(1.0, (double)unlockedAchievements / totalAchievements))
                : 0.0;
            this._OverviewCompletionValueLabel.Text = completionText;
            this._OverviewCompletionProgressBar.Value = completionRatio;
            this._OverviewCompletionDetailsLabel.Text =
                $"{scannedGames.ToString("N0", CultureInfo.CurrentCulture)} scanned  |  " +
                $"{unlockedAchievements.ToString("N0", CultureInfo.CurrentCulture)}/" +
                $"{totalAchievements.ToString("N0", CultureInfo.CurrentCulture)} unlocked";
        }
    }
}

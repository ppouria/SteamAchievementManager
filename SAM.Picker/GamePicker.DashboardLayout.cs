using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace SAM.Picker
{
    internal partial class GamePicker
    {
        private enum DashboardPage
        {
            Overview,
            Games,
            Groups,
            Settings,
        }

        private const int ExpandedSidebarWidth = 240;
        private const int CollapsedSidebarWidth = 72;
        private const double SidebarAnimationDurationMilliseconds = 220.0;
        private const string ProjectRepositoryUrl = "https://github.com/ppouria/SteamAchievementManager";

        private DashboardThemeTokens _DashboardTheme;

        private Panel _NavigationRootPanel;
        private Sidebar _NavigationSidebarPanel;
        private Panel _NavigationMainPanel;
        private Panel _NavigationTopbarPanel;
        private Panel _NavigationPagesPanel;

        private Panel _GamesPagePanel;
        private Panel _OverviewPagePanel;
        private Panel _GroupsPagePanel;
        private Panel _SettingsPagePanel;

        private Label _TopbarTitleLabel;
        private Label _TopbarSubtitleLabel;
        private FlowLayoutPanel _TopbarPrimaryActionsPanel;
        private FlowLayoutPanel _TopbarSecondaryActionsPanel;

        private PremiumButton _NavigationToggleButton;
        private Label _NavigationBrandLabel;
        private Label _NavigationVersionLabel;
        private PremiumButton _NavigationGitHubButton;
        private PremiumButton _NavigationStarButton;

        private NavItem _NavigationOverviewItem;
        private NavItem _NavigationGamesItem;
        private NavItem _NavigationGroupsItem;
        private NavItem _NavigationSettingsItem;

        private PremiumTooltip _NavigationToolTip;
        private Timer _NavigationSidebarAnimationTimer;
        private int _NavigationSidebarAnimationStartWidth;
        private int _NavigationSidebarAnimationTargetWidth;
        private long _NavigationSidebarAnimationStartTimestamp;
        private bool _NavigationSidebarCollapsed;
        private bool _NavigationSidebarCollapsedByViewport;
        private DashboardPage _CurrentDashboardPage;

        private Color _NavigationCardBackColor;
        private Color _NavigationCardBorderColor;
        private Color _NavigationButtonActiveBackColor;
        private Color _NavigationButtonActiveBorderColor;
        private Color _NavigationMutedTextColor;
        private Color _NavigationAccentColor;

        private void InitializeModernNavigationLayout()
        {
            if (this._NavigationRootPanel != null)
            {
                return;
            }

            this.SuspendLayout();

            this._PickerToolStrip.Visible = false;
            this._SummaryCardPanel.Visible = false;

            if (this._PickerToolStrip.Parent != null)
            {
                this._PickerToolStrip.Parent.Controls.Remove(this._PickerToolStrip);
            }

            if (this._SummaryCardPanel.Parent != null)
            {
                this._SummaryCardPanel.Parent.Controls.Remove(this._SummaryCardPanel);
            }

            this._NavigationRootPanel = new Panel()
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(0),
            };

            this._NavigationSidebarPanel = this.CreateNavigationSidebar();
            this._NavigationSidebarPanel.Dock = DockStyle.Left;
            this._NavigationSidebarPanel.Width = ExpandedSidebarWidth;

            this._NavigationMainPanel = new Panel()
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(0),
            };

            this._NavigationTopbarPanel = this.CreateNavigationTopbar();
            this._NavigationTopbarPanel.Dock = DockStyle.Top;

            this._NavigationPagesPanel = new Panel()
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(0),
            };

            this._GamesPagePanel = this.CreateGamesPage();
            this._OverviewPagePanel = this.CreateOverviewPage();
            this._GroupsPagePanel = this.CreateGroupsPage();
            this._SettingsPagePanel = this.CreateSettingsPage();

            this._NavigationPagesPanel.Controls.Add(this._SettingsPagePanel);
            this._NavigationPagesPanel.Controls.Add(this._GroupsPagePanel);
            this._NavigationPagesPanel.Controls.Add(this._GamesPagePanel);
            this._NavigationPagesPanel.Controls.Add(this._OverviewPagePanel);

            this._NavigationMainPanel.Controls.Add(this._NavigationPagesPanel);
            this._NavigationMainPanel.Controls.Add(this._NavigationTopbarPanel);

            this._NavigationRootPanel.Controls.Add(this._NavigationMainPanel);
            this._NavigationRootPanel.Controls.Add(this._NavigationSidebarPanel);

            this.Controls.Add(this._NavigationRootPanel);
            this._NavigationRootPanel.BringToFront();
            this._PickerStatusStrip.BringToFront();

            this._NavigationToolTip = new PremiumTooltip(this._DashboardTheme ?? DashboardThemeTokens.CreateDark(Color.FromArgb(106, 173, 255)));

            this._NavigationSidebarAnimationTimer = new Timer()
            {
                Interval = 15,
            };
            this._NavigationSidebarAnimationTimer.Tick += this.OnSidebarAnimationTick;

            this.Resize += this.OnDashboardResize;

            this.SetNavigationSidebarCollapsed(this.ClientSize.Width < 1160, false);
            this.ShowDashboardPage(DashboardPage.Overview);
            this.RefreshGroupsPage();
            this.SyncSettingsPageControls();
            this.SyncGamesToolbarState();

            this.ResumeLayout(true);
        }

        private Sidebar CreateNavigationSidebar()
        {
            Sidebar sidebar = new();

            Panel headerPanel = new()
            {
                Dock = DockStyle.Top,
                Height = 50,
                Padding = new Padding(0),
            };

            this._NavigationBrandLabel = new Label()
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "Steam Achievement Manager",
                Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold, GraphicsUnit.Point),
            };
            this._NavigationToggleButton = new PremiumButton()
            {
                Dock = DockStyle.Right,
                Width = 34,
                Height = 34,
                Style = PremiumButtonStyle.Ghost,
                IconGlyph = "\uE76B",
                Text = "",
                Margin = new Padding(0),
            };
            this._NavigationToggleButton.Click += (_, _) =>
            {
                this._NavigationSidebarCollapsedByViewport = false;
                this.SetNavigationSidebarCollapsed(this._NavigationSidebarCollapsed == false, true);
            };

            headerPanel.Controls.Add(this._NavigationBrandLabel);
            headerPanel.Controls.Add(this._NavigationToggleButton);

            Panel navItemsPanel = new()
            {
                Dock = DockStyle.Top,
                Height = 230,
                Padding = new Padding(0, 12, 0, 0),
            };

            this._NavigationOverviewItem = this.CreateSidebarRouteItem("\uE80F", "Overview", DashboardPage.Overview);
            this._NavigationGamesItem = this.CreateSidebarRouteItem("\uE7FC", "Games", DashboardPage.Games);
            this._NavigationGroupsItem = this.CreateSidebarRouteItem("\uE902", "Groups", DashboardPage.Groups);
            this._NavigationSettingsItem = this.CreateSidebarRouteItem("\uE713", "Settings", DashboardPage.Settings);

            navItemsPanel.Controls.Add(this._NavigationSettingsItem);
            navItemsPanel.Controls.Add(this._NavigationGroupsItem);
            navItemsPanel.Controls.Add(this._NavigationGamesItem);
            navItemsPanel.Controls.Add(this._NavigationOverviewItem);

            Panel footerPanel = new()
            {
                Dock = DockStyle.Bottom,
                Height = 126,
                Padding = new Padding(0, 6, 0, 0),
            };

            this._NavigationVersionLabel = new Label()
            {
                Dock = DockStyle.Top,
                Height = 22,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "v" + this.GetProductVersionText(),
            };
            this._NavigationGitHubButton = new PremiumButton()
            {
                Dock = DockStyle.Top,
                Height = 30,
                Text = "GitHub",
                Style = PremiumButtonStyle.Ghost,
                IconGlyph = "\uE943",
            };
            this._NavigationGitHubButton.Click += (_, _) => OpenProjectLink(ProjectRepositoryUrl);

            this._NavigationStarButton = new PremiumButton()
            {
                Dock = DockStyle.Top,
                Height = 30,
                Text = "Star Project",
                Style = PremiumButtonStyle.Secondary,
                IconGlyph = "\uE735",
            };
            this._NavigationStarButton.Click += (_, _) => OpenProjectLink(ProjectRepositoryUrl);

            footerPanel.Controls.Add(this._NavigationStarButton);
            footerPanel.Controls.Add(this._NavigationGitHubButton);
            footerPanel.Controls.Add(this._NavigationVersionLabel);

            sidebar.Controls.Add(navItemsPanel);
            sidebar.Controls.Add(footerPanel);
            sidebar.Controls.Add(headerPanel);
            return sidebar;
        }

        private Panel CreateNavigationTopbar()
        {
            Panel topbar = new()
            {
                Height = 56,
                Padding = new Padding(18, 6, 18, 6),
            };

            Panel titleWrap = new()
            {
                Dock = DockStyle.Left,
                Width = 300,
                Padding = new Padding(0),
            };

            this._TopbarTitleLabel = new Label()
            {
                Dock = DockStyle.Top,
                Height = 24,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Semibold", 13f, FontStyle.Bold, GraphicsUnit.Point),
            };
            this._TopbarSubtitleLabel = new Label()
            {
                Dock = DockStyle.Top,
                Height = 22,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point),
            };

            titleWrap.Controls.Add(this._TopbarSubtitleLabel);
            titleWrap.Controls.Add(this._TopbarTitleLabel);

            Panel actionsHost = new()
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(0),
                Margin = new Padding(0),
            };

            this._TopbarSecondaryActionsPanel = new FlowLayoutPanel()
            {
                Dock = DockStyle.Top,
                Height = 32,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoScroll = false,
                Padding = new Padding(0),
                Margin = new Padding(0),
                Visible = false,
            };

            this._TopbarPrimaryActionsPanel = new FlowLayoutPanel()
            {
                Dock = DockStyle.Top,
                Height = 32,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoScroll = false,
                Padding = new Padding(0),
                Margin = new Padding(0),
            };

            actionsHost.Controls.Add(this._TopbarSecondaryActionsPanel);
            actionsHost.Controls.Add(this._TopbarPrimaryActionsPanel);

            topbar.Controls.Add(actionsHost);
            topbar.Controls.Add(titleWrap);
            return topbar;
        }

        private NavItem CreateSidebarRouteItem(string iconGlyph, string text, DashboardPage page)
        {
            NavItem item = new()
            {
                Dock = DockStyle.Top,
                IconGlyph = iconGlyph,
                LabelText = text,
                Tag = page,
            };
            item.Click += this.OnSidebarRouteClick;
            return item;
        }

        private void OnSidebarRouteClick(object sender, EventArgs e)
        {
            if (sender is not NavItem item || item.Tag is not DashboardPage page)
            {
                return;
            }

            this.ShowDashboardPage(page);
        }

        private void ShowDashboardPage(DashboardPage page)
        {
            this._CurrentDashboardPage = page;

            if (this._OverviewPagePanel != null)
            {
                this._OverviewPagePanel.Visible = page == DashboardPage.Overview;
            }
            if (this._GamesPagePanel != null)
            {
                this._GamesPagePanel.Visible = page == DashboardPage.Games;
            }
            if (this._GroupsPagePanel != null)
            {
                this._GroupsPagePanel.Visible = page == DashboardPage.Groups;
            }
            if (this._SettingsPagePanel != null)
            {
                this._SettingsPagePanel.Visible = page == DashboardPage.Settings;
            }

            if (page == DashboardPage.Groups)
            {
                this.RefreshGroupsPage();
            }
            else if (page == DashboardPage.Settings)
            {
                this.SyncSettingsPageControls();
            }
            else if (page == DashboardPage.Games)
            {
                this.SyncGamesToolbarState();
            }

            this.UpdateTopbarForCurrentPage();
            this.UpdateNavigationButtonsState();
        }

        private void UpdateTopbarForCurrentPage()
        {
            if (this._TopbarTitleLabel == null ||
                this._TopbarSubtitleLabel == null ||
                this._TopbarPrimaryActionsPanel == null ||
                this._TopbarSecondaryActionsPanel == null)
            {
                return;
            }

            this._TopbarPrimaryActionsPanel.SuspendLayout();
            this._TopbarSecondaryActionsPanel.SuspendLayout();

            while (this._TopbarPrimaryActionsPanel.Controls.Count > 0)
            {
                this._TopbarPrimaryActionsPanel.Controls.RemoveAt(0);
            }
            while (this._TopbarSecondaryActionsPanel.Controls.Count > 0)
            {
                this._TopbarSecondaryActionsPanel.Controls.RemoveAt(0);
            }

            this._TopbarSecondaryActionsPanel.Visible = false;
            this._NavigationTopbarPanel.Height = 56;
            this._TopbarPrimaryActionsPanel.AutoScroll = true;

            switch (this._CurrentDashboardPage)
            {
                case DashboardPage.Overview:
                    this._TopbarTitleLabel.Text = "Overview";
                    this._TopbarSubtitleLabel.Text = "Global achievement and library metrics.";
                    break;

                case DashboardPage.Games:
                    this._TopbarTitleLabel.Text = "Games";
                    this._TopbarSubtitleLabel.Text = "Search, filter and manage your game list.";
                    this._TopbarSecondaryActionsPanel.Visible = true;
                    this._NavigationTopbarPanel.Height = 92;
                    this._TopbarPrimaryActionsPanel.AutoScroll = false;
                    this.AddTopbarAction(this._GamesSearchInput);
                    this.AddTopbarAction(this._GamesFilterDropdown);
                    this.AddTopbarAction(this._GamesSortDropdown);
                    this.AddTopbarAction(this._GamesViewGridButton);
                    this.AddTopbarAction(this._GamesViewListButton);
                    this.AddTopbarAction(this._GamesCheckAllButton, true);
                    this.AddTopbarAction(this._GamesUnlockAllButton, true);
                    this.AddTopbarAction(this._GamesUnlockSelectedButton, true);
                    this.AddTopbarAction(this._GamesClearGroupFilterButton, true);
                    this.AddTopbarAction(this._GamesRefreshButton, true);
                    break;

                case DashboardPage.Groups:
                    this._TopbarTitleLabel.Text = "Groups";
                    this._TopbarSubtitleLabel.Text = "Create collections and run them in sequence.";
                    this.AddTopbarAction(this._GroupsPageCreateButton);
                    this.AddTopbarAction(this._GroupsPageManageButton);
                    this.AddTopbarAction(this._GroupsPageRenameButton);
                    this.AddTopbarAction(this._GroupsPageDeleteButton);
                    this.AddTopbarAction(this._GroupsPageAddSelectedButton);
                    this.AddTopbarAction(this._GroupsPageRemoveSelectedButton);
                    this.AddTopbarAction(this._GroupsPageShowOnlyButton);
                    this.AddTopbarAction(this._GroupsPageRunButton);
                    this.AddTopbarAction(this._GroupsPageClearFilterButton);
                    break;

                default:
                    this._TopbarTitleLabel.Text = "Settings";
                    this._TopbarSubtitleLabel.Text = "Customize theme and behavior.";
                    break;
            }

            this._TopbarPrimaryActionsPanel.ResumeLayout(true);
            this._TopbarSecondaryActionsPanel.ResumeLayout(true);
            this.UpdateGroupsPageState();
        }

        private void AddTopbarAction(Control control, bool secondRow = false)
        {
            FlowLayoutPanel targetPanel = secondRow == true
                ? this._TopbarSecondaryActionsPanel
                : this._TopbarPrimaryActionsPanel;
            if (control == null || targetPanel == null)
            {
                return;
            }

            control.Margin = new Padding(0, 0, 8, 0);
            targetPanel.Controls.Add(control);
        }

        private void UpdateNavigationButtonsState()
        {
            if (this._NavigationOverviewItem != null)
            {
                this._NavigationOverviewItem.Active = this._CurrentDashboardPage == DashboardPage.Overview;
            }
            if (this._NavigationGamesItem != null)
            {
                this._NavigationGamesItem.Active = this._CurrentDashboardPage == DashboardPage.Games;
            }
            if (this._NavigationGroupsItem != null)
            {
                this._NavigationGroupsItem.Active = this._CurrentDashboardPage == DashboardPage.Groups;
            }
            if (this._NavigationSettingsItem != null)
            {
                this._NavigationSettingsItem.Active = this._CurrentDashboardPage == DashboardPage.Settings;
            }
        }

        private void OnDashboardResize(object sender, EventArgs e)
        {
            bool shouldCollapseByViewport = this.ClientSize.Width < 1140;
            if (shouldCollapseByViewport == this._NavigationSidebarCollapsedByViewport)
            {
                return;
            }

            this._NavigationSidebarCollapsedByViewport = shouldCollapseByViewport;
            this.SetNavigationSidebarCollapsed(shouldCollapseByViewport, true);
        }

        private void SetNavigationSidebarCollapsed(bool collapsed, bool animate)
        {
            if (this._NavigationSidebarPanel == null)
            {
                return;
            }

            int targetWidth = collapsed == true ? CollapsedSidebarWidth : ExpandedSidebarWidth;
            if (animate == false || this._NavigationSidebarAnimationTimer == null)
            {
                this._NavigationSidebarAnimationTimer?.Stop();
                this._NavigationSidebarPanel.Width = targetWidth;
                this._NavigationSidebarCollapsed = collapsed;
                this.ApplySidebarCollapsedVisualState(collapsed);
                return;
            }

            this._NavigationSidebarAnimationStartWidth = this._NavigationSidebarPanel.Width;
            this._NavigationSidebarAnimationTargetWidth = targetWidth;
            this._NavigationSidebarAnimationStartTimestamp = Stopwatch.GetTimestamp();
            this._NavigationSidebarCollapsed = collapsed;
            this._NavigationSidebarAnimationTimer.Stop();
            this._NavigationSidebarAnimationTimer.Start();
        }

        private void OnSidebarAnimationTick(object sender, EventArgs e)
        {
            if (this._NavigationSidebarPanel == null)
            {
                this._NavigationSidebarAnimationTimer?.Stop();
                return;
            }

            double elapsed = (Stopwatch.GetTimestamp() - this._NavigationSidebarAnimationStartTimestamp) *
                             1000.0 /
                             Stopwatch.Frequency;
            double progress = elapsed / SidebarAnimationDurationMilliseconds;
            if (progress < 0.0)
            {
                progress = 0.0;
            }
            if (progress > 1.0)
            {
                progress = 1.0;
            }

            double eased = 1.0 - Math.Pow(1.0 - progress, 3.0);
            int nextWidth = this._NavigationSidebarAnimationStartWidth +
                            (int)Math.Round((this._NavigationSidebarAnimationTargetWidth - this._NavigationSidebarAnimationStartWidth) * eased, MidpointRounding.AwayFromZero);
            this._NavigationSidebarPanel.Width = nextWidth;

            bool collapsedVisual = nextWidth <= (CollapsedSidebarWidth + ((ExpandedSidebarWidth - CollapsedSidebarWidth) / 2));
            this.ApplySidebarCollapsedVisualState(collapsedVisual);

            if (progress >= 1.0)
            {
                this._NavigationSidebarPanel.Width = this._NavigationSidebarAnimationTargetWidth;
                this.ApplySidebarCollapsedVisualState(this._NavigationSidebarCollapsed);
                this._NavigationSidebarAnimationTimer.Stop();
            }
        }

        private void ApplySidebarCollapsedVisualState(bool collapsed)
        {
            if (this._NavigationOverviewItem != null)
            {
                this._NavigationOverviewItem.Collapsed = collapsed;
            }
            if (this._NavigationGamesItem != null)
            {
                this._NavigationGamesItem.Collapsed = collapsed;
            }
            if (this._NavigationGroupsItem != null)
            {
                this._NavigationGroupsItem.Collapsed = collapsed;
            }
            if (this._NavigationSettingsItem != null)
            {
                this._NavigationSettingsItem.Collapsed = collapsed;
            }

            if (this._NavigationBrandLabel != null)
            {
                this._NavigationBrandLabel.Visible = collapsed == false;
            }
            if (this._NavigationVersionLabel != null)
            {
                this._NavigationVersionLabel.Visible = collapsed == false;
            }

            if (this._NavigationGitHubButton != null)
            {
                this._NavigationGitHubButton.Text = collapsed == true ? "" : "GitHub";
            }
            if (this._NavigationStarButton != null)
            {
                this._NavigationStarButton.Text = collapsed == true ? "" : "Support";
            }
            if (this._NavigationToggleButton != null)
            {
                this._NavigationToggleButton.IconGlyph = collapsed == true ? "\uE76C" : "\uE76B";
            }

            if (this._NavigationToolTip != null)
            {
                this._NavigationToolTip.SetToolTip(this._NavigationOverviewItem, collapsed ? "Overview" : string.Empty);
                this._NavigationToolTip.SetToolTip(this._NavigationGamesItem, collapsed ? "Games" : string.Empty);
                this._NavigationToolTip.SetToolTip(this._NavigationGroupsItem, collapsed ? "Groups" : string.Empty);
                this._NavigationToolTip.SetToolTip(this._NavigationSettingsItem, collapsed ? "Settings" : string.Empty);
                this._NavigationToolTip.SetToolTip(this._NavigationGitHubButton, collapsed ? "GitHub" : string.Empty);
                this._NavigationToolTip.SetToolTip(this._NavigationStarButton, collapsed ? "Support project" : string.Empty);
                this._NavigationToolTip.SetToolTip(this._NavigationToggleButton, collapsed ? "Expand sidebar" : "Collapse sidebar");
            }
        }

        private void ApplyNavigationTheme(
            Color sidebarBackColor,
            Color pageBackColor,
            Color cardBackColor,
            Color cardBorderColor,
            Color buttonBackColor,
            Color buttonBorderColor,
            Color buttonActiveBackColor,
            Color buttonActiveBorderColor,
            Color mutedTextColor,
            Color accentColor)
        {
            this._NavigationCardBackColor = cardBackColor;
            this._NavigationCardBorderColor = cardBorderColor;
            this._NavigationButtonActiveBackColor = buttonActiveBackColor;
            this._NavigationButtonActiveBorderColor = buttonActiveBorderColor;
            this._NavigationMutedTextColor = mutedTextColor;
            this._NavigationAccentColor = accentColor;

            bool dark = pageBackColor.GetBrightness() < 0.5f;
            this._DashboardTheme = dark
                ? DashboardThemeTokens.CreateDark(accentColor)
                : DashboardThemeTokens.CreateLight(accentColor);

            if (this._NavigationRootPanel == null)
            {
                return;
            }

            this._NavigationRootPanel.BackColor = this._DashboardTheme.AppBackground;
            this._NavigationMainPanel.BackColor = this._DashboardTheme.SurfaceBackground;
            this._NavigationPagesPanel.BackColor = this._DashboardTheme.SurfaceBackground;

            this._NavigationSidebarPanel.ApplyTheme(this._DashboardTheme);
            this._NavigationSidebarPanel.BackColor = sidebarBackColor;
            this._NavigationTopbarPanel.BackColor = this._DashboardTheme.SurfaceBackground;

            if (this._TopbarTitleLabel != null)
            {
                this._TopbarTitleLabel.ForeColor = this._DashboardTheme.TextPrimary;
            }
            if (this._TopbarSubtitleLabel != null)
            {
                this._TopbarSubtitleLabel.ForeColor = this._DashboardTheme.TextMuted;
            }
            if (this._TopbarPrimaryActionsPanel != null)
            {
                this._TopbarPrimaryActionsPanel.BackColor = this._DashboardTheme.SurfaceBackground;
            }
            if (this._TopbarSecondaryActionsPanel != null)
            {
                this._TopbarSecondaryActionsPanel.BackColor = this._DashboardTheme.SurfaceBackground;
            }

            this._NavigationBrandLabel.ForeColor = this._DashboardTheme.TextPrimary;
            this._NavigationVersionLabel.ForeColor = this._DashboardTheme.TextMuted;

            this._NavigationOverviewItem.ApplyTheme(this._DashboardTheme);
            this._NavigationGamesItem.ApplyTheme(this._DashboardTheme);
            this._NavigationGroupsItem.ApplyTheme(this._DashboardTheme);
            this._NavigationSettingsItem.ApplyTheme(this._DashboardTheme);
            this._NavigationToggleButton.ApplyTheme(this._DashboardTheme);
            this._NavigationGitHubButton.ApplyTheme(this._DashboardTheme);
            this._NavigationStarButton.ApplyTheme(this._DashboardTheme);
            this._NavigationGitHubButton.Style = PremiumButtonStyle.Ghost;
            this._NavigationStarButton.Style = PremiumButtonStyle.Secondary;
            this._NavigationToolTip?.ApplyTheme(this._DashboardTheme);

            this.ApplyThemeToPages();
            this.UpdateTopbarForCurrentPage();
            this.UpdateNavigationButtonsState();
        }

        private static void OpenProjectLink(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url)
                {
                    UseShellExecute = true,
                });
            }
            catch (Exception)
            {
            }
        }

        private string GetProductVersionText()
        {
            string version = Application.ProductVersion;
            int metadataSeparator = version?.IndexOf('+') ?? -1;
            if (metadataSeparator > 0)
            {
                version = version.Substring(0, metadataSeparator);
            }

            if (Version.TryParse(version, out Version parsed) == true)
            {
                return $"{parsed.Major}.{parsed.Minor}.{parsed.Build}";
            }

            return version ?? "0.0.0";
        }
    }
}

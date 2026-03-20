using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Windows.Forms;

namespace SAM.Picker
{
    internal partial class GamePicker
    {
        private sealed class GameGroup
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public HashSet<uint> AppIds { get; } = new();
        }

        [DataContract]
        private sealed class GroupsDatabaseDocument
        {
            [DataMember(Name = "updated_utc")]
            public string UpdatedUtc { get; set; }

            [DataMember(Name = "groups")]
            public GroupRecord[] Groups { get; set; }
        }

        [DataContract]
        private sealed class GroupRecord
        {
            [DataMember(Name = "id")]
            public string Id { get; set; }

            [DataMember(Name = "name")]
            public string Name { get; set; }

            [DataMember(Name = "app_ids")]
            public uint[] AppIds { get; set; }
        }

        private sealed class GroupRunRequest
        {
            public readonly string GroupName;
            public readonly List<uint> GameIds;
            public readonly int[] DurationsSeconds;

            public GroupRunRequest(string groupName, List<uint> gameIds, int[] durationsSeconds)
            {
                this.GroupName = groupName;
                this.GameIds = gameIds ?? new List<uint>();
                this.DurationsSeconds = durationsSeconds ?? Array.Empty<int>();
            }
        }

        private sealed class GroupRunProgress
        {
            public readonly uint GameId;
            public readonly int DurationSeconds;
            public readonly bool Success;
            public readonly string Error;

            public GroupRunProgress(uint gameId, int durationSeconds, bool success, string error)
            {
                this.GameId = gameId;
                this.DurationSeconds = durationSeconds;
                this.Success = success;
                this.Error = error;
            }
        }

        private sealed class GroupListItem
        {
            public readonly GameGroup Group;
            private readonly string _Text;

            public GroupListItem(GameGroup group)
            {
                this.Group = group;
                int count = group?.AppIds?.Count ?? 0;
                this._Text = $"{group?.Name ?? "Unnamed"} ({count} game{(count == 1 ? "" : "s")})";
            }

            public override string ToString()
            {
                return this._Text;
            }
        }

        private sealed class GroupGameListItem
        {
            public readonly uint AppId;
            private readonly string _Text;

            public GroupGameListItem(uint appId, string name)
            {
                this.AppId = appId;
                string normalizedName = string.IsNullOrWhiteSpace(name) == false
                    ? name
                    : $"App {appId.ToString(CultureInfo.InvariantCulture)}";
                this._Text = $"{normalizedName} ({appId.ToString(CultureInfo.InvariantCulture)})";
            }

            public override string ToString()
            {
                return this._Text;
            }
        }

        private const int MaxGamesPerGroup = 32;
        private readonly List<GameGroup> _Groups = new();
        private BackgroundWorker _GroupRunWorker;
        private ToolStripDropDownButton _GroupsDropDownButton;
        private ToolStripMenuItem _ManageGroupsMenuItem;
        private ToolStripMenuItem _CreateGroupMenuItem;
        private ToolStripMenuItem _RenameGroupMenuItem;
        private ToolStripMenuItem _DeleteGroupMenuItem;
        private ToolStripMenuItem _AddSelectedGamesToGroupMenuItem;
        private ToolStripMenuItem _RemoveSelectedGamesFromGroupMenuItem;
        private ToolStripMenuItem _ShowOnlyGroupGamesMenuItem;
        private ToolStripMenuItem _ClearGroupFilterMenuItem;
        private ToolStripMenuItem _RunGroupMenuItem;
        private int _GroupRunCompleted;
        private int _GroupRunTotal;
        private int _GroupRunFailed;
        private string _GroupRunName;
        private string _ActiveGroupFilterId;
        private SplitContainer _DashboardSplitContainer;
        private Panel _GroupsSidebarPanel;
        private Label _GroupsSidebarTitleLabel;
        private Label _GroupsSidebarDetailsLabel;
        private ListBox _GroupsSidebarListBox;
        private TableLayoutPanel _GroupsSidebarActionsTable;
        private PremiumButton _GroupsCreateButton;
        private PremiumButton _GroupsManageButton;
        private PremiumButton _GroupsRenameButton;
        private PremiumButton _GroupsDeleteButton;
        private PremiumButton _GroupsAddSelectedButton;
        private PremiumButton _GroupsRemoveSelectedButton;
        private PremiumButton _GroupsShowOnlyButton;
        private PremiumButton _GroupsRunButton;
        private PremiumButton _GroupsClearFilterButton;
        private bool _GroupsSidebarSuppressSelectionEvents;
        private Color _GroupsSidebarCardBackColor;
        private Color _GroupsSidebarCardBorderColor;
        private Color _GroupsSidebarCardSelectedBackColor;
        private Color _GroupsSidebarCardSelectedBorderColor;
        private Color _GroupsSidebarTextColor;
        private Color _GroupsSidebarMutedTextColor;
        private Color _GroupsSidebarAccentColor;

        private bool IsGroupRunBusy => this._GroupRunWorker?.IsBusy == true;

        private void InitializeGroupsDashboardLayout()
        {
            if (this._DashboardSplitContainer != null ||
                this._GameListView == null ||
                this.Controls.Contains(this._GameListView) == false)
            {
                return;
            }

            int listControlIndex = this.Controls.GetChildIndex(this._GameListView);
            this.Controls.Remove(this._GameListView);

            this._DashboardSplitContainer = new SplitContainer()
            {
                Name = "_DashboardSplitContainer",
                Dock = DockStyle.Fill,
                FixedPanel = FixedPanel.Panel2,
                IsSplitterFixed = false,
                SplitterWidth = 6,
            };

            this._GameListView.Dock = DockStyle.Fill;
            this._DashboardSplitContainer.Panel1.Controls.Add(this._GameListView);

            this._GroupsSidebarPanel = this.CreateGroupsSidebarPanel();
            this._DashboardSplitContainer.Panel2.Controls.Add(this._GroupsSidebarPanel);

            this.Controls.Add(this._DashboardSplitContainer);
            this.Controls.SetChildIndex(this._DashboardSplitContainer, listControlIndex);

            this.ApplyDashboardSplitLayoutDefaults();
        }

        private void ApplyDashboardSplitLayoutDefaults()
        {
            if (this._DashboardSplitContainer == null)
            {
                return;
            }

            int availableWidth = this._DashboardSplitContainer.ClientSize.Width > 0
                ? this._DashboardSplitContainer.ClientSize.Width
                : this.ClientSize.Width;
            if (availableWidth <= 0)
            {
                availableWidth = 742;
            }

            int splitterWidth = this._DashboardSplitContainer.SplitterWidth > 0
                ? this._DashboardSplitContainer.SplitterWidth
                : 6;
            const int desiredSidebarWidth = 320;
            const int minimumMainWidth = 320;
            const int minimumSidebarWidth = 220;

            int panel2Min = Math.Max(
                0,
                Math.Min(minimumSidebarWidth, availableWidth - splitterWidth - minimumMainWidth));
            int panel1Min = Math.Max(
                0,
                Math.Min(minimumMainWidth, availableWidth - splitterWidth - panel2Min));

            try
            {
                this._DashboardSplitContainer.Panel1MinSize = panel1Min;
                this._DashboardSplitContainer.Panel2MinSize = panel2Min;

                int minDistance = this._DashboardSplitContainer.Panel1MinSize;
                int maxDistance = Math.Max(
                    minDistance,
                    availableWidth - this._DashboardSplitContainer.Panel2MinSize - splitterWidth);
                int desiredDistance = availableWidth - desiredSidebarWidth;
                if (desiredDistance < minDistance)
                {
                    desiredDistance = minDistance;
                }
                if (desiredDistance > maxDistance)
                {
                    desiredDistance = maxDistance;
                }

                this._DashboardSplitContainer.SplitterDistance = desiredDistance;
            }
            catch (InvalidOperationException)
            {
                this._DashboardSplitContainer.Panel1MinSize = 0;
                this._DashboardSplitContainer.Panel2MinSize = 0;
                this._DashboardSplitContainer.SplitterDistance = Math.Max(120, availableWidth - desiredSidebarWidth);
            }
        }

        private Panel CreateGroupsSidebarPanel()
        {
            Panel panel = new()
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12, 12, 12, 12),
            };

            this._GroupsSidebarTitleLabel = new Label()
            {
                Dock = DockStyle.Top,
                Height = 26,
                Text = "Groups",
                TextAlign = ContentAlignment.MiddleLeft,
            };

            this._GroupsSidebarDetailsLabel = new Label()
            {
                Dock = DockStyle.Top,
                Height = 40,
                Text = "Create a group and add selected games.",
                TextAlign = ContentAlignment.TopLeft,
            };

            this._GroupsSidebarActionsTable = new TableLayoutPanel()
            {
                Dock = DockStyle.Bottom,
                Height = 148,
                ColumnCount = 2,
                RowCount = 4,
                Padding = new Padding(0),
                Margin = new Padding(0),
            };
            this._GroupsSidebarActionsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            this._GroupsSidebarActionsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            this._GroupsSidebarActionsTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
            this._GroupsSidebarActionsTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
            this._GroupsSidebarActionsTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
            this._GroupsSidebarActionsTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));

            this._GroupsCreateButton = this.CreateSidebarActionButton("New Group", this.OnGroupsSidebarCreateGroup, PremiumButtonStyle.Primary);
            this._GroupsManageButton = this.CreateSidebarActionButton("Manage", this.OnGroupsSidebarManageGroups);
            this._GroupsRenameButton = this.CreateSidebarActionButton("Rename", this.OnGroupsSidebarRenameGroup);
            this._GroupsDeleteButton = this.CreateSidebarActionButton("Delete", this.OnGroupsSidebarDeleteGroup);
            this._GroupsAddSelectedButton = this.CreateSidebarActionButton("Add Selected", this.OnGroupsSidebarAddSelectedGames);
            this._GroupsRemoveSelectedButton = this.CreateSidebarActionButton("Remove Selected", this.OnGroupsSidebarRemoveSelectedGames);
            this._GroupsShowOnlyButton = this.CreateSidebarActionButton("Show Only", this.OnGroupsSidebarShowOnlyGroup);
            this._GroupsRunButton = this.CreateSidebarActionButton("Run Group", this.OnGroupsSidebarRunGroup, PremiumButtonStyle.Primary);

            this._GroupsSidebarActionsTable.Controls.Add(this._GroupsCreateButton, 0, 0);
            this._GroupsSidebarActionsTable.Controls.Add(this._GroupsManageButton, 1, 0);
            this._GroupsSidebarActionsTable.Controls.Add(this._GroupsRenameButton, 0, 1);
            this._GroupsSidebarActionsTable.Controls.Add(this._GroupsDeleteButton, 1, 1);
            this._GroupsSidebarActionsTable.Controls.Add(this._GroupsAddSelectedButton, 0, 2);
            this._GroupsSidebarActionsTable.Controls.Add(this._GroupsRemoveSelectedButton, 1, 2);
            this._GroupsSidebarActionsTable.Controls.Add(this._GroupsShowOnlyButton, 0, 3);
            this._GroupsSidebarActionsTable.Controls.Add(this._GroupsRunButton, 1, 3);

            this._GroupsClearFilterButton = this.CreateSidebarActionButton("Clear Filter", this.OnGroupsSidebarClearFilter, PremiumButtonStyle.Ghost);
            this._GroupsClearFilterButton.Dock = DockStyle.Bottom;
            this._GroupsClearFilterButton.Height = 34;

            this._GroupsSidebarListBox = new ListBox()
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 64,
                IntegralHeight = false,
                SelectionMode = SelectionMode.One,
            };
            this._GroupsSidebarListBox.DrawItem += this.OnGroupsSidebarListBoxDrawItem;
            this._GroupsSidebarListBox.SelectedIndexChanged += this.OnGroupsSidebarSelectionChanged;
            this._GroupsSidebarListBox.DoubleClick += this.OnGroupsSidebarDoubleClick;
            this._GroupsSidebarListBox.HandleCreated += (_, _) => this.ApplyScrollableControlTheme(this._GroupsSidebarListBox);

            panel.Controls.Add(this._GroupsSidebarListBox);
            panel.Controls.Add(this._GroupsSidebarActionsTable);
            panel.Controls.Add(this._GroupsClearFilterButton);
            panel.Controls.Add(this._GroupsSidebarDetailsLabel);
            panel.Controls.Add(this._GroupsSidebarTitleLabel);

            return panel;
        }

        private PremiumButton CreateSidebarActionButton(
            string text,
            EventHandler clickHandler,
            PremiumButtonStyle style = PremiumButtonStyle.Secondary)
        {
            PremiumButton button = new()
            {
                Text = text,
                Height = 28,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 6, 6),
                Style = style,
            };
            button.Click += clickHandler;
            return button;
        }

        private void OnGroupsSidebarSelectionChanged(object sender, EventArgs e)
        {
            if (this._GroupsSidebarSuppressSelectionEvents == true)
            {
                return;
            }

            this.UpdateGroupsSidebarActionState();
            this._GroupsSidebarListBox?.Invalidate();
        }

        private void OnGroupsSidebarDoubleClick(object sender, EventArgs e)
        {
            GameGroup selectedGroup = this.GetSelectedSidebarGroup();
            if (selectedGroup == null)
            {
                return;
            }

            this.ApplyGroupFilter(selectedGroup);
        }

        private void OnGroupsSidebarCreateGroup(object sender, EventArgs e)
        {
            this.CreateGroup(this);
        }

        private void OnGroupsSidebarManageGroups(object sender, EventArgs e)
        {
            this.ShowGroupManagerDialog();
            this.RefreshGroupsSidebar();
            this.UpdateGroupsSidebarActionState();
        }

        private void OnGroupsSidebarRenameGroup(object sender, EventArgs e)
        {
            if (this.TryGetSelectedSidebarGroup(out GameGroup selectedGroup) == false)
            {
                return;
            }

            this.RenameGroup(this, selectedGroup);
        }

        private void OnGroupsSidebarDeleteGroup(object sender, EventArgs e)
        {
            if (this.TryGetSelectedSidebarGroup(out GameGroup selectedGroup) == false)
            {
                return;
            }

            this.DeleteGroup(this, selectedGroup);
        }

        private void OnGroupsSidebarAddSelectedGames(object sender, EventArgs e)
        {
            if (this.TryGetSelectedSidebarGroup(out GameGroup selectedGroup) == false)
            {
                return;
            }

            this.AddGamesToGroup(this, selectedGroup, this.GetSelectedVisibleGameIdsInDisplayOrder());
        }

        private void OnGroupsSidebarRemoveSelectedGames(object sender, EventArgs e)
        {
            if (this.TryGetSelectedSidebarGroup(out GameGroup selectedGroup) == false)
            {
                return;
            }

            this.RemoveGamesFromGroup(this, selectedGroup, this.GetSelectedVisibleGameIdsInDisplayOrder());
        }

        private void OnGroupsSidebarShowOnlyGroup(object sender, EventArgs e)
        {
            if (this.TryGetSelectedSidebarGroup(out GameGroup selectedGroup) == false)
            {
                return;
            }

            this.ApplyGroupFilter(selectedGroup);
        }

        private void OnGroupsSidebarClearFilter(object sender, EventArgs e)
        {
            this.ClearGroupFilter();
        }

        private void OnGroupsSidebarRunGroup(object sender, EventArgs e)
        {
            if (this.TryGetSelectedSidebarGroup(out GameGroup selectedGroup) == false)
            {
                return;
            }

            this.RunGroup(this, selectedGroup);
        }

        private bool TryGetSelectedSidebarGroup(out GameGroup selectedGroup)
        {
            selectedGroup = this.GetSelectedSidebarGroup();
            if (selectedGroup != null)
            {
                return true;
            }

            MessageBox.Show(
                this,
                "Select a group first.",
                "Info",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return false;
        }

        private GameGroup GetSelectedSidebarGroup()
        {
            return (this._GroupsSidebarListBox?.SelectedItem as GroupListItem)?.Group;
        }

        private void RefreshGroupsSidebar(string preferredGroupId = null)
        {
            if (this._GroupsSidebarListBox == null)
            {
                return;
            }

            string selectedId = preferredGroupId;
            if (string.IsNullOrWhiteSpace(selectedId) == true)
            {
                selectedId = this.GetSelectedSidebarGroup()?.Id;
            }
            if (string.IsNullOrWhiteSpace(selectedId) == true)
            {
                selectedId = this._ActiveGroupFilterId;
            }

            this._GroupsSidebarSuppressSelectionEvents = true;
            this._GroupsSidebarListBox.BeginUpdate();
            try
            {
                this._GroupsSidebarListBox.Items.Clear();
                foreach (GameGroup group in this._Groups
                             .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                             .ThenBy(item => item.Id, StringComparer.Ordinal))
                {
                    this._GroupsSidebarListBox.Items.Add(new GroupListItem(group));
                }

                int selectedIndex = -1;
                if (string.IsNullOrWhiteSpace(selectedId) == false)
                {
                    for (int i = 0; i < this._GroupsSidebarListBox.Items.Count; i++)
                    {
                        if (this._GroupsSidebarListBox.Items[i] is not GroupListItem item ||
                            item.Group == null)
                        {
                            continue;
                        }

                        if (string.Equals(item.Group.Id, selectedId, StringComparison.Ordinal) == true)
                        {
                            selectedIndex = i;
                            break;
                        }
                    }
                }

                if (selectedIndex < 0 && this._GroupsSidebarListBox.Items.Count > 0)
                {
                    selectedIndex = 0;
                }

                this._GroupsSidebarListBox.SelectedIndex = selectedIndex;
            }
            finally
            {
                this._GroupsSidebarListBox.EndUpdate();
                this._GroupsSidebarSuppressSelectionEvents = false;
            }

            this.UpdateGroupsSidebarActionState();
            this._GroupsSidebarListBox.Invalidate();
        }

        private void UpdateGroupsSidebarActionState()
        {
            if (this._GroupsSidebarPanel == null)
            {
                return;
            }

            GameGroup selectedGroup = this.GetSelectedSidebarGroup();
            bool hasGroups = this._Groups.Count > 0;
            bool hasSelectedGroup = selectedGroup != null;
            bool hasGameSelection = this.GetSelectedVisibleGameIds().Count > 0;

            if (this._GroupsSidebarTitleLabel != null)
            {
                this._GroupsSidebarTitleLabel.Text = "Groups";
            }

            if (this._GroupsSidebarDetailsLabel != null)
            {
                if (hasSelectedGroup == true)
                {
                    bool isActiveFilter = string.Equals(this._ActiveGroupFilterId, selectedGroup.Id, StringComparison.Ordinal);
                    this._GroupsSidebarDetailsLabel.Text =
                        $"{selectedGroup.AppIds.Count}/{MaxGamesPerGroup} games in selected group" +
                        (isActiveFilter == true ? " • active filter" : "");
                }
                else if (hasGroups == false)
                {
                    this._GroupsSidebarDetailsLabel.Text = "No groups yet. Create one to get started.";
                }
                else
                {
                    this._GroupsSidebarDetailsLabel.Text = "Select a group to filter, edit, or run.";
                }
            }

            if (this._GroupsManageButton != null)
            {
                this._GroupsManageButton.Enabled = true;
            }

            if (this._GroupsRenameButton != null)
            {
                this._GroupsRenameButton.Enabled = hasSelectedGroup;
            }

            if (this._GroupsDeleteButton != null)
            {
                this._GroupsDeleteButton.Enabled = hasSelectedGroup;
            }

            if (this._GroupsAddSelectedButton != null)
            {
                this._GroupsAddSelectedButton.Enabled = hasSelectedGroup && hasGameSelection;
            }

            if (this._GroupsRemoveSelectedButton != null)
            {
                this._GroupsRemoveSelectedButton.Enabled = hasSelectedGroup && hasGameSelection;
            }

            if (this._GroupsShowOnlyButton != null)
            {
                this._GroupsShowOnlyButton.Enabled = hasSelectedGroup;
            }

            if (this._GroupsRunButton != null)
            {
                this._GroupsRunButton.Enabled = hasSelectedGroup && this.IsGroupRunBusy == false;
            }

            if (this._GroupsClearFilterButton != null)
            {
                GameGroup activeFilterGroup = this.GetActiveGroupFilter();
                this._GroupsClearFilterButton.Enabled = activeFilterGroup != null;
                this._GroupsClearFilterButton.Text = activeFilterGroup != null
                    ? $"Clear Filter ({activeFilterGroup.Name})"
                    : "Clear Filter";
            }

            this._GroupsSidebarListBox?.Invalidate();
        }

        private void OnGroupsSidebarListBoxDrawItem(object sender, DrawItemEventArgs e)
        {
            if (this._GroupsSidebarListBox == null ||
                e.Index < 0 ||
                e.Index >= this._GroupsSidebarListBox.Items.Count)
            {
                return;
            }

            if (this._GroupsSidebarListBox.Items[e.Index] is not GroupListItem item ||
                item.Group == null)
            {
                e.DrawBackground();
                return;
            }

            GameGroup group = item.Group;
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            bool activeFilter = string.Equals(this._ActiveGroupFilterId, group.Id, StringComparison.Ordinal);
            e.DrawBackground();

            Rectangle cardRect = Rectangle.Inflate(e.Bounds, -6, -4);
            if (cardRect.Width < 20 || cardRect.Height < 20)
            {
                return;
            }

            Color cardColor = selected == true
                ? this._GroupsSidebarCardSelectedBackColor
                : this._GroupsSidebarCardBackColor;
            if (activeFilter == true)
            {
                cardColor = BlendColor(
                    cardColor,
                    this._GroupsSidebarAccentColor,
                    this._IsDarkThemeActive == true ? 0.20f : 0.12f);
            }

            Color borderColor = selected == true
                ? this._GroupsSidebarCardSelectedBorderColor
                : this._GroupsSidebarCardBorderColor;
            if (activeFilter == true && selected == false)
            {
                borderColor = this._GroupsSidebarAccentColor;
            }

            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var path = CreateRoundedRectanglePath(cardRect, 10))
            using (SolidBrush fillBrush = new(cardColor))
            using (Pen borderPen = new(borderColor))
            {
                e.Graphics.FillPath(fillBrush, path);
                e.Graphics.DrawPath(borderPen, path);
            }

            Rectangle nameRect = new(cardRect.X + 11, cardRect.Y + 9, cardRect.Width - 22, 20);
            Rectangle detailsRect = new(cardRect.X + 11, cardRect.Y + 31, cardRect.Width - 22, 18);
            using (StringFormat format = new()
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap,
            })
            using (SolidBrush nameBrush = new(this._GroupsSidebarTextColor))
            using (SolidBrush detailsBrush = new(this._GroupsSidebarMutedTextColor))
            {
                e.Graphics.DrawString(group.Name, this._GroupsSidebarListBox.Font, nameBrush, nameRect, format);
                string details = $"{group.AppIds.Count}/{MaxGamesPerGroup} games";
                if (activeFilter == true)
                {
                    details += "  •  Active";
                }

                e.Graphics.DrawString(details, this._GroupsSidebarListBox.Font, detailsBrush, detailsRect, format);
            }

            if (activeFilter == true)
            {
                Rectangle indicatorRect = new(cardRect.Right - 14, cardRect.Y + 10, 6, 6);
                using SolidBrush indicatorBrush = new(this._GroupsSidebarAccentColor);
                e.Graphics.FillEllipse(indicatorBrush, indicatorRect);
            }

            if ((e.State & DrawItemState.Focus) == DrawItemState.Focus)
            {
                e.DrawFocusRectangle();
            }
        }

        private static Color BlendColor(Color baseColor, Color overlayColor, float amount)
        {
            if (amount < 0f)
            {
                amount = 0f;
            }
            else if (amount > 1f)
            {
                amount = 1f;
            }

            int r = (int)Math.Round(baseColor.R + (overlayColor.R - baseColor.R) * amount, MidpointRounding.AwayFromZero);
            int g = (int)Math.Round(baseColor.G + (overlayColor.G - baseColor.G) * amount, MidpointRounding.AwayFromZero);
            int b = (int)Math.Round(baseColor.B + (overlayColor.B - baseColor.B) * amount, MidpointRounding.AwayFromZero);
            r = Math.Max(0, Math.Min(255, r));
            g = Math.Max(0, Math.Min(255, g));
            b = Math.Max(0, Math.Min(255, b));
            return Color.FromArgb(r, g, b);
        }

        private void ApplyGroupsSidebarTheme(
            Color panelBackColor,
            Color buttonBackColor,
            Color buttonBorderColor,
            Color cardBackColor,
            Color cardBorderColor,
            Color cardSelectedBackColor,
            Color cardSelectedBorderColor,
            Color accentColor,
            Color mutedTextColor)
        {
            this._GroupsSidebarCardBackColor = cardBackColor;
            this._GroupsSidebarCardBorderColor = cardBorderColor;
            this._GroupsSidebarCardSelectedBackColor = cardSelectedBackColor;
            this._GroupsSidebarCardSelectedBorderColor = cardSelectedBorderColor;
            this._GroupsSidebarTextColor = this.ForeColor;
            this._GroupsSidebarMutedTextColor = mutedTextColor;
            this._GroupsSidebarAccentColor = accentColor;

            if (this._GroupsSidebarPanel == null)
            {
                return;
            }

            this._GroupsSidebarPanel.BackColor = panelBackColor;

            if (this._GroupsSidebarTitleLabel != null)
            {
                this._GroupsSidebarTitleLabel.ForeColor = this.ForeColor;
                this._GroupsSidebarTitleLabel.Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold, GraphicsUnit.Point);
            }

            if (this._GroupsSidebarDetailsLabel != null)
            {
                this._GroupsSidebarDetailsLabel.ForeColor = mutedTextColor;
                this._GroupsSidebarDetailsLabel.Font = new Font("Segoe UI", 8.8f, FontStyle.Regular, GraphicsUnit.Point);
            }

            if (this._GroupsSidebarListBox != null)
            {
                this._GroupsSidebarListBox.BackColor = panelBackColor;
                this._GroupsSidebarListBox.ForeColor = this.ForeColor;
                this._GroupsSidebarListBox.Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
                this._GroupsSidebarListBox.Invalidate();
            }

            this.StyleGroupsSidebarButton(this._GroupsCreateButton, PremiumButtonStyle.Primary);
            this.StyleGroupsSidebarButton(this._GroupsManageButton, PremiumButtonStyle.Secondary);
            this.StyleGroupsSidebarButton(this._GroupsRenameButton, PremiumButtonStyle.Secondary);
            this.StyleGroupsSidebarButton(this._GroupsDeleteButton, PremiumButtonStyle.Secondary);
            this.StyleGroupsSidebarButton(this._GroupsAddSelectedButton, PremiumButtonStyle.Secondary);
            this.StyleGroupsSidebarButton(this._GroupsRemoveSelectedButton, PremiumButtonStyle.Secondary);
            this.StyleGroupsSidebarButton(this._GroupsShowOnlyButton, PremiumButtonStyle.Secondary);
            this.StyleGroupsSidebarButton(this._GroupsRunButton, PremiumButtonStyle.Primary);
            this.StyleGroupsSidebarButton(this._GroupsClearFilterButton, PremiumButtonStyle.Ghost);
        }

        private void StyleGroupsSidebarButton(PremiumButton button, PremiumButtonStyle style)
        {
            if (button == null)
            {
                return;
            }

            DashboardThemeTokens theme = this._IsDarkThemeActive == true
                ? DashboardThemeTokens.CreateDark(this._GroupsSidebarAccentColor)
                : DashboardThemeTokens.CreateLight(this._GroupsSidebarAccentColor);
            button.Style = style;
            button.ApplyTheme(theme);
            button.Font = new Font("Segoe UI", 8.7f, FontStyle.Regular, GraphicsUnit.Point);
        }

        private void InitializeGroupsFeature()
        {
            this.InitializeGroupsMenuButton();
            this.InitializeGroupRunWorker();
            this.LoadGroupsDatabase();
            this.EnsureGroupsDatabaseFileExists();
            this.UpdateGroupsButtonText();
            this.RefreshGroupsSidebar();
            this.UpdateGroupsSidebarActionState();
            this.RefreshGroupsPage();
            this.UpdateGroupsPageState();
            this.FormClosing += this.OnGroupsFeatureFormClosing;
        }

        private void InitializeGroupsMenuButton()
        {
            this._GroupsDropDownButton = new ToolStripDropDownButton()
            {
                Name = "_GroupsDropDownButton",
                Text = "Groups",
                ToolTipText = "Manage game groups",
                DisplayStyle = ToolStripItemDisplayStyle.Text,
            };
            this._GroupsDropDownButton.DropDownOpening += this.OnGroupsMenuOpening;

            this._ManageGroupsMenuItem = new ToolStripMenuItem("Manage Groups...");
            this._ManageGroupsMenuItem.Click += this.OnManageGroups;

            this._CreateGroupMenuItem = new ToolStripMenuItem("Create Group...");
            this._CreateGroupMenuItem.Click += this.OnCreateGroup;

            this._RenameGroupMenuItem = new ToolStripMenuItem("Rename Group...");
            this._RenameGroupMenuItem.Click += this.OnRenameGroup;

            this._DeleteGroupMenuItem = new ToolStripMenuItem("Delete Group...");
            this._DeleteGroupMenuItem.Click += this.OnDeleteGroup;

            this._AddSelectedGamesToGroupMenuItem = new ToolStripMenuItem("Quick Add Selected Games...");
            this._AddSelectedGamesToGroupMenuItem.Click += this.OnAddSelectedGamesToGroup;

            this._RemoveSelectedGamesFromGroupMenuItem = new ToolStripMenuItem("Quick Remove Selected Games...");
            this._RemoveSelectedGamesFromGroupMenuItem.Click += this.OnRemoveSelectedGamesFromGroup;

            this._ShowOnlyGroupGamesMenuItem = new ToolStripMenuItem("Show Only Group Games...");
            this._ShowOnlyGroupGamesMenuItem.Click += this.OnShowOnlyGroupGames;

            this._ClearGroupFilterMenuItem = new ToolStripMenuItem("Clear Group Filter");
            this._ClearGroupFilterMenuItem.Click += this.OnClearGroupFilter;

            this._RunGroupMenuItem = new ToolStripMenuItem("Run Group...");
            this._RunGroupMenuItem.Click += this.OnRunGroup;

            this._GroupsDropDownButton.DropDownItems.Add(this._ManageGroupsMenuItem);
            this._GroupsDropDownButton.DropDownItems.Add(new ToolStripSeparator());
            this._GroupsDropDownButton.DropDownItems.Add(this._CreateGroupMenuItem);
            this._GroupsDropDownButton.DropDownItems.Add(this._RenameGroupMenuItem);
            this._GroupsDropDownButton.DropDownItems.Add(this._DeleteGroupMenuItem);
            this._GroupsDropDownButton.DropDownItems.Add(new ToolStripSeparator());
            this._GroupsDropDownButton.DropDownItems.Add(this._AddSelectedGamesToGroupMenuItem);
            this._GroupsDropDownButton.DropDownItems.Add(this._RemoveSelectedGamesFromGroupMenuItem);
            this._GroupsDropDownButton.DropDownItems.Add(new ToolStripSeparator());
            this._GroupsDropDownButton.DropDownItems.Add(this._ShowOnlyGroupGamesMenuItem);
            this._GroupsDropDownButton.DropDownItems.Add(this._ClearGroupFilterMenuItem);
            this._GroupsDropDownButton.DropDownItems.Add(new ToolStripSeparator());
            this._GroupsDropDownButton.DropDownItems.Add(this._RunGroupMenuItem);

            int insertIndex = this._PickerToolStrip.Items.IndexOf(this._ThemeDropDownButton);
            if (insertIndex < 0)
            {
                insertIndex = this._PickerToolStrip.Items.Count;
            }

            this._PickerToolStrip.Items.Insert(insertIndex, this._GroupsDropDownButton);
            this._GroupsDropDownButton.Visible = false;
        }

        private void ApplyGroupsToolStripTheme()
        {
            if (this._GroupsDropDownButton == null)
            {
                return;
            }

            this.StyleToolStripDropDownButton(this._GroupsDropDownButton, this.GetGroupsButtonText());
            foreach (ToolStripItem item in this._GroupsDropDownButton.DropDownItems)
            {
                item.Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
                item.ForeColor = this.ForeColor;
            }

            this.ApplyDarkThemeToDropDown(
                this._GroupsDropDownButton,
                this._IsDarkThemeActive == true ? Color.FromArgb(43, 46, 54) : Color.White,
                this.ForeColor);
        }

        private string GetGroupsButtonText()
        {
            string filterName = this.GetActiveGroupFilterName();
            if (string.IsNullOrWhiteSpace(filterName) == true)
            {
                return "Groups";
            }

            return $"Groups ({filterName})";
        }

        private void UpdateGroupsButtonText()
        {
            if (this._GroupsDropDownButton != null)
            {
                this._GroupsDropDownButton.Text = this.GetGroupsButtonText();
            }

            this.RefreshGroupsSidebar();
            this.RefreshGroupsPage();
        }

        private void OnGroupsMenuOpening(object sender, EventArgs e)
        {
            bool hasGroups = this._Groups.Count > 0;
            bool hasSelection = this.GetSelectedVisibleGameIds().Count > 0;
            GameGroup activeFilterGroup = this.GetActiveGroupFilter();

            this._ManageGroupsMenuItem.Enabled = true;
            this._RenameGroupMenuItem.Enabled = hasGroups;
            this._DeleteGroupMenuItem.Enabled = hasGroups;
            this._AddSelectedGamesToGroupMenuItem.Enabled = hasGroups && hasSelection;
            this._RemoveSelectedGamesFromGroupMenuItem.Enabled = hasGroups && hasSelection;
            this._ShowOnlyGroupGamesMenuItem.Enabled = hasGroups;
            this._ShowOnlyGroupGamesMenuItem.Checked = activeFilterGroup != null;
            this._ClearGroupFilterMenuItem.Enabled = activeFilterGroup != null;
            this._ClearGroupFilterMenuItem.Text = activeFilterGroup != null
                ? $"Clear Group Filter ({activeFilterGroup.Name})"
                : "Clear Group Filter";
            this._RunGroupMenuItem.Enabled = hasGroups && this.IsGroupRunBusy == false;
        }

        private GameGroup GetActiveGroupFilter()
        {
            if (string.IsNullOrWhiteSpace(this._ActiveGroupFilterId) == true)
            {
                return null;
            }

            GameGroup group = this._Groups.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, this._ActiveGroupFilterId, StringComparison.Ordinal));
            if (group != null)
            {
                return group;
            }

            this._ActiveGroupFilterId = null;
            return null;
        }

        private string GetActiveGroupFilterName()
        {
            return this.GetActiveGroupFilter()?.Name;
        }

        private bool IsGameFilteredOutByGroup(uint gameId)
        {
            GameGroup group = this.GetActiveGroupFilter();
            if (group == null)
            {
                return false;
            }

            return group.AppIds.Contains(gameId) == false;
        }

        private static string GetGroupsDatabasePath()
        {
            return Path.Combine(Application.StartupPath, "data", "groups", "groups.json");
        }

        private static bool TryReadGroupsDatabase(string path, out GroupsDatabaseDocument database)
        {
            database = null;
            if (string.IsNullOrWhiteSpace(path) == true || File.Exists(path) == false)
            {
                return false;
            }

            try
            {
                using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                DataContractJsonSerializer serializer = new(typeof(GroupsDatabaseDocument));
                database = serializer.ReadObject(stream) as GroupsDatabaseDocument;
                return database != null;
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

        private void LoadGroupsDatabase()
        {
            this._Groups.Clear();

            string path = GetGroupsDatabasePath();
            if (TryReadGroupsDatabase(path, out GroupsDatabaseDocument database) == false || database == null)
            {
                return;
            }

            foreach (GroupRecord record in database.Groups ?? Array.Empty<GroupRecord>())
            {
                if (record == null)
                {
                    continue;
                }

                string name = record.Name?.Trim();
                if (string.IsNullOrWhiteSpace(name) == true)
                {
                    continue;
                }

                string id = string.IsNullOrWhiteSpace(record.Id) == false
                    ? record.Id.Trim()
                    : Guid.NewGuid().ToString("N");
                if (this._Groups.Any(group => string.Equals(group.Id, id, StringComparison.Ordinal)) == true)
                {
                    id = Guid.NewGuid().ToString("N");
                }

                name = this.GetUniqueGroupName(name);

                GameGroup group = new()
                {
                    Id = id,
                    Name = name,
                };

                foreach (uint appId in record.AppIds ?? Array.Empty<uint>())
                {
                    if (group.AppIds.Count >= MaxGamesPerGroup)
                    {
                        break;
                    }

                    if (appId == 0)
                    {
                        continue;
                    }

                    group.AppIds.Add(appId);
                }

                this._Groups.Add(group);
            }

            this.SortGroups();
        }

        private void EnsureGroupsDatabaseFileExists()
        {
            string path = GetGroupsDatabasePath();
            if (File.Exists(path) == true)
            {
                return;
            }

            TrySaveGroupsDatabase(out _);
        }

        private bool TrySaveGroupsDatabase(out string errorMessage)
        {
            errorMessage = null;
            string path = GetGroupsDatabasePath();

            try
            {
                string directory = Path.GetDirectoryName(path);
                if (string.IsNullOrWhiteSpace(directory) == false)
                {
                    Directory.CreateDirectory(directory);
                }

                GroupsDatabaseDocument document = new()
                {
                    UpdatedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    Groups = this._Groups
                        .OrderBy(group => group.Name, StringComparer.CurrentCultureIgnoreCase)
                        .ThenBy(group => group.Id, StringComparer.Ordinal)
                        .Select(group => new GroupRecord()
                        {
                            Id = group.Id,
                            Name = group.Name,
                            AppIds = group.AppIds
                                .Where(id => id != 0)
                                .Distinct()
                                .OrderBy(id => id)
                                .Take(MaxGamesPerGroup)
                                .ToArray(),
                        })
                        .ToArray(),
                };

                using FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.Read);
                DataContractJsonSerializer serializer = new(typeof(GroupsDatabaseDocument));
                serializer.WriteObject(stream, document);
                return true;
            }
            catch (IOException ex)
            {
                errorMessage = ex.Message;
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                errorMessage = ex.Message;
                return false;
            }
            catch (SerializationException ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        private bool TrySaveGroupsDatabaseOrShowError(string title)
        {
            if (this.TrySaveGroupsDatabase(out string error) == true)
            {
                this.RefreshGroupsSidebar();
                this.RefreshGroupsPage();
                return true;
            }

            MessageBox.Show(
                this,
                $"Could not save groups database:{Environment.NewLine}{error}",
                title,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return false;
        }

        private void ApplyDialogTheme(Form dialog)
        {
            if (dialog == null || this._IsDarkThemeActive == false)
            {
                return;
            }

            DashboardThemeTokens theme = this._DashboardTheme ?? DashboardThemeTokens.CreateDark(Color.FromArgb(106, 173, 255));
            Color dialogBackColor = BlendColor(theme.AppBackground, Color.Black, 0.20f);
            Color panelBackColor = theme.PanelBackground;
            Color inputBackColor = theme.InputBackground;
            Color borderColor = theme.InputBorder;
            Color textColor = theme.TextPrimary;
            Color mutedTextColor = theme.TextMuted;
            Color accentColor = theme.Accent;

            dialog.BackColor = dialogBackColor;
            dialog.ForeColor = textColor;
            this.ApplyDialogThemeToControlTree(
                dialog,
                dialogBackColor,
                panelBackColor,
                inputBackColor,
                borderColor,
                textColor,
                mutedTextColor,
                accentColor);

            dialog.Shown += (_, _) =>
            {
                this.ApplyDialogThemeToControlTree(
                    dialog,
                    dialogBackColor,
                    panelBackColor,
                    inputBackColor,
                    borderColor,
                    textColor,
                    mutedTextColor,
                    accentColor);
            };
        }

        private void ApplyDialogThemeToControlTree(
            Control root,
            Color dialogBackColor,
            Color panelBackColor,
            Color inputBackColor,
            Color borderColor,
            Color textColor,
            Color mutedTextColor,
            Color accentColor)
        {
            if (root == null)
            {
                return;
            }

            if (root is Label label)
            {
                label.ForeColor = label.Height <= 20 ? mutedTextColor : textColor;
                label.BackColor = Color.Transparent;
            }
            else if (root is Button button)
            {
                button.UseVisualStyleBackColor = false;
                button.BackColor = panelBackColor;
                button.ForeColor = textColor;
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderSize = 1;
                button.FlatAppearance.BorderColor = borderColor;
                button.FlatAppearance.MouseOverBackColor = BlendColor(panelBackColor, accentColor, 0.15f);
                button.FlatAppearance.MouseDownBackColor = BlendColor(panelBackColor, accentColor, 0.22f);
            }
            else if (root is TextBox textBox)
            {
                textBox.BackColor = inputBackColor;
                textBox.ForeColor = textColor;
                textBox.BorderStyle = BorderStyle.FixedSingle;
                this.ApplyDarkExplorerThemeToControl(textBox);
            }
            else if (root is ComboBox comboBox)
            {
                comboBox.BackColor = inputBackColor;
                comboBox.ForeColor = textColor;
                comboBox.FlatStyle = FlatStyle.Flat;
                this.ApplyDarkExplorerThemeToControl(comboBox);
            }
            else if (root is ListBox listBox)
            {
                listBox.BackColor = inputBackColor;
                listBox.ForeColor = textColor;
                listBox.BorderStyle = BorderStyle.FixedSingle;
                this.ApplyDarkExplorerThemeToControl(listBox);
            }
            else if (root is NumericUpDown numeric)
            {
                numeric.BackColor = inputBackColor;
                numeric.ForeColor = textColor;
                numeric.BorderStyle = BorderStyle.FixedSingle;
                this.ApplyDarkExplorerThemeToControl(numeric);
            }
            else if (root is Panel ||
                     root is TableLayoutPanel ||
                     root is FlowLayoutPanel ||
                     root is Form)
            {
                root.BackColor = dialogBackColor;
                root.ForeColor = textColor;
            }
            else
            {
                root.ForeColor = textColor;
            }

            foreach (Control child in root.Controls)
            {
                this.ApplyDialogThemeToControlTree(
                    child,
                    dialogBackColor,
                    panelBackColor,
                    inputBackColor,
                    borderColor,
                    textColor,
                    mutedTextColor,
                    accentColor);
            }
        }

        private void ApplyDarkExplorerThemeToControl(Control control)
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

        private bool TryPromptForText(
            IWin32Window owner,
            string title,
            string prompt,
            string initialValue,
            out string value)
        {
            value = null;

            using Form dialog = new()
            {
                Text = title,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                ShowInTaskbar = false,
                MaximizeBox = false,
                MinimizeBox = false,
                ClientSize = new Size(430, 146),
            };

            Label promptLabel = new()
            {
                AutoSize = false,
                Left = 12,
                Top = 12,
                Width = 406,
                Height = 34,
                Text = prompt ?? "",
            };

            TextBox textBox = new()
            {
                Left = 12,
                Top = 52,
                Width = 406,
                Text = initialValue ?? "",
            };

            Button okButton = new()
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Left = 262,
                Top = 102,
                Width = 75,
            };

            Button cancelButton = new()
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Left = 343,
                Top = 102,
                Width = 75,
            };

            dialog.Controls.Add(promptLabel);
            dialog.Controls.Add(textBox);
            dialog.Controls.Add(okButton);
            dialog.Controls.Add(cancelButton);
            dialog.AcceptButton = okButton;
            dialog.CancelButton = cancelButton;
            this.ApplyDialogTheme(dialog);

            if (dialog.ShowDialog(owner) != DialogResult.OK)
            {
                return false;
            }

            value = textBox.Text?.Trim();
            return true;
        }

        private bool TryPromptForDurationHours(
            IWin32Window owner,
            out double hours)
        {
            hours = 0;

            using Form dialog = new()
            {
                Text = "Run Group",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                ShowInTaskbar = false,
                MaximizeBox = false,
                MinimizeBox = false,
                ClientSize = new Size(360, 126),
            };

            Label label = new()
            {
                AutoSize = false,
                Left = 12,
                Top = 12,
                Width = 336,
                Height = 18,
                Text = "How many hours should this group run?",
            };

            NumericUpDown numeric = new()
            {
                Left = 12,
                Top = 38,
                Width = 336,
                DecimalPlaces = 2,
                Minimum = 0.01m,
                Maximum = 100000m,
                Increment = 0.25m,
                Value = 1m,
            };

            Button okButton = new()
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Left = 180,
                Top = 88,
                Width = 80,
            };

            Button cancelButton = new()
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Left = 268,
                Top = 88,
                Width = 80,
            };

            dialog.Controls.Add(label);
            dialog.Controls.Add(numeric);
            dialog.Controls.Add(okButton);
            dialog.Controls.Add(cancelButton);
            dialog.AcceptButton = okButton;
            dialog.CancelButton = cancelButton;
            this.ApplyDialogTheme(dialog);

            if (dialog.ShowDialog(owner) != DialogResult.OK)
            {
                return false;
            }

            hours = (double)numeric.Value;
            return true;
        }

        private bool TrySelectGroup(string title, string prompt, out GameGroup selectedGroup)
        {
            selectedGroup = null;

            if (this._Groups.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "No groups exist yet.",
                    "Info",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return false;
            }

            List<GroupListItem> items = this._Groups
                .OrderBy(group => group.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(group => group.Id, StringComparer.Ordinal)
                .Select(group => new GroupListItem(group))
                .ToList();

            using Form dialog = new()
            {
                Text = title,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                ShowInTaskbar = false,
                MaximizeBox = false,
                MinimizeBox = false,
                ClientSize = new Size(430, 340),
            };

            Label promptLabel = new()
            {
                AutoSize = false,
                Left = 12,
                Top = 12,
                Width = 406,
                Height = 22,
                Text = prompt,
            };

            ListBox listBox = new()
            {
                Left = 12,
                Top = 38,
                Width = 406,
                Height = 248,
            };
            listBox.Items.AddRange(items.Cast<object>().ToArray());
            if (listBox.Items.Count > 0)
            {
                listBox.SelectedIndex = 0;
            }

            Button okButton = new()
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Left = 262,
                Top = 300,
                Width = 75,
            };

            Button cancelButton = new()
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Left = 343,
                Top = 300,
                Width = 75,
            };

            listBox.DoubleClick += (_, _) =>
            {
                if (listBox.SelectedItem == null)
                {
                    return;
                }

                dialog.DialogResult = DialogResult.OK;
                dialog.Close();
            };

            dialog.Controls.Add(promptLabel);
            dialog.Controls.Add(listBox);
            dialog.Controls.Add(okButton);
            dialog.Controls.Add(cancelButton);
            dialog.AcceptButton = okButton;
            dialog.CancelButton = cancelButton;
            this.ApplyDialogTheme(dialog);

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return false;
            }

            if (listBox.SelectedItem is not GroupListItem item ||
                item.Group == null)
            {
                return false;
            }

            selectedGroup = item.Group;
            return true;
        }

        private string GetUniqueGroupName(string baseName, GameGroup ignoredGroup = null)
        {
            string normalized = (baseName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(normalized) == true)
            {
                normalized = "New Group";
            }

            string candidate = normalized;
            int suffix = 2;
            while (this._Groups.Any(group =>
                       ReferenceEquals(group, ignoredGroup) == false &&
                       string.Equals(group.Name, candidate, StringComparison.CurrentCultureIgnoreCase)))
            {
                candidate = $"{normalized} ({suffix.ToString(CultureInfo.InvariantCulture)})";
                suffix++;
            }

            return candidate;
        }

        private bool ValidateGroupName(
            string rawName,
            GameGroup ignoredGroup,
            out string normalizedName)
        {
            normalizedName = rawName?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedName) == true)
            {
                MessageBox.Show(
                    this,
                    "Group name cannot be empty.",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return false;
            }

            if (normalizedName.Length > 96)
            {
                MessageBox.Show(
                    this,
                    "Group name is too long (max 96 characters).",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return false;
            }

            string candidateName = normalizedName;
            bool alreadyExists = this._Groups.Any(group =>
                ReferenceEquals(group, ignoredGroup) == false &&
                string.Equals(group.Name, candidateName, StringComparison.CurrentCultureIgnoreCase));
            if (alreadyExists == true)
            {
                MessageBox.Show(
                    this,
                    "A group with this name already exists.",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return false;
            }

            return true;
        }

        private void SortGroups()
        {
            this._Groups.Sort((left, right) =>
            {
                int nameComparison = StringComparer.CurrentCultureIgnoreCase.Compare(
                    left?.Name ?? "",
                    right?.Name ?? "");
                if (nameComparison != 0)
                {
                    return nameComparison;
                }

                return StringComparer.Ordinal.Compare(left?.Id ?? "", right?.Id ?? "");
            });
        }

        private string GetGameDisplayName(uint appId)
        {
            if (this._Games.TryGetValue(appId, out GameInfo info) == true &&
                string.IsNullOrWhiteSpace(info?.Name) == false)
            {
                return info.Name;
            }

            return $"App {appId.ToString(CultureInfo.InvariantCulture)}";
        }

        private void CreateGroup(IWin32Window owner)
        {
            if (TryPromptForText(owner, "Create Group", "Enter group name:", "", out string rawName) == false)
            {
                return;
            }

            if (this.ValidateGroupName(rawName, null, out string normalizedName) == false)
            {
                return;
            }

            GameGroup group = new()
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = normalizedName,
            };

            this._Groups.Add(group);
            this.SortGroups();
            if (this.TrySaveGroupsDatabaseOrShowError("Create Group") == false)
            {
                this._Groups.Remove(group);
                return;
            }

            this.UpdateGroupsButtonText();
            this.RefreshGroupsSidebar(group.Id);
            this.UpdatePickerStatus();
        }

        private void RenameGroup(IWin32Window owner, GameGroup group)
        {
            if (group == null)
            {
                return;
            }

            string currentName = group.Name;
            if (TryPromptForText(owner, "Rename Group", "Enter the new name:", currentName, out string rawName) == false)
            {
                return;
            }

            if (this.ValidateGroupName(rawName, group, out string normalizedName) == false)
            {
                return;
            }

            group.Name = normalizedName;
            this.SortGroups();
            if (this.TrySaveGroupsDatabaseOrShowError("Rename Group") == false)
            {
                group.Name = currentName;
                this.SortGroups();
                return;
            }

            this.UpdateGroupsButtonText();
            this.RefreshGroupsSidebar(group.Id);
            this.UpdatePickerStatus();
        }

        private void DeleteGroup(IWin32Window owner, GameGroup group)
        {
            if (group == null)
            {
                return;
            }

            DialogResult result = MessageBox.Show(
                owner,
                $"Delete group \"{group.Name}\"?",
                "Delete Group",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (result != DialogResult.Yes)
            {
                return;
            }

            int oldIndex = this._Groups.IndexOf(group);
            this._Groups.Remove(group);
            if (this.TrySaveGroupsDatabaseOrShowError("Delete Group") == false)
            {
                if (oldIndex < 0 || oldIndex > this._Groups.Count)
                {
                    this._Groups.Add(group);
                }
                else
                {
                    this._Groups.Insert(oldIndex, group);
                }

                this.SortGroups();
                return;
            }

            bool wasActiveFilter = string.Equals(this._ActiveGroupFilterId, group.Id, StringComparison.Ordinal);
            if (wasActiveFilter == true)
            {
                this._ActiveGroupFilterId = null;
            }

            this.UpdateGroupsButtonText();
            this.RefreshGroupsSidebar();
            this.UpdatePickerStatus();
            if (wasActiveFilter == true)
            {
                this.RefreshGames();
            }
        }

        private void AddGamesToGroup(IWin32Window owner, GameGroup group, List<uint> selectedGameIds)
        {
            if (group == null)
            {
                return;
            }

            if (selectedGameIds == null || selectedGameIds.Count == 0)
            {
                MessageBox.Show(
                    owner,
                    "Select one or more games first.",
                    "Info",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            int availableSlots = MaxGamesPerGroup - group.AppIds.Count;
            if (availableSlots <= 0)
            {
                MessageBox.Show(
                    owner,
                    $"This group already contains the maximum of {MaxGamesPerGroup} games.",
                    "Info",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            List<uint> candidates = new();
            HashSet<uint> seen = new();
            foreach (uint gameId in selectedGameIds)
            {
                if (gameId == 0 || seen.Add(gameId) == false)
                {
                    continue;
                }

                if (group.AppIds.Contains(gameId) == true)
                {
                    continue;
                }

                candidates.Add(gameId);
            }

            if (candidates.Count == 0)
            {
                MessageBox.Show(
                    owner,
                    "All selected games are already in the chosen group.",
                    "Info",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            HashSet<uint> original = new(group.AppIds);
            int added = 0;
            foreach (uint gameId in candidates)
            {
                if (availableSlots <= 0)
                {
                    break;
                }

                if (group.AppIds.Add(gameId) == true)
                {
                    added++;
                    availableSlots--;
                }
            }

            if (this.TrySaveGroupsDatabaseOrShowError("Add Games To Group") == false)
            {
                group.AppIds.Clear();
                group.AppIds.UnionWith(original);
                return;
            }

            this.UpdateGroupsButtonText();
            this.RefreshGroupsSidebar(group.Id);
            this.UpdatePickerStatus();
            if (string.Equals(this._ActiveGroupFilterId, group.Id, StringComparison.Ordinal) == true)
            {
                this.RefreshGames();
            }

            if (added < candidates.Count)
            {
                MessageBox.Show(
                    owner,
                    $"Added {added} game(s). Group limit is {MaxGamesPerGroup}, so {candidates.Count - added} game(s) were not added.",
                    "Info",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        private void RemoveGamesFromGroup(IWin32Window owner, GameGroup group, List<uint> selectedGameIds)
        {
            if (group == null)
            {
                return;
            }

            if (selectedGameIds == null || selectedGameIds.Count == 0)
            {
                MessageBox.Show(
                    owner,
                    "Select one or more games first.",
                    "Info",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            HashSet<uint> original = new(group.AppIds);
            int removed = 0;
            foreach (uint gameId in selectedGameIds.Distinct())
            {
                if (group.AppIds.Remove(gameId) == true)
                {
                    removed++;
                }
            }

            if (removed == 0)
            {
                MessageBox.Show(
                    owner,
                    "None of the selected games exist in the chosen group.",
                    "Info",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (this.TrySaveGroupsDatabaseOrShowError("Remove Games From Group") == false)
            {
                group.AppIds.Clear();
                group.AppIds.UnionWith(original);
                return;
            }

            this.UpdateGroupsButtonText();
            this.RefreshGroupsSidebar(group.Id);
            this.UpdatePickerStatus();
            if (string.Equals(this._ActiveGroupFilterId, group.Id, StringComparison.Ordinal) == true)
            {
                this.RefreshGames();
            }
        }

        private void ApplyGroupFilter(GameGroup group)
        {
            if (group == null)
            {
                return;
            }

            this._ActiveGroupFilterId = group.Id;
            this.UpdateGroupsButtonText();
            this.RefreshGames();
        }

        private void ClearGroupFilter()
        {
            if (string.IsNullOrWhiteSpace(this._ActiveGroupFilterId) == true)
            {
                return;
            }

            this._ActiveGroupFilterId = null;
            this.UpdateGroupsButtonText();
            this.RefreshGames();
        }

        private void RunGroup(IWin32Window owner, GameGroup group)
        {
            if (group == null)
            {
                return;
            }

            if (this._ListWorker.IsBusy == true ||
                this._AchievementWorker.IsBusy == true ||
                this._UnlockAllWorker.IsBusy == true ||
                this.IsGroupRunBusy == true)
            {
                MessageBox.Show(
                    owner,
                    "Another operation is in progress. Please wait for it to finish.",
                    "Info",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (TryPromptForDurationHours(owner, out double hours) == false)
            {
                return;
            }

            this.StartGroupRun(group, hours);
            this.UpdateGroupsSidebarActionState();
        }

        private void OnManageGroups(object sender, EventArgs e)
        {
            this.ShowGroupManagerDialog();
        }

        private void ShowGroupManagerDialog(string preferredGroupId = null)
        {
            using Form dialog = new()
            {
                Text = "Manage Groups",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                ShowInTaskbar = false,
                MaximizeBox = false,
                MinimizeBox = false,
                ClientSize = new Size(962, 610),
            };

            Label groupLabel = new()
            {
                Left = 12,
                Top = 16,
                Width = 45,
                Text = "Group:",
            };
            ComboBox groupCombo = new()
            {
                Left = 62,
                Top = 12,
                Width = 360,
                DropDownStyle = ComboBoxStyle.DropDownList,
            };
            Button createButton = new()
            {
                Text = "Create",
                Left = 430,
                Top = 11,
                Width = 78,
            };
            Button renameButton = new()
            {
                Text = "Rename",
                Left = 514,
                Top = 11,
                Width = 78,
            };
            Button deleteButton = new()
            {
                Text = "Delete",
                Left = 598,
                Top = 11,
                Width = 78,
            };

            Label searchLabel = new()
            {
                Left = 12,
                Top = 49,
                Width = 45,
                Text = "Search:",
            };
            TextBox searchTextBox = new()
            {
                Left = 62,
                Top = 45,
                Width = 360,
            };

            Label availableLabel = new()
            {
                Left = 12,
                Top = 78,
                Width = 300,
                Text = "Available games",
            };
            ListBox availableList = new()
            {
                Left = 12,
                Top = 100,
                Width = 430,
                Height = 430,
                SelectionMode = SelectionMode.MultiExtended,
            };

            Label inGroupLabel = new()
            {
                Left = 520,
                Top = 78,
                Width = 300,
                Text = "Games in selected group",
            };
            ListBox inGroupList = new()
            {
                Left = 520,
                Top = 100,
                Width = 430,
                Height = 430,
                SelectionMode = SelectionMode.MultiExtended,
            };

            Button addButton = new()
            {
                Text = "Add >>",
                Left = 450,
                Top = 255,
                Width = 64,
            };
            Button removeButton = new()
            {
                Text = "<< Remove",
                Left = 446,
                Top = 292,
                Width = 72,
            };

            Label tipsLabel = new()
            {
                Left = 12,
                Top = 536,
                Width = 560,
                Height = 34,
                Text = $"Tip: select one or more games on either side, then use Add/Remove. Max {MaxGamesPerGroup} games per group.",
            };
            Label counterLabel = new()
            {
                Left = 520,
                Top = 536,
                Width = 430,
                TextAlign = ContentAlignment.MiddleRight,
                Text = $"0/{MaxGamesPerGroup}",
            };

            Button showOnlyThisGroupButton = new()
            {
                Text = "Show Only This Group",
                Left = 12,
                Top = 574,
                Width = 170,
            };
            Button clearFilterButton = new()
            {
                Text = "Clear Group Filter",
                Left = 188,
                Top = 574,
                Width = 150,
            };
            Button closeButton = new()
            {
                Text = "Close",
                DialogResult = DialogResult.OK,
                Left = 875,
                Top = 574,
                Width = 75,
            };

            dialog.Controls.Add(groupLabel);
            dialog.Controls.Add(groupCombo);
            dialog.Controls.Add(createButton);
            dialog.Controls.Add(renameButton);
            dialog.Controls.Add(deleteButton);
            dialog.Controls.Add(searchLabel);
            dialog.Controls.Add(searchTextBox);
            dialog.Controls.Add(availableLabel);
            dialog.Controls.Add(availableList);
            dialog.Controls.Add(inGroupLabel);
            dialog.Controls.Add(inGroupList);
            dialog.Controls.Add(addButton);
            dialog.Controls.Add(removeButton);
            dialog.Controls.Add(tipsLabel);
            dialog.Controls.Add(counterLabel);
            dialog.Controls.Add(showOnlyThisGroupButton);
            dialog.Controls.Add(clearFilterButton);
            dialog.Controls.Add(closeButton);
            dialog.AcceptButton = closeButton;
            dialog.CancelButton = closeButton;
            this.ApplyDialogTheme(dialog);

            bool suppressEvents = false;

            GameGroup GetSelectedGroup()
            {
                return (groupCombo.SelectedItem as GroupListItem)?.Group;
            }

            void PopulateGroupCombo(string preferredGroupId)
            {
                suppressEvents = true;
                try
                {
                    groupCombo.Items.Clear();
                    foreach (GameGroup group in this._Groups
                                 .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                                 .ThenBy(item => item.Id, StringComparer.Ordinal))
                    {
                        groupCombo.Items.Add(new GroupListItem(group));
                    }

                    if (groupCombo.Items.Count == 0)
                    {
                        groupCombo.SelectedIndex = -1;
                        return;
                    }

                    int preferredIndex = -1;
                    if (string.IsNullOrWhiteSpace(preferredGroupId) == false)
                    {
                        for (int i = 0; i < groupCombo.Items.Count; i++)
                        {
                            if (groupCombo.Items[i] is not GroupListItem item ||
                                item.Group == null)
                            {
                                continue;
                            }

                            if (string.Equals(item.Group.Id, preferredGroupId, StringComparison.Ordinal) == true)
                            {
                                preferredIndex = i;
                                break;
                            }
                        }
                    }

                    if (preferredIndex < 0)
                    {
                        preferredIndex = 0;
                    }

                    groupCombo.SelectedIndex = preferredIndex;
                }
                finally
                {
                    suppressEvents = false;
                }
            }

            void RefreshLists()
            {
                if (suppressEvents == true)
                {
                    return;
                }

                GameGroup selectedGroup = GetSelectedGroup();

                renameButton.Enabled = selectedGroup != null;
                deleteButton.Enabled = selectedGroup != null;
                addButton.Enabled = selectedGroup != null;
                removeButton.Enabled = selectedGroup != null;
                showOnlyThisGroupButton.Enabled = selectedGroup != null;
                clearFilterButton.Enabled = string.IsNullOrWhiteSpace(this._ActiveGroupFilterId) == false;

                availableList.BeginUpdate();
                inGroupList.BeginUpdate();
                try
                {
                    availableList.Items.Clear();
                    inGroupList.Items.Clear();

                    if (selectedGroup == null)
                    {
                        counterLabel.Text = $"0/{MaxGamesPerGroup}";
                        return;
                    }

                    IEnumerable<GroupGameListItem> inGroupItems = selectedGroup.AppIds
                        .Where(id => id != 0)
                        .Select(id => new GroupGameListItem(id, this.GetGameDisplayName(id)))
                        .OrderBy(item => item.ToString(), StringComparer.CurrentCultureIgnoreCase);
                    foreach (GroupGameListItem item in inGroupItems)
                    {
                        inGroupList.Items.Add(item);
                    }

                    string search = searchTextBox.Text?.Trim();
                    IEnumerable<GameInfo> availableCandidates = this._Games.Values
                        .Where(info => info != null && selectedGroup.AppIds.Contains(info.Id) == false)
                        .OrderBy(info => info.Name, StringComparer.CurrentCultureIgnoreCase)
                        .ThenBy(info => info.Id);
                    foreach (GameInfo info in availableCandidates)
                    {
                        if (string.IsNullOrWhiteSpace(search) == false)
                        {
                            string appIdText = info.Id.ToString(CultureInfo.InvariantCulture);
                            bool matchesName = info.Name?.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
                            bool matchesAppId = appIdText.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
                            if (matchesName == false && matchesAppId == false)
                            {
                                continue;
                            }
                        }

                        availableList.Items.Add(new GroupGameListItem(info.Id, info.Name));
                    }

                    counterLabel.Text = $"{selectedGroup.AppIds.Count}/{MaxGamesPerGroup} in group";
                }
                finally
                {
                    inGroupList.EndUpdate();
                    availableList.EndUpdate();
                }
            }

            bool PersistAndRefresh(string title, HashSet<uint> rollbackSet, GameGroup targetGroup)
            {
                if (this.TrySaveGroupsDatabaseOrShowError(title) == true)
                {
                    this.UpdateGroupsButtonText();
                    this.UpdatePickerStatus();
                    return true;
                }

                if (rollbackSet != null && targetGroup != null)
                {
                    targetGroup.AppIds.Clear();
                    targetGroup.AppIds.UnionWith(rollbackSet);
                }

                return false;
            }

            createButton.Click += (_, _) =>
            {
                if (TryPromptForText(dialog, "Create Group", "Enter group name:", "", out string rawName) == false)
                {
                    return;
                }

                if (this.ValidateGroupName(rawName, null, out string normalizedName) == false)
                {
                    return;
                }

                GameGroup group = new()
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = normalizedName,
                };

                this._Groups.Add(group);
                this.SortGroups();
                if (this.TrySaveGroupsDatabaseOrShowError("Create Group") == false)
                {
                    this._Groups.Remove(group);
                    return;
                }

                PopulateGroupCombo(group.Id);
                this.UpdateGroupsButtonText();
                this.UpdatePickerStatus();
                RefreshLists();
            };

            renameButton.Click += (_, _) =>
            {
                GameGroup selectedGroup = GetSelectedGroup();
                if (selectedGroup == null)
                {
                    return;
                }

                string currentName = selectedGroup.Name;
                if (TryPromptForText(dialog, "Rename Group", "Enter the new name:", currentName, out string rawName) == false)
                {
                    return;
                }

                if (this.ValidateGroupName(rawName, selectedGroup, out string normalizedName) == false)
                {
                    return;
                }

                selectedGroup.Name = normalizedName;
                this.SortGroups();
                if (this.TrySaveGroupsDatabaseOrShowError("Rename Group") == false)
                {
                    selectedGroup.Name = currentName;
                    this.SortGroups();
                    return;
                }

                PopulateGroupCombo(selectedGroup.Id);
                this.UpdateGroupsButtonText();
                this.UpdatePickerStatus();
                RefreshLists();
            };

            deleteButton.Click += (_, _) =>
            {
                GameGroup selectedGroup = GetSelectedGroup();
                if (selectedGroup == null)
                {
                    return;
                }

                DialogResult result = MessageBox.Show(
                    dialog,
                    $"Delete group \"{selectedGroup.Name}\"?",
                    "Delete Group",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (result != DialogResult.Yes)
                {
                    return;
                }

                int oldIndex = this._Groups.IndexOf(selectedGroup);
                this._Groups.Remove(selectedGroup);
                if (this.TrySaveGroupsDatabaseOrShowError("Delete Group") == false)
                {
                    if (oldIndex < 0 || oldIndex > this._Groups.Count)
                    {
                        this._Groups.Add(selectedGroup);
                    }
                    else
                    {
                        this._Groups.Insert(oldIndex, selectedGroup);
                    }

                    this.SortGroups();
                    return;
                }

                if (string.Equals(this._ActiveGroupFilterId, selectedGroup.Id, StringComparison.Ordinal) == true)
                {
                    this._ActiveGroupFilterId = null;
                    this.UpdateGroupsButtonText();
                    this.RefreshGames();
                }

                PopulateGroupCombo(null);
                this.UpdateGroupsButtonText();
                this.UpdatePickerStatus();
                RefreshLists();
            };

            addButton.Click += (_, _) =>
            {
                GameGroup selectedGroup = GetSelectedGroup();
                if (selectedGroup == null)
                {
                    return;
                }

                List<uint> selectedAppIds = availableList.SelectedItems
                    .OfType<GroupGameListItem>()
                    .Select(item => item.AppId)
                    .Distinct()
                    .ToList();
                if (selectedAppIds.Count == 0)
                {
                    return;
                }

                int availableSlots = MaxGamesPerGroup - selectedGroup.AppIds.Count;
                if (availableSlots <= 0)
                {
                    MessageBox.Show(
                        dialog,
                        $"This group already contains the maximum of {MaxGamesPerGroup} games.",
                        "Info",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                HashSet<uint> rollback = new(selectedGroup.AppIds);
                int added = 0;
                foreach (uint appId in selectedAppIds)
                {
                    if (availableSlots <= 0)
                    {
                        break;
                    }

                    if (selectedGroup.AppIds.Add(appId) == true)
                    {
                        added++;
                        availableSlots--;
                    }
                }

                if (PersistAndRefresh("Add Games To Group", rollback, selectedGroup) == false)
                {
                    RefreshLists();
                    return;
                }
                RefreshLists();
                if (string.Equals(this._ActiveGroupFilterId, selectedGroup.Id, StringComparison.Ordinal) == true)
                {
                    this.RefreshGames();
                }

                if (added < selectedAppIds.Count)
                {
                    MessageBox.Show(
                        dialog,
                        $"Added {added} game(s). Group limit is {MaxGamesPerGroup}, so {selectedAppIds.Count - added} game(s) were not added.",
                        "Info",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            };

            removeButton.Click += (_, _) =>
            {
                GameGroup selectedGroup = GetSelectedGroup();
                if (selectedGroup == null)
                {
                    return;
                }

                List<uint> selectedAppIds = inGroupList.SelectedItems
                    .OfType<GroupGameListItem>()
                    .Select(item => item.AppId)
                    .Distinct()
                    .ToList();
                if (selectedAppIds.Count == 0)
                {
                    return;
                }

                HashSet<uint> rollback = new(selectedGroup.AppIds);
                foreach (uint appId in selectedAppIds)
                {
                    selectedGroup.AppIds.Remove(appId);
                }

                if (PersistAndRefresh("Remove Games From Group", rollback, selectedGroup) == false)
                {
                    RefreshLists();
                    return;
                }
                RefreshLists();
                if (string.Equals(this._ActiveGroupFilterId, selectedGroup.Id, StringComparison.Ordinal) == true)
                {
                    this.RefreshGames();
                }
            };

            showOnlyThisGroupButton.Click += (_, _) =>
            {
                GameGroup selectedGroup = GetSelectedGroup();
                if (selectedGroup == null)
                {
                    return;
                }

                this._ActiveGroupFilterId = selectedGroup.Id;
                this.UpdateGroupsButtonText();
                this.RefreshGames();
                RefreshLists();
            };

            clearFilterButton.Click += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(this._ActiveGroupFilterId) == true)
                {
                    return;
                }

                this._ActiveGroupFilterId = null;
                this.UpdateGroupsButtonText();
                this.RefreshGames();
                RefreshLists();
            };

            groupCombo.SelectedIndexChanged += (_, _) => RefreshLists();
            searchTextBox.TextChanged += (_, _) => RefreshLists();

            string initialGroupId = string.IsNullOrWhiteSpace(preferredGroupId) == false
                ? preferredGroupId
                : this._ActiveGroupFilterId;
            PopulateGroupCombo(initialGroupId);
            RefreshLists();

            dialog.ShowDialog(this);
            this.RefreshGroupsSidebar();
            this.UpdateGroupsSidebarActionState();
        }

        private void OnCreateGroup(object sender, EventArgs e)
        {
            this.CreateGroup(this);
        }

        private void OnRenameGroup(object sender, EventArgs e)
        {
            if (this.TrySelectGroup("Rename Group", "Select a group to rename:", out GameGroup group) == false)
            {
                return;
            }

            this.RenameGroup(this, group);
        }

        private void OnDeleteGroup(object sender, EventArgs e)
        {
            if (this.TrySelectGroup("Delete Group", "Select a group to delete:", out GameGroup group) == false)
            {
                return;
            }

            this.DeleteGroup(this, group);
        }

        private void OnAddSelectedGamesToGroup(object sender, EventArgs e)
        {
            List<uint> selectedGameIds = this.GetSelectedVisibleGameIdsInDisplayOrder();
            if (selectedGameIds.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "Select one or more games first, or use Groups > Manage Groups...",
                    "Info",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (this.TrySelectGroup("Add Games To Group", "Select a target group:", out GameGroup group) == false)
            {
                return;
            }

            this.AddGamesToGroup(this, group, selectedGameIds);
        }

        private void OnRemoveSelectedGamesFromGroup(object sender, EventArgs e)
        {
            List<uint> selectedGameIds = this.GetSelectedVisibleGameIdsInDisplayOrder();
            if (selectedGameIds.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "Select one or more games first, or use Groups > Manage Groups...",
                    "Info",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (this.TrySelectGroup("Remove Games From Group", "Select a target group:", out GameGroup group) == false)
            {
                return;
            }

            this.RemoveGamesFromGroup(this, group, selectedGameIds);
        }

        private void OnShowOnlyGroupGames(object sender, EventArgs e)
        {
            if (this.TrySelectGroup("Show Group Games", "Select a group to show:", out GameGroup group) == false)
            {
                return;
            }

            this.ApplyGroupFilter(group);
        }

        private void OnClearGroupFilter(object sender, EventArgs e)
        {
            this.ClearGroupFilter();
        }

        private void OnRunGroup(object sender, EventArgs e)
        {
            if (this._ListWorker.IsBusy == true ||
                this._AchievementWorker.IsBusy == true ||
                this._UnlockAllWorker.IsBusy == true ||
                this.IsGroupRunBusy == true)
            {
                MessageBox.Show(
                    this,
                    "Another operation is in progress. Please wait for it to finish.",
                    "Info",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (this.TrySelectGroup("Run Group", "Select a group to run:", out GameGroup group) == false)
            {
                return;
            }

            this.RunGroup(this, group);
        }

        private void StartGroupRun(GameGroup group, double requestedHours)
        {
            List<uint> gameIds = (group?.AppIds ?? new HashSet<uint>())
                .Where(gameId => gameId != 0)
                .Distinct()
                .OrderBy(gameId => gameId)
                .ToList();
            if (gameIds.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "This group has no games.",
                    "Info",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            long requestedSecondsLong = (long)Math.Round(
                requestedHours * 3600.0,
                MidpointRounding.AwayFromZero);
            if (requestedSecondsLong < 1)
            {
                requestedSecondsLong = 1;
            }

            if (requestedSecondsLong > int.MaxValue)
            {
                requestedSecondsLong = int.MaxValue;
            }

            int totalSeconds = (int)requestedSecondsLong;
            bool adjustedForMinimumPerGame = false;
            if (totalSeconds < gameIds.Count)
            {
                totalSeconds = gameIds.Count;
                adjustedForMinimumPerGame = true;
            }

            int[] durations = BuildGroupRunDurations(totalSeconds, gameIds.Count);
            double totalHours = totalSeconds / 3600.0;
            double averageHours = (double)totalSeconds / gameIds.Count / 3600.0;

            string minimumInfo = adjustedForMinimumPerGame == true
                ? "Requested time was shorter than game count, so minimum 1 second per game was applied.\n\n"
                : "";
            DialogResult result = MessageBox.Show(
                this,
                minimumInfo +
                $"Group \"{group.Name}\" will run {gameIds.Count} games sequentially for {totalHours:0.##} hours total (about {averageHours:0.##} hour per game). Continue?",
                "Run Group",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (result != DialogResult.Yes)
            {
                return;
            }

            this._GroupRunName = group.Name;
            this._GroupRunCompleted = 0;
            this._GroupRunTotal = gameIds.Count;
            this._GroupRunFailed = 0;
            this.UpdatePickerStatus();

            if (TryStartBackgroundWorker(this._GroupRunWorker, new GroupRunRequest(group.Name, gameIds, durations)) == false)
            {
                MessageBox.Show(
                    this,
                    "Group runner is busy. Please try again.",
                    "Info",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            this.UpdateLoadingIndicator();
        }

        private static int[] BuildGroupRunDurations(int totalSeconds, int gameCount)
        {
            if (gameCount <= 0)
            {
                return Array.Empty<int>();
            }

            int[] durations = new int[gameCount];
            int baseDuration = totalSeconds / gameCount;
            int remainder = totalSeconds % gameCount;
            for (int i = 0; i < gameCount; i++)
            {
                durations[i] = baseDuration + (i < remainder ? 1 : 0);
            }

            return durations;
        }

        private void InitializeGroupRunWorker()
        {
            this._GroupRunWorker = new BackgroundWorker()
            {
                WorkerReportsProgress = true,
                WorkerSupportsCancellation = true,
            };
            this._GroupRunWorker.DoWork += this.DoGroupRun;
            this._GroupRunWorker.ProgressChanged += this.OnGroupRunProgress;
            this._GroupRunWorker.RunWorkerCompleted += this.OnGroupRunCompleted;
        }

        private void DoGroupRun(object sender, DoWorkEventArgs e)
        {
            if (sender is not BackgroundWorker worker ||
                e.Argument is not GroupRunRequest request)
            {
                return;
            }

            int completed = 0;
            for (int i = 0; i < request.GameIds.Count; i++)
            {
                if (worker.CancellationPending == true)
                {
                    e.Cancel = true;
                    return;
                }

                uint gameId = request.GameIds[i];
                int duration = i < request.DurationsSeconds.Length
                    ? request.DurationsSeconds[i]
                    : 1;

                bool success = TryIdleGame(
                    gameId,
                    duration,
                    () => worker.CancellationPending,
                    out bool cancelled,
                    out string error);
                if (cancelled == true)
                {
                    e.Cancel = true;
                    return;
                }

                int progress = Interlocked.Increment(ref completed);
                worker.ReportProgress(progress, new GroupRunProgress(gameId, duration, success, error));
            }
        }

        private void OnGroupRunProgress(object sender, ProgressChangedEventArgs e)
        {
            if (e.UserState is not GroupRunProgress progress)
            {
                return;
            }

            this._GroupRunCompleted = e.ProgressPercentage;
            if (progress.Success == false)
            {
                this._GroupRunFailed++;
            }

            string outcome = progress.Success == true
                ? $"Group idle completed in {progress.DurationSeconds} seconds."
                : $"Group idle failed: {progress.Error ?? "unknown"}";
            AppendAchievementScanLog(progress.GameId, outcome);

            this.UpdatePickerStatus();
        }

        private void OnGroupRunCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            int total = this._GroupRunTotal;
            int failed = this._GroupRunFailed;
            string name = this._GroupRunName;

            this._GroupRunCompleted = total;
            this.UpdatePickerStatus();
            this.UpdateLoadingIndicator();

            this._GroupRunName = null;
            this._GroupRunTotal = 0;
            this._GroupRunCompleted = 0;
            this._GroupRunFailed = 0;

            if (e.Error != null)
            {
                MessageBox.Show(
                    this,
                    $"Group run failed:{Environment.NewLine}{e.Error.Message}",
                    "Run Group",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            else if (e.Cancelled == true)
            {
                MessageBox.Show(
                    this,
                    "Group run was cancelled.",
                    "Run Group",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            else
            {
                int succeeded = Math.Max(0, total - failed);
                MessageBox.Show(
                    this,
                    $"Group \"{name}\" finished.{Environment.NewLine}Succeeded: {succeeded}{Environment.NewLine}Failed: {failed}",
                    "Run Group",
                    MessageBoxButtons.OK,
                    failed > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            }

            this.UpdatePickerStatus();

            if (this._AchievementScanPending == true)
            {
                this.StartAchievementScan();
            }
        }

        private void OnGroupsFeatureFormClosing(object sender, FormClosingEventArgs e)
        {
            if (this.IsGroupRunBusy == false)
            {
                return;
            }

            DialogResult result = MessageBox.Show(
                this,
                "A group run is still in progress. Stop it and close the app?",
                "Close",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (result != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }

            this._GroupRunWorker.CancelAsync();
        }

        private static bool TryIdleGame(
            uint gameId,
            int durationSeconds,
            Func<bool> isCancellationRequested,
            out bool cancelled,
            out string error)
        {
            cancelled = false;
            error = null;

            if (gameId == 0)
            {
                error = "invalid_appid";
                return false;
            }

            if (durationSeconds <= 0)
            {
                error = "invalid_duration";
                return false;
            }

            if (isCancellationRequested?.Invoke() == true)
            {
                cancelled = true;
                error = "cancelled";
                return false;
            }

            using API.Client client = new();
            try
            {
                client.Initialize(gameId);
            }
            catch (API.ClientInitializeException ex)
            {
                error = string.IsNullOrWhiteSpace(ex.Message) == false
                    ? ex.Message
                    : ex.Failure.ToString();
                return false;
            }
            catch (DllNotFoundException ex)
            {
                error = ex.Message;
                return false;
            }

            DateTime endAtUtc = DateTime.UtcNow.AddSeconds(durationSeconds);
            while (DateTime.UtcNow < endAtUtc)
            {
                if (isCancellationRequested?.Invoke() == true)
                {
                    cancelled = true;
                    error = "cancelled";
                    return false;
                }

                try
                {
                    client.RunCallbacks(false);
                }
                catch (InvalidOperationException ex)
                {
                    error = ex.Message;
                    return false;
                }

                Thread.Sleep(250);
            }

            return true;
        }
    }
}

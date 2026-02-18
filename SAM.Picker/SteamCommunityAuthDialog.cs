using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SAM.Picker
{
    internal sealed class SteamCommunityAuthDialog : Form
    {
        private readonly TextBox _SessionIdTextBox;
        private readonly TextBox _SteamLoginSecureTextBox;
        private readonly TextBox _SteamParentalTextBox;
        private readonly TextBox _SteamMachineAuthTextBox;
        private readonly CheckBox _SaveCheckBox;

        public string SessionId => this._SessionIdTextBox.Text ?? string.Empty;
        public string SteamLoginSecure => this._SteamLoginSecureTextBox.Text ?? string.Empty;
        public string SteamParental => this._SteamParentalTextBox.Text ?? string.Empty;
        public string SteamMachineAuth => this._SteamMachineAuthTextBox.Text ?? string.Empty;
        public bool SaveToFile => this._SaveCheckBox.Checked;

        public SteamCommunityAuthDialog(IDictionary<string, string> cookies)
        {
            this.Text = "Steam Community Authentication";
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MinimizeBox = false;
            this.MaximizeBox = false;
            this.ShowInTaskbar = false;
            this.ClientSize = new Size(760, 300);

            Label sessionLabel = CreateLabel("sessionid", 12);
            this._SessionIdTextBox = CreateTextBox(34, false);

            Label loginLabel = CreateLabel("steamLoginSecure", 68);
            this._SteamLoginSecureTextBox = CreateTextBox(90, true);

            Label parentalLabel = CreateLabel("steamParental (optional)", 124);
            this._SteamParentalTextBox = CreateTextBox(146, true);

            Label machineLabel = CreateLabel("steamMachineAuth (optional)", 180);
            this._SteamMachineAuthTextBox = CreateTextBox(202, true);

            this._SaveCheckBox = new CheckBox()
            {
                Left = 12,
                Top = 236,
                Width = 400,
                Text = "Save to steam-community-cookies.txt",
                Checked = true,
            };

            Button okButton = new()
            {
                Text = "OK",
                Width = 88,
                Height = 28,
                Left = this.ClientSize.Width - 192,
                Top = this.ClientSize.Height - 42,
                DialogResult = DialogResult.OK,
            };
            okButton.Click += this.OnOk;

            Button cancelButton = new()
            {
                Text = "Cancel",
                Width = 88,
                Height = 28,
                Left = this.ClientSize.Width - 98,
                Top = this.ClientSize.Height - 42,
                DialogResult = DialogResult.Cancel,
            };

            this.AcceptButton = okButton;
            this.CancelButton = cancelButton;

            this.Controls.Add(sessionLabel);
            this.Controls.Add(this._SessionIdTextBox);
            this.Controls.Add(loginLabel);
            this.Controls.Add(this._SteamLoginSecureTextBox);
            this.Controls.Add(parentalLabel);
            this.Controls.Add(this._SteamParentalTextBox);
            this.Controls.Add(machineLabel);
            this.Controls.Add(this._SteamMachineAuthTextBox);
            this.Controls.Add(this._SaveCheckBox);
            this.Controls.Add(okButton);
            this.Controls.Add(cancelButton);

            PopulateFromCookies(cookies);
        }

        private static Label CreateLabel(string text, int top)
        {
            return new Label()
            {
                Left = 12,
                Top = top,
                Width = 730,
                Text = text,
            };
        }

        private static TextBox CreateTextBox(int top, bool secret)
        {
            return new TextBox()
            {
                Left = 12,
                Top = top,
                Width = 736,
                UseSystemPasswordChar = secret,
            };
        }

        private void PopulateFromCookies(IDictionary<string, string> cookies)
        {
            if (cookies == null || cookies.Count == 0)
            {
                return;
            }

            if (cookies.TryGetValue("sessionid", out var sessionId) == true)
            {
                this._SessionIdTextBox.Text = sessionId;
            }

            if (cookies.TryGetValue("steamLoginSecure", out var steamLoginSecure) == true)
            {
                this._SteamLoginSecureTextBox.Text = steamLoginSecure;
            }

            if (cookies.TryGetValue("steamParental", out var steamParental) == true)
            {
                this._SteamParentalTextBox.Text = steamParental;
            }

            string machineAuth = null;
            if (cookies.TryGetValue("steamMachineAuth", out var machineAuthValue) == true)
            {
                machineAuth = machineAuthValue;
            }
            else
            {
                var machineAuthCookie = cookies.FirstOrDefault(
                    pair => pair.Key.StartsWith("steamMachineAuth", StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrWhiteSpace(machineAuthCookie.Key) == false)
                {
                    machineAuth = machineAuthCookie.Value;
                }
            }

            if (string.IsNullOrWhiteSpace(machineAuth) == false)
            {
                this._SteamMachineAuthTextBox.Text = machineAuth;
            }
        }

        private void OnOk(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(this._SessionIdTextBox.Text) == true ||
                string.IsNullOrWhiteSpace(this._SteamLoginSecureTextBox.Text) == true)
            {
                MessageBox.Show(
                    this,
                    "sessionid and steamLoginSecure are required.",
                    "Missing Required Fields",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                this.DialogResult = DialogResult.None;
            }
        }
    }
}

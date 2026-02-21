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
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SAM.Game
{
	internal class DoubleBufferedListView : ListView
	{
        private bool _UseDarkScrollBars;
        private bool _UseDarkColumnHeaders;
        private Color _ColumnHeaderBackColor = SystemColors.Control;
        private Color _ColumnHeaderForeColor = SystemColors.ControlText;
        private Color _ColumnHeaderBorderColor = SystemColors.ControlDark;

		public DoubleBufferedListView()
		{
			base.DoubleBuffered = true;
            base.OwnerDraw = true;
            this.DrawColumnHeader += this.OnDrawColumnHeader;
            this.DrawItem += this.HandleDrawItem;
            this.DrawSubItem += this.HandleDrawSubItem;
		}

        public bool UseDarkScrollBars
        {
            get => this._UseDarkScrollBars;
            set
            {
                if (this._UseDarkScrollBars == value)
                {
                    return;
                }

                this._UseDarkScrollBars = value;
                this.ApplyScrollBarTheme();
            }
        }

        public bool UseDarkColumnHeaders
        {
            get => this._UseDarkColumnHeaders;
            set
            {
                if (this._UseDarkColumnHeaders == value)
                {
                    return;
                }

                this._UseDarkColumnHeaders = value;
                this.Invalidate();
            }
        }

        public void SetColumnHeaderColors(Color backColor, Color foreColor, Color borderColor)
        {
            this._ColumnHeaderBackColor = backColor;
            this._ColumnHeaderForeColor = foreColor;
            this._ColumnHeaderBorderColor = borderColor;
            this.Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            this.ApplyScrollBarTheme();
        }

        private void ApplyScrollBarTheme()
        {
            if (this.IsHandleCreated == false)
            {
                return;
            }

            try
            {
                Win32.SetWindowTheme(this.Handle, this._UseDarkScrollBars == true ? "DarkMode_Explorer" : "Explorer", null);
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }

            this.Invalidate();
        }

        private void OnDrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            if (this.View != View.Details || this._UseDarkColumnHeaders == false)
            {
                e.DrawDefault = true;
                return;
            }

            Rectangle bounds = e.Bounds;
            using SolidBrush backBrush = new(this._ColumnHeaderBackColor);
            using Pen borderPen = new(this._ColumnHeaderBorderColor);
            e.Graphics.FillRectangle(backBrush, bounds);

            int rightX = bounds.Right - 1;
            int bottomY = bounds.Bottom - 1;
            e.Graphics.DrawLine(borderPen, bounds.Left, bottomY, bounds.Right, bottomY);
            e.Graphics.DrawLine(borderPen, rightX, bounds.Top, rightX, bounds.Bottom);

            Rectangle textBounds = Rectangle.FromLTRB(bounds.Left + 8, bounds.Top, bounds.Right - 6, bounds.Bottom);
            TextRenderer.DrawText(
                e.Graphics,
                e.Header?.Text ?? string.Empty,
                this.Font,
                textBounds,
                this._ColumnHeaderForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private void HandleDrawItem(object sender, DrawListViewItemEventArgs e)
        {
            e.DrawDefault = true;
        }

        private void HandleDrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            e.DrawDefault = true;
        }

        private static class Win32
        {
            [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
            public static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);
        }
	}
}

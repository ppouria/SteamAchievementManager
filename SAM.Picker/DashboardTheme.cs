using System.Drawing;

namespace SAM.Picker
{
    internal sealed class DashboardThemeTokens
    {
        private DashboardThemeTokens()
        {
        }

        public bool IsDark { get; private set; }
        public Color Accent { get; private set; }
        public Color AppBackground { get; private set; }
        public Color SurfaceBackground { get; private set; }
        public Color PanelBackground { get; private set; }
        public Color PanelElevatedBackground { get; private set; }
        public Color BorderSubtle { get; private set; }
        public Color TextPrimary { get; private set; }
        public Color TextSecondary { get; private set; }
        public Color TextMuted { get; private set; }
        public Color HoverFill { get; private set; }
        public Color ActiveFill { get; private set; }
        public Color ActiveBorder { get; private set; }
        public Color InputBackground { get; private set; }
        public Color InputBorder { get; private set; }
        public Color ShadowColor { get; private set; }
        public Color ProgressTrack { get; private set; }
        public Color ProgressFill { get; private set; }

        public int Space6 => 6;
        public int Space10 => 10;
        public int Space14 => 14;
        public int Space18 => 18;
        public int Space24 => 24;
        public int Radius10 => 10;
        public int Radius14 => 14;
        public int Radius18 => 18;

        public Font TitleFont => new("Segoe UI Semibold", 19f, FontStyle.Bold, GraphicsUnit.Point);
        public Font BodyFont => new("Segoe UI", 13f, FontStyle.Regular, GraphicsUnit.Point);
        public Font MutedFont => new("Segoe UI", 12f, FontStyle.Regular, GraphicsUnit.Point);

        public static DashboardThemeTokens CreateDark(Color accent)
        {
            return new DashboardThemeTokens()
            {
                IsDark = true,
                Accent = accent,
                AppBackground = Color.FromArgb(11, 15, 22),
                SurfaceBackground = Color.FromArgb(16, 21, 31),
                PanelBackground = Color.FromArgb(24, 30, 42),
                PanelElevatedBackground = Color.FromArgb(29, 37, 53),
                BorderSubtle = Color.FromArgb(58, 69, 88),
                TextPrimary = Color.FromArgb(236, 242, 251),
                TextSecondary = Color.FromArgb(190, 202, 223),
                TextMuted = Color.FromArgb(148, 164, 188),
                HoverFill = Color.FromArgb(36, 47, 67),
                ActiveFill = Color.FromArgb(39, 57, 84),
                ActiveBorder = Color.FromArgb(104, 158, 242),
                InputBackground = Color.FromArgb(20, 27, 39),
                InputBorder = Color.FromArgb(60, 73, 95),
                ShadowColor = Color.FromArgb(52, 0, 0, 0),
                ProgressTrack = Color.FromArgb(58, 74, 99),
                ProgressFill = Color.FromArgb(108, 170, 250),
            };
        }

        public static DashboardThemeTokens CreateLight(Color accent)
        {
            return new DashboardThemeTokens()
            {
                IsDark = false,
                Accent = accent,
                AppBackground = Color.FromArgb(242, 245, 250),
                SurfaceBackground = Color.FromArgb(248, 250, 253),
                PanelBackground = Color.FromArgb(255, 255, 255),
                PanelElevatedBackground = Color.FromArgb(255, 255, 255),
                BorderSubtle = Color.FromArgb(214, 223, 236),
                TextPrimary = Color.FromArgb(28, 39, 56),
                TextSecondary = Color.FromArgb(67, 86, 114),
                TextMuted = Color.FromArgb(108, 127, 154),
                HoverFill = Color.FromArgb(240, 245, 252),
                ActiveFill = Color.FromArgb(230, 239, 252),
                ActiveBorder = Color.FromArgb(83, 140, 228),
                InputBackground = Color.FromArgb(255, 255, 255),
                InputBorder = Color.FromArgb(206, 217, 233),
                ShadowColor = Color.FromArgb(24, 28, 42, 65),
                ProgressTrack = Color.FromArgb(216, 226, 239),
                ProgressFill = Color.FromArgb(79, 137, 229),
            };
        }
    }
}

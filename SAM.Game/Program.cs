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
using System.Globalization;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace SAM.Game
{
    internal static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            if (args.Length >= 2 &&
                string.Equals(args[0], "--achievement-progress", StringComparison.OrdinalIgnoreCase) == true)
            {
                RunAchievementProgressMode(args[1]);
                return;
            }

            long appId;

            if (args.Length == 0)
            {
                Process.Start("SAM.Picker.exe");
                return;
            }

            if (long.TryParse(args[0], out appId) == false)
            {
                MessageBox.Show(
                    "Could not parse application ID from command line argument.",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            if (API.Steam.GetInstallPath() == Application.StartupPath)
            {
                MessageBox.Show(
                    "This tool declines to being run from the Steam directory.",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            using (API.Client client = new())
            {
                try
                {
                    client.Initialize(appId);
                }
                catch (API.ClientInitializeException e)
                {
                    if (e.Failure == API.ClientInitializeFailure.ConnectToGlobalUser)
                    {
                        MessageBox.Show(
                            "Steam is not running. Please start Steam then run this tool again.\n\n" +
                            "If you have the game through Family Share, the game may be locked due to\n" +
                            "the Family Share account actively playing a game.\n\n" +
                            "(" + e.Message + ")",
                            "Error",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                    else if (string.IsNullOrEmpty(e.Message) == false)
                    {
                        MessageBox.Show(
                            "Steam is not running. Please start Steam then run this tool again.\n\n" +
                            "(" + e.Message + ")",
                            "Error",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                    else
                    {
                        MessageBox.Show(
                            "Steam is not running. Please start Steam then run this tool again.",
                            "Error",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                    return;
                }
                catch (DllNotFoundException)
                {
                    MessageBox.Show(
                        "You've caused an exceptional error!",
                        "Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new Manager(appId, client));
            }
        }

        private static void RunAchievementProgressMode(string appIdArgument)
        {
            if (long.TryParse(appIdArgument, NumberStyles.Integer, CultureInfo.InvariantCulture, out var appId) == false)
            {
                Console.WriteLine("ERR invalid_appid");
                return;
            }

            if (TryGetAchievementProgress(appId, out var unlocked, out var total) == false)
            {
                Console.WriteLine("ERR scan_failed");
                return;
            }

            Console.WriteLine($"{unlocked} {total}");
        }

        private static bool TryGetAchievementProgress(long appId, out int unlocked, out int total)
        {
            unlocked = -1;
            total = -1;

            using API.Client client = new();
            try
            {
                client.Initialize(appId);
            }
            catch (API.ClientInitializeException)
            {
                return false;
            }
            catch (DllNotFoundException)
            {
                return false;
            }

            var steamId = client.SteamUser.GetSteamId();
            bool callbackReceived = false;
            int callbackResult = -1;

            var userStatsReceived = client.CreateAndRegisterCallback<API.Callbacks.UserStatsReceived>();
            userStatsReceived.OnRun += param =>
            {
                if (param.SteamIdUser != steamId ||
                    param.GameId != (ulong)appId)
                {
                    return;
                }

                callbackReceived = true;
                callbackResult = param.Result;
            };

            var callHandle = client.SteamUserStats.RequestUserStats(steamId);
            if (callHandle == API.CallHandle.Invalid)
            {
                return false;
            }

            var timeoutAt = DateTime.UtcNow.AddSeconds(8);
            while (callbackReceived == false && DateTime.UtcNow < timeoutAt)
            {
                client.RunCallbacks(false);
                Thread.Sleep(15);
            }

            if (callbackReceived == false || callbackResult != 1)
            {
                return false;
            }

            var achievementCount = client.SteamUserStats.GetNumAchievements();
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
                var achievementId = client.SteamUserStats.GetAchievementName(i);
                if (string.IsNullOrEmpty(achievementId) == true)
                {
                    continue;
                }

                found++;
                if (client.SteamUserStats.GetAchievement(achievementId, out var isAchieved) == true &&
                    isAchieved == true)
                {
                    achieved++;
                }
            }

            unlocked = achieved;
            total = found;
            return true;
        }
    }
}

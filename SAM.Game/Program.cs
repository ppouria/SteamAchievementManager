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
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using APITypes = SAM.API.Types;

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

            if (args.Length >= 2 &&
                string.Equals(args[0], "--unlock-all", StringComparison.OrdinalIgnoreCase) == true)
            {
                RunUnlockAllMode(args[1]);
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

        private sealed class UnlockAllResult
        {
            public int Changed { get; set; }
            public int SkippedProtected { get; set; }
            public int Unlocked { get; set; }
            public int Total { get; set; }
        }

        private static void RunUnlockAllMode(string appIdArgument)
        {
            if (long.TryParse(appIdArgument, NumberStyles.Integer, CultureInfo.InvariantCulture, out var appId) == false)
            {
                Console.WriteLine("ERR invalid_appid");
                return;
            }

            if (TryUnlockAllAchievements(appId, out UnlockAllResult result, out string errorCode) == false)
            {
                Console.WriteLine(string.IsNullOrWhiteSpace(errorCode) == false
                    ? $"ERR {errorCode}"
                    : "ERR unlock_failed");
                return;
            }

            Console.WriteLine(
                $"OK {result.Changed} {result.SkippedProtected} {result.Unlocked} {result.Total}");
        }

        private static bool TryUnlockAllAchievements(
            long appId,
            out UnlockAllResult result,
            out string errorCode)
        {
            result = new UnlockAllResult()
            {
                Changed = 0,
                SkippedProtected = 0,
                Unlocked = -1,
                Total = -1,
            };
            errorCode = null;

            using API.Client client = new();
            try
            {
                client.Initialize(appId);
            }
            catch (API.ClientInitializeException)
            {
                errorCode = "initialize_failed";
                return false;
            }
            catch (DllNotFoundException)
            {
                errorCode = "missing_dll";
                return false;
            }

            if (TryRequestUserStats(client, appId, out int callbackResult) == false)
            {
                if (TryTreatAsNoAchievementGame(client, out int unlocked, out int total) == true)
                {
                    result.Unlocked = unlocked;
                    result.Total = total;
                    return true;
                }

                errorCode = "request_user_stats_failed";
                return false;
            }

            if (callbackResult != 1)
            {
                if (TryTreatAsNoAchievementGame(client, out int unlocked, out int total) == true)
                {
                    result.Unlocked = unlocked;
                    result.Total = total;
                    return true;
                }

                errorCode = "request_user_stats_result_failed";
                return false;
            }

            uint achievementCount = client.SteamUserStats.GetNumAchievements();
            if (achievementCount == 0)
            {
                result.Unlocked = 0;
                result.Total = 0;
                return true;
            }

            if (TryGetProtectedAchievementIds(appId, out HashSet<string> protectedIds) == false)
            {
                errorCode = "schema_unavailable";
                return false;
            }

            bool hasChanges = false;
            for (uint i = 0; i < achievementCount; i++)
            {
                string achievementId = client.SteamUserStats.GetAchievementName(i);
                if (string.IsNullOrWhiteSpace(achievementId) == true)
                {
                    continue;
                }

                if (protectedIds.Contains(achievementId) == true)
                {
                    result.SkippedProtected++;
                    continue;
                }

                if (client.SteamUserStats.GetAchievement(achievementId, out bool isAchieved) == false)
                {
                    continue;
                }

                if (isAchieved == true)
                {
                    continue;
                }

                if (client.SteamUserStats.SetAchievement(achievementId, true) == false)
                {
                    continue;
                }

                result.Changed++;
                hasChanges = true;
            }

            if (hasChanges == true &&
                client.SteamUserStats.StoreStats() == false)
            {
                errorCode = "store_failed";
                return false;
            }

            if (TryComputeAchievementProgress(client, out int finalUnlocked, out int finalTotal) == false)
            {
                errorCode = "compute_progress_failed";
                return false;
            }

            result.Unlocked = finalUnlocked;
            result.Total = finalTotal;
            return true;
        }

        private static bool TryRequestUserStats(API.Client client, long appId, out int callbackResult)
        {
            callbackResult = -1;

            ulong steamId = client.SteamUser.GetSteamId();
            bool callbackReceived = false;
            int callbackResultLocal = -1;

            var userStatsReceived = client.CreateAndRegisterCallback<API.Callbacks.UserStatsReceived>();
            userStatsReceived.OnRun += param =>
            {
                if (param.SteamIdUser != steamId ||
                    param.GameId != (ulong)appId)
                {
                    return;
                }

                callbackReceived = true;
                callbackResultLocal = param.Result;
            };

            var callHandle = client.SteamUserStats.RequestUserStats(steamId);
            if (callHandle == API.CallHandle.Invalid)
            {
                return false;
            }

            DateTime timeoutAt = DateTime.UtcNow.AddSeconds(8);
            while (callbackReceived == false && DateTime.UtcNow < timeoutAt)
            {
                client.RunCallbacks(false);
                Thread.Sleep(15);
            }

            callbackResult = callbackResultLocal;
            return callbackReceived;
        }

        private static bool TryComputeAchievementProgress(API.Client client, out int unlocked, out int total)
        {
            unlocked = -1;
            total = -1;

            uint achievementCount = client.SteamUserStats.GetNumAchievements();
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
                string achievementId = client.SteamUserStats.GetAchievementName(i);
                if (string.IsNullOrWhiteSpace(achievementId) == true)
                {
                    continue;
                }

                found++;
                if (client.SteamUserStats.GetAchievement(achievementId, out bool isAchieved) == true &&
                    isAchieved == true)
                {
                    achieved++;
                }
            }

            unlocked = achieved;
            total = found;
            return true;
        }

        private static bool TryGetProtectedAchievementIds(long appId, out HashSet<string> protectedIds)
        {
            protectedIds = new HashSet<string>(StringComparer.Ordinal);

            string schemaPath;
            try
            {
                string fileName = $"UserGameStatsSchema_{appId.ToString(CultureInfo.InvariantCulture)}.bin";
                schemaPath = Path.Combine(API.Steam.GetInstallPath(), "appcache", "stats", fileName);
            }
            catch (InvalidOperationException)
            {
                return false;
            }

            if (File.Exists(schemaPath) == false)
            {
                return false;
            }

            KeyValue kv = KeyValue.LoadAsBinary(schemaPath);
            if (kv == null)
            {
                return false;
            }

            KeyValue stats = kv[appId.ToString(CultureInfo.InvariantCulture)]["stats"];
            if (stats.Valid == false || stats.Children == null)
            {
                return false;
            }

            foreach (KeyValue stat in stats.Children)
            {
                if (stat.Valid == false)
                {
                    continue;
                }

                int rawType = stat["type_int"].Valid
                    ? stat["type_int"].AsInteger(0)
                    : stat["type"].AsInteger(0);
                APITypes.UserStatType type = (APITypes.UserStatType)rawType;
                if (type != APITypes.UserStatType.Achievements &&
                    type != APITypes.UserStatType.GroupAchievements)
                {
                    continue;
                }

                if (stat.Children == null)
                {
                    continue;
                }

                foreach (KeyValue bits in stat.Children.Where(
                    child => string.Equals(child.Name, "bits", StringComparison.InvariantCultureIgnoreCase)))
                {
                    if (bits.Valid == false || bits.Children == null)
                    {
                        continue;
                    }

                    foreach (KeyValue bit in bits.Children)
                    {
                        string id = bit["name"].AsString("");
                        if (string.IsNullOrWhiteSpace(id) == true)
                        {
                            continue;
                        }

                        int permission = bit["permission"].AsInteger(0);
                        if ((permission & 3) != 0)
                        {
                            protectedIds.Add(id);
                        }
                    }
                }
            }

            return true;
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
                if (TryTreatAsNoAchievementGame(client, out unlocked, out total) == true)
                {
                    return true;
                }
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
                if (TryTreatAsNoAchievementGame(client, out unlocked, out total) == true)
                {
                    return true;
                }
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

        private static bool TryTreatAsNoAchievementGame(API.Client client, out int unlocked, out int total)
        {
            unlocked = -1;
            total = -1;

            try
            {
                var achievementCount = client.SteamUserStats.GetNumAchievements();
                if (achievementCount != 0)
                {
                    return false;
                }

                unlocked = 0;
                total = 0;
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }
}

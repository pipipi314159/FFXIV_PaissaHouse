using System;
using System.Linq;
using AutoSweep.Structures;
using Dalamud.Hooking;
using Dalamud.Utility.Signatures;

namespace AutoSweep.Paissa {
    public unsafe class WardObserver {
        private Plugin plugin;
        internal readonly SweepState SweepState;

        private delegate void HandleHousingWardInfoDelegate(
            void* agentBase,
            IntPtr housingWardInfoPtr
        );

        [Signature("40 55 53 41 54 41 55 41 57 48 8D AC 24 ?? ?? ?? ?? B8", DetourName = nameof(OnHousingWardInfo))]
        private Hook<HandleHousingWardInfoDelegate>? housingWardInfoHook;

        public WardObserver(Plugin plugin) {
            this.plugin = plugin;
            Plugin.InteropProvider.InitializeFromAttributes(this);
            SweepState = new SweepState(() => plugin.Configuration.WardsPerDistrict);
            if (housingWardInfoHook == null)
                Plugin.PluginLog.Warning("HousingWardInfo signature not found; ward sweeps will not be recorded on this client version.");
            housingWardInfoHook?.Enable();
        }

        public void Dispose() {
            housingWardInfoHook?.Dispose();
        }

        public void OnHousingWardInfo(
            void* agentBase,
            IntPtr dataPtr
        ) {
            housingWardInfoHook!.Original(agentBase, dataPtr);

            if (!plugin.Configuration.Enabled) return;
            // TW port: the packet layout may start at a different offset on the TW client, so locate it
            IntPtr wardInfoPtr = LocateWardInfo(dataPtr);
            if (wardInfoPtr == IntPtr.Zero) return;
            HousingWardInfo wardInfo = HousingWardInfo.Read(wardInfoPtr);
            Plugin.PluginLog.Debug($"Got HousingWardInfo for ward: {wardInfo.LandIdent.WardNumber} territory: {wardInfo.LandIdent.TerritoryTypeId}");

            // TW port: always save the latest ward state locally (even if seen earlier in this sweep)
            try {
                plugin.LocalStore.RecordWard(wardInfo);
            }
            catch (Exception e) {
                Plugin.PluginLog.Warning(e, "Could not record ward info locally");
            }

            // if the current wardinfo is for a different district than the last swept one, print the header
            // or if the last sweep was > 10m ago
            if (SweepState.ShouldStartNewSweep(wardInfo)) {
                // reset last sweep info to the current sweep
                SweepState.StartDistrictSweep(wardInfo);

                var districtName = plugin.GetDistrictName((uint)wardInfo.LandIdent.TerritoryTypeId);
                var worldName = plugin.GetWorldName((uint)wardInfo.LandIdent.WorldId);
                if (plugin.Configuration.ChatSweepAlert) {
                    Plugin.Chat.Print($"開始巡房：{districtName}（{worldName}）");
                }
            }

            // if we've seen this ward already, ignore it
            if (SweepState.Contains(wardInfo)) {
                Plugin.PluginLog.Debug($"Skipped processing HousingWardInfo for ward: {wardInfo.LandIdent.WardNumber} because we have seen it already");
                return;
            }

            // add the ward to this sweep
            SweepState.Add(wardInfo);

            // if that's all the wards, display the district summary and thanks
            if (SweepState.IsComplete) OnFinishedDistrictSweep(wardInfo);

            Plugin.PluginLog.Debug($"Done processing HousingWardInfo for ward: {wardInfo.LandIdent.WardNumber}");
        }

        // ==== TW port: packet offset detection ====
        private const int ScanBytes = 0x80;
        private int? detectedOffset;
        private int diagnosticsLeft = 3;

        private unsafe IntPtr LocateWardInfo(IntPtr dataPtr) {
            if (dataPtr == IntPtr.Zero) return IntPtr.Zero;
            byte* p = (byte*)dataPtr.ToPointer();

            if (detectedOffset is int known && LooksLikeLandIdent(p + known))
                return dataPtr + known;

            for (int off = 0; off <= ScanBytes; off += 2) {
                if (!LooksLikeLandIdent(p + off)) continue;
                if (detectedOffset != off)
                    Plugin.PluginLog.Info($"HousingWardInfo: LandIdent found at offset 0x{off:X} " +
                                          $"(territory={*(short*)(p + off + 4)}, world={*(short*)(p + off + 6)}, ward={*(short*)(p + off + 2)})");
                detectedOffset = off;
                return dataPtr + off;
            }

            if (diagnosticsLeft > 0) {
                diagnosticsLeft--;
                Plugin.PluginLog.Warning($"HousingWardInfo: could not find LandIdent in first 0x{ScanBytes:X} bytes. Dump:\n{HexDump(p, ScanBytes + 8)}");
            }
            return IntPtr.Zero;
        }

        private unsafe bool LooksLikeLandIdent(byte* b) {
            short ward = *(short*)(b + 2);
            short territory = *(short*)(b + 4);
            short world = *(short*)(b + 6);
            return ward >= 0 && ward < 60
                   && Utils.IsHousingDistrict(territory)
                   && world > 0
                   && plugin.Worlds.TryGetRow((uint)world, out _);
        }

        private static unsafe string HexDump(byte* p, int len) {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < len; i += 16) {
                sb.Append($"{i:X4}: ");
                for (int j = 0; j < 16 && i + j < len; j++) sb.Append($"{p[i + j]:X2} ");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        /// <summary>
        ///     Called each time the user finishes sweeping a full district, with the wardinfo as the last ward swept.
        /// </summary>
        private void OnFinishedDistrictSweep(HousingWardInfo housingWardInfo) {
            var districtName = plugin.GetDistrictName((uint)SweepState.DistrictId);
            var worldName = plugin.GetWorldName((uint)SweepState.WorldId);

            // TW port: auto-export this district to CSV
            if (plugin.Configuration.AutoExportCsv) {
                try {
                    var district = plugin.LocalStore.Districts.Where(d =>
                        d.WorldId == SweepState.WorldId && d.DistrictId == SweepState.DistrictId);
                    string path = plugin.LocalStore.ExportCsv(district, $"{worldName}_{districtName}");
                    Plugin.PluginLog.Info($"Exported sweep CSV to {path}");
                    if (plugin.Configuration.ChatSweepAlert)
                        Plugin.Chat.Print($"已匯出 CSV（/phouse 可開啟資料夾）：{System.IO.Path.GetFileName(path)}");
                }
                catch (Exception e) {
                    Plugin.PluginLog.Warning(e, "Could not export sweep CSV");
                }
            }

            if (!plugin.Configuration.ChatSweepAlert) return;

            Plugin.Chat.Print($"已巡完全部 {plugin.Configuration.WardsPerDistrict} 區。");
            Plugin.Chat.Print($"{districtName}（{worldName}）：{SweepState.OpenHouses.Count} 塊空地。");
            foreach (OpenHouse openHouse in SweepState.OpenHouses)
                plugin.OnFoundOpenHouse((uint)SweepState.WorldId, (uint)SweepState.DistrictId, openHouse.WardNum, openHouse.PlotNum, openHouse.HouseInfoEntry.HousePrice);
        }
    }
}

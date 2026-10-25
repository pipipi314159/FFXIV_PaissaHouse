using System;
using AutoSweep.Structures;
using Dalamud.Hooking;
using Lumina.Excel.Sheets;

namespace AutoSweep.Paissa {
    public unsafe class LotteryObserver {
        private readonly Plugin plugin;
        private Hook<HandlePlacardSaleInfoDelegate>? placardSaleInfoHook;

        private delegate void HandlePlacardSaleInfoDelegate(
            void* agentBase,
            HousingType housingType,
            ushort territoryTypeId,
            byte wardId,
            byte plotId,
            short apartmentNumber,
            IntPtr placardSaleInfoPtr,
            long a8
        );

        // Original notes: easy way to find sig: search for scalar 7043
        // 7.3+ : find the movups pattern, function starts 0xA8 before it
        private const string MovupsSig73 = "41 0F 10 06 0F 11 43 48 41 0F 10 4E 10 0F 11 4B 58";
        // pre-7.3 : direct call to the function
        private const string CallSigPre73 = "E8 ?? ?? ?? ?? 48 8B 74 24 ?? 48 8B 6C 24 ?? E9";

        public LotteryObserver(Plugin plugin) {
            this.plugin = plugin;

            // TW port: the TW client may be on a different patch than global, so try both signatures
            IntPtr functionStart = IntPtr.Zero;
            try {
                if (Plugin.SigScanner.TryScanText(MovupsSig73, out IntPtr movupsAddress)) {
                    functionStart = movupsAddress - 0xA8;
                    Plugin.PluginLog.Info("PlacardSaleInfo: using 7.3+ signature");
                }
                else if (Plugin.SigScanner.TryScanText(CallSigPre73, out IntPtr callTarget)) {
                    functionStart = callTarget;
                    Plugin.PluginLog.Info("PlacardSaleInfo: using pre-7.3 signature");
                }
            }
            catch (Exception e) {
                Plugin.PluginLog.Warning(e, "PlacardSaleInfo signature scan failed");
            }

            if (functionStart == IntPtr.Zero) {
                Plugin.PluginLog.Warning("PlacardSaleInfo signature not found; lottery info will not be recorded.");
                return;
            }

            placardSaleInfoHook = Plugin.InteropProvider.HookFromAddress<HandlePlacardSaleInfoDelegate>(
                functionStart,
                OnPlacardSaleInfo
            );
            placardSaleInfoHook.Enable();
        }

        public void Dispose() {
            placardSaleInfoHook?.Dispose();
        }

        public void OnPlacardSaleInfo(
            void* agentBase,
            HousingType housingType,
            ushort territoryTypeId,
            byte wardId,
            byte plotId,
            short apartmentNumber,
            IntPtr placardSaleInfoPtr,
            long a8
        ) {
            placardSaleInfoHook!.Original(agentBase, housingType, territoryTypeId, wardId, plotId, apartmentNumber, placardSaleInfoPtr, a8);

            // if the plot is owned, ignore it
            if (housingType != HousingType.UnownedHouse) return;
            if (placardSaleInfoPtr == IntPtr.Zero) return;

            PlacardSaleInfo saleInfo = PlacardSaleInfo.Read(placardSaleInfoPtr);

            Plugin.PluginLog.Debug(
                $"Got PlacardSaleInfo: PurchaseType={saleInfo.PurchaseType}, TenantType={saleInfo.TenantType}, available={saleInfo.AvailabilityType}, until={saleInfo.PhaseEndsAt}, numEntries={saleInfo.EntryCount}");
            Plugin.PluginLog.Debug(
                $"unknown1={saleInfo.Unknown1}, unknown2={saleInfo.Unknown2}, unknown3={saleInfo.Unknown3}, unknown4={BitConverter.ToString(saleInfo.Unknown4)}");
            Plugin.PluginLog.Debug(
                $"housingType={housingType}, territoryTypeId={territoryTypeId}, wardId={wardId}, plotId={plotId}, apartmentNumber={apartmentNumber}, placardSaleInfoPtr={placardSaleInfoPtr}, a8={a8}");

            // get information about the world from the clientstate
            World? world = Plugin.ClientState.LocalPlayer?.CurrentWorld.ValueNullable;
            if (world is null) return;

            var place = plugin.GetDistrictName(territoryTypeId);
            var worldName = world.Value.Name;
            Plugin.PluginLog.Info($"Plot {place} {wardId + 1}-{plotId + 1} ({worldName}) has {saleInfo.EntryCount} lottery entries.");

            // TW port: save locally instead of uploading
            try {
                plugin.LocalStore.RecordLottery(world.Value.RowId, territoryTypeId, wardId, plotId, saleInfo);
            }
            catch (Exception e) {
                Plugin.PluginLog.Warning(e, "Could not record lottery info locally");
            }
        }
    }

    public enum HousingType : byte {
        OwnedHouse = 0,
        UnownedHouse = 1,
        FreeCompanyApartment = 2,
        Apartment = 3
    }
}

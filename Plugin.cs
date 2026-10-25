using System;
using AutoSweep.Paissa;
using AutoSweep.Structures;
using Dalamud.Game;
using Dalamud.Game.Command;
using Dalamud.Game.Text;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace AutoSweep {
    public sealed class Plugin : IDalamudPlugin {
        public string Name => "PaissaHouse";

        // frameworks/data
        [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
        [PluginService] internal static IChatGui Chat { get; private set; } = null!;
        [PluginService] internal static IClientState ClientState { get; private set; } = null!;
        [PluginService] internal static ICommandManager Commands { get; private set; } = null!;
        [PluginService] internal static IFramework Framework { get; private set; } = null!;
        [PluginService] internal static IPluginLog PluginLog { get; private set; } = null!;
        [PluginService] internal static IGameInteropProvider InteropProvider { get; private set; } = null!;
        [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
        [PluginService] internal static ISigScanner SigScanner { get; private set; } = null!;
        internal readonly Configuration Configuration;
        internal readonly LocalStore LocalStore;

        internal readonly ExcelSheet<HousingLandSet> HousingLandSets;
        internal readonly ExcelSheet<TerritoryType> Territories;
        internal readonly ExcelSheet<World> Worlds;

        // state
        private readonly WardObserver wardObserver;
        private readonly LotteryObserver lotteryObserver;
        private readonly PluginUI ui;
        private readonly HouseListWindow houseListWindow;

        public Plugin() {
            // setup
            Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            ui = new PluginUI(Configuration);
            Territories = DataManager.GetExcelSheet<TerritoryType>();
            Worlds = DataManager.GetExcelSheet<World>();
            HousingLandSets = DataManager.GetExcelSheet<HousingLandSet>();
            LocalStore = new LocalStore(this);
            houseListWindow = new HouseListWindow(this);

            Commands.AddHandler(Utils.HouseCommandName, new CommandInfo(OnHouseCommand) {
                HelpMessage = "開啟本機空地清單（巡房與抽籤看板紀錄）。"
            });
            Commands.AddHandler(Utils.CommandName, new CommandInfo(OnCommand) {
                HelpMessage =
                    $"開啟 PaissaHouse 設定。\n\"{Utils.CommandName} reset\" 可重設巡房狀態（連續重複巡同一個住宅區時使用）。"
            });

            // event hooks
            PluginInterface.UiBuilder.Draw += DrawUI;
            PluginInterface.UiBuilder.OpenConfigUi += DrawConfigUI;
            PluginInterface.UiBuilder.OpenMainUi += OpenMainUI;

            // observers (TW port: data is stored locally, nothing is uploaded)
            wardObserver = new WardObserver(this);
            lotteryObserver = new LotteryObserver(this);

            PluginLog.Debug($"Initialization complete: configVersion={Configuration.Version}");
        }

        public void Dispose() {
            ui.Dispose();
            PluginInterface.UiBuilder.Draw -= DrawUI;
            PluginInterface.UiBuilder.OpenConfigUi -= DrawConfigUI;
            PluginInterface.UiBuilder.OpenMainUi -= OpenMainUI;
            Commands.RemoveHandler(Utils.CommandName);
            Commands.RemoveHandler(Utils.HouseCommandName);
            lotteryObserver.Dispose();
            wardObserver.Dispose();
        }

        // ==== dalamud events ====
        private void OnHouseCommand(string command, string args) {
            houseListWindow.Visible = !houseListWindow.Visible;
        }

        private void OnCommand(string command, string args) {
            switch (args) {
                case "reset":
                    wardObserver.SweepState.Reset();
                    Chat.Print("巡房狀態已重設。");
                    break;
                default:
                    ui.SettingsVisible = true;
                    break;
            }
        }

        // ==== lumina helpers ====
        internal string GetWorldName(uint worldId) {
            return Worlds.TryGetRow(worldId, out World world) ? world.Name.ToString() : worldId.ToString();
        }

        internal string GetDistrictName(uint territoryTypeId) {
            if (!Territories.TryGetRow(territoryTypeId, out TerritoryType territory)) return territoryTypeId.ToString();
            PlaceName place = territory.PlaceName.Value;
            return (!place.NameNoArticle.IsEmpty ? place.NameNoArticle : place.Name).ToString();
        }

        /// <summary>Returns (size, initial price) of a plot (0-based plot index).</summary>
        internal (byte size, uint initialPrice) GetPlotInfo(uint territoryTypeId, int plotIndex) {
            try {
                HousingLandSet landSet = HousingLandSets.GetRow(Utils.TerritoryTypeIdToLandSetId(territoryTypeId));
                var info = landSet.LandSet[plotIndex];
                return (info.PlotSize, info.InitialPrice);
            }
            catch (Exception e) {
                PluginLog.Warning(e, $"Could not read plot info for territory {territoryTypeId} plot {plotIndex}");
                return (255, 0);
            }
        }

        /// <summary>
        ///     Display the details of an open plot in the user's preferred format.
        /// </summary>
        internal void OnFoundOpenHouse(uint worldId, uint territoryTypeId, int wardNumber, int plotNumber, uint? price,
            string messagePrefix = "") {
            var districtName = GetDistrictName(territoryTypeId);
            var worldName = GetWorldName(worldId);

            var (houseSize, initialPrice) = GetPlotInfo(territoryTypeId, plotNumber);
            uint realPrice = price.GetValueOrDefault(initialPrice); // if price is null, it's probably the default price (landupdate)

            string districtNameNoSpaces = districtName.Replace(" ", "");
            int wardNum = wardNumber + 1;
            int plotNum = plotNumber + 1;
            float housePriceMillions = realPrice / 1000000f;
            string houseSizeName = houseSize switch {
                0 => "Small",
                1 => "Medium",
                _ => "Large"
            };

            string output;
            switch (Configuration.OutputFormat) {
                case OutputFormat.Pings:
                    output =
                        $"{messagePrefix}@{houseSizeName}{districtNameNoSpaces} {wardNum}-{plotNum} ({housePriceMillions:F3}m)";
                    break;
                case OutputFormat.Custom:
                    var template = $"{messagePrefix}{Configuration.OutputFormatString}";
                    output = Utils.FormatCustomOutputString(template, districtName, districtNameNoSpaces,
                        worldName, wardNum.ToString(),
                        plotNum.ToString(), realPrice.ToString(), housePriceMillions.ToString("F3"), houseSizeName);
                    break;
                default:
                    output =
                        $"{messagePrefix}{districtName} {wardNum}-{plotNum} ({houseSizeName}, {housePriceMillions:F3}m)";
                    break;
            }

            SendChatToConfiguredChannel(output);
        }

        // ==== helpers ====
        private void SendChatToConfiguredChannel(string message) {
            Chat.Print(new XivChatEntry {
                Name = "[PaissaHouse]",
                Message = message,
                Type = Configuration.ChatType
            });
        }

        // ==== UI ====
        private void DrawUI() {
            ui.Draw();
            houseListWindow.Draw();
        }

        private void OpenMainUI() {
            houseListWindow.Visible = true;
        }

        private void DrawConfigUI() {
            ui.SettingsVisible = true;
        }
    }
}
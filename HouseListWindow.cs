using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using AutoSweep.Paissa;
using AutoSweep.Structures;
using Dalamud.Bindings.ImGui;

namespace AutoSweep {
    // TW port: in-game list of open plots recorded locally (/phouse)
    internal class HouseListWindow {
        private readonly Plugin plugin;
        private bool visible;

        private int selectedWorld = -1; // -1 = all
        private int selectedDistrict = -1; // -1 = all
        private bool showS = true, showM = true, showL = true;
        private bool onlyBuyableNow = false;
        private int sortMode = 0; // 0=address 1=price 2=entries
        private string status = "";

        private static readonly string[] SortNames = { "地址", "價格", "抽籤人數" };

        public HouseListWindow(Plugin plugin) {
            this.plugin = plugin;
        }

        public bool Visible {
            get => visible;
            set => visible = value;
        }

        public void Draw() {
            if (!visible) return;

            ImGui.SetNextWindowSize(new Vector2(900, 500), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("PaissaHouse 空地清單（本機）###PaissaHouseLocalList", ref visible)) {
                List<LocalDistrict> districts = plugin.LocalStore.Districts.ToList();
                DrawToolbar(districts);
                ImGui.Separator();

                List<LocalDistrict> shownDistricts = districts
                    .Where(d => selectedWorld < 0 || d.WorldId == selectedWorld)
                    .Where(d => selectedDistrict < 0 || d.DistrictId == selectedDistrict)
                    .OrderBy(d => d.WorldId).ThenBy(d => d.DistrictId)
                    .ToList();

                DrawDistrictSummary(shownDistricts);
                ImGui.Separator();
                DrawPlotTable(shownDistricts);
            }
            ImGui.End();
        }

        private void DrawToolbar(List<LocalDistrict> districts) {
            // world filter
            var worlds = districts.GroupBy(d => d.WorldId).Select(g => (g.Key, g.First().WorldName)).OrderBy(w => w.Key).ToList();
            string worldLabel = selectedWorld < 0 ? "全部伺服器" : worlds.FirstOrDefault(w => w.Key == selectedWorld).WorldName ?? selectedWorld.ToString();
            ImGui.SetNextItemWidth(140);
            if (ImGui.BeginCombo("##world", worldLabel)) {
                if (ImGui.Selectable("全部伺服器", selectedWorld < 0)) selectedWorld = -1;
                foreach (var (id, name) in worlds)
                    if (ImGui.Selectable($"{name}##w{id}", selectedWorld == id)) selectedWorld = id;
                ImGui.EndCombo();
            }

            // district filter
            ImGui.SameLine();
            var districtTypes = districts.GroupBy(d => d.DistrictId).Select(g => (g.Key, g.First().DistrictName)).OrderBy(d => d.Key).ToList();
            string districtLabel = selectedDistrict < 0 ? "全部住宅區" : districtTypes.FirstOrDefault(d => d.Key == selectedDistrict).DistrictName ?? selectedDistrict.ToString();
            ImGui.SetNextItemWidth(140);
            if (ImGui.BeginCombo("##district", districtLabel)) {
                if (ImGui.Selectable("全部住宅區", selectedDistrict < 0)) selectedDistrict = -1;
                foreach (var (id, name) in districtTypes)
                    if (ImGui.Selectable($"{name}##d{id}", selectedDistrict == id)) selectedDistrict = id;
                ImGui.EndCombo();
            }

            // size filter
            ImGui.SameLine();
            ImGui.Checkbox("S", ref showS);
            ImGui.SameLine();
            ImGui.Checkbox("M", ref showM);
            ImGui.SameLine();
            ImGui.Checkbox("L", ref showL);

            ImGui.SameLine();
            ImGui.Checkbox("只顯示目前可購買", ref onlyBuyableNow);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("先到先得，或抽籤中且看板顯示「可參加」的空地。\n抽籤狀態要看過門口看板才會有資料。");

            // sort
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            if (ImGui.BeginCombo("排序", SortNames[sortMode])) {
                for (int i = 0; i < SortNames.Length; i++)
                    if (ImGui.Selectable(SortNames[i], sortMode == i)) sortMode = i;
                ImGui.EndCombo();
            }

            // actions
            if (ImGui.Button("匯出 CSV（目前篩選）")) {
                try {
                    var toExport = districts
                        .Where(d => selectedWorld < 0 || d.WorldId == selectedWorld)
                        .Where(d => selectedDistrict < 0 || d.DistrictId == selectedDistrict);
                    string path = plugin.LocalStore.ExportCsv(toExport, "list");
                    status = $"已匯出：{Path.GetFileName(path)}";
                }
                catch (Exception e) {
                    Plugin.PluginLog.Warning(e, "CSV export failed");
                    status = "匯出失敗，請看 /xllog";
                }
            }
            ImGui.SameLine();
            if (ImGui.Button("開啟資料夾")) {
                try {
                    Directory.CreateDirectory(plugin.LocalStore.ExportDirectory);
                    Process.Start(new ProcessStartInfo {
                        FileName = plugin.LocalStore.ExportDirectory,
                        UseShellExecute = true
                    });
                }
                catch (Exception e) {
                    Plugin.PluginLog.Warning(e, "Could not open folder");
                }
            }
            ImGui.SameLine();
            if (ImGui.Button("清除全部資料") && ImGui.GetIO().KeyCtrl) {
                plugin.LocalStore.Clear();
                status = "已清除本機資料";
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("按住 Ctrl 再點才會清除（不會刪除已匯出的 CSV）。");

            if (!string.IsNullOrEmpty(status)) {
                ImGui.SameLine();
                ImGui.TextDisabled(status);
            }
        }

        private void DrawDistrictSummary(List<LocalDistrict> districts) {
            if (districts.Count == 0) {
                ImGui.TextWrapped("還沒有資料。到以太之光或渡船「前往住宅區」，翻過各區的清單就會記錄下來；在空地門口看板查看抽籤資訊會一併記錄。");
                return;
            }
            foreach (LocalDistrict d in districts) {
                ImGui.Text($"{d.WorldName}・{d.DistrictName}：已巡 {d.WardSeenAt.Count}/{plugin.Configuration.WardsPerDistrict} 區，{d.OpenPlots.Count} 塊空地");
                ImGui.SameLine();
                ImGui.TextDisabled($"最後更新 {d.LastUpdated:MM/dd HH:mm}");
            }
        }

        private void DrawPlotTable(List<LocalDistrict> districts) {
            var rows = districts.SelectMany(d => d.OpenPlots.Select(p => (d, p)))
                .Where(r => r.p.Size switch {
                    0 => showS,
                    1 => showM,
                    2 => showL,
                    _ => true
                })
                .Where(r => !onlyBuyableNow || IsBuyableNow(r.p));

            rows = sortMode switch {
                1 => rows.OrderByDescending(r => r.p.Price),
                2 => rows.OrderBy(r => r.p.LotteryEntries ?? uint.MaxValue),
                _ => rows.OrderBy(r => r.d.WorldId).ThenBy(r => r.d.DistrictId).ThenBy(r => r.p.WardNumber).ThenBy(r => r.p.PlotNumber)
            };
            var list = rows.ToList();

            ImGui.TextDisabled($"共 {list.Count} 筆");

            const ImGuiTableFlags flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY |
                                          ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingFixedFit;
            if (!ImGui.BeginTable("plots", 11, flags, new Vector2(0, 0))) return;

            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("伺服器");
            ImGui.TableSetupColumn("住宅區");
            ImGui.TableSetupColumn("地址");
            ImGui.TableSetupColumn("大小");
            ImGui.TableSetupColumn("價格");
            ImGui.TableSetupColumn("方式");
            ImGui.TableSetupColumn("限制");
            ImGui.TableSetupColumn("抽籤");
            ImGui.TableSetupColumn("人數");
            ImGui.TableSetupColumn("階段結束");
            ImGui.TableSetupColumn("資料時間");
            ImGui.TableHeadersRow();

            DateTime now = DateTime.Now;
            foreach (var (d, p) in list) {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text(d.WorldName);
                ImGui.TableNextColumn();
                ImGui.Text(d.DistrictName);
                ImGui.TableNextColumn();
                ImGui.Text($"{p.WardNumber}-{p.PlotNumber}");
                ImGui.TableNextColumn();
                ImGui.Text(TwText.Size(p.Size));
                ImGui.TableNextColumn();
                ImGui.Text(p.Price.ToString("N0"));
                ImGui.TableNextColumn();
                ImGui.Text(TwText.Purchase(p.PurchaseType));
                ImGui.TableNextColumn();
                ImGui.Text(TwText.Tenant(p.TenantType));
                ImGui.TableNextColumn();
                ImGui.Text(TwText.Lottery(p.LotteryPhase));
                ImGui.TableNextColumn();
                ImGui.Text(p.LotteryEntries?.ToString() ?? "");
                ImGui.TableNextColumn();
                ImGui.Text(p.LotteryPhaseEndsAt?.ToString("MM/dd HH:mm") ?? "");
                ImGui.TableNextColumn();
                DateTime seen = p.LotterySeenAt is { } l && l > p.SeenAt ? l : p.SeenAt;
                string seenText = seen.ToString("MM/dd HH:mm");
                if (now - seen > TimeSpan.FromHours(24)) ImGui.TextDisabled(seenText);
                else ImGui.Text(seenText);
            }

            ImGui.EndTable();
        }

        private static bool IsBuyableNow(LocalPlot p) {
            return p.PurchaseType switch {
                PurchaseType.FCFS => true,
                PurchaseType.Lottery => p.LotteryPhase == AvailabilityType.Available,
                _ => false
            };
        }
    }
}

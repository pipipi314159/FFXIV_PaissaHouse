using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AutoSweep.Structures;
using Newtonsoft.Json;

namespace AutoSweep.Paissa {
    // TW port: all housing data is kept on this computer only (no PaissaDB upload).
    // Owner names of other players are never stored.

    public class LocalPlot {
        public int WardNumber { get; set; } // 1-based
        public int PlotNumber { get; set; } // 1-based
        public byte Size { get; set; } // 0=S 1=M 2=L
        public uint Price { get; set; }
        public PurchaseType PurchaseType { get; set; }
        public TenantType TenantType { get; set; }
        public DateTime SeenAt { get; set; }

        // lottery info (from viewing the placard)
        public AvailabilityType? LotteryPhase { get; set; }
        public uint? LotteryEntries { get; set; }
        public DateTime? LotteryPhaseEndsAt { get; set; }
        public DateTime? LotterySeenAt { get; set; }
    }

    public class LocalDistrict {
        public int WorldId { get; set; }
        public string WorldName { get; set; } = "";
        public int DistrictId { get; set; }
        public string DistrictName { get; set; } = "";
        public DateTime LastUpdated { get; set; }
        public Dictionary<int, DateTime> WardSeenAt { get; set; } = new(); // 1-based ward -> time
        public List<LocalPlot> OpenPlots { get; set; } = new();
    }

    public class LocalData {
        public int Version { get; set; } = 1;
        public Dictionary<string, LocalDistrict> Districts { get; set; } = new();
    }

    public class LocalStore {
        private readonly Plugin plugin;
        private readonly object fileLock = new();
        private LocalData data;

        public string DataDirectory { get; }
        public string ExportDirectory => Path.Combine(DataDirectory, "exports");
        private string DataFile => Path.Combine(DataDirectory, "housing_local.json");

        public LocalStore(Plugin plugin) {
            this.plugin = plugin;
            DataDirectory = Plugin.PluginInterface.GetPluginConfigDirectory();
            Directory.CreateDirectory(DataDirectory);
            data = Load();
        }

        public IReadOnlyCollection<LocalDistrict> Districts => data.Districts.Values;

        private static string Key(int worldId, int districtId) => $"{worldId}-{districtId}";

        private LocalData Load() {
            try {
                if (File.Exists(DataFile)) {
                    var loaded = JsonConvert.DeserializeObject<LocalData>(File.ReadAllText(DataFile, Encoding.UTF8));
                    if (loaded != null) {
                        // drop entries recorded with an invalid district (e.g. early TW test builds)
                        foreach (string key in loaded.Districts.Where(kv => !Utils.IsHousingDistrict(kv.Value.DistrictId)).Select(kv => kv.Key).ToList())
                            loaded.Districts.Remove(key);
                        return loaded;
                    }
                }
            }
            catch (Exception e) {
                Plugin.PluginLog.Warning(e, "Could not load local housing data, starting empty");
            }
            return new LocalData();
        }

        public void Save() {
            string json;
            lock (fileLock) json = JsonConvert.SerializeObject(data, Formatting.Indented);
            try {
                lock (fileLock) {
                    string tmp = DataFile + ".tmp";
                    File.WriteAllText(tmp, json, new UTF8Encoding(false));
                    File.Move(tmp, DataFile, true);
                }
            }
            catch (Exception e) {
                Plugin.PluginLog.Warning(e, "Could not save local housing data");
            }
        }

        private LocalDistrict GetOrCreateDistrict(int worldId, int districtId) {
            string key = Key(worldId, districtId);
            if (!data.Districts.TryGetValue(key, out LocalDistrict? district)) {
                district = new LocalDistrict { WorldId = worldId, DistrictId = districtId };
                data.Districts[key] = district;
            }
            district.WorldName = plugin.GetWorldName((uint)worldId);
            district.DistrictName = plugin.GetDistrictName((uint)districtId);
            return district;
        }

        /// <summary>Record one ward's info (replaces that ward's open plots).</summary>
        public void RecordWard(HousingWardInfo wardInfo) {
            int worldId = wardInfo.LandIdent.WorldId;
            int districtId = wardInfo.LandIdent.TerritoryTypeId;
            if (!Utils.IsHousingDistrict(districtId)) return;
            int ward = wardInfo.LandIdent.WardNumber + 1;
            DateTime now = DateTime.Now;

            lock (fileLock) {
                LocalDistrict district = GetOrCreateDistrict(worldId, districtId);
                var previous = district.OpenPlots.Where(p => p.WardNumber == ward).ToDictionary(p => p.PlotNumber);
                district.OpenPlots.RemoveAll(p => p.WardNumber == ward);

                for (int i = 0; i < wardInfo.HouseInfoEntries.Length; i++) {
                    HouseInfoEntry entry = wardInfo.HouseInfoEntries[i];
                    if ((entry.InfoFlags & HousingFlags.PlotOwned) != 0) continue;

                    var (size, initialPrice) = plugin.GetPlotInfo((uint)districtId, i);
                    var plot = new LocalPlot {
                        WardNumber = ward,
                        PlotNumber = i + 1,
                        Size = size,
                        Price = entry.HousePrice != 0 ? entry.HousePrice : initialPrice,
                        PurchaseType = wardInfo.PurchaseType,
                        TenantType = wardInfo.TenantType,
                        SeenAt = now
                    };
                    // keep lottery info if the plot was already open before
                    if (previous.TryGetValue(plot.PlotNumber, out LocalPlot? old)) {
                        plot.LotteryPhase = old.LotteryPhase;
                        plot.LotteryEntries = old.LotteryEntries;
                        plot.LotteryPhaseEndsAt = old.LotteryPhaseEndsAt;
                        plot.LotterySeenAt = old.LotterySeenAt;
                    }
                    district.OpenPlots.Add(plot);
                }

                district.WardSeenAt[ward] = now;
                district.LastUpdated = now;
                district.OpenPlots.Sort((a, b) => a.WardNumber != b.WardNumber
                    ? a.WardNumber.CompareTo(b.WardNumber)
                    : a.PlotNumber.CompareTo(b.PlotNumber));
            }
            Save();
        }

        /// <summary>Record lottery info seen on a placard.</summary>
        public void RecordLottery(uint worldId, ushort districtId, byte wardId, byte plotId, PlacardSaleInfo saleInfo) {
            if (!Utils.IsHousingDistrict(districtId)) return;
            int ward = wardId + 1;
            int plotNum = plotId + 1;
            DateTime now = DateTime.Now;

            lock (fileLock) {
                LocalDistrict district = GetOrCreateDistrict((int)worldId, districtId);
                LocalPlot? plot = district.OpenPlots.FirstOrDefault(p => p.WardNumber == ward && p.PlotNumber == plotNum);
                if (plot == null) {
                    var (size, initialPrice) = plugin.GetPlotInfo(districtId, plotId);
                    plot = new LocalPlot {
                        WardNumber = ward,
                        PlotNumber = plotNum,
                        Size = size,
                        Price = initialPrice,
                        SeenAt = now
                    };
                    district.OpenPlots.Add(plot);
                }

                plot.PurchaseType = saleInfo.PurchaseType;
                plot.TenantType = saleInfo.TenantType;
                plot.LotteryPhase = saleInfo.AvailabilityType;
                plot.LotteryEntries = saleInfo.EntryCount;
                plot.LotteryPhaseEndsAt = saleInfo.PhaseEndsAt == 0
                    ? null
                    : DateTimeOffset.FromUnixTimeSeconds(saleInfo.PhaseEndsAt).LocalDateTime;
                plot.LotterySeenAt = now;
                district.LastUpdated = now;
            }
            Save();
        }

        public void Clear() {
            lock (fileLock) data = new LocalData();
            Save();
        }

        // ==== CSV ====
        public string ExportCsv(IEnumerable<LocalDistrict> districts, string fileTag) {
            Directory.CreateDirectory(ExportDirectory);
            string safeTag = string.Concat(fileTag.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            string path = Path.Combine(ExportDirectory, $"{DateTime.Now:yyyyMMdd-HHmmss}_{safeTag}.csv");

            var sb = new StringBuilder();
            sb.AppendLine("伺服器,住宅區,區,號,大小,價格,購買方式,購買限制,抽籤狀態,抽籤人數,階段結束,巡房時間,看板時間");
            lock (fileLock) {
                foreach (LocalDistrict d in districts.OrderBy(d => d.WorldId).ThenBy(d => d.DistrictId))
                foreach (LocalPlot p in d.OpenPlots) {
                    sb.AppendJoin(',', new[] {
                        Csv(d.WorldName), Csv(d.DistrictName),
                        p.WardNumber.ToString(), p.PlotNumber.ToString(),
                        Csv(TwText.Size(p.Size)), p.Price.ToString(),
                        Csv(TwText.Purchase(p.PurchaseType)), Csv(TwText.Tenant(p.TenantType)),
                        Csv(TwText.Lottery(p.LotteryPhase)), p.LotteryEntries?.ToString() ?? "",
                        p.LotteryPhaseEndsAt?.ToString("yyyy-MM-dd HH:mm") ?? "",
                        p.SeenAt.ToString("yyyy-MM-dd HH:mm"),
                        p.LotterySeenAt?.ToString("yyyy-MM-dd HH:mm") ?? ""
                    });
                    sb.AppendLine();
                }
            }

            // UTF-8 with BOM so Excel shows Chinese correctly
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            return path;
        }

        private static string Csv(string s) {
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }

    public static class TwText {
        public static string Size(byte size) => size switch {
            0 => "S",
            1 => "M",
            2 => "L",
            _ => "?"
        };

        public static string Purchase(PurchaseType t) => t switch {
            PurchaseType.FCFS => "先到先得",
            PurchaseType.Lottery => "抽籤",
            PurchaseType.Unavailable => "不可購買",
            _ => ""
        };

        public static string Tenant(TenantType t) => t switch {
            TenantType.FreeCompany => "部隊",
            TenantType.Personal => "個人",
            _ => ""
        };

        public static string Lottery(AvailabilityType? a) => a switch {
            AvailabilityType.Available => "可參加",
            AvailabilityType.InResultsPeriod => "公布結果中",
            AvailabilityType.Unavailable => "不可參加",
            _ => ""
        };
    }
}

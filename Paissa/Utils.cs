
namespace AutoSweep.Paissa {
    public class Utils {
        // configuration constants
        public const string CommandName = "/psweep";
        public const string HouseCommandName = "/phouse";
        public const int DefaultWardsPerDistrict = 18; // TW client currently has 18 wards (global: 30)

        public static bool IsHousingDistrict(int territoryTypeId) {
            return territoryTypeId is 339 or 340 or 341 or 641 or 979;
        }

        public static uint TerritoryTypeIdToLandSetId(uint territoryTypeId) {
            return territoryTypeId switch {
                641 => 3, // shirogane
                979 => 4, // empyreum
                _ => territoryTypeId - 339 // mist, lb, gob are 339-341
            };
        }

        public static string FormatCustomOutputString(
            string template,
            string districtName,
            string districtNameNoSpaces,
            string worldName,
            string wardNum,
            string plotNum,
            string housePrice,
            string housePriceMillions,
            string houseSizeName
        ) {
            // mildly disgusting
            // why can't we have nice things like python :(
            return template.Replace("{districtName}", districtName)
                .Replace("{districtNameNoSpaces}", districtNameNoSpaces)
                .Replace("{worldName}", worldName)
                .Replace("{wardNum}", wardNum)
                .Replace("{plotNum}", plotNum)
                .Replace("{housePrice}", housePrice)
                .Replace("{housePriceMillions}", housePriceMillions)
                .Replace("{houseSizeName}", houseSizeName);
        }
    }
}

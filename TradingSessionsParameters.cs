using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ImbStrV3
{
    public struct TradingSessionsParameters
    {
        public double MinDailyVolume { get; set; }
        public double MinPrice { get; set; }
        public double MaxPrice { get; set; }
        public int ObservedDays { get; set; }
        public string FilePath { get; set; }
        public double AtrPercentLevel { get; set; }
        public double Quantity { get; set; }
        public double Slpercent { get; set; }

        public TradingSessionsParameters(
            double minDailyVolume,
            double minPrice,
            double maxPrice,
            int observedDays,
            string filePath,
            double atrPercentLevel,
            double quantity,
            double slpercent = 0.01)
        {
            MinDailyVolume = minDailyVolume;
            MinPrice = minPrice;
            MaxPrice = maxPrice;
            ObservedDays = observedDays;
            FilePath = filePath;
            AtrPercentLevel = atrPercentLevel;
            Quantity = quantity;
            Slpercent = slpercent;
        }
    }
}

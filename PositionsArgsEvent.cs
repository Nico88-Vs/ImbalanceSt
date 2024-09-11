using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TradingPlatform.BusinessLayer;

namespace ImbStrV3
{
    public enum PositionEvenType
    {
        In,
        TP,
        SL
    }
    public class PositionsArgsEvent : EventArgs
    {
        public Symbol Symbol { get; set; }
        public PositionEvenType EvenType { get; set; }
        public Side Side { get; set; }
        public double Quantity { get; }
        public string PositionId { get; set; }
        public double TriggerPrice { get; set; }

        public PositionsArgsEvent(Symbol symbol, PositionEvenType evenType, Side side, Double quantity, string positionId = "")
        {
            this.Symbol = symbol;
            this.Side = side;
            this.Quantity = quantity;
            this.EvenType = evenType;
            this.PositionId = positionId;
        }
    }
}

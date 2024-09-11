using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TradingPlatform.BusinessLayer;

namespace ImbStrV3
{
    public class TradableSymbolsManager
    {
        public Account CurrentAccount { get; private set; }
        public Connection CurrentConnection { get; private set; }
        public string MarketOpenOrderTypeId { get; private set; }
        public string MarketTPTypeId { get; private set; }
        public string marketSlTypeId { get; private set; }
        public Symbol CurrentSymbol { get; private set; }
        public bool AllowToTrade { get; private set; } = false;
        public List<Position> Positions { get; private set; }
        //TODO: porco dio non usare una lista 
        public string PositionId { get; set; }
        public double SLpercentage { get; private set; }

        public TradableSymbolsManager(Symbol symbol, Account account, Connection connection, double slperc = 0.01)
        {
            //TODO: manca una gestione dei cts interni
            this.CurrentSymbol = symbol;
            this.CurrentAccount = account;
            this.CurrentConnection = connection;
            this.Validate();
            this.Positions = new List<Position>();
            if (this.AllowToTrade)
            {


            }
        }

        private void Instance_PositionRemoved(Position obj)
        {
            if (obj.Symbol.Id == this.CurrentSymbol.Id & this.Positions.Contains(obj))
            {
                this.Positions.Remove(obj);
                this.AllowToTrade = true;
            }
        }
        private void Instance_PositionAdded(Position obj)
        {
            if (obj.Symbol.Id == this.CurrentSymbol.Id & !this.Positions.Contains(obj))
            {
                this.AllowToTrade = true;
                this.Positions.Add(obj);
                this.PositionId = obj.Id;
                this.PlaceSl(obj);
            }
        }

        private void Validate()
        {
            if (this.CurrentSymbol.Connection.Id == this.CurrentConnection.Id & this.CurrentConnection.Id == this.CurrentAccount.ConnectionId)
            {
                //TODO:occhio ai tipi
                OrderType inMarket = this.CurrentSymbol.GetAlowedOrderTypes(OrderTypeUsage.All).FirstOrDefault(ot => ot.Behavior == OrderTypeBehavior.Market);
                if (inMarket == null)
                {
                    inMarket = this.CurrentSymbol.GetAlowedOrderTypes(OrderTypeUsage.Order).FirstOrDefault(ot => ot.Behavior == OrderTypeBehavior.Market);
                }
                OrderType outSL = this.CurrentSymbol.GetAlowedOrderTypes(OrderTypeUsage.CloseOrder).FirstOrDefault(ot => ot.Behavior == OrderTypeBehavior.Stop);
                OrderType outTP = this.CurrentSymbol.GetAlowedOrderTypes(OrderTypeUsage.CloseOrder).FirstOrDefault(ot => ot.Behavior == OrderTypeBehavior.Stop);
                if (outTP == null)
                    outTP = this.CurrentSymbol.GetAlowedOrderTypes(OrderTypeUsage.CloseOrder).FirstOrDefault();

                if (inMarket != null & outSL != null & outTP != null)
                {
                    this.MarketOpenOrderTypeId = inMarket.Id;
                    this.MarketTPTypeId = inMarket.Id;
                    this.MarketTPTypeId = outTP.Id;
                    this.AllowToTrade = true;

                    Core.Instance.PositionAdded += this.Instance_PositionAdded;
                    Core.Instance.PositionRemoved += this.Instance_PositionRemoved;
                }
                else
                {
                    List<OrderType> list = new List<OrderType>() { inMarket, outSL, outTP };
                    foreach (var item in list)
                    {
                        Core.Instance.Loggers.Log($"Missing Order Type couse Null {nameof(item)}");
                    }
                }
            }
        }

        public void Trade(PositionsArgsEvent args)
        {
            if (!AllowToTrade)
                return;

            this.AllowToTrade = false;

            if (args.EvenType == PositionEvenType.In)
                if (this.Positions.Any())
                    return;

            if (args.EvenType == PositionEvenType.In)
            {
                var request = new PlaceOrderRequestParameters
                {
                    Symbol = this.CurrentSymbol,
                    Account = this.CurrentAccount,
                    Side = args.Side,
                    Quantity = args.Quantity,
                    OrderTypeId = this.MarketOpenOrderTypeId,
                    Comment = "In"

                };

                var result = Core.Instance.PlaceOrder(request);
                //TODO: manca lo split ed un log var results = Core.Instance.PlaceOrders(request);

                //if (result.Status == TradingOperationResultStatus.Failure)
                Core.Instance.Loggers.Log(result.Message, LoggingLevel.Trading);

            }

            //TODO:Occhio al trigger price!!
            if (args.EvenType == PositionEvenType.TP)
            {
                //TODO: proviamo a nercato
                if (!this.Positions.Any())
                {
                    //Logs somenting
                    //checked if ps.id ps[0].it sono uguali
                }
                var request = new PlaceOrderRequestParameters
                {
                    Symbol = this.CurrentSymbol,
                    Account = this.CurrentAccount,
                    Side = args.Side,
                    Quantity = args.Quantity,
                    OrderTypeId = this.MarketTPTypeId,
                    PositionId = this.PositionId,
                    Comment = "Take Profit",
                    TriggerPrice = args.TriggerPrice,
                    AdditionalParameters = new List<SettingItem>
                    {
                        new SettingItemBoolean(OrderType.REDUCE_ONLY, true)
                    }

                };

                var result = Core.Instance.PlaceOrder(request);
                //TODO: manca lo split ed un log var results = Core.Instance.PlaceOrders(request);

                //if (result.Status == TradingOperationResultStatus.Failure)
                Core.Instance.Loggers.Log(result.Message, LoggingLevel.Trading);
            }
        }

        private void PlaceSl(Position ps)
        {
            //TODO: inserire un vero stop los con stop holder
            var request = new PlaceOrderRequestParameters
            {
                Symbol = this.CurrentSymbol,
                Account = this.CurrentAccount,
                Side = ps.Side == Side.Buy ? Side.Sell : Side.Buy,
                Quantity = ps.Quantity,
                OrderTypeId = this.marketSlTypeId,
                TriggerPrice = ps.Side == Side.Buy ? ps.OpenPrice * (1 - this.SLpercentage) : ps.OpenPrice * (1 - this.SLpercentage),
                Comment = "Sl",
                PositionId = this.PositionId,
                AdditionalParameters = new List<SettingItem>
                {
                    new SettingItemBoolean(OrderType.REDUCE_ONLY, true)
                }

            };

            var result = Core.Instance.PlaceOrder(request);
            //TODO: manca lo split ed un log var results = Core.Instance.PlaceOrders(request);

            if (result.Status == TradingOperationResultStatus.Failure)
                Core.Instance.Loggers.Log(result.Message);
        }



    }
}

// Copyright QUANTOWER LLC. © 2017-2023. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using TradingPlatform.BusinessLayer;

namespace ImbStrV3
{
    public class ImbStrV3 : Strategy
    {
        #region Imput Parameters
        //Setting Users imput
        [InputParameter("Min Daily Volume Avarage", 0, 1, 9999999, 1, 0)]     //nome,indice,valore.min,valore.max,precision
        public int _min_Daily_Vol = 100000;

        [InputParameter("Dais in avarage", 1, 1, 30, 1, 0)]     //nome,indice,valore.min,valore.max,precision
        public int _avarage_dais = 7;

        [InputParameter("Min Price", 2, 0.1, 1000000, 0.1, 1)]
        public double _min_Price = 1;

        [InputParameter("Max Price", 3, 0.1, 1000000, 0.1, 1)]
        public double _max_Price = 12000;

        [InputParameter("Filtered Symbols Path", 5)]
        public string _symbols_Pat = $"C:\\Users\\user\\Desktop";

        [InputParameter("Imbalance Area Percent", 6, 0.005, 1, 0.005, 3)]
        public double _Imb_Area = 0.01;

        [InputParameter("Imbalance Deviation Trigger Percent", 7, 1, 49, 1, 0)]
        public double _Imb_Deviation = 70;

        [InputParameter("Sl Percent", 8, 0.001, 1, 0.005, 3)]
        public double _Sl_Percent = 0.01;

        [InputParameter("Trade Quantity", 9, double.MinValue, double.MaxValue, 1, 3)]
        public double _tradable_Quantity = 1;

        [InputParameter("Max Exposition", 10, double.MinValue, double.MaxValue, 1, 3)]
        public double _max_expo = 1;

        [InputParameter("Atr Percent", 11, 0.005, 1, 0.005, 3)]
        public double _atr_percent = 1;

        [InputParameter("Re_Filter Symb", 12)]
        public bool _refilter = false;

        [InputParameter("Account", 13)]
        public Account _account = Core.Instance.Accounts[0];

        [InputParameter("Connection", 14)]
        public Connection _Connection = Core.Instance.Connections.Connected[0];

        [InputParameter("Out Ratio", 15, 0.001, 1, 0.001, 3)]
        public double _OutRatio = 0.1;
        #endregion

        #region local var
        private TradingSessionsParameters _SessionsParameters;
        private SymbolFilterer _SymbolFilterer;
        private List<Symbol> _SymbolsIntrade;
        #endregion
        public ImbStrV3()
            : base()
        {
            // Defines strategy's name and description.
            this.Name = "ImbStrV3";
            this.Description = "My strategy's annotation";
        }


        #region events
        private void _SymbolFilterer_TradingSymbolsUpdated(object sender, List<Symbol> e)
        {
            //TODO: Implementare un sistema d aggiornamento anziche una sostituzione
            try
            {
                foreach (Symbol symbol in this._SymbolsIntrade)
                {
                    symbol.NewLevel2 -= this.Symbol_NewLevel2;
                }
            }
            catch (Exception ex)
            {
                Core.Instance.Loggers.Log("Failed Symbol UnScription", message: ex.Message, loggingLevel: LoggingLevel.Error);
            }

            this._SymbolsIntrade = e;

            foreach (Symbol symbol in this._SymbolsIntrade)
            {
                symbol.NewLevel2 += this.Symbol_NewLevel2;
            }
        }
        private void Symbol_NewLevel2(Symbol symbol, Level2Quote level2, DOMQuote dom)
        {
            double delta = 0;
            double last = -1;

            if (dom != null)
            {
                delta = this.CalculateImbalance(dom.Asks, dom.Bids, symbol);
                last = dom.Asks.OrderByDescending(x => x.Price).First().Price;
            }
            else
            {
                if (level2 != null)
                {
                    var depthOfMarket = symbol.DepthOfMarket.GetDepthOfMarketAggregatedCollections();
                    delta = this.CalculateImbalance(depthOfMarket.Asks, depthOfMarket.Bids, symbol);
                    last = depthOfMarket.Asks.OrderByDescending(x => x.Price).First().Price;
                }
            }

            var position = TradeFollower.InTrade(symbol);

            if (position == null)
            {
                //HINT: In condiction
                //TODO: provo a invertire i side
                if (delta != -1)
                {
                    if (delta < _Imb_Deviation * -1)
                    {
                        TradeFollower.TradeAsync(new PositionsArgsEvent(symbol, PositionEvenType.In, Side.Sell, _SessionsParameters.Quantity));
                    }
                    if (delta > _Imb_Deviation)
                    {
                        TradeFollower.TradeAsync(new PositionsArgsEvent(symbol, PositionEvenType.In, Side.Buy, _SessionsParameters.Quantity));
                    }
                }
            }

            else
            {
                //HINT: out condiction
                Side s = position.Side == Side.Sell ? Side.Buy : Side.Sell;

                if (delta != -1)
                {
                    //TODO: da gestire l out ratio
                    if (delta > _Imb_Deviation * -_OutRatio)
                    {
                        PositionsArgsEvent evento = new PositionsArgsEvent(symbol, PositionEvenType.TP, s, position.Quantity, position.Id);
                        evento.TriggerPrice = last;
                        TradeFollower.TradeAsync(evento);
                    }
                    if (delta < _Imb_Deviation * _OutRatio)
                    {
                        PositionsArgsEvent evento = new PositionsArgsEvent(symbol, PositionEvenType.TP, s, position.Quantity, position.Id);
                        evento.TriggerPrice = last;
                        TradeFollower.TradeAsync(evento);
                    }
                }
            }
        }
        #endregion


        #region QtEvents
        protected override void OnCreated()
        {
            this._SymbolsIntrade = new List<Symbol>();
        }
        protected override void OnRun()
        {
            this._SessionsParameters = new TradingSessionsParameters()
            {
                AtrPercentLevel = _atr_percent,
                MinDailyVolume = _min_Daily_Vol,
                MinPrice = _min_Price,
                MaxPrice = _max_Price,
                ObservedDays = _avarage_dais,
                FilePath = _symbols_Pat,
                Quantity = _tradable_Quantity,
                Slpercent = _Sl_Percent,
            };

            this._SymbolFilterer = new SymbolFilterer(this._SessionsParameters, this._Connection);
            this._SymbolFilterer.TradingSymbolsUpdated += this._SymbolFilterer_TradingSymbolsUpdated;
            this._SymbolFilterer.Init(this._refilter);


            TradeFollower.Init(this._account, this._Connection, this._SessionsParameters);
        }

        /// <summary>
        /// This function will be called after stopping a strategy
        /// </summary>
        protected override void OnStop()
        {
            this._SymbolFilterer.TradingSymbolsUpdated -= this._SymbolFilterer_TradingSymbolsUpdated;

            foreach (Symbol symbol in this._SymbolsIntrade)
            {
                symbol.NewLevel2 -= this.Symbol_NewLevel2;
            }

            this._SymbolFilterer.Clear();
            TradeFollower.StopTasck();
        }

        /// <summary>
        /// This function will be called after removing a strategy
        /// </summary>
        protected override void OnRemove()
        {
        }

        protected override void OnSettingsUpdated()
        {
            base.OnSettingsUpdated();

            //TODO: Verificare!!!!!
            if (_refilter)
            {
                this._SymbolFilterer.Clear();
                this._SymbolFilterer.Init(_refilter);
            }
        }

        #endregion

        #region utils
        private double CalculateImbalance(List<Level2Quote> buy, List<Level2Quote> sell, Symbol symbol)
        {
            //TODO: non usare ask da simbol!!!!! is nan
            double top_level = symbol.Bid * (1 + (_Imb_Area / 2));
            double bottom_level = symbol.Ask * (1 - (_Imb_Area / 2));

            if (!(top_level > 0))
                top_level = symbol.Last * (1 + (_Imb_Area / 2));

            if (!(bottom_level > 0))
                bottom_level = symbol.Last * (1 - (_Imb_Area / 2));

            double buy_pressure = 0;
            double sell_pressure = 0;

            List<Level2Quote> buyers = buy.Where(x => x.Price >= bottom_level).ToList();
            foreach (var item in buyers)
            {
                buy_pressure += item.Size;
            }

            List<Level2Quote> sellers = sell.Where(x => x.Price <= top_level).ToList();
            foreach (var item in sellers)
            {
                sell_pressure += item.Size;
            }

            double total_size = buy_pressure + sell_pressure;
            double delta = buy_pressure - sell_pressure;

            return (delta / total_size) * 100;
        }

        private double CalculateImbalance(Level2Item[] buy, Level2Item[] sell, Symbol symbol)
        {
            double top_level = symbol.Bid * (1 + (_Imb_Area / 2));
            double bottom_level = symbol.Ask * (1 - (_Imb_Area / 2));

            if (!(top_level > 0))
                top_level = symbol.Last * (1 + (_Imb_Area / 2));

            if (!(bottom_level > 0))
                bottom_level = symbol.Last * (1 - (_Imb_Area / 2));

            double buy_pressure = 0;
            double sell_pressure = 0;

            List<Level2Item> temp_buy = buy.ToList();
            List<Level2Item> buyers = temp_buy.Where(x => x.Price >= bottom_level).ToList();

            foreach (var item in buyers)
            {
                buy_pressure += item.Size;
            }

            List<Level2Item> temp_sellers = sell.ToList();
            List<Level2Item> sellers = temp_sellers.Where(x => x.Price <= top_level).ToList();

            foreach (var item in sellers)
            {
                sell_pressure += item.Size;
            }

            double total_size = buy_pressure + sell_pressure;
            double delta = buy_pressure - sell_pressure;

            return (delta / total_size) * 100;
        }
        #endregion
    }
}

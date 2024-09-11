using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using TradingPlatform.BusinessLayer;

namespace ImbStrV3
{
    public class SymbolFilterer
    {
        public bool DebugMode { get; set; } = true;

        #region Prop and var
        public double Min_Daily_Volume { get; }
        public double Min_Price { get; }
        public double Max_Price { get; }
        public int Observed_Days { get; }
        public List<Symbol> SelectedSymbols { get; private set; }
        public string FilePath { get; private set; }
        public List<HistoricalData> ObservedHd { get; private set; }
        public List<Indicator> ObservedAtr { get; private set; }
        private double atr_percent_level;
        public List<Symbol> TradingSymbols { get; private set; }

        private readonly Queue<Action> buffer;
        private readonly object bufferLocker;

        private readonly ManualResetEvent resetEvent;
        private CancellationTokenSource cts;

        public event EventHandler<List<Symbol>> SymbolsAdded;
        public event EventHandler<List<Symbol>> TradingSymbolsUpdated;
        private string ConnectionID;

        public double process_percent { get; set; }

        #endregion

        //TODO: salva il path nel json!!
        public SymbolFilterer(double min_daily_volume = 20000, double min_price = 1, double max_price = 12000, int observed_days = 7, string path = "", double atr_percent_level = 0.01, string connectionID = null)
        {
            //TODO: manca la selezione della connessione
            this.Min_Daily_Volume = min_daily_volume;
            this.Min_Price = min_price;
            this.Max_Price = max_price;
            this.Observed_Days = observed_days;
            this.ConnectionID = connectionID;
            string _temp_path = Path.Combine(path, this.ConnectionID);
            this.FilePath = this.CorrectPath(_temp_path);
            this.atr_percent_level = atr_percent_level;
            this.TradingSymbols = new List<Symbol>();

            this.buffer = new Queue<Action>();
            this.bufferLocker = new object();
            this.resetEvent = new ManualResetEvent(false);
            this.SymbolsAdded += this.SymbolFilterer_SymbolsAdded;
        }

        public SymbolFilterer(TradingSessionsParameters parameters, Connection connection)
        {
            this.Min_Daily_Volume = parameters.MinDailyVolume;
            this.Min_Price = parameters.MinPrice;
            this.Max_Price = parameters.MaxPrice;
            this.Observed_Days = parameters.ObservedDays;
            this.ConnectionID = connection.Id;
            string _temp_path = Path.Combine(parameters.FilePath, this.ConnectionID);
            this.FilePath = this.CorrectPath(_temp_path);
            this.atr_percent_level = parameters.AtrPercentLevel;

            this.TradingSymbols = new List<Symbol>();

            this.buffer = new Queue<Action>();
            this.bufferLocker = new object();
            this.resetEvent = new ManualResetEvent(false);
            this.SymbolsAdded += this.SymbolFilterer_SymbolsAdded;
        }



        private void SymbolFilterer_SymbolsAdded(object sender, List<Symbol> e)
        {
            //TODO: tento di evitare un comportamento anomalo infilando i processi asincroni MANCA IL CTS
            lock (this.bufferLocker)
            {
                this.buffer.Enqueue(() => this.CreateAtrHD(this.cts.Token));
            }
            this.resetEvent.Set();
        }

        public void Init(bool run_extra_researc = false)
        {
            this.cts = new CancellationTokenSource();
            Task.Factory.StartNew(this.Process, this.cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            this.SelectedSymbols = new List<Symbol>();

            //TODO: manca il sistema per eseguire scansioni discrezioanli
            bool retrived_from_file = false;

            lock (this.bufferLocker)
            {
                if (!run_extra_researc)
                    retrived_from_file = this.RetriveSymbolsFromFile();
                if (retrived_from_file)
                    this.OnSymbolsAddedd(this.SelectedSymbols);
                if (!retrived_from_file)
                    this.buffer.Enqueue(() => this.ComputeAlFiltering(this.cts.Token));
            }
            this.resetEvent.Set();
        }

        #region Async
        private void Process()
        {
            while (true)
            {
                try
                {
                    this.resetEvent.WaitOne();

                    if (this.cts?.IsCancellationRequested ?? false)
                        return;

                    while (this.buffer.Count > 0)
                    {
                        if (this.cts?.IsCancellationRequested ?? false)
                            return;

                        try
                        {
                            Action action;

                            lock (this.bufferLocker)
                                action = this.buffer.Dequeue();

                            action.Invoke();
                        }
                        catch (Exception ex)
                        {
                            Core.Instance.Loggers.Log(ex);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Core.Instance.Loggers.Log(ex);
                }
                finally
                {
                    this.resetEvent.Reset();
                }
            }
        }
        private void ComputeAlFiltering(CancellationToken cancellationToken)
        {
            List<Symbol> resoult = new List<Symbol>();

            this.process_percent = 0;
            int percent = 0;

            List<Symbol> _temp = new List<Symbol>();

            try
            {
                _temp = Core.Instance.Symbols.Where(x => x.ConnectionId == this.ConnectionID).ToList();
            }
            catch (Exception ex)
            {

                Core.Instance.Loggers.Log("Error while retriving symbols from connection", LoggingLevel.Error);
                Core.Instance.Loggers.Log(ex.Message, LoggingLevel.Error);
            }

            foreach (Symbol s in _temp)
            {
                try
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        Core.Instance.Loggers.Log($"Process cancelled for symbol {s.Name}", LoggingLevel.System);
                        break;
                    }

                    Core.Instance.Loggers.Log($"Dedicate Process : For Symbol {s.Name}");
                    percent += 1;
                    var canc = new CancellationTokenSource(TimeSpan.FromSeconds(100));
                    var HD = s.GetHistory(new HistoryRequestParameters()
                    {
                        Aggregation = new HistoryAggregationTime(Period.HOUR12),
                        FromTime = Core.Instance.TimeUtils.DateTimeUtcNow.AddDays(-this.Observed_Days),
                        ToTime = default(DateTime),
                        Symbol = s,
                        CancellationToken = canc.Token,
                        HistoryType = HistoryType.Last
                    });
                    Core.Instance.Loggers.Log($"Dedicate Process : Hd Created {HD.ToString()}");

                    var close = HD[0][PriceType.Close];
                    Core.Instance.Loggers.Log($"Dedicate Process : With Close {close}");


                    if (close > this.Min_Price & close < this.Max_Price)
                    {
                        double total_vol = 0;
                        for (int i = 0; i < HD.Count; i++)
                        {
                            total_vol += HD[i][PriceType.Volume];
                            Core.Instance.Loggers.Log($"Dedicate Process : With Vol[{i}] {HD[i][PriceType.Volume]}");
                        }

                        if ((total_vol / HD.Count) * 2 > this.Min_Daily_Volume)
                            resoult.Add(s);
                    }

                    HD.Dispose();

                    this.process_percent = percent;
                }
                catch (Exception ex)
                {
                    Core.Instance.Loggers.Log($"Dedicate Process : FAILED ___ For symb {s.Name} with message _____ {ex.Message}", LoggingLevel.Error);
                }

            }
            SaveJsonListOfStrings(resoult);
            this.OnSymbolsAddedd(resoult);
        }
        private void CreateAtrHD(CancellationToken cancellationToken)
        {
            //TODO: mancano i settaggi dell  indicatore
            this.ObservedHd = new List<HistoricalData>();
            this.ObservedAtr = new List<Indicator>();

            if (this.SelectedSymbols.Count < 1)
                return;

            foreach (Symbol s in SelectedSymbols)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    Core.Instance.Loggers.Log($"Process cancelled for symbol {s.Name}", LoggingLevel.System);

                    break;
                }
                Indicator atr_indi = Core.Instance.Indicators.BuiltIn.ATR(16, MaMode.EMA);
                HistoricalData hd = s.GetHistory(Period.MIN1, DateTime.Now.AddHours(-Observed_Days));
                hd.AddIndicator(atr_indi);
                this.ObservedHd.Add(hd);
                this.ObservedAtr.Add(atr_indi);
            }

            //HINT:Subscribing HD events Here
            foreach (HistoricalData item in this.ObservedHd)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    Core.Instance.Loggers.Log($"Process cancelled for Historical Data {item.ToString()}", LoggingLevel.System);
                    break;
                }
                Core.Instance.Loggers.Log($"Dedicate Process HD session: created HD {item.ToString()}");
                Core.Instance.Loggers.Log($"Dedicate Process HD session: created HD.name {item.Symbol.Name}");
                item.NewHistoryItem += this.Item_NewHistoryItem;
            }

        }
        private void BuildIndi(List<HistoricalData> items)
        {
            bool list_modifited = false;

            foreach (var item in items)
            {
                Indicator indi = this.ObservedAtr.First(x => x.Symbol == item.Symbol);

                var value_indi = indi.GetValue();
                var close = item.Close();

                var value = Math.Abs(this.normalizeAtr(value_indi, close));

                bool atr_response = value < this.atr_percent_level;
                Core.Instance.Loggers.Log($"Dedicate Process ATR: response {atr_response}");
                Core.Instance.Loggers.Log($"Dedicate Process ATR: value {value}");
                Core.Instance.Loggers.Log($"Dedicate Process ATR: atr_percent_level {this.atr_percent_level}");


                if (atr_response)
                {
                    if (!this.TradingSymbols.Contains(item.Symbol))
                    {
                        this.TradingSymbols.Add(item.Symbol);
                        list_modifited = true;
                    }
                }

                else if (!atr_response)
                {
                    if (this.TradingSymbols.Contains(item.Symbol))
                    {
                        this.TradingSymbols.Remove(item.Symbol);
                        list_modifited = true;
                    }
                }
            }

            if (list_modifited)
                this.OnTradingSymbolsUpdated();

        }
        #endregion

        #region Events
        private void Item_NewHistoryItem(object sender, HistoryEventArgs e)
        {
            Core.Instance.Loggers.Log($"Dedicate Process HD session: New Item From");

            lock (this.bufferLocker)
            {
                this.buffer.Enqueue(() => this.BuildIndi(this.ObservedHd));
            }
            this.resetEvent.Set();
        }
        private void OnTradingSymbolsUpdated()
        {
            Core.Instance.Loggers.Log($"Dedicate Process TradingATRUpdate: ATR update eventt FIRED");

            this.TradingSymbolsUpdated?.Invoke(this, this.TradingSymbols);
        }
        private void OnSymbolsAddedd(List<Symbol> symbols)
        {
            this.SelectedSymbols = symbols;
            this.SymbolsAdded?.Invoke(this, symbols);
        }
        #endregion

        #region Utils
        private string CorrectPath(string path)
        {
            // Normalizza il percorso
            path = Path.GetFullPath(path);

            // Sostituisce slash con il separatore corretto
            path = path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);

            // Rimuove caratteri non validi
            path = string.Concat(path.Where(c => !Path.GetInvalidPathChars().Contains(c)));

            return path;
        }
        private double normalizeAtr(double atr, double price) { return atr / price; }
        public void Clear()
        {
            this.cts?.Cancel();

            lock (this.bufferLocker)
                this.buffer.Clear();
            foreach (var item in this.ObservedHd)
            {
                item.NewHistoryItem -= this.Item_NewHistoryItem;
            }
            this.resetEvent.Set();
        }
        private void SaveJsonListOfStrings(List<Symbol> symbols)
        {
            try
            {
                List<string> symbolsNames = symbols.Select(x => x.Name).ToList();

                if (!File.Exists(this.FilePath))
                    using (File.Create(this.FilePath)) { }

                string jsonString = JsonConvert.SerializeObject(symbolsNames, Formatting.Indented);

                File.WriteAllText(this.FilePath, jsonString);

            }
            catch (Exception ex)
            {
                Core.Instance.Loggers.Log(ex.Message, LoggingLevel.Error);
            }
        }
        private bool RetriveSymbolsFromFile()
        {
            List<Symbol> temp_symbols = new List<Symbol>();

            try
            {
                string jsonString = File.ReadAllText(this.FilePath);
                List<string> symbolsNames = JsonConvert.DeserializeObject<List<string>>(jsonString);

                foreach (string symbolName in symbolsNames)
                {
                    Symbol s = Core.Instance.Symbols.FirstOrDefault(x => x.Name == symbolName);
                    temp_symbols.Add(s);
                }

                this.SelectedSymbols = temp_symbols;
                return true;
            }
            catch (Exception ex)
            {
                Core.Instance.Loggers.Log(ex.Message, loggingLevel: LoggingLevel.Error);
                return false;
            }
        }
        #endregion
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TradingPlatform.BusinessLayer;

namespace ImbStrV3
{
    public static class TradeFollower
    {
        private static Queue<Action> buffer;
        private static object bufferLocker;

        private static ManualResetEvent resetEvent;
        private static CancellationTokenSource cts;

        public static TradingSessionsParameters SessionParameter { get; set; }
        public static List<TradableSymbolsManager> TradablesManager { get; set; }
        //HINT:probabilmentre qui e inutile 
        public static Account Account { get; private set; }
        public static bool Started { get; private set; }
        public static Connection Connection { get; private set; }

        public static void Init(Account account, Connection connection, TradingSessionsParameters parameters)
        {
            buffer = new Queue<Action>();
            bufferLocker = new object();
            resetEvent = new ManualResetEvent(false);
            cts = new CancellationTokenSource();

            TradablesManager = new List<TradableSymbolsManager>();
            Account = account;
            Started = false;
            SessionParameter = parameters;
            Connection = connection;
        }

        private static void SymbolFilterer_SymbolsAdded(object sender, List<Symbol> e) => throw new NotImplementedException();
        private static void SymbolFilterer_TradingSymbolsUpdated(object sender, List<Symbol> e) => throw new NotImplementedException();

        //TODO : ricevo l args
        //TODO : verifico che sia tradabile , 1 sola posizione per symbolo
        //TODO : tento di aggiungrlo ed eseguirlo
        private static void Trade(PositionsArgsEvent args)
        {
            try
            {
                if (!TradablesManager.Any(x => x.CurrentSymbol == args.Symbol))
                {
                    if (args.EvenType != PositionEvenType.In)
                    {
                        //TODO: log somenting
                        return;
                    }
                    else
                    {
                        TradableSymbolsManager _temp = new TradableSymbolsManager(args.Symbol, Account, Connection, SessionParameter.Slpercent);
                        TradablesManager.Add(_temp);
                        _temp.Trade(args);
                    }

                }
                else
                {
                    if (args.EvenType == PositionEvenType.In)
                    {
                        if (!TradablesManager.First(x => x.CurrentSymbol == args.Symbol).Positions.Any())
                            TradablesManager.First(x => x.CurrentSymbol == args.Symbol).Trade(args);
                        else
                        {
                            //TODO: log somenting
                            return;
                        }
                    }
                    else
                    {
                        if (TradablesManager.First(x => x.CurrentSymbol == args.Symbol).Positions.Any())
                            TradablesManager.First(x => x.CurrentSymbol == args.Symbol).Trade(args);
                        else
                        {
                            //TODO: log somenting
                        }
                    }

                }
            }
            catch (Exception ex)
            {
                Core.Instance.Loggers.Log("Static Trading Failed!!!!!!!!!!");
                Core.Instance.Loggers.Log(ex.Message);
            }


        }
        public static void TradeAsync(PositionsArgsEvent args)
        {
            if (!Started)
                StarTask();

            lock (bufferLocker)
            {
                Trade(args);
            }
            resetEvent.Set();

        }


        public static Position? InTrade(Symbol s)
        {
            var debug = TradablesManager;
            if (TradablesManager.Any(x => x.CurrentSymbol == s & x.Positions.Any()))
                return TradablesManager.First(x => x.CurrentSymbol == s).Positions.First();
            else return null;
        }

        #region Async Menagment
        public static void StarTask()
        {
            if (!Started)
            {
                cts = new CancellationTokenSource();
                Task.Factory.StartNew(Process, cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

                lock (bufferLocker)
                {
                    start();
                }
                resetEvent.Set();
            }

        }

        private static void start()
        {
            if (!Started)
                Started = true;
            else
            {
                //TODO: log somnting
            }
        }

        public static void StopTasck()
        {
            if (!Started)
            {
                //TODO: Logs Somenting
                return;
            }
            else
            {
                if (cts != null)
                {
                    cts.Cancel();

                }
                else
                {
                    //TODO: Logs Somenting
                }

                Started = false;
                TradablesManager.Clear();
                lock (bufferLocker)
                    buffer.Clear();

                resetEvent.Set();
            }
        }

        private static void Process()
        {
            while (true)
            {
                try
                {
                    //TODO: modificare perche lancaia eccezioni random
                    resetEvent.WaitOne();

                    if (cts?.IsCancellationRequested ?? false)
                        return;

                    while (buffer.Count > 0)
                    {
                        if (cts?.IsCancellationRequested ?? false)
                            return;

                        try
                        {
                            Action action;

                            lock (bufferLocker)
                                action = buffer.Dequeue();

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
                    resetEvent.Reset();
                }
            }
        }
        #endregion
    }
}

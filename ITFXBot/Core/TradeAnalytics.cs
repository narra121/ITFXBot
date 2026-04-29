using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;

namespace cAlgo.Robots
{
    public enum ExitReason
    {
        TakeProfit,
        Lifeline,
        EmergencyStop,
        PushCount,
        TrailingStop,
        Breakeven,
        MaxHoldTime,
        WeekendClose,
        BreakoutGuard,
        ServerSL,
        Unknown
    }

    public class TradeRecord
    {
        public int PositionId { get; set; }
        public string Label { get; set; }
        public string Direction { get; set; }
        public double EntryPrice { get; set; }
        public double ClosePrice { get; set; }
        public double ProfitPips { get; set; }
        public double NetProfit { get; set; }
        public DateTime EntryTime { get; set; }
        public DateTime ExitTime { get; set; }
        public ExitReason ExitReason { get; set; }
        public MarketStateType MarketStateAtEntry { get; set; }
        public double AtrAtEntry { get; set; }
        public double Sma20SlopeAtEntry { get; set; }
        public int PushCountAtExit { get; set; }
        public bool BreakevenWasHit { get; set; }
        public double MaxFavorableExcursion { get; set; }
        public double MaxAdverseExcursion { get; set; }
    }

    public class TradeAnalytics
    {
        private readonly Robot _robot;
        private readonly List<TradeRecord> _completedTrades = new List<TradeRecord>();
        private readonly Dictionary<int, TradeRecord> _openTrades = new Dictionary<int, TradeRecord>();

        private int _signalsGenerated;
        private int _signalsSkippedState;
        private int _signalsSkippedDualTF;
        private int _signalsSkippedExisting;
        private int _signalsSkippedSession;
        private readonly Dictionary<string, int> _stateDistribution = new Dictionary<string, int>();

        public TradeAnalytics(Robot robot)
        {
            _robot = robot;
        }

        public void RecordBarState(MarketStateType state)
        {
            var key = state.ToString();
            if (!_stateDistribution.ContainsKey(key))
                _stateDistribution[key] = 0;
            _stateDistribution[key]++;
        }

        public void RecordSignalSkipped(string reason)
        {
            switch (reason)
            {
                case "state": _signalsSkippedState++; break;
                case "dualTF": _signalsSkippedDualTF++; break;
                case "existing": _signalsSkippedExisting++; break;
                case "session": _signalsSkippedSession++; break;
            }
        }

        public void RecordSignalGenerated() { _signalsGenerated++; }

        public void RecordTradeOpen(int posId, string label, string direction,
            double entryPrice, DateTime entryTime, MarketStateType state, double atr, double slope)
        {
            _openTrades[posId] = new TradeRecord
            {
                PositionId = posId,
                Label = label,
                Direction = direction,
                EntryPrice = entryPrice,
                EntryTime = entryTime,
                MarketStateAtEntry = state,
                AtrAtEntry = atr,
                Sma20SlopeAtEntry = slope,
                MaxFavorableExcursion = 0,
                MaxAdverseExcursion = 0
            };
        }

        public void UpdateExcursions(int posId, double currentPrice)
        {
            if (!_openTrades.ContainsKey(posId)) return;
            var rec = _openTrades[posId];
            double move = rec.Direction == "buy"
                ? currentPrice - rec.EntryPrice
                : rec.EntryPrice - currentPrice;

            if (move > rec.MaxFavorableExcursion)
                rec.MaxFavorableExcursion = move;
            if (-move > rec.MaxAdverseExcursion)
                rec.MaxAdverseExcursion = -move;
        }

        public void RecordTradeClose(int posId, double closePrice, double netProfit,
            double profitPips, ExitReason reason, DateTime exitTime, int pushCount, bool beHit)
        {
            if (!_openTrades.ContainsKey(posId)) return;
            var rec = _openTrades[posId];
            rec.ClosePrice = closePrice;
            rec.NetProfit = netProfit;
            rec.ProfitPips = profitPips;
            rec.ExitReason = reason;
            rec.ExitTime = exitTime;
            rec.PushCountAtExit = pushCount;
            rec.BreakevenWasHit = beHit;
            _completedTrades.Add(rec);
            _openTrades.Remove(posId);
        }

        public void RecordServerClose(int posId, double closePrice, double netProfit, DateTime exitTime)
        {
            if (!_openTrades.ContainsKey(posId)) return;
            var rec = _openTrades[posId];
            double pipSize = _robot.Symbol.PipSize;
            double pips = rec.Direction == "buy"
                ? (closePrice - rec.EntryPrice) / pipSize
                : (rec.EntryPrice - closePrice) / pipSize;

            var reason = pips > 0 ? ExitReason.TakeProfit : ExitReason.ServerSL;
            rec.ClosePrice = closePrice;
            rec.NetProfit = netProfit;
            rec.ProfitPips = pips;
            rec.ExitReason = reason;
            rec.ExitTime = exitTime;
            rec.PushCountAtExit = 0;
            rec.BreakevenWasHit = false;
            _completedTrades.Add(rec);
            _openTrades.Remove(posId);
        }

        public void PrintFullReport()
        {
            _robot.Print("╔══════════════════════════════════════════════════════════════╗");
            _robot.Print("║             ITFX BOT — DETAILED ANALYTICS REPORT           ║");
            _robot.Print("╚══════════════════════════════════════════════════════════════╝");

            PrintOverallStats();
            PrintStrategyBreakdown();
            PrintExitReasonAnalysis();
            PrintDirectionAnalysis();
            PrintMarketStateAnalysis();
            PrintEdgeCaseAnalysis();
            PrintSignalFunnelReport();
            PrintTopLosses();
            PrintMonthlyBreakdown();
        }

        private void PrintOverallStats()
        {
            _robot.Print("\n═══ OVERALL PERFORMANCE ═══");
            var total = _completedTrades.Count;
            if (total == 0) { _robot.Print("No trades recorded."); return; }

            var wins = _completedTrades.Count(t => t.NetProfit > 0);
            var losses = _completedTrades.Count(t => t.NetProfit <= 0);
            var totalNet = _completedTrades.Sum(t => t.NetProfit);
            var avgWin = _completedTrades.Where(t => t.NetProfit > 0).DefaultIfEmpty(new TradeRecord()).Average(t => t.NetProfit);
            var avgLoss = _completedTrades.Where(t => t.NetProfit <= 0).DefaultIfEmpty(new TradeRecord()).Average(t => t.NetProfit);
            var avgDuration = _completedTrades.Average(t => (t.ExitTime - t.EntryTime).TotalHours);

            _robot.Print("Total Trades: {0} | Wins: {1} | Losses: {2} | WinRate: {3:F1}%",
                total, wins, losses, (double)wins / total * 100);
            _robot.Print("Net P/L: {0:F2} | Avg Win: {1:F2} | Avg Loss: {2:F2} | RR: {3:F2}",
                totalNet, avgWin, avgLoss, avgLoss != 0 ? Math.Abs(avgWin / avgLoss) : 0);
            _robot.Print("Avg Hold: {0:F1}h", avgDuration);
        }

        private void PrintStrategyBreakdown()
        {
            _robot.Print("\n═══ PER STRATEGY BREAKDOWN ═══");
            var strategies = _completedTrades.GroupBy(t => t.Label).OrderBy(g => g.Key);

            foreach (var grp in strategies)
            {
                var trades = grp.ToList();
                var wins = trades.Count(t => t.NetProfit > 0);
                var net = trades.Sum(t => t.NetProfit);
                var avgWin = trades.Where(t => t.NetProfit > 0).DefaultIfEmpty(new TradeRecord()).Average(t => t.NetProfit);
                var avgLoss = trades.Where(t => t.NetProfit <= 0).DefaultIfEmpty(new TradeRecord()).Average(t => t.NetProfit);
                var avgMFE = trades.Average(t => t.MaxFavorableExcursion);
                var avgMAE = trades.Average(t => t.MaxAdverseExcursion);
                var avgDur = trades.Average(t => (t.ExitTime - t.EntryTime).TotalHours);

                _robot.Print("--- {0} ---", grp.Key);
                _robot.Print("  Trades: {0} | Wins: {1} ({2:F1}%) | Net: {3:F2}",
                    trades.Count, wins, (double)wins / trades.Count * 100, net);
                _robot.Print("  AvgWin: {0:F2} | AvgLoss: {1:F2} | RR: {2:F2}",
                    avgWin, avgLoss, avgLoss != 0 ? Math.Abs(avgWin / avgLoss) : 0);
                _robot.Print("  AvgMFE: {0:F2} | AvgMAE: {1:F2} | AvgHold: {2:F1}h",
                    avgMFE, avgMAE, avgDur);

                var exitReasons = trades.GroupBy(t => t.ExitReason)
                    .Select(g => g.Key + ":" + g.Count())
                    .ToArray();
                _robot.Print("  Exits: {0}", string.Join(", ", exitReasons));

                var buyCount = trades.Count(t => t.Direction == "buy");
                var buyNet = trades.Where(t => t.Direction == "buy").Sum(t => t.NetProfit);
                var sellCount = trades.Count(t => t.Direction == "sell");
                var sellNet = trades.Where(t => t.Direction == "sell").Sum(t => t.NetProfit);
                _robot.Print("  Buy: {0}t ${1:F2} | Sell: {2}t ${3:F2}", buyCount, buyNet, sellCount, sellNet);
            }
        }

        private void PrintExitReasonAnalysis()
        {
            _robot.Print("\n═══ EXIT REASON ANALYSIS ═══");
            var groups = _completedTrades.GroupBy(t => t.ExitReason).OrderByDescending(g => g.Count());

            foreach (var grp in groups)
            {
                var trades = grp.ToList();
                var wins = trades.Count(t => t.NetProfit > 0);
                var net = trades.Sum(t => t.NetProfit);
                var avg = trades.Average(t => t.NetProfit);
                _robot.Print("{0,-15} {1,4} trades | {2,3} wins ({3:F1}%) | Net: {4:F2} | Avg: {5:F2}",
                    grp.Key, trades.Count, wins, (double)wins / trades.Count * 100, net, avg);
            }
        }

        private void PrintDirectionAnalysis()
        {
            _robot.Print("\n═══ DIRECTION ANALYSIS ═══");
            foreach (var dir in new[] { "buy", "sell" })
            {
                var trades = _completedTrades.Where(t => t.Direction == dir).ToList();
                if (trades.Count == 0) continue;
                var wins = trades.Count(t => t.NetProfit > 0);
                var net = trades.Sum(t => t.NetProfit);
                _robot.Print("{0}: {1} trades | {2} wins ({3:F1}%) | Net: {4:F2}",
                    dir.ToUpper(), trades.Count, wins, (double)wins / trades.Count * 100, net);

                var byStrategy = trades.GroupBy(t => t.Label);
                foreach (var sg in byStrategy)
                {
                    var snet = sg.Sum(t => t.NetProfit);
                    var swins = sg.Count(t => t.NetProfit > 0);
                    _robot.Print("  {0}: {1}t, {2}w, ${3:F2}", sg.Key, sg.Count(), swins, snet);
                }
            }
        }

        private void PrintMarketStateAnalysis()
        {
            _robot.Print("\n═══ MARKET STATE ANALYSIS ═══");

            _robot.Print("Bar distribution:");
            var totalBars = _stateDistribution.Values.Sum();
            foreach (var kvp in _stateDistribution.OrderByDescending(k => k.Value))
            {
                _robot.Print("  {0}: {1} bars ({2:F1}%)", kvp.Key, kvp.Value,
                    (double)kvp.Value / totalBars * 100);
            }

            _robot.Print("Trades entered per state:");
            var stateGroups = _completedTrades.GroupBy(t => t.MarketStateAtEntry);
            foreach (var grp in stateGroups)
            {
                var net = grp.Sum(t => t.NetProfit);
                var wins = grp.Count(t => t.NetProfit > 0);
                _robot.Print("  {0}: {1} trades, {2} wins ({3:F1}%), Net: {4:F2}",
                    grp.Key, grp.Count(), wins, (double)wins / grp.Count() * 100, net);
            }
        }

        private void PrintEdgeCaseAnalysis()
        {
            _robot.Print("\n═══ EDGE CASE / PROBLEM DETECTION ═══");

            var largeLosses = _completedTrades.Where(t => t.NetProfit < -200).ToList();
            _robot.Print("Large losses (> $200): {0}", largeLosses.Count);
            if (largeLosses.Count > 0)
            {
                var byReason = largeLosses.GroupBy(t => t.ExitReason);
                foreach (var g in byReason)
                    _robot.Print("  {0}: {1} trades, total ${2:F2}", g.Key, g.Count(), g.Sum(t => t.NetProfit));
            }

            var gapTrades = _completedTrades.Where(t => (t.ExitTime - t.EntryTime).TotalMinutes < 5 && t.NetProfit < -100).ToList();
            _robot.Print("Likely gap losses (< 5min hold, > $100 loss): {0}", gapTrades.Count);
            foreach (var t in gapTrades)
                _robot.Print("  {0} {1} entry={2:F2} close={3:F2} net=${4:F2} at {5}",
                    t.Label, t.Direction, t.EntryPrice, t.ClosePrice, t.NetProfit, t.EntryTime);

            var mfeWasted = _completedTrades.Where(t => t.NetProfit <= 0 && t.MaxFavorableExcursion > 0).ToList();
            if (mfeWasted.Count > 0)
            {
                var avgWastedMFE = mfeWasted.Average(t => t.MaxFavorableExcursion);
                _robot.Print("Losers that were once winning: {0} (avg MFE wasted: {1:F2} price)", mfeWasted.Count, avgWastedMFE);
            }

            var longHolds = _completedTrades.Where(t => (t.ExitTime - t.EntryTime).TotalHours > 12).ToList();
            _robot.Print("Trades held > 12h: {0}, Net: ${1:F2}",
                longHolds.Count, longHolds.Sum(t => t.NetProfit));

            var pastEmergency = _completedTrades.Where(t => t.ProfitPips < -2480).ToList();
            _robot.Print("Trades past emergency stop (> 2480 pips loss): {0}", pastEmergency.Count);

            var beHitThenLost = _completedTrades.Where(t => t.BreakevenWasHit && t.NetProfit < -50).ToList();
            _robot.Print("Breakeven hit then lost > $50: {0}", beHitThenLost.Count);
        }

        private void PrintSignalFunnelReport()
        {
            _robot.Print("\n═══ SIGNAL FUNNEL ═══");
            _robot.Print("Signals generated:     {0}", _signalsGenerated);
            _robot.Print("Skipped (wrong state): {0}", _signalsSkippedState);
            _robot.Print("Skipped (dual TF):     {0}", _signalsSkippedDualTF);
            _robot.Print("Skipped (existing pos):{0}", _signalsSkippedExisting);
            _robot.Print("Skipped (session):     {0}", _signalsSkippedSession);
            _robot.Print("Executed:              {0}", _completedTrades.Count + _openTrades.Count);
        }

        private void PrintTopLosses()
        {
            _robot.Print("\n═══ TOP 10 LOSSES ═══");
            var worst = _completedTrades.OrderBy(t => t.NetProfit).Take(10);
            foreach (var t in worst)
            {
                _robot.Print("  {0} {1} | Net: ${2:F2} | Pips: {3:F0} | Exit: {4} | State: {5} | Hold: {6:F1}h | MFE: {7:F2}",
                    t.Label, t.Direction, t.NetProfit, t.ProfitPips, t.ExitReason,
                    t.MarketStateAtEntry, (t.ExitTime - t.EntryTime).TotalHours, t.MaxFavorableExcursion);
            }
        }

        private void PrintMonthlyBreakdown()
        {
            _robot.Print("\n═══ MONTHLY BREAKDOWN ═══");
            var monthly = _completedTrades.GroupBy(t => t.ExitTime.ToString("yyyy-MM")).OrderBy(g => g.Key);
            foreach (var grp in monthly)
            {
                var net = grp.Sum(t => t.NetProfit);
                var wins = grp.Count(t => t.NetProfit > 0);
                var wr = (double)wins / grp.Count() * 100;
                var strategies = grp.GroupBy(t => t.Label)
                    .Select(sg => sg.Key + ":" + sg.Sum(t => t.NetProfit).ToString("F0"))
                    .ToArray();
                _robot.Print("{0}  {1,3}t  {2,3}w ({3:F0}%)  Net: {4,8:F0}  | {5}",
                    grp.Key, grp.Count(), wins, wr, net, string.Join(" ", strategies));
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    public class PositionMeta
    {
        public double LifelinePrice { get; set; }
        public double EntryPrice { get; set; }
        public TradeDirection Direction { get; set; }
        public bool BreakevenMoved { get; set; }
        public int PushCount { get; set; }
        public double LastExtreme { get; set; }
        public bool InPullback { get; set; }
        public string StrategyLabel { get; set; }
    }

    public class TradeManager
    {
        private readonly Robot _robot;
        private readonly RiskManager _riskManager;
        private readonly double _breakevenBufferPips;
        private readonly int _targetPushCount;
        private readonly Dictionary<int, PositionMeta> _positionMetas = new Dictionary<int, PositionMeta>();
        private TradeAnalytics _analytics;

        public TradeManager(Robot robot, RiskManager riskManager, double breakevenBufferPips, int targetPushCount)
        {
            _robot = robot;
            _riskManager = riskManager;
            _breakevenBufferPips = breakevenBufferPips;
            _targetPushCount = targetPushCount;
        }

        public void SetAnalytics(TradeAnalytics analytics) { _analytics = analytics; }

        public void ExecuteTrade(StrategySignal signal, double volume, MarketStateType state, double atr, double slope)
        {
            var tradeType = signal.Direction == TradeDirection.Buy ? TradeType.Buy : TradeType.Sell;
            double emergencyStopPips = _riskManager.GetEmergencyStopPips();
            double takeProfitPips = _riskManager.GetTakeProfitPips();

            var result = _robot.ExecuteMarketOrder(tradeType, _robot.SymbolName, volume, signal.Label, emergencyStopPips, takeProfitPips);

            if (result.IsSuccessful)
            {
                var pos = result.Position;
                _positionMetas[pos.Id] = new PositionMeta
                {
                    LifelinePrice = _riskManager.GetLifelinePrice(pos.EntryPrice, signal.Direction),
                    EntryPrice = pos.EntryPrice,
                    Direction = signal.Direction,
                    BreakevenMoved = false,
                    PushCount = 0,
                    LastExtreme = pos.EntryPrice,
                    InPullback = false,
                    StrategyLabel = signal.Label
                };

                _analytics?.RecordTradeOpen(pos.Id, signal.Label,
                    signal.Direction == TradeDirection.Buy ? "buy" : "sell",
                    pos.EntryPrice, _robot.Server.TimeInUtc, state, atr, slope);

                _robot.Print("[ITFX] OPEN {0} {1} at {2:F2} | SL:{3:F2} TP:{4:F2} | Lifeline:{5:F2} | {6} | State:{7}",
                    signal.Direction, _robot.SymbolName, pos.EntryPrice,
                    pos.EntryPrice + (signal.Direction == TradeDirection.Buy ? -1 : 1) * emergencyStopPips * _robot.Symbol.PipSize,
                    pos.EntryPrice + (signal.Direction == TradeDirection.Buy ? 1 : -1) * takeProfitPips * _robot.Symbol.PipSize,
                    _positionMetas[pos.Id].LifelinePrice, signal.Label, state);
            }
        }

        public void ManageOpenPositions(double barClose, double barHigh, double barLow)
        {
            var closedIds = new List<int>();

            foreach (var pos in _robot.Positions.Where(p => p.SymbolName == _robot.SymbolName).ToList())
            {
                if (!_positionMetas.ContainsKey(pos.Id))
                    continue;

                var meta = _positionMetas[pos.Id];

                _analytics?.UpdateExcursions(pos.Id, barClose);

                if (CheckLifelineExit(pos, meta, barClose))
                {
                    closedIds.Add(pos.Id);
                    continue;
                }

                UpdateBreakeven(pos, meta, barHigh, barLow);
                UpdatePushCount(pos, meta, barHigh, barLow);

                if (meta.PushCount >= _targetPushCount)
                {
                    double profitPips = meta.Direction == TradeDirection.Buy
                        ? (barClose - meta.EntryPrice) / _robot.Symbol.PipSize
                        : (meta.EntryPrice - barClose) / _robot.Symbol.PipSize;
                    if (profitPips >= _riskManager.WinBoxPips)
                    {
                        _robot.Print("[ITFX] CLOSE {0} push-exit | {1} pushes | {2:F0} pips", pos.Id, meta.PushCount, profitPips);
                        RecordAndClose(pos, meta, barClose, ExitReason.PushCount);
                        closedIds.Add(pos.Id);
                        continue;
                    }
                }

                TrailStop(pos, meta, barHigh, barLow);
            }

            foreach (var id in closedIds)
                _positionMetas.Remove(id);
        }

        private bool CheckLifelineExit(Position pos, PositionMeta meta, double barClose)
        {
            bool shouldClose = false;

            if (meta.Direction == TradeDirection.Buy && barClose < meta.LifelinePrice)
                shouldClose = true;
            else if (meta.Direction == TradeDirection.Sell && barClose > meta.LifelinePrice)
                shouldClose = true;

            if (shouldClose)
            {
                _robot.Print("[ITFX] CLOSE {0} lifeline | bar={1:F2} past lifeline={2:F2}", pos.Id, barClose, meta.LifelinePrice);
                RecordAndClose(pos, meta, barClose, ExitReason.Lifeline);
                return true;
            }

            return false;
        }

        private void UpdateBreakeven(Position pos, PositionMeta meta, double barHigh, double barLow)
        {
            if (meta.BreakevenMoved) return;

            double winBoxDistance = _riskManager.GetWinBoxPriceDistance();
            bool newExtreme = false;
            if (meta.Direction == TradeDirection.Buy && barHigh > meta.EntryPrice + winBoxDistance)
                newExtreme = true;
            else if (meta.Direction == TradeDirection.Sell && barLow < meta.EntryPrice - winBoxDistance)
                newExtreme = true;

            if (newExtreme)
            {
                double bufferPrice = _breakevenBufferPips * _robot.Symbol.PipSize;
                double newStop;

                if (meta.Direction == TradeDirection.Buy)
                    newStop = meta.EntryPrice + bufferPrice;
                else
                    newStop = meta.EntryPrice - bufferPrice;

                _robot.ModifyPosition(pos, newStop, pos.TakeProfit, false);
                meta.BreakevenMoved = true;
                _robot.Print("[ITFX] BE set {0} at {1:F2}", pos.Id, newStop);
            }
        }

        private void UpdatePushCount(Position pos, PositionMeta meta, double barHigh, double barLow)
        {
            if (meta.Direction == TradeDirection.Buy)
            {
                if (barHigh > meta.LastExtreme)
                {
                    meta.LastExtreme = barHigh;
                    meta.InPullback = false;
                }
                else if (!meta.InPullback && barLow < meta.LastExtreme)
                {
                    meta.PushCount++;
                    meta.InPullback = true;
                }
            }
            else
            {
                if (barLow < meta.LastExtreme)
                {
                    meta.LastExtreme = barLow;
                    meta.InPullback = false;
                }
                else if (!meta.InPullback && barHigh > meta.LastExtreme)
                {
                    meta.PushCount++;
                    meta.InPullback = true;
                }
            }
        }

        private void TrailStop(Position pos, PositionMeta meta, double barHigh, double barLow)
        {
            if (!meta.BreakevenMoved) return;

            double newStop;
            double trailDistance = _riskManager.GetWinBoxPriceDistance();
            double minGap = _robot.Symbol.PipSize * 10;

            if (meta.Direction == TradeDirection.Buy)
            {
                newStop = meta.LastExtreme - trailDistance;
                if (pos.TakeProfit.HasValue && newStop >= pos.TakeProfit.Value - minGap)
                    return;
                if (pos.StopLoss.HasValue && newStop > pos.StopLoss.Value)
                    _robot.ModifyPosition(pos, newStop, pos.TakeProfit, false);
            }
            else
            {
                newStop = meta.LastExtreme + trailDistance;
                if (pos.TakeProfit.HasValue && newStop <= pos.TakeProfit.Value + minGap)
                    return;
                if (pos.StopLoss.HasValue && newStop < pos.StopLoss.Value)
                    _robot.ModifyPosition(pos, newStop, pos.TakeProfit, false);
            }
        }

        public int GetPositionCountByLabel(string labelPrefix)
        {
            return _robot.Positions.Count(p =>
                p.SymbolName == _robot.SymbolName &&
                p.Label != null &&
                p.Label.StartsWith(labelPrefix));
        }

        public void HandlePositionClosed(Position pos)
        {
            if (!_positionMetas.ContainsKey(pos.Id)) return;
            var meta = _positionMetas[pos.Id];

            double pipSize = _robot.Symbol.PipSize;
            double profitPips = meta.Direction == TradeDirection.Buy
                ? (pos.EntryPrice - pos.EntryPrice + pos.Pips * pipSize) / pipSize
                : pos.Pips;

            ExitReason reason;
            double tpDistance = _riskManager.GetTakeProfitPips() * pipSize;
            double slDistance = _riskManager.GetEmergencyStopPips() * pipSize;
            double closeDistance = Math.Abs(pos.EntryPrice - pos.EntryPrice);

            if (pos.Pips > 0 && Math.Abs(pos.Pips - _riskManager.GetTakeProfitPips()) < 50)
                reason = ExitReason.TakeProfit;
            else if (pos.Pips < 0 && Math.Abs(pos.Pips + _riskManager.GetEmergencyStopPips()) < 50)
                reason = ExitReason.EmergencyStop;
            else if (pos.Pips > 0)
                reason = ExitReason.TrailingStop;
            else
                reason = ExitReason.ServerSL;

            _analytics?.RecordTradeClose(pos.Id, pos.EntryPrice + pos.Pips * pipSize,
                pos.NetProfit, pos.Pips, reason, _robot.Server.TimeInUtc,
                meta.PushCount, meta.BreakevenMoved);

            _robot.Print("[ITFX] CLOSED {0} {1} by {2} | Pips: {3:F0} | Net: {4:F2}",
                pos.Id, meta.StrategyLabel, reason, pos.Pips, pos.NetProfit);

            _positionMetas.Remove(pos.Id);
        }

        public void CleanupClosedPositions()
        {
            var activeIds = new HashSet<int>(_robot.Positions.Select(p => p.Id));
            var staleIds = _positionMetas.Keys.Where(id => !activeIds.Contains(id)).ToList();
            foreach (var id in staleIds)
                _positionMetas.Remove(id);
        }

        public void CloseExpiredPositions(DateTime utcNow, int maxHoldHours)
        {
            foreach (var pos in _robot.Positions.Where(p => p.SymbolName == _robot.SymbolName).ToList())
            {
                if (!_positionMetas.ContainsKey(pos.Id)) continue;
                if ((utcNow - pos.EntryTime).TotalHours >= maxHoldHours)
                {
                    var meta = _positionMetas[pos.Id];
                    _robot.Print("[ITFX] CLOSE {0} max-hold {1}h", pos.Id, maxHoldHours);
                    RecordAndClose(pos, meta, _robot.Symbol.Bid, ExitReason.MaxHoldTime);
                    _positionMetas.Remove(pos.Id);
                }
            }
        }

        public void CloseAllPositions()
        {
            foreach (var pos in _robot.Positions.Where(p => p.SymbolName == _robot.SymbolName).ToList())
            {
                if (_positionMetas.ContainsKey(pos.Id))
                {
                    var meta = _positionMetas[pos.Id];
                    _robot.Print("[ITFX] CLOSE {0} weekend", pos.Id);
                    RecordAndClose(pos, meta, _robot.Symbol.Bid, ExitReason.WeekendClose);
                    _positionMetas.Remove(pos.Id);
                }
                else
                {
                    _robot.ClosePosition(pos);
                }
            }
        }

        public void CheckBreakoutGuard(double barClose, double sma20, double atr, double rangeExtremeAtr)
        {
            double rangeTop = sma20 + rangeExtremeAtr * atr;
            double rangeBottom = sma20 - rangeExtremeAtr * atr;
            double winBoxDistance = _riskManager.GetWinBoxPriceDistance();

            var s5Positions = _robot.Positions
                .Where(p => p.SymbolName == _robot.SymbolName && p.Label == "ITFX_S5")
                .ToList();

            foreach (var pos in s5Positions)
            {
                bool breakout = barClose > rangeTop + winBoxDistance || barClose < rangeBottom - winBoxDistance;
                if (breakout && _positionMetas.ContainsKey(pos.Id))
                {
                    var meta = _positionMetas[pos.Id];
                    _robot.Print("[ITFX] CLOSE {0} breakout-guard", pos.Id);
                    RecordAndClose(pos, meta, barClose, ExitReason.BreakoutGuard);
                    _positionMetas.Remove(pos.Id);
                }
            }
        }

        private void RecordAndClose(Position pos, PositionMeta meta, double closePrice, ExitReason reason)
        {
            double pipSize = _robot.Symbol.PipSize;
            double profitPips = meta.Direction == TradeDirection.Buy
                ? (closePrice - meta.EntryPrice) / pipSize
                : (meta.EntryPrice - closePrice) / pipSize;

            _analytics?.RecordTradeClose(pos.Id, closePrice, pos.NetProfit, profitPips,
                reason, _robot.Server.TimeInUtc, meta.PushCount, meta.BreakevenMoved);

            _robot.ClosePosition(pos);
        }
    }
}

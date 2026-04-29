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

        public TradeManager(Robot robot, RiskManager riskManager, double breakevenBufferPips, int targetPushCount)
        {
            _robot = robot;
            _riskManager = riskManager;
            _breakevenBufferPips = breakevenBufferPips;
            _targetPushCount = targetPushCount;
        }

        public void ExecuteTrade(StrategySignal signal, double volume)
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
                _robot.Print("[ITFX] Opened {0} {1} at {2} | Lifeline: {3} | Label: {4}",
                    signal.Direction, _robot.SymbolName, pos.EntryPrice,
                    _positionMetas[pos.Id].LifelinePrice, signal.Label);
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

                if (CheckLifelineExit(pos, meta, barClose))
                {
                    closedIds.Add(pos.Id);
                    continue;
                }

                UpdateBreakeven(pos, meta, barHigh, barLow);
                UpdatePushCount(pos, meta, barHigh, barLow);

                if (meta.PushCount >= _targetPushCount)
                {
                    _robot.Print("[ITFX] Closing {0} after {1} pushes", pos.Id, meta.PushCount);
                    _robot.ClosePosition(pos);
                    closedIds.Add(pos.Id);
                    continue;
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
                _robot.Print("[ITFX] Lifeline exit: {0} closed at bar close {1} past lifeline {2}",
                    pos.Id, barClose, meta.LifelinePrice);
                _robot.ClosePosition(pos);
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
                _robot.Print("[ITFX] Breakeven set for position {0} at {1}", pos.Id, newStop);
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
                    _robot.Print("[ITFX] Push #{0} detected for position {1}", meta.PushCount, pos.Id);
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
                    _robot.Print("[ITFX] Push #{0} detected for position {1}", meta.PushCount, pos.Id);
                }
            }
        }

        private void TrailStop(Position pos, PositionMeta meta, double barHigh, double barLow)
        {
            if (!meta.BreakevenMoved) return;

            double newStop;
            double trailDistance = _riskManager.GetWinBoxPriceDistance();

            if (meta.Direction == TradeDirection.Buy)
            {
                newStop = meta.LastExtreme - trailDistance;
                if (pos.StopLoss.HasValue && newStop > pos.StopLoss.Value)
                    _robot.ModifyPosition(pos, newStop, pos.TakeProfit, false);
            }
            else
            {
                newStop = meta.LastExtreme + trailDistance;
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

        public void CleanupClosedPositions()
        {
            var activeIds = new HashSet<int>(_robot.Positions.Select(p => p.Id));
            var staleIds = _positionMetas.Keys.Where(id => !activeIds.Contains(id)).ToList();
            foreach (var id in staleIds)
                _positionMetas.Remove(id);
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
                if (breakout)
                {
                    _robot.Print("[ITFX] Breakout guard: closing S5 position {0}", pos.Id);
                    _robot.ClosePosition(pos);
                    if (_positionMetas.ContainsKey(pos.Id))
                        _positionMetas.Remove(pos.Id);
                }
            }
        }
    }
}

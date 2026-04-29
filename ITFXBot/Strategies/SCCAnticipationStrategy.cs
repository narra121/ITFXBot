using System;

namespace cAlgo.Robots
{
    public class SCCAnticipationStrategy : IStrategy
    {
        public string Name => "SCC M8";
        public bool IsEnabled { get; set; }
        public MarketStateType[] ValidStates => new[] { MarketStateType.Trending };

        private readonly double _powerMoveAtr;
        private readonly int _maxPositions;
        private const int SccMemoryBars = 5;

        private bool _sccDetected;
        private TradeDirection _sccDirection;
        private int _barsSinceScc;

        public SCCAnticipationStrategy(double powerMoveAtr, int maxPositions)
        {
            _powerMoveAtr = powerMoveAtr;
            _maxPositions = maxPositions;
        }

        public StrategySignal Evaluate(MarketSnapshot snap)
        {
            if (!snap.DualTimeframeAgrees)
                return StrategySignal.NoSignal();

            if (snap.Strategy2PositionCount >= _maxPositions)
                return StrategySignal.NoSignal();

            double distanceFromSma20 = Math.Abs(snap.Close - snap.Sma20);
            bool isPowerMove = distanceFromSma20 > _powerMoveAtr * snap.Atr;

            if (!isPowerMove)
            {
                ResetScc();
                return StrategySignal.NoSignal();
            }

            bool bullishPower = snap.Close > snap.Sma20 && snap.Close > snap.Sma200;
            bool bearishPower = snap.Close < snap.Sma20 && snap.Close < snap.Sma200;

            bool currentIsSCC = snap.CandleBody > 0.5 * snap.CandleRange;

            if (currentIsSCC)
            {
                if (bullishPower && snap.IsGreenCandle)
                {
                    _sccDetected = true;
                    _sccDirection = TradeDirection.Buy;
                    _barsSinceScc = 0;
                }
                else if (bearishPower && snap.IsRedCandle)
                {
                    _sccDetected = true;
                    _sccDirection = TradeDirection.Sell;
                    _barsSinceScc = 0;
                }
            }

            if (!_sccDetected)
                return StrategySignal.NoSignal();

            _barsSinceScc++;
            if (_barsSinceScc > SccMemoryBars)
            {
                ResetScc();
                return StrategySignal.NoSignal();
            }

            if (_sccDirection == TradeDirection.Buy)
            {
                if (snap.IsRedCandle && snap.Low <= snap.M8High)
                {
                    ResetScc();
                    return StrategySignal.CreateBuy(Name, "ITFX_S2", snap.Close);
                }
            }
            else if (_sccDirection == TradeDirection.Sell)
            {
                if (snap.IsGreenCandle && snap.High >= snap.M8Low)
                {
                    ResetScc();
                    return StrategySignal.CreateSell(Name, "ITFX_S2", snap.Close);
                }
            }

            return StrategySignal.NoSignal();
        }

        private void ResetScc()
        {
            _sccDetected = false;
            _sccDirection = TradeDirection.None;
            _barsSinceScc = 0;
        }
    }
}

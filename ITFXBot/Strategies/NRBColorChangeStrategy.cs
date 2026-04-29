using System;

namespace cAlgo.Robots
{
    public class NRBColorChangeStrategy : IStrategy
    {
        public string Name => "NRB Color";
        public bool IsEnabled { get; set; }
        public MarketStateType[] ValidStates => new[] { MarketStateType.Narrow };

        private readonly double _nrbThresholdPercent;
        private readonly double _nrbMaxDistanceAtr;

        public NRBColorChangeStrategy(double nrbThresholdPercent, double nrbMaxDistanceAtr)
        {
            _nrbThresholdPercent = nrbThresholdPercent;
            _nrbMaxDistanceAtr = nrbMaxDistanceAtr;
        }

        public StrategySignal Evaluate(MarketSnapshot snap)
        {
            if (!snap.DualTimeframeAgrees)
                return StrategySignal.NoSignal();

            bool previousIsNRB = snap.PreviousBody < (_nrbThresholdPercent / 100.0) * snap.Atr;
            if (!previousIsNRB)
                return StrategySignal.NoSignal();

            double nrbDistanceFromSma20 = Math.Abs(
                (snap.PreviousClose + snap.PreviousOpen) / 2.0 - snap.Sma20Previous);
            bool nrbNearSma20 = nrbDistanceFromSma20 < _nrbMaxDistanceAtr * snap.Atr;
            if (!nrbNearSma20)
                return StrategySignal.NoSignal();

            bool bullishTrend = snap.Sma20 > snap.Sma200;
            bool bearishTrend = snap.Sma20 < snap.Sma200;

            if (bullishTrend && snap.PreviousIsRed)
            {
                if (snap.IsGreenCandle && snap.Close > snap.PreviousHigh)
                    return StrategySignal.CreateBuy(Name, "ITFX_S3", snap.Close);
            }

            if (bearishTrend && snap.PreviousIsGreen)
            {
                if (snap.IsRedCandle && snap.Close < snap.PreviousLow)
                    return StrategySignal.CreateSell(Name, "ITFX_S3", snap.Close);
            }

            return StrategySignal.NoSignal();
        }
    }
}

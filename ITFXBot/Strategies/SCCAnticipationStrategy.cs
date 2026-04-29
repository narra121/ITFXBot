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
                return StrategySignal.NoSignal();

            bool bullishPower = snap.Close > snap.Sma20 && snap.Close > snap.Sma200;
            bool bearishPower = snap.Close < snap.Sma20 && snap.Close < snap.Sma200;

            bool previousIsSCC = snap.PreviousBody > 0.5 * (snap.PreviousHigh - snap.PreviousLow);

            if (bullishPower && previousIsSCC && snap.PreviousIsGreen)
            {
                if (snap.IsRedCandle && snap.Low <= snap.M8High)
                    return StrategySignal.CreateBuy(Name, "ITFX_S2", snap.Close);
            }

            if (bearishPower && previousIsSCC && snap.PreviousIsRed)
            {
                if (snap.IsGreenCandle && snap.High >= snap.M8Low)
                    return StrategySignal.CreateSell(Name, "ITFX_S2", snap.Close);
            }

            return StrategySignal.NoSignal();
        }
    }
}

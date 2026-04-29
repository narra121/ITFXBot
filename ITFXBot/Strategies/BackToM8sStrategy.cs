using System;

namespace cAlgo.Robots
{
    public class BackToM8sStrategy : IStrategy
    {
        public string Name => "Back to 20";
        public bool IsEnabled { get; set; }
        public MarketStateType[] ValidStates => new[] { MarketStateType.Ranging };

        private readonly double _rangeExtremeAtr;

        public BackToM8sStrategy(double rangeExtremeAtr)
        {
            _rangeExtremeAtr = rangeExtremeAtr;
        }

        public StrategySignal Evaluate(MarketSnapshot snap)
        {
            double distanceFromSma20 = snap.Close - snap.Sma20;
            double extremeThreshold = _rangeExtremeAtr * snap.Atr;

            if (distanceFromSma20 < -extremeThreshold)
                return StrategySignal.CreateBuy(Name, "ITFX_S5", snap.Close);

            if (distanceFromSma20 > extremeThreshold)
                return StrategySignal.CreateSell(Name, "ITFX_S5", snap.Close);

            return StrategySignal.NoSignal();
        }
    }
}

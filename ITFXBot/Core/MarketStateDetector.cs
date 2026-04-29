using System;

namespace cAlgo.Robots
{
    public class MarketStateDetector
    {
        private readonly double _narrowMultiplier;
        private readonly double _wideMultiplier;
        private readonly double _flatThreshold;

        public MarketStateDetector(double narrowMultiplier, double wideMultiplier, double flatThreshold)
        {
            _narrowMultiplier = narrowMultiplier;
            _wideMultiplier = wideMultiplier;
            _flatThreshold = flatThreshold;
        }

        public MarketStateType Detect(double sma20, double sma200, double atr, double sma20Slope, double m8High, double m8Low)
        {
            if (atr <= 0)
                return MarketStateType.Ranging;

            double distance = Math.Abs(sma20 - sma200);

            if (distance < _narrowMultiplier * atr)
                return MarketStateType.Narrow;

            if (distance > _wideMultiplier * atr)
                return MarketStateType.Wide;

            bool sma20IsFlat = Math.Abs(sma20Slope) < _flatThreshold;
            bool sma20BetweenM8s = sma20 >= m8Low && sma20 <= m8High;

            if (sma20IsFlat && sma20BetweenM8s)
                return MarketStateType.Ranging;

            return MarketStateType.Trending;
        }
    }
}

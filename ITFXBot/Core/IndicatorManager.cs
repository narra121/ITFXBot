using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    public class IndicatorManager
    {
        public SimpleMovingAverage Sma200 { get; private set; }
        public SimpleMovingAverage Sma20 { get; private set; }
        public SimpleMovingAverage M8High { get; private set; }
        public SimpleMovingAverage M8Low { get; private set; }
        public AverageTrueRange Atr { get; private set; }

        public SimpleMovingAverage ConfSma200 { get; private set; }
        public SimpleMovingAverage ConfSma20 { get; private set; }

        private Bars _entryBars;
        private Bars _confBars;

        public void Initialize(Robot robot, Bars entryBars, Bars confBars, int atrPeriod)
        {
            _entryBars = entryBars;
            _confBars = confBars;

            Sma200 = robot.Indicators.SimpleMovingAverage(entryBars.ClosePrices, 200);
            Sma20 = robot.Indicators.SimpleMovingAverage(entryBars.ClosePrices, 20);
            M8High = robot.Indicators.SimpleMovingAverage(entryBars.HighPrices, 8);
            M8Low = robot.Indicators.SimpleMovingAverage(entryBars.LowPrices, 8);
            Atr = robot.Indicators.AverageTrueRange(entryBars, atrPeriod, MovingAverageType.Simple);

            ConfSma200 = robot.Indicators.SimpleMovingAverage(confBars.ClosePrices, 200);
            ConfSma20 = robot.Indicators.SimpleMovingAverage(confBars.ClosePrices, 20);
        }

        public double GetSma20Slope(int lookback)
        {
            double current = Sma20.Result.Last(1);
            double previous = Sma20.Result.Last(1 + lookback);
            if (previous == 0) return 0;
            return (current - previous) / previous * 100;
        }

        public double GetConfSma20Slope(int lookback)
        {
            double current = ConfSma20.Result.Last(1);
            double previous = ConfSma20.Result.Last(1 + lookback);
            if (previous == 0) return 0;
            return (current - previous) / previous * 100;
        }
    }
}

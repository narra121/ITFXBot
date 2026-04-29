using System;
using System.Collections.Generic;

namespace cAlgo.Robots
{
    public class MWPullbackStrategy : IStrategy
    {
        public string Name => "M/W Pullback";
        public bool IsEnabled { get; set; }
        public MarketStateType[] ValidStates => new[] { MarketStateType.Wide };

        private readonly int _swingLookback;
        private readonly double _patternTolerance;

        private readonly List<double> _recentHighs = new List<double>();
        private readonly List<double> _recentLows = new List<double>();

        private enum PatternState { Scanning, WaitingForLCP, WaitingForM8Pullback }
        private PatternState _state = PatternState.Scanning;
        private TradeDirection _patternDirection = TradeDirection.None;
        private double _lcpLevel;
        private int _staleBarCount;

        public MWPullbackStrategy(int swingLookback, double patternTolerance)
        {
            _swingLookback = swingLookback;
            _patternTolerance = patternTolerance;
        }

        public StrategySignal Evaluate(MarketSnapshot snap)
        {
            UpdatePriceHistory(snap);

            if (_state == PatternState.Scanning)
                ScanForPattern(snap);

            if (_state == PatternState.WaitingForLCP)
                CheckForLCP(snap);

            if (_state == PatternState.WaitingForM8Pullback)
                return CheckForM8Entry(snap);

            return StrategySignal.NoSignal();
        }

        private void UpdatePriceHistory(MarketSnapshot snap)
        {
            _recentHighs.Add(snap.High);
            _recentLows.Add(snap.Low);

            int maxHistory = _swingLookback * 10;
            if (_recentHighs.Count > maxHistory)
            {
                _recentHighs.RemoveAt(0);
                _recentLows.RemoveAt(0);
            }
        }

        private void ScanForPattern(MarketSnapshot snap)
        {
            if (_recentHighs.Count < _swingLookback * 3)
                return;

            int count = _recentHighs.Count;

            double peak1 = FindSwingHigh(count - _swingLookback * 3);
            double valley = FindSwingLow(count - _swingLookback * 2);
            double peak2 = FindSwingHigh(count - _swingLookback);

            if (peak1 > 0 && peak2 > 0 && valley > 0)
            {
                double tolerance = peak1 * _patternTolerance / 100.0;
                if (Math.Abs(peak1 - peak2) < tolerance && valley < peak1 && valley < peak2)
                {
                    _state = PatternState.WaitingForLCP;
                    _patternDirection = TradeDirection.Sell;
                    _lcpLevel = valley;
                    return;
                }
            }

            double trough1 = FindSwingLow(count - _swingLookback * 3);
            double peak = FindSwingHigh(count - _swingLookback * 2);
            double trough2 = FindSwingLow(count - _swingLookback);

            if (trough1 > 0 && trough2 > 0 && peak > 0)
            {
                double tolerance = trough1 * _patternTolerance / 100.0;
                if (Math.Abs(trough1 - trough2) < tolerance && peak > trough1 && peak > trough2)
                {
                    _state = PatternState.WaitingForLCP;
                    _patternDirection = TradeDirection.Buy;
                    _lcpLevel = peak;
                    return;
                }
            }
        }

        private void CheckForLCP(MarketSnapshot snap)
        {
            bool lcpConfirmed = false;

            if (_patternDirection == TradeDirection.Buy && snap.Close > _lcpLevel)
                lcpConfirmed = true;
            else if (_patternDirection == TradeDirection.Sell && snap.Close < _lcpLevel)
                lcpConfirmed = true;

            if (lcpConfirmed)
            {
                bool strongCandle = snap.CandleBody > 0.5 * snap.CandleRange;
                if (strongCandle)
                    _state = PatternState.WaitingForM8Pullback;
                else
                    Reset();
            }
        }

        private StrategySignal CheckForM8Entry(MarketSnapshot snap)
        {
            if (_patternDirection == TradeDirection.Buy)
            {
                if (snap.IsRedCandle && snap.Low <= snap.M8High)
                {
                    Reset();
                    return StrategySignal.CreateBuy(Name, "ITFX_S4", snap.Close);
                }
            }
            else if (_patternDirection == TradeDirection.Sell)
            {
                if (snap.IsGreenCandle && snap.High >= snap.M8Low)
                {
                    Reset();
                    return StrategySignal.CreateSell(Name, "ITFX_S4", snap.Close);
                }
            }

            _staleBarCount++;
            if (_staleBarCount > _swingLookback * 2)
                Reset();

            return StrategySignal.NoSignal();
        }

        private void Reset()
        {
            _state = PatternState.Scanning;
            _patternDirection = TradeDirection.None;
            _lcpLevel = 0;
            _staleBarCount = 0;
        }

        private double FindSwingHigh(int centerIndex)
        {
            if (centerIndex < _swingLookback || centerIndex >= _recentHighs.Count - _swingLookback)
                return -1;

            double center = _recentHighs[centerIndex];
            for (int i = centerIndex - _swingLookback; i <= centerIndex + _swingLookback; i++)
            {
                if (i == centerIndex) continue;
                if (_recentHighs[i] >= center) return -1;
            }
            return center;
        }

        private double FindSwingLow(int centerIndex)
        {
            if (centerIndex < _swingLookback || centerIndex >= _recentLows.Count - _swingLookback)
                return -1;

            double center = _recentLows[centerIndex];
            for (int i = centerIndex - _swingLookback; i <= centerIndex + _swingLookback; i++)
            {
                if (i == centerIndex) continue;
                if (_recentLows[i] <= center) return -1;
            }
            return center;
        }
    }
}

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
        private int _lcpWaitCount;

        public MWPullbackStrategy(int swingLookback, double patternTolerance)
        {
            _swingLookback = swingLookback;
            _patternTolerance = patternTolerance;
        }

        public StrategySignal Evaluate(MarketSnapshot snap)
        {
            UpdatePriceHistory(snap);

            if (_state == PatternState.Scanning)
                ScanForPattern();

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

            int maxHistory = _swingLookback * 20;
            if (_recentHighs.Count > maxHistory)
            {
                _recentHighs.RemoveAt(0);
                _recentLows.RemoveAt(0);
            }
        }

        private void ScanForPattern()
        {
            var swingHighs = FindAllSwingHighs();
            var swingLows = FindAllSwingLows();

            if (swingHighs.Count >= 2 && swingLows.Count >= 1)
            {
                var p2 = swingHighs[swingHighs.Count - 1];
                var p1 = swingHighs[swingHighs.Count - 2];
                double valley = FindLowestBetween(p1.Index, p2.Index);

                if (valley > 0)
                {
                    double tolerance = p1.Value * _patternTolerance / 100.0;
                    if (Math.Abs(p1.Value - p2.Value) < tolerance && valley < p1.Value && valley < p2.Value)
                    {
                        _state = PatternState.WaitingForLCP;
                        _patternDirection = TradeDirection.Sell;
                        _lcpLevel = valley;
                        _lcpWaitCount = 0;
                        return;
                    }
                }
            }

            if (swingLows.Count >= 2 && swingHighs.Count >= 1)
            {
                var t2 = swingLows[swingLows.Count - 1];
                var t1 = swingLows[swingLows.Count - 2];
                double peak = FindHighestBetween(t1.Index, t2.Index);

                if (peak > 0)
                {
                    double tolerance = t1.Value * _patternTolerance / 100.0;
                    if (Math.Abs(t1.Value - t2.Value) < tolerance && peak > t1.Value && peak > t2.Value)
                    {
                        _state = PatternState.WaitingForLCP;
                        _patternDirection = TradeDirection.Buy;
                        _lcpLevel = peak;
                        _lcpWaitCount = 0;
                        return;
                    }
                }
            }
        }

        private void CheckForLCP(MarketSnapshot snap)
        {
            _lcpWaitCount++;
            if (_lcpWaitCount > _swingLookback * 3)
            {
                Reset();
                return;
            }

            bool lcpConfirmed = false;
            if (_patternDirection == TradeDirection.Buy && snap.Close > _lcpLevel)
                lcpConfirmed = true;
            else if (_patternDirection == TradeDirection.Sell && snap.Close < _lcpLevel)
                lcpConfirmed = true;

            if (lcpConfirmed)
            {
                bool strongCandle = snap.CandleBody > 0.5 * snap.CandleRange;
                if (strongCandle)
                {
                    _state = PatternState.WaitingForM8Pullback;
                    _staleBarCount = 0;
                }
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
            _lcpWaitCount = 0;
        }

        private struct SwingPoint
        {
            public int Index;
            public double Value;
        }

        private List<SwingPoint> FindAllSwingHighs()
        {
            var result = new List<SwingPoint>();
            int count = _recentHighs.Count;
            for (int i = _swingLookback; i < count - _swingLookback; i++)
            {
                double center = _recentHighs[i];
                bool isSwing = true;
                for (int j = i - _swingLookback; j <= i + _swingLookback; j++)
                {
                    if (j == i) continue;
                    if (_recentHighs[j] >= center) { isSwing = false; break; }
                }
                if (isSwing)
                    result.Add(new SwingPoint { Index = i, Value = center });
            }
            return result;
        }

        private List<SwingPoint> FindAllSwingLows()
        {
            var result = new List<SwingPoint>();
            int count = _recentLows.Count;
            for (int i = _swingLookback; i < count - _swingLookback; i++)
            {
                double center = _recentLows[i];
                bool isSwing = true;
                for (int j = i - _swingLookback; j <= i + _swingLookback; j++)
                {
                    if (j == i) continue;
                    if (_recentLows[j] <= center) { isSwing = false; break; }
                }
                if (isSwing)
                    result.Add(new SwingPoint { Index = i, Value = center });
            }
            return result;
        }

        private double FindLowestBetween(int startIdx, int endIdx)
        {
            if (startIdx >= endIdx || startIdx < 0 || endIdx >= _recentLows.Count)
                return -1;
            double lowest = double.MaxValue;
            for (int i = startIdx; i <= endIdx; i++)
                lowest = Math.Min(lowest, _recentLows[i]);
            return lowest < double.MaxValue ? lowest : -1;
        }

        private double FindHighestBetween(int startIdx, int endIdx)
        {
            if (startIdx >= endIdx || startIdx < 0 || endIdx >= _recentHighs.Count)
                return -1;
            double highest = double.MinValue;
            for (int i = startIdx; i <= endIdx; i++)
                highest = Math.Max(highest, _recentHighs[i]);
            return highest > double.MinValue ? highest : -1;
        }
    }
}

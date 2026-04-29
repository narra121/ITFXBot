using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(AccessRights = AccessRights.None)]
    public class ITFXBot : Robot
    {
        [Parameter("Entry Timeframe", Group = "General", DefaultValue = "Minute15")]
        public TimeFrame EntryTimeframe { get; set; }

        [Parameter("Confirmation Timeframe", Group = "General", DefaultValue = "Minute30")]
        public TimeFrame ConfirmationTimeframe { get; set; }

        [Parameter("Slope Lookback", Group = "General", DefaultValue = 5, MinValue = 1)]
        public int SlopeLookback { get; set; }

        [Parameter("Flat Threshold %", Group = "General", DefaultValue = 0.05)]
        public double FlatThreshold { get; set; }

        [Parameter("ATR Period", Group = "Market State", DefaultValue = 14, MinValue = 1)]
        public int ATRPeriod { get; set; }

        [Parameter("Narrow Multiplier", Group = "Market State", DefaultValue = 1.0)]
        public double NarrowMultiplier { get; set; }

        [Parameter("Wide Multiplier", Group = "Market State", DefaultValue = 2.0)]
        public double WideMultiplier { get; set; }

        [Parameter("Enable 20 Touch", Group = "Strategies", DefaultValue = true)]
        public bool EnableTwentyTouch { get; set; }

        [Parameter("Enable SCC Anticipation", Group = "Strategies", DefaultValue = true)]
        public bool EnableSCCAnticipation { get; set; }

        [Parameter("Enable NRB Color Change", Group = "Strategies", DefaultValue = true)]
        public bool EnableNRBColorChange { get; set; }

        [Parameter("Enable M/W Pullback", Group = "Strategies", DefaultValue = false)]
        public bool EnableMWPullback { get; set; }

        [Parameter("Enable Back to M8s", Group = "Strategies", DefaultValue = false)]
        public bool EnableBackToM8s { get; set; }

        [Parameter("Sizing Mode", Group = "Risk", DefaultValue = SizingMode.RiskPercent)]
        public SizingMode SizingModeParam { get; set; }

        [Parameter("Fixed Lot Size", Group = "Risk", DefaultValue = 0.01, MinValue = 0.01)]
        public double FixedLotSize { get; set; }

        [Parameter("Risk Percentage", Group = "Risk", DefaultValue = 2.0, MinValue = 0.1)]
        public double RiskPercentage { get; set; }

        [Parameter("Custom WinBox Pips (0=auto)", Group = "Risk", DefaultValue = 0)]
        public double CustomWinBoxPips { get; set; }

        [Parameter("Emergency Stop Multiplier", Group = "Risk", DefaultValue = 2.0, MinValue = 1.0)]
        public double EmergencyStopMultiplier { get; set; }

        [Parameter("Breakeven Buffer Pips", Group = "Risk", DefaultValue = 2.0)]
        public double BreakevenBufferPips { get; set; }

        [Parameter("Target Push Count", Group = "Risk", DefaultValue = 3, MinValue = 1)]
        public int TargetPushCount { get; set; }

        [Parameter("Max Open Positions (S2)", Group = "Risk", DefaultValue = 3, MinValue = 1)]
        public int MaxOpenPositions { get; set; }

        [Parameter("NRB Threshold %", Group = "Strategy Settings", DefaultValue = 25.0)]
        public double NRBThresholdPercent { get; set; }

        [Parameter("NRB Max Distance ATR", Group = "Strategy Settings", DefaultValue = 1.0)]
        public double NRBMaxDistanceATR { get; set; }

        [Parameter("Swing Lookback", Group = "Strategy Settings", DefaultValue = 10, MinValue = 3)]
        public int SwingLookback { get; set; }

        [Parameter("Pattern Tolerance %", Group = "Strategy Settings", DefaultValue = 0.5)]
        public double PatternTolerance { get; set; }

        [Parameter("Range Extreme ATR", Group = "Strategy Settings", DefaultValue = 1.5)]
        public double RangeExtremeATR { get; set; }

        [Parameter("Power Move ATR", Group = "Strategy Settings", DefaultValue = 2.0)]
        public double PowerMoveATR { get; set; }

        [Parameter("Trading Start Hour (UTC)", Group = "Session", DefaultValue = 1)]
        public int TradingStartHour { get; set; }

        [Parameter("Trading End Hour (UTC)", Group = "Session", DefaultValue = 21)]
        public int TradingEndHour { get; set; }

        [Parameter("Close Before Weekend", Group = "Session", DefaultValue = true)]
        public bool CloseBeforeWeekend { get; set; }

        [Parameter("Friday Close Hour (UTC)", Group = "Session", DefaultValue = 20)]
        public int FridayCloseHour { get; set; }

        [Parameter("Max Hold Hours (0=unlimited)", Group = "Session", DefaultValue = 24, MinValue = 0)]
        public int MaxHoldHours { get; set; }

        private IndicatorManager _indicators;
        private MarketStateDetector _stateDetector;
        private RiskManager _riskManager;
        private TradeManager _tradeManager;
        private TradeAnalytics _analytics;
        private List<IStrategy> _strategies;
        private Bars _entryBars;
        private Bars _confBars;

        protected override void OnStart()
        {
            _entryBars = MarketData.GetBars(EntryTimeframe);
            _confBars = MarketData.GetBars(ConfirmationTimeframe);

            _indicators = new IndicatorManager();
            _indicators.Initialize(this, _entryBars, _confBars, ATRPeriod);

            _stateDetector = new MarketStateDetector(NarrowMultiplier, WideMultiplier, FlatThreshold);

            _riskManager = new RiskManager(Symbol, SizingModeParam, FixedLotSize,
                RiskPercentage, CustomWinBoxPips, EmergencyStopMultiplier);

            _analytics = new TradeAnalytics(this);

            _tradeManager = new TradeManager(this, _riskManager, BreakevenBufferPips, TargetPushCount);
            _tradeManager.SetAnalytics(_analytics);

            _strategies = new List<IStrategy>();

            var s4 = new MWPullbackStrategy(SwingLookback, PatternTolerance) { IsEnabled = EnableMWPullback };
            var s1 = new TwentyTouchStrategy() { IsEnabled = EnableTwentyTouch };
            var s3 = new NRBColorChangeStrategy(NRBThresholdPercent, NRBMaxDistanceATR) { IsEnabled = EnableNRBColorChange };
            var s2 = new SCCAnticipationStrategy(PowerMoveATR, MaxOpenPositions) { IsEnabled = EnableSCCAnticipation };
            var s5 = new BackToM8sStrategy(RangeExtremeATR) { IsEnabled = EnableBackToM8s };

            _strategies.Add(s4);
            _strategies.Add(s1);
            _strategies.Add(s3);
            _strategies.Add(s2);
            _strategies.Add(s5);

            _entryBars.BarOpened += OnEntryBarOpened;

            Print("[ITFX] Bot started | WinBox: {0} pips | Session: {1}-{2} UTC | MaxHold: {3}h",
                _riskManager.WinBoxPips, TradingStartHour, TradingEndHour, MaxHoldHours);
            Print("[ITFX] Strategies: {0}",
                string.Join(", ", _strategies.Where(s => s.IsEnabled).Select(s => s.Name)));
        }

        private bool IsWithinTradingSession()
        {
            int hour = Server.TimeInUtc.Hour;
            int dow = (int)Server.TimeInUtc.DayOfWeek;

            if (dow == 0 || dow == 6)
                return false;

            if (CloseBeforeWeekend && dow == 5 && hour >= FridayCloseHour)
                return false;

            if (TradingStartHour < TradingEndHour)
                return hour >= TradingStartHour && hour < TradingEndHour;

            return hour >= TradingStartHour || hour < TradingEndHour;
        }

        private void OnEntryBarOpened(BarOpenedEventArgs args)
        {
            if (_entryBars.Count < 201 || _confBars.Count < 201)
                return;

            _tradeManager.CleanupClosedPositions();

            var snap = BuildSnapshot();
            _analytics.RecordBarState(snap.MarketState);

            _tradeManager.ManageOpenPositions(snap.PreviousClose, snap.PreviousHigh, snap.PreviousLow);

            if (MaxHoldHours > 0)
                _tradeManager.CloseExpiredPositions(Server.TimeInUtc, MaxHoldHours);

            if (CloseBeforeWeekend)
            {
                int dow = (int)Server.TimeInUtc.DayOfWeek;
                int hour = Server.TimeInUtc.Hour;
                if (dow == 5 && hour >= FridayCloseHour)
                    _tradeManager.CloseAllPositions();
            }

            if (EnableBackToM8s)
            {
                _tradeManager.CheckBreakoutGuard(snap.PreviousClose, snap.Sma20, snap.Atr, RangeExtremeATR);
            }

            if (!IsWithinTradingSession())
            {
                _analytics.RecordSignalSkipped("session");
                return;
            }

            bool enteredThisBar = false;

            foreach (var strategy in _strategies)
            {
                if (!strategy.IsEnabled) continue;
                if (enteredThisBar) break;

                bool validState = false;
                foreach (var state in strategy.ValidStates)
                {
                    if (state == snap.MarketState)
                    {
                        validState = true;
                        break;
                    }
                }
                if (!validState)
                {
                    _analytics.RecordSignalSkipped("state");
                    continue;
                }

                var signal = strategy.Evaluate(snap);
                if (!signal.HasSignal) continue;

                _analytics.RecordSignalGenerated();

                if (strategy.Name == "SCC M8")
                {
                }
                else
                {
                    int existingForStrategy = _tradeManager.GetPositionCountByLabel(signal.Label);
                    if (existingForStrategy > 0)
                    {
                        _analytics.RecordSignalSkipped("existing");
                        continue;
                    }
                }

                double volume = _riskManager.CalculateVolumeInUnits(Account.Balance);

                Print("[ITFX] SIGNAL: {0} {1} from {2} | State: {3} | ATR: {4:F2}",
                    signal.Direction, SymbolName, signal.StrategyName, snap.MarketState, snap.Atr);
                _tradeManager.ExecuteTrade(signal, volume, snap.MarketState, snap.Atr, snap.Sma20Slope);
                enteredThisBar = true;
            }
        }

        private MarketSnapshot BuildSnapshot()
        {
            double sma20Slope = _indicators.GetSma20Slope(SlopeLookback);
            double confSma20Slope = _indicators.GetConfSma20Slope(SlopeLookback);

            double sma200 = _indicators.Sma200.Result.Last(1);
            double sma20 = _indicators.Sma20.Result.Last(1);
            double sma20Prev = _indicators.Sma20.Result.Last(2);
            double m8High = _indicators.M8High.Result.Last(1);
            double m8Low = _indicators.M8Low.Result.Last(1);
            double atr = _indicators.Atr.Result.Last(1);

            double confSma200 = _indicators.ConfSma200.Result.Last(1);
            double confSma20 = _indicators.ConfSma20.Result.Last(1);

            var marketState = _stateDetector.Detect(sma20, sma200, atr, sma20Slope, m8High, m8Low);

            bool entryAboveSma200 = _entryBars.ClosePrices.Last(1) > sma200;
            bool confAboveSma200 = _confBars.ClosePrices.Last(1) > confSma200;
            bool priceAgrees = (entryAboveSma200 && confAboveSma200) ||
                               (!entryAboveSma200 && !confAboveSma200);

            bool slopeAgrees = (sma20Slope > 0 && confSma20Slope > 0) ||
                               (sma20Slope < 0 && confSma20Slope < 0);

            bool isBullishEntry = entryAboveSma200 && sma20Slope > 0;
            bool isBullishConf = confAboveSma200 && confSma20Slope > 0;

            bool dualAgrees = priceAgrees && slopeAgrees;

            int s2Count = _tradeManager.GetPositionCountByLabel("ITFX_S2");

            return new MarketSnapshot
            {
                Open = _entryBars.OpenPrices.Last(1),
                High = _entryBars.HighPrices.Last(1),
                Low = _entryBars.LowPrices.Last(1),
                Close = _entryBars.ClosePrices.Last(1),
                PreviousOpen = _entryBars.OpenPrices.Last(2),
                PreviousHigh = _entryBars.HighPrices.Last(2),
                PreviousLow = _entryBars.LowPrices.Last(2),
                PreviousClose = _entryBars.ClosePrices.Last(2),
                Sma200 = sma200,
                Sma20 = sma20,
                Sma20Previous = sma20Prev,
                M8High = m8High,
                M8Low = m8Low,
                Atr = atr,
                Sma20Slope = sma20Slope,
                ConfSma200 = confSma200,
                ConfSma20 = confSma20,
                ConfSma20Slope = confSma20Slope,
                MarketState = marketState,
                IsBullishEntry = isBullishEntry,
                IsBullishConfirmation = isBullishConf,
                DualTimeframeAgrees = dualAgrees,
                Bid = Symbol.Bid,
                Ask = Symbol.Ask,
                OpenPositionCount = Positions.Count(p => p.SymbolName == SymbolName),
                Strategy2PositionCount = s2Count
            };
        }

        protected override void OnStop()
        {
            _entryBars.BarOpened -= OnEntryBarOpened;
            _analytics.PrintFullReport();
            Print("[ITFX] Bot stopped");
        }
    }
}

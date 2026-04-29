# ITFX Daily Money cTrader cBot — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a modular cTrader cBot (C#) implementing 5 ITFX trading strategies with ATR-based market state detection, WinBox/Lifeline risk management, dual-timeframe confirmation, and configurable parameters.

**Architecture:** Strategy Pattern with a main Robot orchestrator, shared Core services (indicators, market state, risk, trade management), pluggable Strategy classes behind an IStrategy interface, and lightweight Models. All files compile together in a single cTrader cBot project.

**Tech Stack:** C# / cAlgo.API (cTrader Automate), .NET Framework, cTrader Backtester for testing.

**Testing Note:** cTrader cBots run inside the cTrader runtime and cannot be unit-tested with standard frameworks (xUnit/NUnit) because the cAlgo API has no public mocking surface. Each task is verified by: (1) successful compilation in cTrader, (2) Print()-based logging to confirm logic, and (3) backtester runs after integration. Strategy logic is kept in pure methods where possible to maximize future testability.

---

## File Structure

```
e:/Test/ITFXBot/
├── ITFXBot.cs                          — Main Robot class, parameters, OnBar orchestration
├── Models/
│   ├── Enums.cs                        — MarketStateType, TradeDirection, SizingMode enums
│   ├── StrategySignal.cs               — Signal returned by strategies
│   ├── MarketSnapshot.cs               — Per-bar data passed to strategies
│   └── WinBoxConfig.cs                 — Per-symbol WinBox pip mappings
├── Core/
│   ├── IndicatorManager.cs             — Dual-timeframe SMA + ATR initialization and access
│   ├── MarketStateDetector.cs          — ATR-based Narrow/Wide/Ranging/Trending detection
│   ├── RiskManager.cs                  — Position sizing, WinBox lookups
│   └── TradeManager.cs                 — Execution, Lifeline checks, breakeven, trailing, push counting
├── Strategies/
│   ├── IStrategy.cs                    — Strategy interface
│   ├── TwentyTouchStrategy.cs          — Strategy 1: 20 Touch Entry
│   ├── SCCAnticipationStrategy.cs      — Strategy 2: SCC to Anticipation M8
│   ├── NRBColorChangeStrategy.cs       — Strategy 3: NRB Color Change
│   ├── MWPullbackStrategy.cs           — Strategy 4: M/W M8 Pullback to LCP
│   └── BackToM8sStrategy.cs            — Strategy 5: Back to M8s to the 20
```

---

### Task 1: Project Setup + Enums

**Files:**
- Create: `e:/Test/ITFXBot/Models/Enums.cs`

- [ ] **Step 1: Create project directory structure**

```bash
mkdir -p e:/Test/ITFXBot/Models e:/Test/ITFXBot/Core e:/Test/ITFXBot/Strategies
```

- [ ] **Step 2: Create Enums.cs**

```csharp
namespace cAlgo.Robots
{
    public enum MarketStateType
    {
        Narrow,
        Wide,
        Ranging,
        Trending
    }

    public enum TradeDirection
    {
        Buy,
        Sell,
        None
    }

    public enum SizingMode
    {
        FixedLot,
        RiskPercent
    }
}
```

- [ ] **Step 3: Initialize git and commit**

```bash
cd e:/Test
git init
git add ITFXBot/Models/Enums.cs
git commit -m "feat: add ITFX enums (MarketStateType, TradeDirection, SizingMode)"
```

---

### Task 2: StrategySignal Model

**Files:**
- Create: `e:/Test/ITFXBot/Models/StrategySignal.cs`

- [ ] **Step 1: Create StrategySignal.cs**

```csharp
namespace cAlgo.Robots
{
    public class StrategySignal
    {
        public bool HasSignal { get; set; }
        public TradeDirection Direction { get; set; }
        public string StrategyName { get; set; }
        public string Label { get; set; }
        public double EntryPrice { get; set; }

        public static StrategySignal NoSignal()
        {
            return new StrategySignal { HasSignal = false, Direction = TradeDirection.None };
        }

        public static StrategySignal CreateBuy(string strategyName, string label, double entryPrice)
        {
            return new StrategySignal
            {
                HasSignal = true,
                Direction = TradeDirection.Buy,
                StrategyName = strategyName,
                Label = label,
                EntryPrice = entryPrice
            };
        }

        public static StrategySignal CreateSell(string strategyName, string label, double entryPrice)
        {
            return new StrategySignal
            {
                HasSignal = true,
                Direction = TradeDirection.Sell,
                StrategyName = strategyName,
                Label = label,
                EntryPrice = entryPrice
            };
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add ITFXBot/Models/StrategySignal.cs
git commit -m "feat: add StrategySignal model with factory methods"
```

---

### Task 3: WinBoxConfig

**Files:**
- Create: `e:/Test/ITFXBot/Models/WinBoxConfig.cs`

- [ ] **Step 1: Create WinBoxConfig.cs**

```csharp
using System.Collections.Generic;

namespace cAlgo.Robots
{
    public static class WinBoxConfig
    {
        private static readonly Dictionary<string, double> DefaultWinBoxPips = new Dictionary<string, double>
        {
            { "Step Index", 125 },
            { "Step Index 200", 125 },
            { "US 30", 12350 },
            { "US30", 12350 },
            { "Volatility 100 Index", 12130 },
            { "Volatility 100", 12130 },
            { "XAUUSD", 1240 },
            { "Gold", 1240 },
            { "EURAUD", 1020 },
            { "EURJPY", 1020 }
        };

        public static double GetWinBoxPips(string symbolName, double customWinBoxPips)
        {
            if (customWinBoxPips > 0)
                return customWinBoxPips;

            foreach (var kvp in DefaultWinBoxPips)
            {
                if (symbolName.Contains(kvp.Key) || kvp.Key.Contains(symbolName))
                    return kvp.Value;
            }

            return 100;
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add ITFXBot/Models/WinBoxConfig.cs
git commit -m "feat: add WinBoxConfig with per-symbol pip defaults"
```

---

### Task 4: MarketSnapshot

**Files:**
- Create: `e:/Test/ITFXBot/Models/MarketSnapshot.cs`

- [ ] **Step 1: Create MarketSnapshot.cs**

```csharp
using cAlgo.API;

namespace cAlgo.Robots
{
    public class MarketSnapshot
    {
        // Entry timeframe bar data
        public double Open { get; set; }
        public double High { get; set; }
        public double Low { get; set; }
        public double Close { get; set; }
        public double PreviousClose { get; set; }
        public double PreviousOpen { get; set; }
        public double PreviousHigh { get; set; }
        public double PreviousLow { get; set; }

        // Entry timeframe indicators
        public double Sma200 { get; set; }
        public double Sma20 { get; set; }
        public double Sma20Previous { get; set; }
        public double M8High { get; set; }
        public double M8Low { get; set; }
        public double Atr { get; set; }
        public double Sma20Slope { get; set; }

        // Confirmation timeframe indicators
        public double ConfSma200 { get; set; }
        public double ConfSma20 { get; set; }
        public double ConfSma20Slope { get; set; }

        // Market state
        public MarketStateType MarketState { get; set; }

        // Trend direction helpers
        public bool IsBullishEntry { get; set; }
        public bool IsBullishConfirmation { get; set; }
        public bool DualTimeframeAgrees { get; set; }

        // Current price
        public double Bid { get; set; }
        public double Ask { get; set; }

        // Position info
        public int OpenPositionCount { get; set; }
        public int Strategy2PositionCount { get; set; }

        // Candle properties
        public bool IsGreenCandle => Close > Open;
        public bool IsRedCandle => Close < Open;
        public double CandleBody => System.Math.Abs(Close - Open);
        public double CandleRange => High - Low;
        public bool PreviousIsGreen => PreviousClose > PreviousOpen;
        public bool PreviousIsRed => PreviousClose < PreviousOpen;
        public double PreviousBody => System.Math.Abs(PreviousClose - PreviousOpen);
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add ITFXBot/Models/MarketSnapshot.cs
git commit -m "feat: add MarketSnapshot data class for per-bar state"
```

---

### Task 5: IStrategy Interface

**Files:**
- Create: `e:/Test/ITFXBot/Strategies/IStrategy.cs`

- [ ] **Step 1: Create IStrategy.cs**

```csharp
namespace cAlgo.Robots
{
    public interface IStrategy
    {
        string Name { get; }
        bool IsEnabled { get; set; }
        MarketStateType[] ValidStates { get; }
        StrategySignal Evaluate(MarketSnapshot snapshot);
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add ITFXBot/Strategies/IStrategy.cs
git commit -m "feat: add IStrategy interface"
```

---

### Task 6: IndicatorManager

**Files:**
- Create: `e:/Test/ITFXBot/Core/IndicatorManager.cs`

- [ ] **Step 1: Create IndicatorManager.cs**

```csharp
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
            double current = Sma20.Result.Last(0);
            double previous = Sma20.Result.Last(lookback);
            if (previous == 0) return 0;
            return (current - previous) / previous * 100;
        }

        public double GetConfSma20Slope(int lookback)
        {
            double current = ConfSma20.Result.Last(0);
            double previous = ConfSma20.Result.Last(lookback);
            if (previous == 0) return 0;
            return (current - previous) / previous * 100;
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add ITFXBot/Core/IndicatorManager.cs
git commit -m "feat: add IndicatorManager with dual-timeframe SMA and ATR"
```

---

### Task 7: MarketStateDetector

**Files:**
- Create: `e:/Test/ITFXBot/Core/MarketStateDetector.cs`

- [ ] **Step 1: Create MarketStateDetector.cs**

```csharp
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
```

- [ ] **Step 2: Commit**

```bash
git add ITFXBot/Core/MarketStateDetector.cs
git commit -m "feat: add MarketStateDetector with ATR-based state logic"
```

---

### Task 8: RiskManager

**Files:**
- Create: `e:/Test/ITFXBot/Core/RiskManager.cs`

- [ ] **Step 1: Create RiskManager.cs**

```csharp
using System;
using cAlgo.API;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    public class RiskManager
    {
        private readonly Symbol _symbol;
        private readonly SizingMode _sizingMode;
        private readonly double _fixedLotSize;
        private readonly double _riskPercentage;
        private readonly double _customWinBoxPips;
        private readonly double _emergencyStopMultiplier;

        public double WinBoxPips { get; private set; }

        public RiskManager(Symbol symbol, SizingMode sizingMode, double fixedLotSize,
            double riskPercentage, double customWinBoxPips, double emergencyStopMultiplier)
        {
            _symbol = symbol;
            _sizingMode = sizingMode;
            _fixedLotSize = fixedLotSize;
            _riskPercentage = riskPercentage;
            _customWinBoxPips = customWinBoxPips;
            _emergencyStopMultiplier = emergencyStopMultiplier;

            WinBoxPips = WinBoxConfig.GetWinBoxPips(symbol.Name, customWinBoxPips);
        }

        public double GetWinBoxPriceDistance()
        {
            return WinBoxPips * _symbol.PipSize;
        }

        public double GetEmergencyStopPips()
        {
            return WinBoxPips * _emergencyStopMultiplier;
        }

        public double GetLifelinePrice(double entryPrice, TradeDirection direction)
        {
            double distance = GetWinBoxPriceDistance();
            return direction == TradeDirection.Buy
                ? entryPrice - distance
                : entryPrice + distance;
        }

        public double CalculateVolumeInUnits(double accountBalance)
        {
            double volume;

            if (_sizingMode == SizingMode.FixedLot)
            {
                volume = _symbol.QuantityToVolumeInUnits(_fixedLotSize);
            }
            else
            {
                double riskAmount = accountBalance * _riskPercentage / 100.0;
                double pipValue = _symbol.PipValue;
                if (pipValue <= 0 || WinBoxPips <= 0)
                    volume = _symbol.VolumeInUnitsMin;
                else
                    volume = riskAmount / (WinBoxPips * pipValue);
            }

            volume = _symbol.NormalizeVolumeInUnits(volume, RoundingMode.Down);
            return Math.Max(volume, _symbol.VolumeInUnitsMin);
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add ITFXBot/Core/RiskManager.cs
git commit -m "feat: add RiskManager with WinBox, Lifeline, and position sizing"
```

---

### Task 9: TradeManager

**Files:**
- Create: `e:/Test/ITFXBot/Core/TradeManager.cs`

- [ ] **Step 1: Create TradeManager.cs with position tracking structure**

```csharp
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
        private readonly Dictionary<long, PositionMeta> _positionMetas = new Dictionary<long, PositionMeta>();

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

            var result = _robot.ExecuteMarketOrder(tradeType, _robot.SymbolName, volume, signal.Label, emergencyStopPips, null);

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
            var closedIds = new List<long>();

            foreach (var pos in _robot.Positions.Where(p => p.SymbolName == _robot.SymbolName))
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

            bool newExtreme = false;
            if (meta.Direction == TradeDirection.Buy && barHigh > meta.EntryPrice)
                newExtreme = true;
            else if (meta.Direction == TradeDirection.Sell && barLow < meta.EntryPrice)
                newExtreme = true;

            if (newExtreme)
            {
                double bufferPrice = _breakevenBufferPips * _robot.Symbol.PipSize;
                double newStop;

                if (meta.Direction == TradeDirection.Buy)
                    newStop = meta.EntryPrice + bufferPrice;
                else
                    newStop = meta.EntryPrice - bufferPrice;

                _robot.ModifyPosition(pos, newStop, pos.TakeProfit);
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
                else if (barLow < meta.LastExtreme && !meta.InPullback)
                {
                    meta.PushCount++;
                    meta.InPullback = true;
                    _robot.Print("[ITFX] Push #{0} detected for position {1}", meta.PushCount, pos.Id);
                }
                else if (barHigh > meta.LastExtreme)
                {
                    meta.InPullback = false;
                }
            }
            else
            {
                if (barLow < meta.LastExtreme)
                {
                    meta.LastExtreme = barLow;
                    meta.InPullback = false;
                }
                else if (barHigh > meta.LastExtreme && !meta.InPullback)
                {
                    meta.PushCount++;
                    meta.InPullback = true;
                    _robot.Print("[ITFX] Push #{0} detected for position {1}", meta.PushCount, pos.Id);
                }
                else if (barLow < meta.LastExtreme)
                {
                    meta.InPullback = false;
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
                    _robot.ModifyPosition(pos, newStop, pos.TakeProfit);
            }
            else
            {
                newStop = meta.LastExtreme + trailDistance;
                if (pos.StopLoss.HasValue && newStop < pos.StopLoss.Value)
                    _robot.ModifyPosition(pos, newStop, pos.TakeProfit);
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
            var activeIds = new HashSet<long>(_robot.Positions.Select(p => p.Id));
            var staleIds = _positionMetas.Keys.Where(id => !activeIds.Contains(id)).ToList();
            foreach (var id in staleIds)
                _positionMetas.Remove(id);
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add ITFXBot/Core/TradeManager.cs
git commit -m "feat: add TradeManager with Lifeline, breakeven, trailing, push counting"
```

---

### Task 10: Strategy 1 — TwentyTouchStrategy

**Files:**
- Create: `e:/Test/ITFXBot/Strategies/TwentyTouchStrategy.cs`

- [ ] **Step 1: Create TwentyTouchStrategy.cs**

```csharp
namespace cAlgo.Robots
{
    public class TwentyTouchStrategy : IStrategy
    {
        public string Name => "20 Touch";
        public bool IsEnabled { get; set; }
        public MarketStateType[] ValidStates => new[] { MarketStateType.Trending };

        public StrategySignal Evaluate(MarketSnapshot snap)
        {
            if (!snap.DualTimeframeAgrees)
                return StrategySignal.NoSignal();

            bool isBullish = snap.Close > snap.Sma200 && snap.Sma20 > snap.Sma200;
            bool isBearish = snap.Close < snap.Sma200 && snap.Sma20 < snap.Sma200;

            if (isBullish && snap.Low <= snap.Sma20 && snap.Close > snap.Sma20)
            {
                return StrategySignal.CreateBuy(Name, "ITFX_S1", snap.Close);
            }

            if (isBearish && snap.High >= snap.Sma20 && snap.Close < snap.Sma20)
            {
                return StrategySignal.CreateSell(Name, "ITFX_S1", snap.Close);
            }

            return StrategySignal.NoSignal();
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add ITFXBot/Strategies/TwentyTouchStrategy.cs
git commit -m "feat: add Strategy 1 - TwentyTouchStrategy (20 SMA touch entry)"
```

---

### Task 11: Strategy 2 — SCCAnticipationStrategy

**Files:**
- Create: `e:/Test/ITFXBot/Strategies/SCCAnticipationStrategy.cs`

- [ ] **Step 1: Create SCCAnticipationStrategy.cs**

```csharp
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
                {
                    return StrategySignal.CreateBuy(Name, "ITFX_S2", snap.Close);
                }
            }

            if (bearishPower && previousIsSCC && snap.PreviousIsRed)
            {
                if (snap.IsGreenCandle && snap.High >= snap.M8Low)
                {
                    return StrategySignal.CreateSell(Name, "ITFX_S2", snap.Close);
                }
            }

            return StrategySignal.NoSignal();
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add ITFXBot/Strategies/SCCAnticipationStrategy.cs
git commit -m "feat: add Strategy 2 - SCCAnticipationStrategy (power move M8 entry)"
```

---

### Task 12: Strategy 3 — NRBColorChangeStrategy

**Files:**
- Create: `e:/Test/ITFXBot/Strategies/NRBColorChangeStrategy.cs`

- [ ] **Step 1: Create NRBColorChangeStrategy.cs**

```csharp
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
                {
                    return StrategySignal.CreateBuy(Name, "ITFX_S3", snap.Close);
                }
            }

            if (bearishTrend && snap.PreviousIsGreen)
            {
                if (snap.IsRedCandle && snap.Close < snap.PreviousLow)
                {
                    return StrategySignal.CreateSell(Name, "ITFX_S3", snap.Close);
                }
            }

            return StrategySignal.NoSignal();
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add ITFXBot/Strategies/NRBColorChangeStrategy.cs
git commit -m "feat: add Strategy 3 - NRBColorChangeStrategy (narrow state breakout)"
```

---

### Task 13: Strategy 4 — MWPullbackStrategy

**Files:**
- Create: `e:/Test/ITFXBot/Strategies/MWPullbackStrategy.cs`

- [ ] **Step 1: Create MWPullbackStrategy.cs**

This is the most complex strategy. It tracks swing points, detects M/W patterns, waits for LCP, then enters on M8 pullback. We use a state machine within the strategy.

```csharp
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

        private readonly List<double> _swingHighs = new List<double>();
        private readonly List<double> _swingLows = new List<double>();
        private readonly List<double> _recentHighs = new List<double>();
        private readonly List<double> _recentLows = new List<double>();

        private enum PatternState { Scanning, WaitingForLCP, WaitingForM8Pullback }
        private PatternState _state = PatternState.Scanning;
        private TradeDirection _patternDirection = TradeDirection.None;
        private double _lcpLevel;

        public MWPullbackStrategy(int swingLookback, double patternTolerance)
        {
            _swingLookback = swingLookback;
            _patternTolerance = patternTolerance;
        }

        public StrategySignal Evaluate(MarketSnapshot snap)
        {
            UpdatePriceHistory(snap);

            if (_state == PatternState.Scanning)
            {
                ScanForPattern(snap);
            }

            if (_state == PatternState.WaitingForLCP)
            {
                CheckForLCP(snap);
            }

            if (_state == PatternState.WaitingForM8Pullback)
            {
                return CheckForM8Entry(snap);
            }

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

        private int _staleBarCount;

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
```

- [ ] **Step 2: Commit**

```bash
git add ITFXBot/Strategies/MWPullbackStrategy.cs
git commit -m "feat: add Strategy 4 - MWPullbackStrategy (M/W reversal with LCP)"
```

---

### Task 14: Strategy 5 — BackToM8sStrategy

**Files:**
- Create: `e:/Test/ITFXBot/Strategies/BackToM8sStrategy.cs`

- [ ] **Step 1: Create BackToM8sStrategy.cs**

```csharp
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
            {
                return StrategySignal.CreateBuy(Name, "ITFX_S5", snap.Close);
            }

            if (distanceFromSma20 > extremeThreshold)
            {
                return StrategySignal.CreateSell(Name, "ITFX_S5", snap.Close);
            }

            return StrategySignal.NoSignal();
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add ITFXBot/Strategies/BackToM8sStrategy.cs
git commit -m "feat: add Strategy 5 - BackToM8sStrategy (range mean-reversion)"
```

---

### Task 15: Main ITFXBot.cs — Parameters and Initialization

**Files:**
- Create: `e:/Test/ITFXBot/ITFXBot.cs`

- [ ] **Step 1: Create ITFXBot.cs with parameters, OnStart, and OnBar orchestration**

```csharp
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
        // ── General ──
        [Parameter("Entry Timeframe", Group = "General", DefaultValue = "Minute15")]
        public TimeFrame EntryTimeframe { get; set; }

        [Parameter("Confirmation Timeframe", Group = "General", DefaultValue = "Minute30")]
        public TimeFrame ConfirmationTimeframe { get; set; }

        [Parameter("Slope Lookback", Group = "General", DefaultValue = 5, MinValue = 1)]
        public int SlopeLookback { get; set; }

        [Parameter("Flat Threshold %", Group = "General", DefaultValue = 0.05)]
        public double FlatThreshold { get; set; }

        // ── Market State ──
        [Parameter("ATR Period", Group = "Market State", DefaultValue = 14, MinValue = 1)]
        public int ATRPeriod { get; set; }

        [Parameter("Narrow Multiplier", Group = "Market State", DefaultValue = 1.0)]
        public double NarrowMultiplier { get; set; }

        [Parameter("Wide Multiplier", Group = "Market State", DefaultValue = 3.0)]
        public double WideMultiplier { get; set; }

        // ── Strategy Toggles ──
        [Parameter("Enable 20 Touch", Group = "Strategies", DefaultValue = true)]
        public bool EnableTwentyTouch { get; set; }

        [Parameter("Enable SCC Anticipation", Group = "Strategies", DefaultValue = true)]
        public bool EnableSCCAnticipation { get; set; }

        [Parameter("Enable NRB Color Change", Group = "Strategies", DefaultValue = true)]
        public bool EnableNRBColorChange { get; set; }

        [Parameter("Enable M/W Pullback", Group = "Strategies", DefaultValue = true)]
        public bool EnableMWPullback { get; set; }

        [Parameter("Enable Back to M8s", Group = "Strategies", DefaultValue = false)]
        public bool EnableBackToM8s { get; set; }

        // ── Risk Management ──
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

        // ── Strategy-Specific ──
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

        // ── Internal ──
        private IndicatorManager _indicators;
        private MarketStateDetector _stateDetector;
        private RiskManager _riskManager;
        private TradeManager _tradeManager;
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

            _tradeManager = new TradeManager(this, _riskManager, BreakevenBufferPips, TargetPushCount);

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

            Print("[ITFX] Bot started | WinBox: {0} pips | Strategies: {1}",
                _riskManager.WinBoxPips,
                string.Join(", ", _strategies.Where(s => s.IsEnabled).Select(s => s.Name)));
        }

        private void OnEntryBarOpened(BarOpenedEventArgs args)
        {
            _tradeManager.CleanupClosedPositions();

            var snap = BuildSnapshot();

            Print("[ITFX] Bar | State: {0} | SMA20: {1:F5} | SMA200: {2:F5} | ATR: {3:F5}",
                snap.MarketState, snap.Sma20, snap.Sma200, snap.Atr);

            _tradeManager.ManageOpenPositions(snap.PreviousClose, snap.PreviousHigh, snap.PreviousLow);

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
                if (!validState) continue;

                var signal = strategy.Evaluate(snap);
                if (!signal.HasSignal) continue;

                if (strategy.Name == "SCC M8")
                {
                    // Strategy 2 allows pyramiding — skip the "one entry per bar" rule
                }
                else
                {
                    int existingForStrategy = _tradeManager.GetPositionCountByLabel(signal.Label);
                    if (existingForStrategy > 0) continue;
                }

                double volume = _riskManager.CalculateVolumeInUnits(Account.Balance);

                Print("[ITFX] SIGNAL: {0} {1} from {2}", signal.Direction, SymbolName, signal.StrategyName);
                _tradeManager.ExecuteTrade(signal, volume);
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

            bool isBullishEntry = _entryBars.ClosePrices.Last(1) > sma200 && sma20Slope > 0;
            bool isBullishConf = _confBars.ClosePrices.Last(1) > confSma200 && confSma20Slope > 0;

            bool dualAgrees = (isBullishEntry && isBullishConf) ||
                              (!isBullishEntry && !isBullishConf);

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
            Print("[ITFX] Bot stopped");
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add ITFXBot/ITFXBot.cs
git commit -m "feat: add main ITFXBot Robot with parameters and OnBar orchestration"
```

---

### Task 16: Breakout Guard for Strategy 5

**Files:**
- Modify: `e:/Test/ITFXBot/Core/TradeManager.cs`

Strategy 5 positions need a breakout guard: if a candle closes past range + WinBox, exit immediately regardless of Lifeline.

- [ ] **Step 1: Add breakout guard check to TradeManager.ManageOpenPositions**

Add this method to `TradeManager`:

```csharp
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
```

- [ ] **Step 2: Call breakout guard from ITFXBot.OnEntryBarOpened**

In `ITFXBot.cs`, inside `OnEntryBarOpened`, after `ManageOpenPositions`, add:

```csharp
if (EnableBackToM8s)
{
    _tradeManager.CheckBreakoutGuard(snap.PreviousClose, snap.Sma20, snap.Atr, RangeExtremeATR);
}
```

- [ ] **Step 3: Commit**

```bash
git add ITFXBot/Core/TradeManager.cs ITFXBot/ITFXBot.cs
git commit -m "feat: add breakout guard for Strategy 5 range positions"
```

---

### Task 17: Compilation Verification and Backtest

**Files:** None (verification only)

- [ ] **Step 1: Copy bot to cTrader source directory**

Copy the `ITFXBot/` folder to your cTrader Automate sources directory:

```bash
# Typical path — adjust for your cTrader installation:
cp -r e:/Test/ITFXBot "$HOME/Documents/cTrader Automate/Sources/Robots/ITFXBot"
```

- [ ] **Step 2: Open cTrader and compile**

1. Open cTrader desktop application
2. Go to **Automate** tab
3. Find **ITFXBot** in the left panel under cBots
4. Click **Build** (Ctrl+B)
5. Verify: "Build succeeded" with 0 errors

If there are errors, fix them and rebuild. Common issues:
- Missing `using` statements
- Namespace mismatches
- cAlgo API version differences

- [ ] **Step 3: Run initial backtest**

1. In cTrader, right-click ITFXBot → **Backtest**
2. Settings:
   - Symbol: Step Index (or any available symbol)
   - Timeframe: M15
   - Period: last 3 months
   - Initial balance: 10,000
   - All strategies enabled except Strategy 5
3. Click **Start**
4. Verify: bot runs without crashes, Print() logs show state detection and signal evaluation
5. Check the log for:
   - `[ITFX] Bot started` message with correct WinBox and strategy list
   - `[ITFX] Bar |` messages showing market state changes
   - At least some `[ITFX] SIGNAL:` messages (if not, try a more volatile period)

- [ ] **Step 4: Commit any compilation fixes**

```bash
git add -A ITFXBot/
git commit -m "fix: resolve compilation issues from backtest verification"
```

- [ ] **Step 5: Final commit with all files**

```bash
git add -A
git commit -m "feat: ITFX Daily Money cTrader cBot - complete implementation"
```

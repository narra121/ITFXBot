# ITFX Daily Money cTrader cBot — Design Spec

## Overview

A cTrader cBot (C#) implementing all 5 trading strategies from Immanuel Thelight's "Engineering Freedom Through Simple Mobile Trading" course. The bot uses the ITFX Daily Money Indicator system (4 SMAs), ATR-based market state detection, WinBox/Lifeline risk management, and a modular strategy pattern architecture.

## Decisions Summary

| Decision | Choice |
|----------|--------|
| Assets | All configurable per symbol (Step Index, US30, V100, Gold, EUR pairs) |
| Strategy selection | All 5 built in, toggled on/off via parameters |
| Market state detection | ATR-relative with user-adjustable multipliers |
| Stop loss | Candle-close Lifeline + emergency hard stop (2x WinBox) |
| Timeframes | Dual-timeframe confirmation, user-configurable (default M15 + M30) |
| Position sizing | Fixed lot OR risk-percent, user chooses mode |
| Pyramiding | Allowed for Strategy 2, max positions configurable (default 3) |
| Architecture | Strategy Pattern with separate classes |

---

## Section 1: Core Indicators & Market State Detection

### ITFX Daily Money Indicator

4 moving averages initialized on both timeframes (Entry + Confirmation):

| Indicator | Type | Period | Applied To | Purpose |
|-----------|------|--------|------------|---------|
| 200 SMA | Simple | 200 | Close | Trend filter / state anchor |
| 20 SMA | Simple | 20 | Close | Main trend line / entry zone |
| 8 SMA High | Simple | 8 | High | Micro-resistance (M8 High) |
| 8 SMA Low | Simple | 8 | Low | Micro-support (M8 Low) |

### Market State Detection (ATR-based)

```
distance = |SMA20 - SMA200|
atr = ATR(14)

if distance < NarrowMultiplier * atr -> Narrow State
if distance > WideMultiplier * atr -> Wide State
if SMA20 slope < FlatThreshold
   AND SMA20 between M8H and M8L -> Ranging State
else -> Trending State
```

### SMA20 Slope

Measured as percentage change over `SlopeLookback` bars:

```
slope = (SMA20[0] - SMA20[SlopeLookback]) / SMA20[SlopeLookback] * 100
```

If `|slope| < FlatThreshold` the SMA20 is considered flat.

### Dual Timeframe Confirmation

Indicators loaded on both `EntryTimeframe` (default M15) and `ConfirmationTimeframe` (default M30). A signal is only valid when both timeframes agree on:
- Trend direction (price above/below SMA200)
- SMA20 slope direction (both angled same way)

---

## Section 2: Risk Management System

### Position Sizing

Two modes via `SizingMode` parameter:

| Mode | Calculation |
|------|-------------|
| FixedLot | User sets `FixedLotSize` directly |
| RiskPercent | `LotSize = (Balance * RiskPercentage / 100) / (WinBoxPoints * PipValue)` |

### WinBox Per-Symbol Defaults

| Symbol | WinBox Points |
|--------|--------------|
| Step Index | 125 |
| US30 | 12,350 |
| Volatility 100 | 12,130 |
| XAUUSD | 1,240 |
| EURAUD / EURJPY | 1,020 |

User can override via `CustomWinBoxPoints` for unlisted symbols.

### Lifeline (Primary Stop)

- Placed at WinBox distance from entry
- Buy: `Lifeline = EntryPrice - WinBoxPoints`
- Sell: `Lifeline = EntryPrice + WinBoxPoints`
- Exit rule: only if candle **closes** past the Lifeline (checked on each bar close)
- Wicks past the Lifeline do NOT trigger exit

### Emergency Hard Stop

- Server-side stop-loss at `EmergencyStopMultiplier * WinBox` from entry (default 2.0x)
- Safety net against flash crashes
- Always active regardless of Lifeline logic

### Breakeven

- Triggered when price forms a new high (buy) or new low (sell) beyond entry
- Stop moved to `EntryPrice +/- BreakevenBufferPips`

### Trailing Stop

- After breakeven, trails behind each subsequent swing high/low
- Three-push rule: bot counts trend "pushes" (waves)
- Auto-close after `TargetPushCount` pushes (default 3) OR continue trailing

### Push Detection

A "push" is a new swing extreme followed by a pullback:
- Uptrend push: new swing high followed by a pullback (lower low on subsequent bar)
- Downtrend push: new swing low followed by a pullback (higher high on subsequent bar)

### Pyramiding (Strategy 2 Only)

- `MaxOpenPositions` parameter (default 3)
- Each new position uses the same lot size calculation
- Each position gets its own Lifeline and emergency stop
- Only adds when a new SCC + M8 touch occurs in the same power move

---

## Section 3: Strategy Logic

### Strategy 1 — 20 Touch Entry (Trend Continuation)

| Element | Rule |
|---------|------|
| Market State | Trending (emerging from Narrow preferred) |
| Trend Filter | Price on same side of 200 SMA as trade direction |
| Entry Trigger | Price pulls back and touches the 20 SMA |
| Buy | Price touches 20 SMA from above in uptrend |
| Sell | Price touches 20 SMA from below in downtrend |
| Dual TF | Both M15 and M30 must agree on trend direction |
| Lifeline | WinBox distance from entry, candle-close exit |
| Target | Three-push rule OR 2:1 risk-reward, whichever first |

**Touch detection:** Bar low <= SMA20 value (for buys) or Bar high >= SMA20 value (for sells) on the entry timeframe.

### Strategy 2 — SCC to Anticipation M8 Entry (Power Move Add-On)

| Element | Rule |
|---------|------|
| Market State | Trending — power move (steep, fast) |
| Power Detection | Price distance from 20 SMA > configurable threshold |
| SCC Signal | Strong candle closing in power direction (body > 50% of candle range) |
| Entry Trigger | Opposite-color candle touches M8 line |
| Buy | Red candle pulls back to touch M8 High |
| Sell | Green candle pulls back to touch M8 Low |
| Pyramiding | Up to `MaxOpenPositions` on successive M8 touches |
| Dual TF | Both timeframes confirm power direction |
| Target | Three-push rule OR 2:1 RR |

**Power move detection:** Distance from price to SMA20 > `PowerMoveATR * ATR` (derived from market being too steep to touch the 20).

### Strategy 3 — NRB Color Change (Narrow State Breakout)

| Element | Rule |
|---------|------|
| Market State | Narrow State (20 and 200 SMA converging) |
| NRB Detection | Candle body size < `NRBThresholdPercent` of ATR (default 25%) |
| Proximity | NRB must be within `NRBMaxDistanceATR` of 20 SMA |
| Three-candle pattern | 1) NRB of opposite color, 2) next candle clears NRB high/low |
| Buy | Red NRB cleared by green candle closing above NRB high |
| Sell | Green NRB cleared by red candle closing below NRB low |
| Dual TF | Both timeframes in Narrow State or early trend |
| Target | Three-push rule |

**NRB body calculation:** `bodySize = |Open - Close|`. NRB if `bodySize < (NRBThresholdPercent / 100) * ATR`.

### Strategy 4 — M/W M8 Pullback to LCP (Reversal)

| Element | Rule |
|---------|------|
| Market State | Wide State (20 and 200 SMA far apart) |
| M Pattern | Swing high -> swing low -> swing high within `PatternTolerance` % |
| W Pattern | Swing low -> swing high -> swing low within `PatternTolerance` % |
| LCP SCC | Strong candle that clears the entire M/W structure |
| Entry | After LCP SCC, micro-pullback to M8 line |
| Buy (W) | LCP clears W -> red candle touches M8 High |
| Sell (M) | LCP clears M -> green candle touches M8 Low |
| Target | Hold toward opposite state (Wide -> Narrow) OR three-push rule |

**Swing detection:** Uses `SwingLookback` bars (default 10) to identify local highs and lows. A swing high requires the bar's high to be higher than `SwingLookback` bars on each side. Pattern tolerance allows peaks/troughs within `PatternTolerance`% of each other.

**LCP detection:** After pattern forms, a candle that closes above the W valley (for buys, clearing the W structure upward) or below the M valley (for sells, clearing the M structure downward), consuming all intermediate price action.

### Strategy 5 — Back to M8s to the 20 (Range Mean-Reversion)

| Element | Rule |
|---------|------|
| Market State | Ranging (20 SMA flat, between M8H and M8L) |
| Range Detection | SMA20 slope < FlatThreshold AND SMA20 between M8H and M8L |
| Entry | Price at range extreme (far from 20 SMA) |
| Buy | Price below 20 SMA by > `RangeExtremeATR` * ATR |
| Sell | Price above 20 SMA by > `RangeExtremeATR` * ATR |
| Target | Primary: 20 SMA. Extended: opposite side of range |
| Breakout Guard | If candle closes past range + WinBox, exit immediately |

**Breakout guard:** If a candle closes beyond the detected range boundary by more than WinBox distance, close the position immediately regardless of Lifeline — the range has likely broken.

---

## Section 4: cBot Parameters

### General

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| EntryTimeframe | TimeFrame | M15 | Primary timeframe |
| ConfirmationTimeframe | TimeFrame | M30 | Dual-confirmation timeframe |
| SlopeLookback | int | 5 | Bars to measure SMA20 slope |
| FlatThreshold | double | 0.05 | Max slope % to consider flat |

### Market State

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| ATRPeriod | int | 14 | ATR calculation period |
| NarrowMultiplier | double | 1.0 | Distance < this * ATR = Narrow |
| WideMultiplier | double | 3.0 | Distance > this * ATR = Wide |

### Strategy Toggles

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| EnableTwentyTouch | bool | true | Strategy 1 on/off |
| EnableSCCAnticipation | bool | true | Strategy 2 on/off |
| EnableNRBColorChange | bool | true | Strategy 3 on/off |
| EnableMWPullback | bool | true | Strategy 4 on/off |
| EnableBackToM8s | bool | false | Strategy 5 on/off (advanced, off by default) |

### Risk Management

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| SizingMode | enum | RiskPercent | FixedLot or RiskPercent |
| FixedLotSize | double | 0.01 | Lot size (FixedLot mode) |
| RiskPercentage | double | 2.0 | % of balance per trade |
| CustomWinBoxPoints | double | 0 | Override WinBox (0 = use defaults) |
| EmergencyStopMultiplier | double | 2.0 | Hard stop at N * WinBox |
| BreakevenBufferPips | double | 2.0 | Buffer before breakeven move |
| TargetPushCount | int | 3 | Close after N pushes |
| MaxOpenPositions | int | 3 | Max pyramided positions (Strategy 2) |

### Strategy-Specific

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| NRBThresholdPercent | double | 25.0 | Max body as % of ATR for NRB |
| NRBMaxDistanceATR | double | 1.0 | Max NRB distance from 20 SMA |
| SwingLookback | int | 10 | Bars to identify swing points |
| PatternTolerance | double | 0.5 | % tolerance for M/W peak matching |
| RangeExtremeATR | double | 1.5 | Min distance from 20 SMA for range entry |
| PowerMoveATR | double | 2.0 | Min price-to-SMA20 distance for power move |

---

## Section 5: Architecture & File Structure

### Pattern: Strategy Pattern with Modular Classes

```
ITFXBot/
+-- ITFXBot.cs                      -- cBot entry, parameters, OnBar orchestration
+-- Core/
|   +-- MarketStateDetector.cs      -- Narrow/Wide/Ranging/Trending detection
|   +-- IndicatorManager.cs         -- SMA 200, 20, 8H, 8L on both timeframes
|   +-- RiskManager.cs              -- WinBox, Lifeline, position sizing, emergency stop
|   +-- TradeManager.cs             -- Execution, breakeven, trailing, push counting
+-- Strategies/
|   +-- IStrategy.cs                -- Interface: StrategySignal Evaluate(MarketState, BarData)
|   +-- TwentyTouchStrategy.cs      -- Strategy 1
|   +-- SCCAnticipationStrategy.cs  -- Strategy 2
|   +-- NRBColorChangeStrategy.cs   -- Strategy 3
|   +-- MWPullbackStrategy.cs       -- Strategy 4
|   +-- BackToM8sStrategy.cs        -- Strategy 5
+-- Models/
    +-- MarketState.cs              -- Enum: Narrow, Wide, Ranging, Trending
    +-- StrategySignal.cs           -- Signal type, direction, entry price, strategy name
    +-- WinBoxConfig.cs             -- Per-symbol WinBox point mappings
```

### IStrategy Interface

```csharp
public interface IStrategy
{
    string Name { get; }
    bool IsEnabled { get; set; }
    MarketStateType[] ValidStates { get; }
    StrategySignal Evaluate(MarketSnapshot snapshot);
}
```

### MarketSnapshot (passed to strategies each bar)

Contains: current bar data, indicator values (all 4 SMAs on both timeframes), market state, ATR value, open positions, SMA20 slope.

### OnBar Execution Flow

```
1. Update indicators on both timeframes
2. Detect market state (Narrow / Wide / Ranging / Trending)
3. Manage open positions:
   a. Check Lifeline candle-close exits
   b. Update breakeven / trailing stops
   c. Count pushes for take-profit
4. For each enabled strategy:
   a. Skip if market state not in strategy's ValidStates
   b. Check dual-timeframe confirmation
   c. Call strategy.Evaluate(snapshot)
   d. If signal -> calculate lot size -> execute trade -> set Lifeline + emergency stop
5. Log state, signals, and actions to cTrader log
```

### Strategy Priority

When multiple strategies fire simultaneously:
- Only one new entry per bar (prevents conflicting entries)
- Priority order: Strategy 4 > Strategy 1 > Strategy 3 > Strategy 2 > Strategy 5
- Rationale: reversals (4) take precedence, then trend entries (1, 3), then add-ons (2), then range (5)

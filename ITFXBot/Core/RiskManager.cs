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

        public double GetTakeProfitPips()
        {
            return WinBoxPips * 2.0;
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

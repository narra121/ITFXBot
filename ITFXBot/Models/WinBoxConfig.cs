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

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

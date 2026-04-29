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
                return StrategySignal.CreateBuy(Name, "ITFX_S1", snap.Close);

            if (isBearish && snap.High >= snap.Sma20 && snap.Close < snap.Sma20)
                return StrategySignal.CreateSell(Name, "ITFX_S1", snap.Close);

            return StrategySignal.NoSignal();
        }
    }
}

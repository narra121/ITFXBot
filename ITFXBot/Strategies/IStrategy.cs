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

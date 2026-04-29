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

namespace cAlgo.Robots
{
    public class MarketSnapshot
    {
        public double Open { get; set; }
        public double High { get; set; }
        public double Low { get; set; }
        public double Close { get; set; }
        public double PreviousClose { get; set; }
        public double PreviousOpen { get; set; }
        public double PreviousHigh { get; set; }
        public double PreviousLow { get; set; }

        public double Sma200 { get; set; }
        public double Sma20 { get; set; }
        public double Sma20Previous { get; set; }
        public double M8High { get; set; }
        public double M8Low { get; set; }
        public double Atr { get; set; }
        public double Sma20Slope { get; set; }

        public double ConfSma200 { get; set; }
        public double ConfSma20 { get; set; }
        public double ConfSma20Slope { get; set; }

        public MarketStateType MarketState { get; set; }

        public bool IsBullishEntry { get; set; }
        public bool IsBullishConfirmation { get; set; }
        public bool DualTimeframeAgrees { get; set; }

        public double Bid { get; set; }
        public double Ask { get; set; }

        public int OpenPositionCount { get; set; }
        public int Strategy2PositionCount { get; set; }

        public bool IsGreenCandle => Close > Open;
        public bool IsRedCandle => Close < Open;
        public double CandleBody => System.Math.Abs(Close - Open);
        public double CandleRange => High - Low;
        public bool PreviousIsGreen => PreviousClose > PreviousOpen;
        public bool PreviousIsRed => PreviousClose < PreviousOpen;
        public double PreviousBody => System.Math.Abs(PreviousClose - PreviousOpen);
    }
}

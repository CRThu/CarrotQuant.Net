namespace CarrotBacktesting.NET.Data
{
    public class MarketFrameBuilder
    {
        public DateTime Time { get; set; }

        public StockFrame?[] PrimaryData { get; set; }

        //Dictionary<string, string?[]> StringExtendedData { get; set; } = new Dictionary<string, string?[]>();

        //Dictionary<string, float?[]> DoubleExtendedData { get; set; } = new Dictionary<string, float?[]>();

        public MarketFrameBuilder(string time, int capable)
        {
            Time = DateTime.Parse(time);
            PrimaryData = new StockFrame?[capable];
        }

        public void Add(int index, StockFrame? frame)
        {
            PrimaryData[index] = frame;
        }

        public MarketFrame Build()
        {
            return new MarketFrame(Time, PrimaryData, null, null);
        }
    }
}
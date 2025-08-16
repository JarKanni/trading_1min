using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CryptoClients.Net;
using CryptoClients.Net.Interfaces;
using CryptoExchange.Net.SharedApis;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Authentication;

var client = new ExchangeRestClient();

// Print enough newlines to "clear" the visible area
Console.WriteLine(new string('\n', 20));

Console.WriteLine("===============================================");
Console.WriteLine("        1-Minute Candlestick Monitor");
Console.WriteLine("===============================================");
Console.WriteLine("Starting 1-minute candlestick monitoring...");
Console.WriteLine();

// Note: Kraken uses USD instead of USDT for most trading pairs
var symbols = new[]
{
    new { Name = "BTC", Symbol = new SharedSymbol(TradingMode.Spot, "BTC", "USD") },
    new { Name = "ETH", Symbol = new SharedSymbol(TradingMode.Spot, "ETH", "USD") },
    new { Name = "XRP", Symbol = new SharedSymbol(TradingMode.Spot, "XRP", "USD") },
    new { Name = "LINK", Symbol = new SharedSymbol(TradingMode.Spot, "LINK", "USD") },
    new { Name = "ALGO", Symbol = new SharedSymbol(TradingMode.Spot, "ALGO", "USD") },
    new { Name = "BAT", Symbol = new SharedSymbol(TradingMode.Spot, "BAT", "USD") }
};

var logFile = "/home/trading/1min/prices.csv";  // Updated path for 1min

// Create CSV header if file doesn't exist
if (!File.Exists(logFile))
{
    await File.WriteAllTextAsync(logFile, "timestamp,coin,open,low,high,close,variance\n");
}

while (true)
{
    var startTime = DateTime.Now;
    var timestamp = startTime.ToString("yyyy-MM-dd HH:mm:ss");
    
    Console.WriteLine("\n\n");
    Console.WriteLine("=========================================================================");
    Console.WriteLine($"   1-Min Candlesticks         {DateTime.Now:h:mm:sstt}           >>------<<");
    Console.WriteLine("=========================================================================");
    Console.WriteLine(" Coin |    Open     |     Low     |    High     |    Close    | Variance ");
    Console.WriteLine("------|-------------|-------------|-------------|-------------|----------");

    foreach (var crypto in symbols)
    {
        try
        {
            // Get 1-minute candlestick data (last 2 candles to ensure we get a complete one)
            var klineRequest = new GetKlinesRequest(crypto.Symbol, SharedKlineInterval.OneMinute);
            klineRequest.Limit = 2; // Get last 2 candles
            klineRequest.EndTime = DateTime.UtcNow;
            
            var klineResults = await client.GetKlinesAsync(klineRequest, new[] { "Kraken" });
            
            if (klineResults.Any() && klineResults.First().Success && klineResults.First().Data.Any())
            {
                // Get the most recent completed candle (second to last, as the last one might be incomplete)
                var candles = klineResults.First().Data.ToList();
                var candle = candles.Count > 1 ? candles[candles.Count - 2] : candles.Last();
                
                var open = candle.OpenPrice;
                var low = candle.LowPrice;
                var high = candle.HighPrice;
                var close = candle.ClosePrice;
                var volume = candle.Volume;
                
                // Calculate variance: distance from close to middle of range
                var midPrice = (low + high) / 2;
                var variance = close - midPrice;
                
                // Display in table format
                Console.WriteLine($"{crypto.Name,-5} | ${open,10:F2} | ${low,10:F2} | ${high,10:F2} | ${close,10:F2} | ${variance,8:F2}");
                
                // Log to CSV file
                var csvLine = $"{timestamp},{crypto.Name},{open:F2},{low:F2},{high:F2},{close:F2},{variance:F2}\n";
                await File.AppendAllTextAsync(logFile, csvLine);
            }
            else
            {
                Console.WriteLine($"{crypto.Name,-5} | {"Error",-10} | {"Error",-10} | {"Error",-10} | {"Error",-10} | {"Error",-8}");
                
                // Add error logging
                var errorMessage = klineResults.FirstOrDefault()?.Error?.Message ?? "No candlestick data available";
                var errorLog = $"{timestamp},ERROR,{crypto.Name},{errorMessage}\n";
                await File.AppendAllTextAsync("/home/trading/1min/errors.log", errorLog);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{crypto.Name,-5} | {"Exception",-10} | {"Exception",-10} | {"Exception",-10} | {"Exception",-10} | {"Exception",-8}");
            
            // Add exception logging
            var errorLog = $"{timestamp},EXCEPTION,{crypto.Name},{ex.Message}\n";
            await File.AppendAllTextAsync("/home/trading/1min/errors.log", errorLog);
        }
    }
    
    Console.WriteLine();
    
    // Calculate how long the API calls took
    var elapsed = DateTime.Now - startTime;
    var remainingTime = TimeSpan.FromMinutes(1) - elapsed;
    
    // Only delay if there's time remaining
    if (remainingTime.TotalMilliseconds > 0)
    {
        await Task.Delay(remainingTime);
    }
    else
    {
        Console.WriteLine("API calls took longer than 1 minute, updating immediately...");
    }
}
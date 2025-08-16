using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using CryptoClients.Net;
using CryptoClients.Net.Interfaces;
using CryptoExchange.Net.SharedApis;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Authentication;

var client = new ExchangeRestClient();

// Print enough newlines to "clear" the visible area
Console.WriteLine(new string('\n', 20));

Console.WriteLine("===============================================");
Console.WriteLine("        1-Minute Ticker-Based Monitor");
Console.WriteLine("===============================================");
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

var logFile = "/home/trading/1min/prices.csv";

// Create CSV header if file doesn't exist
if (!File.Exists(logFile))
{
    await File.WriteAllTextAsync(logFile, "timestamp,coin,open,low,high,close,percent_change,volatility,spread,samples\n");
}

// Dictionary to store price samples for each coin within the current minute
var priceData = new Dictionary<string, List<(DateTime Time, decimal Price)>>();

// Dictionary to store the current minute's OHLC data
var currentBars = new Dictionary<string, (decimal Open, decimal Low, decimal High, decimal Close, DateTime StartTime, int SampleCount)>();

// Load initial data from existing CSV if available - get last 4 samples per coin
if (File.Exists(logFile))
{
    try
    {
        var lines = File.ReadAllText(logFile).Split('\n');
        var dataLines = new List<string>();
        
        for (int i = 1; i < lines.Length; i++) // Skip header
        {
            if (!string.IsNullOrWhiteSpace(lines[i]))
            {
                dataLines.Add(lines[i]);
            }
        }
        
        if (dataLines.Count > 0)
        {
            // Get the last 50 entries to ensure we have enough data
            var recentLines = dataLines.TakeLast(50).ToList();
            
            // Find the most recent minute that has data
            DateTime? latestMinute = null;
            for (int i = recentLines.Count - 1; i >= 0; i--)
            {
                var parts = recentLines[i].Split(',');
                if (parts.Length >= 9 && DateTime.TryParse(parts[0], out var timestamp))
                {
                    var minute = new DateTime(timestamp.Year, timestamp.Month, timestamp.Day, timestamp.Hour, timestamp.Minute, 0);
                    latestMinute = minute;
                    break;
                }
            }
            
            if (latestMinute.HasValue)
            {
                // Just get the final OHLC data for each coin from the most recent completed minute
                for (int i = recentLines.Count - 1; i >= 0; i--)
                {
                    var parts = recentLines[i].Split(',');
                    if (parts.Length >= 10 && DateTime.TryParse(parts[0], out var timestamp))
                    {
                        var minute = new DateTime(timestamp.Year, timestamp.Month, timestamp.Day, timestamp.Hour, timestamp.Minute, 0);
                        
                        if (minute == latestMinute.Value && 
                            decimal.TryParse(parts[2], out var open) &&
                            decimal.TryParse(parts[3], out var low) &&
                            decimal.TryParse(parts[4], out var high) &&
                            decimal.TryParse(parts[5], out var close) &&
                            int.TryParse(parts[9], out var samples))
                        {
                            var coinName = parts[1];
                            // Only take the entry with the highest sample count (most complete)
                            if (!currentBars.ContainsKey(coinName) || samples > currentBars[coinName].SampleCount)
                            {
                                currentBars[coinName] = (open, low, high, close, latestMinute.Value, samples);
                            }
                        }
                    }
                }
                
                if (currentBars.Any())
                {
                    // Display the loaded data immediately
                    Console.WriteLine("===============================================================");
                    Console.WriteLine($"   Last Completed Minute: {latestMinute.Value:HH:mm}");
                    Console.WriteLine("===============================================================");
                    Console.WriteLine(" Coin |    Open     |     Low     |    High     |    Close    | % Change | Volatility | Spread  | Samples");
                    Console.WriteLine("------|-------------|-------------|-------------|-------------|----------|------------|---------|--------");

                    foreach (var crypto in symbols)
                    {
                        if (currentBars.ContainsKey(crypto.Name))
                        {
                            var bar = currentBars[crypto.Name];
                            var open = bar.Open;
                            var low = bar.Low;
                            var high = bar.High;
                            var close = bar.Close;
                            var samples = bar.SampleCount;
                            
                            // Calculate metrics
                            var percentChange = open != 0 ? ((close - open) / open) * 100 : 0;
                            var volatility = open != 0 ? ((high - low) / open) * 100 : 0;
                            var spread = high - low;
                            
                            Console.WriteLine($"{crypto.Name,-5} | ${open,10:F2} | ${low,10:F2} | ${high,10:F2} | ${close,10:F2} | {percentChange,7:F2}% | {volatility,9:F2}% | ${spread,7:F2} | {samples,6}");
                        }
                    }
                    Console.WriteLine();
                }
                else
                {
                    Console.WriteLine("No CSV data could be loaded - starting fresh");
                }
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error loading CSV data: {ex.Message}");
    }
}

// Keep any loaded data - don't clear it immediately
// currentBars.Clear(); // REMOVED - this was wiping out the CSV data!

var lastDisplayTime = DateTime.MinValue;

// If we loaded data from CSV, set the lastDisplayTime to that minute
if (currentBars.Any())
{
    lastDisplayTime = currentBars.Values.First().StartTime;
}

while (true)
{
    var now = DateTime.Now;
    var currentMinute = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
    
    // Check if we've moved to a new minute - if so, display the completed bar and reset
    if (lastDisplayTime != DateTime.MinValue && currentMinute > lastDisplayTime)
    {
        // Clear the data for the new minute
        currentBars.Clear();
        priceData.Clear();
    }
    
    // Collect current price samples FIRST
    foreach (var crypto in symbols)
    {
        try
        {
            var results = await client.GetSpotTickerAsync(new GetTickerRequest(crypto.Symbol), new[] { "Kraken" });
            
            if (results.Any() && results.First().Success)
            {
                var currentPrice = results.First().Data.LastPrice ?? 0m;
                
                // Skip if price is 0 (invalid data)
                if (currentPrice == 0) continue;
                
                var sampleTime = DateTime.Now;
                
                // Initialize price data for this coin if it doesn't exist
                if (!priceData.ContainsKey(crypto.Name))
                {
                    priceData[crypto.Name] = new List<(DateTime, decimal)>();
                }
                
                // Add current price sample
                priceData[crypto.Name].Add((sampleTime, currentPrice));
                
                // Update or create the OHLC bar for this minute
                if (currentBars.ContainsKey(crypto.Name))
                {
                    var bar = currentBars[crypto.Name];
                    currentBars[crypto.Name] = (
                        bar.Open,  // Keep original open
                        Math.Min(bar.Low, currentPrice),  // Update low
                        Math.Max(bar.High, currentPrice), // Update high
                        currentPrice,  // Update close to current price
                        bar.StartTime,
                        bar.SampleCount + 1
                    );
                }
                else
                {
                    // First sample for this coin in this minute
                    currentBars[crypto.Name] = (
                        currentPrice,  // Open
                        currentPrice,  // Low
                        currentPrice,  // High
                        currentPrice,  // Close
                        currentMinute, // Start time
                        1              // Sample count
                    );
                }
            }
            else
            {
                // Log error for this sample
                var errorMessage = results.FirstOrDefault()?.Error?.Message ?? "Unknown ticker error";
                var errorLog = $"{now:yyyy-MM-dd HH:mm:ss},TICKER_ERROR,{crypto.Name},{errorMessage}\n";
                await File.AppendAllTextAsync("/home/trading/1min/errors.log", errorLog);
            }
        }
        catch (Exception ex)
        {
            // Log exception for this sample
            var errorLog = $"{now:yyyy-MM-dd HH:mm:ss},TICKER_EXCEPTION,{crypto.Name},{ex.Message}\n";
            await File.AppendAllTextAsync("/home/trading/1min/errors.log", errorLog);
        }
    }
    
    // NOW display the updated data
    Console.WriteLine("\n\n");
    Console.WriteLine("===============================================================");
    Console.WriteLine($"   1-Min Ticker Bars     >>------<<          {DateTime.Now:h:mm:sstt}");
    Console.WriteLine("===============================================================");
    Console.WriteLine(" Coin |    Open     |     Low     |    High     |    Close    | % Change | Volatility | Spread  | Samples");
    Console.WriteLine("------|-------------|-------------|-------------|-------------|----------|------------|---------|--------");

    // Show current bars for all coins
    foreach (var crypto in symbols)
    {
        if (currentBars.ContainsKey(crypto.Name))
        {
            var bar = currentBars[crypto.Name];
            var open = bar.Open;
            var low = bar.Low;
            var high = bar.High;
            var close = bar.Close;
            var samples = bar.SampleCount;
            
            // Calculate metrics
            var percentChange = open != 0 ? ((close - open) / open) * 100 : 0;
            var volatility = open != 0 ? ((high - low) / open) * 100 : 0;
            var spread = high - low;
            
            // Display in table format
            Console.WriteLine($"{crypto.Name,-5} | ${open,10:F2} | ${low,10:F2} | ${high,10:F2} | ${close,10:F2} | {percentChange,7:F2}% | {volatility,9:F2}% | ${spread,7:F2} | {samples,6}");
        }
        else
        {
            Console.WriteLine($"{crypto.Name,-5} | {"--",-10} | {"--",-10} | {"--",-10} | {"--",-10} | {"--",-8} | {"--",-10} | {"--",-8} | {"0",6}");
        }
    }
    
    // Log completed minute data to CSV only when minute changes
    if (lastDisplayTime != DateTime.MinValue && currentMinute > lastDisplayTime && currentBars.Any())
    {
        var timestamp = lastDisplayTime.ToString("yyyy-MM-dd HH:mm:ss");
        foreach (var crypto in symbols)
        {
            if (currentBars.ContainsKey(crypto.Name))
            {
                var bar = currentBars[crypto.Name];
                var percentChange = bar.Open != 0 ? ((bar.Close - bar.Open) / bar.Open) * 100 : 0;
                var volatility = bar.Open != 0 ? ((bar.High - bar.Low) / bar.Open) * 100 : 0;
                var spread = bar.High - bar.Low;
                
                var csvLine = $"{timestamp},{crypto.Name},{bar.Open:F2},{bar.Low:F2},{bar.High:F2},{bar.Close:F2},{percentChange:F2},{volatility:F2},{spread:F2},{bar.SampleCount}\n";
                await File.AppendAllTextAsync(logFile, csvLine);
            }
        }
    }
    
    // Update the last display time to current minute
    lastDisplayTime = currentMinute;
    
    // Wait 15 seconds before next sample
    await Task.Delay(15000);
}
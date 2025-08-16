using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace TradingMonitor;

public class PriceSample
{
    public DateTime Timestamp { get; set; }
    public decimal Price { get; set; }
    public string Source { get; set; } = "API";
}

public class MinuteBar
{
    public DateTime Minute { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public List<PriceSample> Samples { get; set; } = [];
    
    public decimal PercentChange => Open != 0 ? ((Close - Open) / Open) * 100 : 0;
    public decimal Volatility => CalculateVolatility();
    public decimal Spread => High - Low;
    public int SampleCount => Samples.Count;

    private decimal CalculateVolatility()
    {
        if (Samples.Count < 2) return 0;
        
        var prices = Samples.Select(s => s.Price).ToList();
        var mean = prices.Average();
        var variance = prices.Sum(p => (p - mean) * (p - mean)) / prices.Count;
        var stdDev = (decimal)Math.Sqrt((double)variance);
        
        return mean != 0 ? (stdDev / mean) * 100 : 0;
    }

    public void AddSample(decimal price, DateTime timestamp)
    {
        var sample = new PriceSample { Price = price, Timestamp = timestamp };
        
        if (Samples.Count == 0)
        {
            Open = price;
            High = price;
            Low = price;
        }
        else
        {
            if (price > High) High = price;
            if (price < Low) Low = price;
        }
        
        Close = price;
        Samples.Add(sample);
    }
}

// DTOs for Kraken API responses
public class KrakenTickerResponse
{
    public Dictionary<string, KrakenTickerData>? result { get; set; }
    public string[]? error { get; set; }
}

public class KrakenTickerData
{
    public string[]? c { get; set; } // Last trade closed array [price, lot volume]
}

public class KrakenClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _apiSecret;
    private const string BaseUrl = "https://api.kraken.com";

    public KrakenClient(string apiKey, string apiSecret)
    {
        _apiKey = apiKey;
        _apiSecret = apiSecret;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "TradingMonitor/1.0");
    }

    public async Task<Dictionary<string, decimal>> GetTickerPricesAsync(string[] pairs)
    {
        try
        {
            var pairList = string.Join(",", pairs);
            var url = $"{BaseUrl}/0/public/Ticker?pair={pairList}";
            
            var response = await _httpClient.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();
            
            var tickerResponse = JsonSerializer.Deserialize<KrakenTickerResponse>(content);
            
            if (tickerResponse?.error?.Length > 0)
            {
                Console.WriteLine($"ERROR: Kraken API errors: {string.Join(", ", tickerResponse.error)}");
                return new Dictionary<string, decimal>();
            }

            var results = new Dictionary<string, decimal>();
            
            if (tickerResponse?.result != null)
            {
                foreach (var (pair, data) in tickerResponse.result)
                {
                    if (data?.c?.Length > 0 && decimal.TryParse(data.c[0], out var price))
                    {
                        results[pair] = price;
                    }
                }
            }
            
            return results;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: Kraken API call failed: {ex.Message}");
            return new Dictionary<string, decimal>();
        }
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}

public class CoinMonitor
{
    // Kraken symbol mapping - corrected for their actual API responses
    private readonly Dictionary<string, string> _krakenPairs = new()
    {
        { "BTC", "XXBTZUSD" },  // Bitcoin on Kraken
        { "ETH", "XETHZUSD" },  // Ethereum on Kraken  
        { "XRP", "XXRPZUSD" },  // XRP on Kraken
        { "LINK", "LINKUSD" },  // Chainlink on Kraken
        { "ALGO", "ALGOUSD" },  // Algorand on Kraken
        { "BAT", "BATUSD" }     // BAT on Kraken
    };

    private readonly KrakenClient _krakenClient;
    private readonly ConcurrentDictionary<string, List<PriceSample>> _currentMinuteData;
    private readonly object _displayLock = new();
    private readonly ILogger _logger;
    private DateTime _currentMinute;
    private readonly Timer _samplingTimer;
    private readonly Timer _minuteTimer;
    private bool _isRunning = true;

    public CoinMonitor()
    {
        using var factory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = factory.CreateLogger<CoinMonitor>();
        
        // Load API credentials from environment or .env file
        var apiKey = Environment.GetEnvironmentVariable("KRAKEN_API_KEY") ?? LoadFromEnv("KRAKEN_API_KEY");
        var apiSecret = Environment.GetEnvironmentVariable("KRAKEN_API_SECRET") ?? LoadFromEnv("KRAKEN_API_SECRET");
        
        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
        {
            Console.WriteLine("WARNING: Kraken API credentials not found. Add KRAKEN_API_KEY and KRAKEN_API_SECRET to .env file or environment variables.");
            Console.WriteLine("For public data only, we'll continue without authentication.");
        }
        
        _krakenClient = new KrakenClient(apiKey ?? "", apiSecret ?? "");
        _currentMinuteData = new ConcurrentDictionary<string, List<PriceSample>>();
        _currentMinute = GetCurrentMinute();
        
        // Initialize data structures
        foreach (var symbol in _krakenPairs.Keys)
        {
            _currentMinuteData[symbol] = [];
        }

        // Sample every 5 seconds
        _samplingTimer = new Timer(SamplePricesCallback, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        
        // Check for minute transitions every second
        _minuteTimer = new Timer(CheckMinuteTransition, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private static string? LoadFromEnv(string key)
    {
        try
        {
            if (!File.Exists(".env")) return null;
            
            var lines = File.ReadAllLines(".env");
            foreach (var line in lines)
            {
                if (line.StartsWith($"{key}="))
                {
                    return line.Substring(key.Length + 1).Trim();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARNING: Error reading .env file: {ex.Message}");
        }
        return null;
    }

    private static DateTime GetCurrentMinute()
    {
        var now = DateTime.Now;
        return new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
    }

    private async void SamplePricesCallback(object? state)
    {
        if (!_isRunning) return;

        try
        {
            var timestamp = DateTime.Now;
            var currentMinute = GetCurrentMinute();
            
            // If minute changed, don't collect data until after transition is handled
            if (currentMinute != _currentMinute)
                return;

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Get all prices in one API call
            var krakenPairs = _krakenPairs.Values.ToArray();
            var priceResults = await _krakenClient.GetTickerPricesAsync(krakenPairs);
            stopwatch.Stop();

            // Process results and map back to our symbols
            var successCount = 0;
            foreach (var (symbol, krakenPair) in _krakenPairs)
            {
                if (priceResults.TryGetValue(krakenPair, out var price))
                {
                    _currentMinuteData[symbol].Add(new PriceSample
                    {
                        Price = price,
                        Timestamp = timestamp,
                        Source = "Kraken"
                    });
                    successCount++;
                }
            }

            // Display current status
            DisplayCurrentData();
        }
        catch (Exception ex)
        {
            await LogError($"Critical error in sampling: {ex.Message}");
        }
    }

    private void CheckMinuteTransition(object? state)
    {
        var currentMinute = GetCurrentMinute();
        if (currentMinute != _currentMinute)
        {
            ProcessCompletedMinute();
            _currentMinute = currentMinute;
        }
    }

    private void ProcessCompletedMinute()
    {
        Console.WriteLine($"\n===============================================================");
        Console.WriteLine($"   Minute {_currentMinute:HH:mm} Complete - Processing Data");
        Console.WriteLine($"===============================================================");

        // Convert to minute bars and log
        var minuteBars = new List<MinuteBar>();
        foreach (var (symbol, samples) in _currentMinuteData)
        {
            if (samples.Count != 0)
            {
                var bar = new MinuteBar { Minute = _currentMinute };
                foreach (var sample in samples.OrderBy(s => s.Timestamp))
                {
                    bar.AddSample(sample.Price, sample.Timestamp);
                }
                minuteBars.Add(bar);
            }
        }

        // Log to CSV
        LogMinuteBarsToCSV(minuteBars);

        // Clear data for new minute
        foreach (var symbol in _krakenPairs.Keys)
        {
            _currentMinuteData[symbol].Clear();
        }
        
        Console.WriteLine($"Data cleared, now collecting for minute {GetCurrentMinute():HH:mm}\n");
    }

    private void DisplayCurrentData()
    {
        lock (_displayLock)
        {
            Console.Clear();
            Console.WriteLine("===============================================");
            Console.WriteLine("        5-Second Continuous Monitor (Kraken)");
            Console.WriteLine("===============================================\n");

            Console.WriteLine($"===============================================================");
            Console.WriteLine($"   Current Minute: {_currentMinute:HH:mm}     >>------<<     {DateTime.Now:HH:mm:ss}");
            Console.WriteLine($"===============================================================");
            Console.WriteLine($" Coin |    Open     |     Low     |    High     |    Close    | % Change | Volatility | Spread  | Samples");
            Console.WriteLine($"------|-------------|-------------|-------------|-------------|----------|------------|---------|--------");

            foreach (var symbol in _krakenPairs.Keys)
            {
                var samples = _currentMinuteData[symbol];

                if (samples.Count != 0)
                {
                    var bar = new MinuteBar();
                    foreach (var sample in samples.OrderBy(s => s.Timestamp))
                    {
                        bar.AddSample(sample.Price, sample.Timestamp);
                    }

                    Console.WriteLine($"{symbol,-5} | ${bar.Open,10:F2} | ${bar.Low,10:F2} | ${bar.High,10:F2} | ${bar.Close,10:F2} | " +
                                    $"{bar.PercentChange,7:F2}% | {bar.Volatility,9:F2}% | ${bar.Spread,6:F2} | {bar.SampleCount,7}");
                }
                else
                {
                    Console.WriteLine($"{symbol,-5} | ${"--",10} | ${"--",10} | ${"--",10} | ${"--",10} | " +
                                    $"{"--",7} | {"--",9} | ${"--",6} | {0,7}");
                }
            }
            Console.WriteLine();
        }
    }

    private async Task LogMinuteBarsToCSV(List<MinuteBar> bars)
    {
        try
        {
            const string csvFile = "prices.csv";
            var csvExists = File.Exists(csvFile);
            
            await using var writer = new StreamWriter(csvFile, append: true);
            
            // Write header if file doesn't exist
            if (!csvExists)
            {
                await writer.WriteLineAsync("Timestamp,Symbol,Open,High,Low,Close,Volume,Samples,PercentChange,Volatility,Spread");
            }

            foreach (var bar in bars)
            {
                var symbol = _krakenPairs.Keys.FirstOrDefault(s => _currentMinuteData[s].Count != 0) ?? "UNKNOWN";
                
                await writer.WriteLineAsync($"{bar.Minute:yyyy-MM-dd HH:mm:ss},{symbol}," +
                                          $"{bar.Open},{bar.High},{bar.Low},{bar.Close},0," +
                                          $"{bar.SampleCount},{bar.PercentChange:F4},{bar.Volatility:F4},{bar.Spread}");
            }
        }
        catch (Exception ex)
        {
            await LogError($"Error writing to CSV: {ex.Message}");
        }
    }

    private async Task LogError(string message)
    {
        try
        {
            var logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}";
            Console.WriteLine($"ERROR: {message}");
            await File.AppendAllTextAsync("errors.log", logEntry + Environment.NewLine);
        }
        catch
        {
            // If we can't log errors, at least print them
            Console.WriteLine($"CRITICAL: Could not log error: {message}");
        }
    }

    public void Stop()
    {
        _isRunning = false;
        _samplingTimer?.Dispose();
        _minuteTimer?.Dispose();
        _krakenClient?.Dispose();
        
        // Process any remaining data
        if (_currentMinuteData.Values.Any(samples => samples.Count != 0))
        {
            ProcessCompletedMinute();
        }
    }
}

class Program
{
    private static CoinMonitor? _monitor;

    static async Task Main(string[] args)
    {
        Console.WriteLine("Starting Crypto Trading Monitor with Kraken...");
        Console.WriteLine("Press Ctrl+C to stop\n");

        // Handle graceful shutdown
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("\nShutting down gracefully...");
            _monitor?.Stop();
            Environment.Exit(0);
        };

        try
        {
            _monitor = new CoinMonitor();
            
            // Keep the application running
            while (true)
            {
                await Task.Delay(1000);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fatal error: {ex.Message}");
            await File.AppendAllTextAsync("errors.log", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - FATAL: {ex.Message}\n{ex.StackTrace}\n");
        }
    }
}
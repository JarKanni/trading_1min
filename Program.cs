using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CryptoClients.Net;
using CryptoClients.Net.Interfaces;
using CryptoExchange.Net.SharedApis;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Authentication;

// Load .env file
var envFile = "/home/trading/sisu/.env";
Console.WriteLine($"Looking for .env file at: {envFile}");

if (File.Exists(envFile))
{
    Console.WriteLine(".env file found! Loading credentials...");
    foreach (var line in File.ReadAllLines(envFile))
    {
        if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("#") && line.Contains("="))
        {
            var parts = line.Split('=', 2);
            if (parts.Length == 2)
            {
                var key = parts[0].Trim();
                var value = parts[1].Trim();
                Environment.SetEnvironmentVariable(key, value);
                Console.WriteLine($"Loaded: {key} = {(key.Contains("SECRET") ? "[HIDDEN]" : value)}");
            }
        }
    }
}
else
{
    Console.WriteLine(".env file NOT found!");
}

var client = new ExchangeRestClient();

// Configure API credentials from .env file
var apiKey = Environment.GetEnvironmentVariable("KRAKEN_API_KEY");
var apiSecret = Environment.GetEnvironmentVariable("KRAKEN_API_SECRET");

Console.WriteLine($"API Key found: {!string.IsNullOrEmpty(apiKey)}");
Console.WriteLine($"API Secret found: {!string.IsNullOrEmpty(apiSecret)}");
Console.WriteLine();

// Print enough newlines to "clear" the visible area
Console.WriteLine(new string('\n', 20));

Console.WriteLine();

// Ask user what they want to see
Console.WriteLine("===============================================");
Console.WriteLine("           Trading Program Options");
Console.WriteLine("===============================================");
Console.WriteLine("1. Show account balance only");
Console.WriteLine("2. Monitor live prices only");
Console.WriteLine("3. Show balance then monitor prices");
Console.WriteLine("===============================================");
Console.Write("Enter your choice (1, 2, or 3): ");

var choice = Console.ReadLine();
Console.WriteLine();

if (choice == "1")
{
    // Show balance only and exit
    if (!string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(apiSecret))
    {
        Console.WriteLine("Fetching account balance...");
        
        try
        {
            client.SetApiCredentials("Kraken", apiKey, apiSecret);
            var balanceResult = await client.GetBalancesAsync(new GetBalancesRequest(), new[] { "Kraken" });
            
            Console.WriteLine("===============================================");
            Console.WriteLine("            Account Balance");
            Console.WriteLine("===============================================");
            
            if (balanceResult.Any() && balanceResult.First().Success)
            {
                foreach (var balance in balanceResult.First().Data)
                {
                    Console.WriteLine($"{balance.Asset,-8} | {balance.Available,15:F8} | {balance.Total,15:F8}");
                }
            }
            else
            {
                Console.WriteLine("Error fetching balance");
            }
            Console.WriteLine("===============================================");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception getting balance: {ex.Message}");
        }
    }
    else
    {
        Console.WriteLine("No API credentials found!");
    }
    return; // Exit program
}
else if (choice == "2")
{
    // Skip balance display, go straight to price monitoring
    Console.WriteLine("Starting price monitoring...");
}
else if (choice == "3")
{
    // Show balance first, then continue to price monitoring
    if (!string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(apiSecret))
    {
        Console.WriteLine("API credentials found. Fetching account balance...");
        
        try
        {
            client.SetApiCredentials("Kraken", apiKey, apiSecret);
            var balanceResult = await client.GetBalancesAsync(new GetBalancesRequest(), new[] { "Kraken" });
            
            Console.WriteLine("===============================================");
            Console.WriteLine("            Account Balance");
            Console.WriteLine("===============================================");
            
            if (balanceResult.Any())
            {
                var krakenResult = balanceResult.First();
                if (krakenResult.Success)
                {
                    Console.WriteLine("Balance fetch successful!");
                    foreach (var balance in krakenResult.Data)
                    {
                        Console.WriteLine($"{balance.Asset,-8} | {balance.Available,15:F8} | {balance.Total,15:F8}");
                    }
                }
                else
                {
                    Console.WriteLine($"Balance fetch failed: {krakenResult.Error?.Message ?? "Unknown error"}");
                }
            }
            else
            {
                Console.WriteLine("No balance results returned");
            }
            Console.WriteLine("===============================================");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception getting balance: {ex.Message}");
            Console.WriteLine("===============================================");
        }
    }
    else
    {
        Console.WriteLine("===============================================");
        Console.WriteLine("        No API Credentials Set");
        Console.WriteLine("===============================================");
        Console.WriteLine("Set KRAKEN_API_KEY and KRAKEN_API_SECRET");
        Console.WriteLine("environment variables for balance data.");
        Console.WriteLine("===============================================");
    }
    
    Console.WriteLine("\nStarting price monitoring in 3 seconds...");
    await Task.Delay(3000);
}
else
{
    Console.WriteLine("Invalid choice. Starting price monitoring...");
}

// Note: Kraken uses USD instead of USDT for most trading pairs
var symbols = new[]
{
    new { Name = "BTC", Symbol = new SharedSymbol(TradingMode.Spot, "BTC", "USD") },
    new { Name = "ETH", Symbol = new SharedSymbol(TradingMode.Spot, "ETH", "USD") },
    new { Name = "XRP", Symbol = new SharedSymbol(TradingMode.Spot, "XRP", "USD") },
    new { Name = "SOL", Symbol = new SharedSymbol(TradingMode.Spot, "SOL", "USD") },
    new { Name = "ADA", Symbol = new SharedSymbol(TradingMode.Spot, "ADA", "USD") }
};

var logFile = "/home/trading/sisu/prices.csv";

// Create CSV header if file doesn't exist
if (!File.Exists(logFile))
{
    await File.WriteAllTextAsync(logFile, "timestamp,coin,low,last,high\n");
}

Console.WriteLine("Press Ctrl+C to stop...");
Console.WriteLine();

while (true)
{
    var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

    Console.WriteLine("\n\n");
    Console.WriteLine("===============================================");
    Console.WriteLine($"    Crypto Trading     >>-----<<     {DateTime.Now:h:mm:sstt}");
    Console.WriteLine("===============================================");
    Console.WriteLine(" Coin |     Low     |     Last    |    High");
    Console.WriteLine("------|-------------|-------------|------------");

    foreach (var crypto in symbols)
    {
        var results = await client.GetSpotTickerAsync(new GetTickerRequest(crypto.Symbol), new[] { "Kraken" });
        
        foreach (var result in results)
        {
            if (result.Success)
            {
                // Display in table format
                Console.WriteLine($"{crypto.Name,-5} | ${result.Data.LowPrice,10:F2} | ${result.Data.LastPrice,10:F2} | ${result.Data.HighPrice,10:F2}");
                
                // Log to CSV file
                var csvLine = $"{timestamp},{crypto.Name},{result.Data.LowPrice:F2},{result.Data.LastPrice:F2},{result.Data.HighPrice:F2}\n";
                await File.AppendAllTextAsync(logFile, csvLine);
            }
            else
            {
                Console.WriteLine($"{crypto.Name,-5} | {"Error",-10} | {"Error",-10} | {"Error",-10}");
            }
        }
    }
    
    Console.WriteLine();
    Console.WriteLine($"Data logged.");
    Console.WriteLine("Press Ctrl+C to stop...");
    
    await Task.Delay(20000); // Wait 20 seconds
}
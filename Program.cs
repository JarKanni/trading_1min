using System;
using System.Threading.Tasks;
using CryptoClients.Net;
using CryptoClients.Net.Interfaces;
using CryptoExchange.Net.SharedApis;
using CryptoExchange.Net.Objects;

var client = new ExchangeRestClient();
var symbols = new[]
{
    new { Name = "BTC", Symbol = new SharedSymbol(TradingMode.Spot, "BTC", "USDT") },
    new { Name = "ETH", Symbol = new SharedSymbol(TradingMode.Spot, "ETH", "USDT") },
    new { Name = "XRP", Symbol = new SharedSymbol(TradingMode.Spot, "XRP", "USDT") },
    new { Name = "SOL", Symbol = new SharedSymbol(TradingMode.Spot, "SOL", "USDT") },
    new { Name = "ADA", Symbol = new SharedSymbol(TradingMode.Spot, "ADA", "USDT") }
};

Console.WriteLine("Press Ctrl+C to stop...");
Console.WriteLine();

while (true)
{
    Console.Clear();
    
    Console.WriteLine("===============================================");
    Console.WriteLine($"    Crypto Trading     >>-----<<     {DateTime.Now:h:mm:sstt}");
    Console.WriteLine("===============================================");
    Console.WriteLine(" Coin |     Low     |     Last    |    High");
    Console.WriteLine("------|-------------|-------------|------------");

    foreach (var crypto in symbols)
    {
        var results = await client.GetSpotTickerAsync(new GetTickerRequest(crypto.Symbol), new[] { "Kucoin" });
       
        foreach (var result in results)
        {
            if (result.Success)
                Console.WriteLine($"{crypto.Name,-5} | ${result.Data.LowPrice,10:F2} | ${result.Data.LastPrice,10:F2} | ${result.Data.HighPrice,10:F2}");
            else
                Console.WriteLine($"{crypto.Name,-5} | {"Error",-10} | {"Error",-10} | {"Error",-10}");
        }
    }
    
    Console.WriteLine();
    Console.WriteLine("Press Ctrl+C to stop...");
    
    await Task.Delay(20000); // Wait 20 seconds
}
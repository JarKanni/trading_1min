using System;
using CryptoClients.Net;
using CryptoClients.Net.Interfaces;
using CryptoExchange.Net.SharedApis;
using CryptoExchange.Net.Objects;

var client = new ExchangeRestClient();
var exchanges = new[] { "OKX", "Kucoin", "CoinEx" };

var symbols = new[]
{
    new { Name = "BTC/USDT", Symbol = new SharedSymbol(TradingMode.Spot, "BTC", "USDT") },
    new { Name = "ETH/USDT", Symbol = new SharedSymbol(TradingMode.Spot, "ETH", "USDT") },
    new { Name = "SOL/USDT", Symbol = new SharedSymbol(TradingMode.Spot, "SOL", "USDT") },
    new { Name = "ADA/USDT", Symbol = new SharedSymbol(TradingMode.Spot, "ADA", "USDT") }
};

Console.WriteLine("================================");
Console.WriteLine($"Crypto Trading - {DateTime.Now:h:mm:sstt}");
Console.WriteLine("================================");

foreach (var crypto in symbols)
{
    var results = await client.GetSpotTickerAsync(new GetTickerRequest(crypto.Symbol), exchanges);
    
    Console.WriteLine($"{crypto.Name}:");
    foreach (var result in results)
    {
        if (result.Success)
            Console.WriteLine($"{result.Exchange}: ${result.Data.LastPrice:F2}");
        else
            Console.WriteLine($"{result.Exchange} error: {result.Error}");
    }
    Console.WriteLine(); // Empty line between symbols
};

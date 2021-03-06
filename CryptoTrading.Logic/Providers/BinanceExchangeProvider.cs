﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using CryptoTrading.Logic.Models;
using CryptoTrading.Logic.Options;
using CryptoTrading.Logic.Providers.Interfaces;
using CryptoTrading.Logic.Providers.Models;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CryptoTrading.Logic.Providers
{
    public class BinanceExchangeProvider : HttpBaseProvider, IExchangeProvider
    {
        private readonly BinanceOptions _binanceOptions;
        private const int RecvWindow = 30000; // 30 seconds to receive the reqest to exchange server

        public BinanceExchangeProvider(IOptions<BinanceOptions> binanceOptions) : base(binanceOptions.Value.ApiUrl)
        {
            _binanceOptions = binanceOptions.Value;
        }

        public async Task<IEnumerable<CandleModel>> GetCandlesAsync(string tradingPair, CandlePeriod candlePeriod, long start, long? end)
        {
            var endTime = end ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var endPointUrl = $"/api/v1/klines?symbol={tradingPair}&interval={(int)candlePeriod}m&startTime={start * 1000}&endTime={(endTime * 1000) - 1}";
            
            using (var client = GetClient())
            {
                using (var response = await client.GetAsync(endPointUrl))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new Exception($"Response code: {response.StatusCode}, body: {response.Content.ReadAsStringAsync().Result}");
                    }

                    var resultResponseContent = await response.Content.ReadAsStringAsync();
                    var candles = new List<CandleModel>();
                    if (resultResponseContent == "null")
                    {
                        return candles;
                    }
                    var result = JsonConvert.DeserializeObject<object[]>(resultResponseContent).ToList();
                    foreach (var item in result)
                    {
                        var arrayResult = JsonConvert.DeserializeObject<object[]>(item.ToString());
                        candles.Add(new CandleModel
                        {
                            StartDateTime = DateTimeOffset.FromUnixTimeSeconds((long)arrayResult[0] / 1000).DateTime,
                            OpenPrice = decimal.Parse(arrayResult[1].ToString(), NumberStyles.Any, CultureInfo.InvariantCulture),
                            HighPrice = decimal.Parse(arrayResult[2].ToString(), NumberStyles.Any, CultureInfo.InvariantCulture),
                            LowPrice = decimal.Parse(arrayResult[3].ToString(), NumberStyles.Any, CultureInfo.InvariantCulture),
                            ClosePrice = decimal.Parse(arrayResult[4].ToString(), NumberStyles.Any, CultureInfo.InvariantCulture),
                            Volume = decimal.Parse(arrayResult[5].ToString(), NumberStyles.Any, CultureInfo.InvariantCulture)
                        });
                    }
                    
                    return candles;
                }
            }
        }

        public async Task<long> CreateOrderAsync(TradeType tradeType, string tradingPair, decimal rate, decimal amount)
        {
            var createdTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var formParameters = $"symbol={tradingPair}" +
                                 $"&side={tradeType.ToString().ToUpper()}" +
                                 "&type=LIMIT" +
                                 "&timeInForce=GTC" +
                                 $"&quantity={Math.Round(amount, 5).ToString(CultureInfo.InvariantCulture)}" +
                                 $"&price={Math.Round(rate, 2).ToString(CultureInfo.InvariantCulture)}" +
                                 $"&recvWindow={RecvWindow}" +
                                 $"&timestamp={createdTs}";

            using (var client = GetClient())
            {
                client.DefaultRequestHeaders.Add("X-MBX-APIKEY", _binanceOptions.ApiKey);
                var signature = SignPostBody(formParameters);

                var requestBody = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("symbol", tradingPair),
                    new KeyValuePair<string, string>("side", tradeType.ToString().ToUpper()),
                    new KeyValuePair<string, string>("type", "LIMIT"),
                    new KeyValuePair<string, string>("timeInForce", "GTC"),
                    new KeyValuePair<string, string>("quantity", Math.Round(amount, 5).ToString(CultureInfo.InvariantCulture)),
                    new KeyValuePair<string, string>("price", Math.Round(rate, 2).ToString(CultureInfo.InvariantCulture)),
                    new KeyValuePair<string, string>("recvWindow", RecvWindow.ToString()),
                    new KeyValuePair<string, string>("timestamp", createdTs.ToString()),
                    new KeyValuePair<string, string>("signature", signature.ToLower())
                });

                using (var response = await client.PostAsync("/api/v3/order", requestBody))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new Exception($"Response code: {response.StatusCode}, body: {response.Content.ReadAsStringAsync().Result}");
                    }

                    var responseDefinition = new { OrderId = long.MaxValue };
                    var responseContent = await response.Content.ReadAsStringAsync();
                    if (responseContent.ToLower().Contains("error"))
                    {
                        Console.WriteLine($"Create order error: Error: {responseContent}");
                        return -1;
                    }
                    var deserializedResponse = JsonConvert.DeserializeAnonymousType(responseContent, responseDefinition);
                    return deserializedResponse.OrderId;
                }
            }
        }

        public async Task<bool> CancelOrderAsync(string tradingPair, long orderNumber)
        {
            var createdTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var formParameters = $"symbol={tradingPair}" +
                                 $"&orderId={orderNumber}" +
                                 $"&recvWindow={RecvWindow}" +
                                 $"&timestamp={createdTs}";

            using (var client = GetClient())
            {
                client.DefaultRequestHeaders.Add("X-MBX-APIKEY", _binanceOptions.ApiKey);
                var signature = SignPostBody(formParameters);

                using (var response = await client.DeleteAsync($"/api/v3/order?{formParameters}&signature={signature}"))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new Exception($"Response code: {response.StatusCode}, body: {response.Content.ReadAsStringAsync().Result}");
                    }

                    var responseContent = await response.Content.ReadAsStringAsync();
                    if (responseContent.ToLower().Contains("error"))
                    {
                        Console.WriteLine($"Create order error: Error: {responseContent}");
                        return false;
                    }
                    return true;
                }
            }
        }

        public async Task<IEnumerable<OrderDetail>> GetOrderAsync(string tradingPair, long orderNumber)
        {
            var createdTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var formParameters = $"symbol={tradingPair}" +
                                 $"&orderId={orderNumber}" +
                                 $"&recvWindow={RecvWindow}" +
                                 $"&timestamp={createdTs}";

            using (var client = GetClient())
            {
                client.DefaultRequestHeaders.Add("X-MBX-APIKEY", _binanceOptions.ApiKey);
                var signature = SignPostBody(formParameters);

                using (var response = await client.GetAsync($"/api/v3/order?{formParameters}&signature={signature}"))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new Exception($"Response code: {response.StatusCode}, body: {response.Content.ReadAsStringAsync().Result}");
                    }

                    var responseDefinition = new { OrderId = long.MaxValue, Status = BinanceStatus.New, executedQty = decimal.MaxValue };
                    var responseContent = await response.Content.ReadAsStringAsync();
                    if (responseContent.ToLower().Contains("error"))
                    {
                        Console.WriteLine($"Create order error: Error: {responseContent}");
                        return null;
                    }
                    var deserializedResponse = JsonConvert.DeserializeAnonymousType(responseContent, responseDefinition);
                    if (deserializedResponse.Status != BinanceStatus.Filled)
                    {
                        return null;
                    }
                    var freeBalance = await GetBalanceAsync(tradingPair);
                    return new List<OrderDetail>
                        {
                            new OrderDetail
                            {
                                CurrencyPair = tradingPair,
                                Rate = Math.Floor(freeBalance * 100000) / 100000
                            }
                        };
                }
            }
        }

        private async Task<decimal> GetBalanceAsync(string tradingPair)
        {
            var createdTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var formParameters = $"&recvWindow={RecvWindow}" +
                                 $"&timestamp={createdTs}";

            using (var client = GetClient())
            {
                client.DefaultRequestHeaders.Add("X-MBX-APIKEY", _binanceOptions.ApiKey);
                var signature = SignPostBody(formParameters);

                using (var response = await client.GetAsync($"/api/v3/account?{formParameters}&signature={signature}"))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new Exception($"Response code: {response.StatusCode}, body: {response.Content.ReadAsStringAsync().Result}");
                    }

                    var responseDefinition = new { Balances = new List<BalanceItem>() };
                    var responseContent = await response.Content.ReadAsStringAsync();
                    if (responseContent.ToLower().Contains("error"))
                    {
                        Console.WriteLine($"Error: {responseContent}");
                        return -1;
                    }
                    var deserializedResponse = JsonConvert.DeserializeAnonymousType(responseContent, responseDefinition);
                    var balanceItem = deserializedResponse.Balances.FirstOrDefault(w => w.Asset == tradingPair.ToUpper().Replace("USDT", ""));
                    return balanceItem?.FreeBalance ?? -1;
                }
            }
        }

        public async Task<Ticker> GetTicker(string tradingPair)
        {
            using (var client = GetClient())
            {
                using (var response = await client.GetAsync($"/api/v3/ticker/bookTicker?symbol={tradingPair}"))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new Exception($"Response code: {response.StatusCode}, body: {response.Content.ReadAsStringAsync().Result}");
                    }

                    var responseDefinition = new { Symbol = "", BidPrice = (decimal)0.0, AskPrice = (decimal)0.0 };
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var deserializedResponse = JsonConvert.DeserializeAnonymousType(responseContent, responseDefinition);
                    return new Ticker
                    {
                        HighestBid = deserializedResponse.BidPrice,
                        LowestAsk = deserializedResponse.AskPrice
                    };
                }
            }
        }

        public async Task<DepthModel> GetDepth(string tradingPair)
        {
            using (var client = GetClient())
            {
                using (var response = await client.GetAsync($"/api/v1/depth?symbol={tradingPair}"))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new Exception($"Response code: {response.StatusCode}, body: {response.Content.ReadAsStringAsync().Result}");
                    }

                    var depthModel = new DepthModel
                    {
                        Bids = new List<PriceRateModel>(),
                        Asks = new List<PriceRateModel>()
                    };
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var obj = (JObject)JsonConvert.DeserializeObject(responseContent);
                    var bidArray = obj["bids"].ToArray();
                    foreach (var bid in bidArray)
                    {
                        var bidPriceAndRate = bid.ToArray();
                        depthModel.Bids.Add(new PriceRateModel
                        {
                            Price = (decimal)bidPriceAndRate[0],
                            Quantity = (decimal)bidPriceAndRate[1]
                        });
                    }
                    var askArray = obj["asks"].ToArray();
                    foreach (var ask in askArray)
                    {
                        var askPriceAndRate = ask.ToArray();
                        depthModel.Asks.Add(new PriceRateModel
                        {
                            Price = (decimal)askPriceAndRate[0],
                            Quantity = (decimal)askPriceAndRate[1]
                        });
                    }
                    return depthModel;
                }
            }
        }

        private string SignPostBody(string formParameters)
        {
            var hmacHash = new HMACSHA256(Encoding.UTF8.GetBytes(_binanceOptions.ApiSecret));
            var signedParametersByteArray = hmacHash.ComputeHash(Encoding.UTF8.GetBytes(formParameters));
            return BitConverter.ToString(signedParametersByteArray).Replace("-", "");
        }
    }

    public class BalanceItem
    {
        public string Asset { get; set; }
        [JsonProperty("free")]
        public decimal FreeBalance { get; set; }
    }

    public enum BinanceStatus
    {
        New,
        PartiallyFilled,
        Filled,
        Canceled,
        PendingCancel,
        Rejected,
        Expired
    }
}

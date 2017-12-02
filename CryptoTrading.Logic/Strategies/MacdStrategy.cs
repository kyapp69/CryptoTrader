﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CryptoTrading.Logic.Indicators.Interfaces;
using CryptoTrading.Logic.Models;
using CryptoTrading.Logic.Options;
using CryptoTrading.Logic.Strategies.Interfaces;
using Microsoft.Extensions.Options;

namespace CryptoTrading.Logic.Strategies
{
    public class MacdStrategy : IStrategy
    {
        private readonly IIndicator _shortEmaIndicator;
        private readonly IIndicator _longEmaIndicator;
        private readonly IIndicator _signalEmaIndicator;

        private TrendDirection _lastTrend = TrendDirection.Short;
        private int _persistenceSellCount = 1;
        private decimal _maxOrMinMacd;
        private decimal? _lastMacd;

        public MacdStrategy(IOptions<MacdStrategyOptions> options, IIndicatorFactory indicatorFactory)
        {
            _shortEmaIndicator = indicatorFactory.GetEmaIndicator(options.Value.ShortWeight);
            _longEmaIndicator = indicatorFactory.GetEmaIndicator(options.Value.LongWeight);
            _signalEmaIndicator = indicatorFactory.GetEmaIndicator(options.Value.Signal);
        }

        public int CandleSize => 1;

        public async Task<TrendDirection> CheckTrendAsync(List<CandleModel> previousCandles, CandleModel currentCandle)
        {
            var shortEmaValue = _shortEmaIndicator.GetIndicatorValue(currentCandle).IndicatorValue;
            var longEmaValue = _longEmaIndicator.GetIndicatorValue(currentCandle).IndicatorValue;
            var emaDiffValue = shortEmaValue - longEmaValue;
            var signalEmaValue = Math.Round(_signalEmaIndicator.GetIndicatorValue(emaDiffValue).IndicatorValue, 4);
            var macdValue = Math.Round(emaDiffValue - signalEmaValue, 4);

            Console.WriteLine($"DateTs: {currentCandle.StartDateTime:s}; " +
                              $"MACD: {macdValue};" +
                              $"Close price: {currentCandle.ClosePrice}; ");

            if (!_lastMacd.HasValue)
            {
                _lastMacd = macdValue;
                return await Task.FromResult(TrendDirection.None);
            }

            if (_lastTrend == TrendDirection.Short)
            {
                if (macdValue < 0 && macdValue < _lastMacd)
                {
                    _maxOrMinMacd = macdValue;
                }

                _lastMacd = macdValue;
                var diffPreviousMacd = Math.Abs(_maxOrMinMacd - macdValue);
                if (macdValue < 0 && diffPreviousMacd > 1)
                {
                    _lastTrend = TrendDirection.Long;
                    _maxOrMinMacd = 0;
                }
                else
                {
                    return await Task.FromResult(TrendDirection.None);
                }
            }
            else if (_lastTrend == TrendDirection.Long)
            {
                if (macdValue > 0 && macdValue > _lastMacd)
                {
                    _maxOrMinMacd = macdValue;
                }

                _lastMacd = macdValue;
                var diffPreviousMacd = Math.Abs(_maxOrMinMacd - macdValue);
                if (macdValue > 0 && diffPreviousMacd > 1)
                {
                    _lastTrend = TrendDirection.Short;
                    _maxOrMinMacd = 0;
                }
                else
                {
                    return await Task.FromResult(TrendDirection.None);
                }
            }

            return await Task.FromResult(_lastTrend);
        }
    }
}
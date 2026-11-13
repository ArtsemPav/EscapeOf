using UnityEngine;

namespace EscapeOf.Puzzle.Laptop
{
    /// <summary>
    /// Monte-Carlo RTP estimator for a LaptopSlotMachineFile config.
    /// Mirrors the exact evaluation logic of the runtime game (lines, wilds, scatter, free spins with retrigger).
    /// </summary>
    public class SlotMachineRtpEstimator
    {
        private readonly LaptopSlotMachineFile _config;
        private readonly string[][] _strips;
        private uint _rngState;

        /// <param name="config">Slot machine configuration asset to estimate.</param>
        public SlotMachineRtpEstimator(LaptopSlotMachineFile config)
        {
            _config = config;
            _strips = new string[3][];
            _strips[0] = config.Reel0.ToArray();
            _strips[1] = config.Reel1.ToArray();
            _strips[2] = config.Reel2.ToArray();
        }

        /// <summary>
        /// Runs a Monte-Carlo simulation and returns RTP (total return / total bet).
        /// </summary>
        /// <param name="spins">Number of base-game spins to simulate.</param>
        /// <param name="seed">RNG seed for reproducibility.</param>
        public float Estimate(int spins, uint seed)
        {
            _rngState = seed == 0 ? 1u : seed;
            long totalBet = 0;
            double totalReturn = 0;

            for (var i = 0; i < spins; i++)
            {
                totalBet += _config.TotalBet;
                var result = PlaySpin(1);
                totalReturn += result.win;
                if (result.bonusTriggered)
                {
                    var remaining = _config.FreeSpinsCount;
                    while (remaining > 0)
                    {
                        remaining--;
                        var freeSpin = PlaySpin(_config.FreeSpinsMultiplier);
                        totalReturn += freeSpin.win;
                        if (freeSpin.bonusTriggered)
                            remaining += _config.FreeSpinsCount; // retrigger
                    }
                }
            }
            return (float)(totalReturn / totalBet);
        }

        private (double win, bool bonusTriggered) PlaySpin(int multiplier)
        {
            // grid[reel][row]: row 0 = top, 1 = center, 2 = bottom
            var grid = new string[3][];
            for (var reel = 0; reel < 3; reel++)
            {
                var strip = _strips[reel];
                var stop = (int)(NextRandom() % (uint)strip.Length);
                grid[reel] = new[]
                {
                    strip[stop],
                    strip[(stop + 1) % strip.Length],
                    strip[(stop + 2) % strip.Length]
                };
            }

            var lineWinTotal = 0;
            for (var row = 0; row < 3; row++)
                lineWinTotal += EvaluateLine(grid[0][row], grid[1][row], grid[2][row]);

            var scatterCount = CountSymbol(grid, _config.ScatterSymbolId);
            var scatterWin = scatterCount >= _config.ScatterMinCount
                ? _config.ScatterPayXTotalBet * _config.TotalBet
                : 0;

            var bonusCount = CountSymbol(grid, _config.BonusSymbolId);
            var bonusTriggered = bonusCount >= _config.BonusMinCount;
            // Scatter win also launches the feature (same as the runtime window).
            var scatterTriggered = scatterWin > 0;

            return ((lineWinTotal + scatterWin) * multiplier, bonusTriggered || scatterTriggered);
        }

        /// <summary>Evaluates one 3-cell line (left to right, wilds substitute). Returns win x line bet.</summary>
        private int EvaluateLine(string c0, string c1, string c2)
        {
            const string wild = "WILD";
            if (c0 == wild && c1 == wild && c2 == wild)
                return _config.TryGetPayout(wild, out var wildPay, out _) ? wildPay : 0;

            var baseSymbol = c0 == wild ? (c1 == wild ? c2 : c1) : c0;

            var count = 1;
            if (c1 == wild || c1 == baseSymbol) count = 2;
            if (count == 2 && (c2 == wild || c2 == baseSymbol)) count = 3;

            if (!_config.TryGetPayout(baseSymbol, out var pay3, out var pay2))
                return 0;
            return count == 3 ? pay3 : count == 2 ? pay2 : 0;
        }

        private int CountSymbol(string[][] grid, string symbolId)
        {
            var count = 0;
            for (var reel = 0; reel < 3; reel++)
                for (var row = 0; row < 3; row++)
                    if (grid[reel][row] == symbolId) count++;
            return count;
        }

        private uint NextRandom()
        {
            _rngState = _rngState * 1103515245u + 12345u;
            return _rngState;
        }
    }
}

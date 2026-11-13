using System.Collections.Generic;
using UnityEngine;

namespace EscapeOf.Puzzle.Laptop
{
    /// <summary>A symbol that can appear on a slot machine reel.</summary>
    [System.Serializable]
    public class SlotSymbol
    {
        [Tooltip("Internal id used by the reel strips and paytable (e.g. SEVEN, WILD).")]
        public string id;

        [Tooltip("TMP glyph or short text shown on the reel cell until real sprites are made.")]
        public string glyph;

        [Tooltip("Cell text color.")]
        public Color color = Color.white;

        [Tooltip("Optional sprite replacing the glyph once art is ready.")]
        public Sprite sprite;
    }

    /// <summary>Payout entry for a line result.</summary>
    [System.Serializable]
    public class SlotPayEntry
    {
        [Tooltip("Symbol id this payout applies to.")]
        public string symbolId;

        [Tooltip("Payout multiplier for 3 of a kind on a line (x line bet).")]
        public int pay3;

        [Tooltip("Payout multiplier for 2 of a kind from the leftmost reel (x line bet). 0 = no 2-of-a-kind pay.")]
        public int pay2;
    }

    /// <summary>
    /// Configuration asset for the fake slot machine minigame (opened from the nurse laptop desktop).
    /// Holds symbol definitions, per-reel strips, paytable, scatter and bonus rules.
    /// Strip composition defines exact probabilities: P(symbol) = count(symbol) / strip length.
    /// </summary>
    [CreateAssetMenu(fileName = "SlotMachineConfig", menuName = "Laptop/Slot Machine Config")]
    public class LaptopSlotMachineFile : LaptopFileData
    {
        [Header("Symbols")]
        [Tooltip("All symbols that can appear on reels.")]
        [SerializeField] private List<SlotSymbol> _symbols = new List<SlotSymbol>();

        [Header("Reel Strips (probability source)")]
        [Tooltip("Ordered symbol ids for reel 0. Probability = occurrences / length.")]
        [SerializeField] private List<string> _reel0 = new List<string>();
        [Tooltip("Ordered symbol ids for reel 1.")]
        [SerializeField] private List<string> _reel1 = new List<string>();
        [Tooltip("Ordered symbol ids for reel 2.")]
        [SerializeField] private List<string> _reel2 = new List<string>();

        [Header("Paytable (multipliers of the line bet)")]
        [SerializeField] private List<SlotPayEntry> _paytable = new List<SlotPayEntry>();

        [Header("Scatter")]
        [Tooltip("Symbol id that pays anywhere when it lands this many times.")]
        [SerializeField] private string _scatterSymbolId = "STAR";
        [Tooltip("Minimum scatter count to pay.")]
        [SerializeField] private int _scatterMinCount = 3;
        [Tooltip("Scatter payout, multiplier of the TOTAL bet (all lines).")]
        [SerializeField] private int _scatterPayXTotalBet = 2;

        [Header("Bonus (Free Spins)")]
        [Tooltip("Symbol id that triggers the free spins round.")]
        [SerializeField] private string _bonusSymbolId = "BONUS";
        [Tooltip("Minimum bonus symbols to trigger free spins.")]
        [SerializeField] private int _bonusMinCount = 3;
        [SerializeField] private int _freeSpinsCount = 10;
        [SerializeField] private int _freeSpinsMultiplier = 2;

        [Header("Economy")]
        [SerializeField] private int _startingCredits = 100;
        [SerializeField] private int _betPerLine = 1;
        [SerializeField] private int _lineCount = 3;

        public List<SlotSymbol> Symbols => _symbols;
        public List<string> Reel0 => _reel0;
        public List<string> Reel1 => _reel1;
        public List<string> Reel2 => _reel2;
        public List<SlotPayEntry> Paytable => _paytable;
        public string ScatterSymbolId => _scatterSymbolId;
        public int ScatterMinCount => _scatterMinCount;
        public int ScatterPayXTotalBet => _scatterPayXTotalBet;
        public string BonusSymbolId => _bonusSymbolId;
        public int BonusMinCount => _bonusMinCount;
        public int FreeSpinsCount => _freeSpinsCount;
        public int FreeSpinsMultiplier => _freeSpinsMultiplier;
        public int StartingCredits => _startingCredits;
        public int BetPerLine => _betPerLine;
        public int LineCount => _lineCount;
        public int TotalBet => _betPerLine * _lineCount;

        /// <summary>Finds a symbol definition by id, null if missing.</summary>
        public SlotSymbol GetSymbol(string id)
        {
            foreach (var s in _symbols)
                if (s.id == id) return s;
            return null;
        }

        /// <summary>Returns payout multipliers (pay3, pay2) for a symbol id.</summary>
        public bool TryGetPayout(string symbolId, out int pay3, out int pay2)
        {
            foreach (var e in _paytable)
            {
                if (e.symbolId == symbolId) { pay3 = e.pay3; pay2 = e.pay2; return true; }
            }
            pay3 = 0; pay2 = 0;
            return false;
        }

#if UNITY_EDITOR
        /// <summary>Monte-Carlo RTP estimate over the current strips/paytable (about a second).</summary>
        [ContextMenu("Estimate RTP (1M spins)")]
        private void EstimateRtp()
        {
            var estimator = new SlotMachineRtpEstimator(this);
            var rtp = estimator.Estimate(1000000, 123456789);
            Debug.Log($"[{name}] RTP estimate over 1,000,000 spins: {rtp * 100f:F2}%", this);
        }
#endif
    }
}

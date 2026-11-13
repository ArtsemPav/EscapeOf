using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace EscapeOf.Puzzle.Laptop
{
    /// <summary>
    /// Fake slot machine minigame window inside the laptop OS. 3x3 grid, 3 paylines (top/center/bottom),
    /// wild substitution, scatter pays, free spins bonus round with retrigger.
    /// Each reel visually scrolls its strip through a masked window with ease-out deceleration and a landing bounce.
    /// Symbol cells show the configured sprite, or the glyph text as a fallback. Balance persists via ISaveable.
    /// </summary>
    public class LaptopSlotMachineWindow : LaptopWindow, ISaveable
    {
        private const int ReelCount = 3;
        private const int RowsPerReel = 3;
        private const float CellSize = 130f;
        private const float SpinDuration = 1.6f;
        private const float StopStagger = 0.45f;
        private const float BrakeCells = 2.5f;
        private const float WinCountDuration = 0.8f;
        private const float BounceHeight = 18f;
        private const float BounceDuration = 0.18f;
        private const int SpinLoops = 2;

        [Header("Reel References")]
        [Tooltip("Masked windows of the three reels; strip contents are built inside at runtime.")]
        [SerializeField] private RectTransform[] _reelViewports;

        [Header("Game UI")]
        [SerializeField] private TMP_Text _balanceText;
        [SerializeField] private TMP_Text _betText;
        [SerializeField] private TMP_Text _messageText;
        [SerializeField] private TMP_Text _winText;
        [SerializeField] private Button _spinButton;
        [SerializeField] private Button _increaseBetButton;
        [SerializeField] private Button _decreaseBetButton;
        [Tooltip("Opens the paytable popup.")]
        [SerializeField] private Button _paytableButton;

        [Header("Popup")]
        [Tooltip("Dimmed fullscreen overlay shown on wins / bonus.")]
        [SerializeField] private Image _popupOverlay;
        [SerializeField] private TMP_Text _popupTitle;
        [SerializeField] private TMP_Text _popupSubtitle;

        [Header("Economy")]
        [Tooltip("Possible line bets the player can cycle through.")]
        [SerializeField] private int[] _betOptions = { 1, 2, 5 };

        private LaptopSlotMachineFile _config;
        private string[][] _strips;
        private RectTransform[] _reelStrips;
        private int _credits;
        private int _betIndex;
        private bool _isSpinning;
        private bool _inFreeSpins;
        private int _freeSpinsRemaining;
        private Coroutine _spinRoutine;
        private string _lastWinDetails = string.Empty;

        public string SaveId => "slotmachine." + (FileData != null ? FileData.fileId : name);

        /// <summary>Current line bet selected by the player (cycles through _betOptions).</summary>
        public int CurrentBet => _betOptions != null && _betOptions.Length > 0 ? _betOptions[Mathf.Clamp(_betIndex, 0, _betOptions.Length - 1)] : (_config != null ? _config.BetPerLine : 1);

        protected override void OnOpen(LaptopFileData file)
        {
            _config = file as LaptopSlotMachineFile;
            if (_config == null)
            {
                Debug.LogError($"[LaptopSlotMachineWindow] {file.name} is not a LaptopSlotMachineFile.", this);
                return;
            }

            _strips = new string[ReelCount][];
            _strips[0] = _config.Reel0.ToArray();
            _strips[1] = _config.Reel1.ToArray();
            _strips[2] = _config.Reel2.ToArray();

            _credits = _config.StartingCredits;
            _betIndex = 0;

            BuildReels();
            RefreshHud();

            if (_spinButton != null) _spinButton.onClick.AddListener(HandleSpinClicked);
            if (_increaseBetButton != null) _increaseBetButton.onClick.AddListener(() => ChangeBet(1));
            if (_decreaseBetButton != null) _decreaseBetButton.onClick.AddListener(() => ChangeBet(-1));
            if (_paytableButton != null) _paytableButton.onClick.AddListener(HandlePaytableClicked);
        }

        protected override void OnClose()
        {
            if (_spinRoutine != null)
            {
                StopCoroutine(_spinRoutine);
                _spinRoutine = null;
            }
            _isSpinning = false;

            if (_popupOverlay != null) _popupOverlay.gameObject.SetActive(false);

            if (_spinButton != null) _spinButton.onClick.RemoveListener(HandleSpinClicked);
            if (_increaseBetButton != null) _increaseBetButton.onClick.RemoveAllListeners();
            if (_decreaseBetButton != null) _decreaseBetButton.onClick.RemoveAllListeners();
            if (_paytableButton != null) _paytableButton.onClick.RemoveAllListeners();

            SaveManager.Instance?.Save();
        }

        /// <summary>
        /// Builds each reel: a tall "strip" RectTransform inside the masked viewport holding the strip
        /// symbols twice (2N cells, seamless wrap) as sprite cards with a glyph-text fallback.
        /// </summary>
        private void BuildReels()
        {
            _reelStrips = new RectTransform[ReelCount];
            var stripLength = _strips[0].Length;

            for (var reel = 0; reel < ReelCount; reel++)
            {
                var viewport = _reelViewports[reel];
                for (var i = viewport.childCount - 1; i >= 0; i--)
                    Destroy(viewport.GetChild(i).gameObject);

                var stripRect = new GameObject("Strip", typeof(RectTransform)).GetComponent<RectTransform>();
                stripRect.SetParent(viewport, false);
                stripRect.anchorMin = new Vector2(0.5f, 1f);
                stripRect.anchorMax = new Vector2(0.5f, 1f);
                stripRect.pivot = new Vector2(0.5f, 1f);
                stripRect.sizeDelta = new Vector2(CellSize, stripLength * 2 * CellSize);
                stripRect.anchoredPosition = Vector2.zero;
                _reelStrips[reel] = stripRect;

                for (var i = 0; i < stripLength * 2; i++)
                {
                    var symbol = _config.GetSymbol(_strips[reel][i % stripLength]);
                    BuildCell(stripRect, i, symbol);
                }
            }
        }

        private void BuildCell(RectTransform stripRect, int index, SlotSymbol symbol)
        {
            var cell = new GameObject($"Cell{index}", typeof(RectTransform), typeof(Image));
            var cellRect = cell.GetComponent<RectTransform>();
            cellRect.SetParent(stripRect, false);
            cellRect.anchorMin = new Vector2(0.5f, 1f);
            cellRect.anchorMax = new Vector2(0.5f, 1f);
            cellRect.pivot = new Vector2(0.5f, 1f);
            cellRect.sizeDelta = new Vector2(CellSize, CellSize);
            // Cell top edge sits exactly at index * CellSize below the strip top, so at rest
            // (strip y = stop * CellSize) rows align flush with the mask edges: no cut rows.
            cellRect.anchoredPosition = new Vector2(0, -index * CellSize);

            var image = cell.GetComponent<Image>();
            if (symbol?.sprite != null)
                image.sprite = symbol.sprite;
            else
                image.color = new Color(0.15f, 0.15f, 0.2f, 1f);

            var labelObject = new GameObject("Glyph", typeof(RectTransform), typeof(TextMeshProUGUI));
            var labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.SetParent(cellRect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.sizeDelta = Vector2.zero;
            labelRect.anchoredPosition = Vector2.zero;

            var label = labelObject.GetComponent<TextMeshProUGUI>();
            label.text = symbol?.glyph ?? "?";
            label.fontSize = 56;
            label.alignment = TextAlignmentOptions.Center;
            label.color = symbol != null ? symbol.color : Color.white;
            label.raycastTarget = false;
            // Glyph is only needed while there is no sprite art for the symbol.
            label.gameObject.SetActive(symbol?.sprite == null);
        }

        private void HandleSpinClicked()
        {
            if (_isSpinning || _config == null) return;

            var totalBet = CurrentBet * _config.LineCount;
            if (_credits < totalBet)
            {
                SetMessage("Not enough credits");
                return;
            }

            _credits -= totalBet;
            RefreshHud();
            SetMessage(string.Empty);
            SetWinText(string.Empty);

            _isSpinning = true;
            SetSpinButtonInteractable(false);

            var stops = new int[ReelCount];
            for (var reel = 0; reel < ReelCount; reel++)
                stops[reel] = Random.Range(0, _strips[reel].Length);

            _spinRoutine = StartCoroutine(SpinRoutine(stops));
        }

        /// <summary>
        /// Spins the reels, evaluates the result, then plays the free-spins round
        /// automatically (with retrigger) when bonus symbols landed.
        /// </summary>
        private IEnumerator SpinRoutine(int[] stops)
        {
            // All reels spin together, each stops on its own: left to right.
            yield return AnimateReels(stops);

            var spin = EvaluateSpin(stops);
            var freeSpinTotal = 0;

            // Scatter AND bonus symbols both launch the free-spins round (BGaming model).
            var featureTriggered = spin.bonusTriggered || spin.scatterTriggered;

            if (featureTriggered)
            {
                _inFreeSpins = true;
                _freeSpinsRemaining = _config.FreeSpinsCount;
                var triggerText = spin.scatterTriggered && spin.bonusTriggered
                    ? "SCATTER + BONUS!"
                    : spin.scatterTriggered ? "SCATTER WIN!" : "BONUS!";
                yield return ShowPopup(triggerText, $"{_config.FreeSpinsCount} free spins x{_config.FreeSpinsMultiplier}");
                SetMessage($"FREE SPINS: {_freeSpinsRemaining} left (x{_config.FreeSpinsMultiplier})");
            }
            else if (spin.totalWin > 0)
            {
                yield return CountUpBalance(_credits - spin.totalWin, _credits);
                yield return ShowPopup("YOU WIN!", $"+{spin.totalWin} credits\n\n{_lastWinDetails}");
            }

            while (_inFreeSpins && _freeSpinsRemaining > 0)
            {
                _freeSpinsRemaining--;
                SetMessage($"FREE SPINS: {_freeSpinsRemaining} left (x{_config.FreeSpinsMultiplier})");
                RefreshHud();
                yield return new WaitForSeconds(0.6f);

                for (var reel = 0; reel < ReelCount; reel++)
                    stops[reel] = Random.Range(0, _strips[reel].Length);

                yield return AnimateReels(stops);

                spin = EvaluateSpin(stops);
                freeSpinTotal += spin.totalWin;

                // Scatter AND bonus both retrigger during the feature.
                if (spin.scatterTriggered || spin.bonusTriggered)
                {
                    _freeSpinsRemaining += _config.FreeSpinsCount; // retrigger
                    yield return ShowPopup("RETRIGGER!", $"+{_config.FreeSpinsCount} free spins");
                }
            }

            if (_inFreeSpins)
            {
                yield return ShowPopup("BONUS COMPLETE!", $"+{freeSpinTotal} credits");
                _inFreeSpins = false;
            }

            _isSpinning = false;
            SetSpinButtonInteractable(true);
            _spinRoutine = null;
        }

        /// <summary>Animates all reels in parallel (used during free spins).</summary>
        private IEnumerator AnimateReels(int[] stops)
        {
            var routines = new Coroutine[ReelCount];
            for (var reel = 0; reel < ReelCount; reel++)
                routines[reel] = StartCoroutine(AnimateReel(reel, stops[reel]));
            foreach (var routine in routines)
                yield return routine;
        }

        /// <summary>Animates the balance counter from the old value to the new one (win count-up).</summary>
        private IEnumerator CountUpBalance(int from, int to)
        {
            var elapsed = 0f;
            while (elapsed < WinCountDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / WinCountDuration);
                // Ease-out so the counter slows down as it approaches the final value.
                var eased = 1f - (1f - t) * (1f - t);
                if (_balanceText != null)
                    _balanceText.text = $"CREDITS: {Mathf.RoundToInt(Mathf.Lerp(from, to, eased))}";
                yield return null;
            }
            RefreshHud();
        }

        /// <summary>
        /// Shows the popup overlay with a title/subtitle and waits for a click before hiding it.
        /// </summary>
        private IEnumerator ShowPopup(string title, string subtitle)
        {
            if (_popupOverlay == null)
            {
                // No overlay wired — fall back to the message line so the info is not lost.
                SetMessage($"{title} {subtitle}");
                yield break;
            }

            if (_popupTitle != null) _popupTitle.text = title;
            if (_popupSubtitle != null) _popupSubtitle.text = subtitle;

            // Guarantee the popup renders above every other element of the window.
            _popupOverlay.transform.SetAsLastSibling();
            _popupOverlay.gameObject.SetActive(true);
            yield return WaitForPopupClick();
            _popupOverlay.gameObject.SetActive(false);
        }

        /// <summary>Waits until the player clicks anywhere over the popup.</summary>
        private IEnumerator WaitForPopupClick()
        {
            // Skip the frame the popup appeared on so the closing click is not consumed instantly.
            yield return null;
            while (!Mouse.current.leftButton.wasPressedThisFrame && !Mouse.current.rightButton.wasPressedThisFrame)
                yield return null;
        }

        /// <summary>
        /// Scrolls the reel at constant speed (like a real slot), then brakes over the last cells and
        /// lands on <paramref name="targetStop"/>. Each reel spins for <see cref="SpinDuration"/> plus a
        /// per-reel <see cref="StopStagger"/> offset (left reel stops first), then bounces.
        /// </summary>
        private IEnumerator AnimateReel(int reelIndex, int targetStop)
        {
            var stripRect = _reelStrips[reelIndex];
            var stripLength = _strips[reelIndex].Length;
            var duration = SpinDuration + reelIndex * StopStagger;

            // Travel in cells: full loops plus enough to land exactly on the target stop.
            var currentStop = Mathf.RoundToInt(stripRect.anchoredPosition.y / CellSize) % stripLength;
            var travel = SpinLoops * stripLength + ((targetStop - currentStop) % stripLength + stripLength) % stripLength;

            // Real-slot motion: constant fast scroll for most of the spin,
            // then a short deceleration over the last StopCells cells (BGaming model).
            var elapsed = 0f;
            var previousTravel = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                var currentTravel = travel * t;
                var speed = (currentTravel - previousTravel) / Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
                previousTravel = currentTravel;

                if (t < 1f - BrakeCells / travel)
                {
                    // Full-speed phase.
                    SetStripPosition(stripRect, currentTravel);
                }
                else
                {
                    // Brake phase: blend the constant speed down to a crawl over the remaining cells.
                    var brakeT = (t - (1f - BrakeCells / travel)) / (BrakeCells / travel);
                    var easedBrake = 1f - (1f - brakeT) * (1f - brakeT);
                    SetStripPosition(stripRect, travel - BrakeCells + BrakeCells * easedBrake);
                }
                yield return null;
            }
            SetStripPosition(stripRect, travel);

            // Landing bounce: strip overshoots up, settles back to the exact stop.
            var restY = (targetStop % stripLength) * CellSize;
            var bounceElapsed = 0f;
            while (bounceElapsed < BounceDuration)
            {
                bounceElapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(bounceElapsed / BounceDuration);
                stripRect.anchoredPosition = new Vector2(0, restY + Mathf.Sin(t * Mathf.PI) * BounceHeight);
                yield return null;
            }
            stripRect.anchoredPosition = new Vector2(0, restY);
        }

        /// <summary>Keeps the strip y inside one loop height so the 2N-cell pattern wraps seamlessly.</summary>
        private void SetStripPosition(RectTransform stripRect, float travelCells)
        {
            var y = (travelCells * CellSize) % (_strips[0].Length * CellSize);
            stripRect.anchoredPosition = new Vector2(0, y);
        }

        /// <summary>Evaluates lines/scatter/bonus for fixed stops and credits the balance.</summary>
        private SpinResult EvaluateSpin(int[] stops)
        {
            var grid = new string[ReelCount][];
            for (var reel = 0; reel < ReelCount; reel++)
            {
                var strip = _strips[reel];
                grid[reel] = new[]
                {
                    strip[stops[reel]],
                    strip[(stops[reel] + 1) % strip.Length],
                    strip[(stops[reel] + 2) % strip.Length]
                };
            }

            var totalWin = 0;
            var winningLines = new List<string>();
            for (var row = 0; row < RowsPerReel; row++)
            {
                var lineWin = EvaluateLine(grid[0][row], grid[1][row], grid[2][row]) * CurrentBet;
                if (lineWin > 0)
                {
                    totalWin += lineWin;
                    winningLines.Add($"{SymbolGlyph(grid[0][row])} {SymbolGlyph(grid[1][row])} {SymbolGlyph(grid[2][row])}: +{lineWin}");
                }
            }

            var scatterCount = CountSymbol(grid, _config.ScatterSymbolId);
            var scatterWin = scatterCount >= _config.ScatterMinCount
                ? _config.ScatterPayXTotalBet * CurrentBet * _config.LineCount
                : 0;
            if (scatterWin > 0)
            {
                totalWin += scatterWin;
                winningLines.Add($"{_config.ScatterSymbolId} x{scatterCount} anywhere: +{scatterWin}");
            }

            var bonusCount = CountSymbol(grid, _config.BonusSymbolId);
            var bonusTriggered = bonusCount >= _config.BonusMinCount;
            // Scatter launches the feature even when its cash payout is zero.
            var scatterTriggered = scatterCount >= _config.ScatterMinCount;

            if (totalWin > 0)
            {
                _credits += totalWin;
                SetWinText($"+{totalWin}");
                _lastWinDetails = string.Join("\n", winningLines);
            }
            else
            {
                _lastWinDetails = string.Empty;
            }
            RefreshHud();

            return new SpinResult { totalWin = totalWin, scatterWin = scatterWin, bonusTriggered = bonusTriggered, scatterTriggered = scatterTriggered };
        }

        /// <summary>Returns the display glyph of a symbol id for win descriptions.</summary>
        private string SymbolGlyph(string symbolId)
        {
            var symbol = _config.GetSymbol(symbolId);
            return symbol != null ? symbol.glyph : symbolId;
        }

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
            for (var reel = 0; reel < ReelCount; reel++)
                for (var row = 0; row < RowsPerReel; row++)
                    if (grid[reel][row] == symbolId) count++;
            return count;
        }

        private void ChangeBet(int direction)
        {
            if (_isSpinning || _betOptions == null || _betOptions.Length == 0) return;
            _betIndex = Mathf.Clamp(_betIndex + direction, 0, _betOptions.Length - 1);
            RefreshHud();
            SaveManager.Instance?.Save();
        }

        /// <summary>Shows the full paytable of the current config in the popup.</summary>
        private void HandlePaytableClicked()
        {
            if (_isSpinning || _config == null) return;

            var sb = new StringBuilder();
            foreach (var entry in _config.Paytable)
            {
                var symbol = _config.GetSymbol(entry.symbolId);
                var glyph = symbol != null ? symbol.glyph : entry.symbolId;
                sb.AppendLine(entry.pay2 > 0
                    ? $"3x {glyph} = {entry.pay3}   |   2x {glyph} = {entry.pay2}"
                    : $"3x {glyph} = {entry.pay3}");
            }
            sb.AppendLine($"3+ {_config.GetSymbol(_config.ScatterSymbolId)?.glyph} anywhere = {_config.ScatterPayXTotalBet}x total bet");
            sb.AppendLine($"3+ {_config.GetSymbol(_config.BonusSymbolId)?.glyph} = {_config.FreeSpinsCount} free spins x{_config.FreeSpinsMultiplier}");
            sb.AppendLine();
            sb.AppendLine($"WILD substitutes any symbol. Bet {CurrentBet} x {_config.LineCount} lines.");

            StartCoroutine(ShowPopup("PAYTABLE", sb.ToString()));
        }

        private void RefreshHud()
        {
            if (_balanceText != null) _balanceText.text = $"CREDITS: {_credits}";
            if (_betText != null) _betText.text = _config != null ? $"BET: {CurrentBet} x {_config.LineCount}" : string.Empty;
        }

        private void SetMessage(string message)
        {
            if (_messageText != null) _messageText.text = message;
        }

        private void SetWinText(string text)
        {
            if (_winText != null) _winText.text = text;
        }

        private void SetSpinButtonInteractable(bool interactable)
        {
            if (_spinButton != null) _spinButton.interactable = interactable;
        }

        public string GetSaveData() => JsonUtility.ToJson(new SlotMachineSaveData { credits = _credits });

        public void LoadSaveData(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            var parsed = JsonUtility.FromJson<SlotMachineSaveData>(json);
            if (parsed != null)
                _credits = parsed.credits;
        }

        [System.Serializable]
        private class SlotMachineSaveData
        {
            public int credits;
        }

        private struct SpinResult
        {
            public int totalWin;
            public int scatterWin;
            public bool bonusTriggered;
            public bool scatterTriggered;
        }
    }
}

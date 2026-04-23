using System;
using UnityEngine;

[Serializable]
public struct SwitchRequirement
{
    [Tooltip("Индекс рубильника (0 = S1, последний = S6 мастер).")]
    public int switchIndex;
    [Tooltip("Нужное состояние рубильника.")]
    public bool mustBeOn;
}

[Serializable]
public struct SpotlightActivationRule
{
    [Tooltip("ВСЕ условия должны совпасть (AND). Пустой массив = всегда активен.")]
    public SwitchRequirement[] requirements;
}

[Serializable]
public struct SpotlightPowerConfig
{
    public PaintingSpotlight spotlight;
    [Tooltip("Хотя бы ОДНО правило должно совпасть (OR).")]
    public SpotlightActivationRule[] activationRules;
}

[Serializable]
public struct SwitchAdjacency
{
    [Tooltip("Индексы других рубильников, которые переключаются вместе с этим.")]
    public int[] neighborIndices;
}

/// <summary>
/// Единый компонент панели рубильников S1–S6:
///   — S6: кнопка питания. При включении разблокирует S1–S5 и запускает загадку.
///         При выключении блокирует S1–S5 и гасит все прожекторы.
///   — S1–S5: Lights Out каскад. Заблокированы до включения S6.
///   — L1–L4: питание по OR-of-AND правилам, настраивается в Инспекторе.
/// </summary>
public class LoopPuzzlePowerCircuit : MonoBehaviour
{
    [Header("Рубильники (S1…S5, последний = S6 питание)")]
    [SerializeField] private LoopPuzzleButton[] _switches;

    [Header("Логика питания прожекторов")]
    [SerializeField] private SpotlightPowerConfig[] _spotlightConfigs;

    [Header("Lights Out — смежность S1–S5")]
    [Tooltip("Настраивается через матрицу в Инспекторе.")]
    [SerializeField] private SwitchAdjacency[] _adjacency;

    // Оставлено для совместимости с редактором, в рантайме не используется.
    [HideInInspector] [SerializeField] private int[] _masterUnlockSequence;

    /// <summary>Срабатывает при любом изменении питания прожекторов.</summary>
    public event Action OnPowerChanged;

    /// <summary>Срабатывает при включении или выключении S6 (мастер-рубильника). True = включён.</summary>
    public event Action<bool> OnMasterToggled;

    /// <summary>True когда мастер-рубильник S6 активен.</summary>
    public bool IsMasterOn => PowerButton != null && PowerButton.IsActive;

    private bool _processingCascade;

    private LoopPuzzleButton PowerButton =>
        _switches != null && _switches.Length > 0 ? _switches[_switches.Length - 1] : null;

    private int NonMasterCount =>
        _switches != null ? _switches.Length - 1 : 0;

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    private void Awake()
    {
        // S1–S5 заблокированы до включения питания.
        for (int i = 0; i < NonMasterCount; i++)
            _switches[i]?.SetLocked(true);

        // S6 сразу доступен игроку — это кнопка питания.
        PowerButton?.SetLocked(false);

        // Подписка на S1–S5.
        for (int i = 0; i < NonMasterCount; i++)
        {
            int index = i;
            if (_switches[i] != null)
                _switches[i].OnToggled += _ => HandlePuzzleSwitchToggled(index);
        }

        // Подписка на S6 — запуск/остановка загадки.
        if (PowerButton != null)
            PowerButton.OnToggled += HandlePowerToggled;
    }

    private void Start() => EvaluateAndApply();

    // ── Input handling ─────────────────────────────────────────────────────────

    /// <summary>S6 включён/выключен — разблокировать или заблокировать S1–S5.</summary>
    private void HandlePowerToggled(bool isOn)
    {
        for (int i = 0; i < NonMasterCount; i++)
            _switches[i]?.SetLocked(!isOn);

        EvaluateAndApply();
        OnMasterToggled?.Invoke(isOn);
    }

    /// <summary>Один из S1–S5 нажат — применить Lights Out каскад и пересчитать питание.</summary>
    private void HandlePuzzleSwitchToggled(int switchIndex)
    {
        if (_processingCascade) return;
        ApplyCascade(switchIndex);
        EvaluateAndApply();
    }

    private void ApplyCascade(int switchIndex)
    {
        if (_adjacency == null || switchIndex >= _adjacency.Length) return;
        if (_adjacency[switchIndex].neighborIndices == null) return;

        _processingCascade = true;
        foreach (int nb in _adjacency[switchIndex].neighborIndices)
            if (nb >= 0 && nb < NonMasterCount)
                _switches[nb]?.ToggleSilent();
        _processingCascade = false;
    }

    // ── Power evaluation ───────────────────────────────────────────────────────

    /// <summary>Пересчитывает и применяет питание всех прожекторов.</summary>
    public void EvaluateAndApply()
    {
        bool powerOn = PowerButton != null && PowerButton.IsActive;

        foreach (var config in _spotlightConfigs)
        {
            if (config.spotlight == null) continue;
            bool powered = powerOn && EvaluateSpotlight(config, GetSwitchState);
            config.spotlight.SetPowered(powered);
        }

        OnPowerChanged?.Invoke();
    }

    private bool EvaluateSpotlight(SpotlightPowerConfig config, Func<int, bool> getState)
    {
        if (config.activationRules == null || config.activationRules.Length == 0) return true;
        foreach (var rule in config.activationRules)
            if (EvaluateRule(rule, getState)) return true; // OR
        return false;
    }

    private bool EvaluateRule(SpotlightActivationRule rule, Func<int, bool> getState)
    {
        if (rule.requirements == null || rule.requirements.Length == 0) return true;
        foreach (var req in rule.requirements)
            if (getState(req.switchIndex) != req.mustBeOn) return false; // AND
        return true;
    }

    // ── Switch state API ───────────────────────────────────────────────────────

    /// <summary>Возвращает текущее состояние рубильника по индексу.</summary>
    public bool GetSwitchState(int index)
    {
        if (index < 0 || index >= _switches.Length) return false;
        return _switches[index] != null && _switches[index].IsActive;
    }

    /// <summary>Возвращает состояния всех рубильников для сохранения.</summary>
    public bool[] GetAllSwitchStates()
    {
        var states = new bool[_switches.Length];
        for (int i = 0; i < _switches.Length; i++)
            states[i] = _switches[i] != null && _switches[i].IsActive;
        return states;
    }

    /// <summary>Восстанавливает состояния рубильников из сохранения без событий.</summary>
    public void RestoreSwitchStates(bool[] states)
    {
        for (int i = 0; i < _switches.Length && i < states.Length; i++)
            _switches[i]?.SetStateSilent(states[i]);
        EvaluateAndApply();
    }

    // ── Editor utilities ───────────────────────────────────────────────────────

    /// <summary>Locks all switches including the master S6. Called when the puzzle is solved.</summary>
    public void LockAllSwitches()
    {
        if (_switches == null) return;
        foreach (var sw in _switches)
            sw?.SetLocked(true);
    }

    public int SwitchCount          => _switches != null ? _switches.Length : 0;
    public int SpotlightConfigCount => _spotlightConfigs != null ? _spotlightConfigs.Length : 0;
    public SwitchAdjacency[] Adjacency            => _adjacency;
    public int[]             MasterUnlockSequence => _masterUnlockSequence;

    public SpotlightPowerConfig GetSpotlightConfig(int i) => _spotlightConfigs[i];

    /// <summary>Строит матрицу смежности для S1–S5. Используется редактором (GF(2) анализ).</summary>
    public bool[,] BuildAdjacencyMatrix()
    {
        int n = NonMasterCount;
        var A = new bool[n, n];
        for (int i = 0; i < n; i++) A[i, i] = true;
        if (_adjacency == null) return A;

        for (int i = 0; i < Mathf.Min(n, _adjacency.Length); i++)
        {
            if (_adjacency[i].neighborIndices == null) continue;
            foreach (int nb in _adjacency[i].neighborIndices)
            {
                if (nb < 0 || nb >= n || nb == i) continue;
                A[nb, i] = true;
                A[i, nb] = true;
            }
        }
        return A;
    }

    /// <summary>Проверяет решаемость при гипотетическом состоянии S1–S5. Используется редактором.</summary>
    public bool CheckAllPoweredWith(bool[] switchStates)
    {
        bool StateResolver(int i) => i >= 0 && i < switchStates.Length && switchStates[i];
        foreach (var config in _spotlightConfigs)
            if (!EvaluateSpotlight(config, StateResolver)) return false;
        return true;
    }
}

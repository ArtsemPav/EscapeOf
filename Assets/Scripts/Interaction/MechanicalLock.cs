using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// Logic for a 5-digit combination lock using mechanical cylinders.
/// Integrated with PuzzleModeController for state management and events.
/// </summary>
public class MechanicalLock : MonoBehaviour, ISaveable
{
    [Header("Combination Settings")]
    [SerializeField] private int[] _correctCombination = new int[5] { 1, 2, 3, 4, 5 };
    
    [Header("References")]
    [SerializeField] private PuzzleModeController _puzzleController;
    [SerializeField] private LockCylinder[] _cylinders;
    
    [Header("Save")]
    [SerializeField] private string _saveId;

    private Camera _mainCamera;

    public string SaveId => _saveId;

    private void Awake()
    {
        _mainCamera = Camera.main;
        if (_puzzleController == null) _puzzleController = GetComponent<PuzzleModeController>();
        
        SaveManager.Instance?.Register(this);
    }

    private void Start()
    {
        // Randomize cylinders on start if not already solved (and if no save data was applied yet)
        if (_puzzleController != null && !_puzzleController.IsSolved)
        {
            RandomizeCylinders();
        }
    }

    private void RandomizeCylinders()
    {
        bool isCorrect;
        do
        {
            isCorrect = true;
            for (int i = 0; i < _cylinders.Length; i++)
            {
                if (_cylinders[i] != null)
                {
                    int randomValue = UnityEngine.Random.Range(0, 10);
                    _cylinders[i].SetValue(randomValue);
                    
                    if (i < _correctCombination.Length && randomValue != _correctCombination[i])
                    {
                        isCorrect = false;
                    }
                }
            }
            // If we accidentally rolled the correct combination, try again
        } while (isCorrect && _cylinders.Length == _correctCombination.Length);
    }

    private void OnDestroy()
    {
        SaveManager.Instance?.Unregister(this);
    }

    private void Update()
    {
        if (_puzzleController != null && _puzzleController.IsActive && !_puzzleController.IsSolved)
        {
            HandleInput();
        }
    }

    private void HandleInput()
    {
        bool lmb = Mouse.current.leftButton.wasPressedThisFrame;
        bool rmb = Mouse.current.rightButton.wasPressedThisFrame;

        if (lmb || rmb)
        {
            Ray ray = _mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                LockCylinder cylinder = hit.collider.GetComponentInParent<LockCylinder>();
                if (cylinder != null && Array.IndexOf(_cylinders, cylinder) != -1)
                {
                    // LMB -> rotate forward (-36 deg), RMB -> rotate backward (+36 deg)
                    cylinder.Rotate(!lmb);
                    CheckCombination();
                }
            }
        }
    }

    private void CheckCombination()
    {
        if (_cylinders.Length != _correctCombination.Length) return;

        for (int i = 0; i < _cylinders.Length; i++)
        {
            if (_cylinders[i].CurrentValue != _correctCombination[i])
            {
                return;
            }
        }

        Solve();
    }

    private void Solve()
    {
        if (_puzzleController != null)
        {
            // PuzzleModeController handles its own solved state, events (OnPuzzleSolved), and saves state.
            _puzzleController.SetSolved();
        }
    }

    // ── ISaveable Implementation ───────────────────────────────────────────────

    public string GetSaveData()
    {
        int[] values = new int[_cylinders.Length];
        for (int i = 0; i < _cylinders.Length; i++) values[i] = _cylinders[i].CurrentValue;
        
        return JsonUtility.ToJson(new LockSaveData { 
            cylinderValues = values 
        });
    }

    public void LoadSaveData(string json)
    {
        var data = JsonUtility.FromJson<LockSaveData>(json);
        
        if (data.cylinderValues != null && data.cylinderValues.Length == _cylinders.Length)
        {
            for (int i = 0; i < _cylinders.Length; i++)
            {
                _cylinders[i].SetValue(data.cylinderValues[i]);
            }
        }
    }

    [Serializable]
    private struct LockSaveData
    {
        public int[] cylinderValues;
    }

    [ContextMenu("Generate Save ID")]
    private void GenerateSaveId()
    {
        if (!string.IsNullOrEmpty(_saveId)) return;
        _saveId = Guid.NewGuid().ToString();
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }
}

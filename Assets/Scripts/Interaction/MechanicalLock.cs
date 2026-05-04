using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// Logic for a 5-digit combination lock using mechanical cylinders.
/// </summary>
public class MechanicalLock : MonoBehaviour, ISaveable
{
    [Header("Combination Settings")]
    [SerializeField] private int[] _correctCombination = new int[5] { 1, 2, 3, 4, 5 };
    
    [Header("References")]
    [SerializeField] private PuzzleModeController _puzzleController;
    [SerializeField] private LockCylinder[] _cylinders;
    
    [Header("Events")]
    [SerializeField] private UnityEvent OnPuzzleSolved;
    [SerializeField] private GameEvent _puzzleSolvedGameEvent;

    [Header("Save")]
    [SerializeField] private string _saveId;

    private bool _isSolved;
    private Camera _mainCamera;

    public string SaveId => _saveId;

    private void Awake()
    {
        _mainCamera = Camera.main;
        if (_puzzleController == null) _puzzleController = GetComponent<PuzzleModeController>();
        
        SaveManager.Instance?.Register(this);
    }

    private void OnDestroy()
    {
        SaveManager.Instance?.Unregister(this);
    }

    private void Update()
    {
        if (_puzzleController != null && _puzzleController.IsActive && !_isSolved)
        {
            HandleInput();
        }
    }

    private void HandleInput()
    {
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            Ray ray = _mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                LockCylinder cylinder = hit.collider.GetComponentInParent<LockCylinder>();
                if (cylinder != null && Array.IndexOf(_cylinders, cylinder) != -1)
                {
                    cylinder.Rotate();
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
        _isSolved = true;
        OnPuzzleSolved?.Invoke();
        _puzzleSolvedGameEvent?.Raise();
        
        if (_puzzleController != null)
        {
            _puzzleController.SetSolved();
        }
    }

    // ── ISaveable Implementation ───────────────────────────────────────────────

    public string GetSaveData()
    {
        int[] values = new int[_cylinders.Length];
        for (int i = 0; i < _cylinders.Length; i++) values[i] = _cylinders[i].CurrentValue;
        
        return JsonUtility.ToJson(new LockSaveData { 
            isSolved = _isSolved, 
            cylinderValues = values 
        });
    }

    public void LoadSaveData(string json)
    {
        var data = JsonUtility.FromJson<LockSaveData>(json);
        _isSolved = data.isSolved;
        
        if (data.cylinderValues != null && data.cylinderValues.Length == _cylinders.Length)
        {
            for (int i = 0; i < _cylinders.Length; i++)
            {
                _cylinders[i].SetValue(data.cylinderValues[i]);
            }
        }

        if (_isSolved && _puzzleController != null && !_puzzleController.IsSolved)
        {
            _puzzleController.SetSolved();
        }
    }

    [Serializable]
    private struct LockSaveData
    {
        public bool isSolved;
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

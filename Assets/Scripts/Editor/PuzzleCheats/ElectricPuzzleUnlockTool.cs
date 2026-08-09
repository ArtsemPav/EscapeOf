using UnityEngine;
using UnityEditor;
using System.Reflection;

/// <summary>
/// Editor cheat tool to instantly solve the Electric Puzzle in the current scene.
/// Only works in Play Mode — wires and fuse visuals are created at runtime.
/// If invoked while the game is paused, the SetSolved call (which exits puzzle mode
/// and fires solve events) is deferred until timeScale is restored.
/// </summary>
public static class ElectricPuzzleUnlockTool
{
    private const string MENU_PATH = "Tools/PuzzlesCheats/Solve Electric Puzzle";

    private const BindingFlags InstanceFlags =
        BindingFlags.NonPublic | BindingFlags.Instance;

    private static bool _pendingSolvedEvent;

    [MenuItem(MENU_PATH)]
    public static void SolveElectricPuzzle()
    {
        ElectricPuzzleController controller =
            Object.FindFirstObjectByType<ElectricPuzzleController>();

        if (controller == null)
        {
            Debug.LogWarning("[PuzzleCheats] ElectricPuzzleController not found in the current scene.");
            return;
        }

        if (!Application.isPlaying)
        {
            Debug.LogWarning("[PuzzleCheats] Electric Puzzle can only be solved in Play Mode.");
            return;
        }

        var type = typeof(ElectricPuzzleController);

        // ── Read puzzle data for the solution ────────────────────────────────

        var puzzleData = type.GetField("_puzzleData", InstanceFlags)?
            .GetValue(controller) as ElectricPuzzleData;

        if (puzzleData == null)
        {
            Debug.LogWarning("[PuzzleCheats] _puzzleData is not assigned on ElectricPuzzleController.");
            return;
        }

        int[] solution = (int[])puzzleData.Solution.Clone();

        // ── Read fuse item ID from accepted items ────────────────────────────

        var acceptedItems = type.GetField("_acceptedItems", InstanceFlags)?
            .GetValue(controller) as ItemData[];

        string fuseItemId = acceptedItems != null && acceptedItems.Length > 0
            ? acceptedItems[0].ItemId
            : "cheat_fuse";

        // ── Set pending-load fields so ApplyPendingLoad recreates all wires ──

        type.GetField("_pendingSolved", InstanceFlags).SetValue(controller, true);
        type.GetField("_pendingFuseInserted", InstanceFlags).SetValue(controller, true);
        type.GetField("_pendingFuseItemId", InstanceFlags).SetValue(controller, fuseItemId);
        type.GetField("_pendingConnections", InstanceFlags).SetValue(controller, solution);
        type.GetField("_pendingLoad", InstanceFlags).SetValue(controller, true);

        // ── Apply load: creates wires, sets _isSolved, _fuseInserted, _connections

        type.GetMethod("ApplyPendingLoad", InstanceFlags).Invoke(controller, null);

        // Settle all wires so they load in their natural hanging shape
        ElectricWire.JointPresettle();

        // ── Restore fuse visuals and disable the anchor collider ─────────────

        type.GetMethod("ShowFuseMesh", InstanceFlags).Invoke(controller, null);

        var fuseCollider = type.GetField("_fuseAnchorCollider", InstanceFlags)?
            .GetValue(controller) as Collider;
        if (fuseCollider != null)
            fuseCollider.enabled = false;

        // ── Refresh visuals: _wiresCorrect, lamp color, lever pulled, solved object

        type.GetMethod("RefreshVisuals", InstanceFlags).Invoke(controller, null);

        // ── Start the solved ambient loop ────────────────────────────────────

        type.GetMethod("StartSolvedLoop", InstanceFlags).Invoke(controller, null);

        // ── Activate building power (same as HandleLeverPulled on correct solve)

        LightingSystem.Instance?.ActivatePower();

        // ── Persist state ────────────────────────────────────────────────────

        SaveManager.Instance?.Save();

        // ── Fire solved event via PuzzleModeController ────────────────────────

        var puzzleModeController = type.GetField("_controller", InstanceFlags)?
            .GetValue(controller) as PuzzleModeController;

        if (Time.timeScale == 0f)
        {
            if (!_pendingSolvedEvent)
            {
                _pendingSolvedEvent = true;
                EditorApplication.update += WaitForUnpauseAndFireSolved;
                Debug.Log("<color=yellow>[PuzzleCheats] Electric Puzzle state set to solved. "
                          + "PuzzleModeController.SetSolved() will fire when you unpause.</color>");
            }
            else
            {
                Debug.LogWarning("[PuzzleCheats] A pending solve is already waiting for unpause.");
            }
        }
        else
        {
            puzzleModeController?.SetSolved();
            Debug.Log("<color=green>[PuzzleCheats] Electric Puzzle has been force-solved!</color>");
        }
    }

    private static void WaitForUnpauseAndFireSolved()
    {
        if (!EditorApplication.isPlaying)
        {
            _pendingSolvedEvent = false;
            EditorApplication.update -= WaitForUnpauseAndFireSolved;
            return;
        }

        if (Time.timeScale > 0f)
        {
            _pendingSolvedEvent = false;
            EditorApplication.update -= WaitForUnpauseAndFireSolved;

            var controller = Object.FindFirstObjectByType<ElectricPuzzleController>();

            if (controller != null)
            {
                var puzzleModeController = typeof(ElectricPuzzleController)
                    .GetField("_controller", InstanceFlags)?
                    .GetValue(controller) as PuzzleModeController;

                puzzleModeController?.SetSolved();
                Debug.Log("<color=green>[PuzzleCheats] SetSolved fired after unpause! "
                          + "Electric Puzzle has been force-solved!</color>");
            }
            else
            {
                Debug.LogWarning("[PuzzleCheats] ElectricPuzzleController disappeared before unpause.");
            }
        }
    }
}

using UnityEngine;
using UnityEditor;
using System.Reflection;
using System.Collections.Generic;

/// <summary>
/// Editor cheat tool to instantly solve the Generator Puzzle in the current scene.
/// Marks all drop-slot items as placed, sets _isSolved, plays completion VFX/shake/audio,
/// activates building power, and fires PuzzleModeController.SetSolved().
/// If invoked while the game is paused, the SetSolved call is deferred until timeScale is restored.
/// </summary>
public static class GeneratorPuzzleUnlockTool
{
    private const string MENU_PATH = "Tools/PuzzlesCheats/Solve Generator Puzzle";

    private const BindingFlags InstanceFlags =
        BindingFlags.NonPublic | BindingFlags.Instance;

    private const BindingFlags PublicInstanceFlags =
        BindingFlags.Public | BindingFlags.Instance;

    private static bool _pendingSolvedEvent;

    [MenuItem(MENU_PATH)]
    public static void SolveGeneratorPuzzle()
    {
        GeneratorPuzzleController controller =
            Object.FindFirstObjectByType<GeneratorPuzzleController>();

        if (controller == null)
        {
            Debug.LogWarning("[PuzzleCheats] GeneratorPuzzleController not found in the current scene.");
            return;
        }

        if (!Application.isPlaying)
        {
            Debug.LogWarning("[PuzzleCheats] Generator Puzzle can only be solved in Play Mode.");
            return;
        }

        var type = typeof(GeneratorPuzzleController);

        // ── Mark all drop-slot items as placed ────────────────────────────────

        var placedItemIds = type.GetField("_placedItemIds", InstanceFlags)?
            .GetValue(controller) as HashSet<string>;

        var dropSlots = type.GetField("_dropSlots", InstanceFlags)?
            .GetValue(controller) as System.Array;

        if (placedItemIds != null && dropSlots != null)
        {
            foreach (var slot in dropSlots)
            {
                if (slot == null) continue;

                // GeneratorDropSlot is a private nested class — use reflection
                var itemField = slot.GetType().GetField("item", PublicInstanceFlags);
                var item = itemField?.GetValue(slot) as ItemData;

                if (item != null && !string.IsNullOrEmpty(item.ItemId))
                    placedItemIds.Add(item.ItemId);
            }
        }

        // ── Stop minigame and hide its panel ──────────────────────────────────

        var minigame = type.GetField("_minigame", InstanceFlags)?
            .GetValue(controller) as GeneratorTimingMinigame;
        minigame?.StopMinigame();

        var minigamePanel = type.GetField("_minigamePanel", InstanceFlags)?
            .GetValue(controller) as GameObject;
        if (minigamePanel != null)
            minigamePanel.SetActive(false);

        // ── Set solved flag and reset processing flags ────────────────────────

        type.GetField("_isSolved", InstanceFlags).SetValue(controller, true);
        type.GetField("_allItemsReady", InstanceFlags).SetValue(controller, false);
        type.GetField("_isProcessing", InstanceFlags).SetValue(controller, false);

        // ── Play completion VFX ────────────────────────────────────────────────

        type.GetMethod("PlayCompletionVfx", InstanceFlags).Invoke(controller, null);

        // ── Enable continuous generator shake ──────────────────────────────────

        type.GetMethod("EnableGeneratorShake", InstanceFlags).Invoke(controller, null);

        // ── Activate building power ────────────────────────────────────────────

        LightingSystem.Instance?.SetGeneratorReady(true);

        // ── Start the generator loop audio directly ────────────────────────────

        var loopAudio = type.GetField("_generatorLoopAudio", InstanceFlags)?
            .GetValue(controller) as LoopAudioController;
        loopAudio?.StartLoop();

        // ── Persist state ──────────────────────────────────────────────────────

        SaveManager.Instance?.Save();

        // ── Fire solved event via PuzzleModeController ──────────────────────────

        var puzzleModeController = type.GetField("_controller", InstanceFlags)?
            .GetValue(controller) as PuzzleModeController;

        if (Time.timeScale == 0f)
        {
            if (!_pendingSolvedEvent)
            {
                _pendingSolvedEvent = true;
                EditorApplication.update += WaitForUnpauseAndFireSolved;
                Debug.Log("<color=yellow>[PuzzleCheats] Generator Puzzle state set to solved. "
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
            Debug.Log("<color=green>[PuzzleCheats] Generator Puzzle has been force-solved!</color>");
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

            var controller = Object.FindFirstObjectByType<GeneratorPuzzleController>();

            if (controller != null)
            {
                var puzzleModeController = typeof(GeneratorPuzzleController)
                    .GetField("_controller", InstanceFlags)?
                    .GetValue(controller) as PuzzleModeController;

                puzzleModeController?.SetSolved();
                Debug.Log("<color=green>[PuzzleCheats] SetSolved fired after unpause! "
                          + "Generator Puzzle has been force-solved!</color>");
            }
            else
            {
                Debug.LogWarning("[PuzzleCheats] GeneratorPuzzleController disappeared before unpause.");
            }
        }
    }
}

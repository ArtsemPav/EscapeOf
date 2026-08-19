using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Runtime puzzle solving cheats. Ports the Editor-only Tools/PuzzlesCheats
/// to work in Play Mode and Development Builds without UnityEditor dependencies.
/// Each method returns a CheatResult with a status message and an optional deferred
/// action to execute when Time.timeScale returns above zero (game unpaused).
/// </summary>
public static class DevPuzzleCheats
{
    private const BindingFlags InstanceFlags =
        BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags PublicFlags =
        BindingFlags.Public | BindingFlags.Instance;
    private const BindingFlags AllFlags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>
    /// Result of a cheat action: success status, message, and optional deferred
    /// action to execute when Time.timeScale returns above zero.
    /// </summary>
    public struct CheatResult
    {
        public bool Success;
        public string Message;
        public Action DeferredAction;
    }

    // ── Public cheat methods ──────────────────────────────────────────────

    /// <summary>Solves the Board Puzzle by setting _isSolved and firing OnPuzzleSolved.</summary>
    public static CheatResult SolveBoardPuzzle()
    {
        return SolveSimplePuzzle("BoardPuzzleManager", "Board Puzzle",
            extraMethod: "LockAllCylinders");
    }

    /// <summary>Solves the Metamorf Puzzle by setting _isSolved and firing OnPuzzleSolved.</summary>
    public static CheatResult SolveMetamorfPuzzle()
    {
        return SolveSimplePuzzle("MetamorfPuzzleController", "Metamorf Puzzle",
            extraMethod: "DisableCollidersOnSolved");
    }

    /// <summary>Solves the Electric Puzzle by replicating the editor cheat: pending load, visuals, power, SetSolved.</summary>
    public static CheatResult SolveElectricPuzzle()
    {
        Type type = FindType("ElectricPuzzleController");
        if (type == null) return Failed("ElectricPuzzleController type not found");
        UnityEngine.Object instance = FindFirst(type);
        if (instance == null) return Failed("ElectricPuzzleController not found in scene");

        // ── Read puzzle data for the solution ────────────────────────────────
        var puzzleData = GetField<object>(instance, "_puzzleData");
        if (puzzleData == null)
            return Failed("Electric Puzzle: _puzzleData is not assigned");

        var solutionProp = puzzleData.GetType().GetProperty("Solution");
        int[] solution = solutionProp?.GetValue(puzzleData) as int[];
        if (solution == null)
            return Failed("Electric Puzzle: could not read solution");
        solution = (int[])solution.Clone();

        // ── Read fuse item ID from accepted items ────────────────────────────
        var acceptedItems = GetField<UnityEngine.Object[]>(instance, "_acceptedItems");
        string fuseItemId = "cheat_fuse";
        if (acceptedItems != null && acceptedItems.Length > 0 && acceptedItems[0] is ItemData fuse)
            fuseItemId = fuse.ItemId;

        // ── Set pending-load fields so ApplyPendingLoad recreates all wires ──
        SetField(instance, "_pendingSolved", true);
        SetField(instance, "_pendingFuseInserted", true);
        SetField(instance, "_pendingFuseItemId", fuseItemId);
        SetField(instance, "_pendingConnections", solution);
        SetField(instance, "_pendingLoad", true);

        // ── Apply load ───────────────────────────────────────────────────────
        type.GetMethod("ApplyPendingLoad", InstanceFlags)?.Invoke(instance, null);

        // Settle wires
        Type wireType = FindType("ElectricWire");
        wireType?.GetMethod("JointPresettle", PublicFlags | BindingFlags.Static)?
            .Invoke(null, null);

        // ── Restore fuse visuals and disable anchor collider ─────────────────
        type.GetMethod("ShowFuseMesh", InstanceFlags)?.Invoke(instance, null);

        var fuseCollider = GetField<UnityEngine.Object>(instance, "_fuseAnchorCollider") as Collider;
        if (fuseCollider != null) fuseCollider.enabled = false;

        // ── Refresh visuals ──────────────────────────────────────────────────
        type.GetMethod("RefreshVisuals", InstanceFlags)?.Invoke(instance, null);
        type.GetMethod("StartSolvedLoop", InstanceFlags)?.Invoke(instance, null);

        // ── Activate building power ──────────────────────────────────────────
        ActivateLightingPower();

        SaveManager.Instance?.Save();

        // ── Fire solved event via PuzzleModeController ────────────────────────
        object controller = GetField<object>(instance, "_controller");
        if (controller != null)
            return FireOrDefer(controller, "Electric Puzzle");

        return Solved("Electric Puzzle solved (no event controller)");
    }

    /// <summary>Solves the Generator Puzzle by replicating the editor cheat: mark slots, VFX, power, SetSolved.</summary>
    public static CheatResult SolveGeneratorPuzzle()
    {
        Type type = FindType("GeneratorPuzzleController");
        if (type == null) return Failed("GeneratorPuzzleController type not found");
        UnityEngine.Object instance = FindFirst(type);
        if (instance == null) return Failed("GeneratorPuzzleController not found in scene");

        // ── Mark all drop-slot items as placed ────────────────────────────────
        var placedItemIds = GetField<object>(instance, "_placedItemIds");
        var dropSlots = GetField<UnityEngine.Object[]>(instance, "_dropSlots");

        if (placedItemIds != null && dropSlots != null)
        {
            var addMethod = placedItemIds.GetType().GetMethod("Add");
            foreach (var slot in dropSlots)
            {
                if (slot == null) continue;
                var item = slot.GetType().GetField("item", PublicFlags)?.GetValue(slot) as ItemData;
                if (item != null && !string.IsNullOrEmpty(item.ItemId))
                    addMethod?.Invoke(placedItemIds, new object[] { item.ItemId });
            }
        }

        // ── Stop minigame and hide its panel ──────────────────────────────────
        var minigame = GetField<object>(instance, "_minigame");
        minigame?.GetType().GetMethod("StopMinigame", PublicFlags)?.Invoke(minigame, null);

        var minigamePanel = GetField<UnityEngine.Object>(instance, "_minigamePanel") as GameObject;
        if (minigamePanel != null) minigamePanel.SetActive(false);

        // ── Set solved flag and reset processing flags ────────────────────────
        SetField(instance, "_isSolved", true);
        SetField(instance, "_allItemsReady", false);
        SetField(instance, "_isProcessing", false);

        // ── Play completion VFX and enable generator shake ────────────────────
        type.GetMethod("PlayCompletionVfx", InstanceFlags)?.Invoke(instance, null);
        type.GetMethod("EnableGeneratorShake", InstanceFlags)?.Invoke(instance, null);

        // ── Activate building power ──────────────────────────────────────────
        Type lightingType = FindType("LightingSystem");
        if (lightingType != null)
        {
            var lightingInstance = lightingType.GetProperty("Instance")?.GetValue(null);
            lightingType.GetMethod("SetGeneratorReady")?.Invoke(lightingInstance, new object[] { true });
        }

        // ── Start generator loop audio ────────────────────────────────────────
        var loopAudio = GetField<object>(instance, "_generatorLoopAudio");
        loopAudio?.GetType().GetMethod("StartLoop", PublicFlags)?.Invoke(loopAudio, null);

        SaveManager.Instance?.Save();

        // ── Fire solved event via PuzzleModeController ────────────────────────
        object controller = GetField<object>(instance, "_controller");
        if (controller != null)
            return FireOrDefer(controller, "Generator Puzzle");

        return Solved("Generator Puzzle solved (no event controller)");
    }

    /// <summary>Solves the Fifteen Puzzle via AutoSolve().</summary>
    public static CheatResult SolveFifteenPuzzle()
    {
        return SolveByAutoSolve("FifteenPuzzleManager", "Fifteen Puzzle");
    }

    /// <summary>Solves the Paint (Loop) Puzzle via AutoSolve().</summary>
    public static CheatResult SolvePaintPuzzle()
    {
        return SolveByAutoSolve("LoopPuzzleController", "Paint Puzzle",
            checkSolvedProperty: "IsSolved");
    }

    /// <summary>Unlocks all Digital Lock Systems (procedural safes).</summary>
    public static CheatResult SolveProceduralSafes()
    {
        return SolveLockSystem("DigitalLockSystem", "Procedural Safe",
            solveMethod: "Submit",
            preAction: (instance, type) =>
            {
                string correctCode = GetField<string>(instance, "_correctCode");
                if (correctCode != null)
                    SetField(instance, "_currentInput", correctCode);
                SetField(instance, "_isDisplayingTemporaryMessage", false);
            });
    }

    /// <summary>Unlocks the Da Vinci puzzle (Room 4) — a specific MechanicalLock by save ID.</summary>
    public static CheatResult SolveDaVinciPuzzle()
    {
        return SolveMechanicalLockBySaveId("davinchi_puzzle_room4", "Da Vinci (Room 4)");
    }

    /// <summary>Unlocks the Padlock puzzle (Room 2) — a specific MechanicalLock by save ID.</summary>
    public static CheatResult SolvePadlockPuzzle()
    {
        return SolveMechanicalLockBySaveId("lock_padlock_room2", "Padlock (Room 2)");
    }

    /// <summary>Unlocks all LockDials (Doctor Room Safe).</summary>
    public static CheatResult SolveDoctorSafes()
    {
        Type type = FindType("LockDial");
        if (type == null) return Failed("LockDial type not found");

        UnityEngine.Object[] instances = FindAll(type);
        if (instances == null || instances.Length == 0)
            return Failed("No LockDial found in scene");

        int count = 0;
        foreach (var instance in instances)
        {
            var isUnlockedProp = type.GetProperty("IsUnlocked", PublicFlags);
            bool isUnlocked = isUnlockedProp != null &&
                              (bool)isUnlockedProp.GetValue(instance);
            if (isUnlocked) continue;

            type.GetMethod("Unlock", InstanceFlags)?.Invoke(instance, null);
            count++;
        }

        return count > 0
            ? Solved($"Unlocked {count} LockDial(s)")
            : Solved("All LockDials were already unlocked");
    }

    /// <summary>Attempts to solve all known puzzle types in the scene.</summary>
    public static CheatResult SolveAllPuzzles()
    {
        var messages = new System.Collections.Generic.List<string>();
        var deferredActions = new System.Collections.Generic.List<Action>();

        var methods = new (string name, Func<CheatResult> solver)[]
        {
            ("Board", SolveBoardPuzzle),
            ("Metamorf", SolveMetamorfPuzzle),
            ("Electric", SolveElectricPuzzle),
            ("Generator", SolveGeneratorPuzzle),
            ("Fifteen", SolveFifteenPuzzle),
            ("Paint", SolvePaintPuzzle),
            ("ProcSafe", SolveProceduralSafes),
            ("DaVinci", SolveDaVinciPuzzle),
            ("Padlock", SolvePadlockPuzzle),
            ("DocSafe", SolveDoctorSafes),
        };

        foreach (var (name, method) in methods)
        {
            CheatResult result = method();
            if (result.Success)
                messages.Add($"[{name}] {result.Message}");
            if (result.DeferredAction != null)
                deferredActions.Add(result.DeferredAction);
        }

        Action deferred = null;
        if (deferredActions.Count > 0)
            deferred = () => { foreach (var a in deferredActions) a?.Invoke(); };

        return new CheatResult
        {
            Success = true,
            Message = string.Join("\n", messages),
            DeferredAction = deferred
        };
    }

    // ── Internal solvers ──────────────────────────────────────────────────

    /// <summary>Activates LightingSystem.ActivatePower() via reflection.</summary>
    private static void ActivateLightingPower()
    {
        Type lightingType = FindType("LightingSystem");
        if (lightingType == null) return;
        var instance = lightingType.GetProperty("Instance")?.GetValue(null);
        lightingType.GetMethod("ActivatePower")?.Invoke(instance, null);
    }

    /// <summary>Finds a specific MechanicalLock by its _saveId and solves it.</summary>
    private static CheatResult SolveMechanicalLockBySaveId(string saveId, string displayName)
    {
        Type type = FindType("MechanicalLock");
        if (type == null) return Failed("MechanicalLock type not found");

        UnityEngine.Object[] instances = FindAll(type);
        if (instances == null || instances.Length == 0)
            return Failed("No MechanicalLock found in scene");

        UnityEngine.Object target = null;
        foreach (var instance in instances)
        {
            string id = GetField<string>(instance, "_saveId");
            if (id == saveId) { target = instance; break; }
        }

        if (target == null)
            return Failed($"No MechanicalLock with save ID '{saveId}' found");

        // Set cylinders to correct combination
        int[] combination = GetField<int[]>(target, "_correctCombination");
        UnityEngine.Object[] cylinders = GetField<UnityEngine.Object[]>(target, "_cylinders");
        if (combination != null && cylinders != null)
        {
            for (int i = 0; i < cylinders.Length && i < combination.Length; i++)
            {
                if (cylinders[i] == null) continue;
                cylinders[i].GetType().GetMethod("SetValue", PublicFlags)?
                    .Invoke(cylinders[i], new object[] { combination[i] });
            }
        }

        // Call Solve()
        type.GetMethod("Solve", AllFlags)?.Invoke(target, null);

        // Ensure controller fires SetSolved
        object controller = GetField<object>(target, "_puzzleController");
        if (controller == null)
        {
            var mb = target as MonoBehaviour;
            if (mb != null)
            {
                Type controllerType = FindType("PuzzleModeController");
                if (controllerType != null)
                {
                    controller = mb.GetComponent(controllerType) ??
                                 mb.GetComponentInParent(controllerType);
                    if (controller != null)
                        SetField(target, "_puzzleController", controller);
                }
            }
        }

        if (controller != null)
        {
            var isSolvedProp = controller.GetType().GetProperty("IsSolved", PublicFlags);
            bool isSolved = isSolvedProp != null && (bool)isSolvedProp.GetValue(controller);
            if (!isSolved)
                return FireOrDefer(controller, displayName);
        }

        return Solved($"{displayName} solved (no event controller)");
    }

    /// <summary>Sets _isSolved, calls an extra method, fires OnPuzzleSolved UnityEvent.</summary>
    private static CheatResult SolveSimplePuzzle(string typeName, string displayName,
        string extraMethod = null)
    {
        Type type = FindType(typeName);
        if (type == null) return Failed($"{typeName} type not found");
        UnityEngine.Object instance = FindFirst(type);
        if (instance == null) return Failed($"{typeName} not found in scene");

        SetField(instance, "_isSolved", true);
        if (extraMethod != null)
            type.GetMethod(extraMethod, InstanceFlags)?.Invoke(instance, null);

        return FireEventOrDefer(instance, type, displayName);
    }

    /// <summary>Calls a public or private AutoSolve method.</summary>
    private static CheatResult SolveByAutoSolve(string typeName, string displayName,
        string checkSolvedProperty = null)
    {
        Type type = FindType(typeName);
        if (type == null) return Failed($"{typeName} type not found");
        UnityEngine.Object instance = FindFirst(type);
        if (instance == null) return Failed($"{typeName} not found in scene");

        if (checkSolvedProperty != null)
        {
            var prop = type.GetProperty(checkSolvedProperty, PublicFlags);
            bool isSolved = prop != null && (bool)prop.GetValue(instance);
            if (isSolved) return Solved($"{displayName} is already solved");
        }

        var autoSolve = type.GetMethod("AutoSolve", AllFlags);
        if (autoSolve != null)
        {
            autoSolve.Invoke(instance, null);
            return Solved($"{displayName} auto-solved!");
        }

        return Failed($"{typeName}.AutoSolve() method not found");
    }

    /// <summary>Runs a pre-action, calls the solve method, and fires controller.SetSolved if needed.</summary>
    private static CheatResult SolveLockSystem(string typeName, string displayName,
        string solveMethod, Action<object, Type> preAction = null)
    {
        Type type = FindType(typeName);
        if (type == null) return Failed($"{typeName} type not found");

        UnityEngine.Object[] instances = FindAll(type);
        if (instances == null || instances.Length == 0)
            return Failed($"No {typeName} found in scene");

        int count = 0;
        foreach (var instance in instances)
        {
            preAction?.Invoke(instance, type);

            type.GetMethod(solveMethod, AllFlags)?.Invoke(instance, null);

            object controller = GetField<object>(instance, "_puzzleController");
            if (controller == null)
            {
                var mb = instance as MonoBehaviour;
                if (mb != null)
                {
                    Type controllerType = FindType("PuzzleModeController");
                    if (controllerType != null)
                    {
                        controller = mb.GetComponent(controllerType) ??
                                     mb.GetComponentInParent(controllerType);
                        if (controller != null)
                            SetField(instance, "_puzzleController", controller);
                    }
                }
            }

            if (controller != null)
            {
                var isSolvedProp = controller.GetType().GetProperty("IsSolved", PublicFlags);
                bool isSolved = isSolvedProp != null && (bool)isSolvedProp.GetValue(controller);
                if (!isSolved)
                    controller.GetType().GetMethod("SetSolved", PublicFlags)?
                        .Invoke(controller, null);
            }

            count++;
        }

        return Solved($"Processed {count} {displayName}(s)");
    }

    // ── Event firing helpers ──────────────────────────────────────────────

    private static CheatResult FireEventOrDefer(object instance, Type type, string displayName)
    {
        Action fireAction = () =>
        {
            var eventValue = type.GetField("OnPuzzleSolved", AllFlags)?.GetValue(instance);
            (eventValue as UnityEvent)?.Invoke();
            SaveManager.Instance?.Save();
        };

        if (Time.timeScale == 0f)
            return Pending($"{displayName} marked as solved. Event fires on unpause.", fireAction);

        fireAction();
        return Solved($"{displayName} solved!");
    }

    private static CheatResult FireOrDefer(object controller, string displayName)
    {
        Action fireAction = () =>
        {
            controller.GetType().GetMethod("SetSolved", PublicFlags)?.Invoke(controller, null);
            SaveManager.Instance?.Save();
        };

        if (Time.timeScale == 0f)
            return Pending($"{displayName} marked as solved. Event fires on unpause.", fireAction);

        fireAction();
        return Solved($"{displayName} solved!");
    }

    // ── Result factories ──────────────────────────────────────────────────

    private static CheatResult Solved(string msg) =>
        new() { Success = true, Message = msg };

    private static CheatResult Failed(string msg) =>
        new() { Success = false, Message = msg };

    private static CheatResult Pending(string msg, Action deferred) =>
        new() { Success = true, Message = msg, DeferredAction = deferred };

    // ── Reflection helpers ────────────────────────────────────────────────

    /// <summary>Searches all loaded assemblies for a type by full or simple name.</summary>
    private static Type FindType(string typeName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(typeName);
            if (type != null) return type;
        }
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (var t in assembly.GetTypes())
            {
                if (t.Name == typeName) return t;
            }
        }
        return null;
    }

    private static UnityEngine.Object FindFirst(Type type)
    {
        UnityEngine.Object[] objects = FindAll(type);
        return objects != null && objects.Length > 0 ? objects[0] : null;
    }

    private static UnityEngine.Object[] FindAll(Type type)
    {
#pragma warning disable CS0618
        return UnityEngine.Object.FindObjectsOfType(type);
#pragma warning restore CS0618
    }

    private static T GetField<T>(object obj, string name)
    {
        var field = obj.GetType().GetField(name, InstanceFlags);
        return field != null ? (T)field.GetValue(obj) : default;
    }

    private static void SetField(object obj, string name, object value)
    {
        obj.GetType().GetField(name, InstanceFlags)?.SetValue(obj, value);
    }
}

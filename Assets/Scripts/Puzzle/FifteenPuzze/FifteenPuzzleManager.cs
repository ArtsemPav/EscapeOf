using UnityEngine;
using System.Collections.Generic;
using System.Collections;

namespace PuzzleGame
{
    /// <summary>
    /// Manages the sliding puzzle logic, including grid generation, shuffling, and win conditions.
    /// </summary>
    public class FifteenPuzzleManager : MonoBehaviour, ISaveable
    {
        [Header("Save")]
        [SerializeField] private string _saveId = "fifteen_puzzle";

        [Header("Grid Settings")]
        [SerializeField] private int width = 3;
        [SerializeField] private int height = 3;
        [SerializeField] private float spacing = 0.22f; // Distance between tile centers

        [Header("Setup")]
        [SerializeField] private List<FifteenPuzzleElement> elements = new List<FifteenPuzzleElement>();
        [SerializeField] private GameObject lastElementPrefab; // The last tile to show on win
        [SerializeField] private ItemData rewardItemData;      // Item to add to inventory on restore (skips LastElement spawn)

        [Header("Visual Settings")]
        [SerializeField] private bool useImageAtlas = true;
        [SerializeField] private Material puzzleMaterial;

        private FifteenPuzzleElement[,] grid;
        private Vector2Int emptyPosition;
        private bool isShuffling;
        private GameObject spawnedLastElement;

        private bool isPuzzleSolved;
        private bool isLoadedAsSolved;

        private void Awake()
        {
            SaveManager.Instance?.Register(this);
        }

        private void OnDestroy()
        {
            SaveManager.Instance?.Unregister(this);
        }

        private void Start()
        {
            isPuzzleSolved = false;

            // InitializeGrid always runs so elements are properly set up
            InitializeGrid();

            if (useImageAtlas && puzzleMaterial != null)
            {
                ApplyAtlasToElements();
            }

            PrepareLastElement();

            if (isLoadedAsSolved)
            {
                RestoreSolvedState();
            }
            else
            {
                StartCoroutine(DelayedShuffle());
            }
        }

        private void RestoreSolvedState()
        {
            isPuzzleSolved = true;

            // Place each element at its target (solved) position without shuffling
            elements.Sort((a, b) => a.TargetIndex.CompareTo(b.TargetIndex));
            for (int i = 0; i < elements.Count; i++)
            {
                int targetX = elements[i].TargetIndex % width;
                int targetY = elements[i].TargetIndex / width;

                // Update logical grid
                grid[targetX, targetY] = elements[i];
                elements[i].GridPosition = new Vector2Int(targetX, targetY);
                elements[i].transform.localPosition = GetWorldPosition(targetX, targetY);

                Collider col = elements[i].GetComponent<Collider>();
                if (col != null) col.enabled = false;
            }

            // The last grid cell (bottom-right) is always the empty slot after solving
            emptyPosition = new Vector2Int(width - 1, height - 1);
            grid[emptyPosition.x, emptyPosition.y] = null;

            // Do NOT spawn LastElement — its PickableItem would be immediately destroyed by
            // the save system (collected=true). Instead, add the reward item directly to the
            // inventory if it is not already there.
            if (rewardItemData != null && InventorySystem.Instance != null
                && !InventorySystem.Instance.HasItem(rewardItemData))
            {
                if (!InventorySystem.Instance.AddItem(rewardItemData))
                    Debug.LogWarning($"[PuzzleManager] Could not restore '{rewardItemData.itemName}' — inventory is full.");
                else
                    Debug.Log($"[PuzzleManager] Restored: added '{rewardItemData.itemName}' to inventory.");
            }

            Debug.Log("[PuzzleManager] Restored solved state from save.");
        }

        private void PrepareLastElement()
        {
            if (lastElementPrefab == null) return;

            // Instantiate the last element but keep it hidden initially
            spawnedLastElement = Instantiate(lastElementPrefab, transform);
            spawnedLastElement.SetActive(false);

            // Set its position to the empty slot's initial world position
            spawnedLastElement.transform.localPosition = GetWorldPosition(width - 1, height - 1);
        }

        private void ApplyAtlasToElements()
        {
            float uvWidth = 1f / width;
            float uvHeight = 1f / height;

            foreach (var element in elements)
            {
                MeshRenderer renderer = element.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    // Create a unique material instance for each tile to set individual offsets
                    Material instanceMaterial = new Material(puzzleMaterial);
                    renderer.material = instanceMaterial;

                    // Calculate position in the grid for the target (solved) state
                    int targetX = element.TargetIndex % width;
                    int targetY = element.TargetIndex / width;

                    // Set Tiling
                    instanceMaterial.mainTextureScale = new Vector2(uvWidth, uvHeight);
                    
                    // Set Offset (Y is inverted because UV (0,0) is bottom-left, but grid (0,0) is top-left)
                    float offsetX = targetX * uvWidth;
                    float offsetY = 1f - (targetY + 1) * uvHeight;
                    instanceMaterial.mainTextureOffset = new Vector2(offsetX, offsetY);
                }
            }
        }

        private void InitializeGrid()
        {
            grid = new FifteenPuzzleElement[width, height];
            
            // If elements are not pre-assigned, try to find them in children
            if (elements.Count == 0)
            {
                elements.AddRange(GetComponentsInChildren<FifteenPuzzleElement>());
            }

            int index = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (index < elements.Count)
                    {
                        FifteenPuzzleElement element = elements[index];
                        element.Initialize(this, new Vector2Int(x, y), index);
                        grid[x, y] = element;
                        
                        // Set initial position based on grid
                        element.transform.localPosition = GetWorldPosition(x, y);
                        index++;
                    }
                    else
                    {
                        // The last cell (or remaining) is empty
                        emptyPosition = new Vector2Int(x, y);
                        grid[x, y] = null;
                    }
                }
            }
        }

        private IEnumerator DelayedShuffle()
        {
            isShuffling = true;
            yield return new WaitForSeconds(1f);
            Shuffle(width * height * 10);
            isShuffling = false;
        }

        /// <summary>
        /// Attempts to move a tile into the adjacent empty space.
        /// </summary>
        public bool TryMoveElement(FifteenPuzzleElement element)
        {
            if (isShuffling || isPuzzleSolved) return false;

            Vector2Int pos = element.GridPosition;
            if (IsAdjacent(pos, emptyPosition))
            {
                MoveElementToEmpty(element);
                CheckWinCondition();
                return true;
            }
            return false;
        }

        private void MoveElementToEmpty(FifteenPuzzleElement element)
        {
            Vector2Int oldPos = element.GridPosition;
            
            // Swap in logical grid
            grid[emptyPosition.x, emptyPosition.y] = element;
            grid[oldPos.x, oldPos.y] = null;
            
            // Update positions
            element.GridPosition = emptyPosition;
            emptyPosition = oldPos;
            
            // Visual move
            element.MoveTo(transform.TransformPoint(GetWorldPosition(element.GridPosition.x, element.GridPosition.y)));
        }

        private bool IsAdjacent(Vector2Int p1, Vector2Int p2)
        {
            return (Mathf.Abs(p1.x - p2.x) == 1 && p1.y == p2.y) ||
                   (Mathf.Abs(p1.y - p2.y) == 1 && p1.x == p2.x);
        }

        private Vector3 GetWorldPosition(int x, int y)
        {
            // Grid layout centered around local origin (X and Y axis)
            float offsetX = (width - 1) * spacing * 0.5f;
            float offsetY = (height - 1) * spacing * 0.5f;
            // Changed from (x, 0, -y) to (x, y, 0) logic
            return new Vector3(x * spacing - offsetX, -y * spacing + offsetY, 0);
        }

        private void Shuffle(int moves)
        {
            for (int i = 0; i < moves; i++)
            {
                List<FifteenPuzzleElement> neighbors = GetNeighbors(emptyPosition);
                if (neighbors.Count > 0)
                {
                    FifteenPuzzleElement randomNeighbor = neighbors[Random.Range(0, neighbors.Count)];
                    
                    // Instant swap for shuffle
                    Vector2Int oldPos = randomNeighbor.GridPosition;
                    grid[emptyPosition.x, emptyPosition.y] = randomNeighbor;
                    grid[oldPos.x, oldPos.y] = null;
                    randomNeighbor.GridPosition = emptyPosition;
                    emptyPosition = oldPos;
                    randomNeighbor.transform.localPosition = GetWorldPosition(randomNeighbor.GridPosition.x, randomNeighbor.GridPosition.y);
                }
            }
        }

        private List<FifteenPuzzleElement> GetNeighbors(Vector2Int pos)
        {
            List<FifteenPuzzleElement> neighbors = new List<FifteenPuzzleElement>();
            Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
            
            foreach (var dir in dirs)
            {
                Vector2Int nPos = pos + dir;
                if (nPos.x >= 0 && nPos.x < width && nPos.y >= 0 && nPos.y < height)
                {
                    if (grid[nPos.x, nPos.y] != null)
                        neighbors.Add(grid[nPos.x, nPos.y]);
                }
            }
            return neighbors;
        }

        private void CheckWinCondition()
        {
            int index = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (grid[x, y] != null)
                    {
                        if (grid[x, y].TargetIndex != index) return;
                    }
                    else if (index != width * height - 1) return;
                    
                    index++;
                }
            }
            OnPuzzleSolved();
        }

        private void OnPuzzleSolved()
        {
            Debug.Log("Puzzle Solved!");
            isPuzzleSolved = true;
            
            // Disable interaction for all elements
            foreach (var element in elements)
            {
                if (element != null)
                {
                    Collider col = element.GetComponent<Collider>();
                    if (col != null) col.enabled = false;
                }
            }
            
            // Show the last element to complete the image
            if (spawnedLastElement != null)
            {
                // Ensure it's in the correct final position (the empty slot)
                spawnedLastElement.transform.localPosition = GetWorldPosition(emptyPosition.x, emptyPosition.y);
                spawnedLastElement.SetActive(true);
            }

            SaveManager.Instance?.Save();
        }

        // ── ISaveable ─────────────────────────────────────────────────────────────

        public string SaveId => _saveId;

        public string GetSaveData()
        {
            return JsonUtility.ToJson(new SaveData { isSolved = isPuzzleSolved });
        }

        public void LoadSaveData(string json)
        {
            var data = JsonUtility.FromJson<SaveData>(json);
            isLoadedAsSolved = data.isSolved;
        }

        [System.Serializable]
        private struct SaveData
        {
            public bool isSolved;
        }

        /// <summary>
        /// Automatically solves the puzzle by placing all tiles in their target positions.
        /// </summary>
        public void AutoSolve()
        {
            if (isPuzzleSolved) return;

            // Stop any ongoing shuffle
            StopAllCoroutines();
            isShuffling = false;

            // Sort elements by their target index to easily place them
            elements.Sort((a, b) => a.TargetIndex.CompareTo(b.TargetIndex));

            // Reset the grid
            grid = new FifteenPuzzleElement[width, height];
            int index = 0;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (index < elements.Count)
                    {
                        FifteenPuzzleElement element = elements[index];
                        element.GridPosition = new Vector2Int(x, y);
                        grid[x, y] = element;
                        
                        // Instantly move to the correct local position
                        element.transform.localPosition = GetWorldPosition(x, y);
                        index++;
                    }
                    else
                    {
                        // The last cell is empty
                        emptyPosition = new Vector2Int(x, y);
                        grid[x, y] = null;
                    }
                }
            }

            CheckWinCondition();
        }

    }
}

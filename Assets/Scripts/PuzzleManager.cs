using UnityEngine;
using System.Collections.Generic;
using System.Collections.Generic;
using System.Collections;

namespace PuzzleGame
{
    /// <summary>
    /// Manages the sliding puzzle logic, including grid generation, shuffling, and win conditions.
    /// </summary>
    public class PuzzleManager : MonoBehaviour
    {
        [Header("Grid Settings")]
        [SerializeField] private int width = 3;
        [SerializeField] private int height = 3;
        [SerializeField] private float spacing = 0.22f; // Distance between tile centers

        [Header("Setup")]
        [SerializeField] private List<PuzzleElement> elements = new List<PuzzleElement>();
        [SerializeField] private GameObject lastElementPrefab; // The last tile to show on win

        [Header("Visual Settings")]
        [SerializeField] private bool useImageAtlas = true;
        [SerializeField] private Material puzzleMaterial;

        private PuzzleElement[,] grid;
        private Vector2Int emptyPosition;
        private bool isShuffling;
        private GameObject spawnedLastElement;

        private bool isPuzzleSolved;

        private void Start()
        {
            isPuzzleSolved = false;
            InitializeGrid();
            if (useImageAtlas && puzzleMaterial != null)
            {
                ApplyAtlasToElements();
            }
            PrepareLastElement();
            StartCoroutine(DelayedShuffle());
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
            grid = new PuzzleElement[width, height];
            
            // If elements are not pre-assigned, try to find them in children
            if (elements.Count == 0)
            {
                elements.AddRange(GetComponentsInChildren<PuzzleElement>());
            }

            int index = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (index < elements.Count)
                    {
                        PuzzleElement element = elements[index];
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
        public bool TryMoveElement(PuzzleElement element)
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

        private void MoveElementToEmpty(PuzzleElement element)
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
                List<PuzzleElement> neighbors = GetNeighbors(emptyPosition);
                if (neighbors.Count > 0)
                {
                    PuzzleElement randomNeighbor = neighbors[Random.Range(0, neighbors.Count)];
                    
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

        private List<PuzzleElement> GetNeighbors(Vector2Int pos)
        {
            List<PuzzleElement> neighbors = new List<PuzzleElement>();
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
        }
    }
}

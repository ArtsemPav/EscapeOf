using UnityEngine;
using System.Collections;
using TMPro;

namespace PuzzleGame
{
    /// <summary>
    /// Represents a single tile in the 3D sliding puzzle.
    /// Handles player interaction via mouse click and movement animation.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class FifteenPuzzleElement : MonoBehaviour, IInteractable
    {
        [Header("Movement Settings")]
        [SerializeField] private float moveSpeed = 10f;

        [Header("Debug Settings")]
        [SerializeField] private TextMeshPro numberText;
        [SerializeField] private bool showDebugText = false;
        
        private FifteenPuzzleManager manager;
        private Vector2Int gridPosition;
        private int targetIndex;
        private bool isMoving;

        public Vector2Int GridPosition 
        { 
            get => gridPosition; 
            set => gridPosition = value; 
        }
        
        public int TargetIndex 
        { 
            get => targetIndex; 
            set => targetIndex = value; 
        }

        public void Initialize(FifteenPuzzleManager puzzleManager, Vector2Int startPos, int index)
        {
            manager = puzzleManager;
            gridPosition = startPos;
            targetIndex = index;

            // Update and show/hide debug text if assigned
            if (numberText != null)
            {
                numberText.text = (targetIndex + 1).ToString();
                numberText.gameObject.SetActive(showDebugText);
            }
        }

        #region IInteractable Implementation

        public void Interact()
        {
            if (isMoving || manager == null) return;
            manager.TryMoveElement(this);
        }

        public string GetInteractText() => "Move Tile";
        public bool IsPickable() => false;
        public bool UseLMBClick => true; // Allows interaction via left mouse click
        public CrosshairMode GetCrosshairMode() => CrosshairMode.Hand;

        #endregion

        /* Removed OnMouseDown to use IInteractable */
        // private void OnMouseDown() { ... }


        /// <summary>
        /// Moves the element to a new world position smoothly.
        /// </summary>
        /// <param name="targetWorldPos">Destination in world space.</param>
        public void MoveTo(Vector3 targetWorldPos)
        {
            StopAllCoroutines();
            StartCoroutine(MoveRoutine(targetWorldPos));
        }

        private IEnumerator MoveRoutine(Vector3 targetWorldPos)
        {
            isMoving = true;
            
            while (Vector3.Distance(transform.position, targetWorldPos) > 0.001f)
            {
                transform.position = Vector3.Lerp(transform.position, targetWorldPos, Time.deltaTime * moveSpeed);
                yield return null;
            }
            
            transform.position = targetWorldPos;
            isMoving = false;
        }
    }
}

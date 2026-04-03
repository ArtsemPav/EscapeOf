using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace PuzzleGame
{
    /// <summary>
    /// Represents a single tile in the 3D sliding puzzle.
    /// Handles player interaction via mouse click and movement animation.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class PuzzleElement : MonoBehaviour, IInteractable
    {
        [Header("Movement Settings")]
        [SerializeField] private float moveSpeed = 10f;
        
        private PuzzleManager manager;
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

        public void Initialize(PuzzleManager puzzleManager, Vector2Int startPos, int index)
        {
            manager = puzzleManager;
            gridPosition = startPos;
            targetIndex = index;
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

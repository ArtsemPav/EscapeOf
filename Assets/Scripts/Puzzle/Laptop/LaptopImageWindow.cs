using UnityEngine;
using UnityEngine.UI;

namespace EscapeOf.Puzzle.Laptop
{
    /// <summary>Image viewer window with aspect ratio preservation and zoom functionality.</summary>
    public class LaptopImageWindow : LaptopWindow
    {
        [Header("References")]
        [SerializeField] private Image             _imageDisplay;
        [SerializeField] private AspectRatioFitter _aspectFitter;
        [SerializeField] private RectTransform     _zoomContainer;

        [Header("Settings")]
        [SerializeField] private float _minScale = 0.5f;
        [SerializeField] private float _maxScale = 3.0f;
        [SerializeField] private float _zoomStep = 0.2f;

        private float _currentScale = 1.0f;

        protected override void OnOpen(LaptopFileData file)
        {
            if (!(file is LaptopImageFile imageFile)) return;

            _imageDisplay.sprite = imageFile.image;
            ResetZoom();

            if (_aspectFitter != null && imageFile.image != null)
            {
                float ratio = (float)imageFile.image.texture.width / imageFile.image.texture.height;
                _aspectFitter.aspectRatio = ratio;
            }
        }

        /// <summary>Increases image scale.</summary>
        public void ZoomIn()
        {
            SetScale(_currentScale + _zoomStep);
        }

        /// <summary>Decreases image scale.</summary>
        public void ZoomOut()
        {
            SetScale(_currentScale - _zoomStep);
        }

        /// <summary>Resets scale to default 1:1.</summary>
        public void ResetZoom()
        {
            SetScale(1.0f);
        }

        private void SetScale(float targetScale)
        {
            _currentScale = Mathf.Clamp(targetScale, _minScale, _maxScale);
            if (_zoomContainer != null)
            {
                _zoomContainer.localScale = new Vector3(_currentScale, _currentScale, 1f);
            }
        }
    }
}

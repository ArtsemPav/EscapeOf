using UnityEngine;
using UnityEngine.Video;

namespace EscapeOf.Puzzle.Laptop
{
    /// <summary>Video file for the laptop OS.</summary>
    [CreateAssetMenu(menuName = "Laptop/Video File", fileName = "NewVideoFile")]
    public class LaptopVideoFile : LaptopFileData
    {
        [Tooltip("Video clip played in the video player window.")]
        public VideoClip clip;
    }
}

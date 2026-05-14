using UnityEngine;

namespace EscapeOf.Puzzle.Laptop
{
    /// <summary>Audio file for the laptop OS.</summary>
    [CreateAssetMenu(menuName = "Laptop/Audio File", fileName = "NewAudioFile")]
    public class LaptopAudioFile : LaptopFileData
    {
        [Tooltip("Audio clip played in the audio player window.")]
        public AudioClip clip;
    }
}

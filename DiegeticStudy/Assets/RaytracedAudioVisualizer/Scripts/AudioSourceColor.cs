using UnityEngine;

namespace RaytracedAudioVisualizerPlugin
{
    [RequireComponent(typeof(AudioSource))]
    public class AudioSourceColor : MonoBehaviour
    {
        [Header("Sonar Settings")]
        [Tooltip("The color this sound will generate in the sonar view.")]
        public Color cueColor = Color.green;

        [Tooltip("Multiplies the visual intensity of this specific sound.")]
        [Range(0f, 2f)]
        public float intensityMultiplier = 1.0f;

        private void Reset()
        {
            cueColor = Color.HSVToRGB(Random.value, 0.8f, 1f);
        }
    }
}
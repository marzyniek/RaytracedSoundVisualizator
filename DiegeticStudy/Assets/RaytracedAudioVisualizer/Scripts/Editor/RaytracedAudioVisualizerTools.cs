using UnityEngine;
using UnityEditor;

namespace RaytracedAudioVisualizerPlugin
{
    public class RaytracedAudioVisualizerTools : Editor
    {
        [MenuItem("Tools/AudioVis Plugin/Setup Audio Sources")]
        public static void SetupAudioSources()
        {
            var allAudioSources = Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
            
            int addedCount = 0;
            int existingCount = 0;

            foreach (var source in allAudioSources)
            {
                var existingComponent = source.GetComponent<AudioSourceColor>();
                
                if (existingComponent != null)
                {
                    existingCount++;
                    continue;
                }

                var newComponent = Undo.AddComponent<AudioSourceColor>(source.gameObject);
                
                newComponent.cueColor = Color.HSVToRGB(Random.value, Random.Range(0.7f, 1f), 1f);
                
                addedCount++;
            }

            Debug.Log(addedCount > 0
                ? $"<color=#00FF00><b>[AudioVis Plugin]</b></color> Successfully setup {addedCount} new Audio Sources! ({existingCount} were already setup)."
                : $"<color=#FFFF00><b>[AudioVis Plugin]</b></color> No new Audio Sources found to setup.");
        }
    }
}
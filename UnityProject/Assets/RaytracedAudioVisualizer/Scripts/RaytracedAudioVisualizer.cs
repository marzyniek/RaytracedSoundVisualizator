using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Random = UnityEngine.Random;

namespace RaytracedAudioVisualizerPlugin
{
    public class RaytracedAudioVisualizer : MonoBehaviour
    {
        [Header("Settings")] [SerializeField] private LayerMask obstacleMask;
        [SerializeField] private float scanRadius = 50f;
        [SerializeField] private float maxRayLength = 50f;
        [Range(1, 5000)] [SerializeField] private int rayCount = 1000;
        [Range(0, 8)] [SerializeField] private int maxBounces = 2;
        [Range(0f, 1f)] [SerializeField] private float bounceEnergyLossMultiplier = 0.8f;

        [Header("Visualization")] [SerializeField]
        private Mesh dotMesh;

        [SerializeField] private Material dotMaterial;
        [SerializeField] private int maxDotCapacity = 20000;

        [Header("Debug")] [SerializeField] private bool showDebugGizmos = true;

        [Header("Acoustics")] [SerializeField] private float maxExpectedWallThickness = 2.0f;

        [SerializeField] private List<LayerAcousticConfig> layerConfigs = new();
        private readonly uint[] _args = new uint[5] { 0, 0, 0, 0, 0 };
        private ComputeBuffer _argsBuffer;
        private int _bufferHeadIndex;

        private readonly List<DebugLine> _debugLines = new();

        private ComputeBuffer _dotBuffer;

        private Material _instancedMaterial;
        private DotData[] _localDataBuffer;

        private NativeArray<LayerAcousticData> _nativeLayerData;

        private NativeArray<RaycastCommand> _probeCommands;
        private NativeArray<RaycastHit> _probeResults;

        private void Awake()
        {
            InitializeBuffers();
            InitializeMaterial();
        }

        private void Update()
        {
            var activeSources = CollectAudioSources();
            if (activeSources.Count > 0) SimulateRays(activeSources);

            RenderDots();
        }

        private void OnDestroy()
        {
            _dotBuffer?.Release();
            _argsBuffer?.Release();
            if (_probeCommands.IsCreated) _probeCommands.Dispose();
            if (_probeResults.IsCreated) _probeResults.Dispose();
            if (_nativeLayerData.IsCreated) _nativeLayerData.Dispose();
            if (_instancedMaterial) Destroy(_instancedMaterial);
        }

        private void OnDrawGizmos()
        {
            if (!showDebugGizmos || _debugLines == null) return;

            foreach (var line in _debugLines)
            {
                Gizmos.color = line.color;
                Gizmos.DrawLine(line.start, line.end);
            }
        }

        private void InitializeBuffers()
        {
            var stride = Marshal.SizeOf(typeof(DotData));
            _dotBuffer = new ComputeBuffer(maxDotCapacity, stride, ComputeBufferType.Default);
            _argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
            _localDataBuffer = new DotData[maxDotCapacity];

            for (var i = 0; i < maxDotCapacity; i++) _localDataBuffer[i].startTime = -999f;
            _dotBuffer.SetData(_localDataBuffer);

            _probeCommands = new NativeArray<RaycastCommand>(rayCount, Allocator.Persistent);
            _probeResults = new NativeArray<RaycastHit>(rayCount, Allocator.Persistent);

            _nativeLayerData = new NativeArray<LayerAcousticData>(32, Allocator.Persistent);

            for (var i = 0; i < 32; i++)
                _nativeLayerData[i] = new LayerAcousticData { transmissionChance = 0f, attenuation = 0f };

            foreach (var config in layerConfigs)
                _nativeLayerData[config.layerIndex] = new LayerAcousticData
                {
                    transmissionChance = config.transmissionChance,
                    attenuation = config.attenuationPerUnit
                };
        }

        private void InitializeMaterial()
        {
            if (dotMaterial == null) return;
            _instancedMaterial = new Material(dotMaterial);
            _instancedMaterial.EnableKeyword("PROCEDURAL_INSTANCING_ON");
        }

        private List<ActiveSource> CollectAudioSources()
        {
            var sources = new List<ActiveSource>();

            var allAudio = FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
            foreach (var audioSource in allAudio)
            {
                if (!audioSource.isPlaying) continue;

                var dist = Vector3.Distance(transform.position, audioSource.transform.position);
                if (dist > scanRadius || dist > audioSource.maxDistance) continue;

                Color finalColor;
                var intensity = 1.0f;

                if (audioSource.TryGetComponent<AudioSourceColor>(out var colorComp))
                {
                    finalColor = colorComp.cueColor;
                    intensity = colorComp.intensityMultiplier;
                }
                else
                {
                    var hue = Mathf.Repeat(audioSource.GetInstanceID() * 0.1f, 1f);
                    finalColor = Color.HSVToRGB(hue, 1f, 1f);
                }

                sources.Add(new ActiveSource
                {
                    position = audioSource.transform.position,
                    range = audioSource.maxDistance,
                    color = finalColor
                });
            }

            return sources;
        }

        private void SimulateRays(List<ActiveSource> sources)
        {
            if (showDebugGizmos) _debugLines.Clear();
            var queryParams = QueryParameters.Default;
            queryParams.layerMask = obstacleMask;
            queryParams.hitTriggers = QueryTriggerInteraction.Ignore;

            var stepCommands = new List<NativeArray<RaycastCommand>>();
            var stepResults = new List<NativeArray<RaycastHit>>();

            for (var depth = 0; depth <= maxBounces; depth++)
            {
                NativeArray<RaycastCommand> commands;
                NativeArray<RaycastHit> results;

                if (depth == 0)
                {
                    for (var i = 0; i < rayCount; i++)
                    {
                        var dir = Random.onUnitSphere;

                        _probeCommands[i] =
                            new RaycastCommand(sources[i % sources.Count].position, dir, queryParams, maxRayLength);
                    }

                    commands = _probeCommands;
                    results = _probeResults;
                }
                else
                {
                    commands = new NativeArray<RaycastCommand>(rayCount, Allocator.TempJob);
                    results = new NativeArray<RaycastHit>(rayCount, Allocator.TempJob);

                    var prevResults = stepResults[depth - 1];
                    var prevCommands = stepCommands[depth - 1];

                    for (var i = 0; i < rayCount; i++)
                    {
                        var hit = prevResults[i];

                        if (hit.collider == null)
                        {
                            commands[i] = new RaycastCommand();
                            continue;
                        }

                        var incomingDir = prevCommands[i].direction;
                        var reflectDir = Vector3.Reflect(incomingDir, hit.normal);

                        var remainingDist = prevCommands[i].distance - hit.distance;
                        if (remainingDist <= 0.01f)
                        {
                            commands[i] = new RaycastCommand();
                            continue;
                        }

                        commands[i] = new RaycastCommand(
                            hit.point + hit.normal * 0.02f,
                            reflectDir,
                            queryParams,
                            remainingDist
                        );
                    }
                }

                var handle = RaycastCommand.ScheduleBatch(commands, results, 32);
                handle.Complete();

                stepCommands.Add(commands);
                stepResults.Add(results);
            }

            var uploadCount = 0;
            var startIndex = _bufferHeadIndex;

            for (var d = 0; d < stepResults.Count; d++)
            {
                var results = stepResults[d];
                var debugColor = Color.Lerp(Color.green, new Color(0.5f, 0f, 1f), d / (float)(d + 1));

                for (var i = 0; i < rayCount; i++)
                {
                    var hit = results[i];
                    var src = sources[i % sources.Count];

                    if (!hit.collider) continue;

                    if (showDebugGizmos)
                    {
                        var prevPoint =
                            d == 0 ? sources[i % sources.Count].position : stepResults[d - 1][i].point;
                        _debugLines.Add(new DebugLine
                        {
                            start = prevPoint,
                            end = hit.point,
                            color = debugColor
                        });
                    }

                    var energy = Mathf.Clamp(1f - Vector3.Distance(src.position, hit.point) / src.range, 0f, 1f);
                    energy = MathF.Pow(bounceEnergyLossMultiplier, d) * energy;
                    var intensity = Mathf.Clamp(1f - Vector3.Distance(src.position, transform.position) / src.range,
                        0.1f, 1f);
                    if (energy <= 0.05f) continue;

                    _localDataBuffer[_bufferHeadIndex] = new DotData
                    {
                        position = hit.point,
                        normal = hit.normal,
                        color = src.color,
                        startTime = Time.time,
                        energy = energy,
                        intensity = intensity
                    };

                    _bufferHeadIndex = (_bufferHeadIndex + 1) % maxDotCapacity;
                    uploadCount++;
                }

                if (uploadCount >= 5000) break;
            }

            UploadBufferData(startIndex, uploadCount);

            for (var d = 1; d < stepCommands.Count; d++)
            {
                if (stepCommands[d].IsCreated) stepCommands[d].Dispose();
                if (stepResults[d].IsCreated) stepResults[d].Dispose();
            }
        }

        private void UploadBufferData(int startIndex, int count)
        {
            if (count == 0) return;

            if (startIndex + count <= maxDotCapacity)
            {
                _dotBuffer.SetData(_localDataBuffer, startIndex, startIndex, count);
            }
            else
            {
                var firstBlock = maxDotCapacity - startIndex;
                var secondBlock = count - firstBlock;
                _dotBuffer.SetData(_localDataBuffer, startIndex, startIndex, firstBlock);
                _dotBuffer.SetData(_localDataBuffer, 0, 0, secondBlock);
            }
        }

        private void RenderDots()
        {
            if (!dotMesh || !_instancedMaterial) return;

            _args[0] = dotMesh.GetIndexCount(0);
            _args[1] = (uint)maxDotCapacity;
            _args[2] = dotMesh.GetIndexStart(0);
            _args[3] = dotMesh.GetBaseVertex(0);
            _argsBuffer.SetData(_args);

            _instancedMaterial.SetBuffer("_DotBuffer", _dotBuffer);
            _instancedMaterial.SetMatrix("_LocalToWorld", Matrix4x4.identity);

            Graphics.DrawMeshInstancedIndirect(dotMesh, 0, _instancedMaterial,
                new Bounds(Vector3.zero, Vector3.one * 10000), _argsBuffer);
        }

        [Serializable]
        public struct LayerAcousticConfig
        {
            [Tooltip("The Unity Layer this configuration applies to.")] [Range(0, 31)]
            public int layerIndex;

            [Tooltip("0 = Total Reflection (Solid Wall), 1 = Total Transmission (Air/Ghost)")] [Range(0f, 1f)]
            public float transmissionChance;

            [Tooltip("Energy lost per unit of thickness when passing through this material.")]
            public float attenuationPerUnit;
        }

        private struct DebugLine
        {
            public Vector3 start;
            public Vector3 end;
            public Color color;
        }

        private struct DotData
        {
            public Vector3 position;
            public float padding1;
            public Vector3 normal;
            public float padding2;
            public Vector4 color;
            public float startTime;
            public float energy;
            public float intensity;
            public float padding4;
        }

        private struct ActiveSource
        {
            public Vector3 position;
            public Vector4 color;
            public float range;
        }

        public struct LayerAcousticData
        {
            public float transmissionChance;
            public float attenuation;
        }

        public struct ProcessBounceJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<RaycastHit> previousHits;
            [ReadOnly] public NativeArray<RaycastCommand> previousCommands;
            [ReadOnly] public NativeArray<LayerAcousticData> layerData;

            public Unity.Mathematics.Random randomizer;
            public float maxThickness;

            public QueryParameters queryParams;

            [WriteOnly] public NativeArray<RaycastCommand> nextCommands;

            public void Execute(int index)
            {
                var hit = previousHits[index];
                var prevCmd = previousCommands[index];

                if (hit.colliderInstanceID == 0) // Did not hit anything
                {
                    nextCommands[index] = new RaycastCommand();
                    return;
                }

                // 1. Look up the acoustic properties for the layer we just hit
                var layer = 0; // Note: In a real implementation, you need a way to pass the layer. 
                // Since RaycastHit in jobs doesn't easily expose the layer directly without a collider reference, 
                // you might need to map colliderInstanceIDs to layers via a concurrent dictionary, 
                // OR use a unified layer for obstacles and vary properties by a different metric.
                // For simplicity, assuming we got the layer index:

                var material = layerData[layer];

                // 2. Stochastic Decision: Reflect or Penetrate?
                if (randomizer.NextFloat() > material.transmissionChance)
                {
                    // --- REFLECTION ---
                    var reflectDir = Vector3.Reflect(prevCmd.direction, hit.normal);
                    nextCommands[index] = new RaycastCommand(
                        hit.point + hit.normal * 0.02f,
                        reflectDir,
                        queryParams,
                        prevCmd.distance - hit.distance
                    );
                }
                else
                {
                    // --- PENETRATION (Prepare Backcast) ---
                    // We step forward through the object, and point the ray backwards to find the exit hole
                    var backcastOrigin = hit.point + prevCmd.direction * maxThickness;

                    nextCommands[index] = new RaycastCommand(
                        backcastOrigin,
                        -prevCmd.direction,
                        queryParams,
                        maxThickness
                    );
                }
            }
        }
    }
}
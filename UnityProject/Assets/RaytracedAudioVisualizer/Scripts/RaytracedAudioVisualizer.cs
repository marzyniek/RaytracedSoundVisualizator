using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Random = UnityEngine.Random;

namespace RaytracedAudioVisualizerPlugin
{
    public class RaytracedAudioVisualizer : MonoBehaviour
    {
        public enum RayState
        {
            Forward,
            ResolvingPenetration,
            Dead
        }

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

        [SerializeField] private float minEnergyThreshold = 0.01f;

        [Tooltip("Energy lost per unit of distance traveled through the air.")] [SerializeField]
        private float airAttenuationPerUnit = 0.005f;

        [SerializeField] private List<LayerAcousticConfig> layerConfigs = new();
        private readonly uint[] _args = new uint[5] { 0, 0, 0, 0, 0 };

        private readonly List<DebugLine> _debugLines = new();
        private ComputeBuffer _argsBuffer;
        private int _bufferHeadIndex;

        private AudioListener _currentListener;

        private ComputeBuffer _dotBuffer;

        private NativeArray<int> _expectedColliderIDs;

        private Material _instancedMaterial;
        private DotData[] _localDataBuffer;

        private NativeArray<LayerAcousticData> _nativeLayerData;

        private NativeArray<RaycastCommand> _probeCommands;
        private NativeArray<RaycastHit> _probeResults;
        private NativeArray<float> _rayEnergies;

        private NativeArray<RayState> _rayStates;

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
            if (_rayStates.IsCreated) _rayStates.Dispose();
            if (_rayEnergies.IsCreated) _rayEnergies.Dispose();
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

        private Vector3 GetListenerPosition()
        {
            if (_currentListener == null) _currentListener = FindFirstObjectByType<AudioListener>();

            // Fallback to transform.position if no listener exists in the scene
            return _currentListener != null ? _currentListener.transform.position : transform.position;
        }

        private void InitializeBuffers()
        {
            var stride = Marshal.SizeOf(typeof(DotData));
            _dotBuffer = new ComputeBuffer(maxDotCapacity, stride, ComputeBufferType.Default);
            _argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
            _localDataBuffer = new DotData[maxDotCapacity];

            for (var i = 0; i < maxDotCapacity; i++) _localDataBuffer[i].startTime = -999f;
            _dotBuffer.SetData(_localDataBuffer);

            _rayStates = new NativeArray<RayState>(rayCount, Allocator.Persistent);
            _rayEnergies = new NativeArray<float>(rayCount, Allocator.Persistent);

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

                var dist = Vector3.Distance(GetListenerPosition(), audioSource.transform.position);
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

            var stepStates = new List<NativeArray<RayState>>();
            var stepEnergies = new List<NativeArray<float>>();

            var allColliders = FindObjectsByType<Collider>(FindObjectsSortMode.None);
            var colliderToLayerMap = new NativeParallelHashMap<int, int>(allColliders.Length, Allocator.TempJob);

            foreach (var col in allColliders) colliderToLayerMap.TryAdd(col.GetInstanceID(), col.gameObject.layer);

            for (var depth = 0; depth <= maxBounces; depth++)
            {
                NativeArray<RaycastCommand> commands;
                NativeArray<RaycastHit> results;

                if (depth == 0)
                {
                    var initialStates = new NativeArray<RayState>(rayCount, Allocator.TempJob);
                    var initialEnergies = new NativeArray<float>(rayCount, Allocator.TempJob);

                    for (var i = 0; i < rayCount; i++)
                    {
                        initialEnergies[i] = 1.0f;
                        initialStates[i] = RayState.Forward;

                        var dir = Random.onUnitSphere;

                        _probeCommands[i] =
                            new RaycastCommand(sources[i % sources.Count].position, dir, queryParams, maxRayLength);
                    }

                    commands = _probeCommands;
                    results = _probeResults;
                    stepStates.Add(initialStates);
                    stepEnergies.Add(initialEnergies);
                }
                else
                {
                    commands = new NativeArray<RaycastCommand>(rayCount, Allocator.TempJob);
                    results = new NativeArray<RaycastHit>(rayCount, Allocator.TempJob);

                    var nextStates = new NativeArray<RayState>(rayCount, Allocator.TempJob);
                    var nextEnergies = new NativeArray<float>(rayCount, Allocator.TempJob);

                    var bounceJob = new ProcessBounceJob
                    {
                        previousHits = stepResults[depth - 1],
                        previousCommands = stepCommands[depth - 1],
                        previousStates = stepStates[depth - 1],
                        previousEnergies = stepEnergies[depth - 1],
                        layerData = _nativeLayerData,
                        colliderToLayerMap = colliderToLayerMap,
                        randomSeed = (uint)(Random.Range(1, 100000) + depth * 100),
                        maxThickness = maxExpectedWallThickness,
                        minEnergyThreshold = minEnergyThreshold,
                        airAttenuation = airAttenuationPerUnit,
                        queryParams = queryParams,
                        bounceEnergyLossMultiplier = bounceEnergyLossMultiplier,
                        nextCommands = commands,
                        nextStates = nextStates,
                        nextEnergies = nextEnergies
                    };

                    bounceJob.Schedule(rayCount, 32).Complete();
                    stepStates.Add(nextStates);
                    stepEnergies.Add(nextEnergies);
                }

                var handle = RaycastCommand.ScheduleBatch(commands, results, 32);
                handle.Complete();

                stepCommands.Add(commands);
                stepResults.Add(results);
            }

            colliderToLayerMap.Dispose();

            var uploadCount = 0;
            var startIndex = _bufferHeadIndex;

            for (var d = 0; d < stepResults.Count; d++)
            {
                var results = stepResults[d];
                var states = stepStates[d];
                var energies = stepEnergies[d];
                var commands = stepCommands[d];

                var debugColor = Color.Lerp(Color.green, new Color(0.5f, 0f, 1f), d / (float)(d + 1));

                for (var i = 0; i < rayCount; i++)
                {
                    var hit = results[i];
                    var state = states[i];
                    var energy = energies[i];
                    var cmd = commands[i];
                    var src = sources[i % sources.Count];

                    if (state == RayState.Forward && hit.collider)
                        energy -= hit.distance * airAttenuationPerUnit;

                    if (showDebugGizmos && state != RayState.Dead && energy > minEnergyThreshold)
                    {
                        var prevPoint = d == 0 ? src.position : stepResults[d - 1][i].point;

                        var endPoint = hit.collider ? hit.point : cmd.from + cmd.direction * cmd.distance;

                        var lineColor = state == RayState.ResolvingPenetration ? Color.cyan : debugColor;

                        _debugLines.Add(new DebugLine
                        {
                            start = prevPoint,
                            end = endPoint,
                            color = lineColor
                        });
                    }

                    if (state == RayState.ResolvingPenetration)
                        energy = d + 1 < stepEnergies.Count
                            ? stepEnergies[d + 1][i]
                            : 0f;

                    var displayNormal = hit.normal;

                    if (!hit.collider || state == RayState.Dead || energy <= minEnergyThreshold) continue;

                    var intensity = Mathf.Clamp(1f - Vector3.Distance(src.position, GetListenerPosition()) / src.range,
                        0.0f, 1f);

                    _localDataBuffer[_bufferHeadIndex] = new DotData
                    {
                        position = hit.point,
                        normal = displayNormal,
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

            for (var d = 0; d < stepStates.Count; d++)
            {
                if (stepStates[d].IsCreated) stepStates[d].Dispose();
                if (stepEnergies[d].IsCreated) stepEnergies[d].Dispose();
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

        [BurstCompile]
        public struct ProcessBounceJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<RaycastHit> previousHits;
            [ReadOnly] public NativeArray<RaycastCommand> previousCommands;
            [ReadOnly] public NativeArray<LayerAcousticData> layerData;
            [ReadOnly] public NativeParallelHashMap<int, int> colliderToLayerMap;
            [ReadOnly] public NativeArray<RayState> previousStates;
            [ReadOnly] public NativeArray<float> previousEnergies;

            public uint randomSeed;
            public float maxThickness;
            public float airAttenuation;
            public float minEnergyThreshold;
            public QueryParameters queryParams;
            public float bounceEnergyLossMultiplier;

            [WriteOnly] public NativeArray<RaycastCommand> nextCommands;
            [WriteOnly] public NativeArray<RayState> nextStates;
            [WriteOnly] public NativeArray<float> nextEnergies;

            public void Execute(int index)
            {
                var state = previousStates[index];
                var energy = previousEnergies[index];

                nextStates[index] = state;
                nextEnergies[index] = energy;

                if (state == RayState.Dead)
                {
                    nextCommands[index] = new RaycastCommand();
                    return;
                }

                var hit = previousHits[index];
                var prevCmd = previousCommands[index];

                if (state == RayState.Forward && hit.colliderInstanceID != 0) energy -= hit.distance * airAttenuation;

                if (energy <= minEnergyThreshold)
                {
                    nextStates[index] = RayState.Dead;
                    nextEnergies[index] = 0f;
                    nextCommands[index] = new RaycastCommand();
                    return;
                }

                nextStates[index] = state;
                nextEnergies[index] = energy;

                if (state == RayState.ResolvingPenetration)
                {
                    if (hit.colliderInstanceID != 0)
                    {
                        var thickness = maxThickness - hit.distance;
                        var layer = 3;
                        if (colliderToLayerMap.TryGetValue(hit.colliderInstanceID, out var foundLayer))
                            layer = foundLayer;

                        energy -= thickness * layerData[layer].attenuation;
                        nextEnergies[index] = energy;

                        if (energy <= minEnergyThreshold)
                        {
                            nextStates[index] = RayState.Dead;
                            nextCommands[index] = new RaycastCommand();
                            return;
                        }

                        nextCommands[index] = new RaycastCommand(
                            hit.point + -prevCmd.direction * 0.02f,
                            -prevCmd.direction, queryParams, maxThickness);

                        nextStates[index] = RayState.Forward;
                    }
                    else
                    {
                        nextStates[index] = RayState.Dead;
                        nextCommands[index] = new RaycastCommand();
                    }

                    return;
                }

                if (hit.colliderInstanceID == 0 || prevCmd.distance - hit.distance <= 0.01f)
                {
                    nextStates[index] = RayState.Dead;
                    nextCommands[index] = new RaycastCommand();
                    return;
                }

                var currentLayer = 3;
                if (colliderToLayerMap.TryGetValue(hit.colliderInstanceID, out var currentFoundLayer))
                    currentLayer = currentFoundLayer;

                var seed = randomSeed + (uint)index;
                var randomizer = new Unity.Mathematics.Random(seed == 0 ? 1 : seed);

                if (randomizer.NextFloat() > layerData[currentLayer].transmissionChance)
                {
                    nextEnergies[index] = energy * bounceEnergyLossMultiplier;
                    nextCommands[index] = new RaycastCommand(
                        hit.point + hit.normal * 0.02f,
                        Vector3.Reflect(prevCmd.direction, hit.normal),
                        queryParams, prevCmd.distance - hit.distance);
                }
                else
                {
                    nextCommands[index] = new RaycastCommand(
                        hit.point + prevCmd.direction * maxThickness,
                        -prevCmd.direction, queryParams, maxThickness);

                    nextStates[index] = RayState.ResolvingPenetration;
                }
            }
        }
    }
}
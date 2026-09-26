// ============================================================================
// ShapeWipe
// Two-phase alpha-mask screen transition with up to 200 shape instances.
// Optionally scales a UI RectTransform in sync.
// Compatible with Unity 2018.4+.
// ============================================================================

using UnityEngine;
using UnityEngine.Events;
using System.Collections;

namespace ShapeWipe
{
    public enum DelayOrder
    {
        RowMajor,
        ColumnMajor,
        Diagonal,
        RowPairs,
        RowConstant,
        ColumnConstant
    }

    public enum EndScaleAxis
    {
        Both,
        XOnly,
        YOnly
    }

    public enum EasingType
    {
        Linear,
        EaseIn,
        EaseOut,
        EaseInOut
    }

    public enum FlipMode
    {
        None,
        Horizontal,
        Vertical,
        Both
    }

    [System.Serializable]
    public class ShapeInstance
    {
        [Tooltip("Baseline screen UV position (0~1, origin at bottom-left).")]
        public Vector2 center = new Vector2(0.5f, 0.5f);

        [Tooltip("Baseline wipe offset applied as progress goes 0 -> 1.")]
        public Vector2 wipeOffset = Vector2.zero;

        [Tooltip("Starting size at progress = 0. X = width scale, Y = height scale.")]
        public Vector2 startScale = Vector2.zero;

        [Tooltip("Final size at progress = 1. X = width scale, Y = height scale.")]
        public Vector2 endScale = new Vector2(1f, 1f);

        [Tooltip("Starting rotation in degrees at progress = 0.")]
        [Range(-360f, 360f)]
        public float rotation = 0f;

        [Tooltip("Final rotation in degrees at progress = 1.")]
        [Range(-360f, 360f)]
        public float endRotation = 0f;

        [Tooltip("Delay in seconds before this shape starts to grow.")]
        public float delay = 0f;

        [Tooltip("Expand duration in seconds. 0 = auto (remaining time after delay).")]
        public float expandDuration = 0f;

        // Grid coordinate this shape was generated at. Used by FlipMode to find
        // its mirror counterpart. Filled by the Grid Generator.
        [HideInInspector] public Vector2Int gridPos = Vector2Int.zero;
    }

    [System.Serializable]
    public class GridGeneratorSettings
    {
        [Header("Layout")]
        public int columns = 10;
        public int rows = 10;

        [Range(0f, 0.4f)] public float marginX = 0.02f;
        [Range(0f, 0.4f)] public float marginY = 0.02f;

        public bool staggerRows = false;
        public bool snakeOrder = false;

        public float postOffsetX = 0f;
        public float postOffsetY = 0f;

        [Header("Timing")]
        public float baseDelay = 0f;
        public float delayStep = 0.02f;

        public float rowGroupStep = 0.2f;

        public DelayOrder delayOrder = DelayOrder.RowPairs;

        public float staggerDelayCompensation = 0f;

        public float expand = 0.08f;

        [Header("Size")]
        [Range(0f, 1f)] public float startX = 0f;
        [Range(0f, 1f)] public float startY = 0f;

        public float endScaleX = 1f;
        public float endScaleY = 1f;

        [Header("Rotation (degrees)")]
        public float startRotation = 0f;
        public float endRotation = 0f;

        [Header("Offset (applied to every generated shape)")]
        public float offsetX = 0f;
        public float offsetY = 0f;
    }

    [AddComponentMenu("Shape Wipe/Screen Transition")]
    [DisallowMultipleComponent]
    public class ShapeWipe : MonoBehaviour
    {
        public const int MAX_SHAPES = 200;

        // ====================================================================
        // Inspector fields
        // ====================================================================

        // ---- Mask ----
        public Material maskMaterial;

        public ShapeInstance[] shapes = new ShapeInstance[]
        {
            new ShapeInstance
            {
                center = new Vector2(0.5f, 0.5f),
                wipeOffset = Vector2.zero,
                startScale = Vector2.zero,
                endScale = new Vector2(1f, 1f),
                rotation = 0f,
                endRotation = 0f,
                delay = 0f,
                expandDuration = 0f
            }
        };

        // ---- Playback ----
        public float closeDuration = 2.0f;
        public float openDuration = 2.0f;
        public float holdDuration = 0.4f;

        public EasingType easing = EasingType.Linear;

        public bool reverseClosePhase = false;
        public bool reverseOpenPhase = false;

        public FlipMode closeFlip = FlipMode.None;
        public FlipMode openFlip = FlipMode.None;

        public RectTransform scaleTarget;
        public float scaleInDuration = 0.3f;
        public float scaleOutDuration = 0.3f;

        // ---- Runtime Randomization ----
        public bool randomizePosition = true;
        public float randomOffsetX = 0.05f;
        public float randomOffsetY = 0.05f;

        public bool randomizeRotation = true;
        public float randomRotationMin = -20f;
        public float randomRotationMax = 20f;

        public bool randomizeDelay = false;
        public float randomDelayMin = 0f;
        public float randomDelayMax = 0.15f;

        public bool randomizeEndScale = false;
        public EndScaleAxis randomEndScaleAxis = EndScaleAxis.Both;
        public float randomEndScaleMin = 0.8f;
        public float randomEndScaleMax = 1.2f;
        public bool randomEndScaleUniform = true;

        public bool randomizeEachPhase = false;

        // ---- Events ----
        public UnityEvent onScreenCovered;
        public UnityEvent onTransitionDone;

        public KeyCode testKey = KeyCode.M;

        // ---- Grid Generator ----
        public GridGeneratorSettings gridSettings = new GridGeneratorSettings();

        // ====================================================================
        // Runtime state
        // ====================================================================

        private int _progressID, _aspectID, _modeID;
        private int _shapeDataID, _shapeTargetID, _shapeMotionID, _shapeRotationID;
        private int _activeCountID;

        private Vector2[] _rtOffset = new Vector2[MAX_SHAPES];
        private float[] _rtRotation = new float[MAX_SHAPES];
        private float[] _rtDelay = new float[MAX_SHAPES];
        private float[] _rtEndScaleX = new float[MAX_SHAPES];
        private float[] _rtEndScaleY = new float[MAX_SHAPES];

        // Upload buffers. Reallocated only when the shape count changes.
        private Vector4[] _shapeData = new Vector4[0];
        private Vector4[] _shapeTarget = new Vector4[0];
        private Vector4[] _shapeMotion = new Vector4[0];
        private Vector4[] _shapeRotation = new Vector4[0];
        private int _uploadCount = -1;

        private float[] _delayGrid = new float[MAX_SHAPES];

        private bool _isPlaying = false;
        private float _lastAspect = -1f;

        private Vector3 _scaleTargetFull = Vector3.one;
        private bool _scaleTargetCaptured = false;

        public bool IsPlaying { get { return _isPlaying; } }

        // ====================================================================
        // Unity lifecycle
        // ====================================================================

        private void Awake()
        {
            if (maskMaterial == null)
            {
                Debug.LogError("[ShapeWipe] maskMaterial is not assigned.", this);
                enabled = false;
                return;
            }

            _progressID = Shader.PropertyToID("_Progress");
            _aspectID = Shader.PropertyToID("_ScreenAspect");
            _modeID = Shader.PropertyToID("_Mode");
            _shapeDataID = Shader.PropertyToID("_ShapeData");
            _shapeTargetID = Shader.PropertyToID("_ShapeTarget");
            _shapeMotionID = Shader.PropertyToID("_ShapeMotion");
            _shapeRotationID = Shader.PropertyToID("_ShapeRotation");
            _activeCountID = Shader.PropertyToID("_ActiveCount");

            maskMaterial.SetFloat(_progressID, 0f);
            maskMaterial.SetFloat(_modeID, 0f);

            CaptureScaleTarget();
            ZeroRuntimeRandom();
            WriteShapesToShader(closeDuration, FlipMode.None);
            UpdateAspect(true);
        }

        private void Update()
        {
            if (testKey != KeyCode.None && Input.GetKeyDown(testKey) && !_isPlaying)
                StartCoroutine(Play());

            UpdateAspect(false);
        }

        private void UpdateAspect(bool force)
        {
            if (maskMaterial == null) return;

            float aspect = (float)Screen.width / Screen.height;
            if (!force && Mathf.Abs(aspect - _lastAspect) < 0.0001f) return;

            _lastAspect = aspect;
            maskMaterial.SetFloat(_aspectID, aspect);
        }

        private void CaptureScaleTarget()
        {
            if (_scaleTargetCaptured) return;
            if (scaleTarget != null) _scaleTargetFull = scaleTarget.localScale;
            _scaleTargetCaptured = true;
        }

        // ====================================================================
        // Public API
        // ====================================================================

        public IEnumerator Play()
        {
            yield return Play(null);
        }

        public IEnumerator Play(System.Func<IEnumerator> loadAction)
        {
            if (_isPlaying) yield break;
            _isPlaying = true;

            RollRuntimeRandom();

            if (scaleTarget != null)
            {
                CaptureScaleTarget();
                scaleTarget.localScale = Vector3.zero;
                StartCoroutine(ScaleUITo(_scaleTargetFull, scaleInDuration));
            }

            // ---- Phase 1: close ----
            WriteShapesToShader(closeDuration, closeFlip);
            maskMaterial.SetFloat(_modeID, reverseClosePhase ? 1f : 0f);
            yield return AnimateProgress(
                reverseClosePhase ? 1f : 0f,
                reverseClosePhase ? 0f : 1f,
                closeDuration);

            if (onScreenCovered != null) onScreenCovered.Invoke();

            // ---- Hold / Load ----
            if (loadAction != null)
            {
                yield return loadAction();
            }
            else if (holdDuration > 0f)
            {
                yield return new WaitForSecondsRealtime(holdDuration);
            }

            // ---- Phase 2: open ----
            if (randomizeEachPhase) RollRuntimeRandom();

            WriteShapesToShader(openDuration, openFlip);
            maskMaterial.SetFloat(_modeID, reverseOpenPhase ? 0f : 1f);

            Coroutine openCo = StartCoroutine(AnimateProgress(
                reverseOpenPhase ? 1f : 0f,
                reverseOpenPhase ? 0f : 1f,
                openDuration));

            float scaleOutLead = Mathf.Min(scaleOutDuration, openDuration);
            float openBeforeOutro = Mathf.Max(0f, openDuration - scaleOutLead);

            if (openBeforeOutro > 0f)
                yield return new WaitForSecondsRealtime(openBeforeOutro);

            if (scaleTarget != null)
                yield return ScaleUITo(Vector3.zero, scaleOutLead);

            yield return openCo;

            if (scaleTarget != null)
                scaleTarget.localScale = Vector3.zero;

            _isPlaying = false;
            if (onTransitionDone != null) onTransitionDone.Invoke();
        }

        /// <summary>
        /// Ensures the upload buffers are the right size. Only reallocates when
        /// the shape count actually changes.
        /// </summary>
        private void EnsureUploadBuffers(int count)
        {
            if (_uploadCount == count && _shapeData.Length == count) return;

            _uploadCount = count;
            _shapeData = new Vector4[count];
            _shapeTarget = new Vector4[count];
            _shapeMotion = new Vector4[count];
            _shapeRotation = new Vector4[count];
        }

        /// <summary>
        /// Writes all shape data into the shader arrays. `flip` mirrors the
        /// delay ramp spatially (H/V/Both) using each shape's grid position.
        /// A non-positive phase duration makes every shape complete instantly.
        /// </summary>
        public void WriteShapesToShader(float phaseDuration, FlipMode flip)
        {
            int count = (shapes != null) ? Mathf.Min(shapes.Length, MAX_SHAPES) : 0;
            bool instant = phaseDuration <= 0f;
            float safeDuration = Mathf.Max(0.0001f, phaseDuration);

            int gridCols, gridRows;
            bool flipActive = BuildFlippedDelayGrid(count, flip, out gridCols, out gridRows);

            // Resize the upload buffers only if the count changed.
            EnsureUploadBuffers(count);

            for (int i = 0; i < count; i++)
            {
                ShapeInstance s = shapes[i];

                float baseDelaySec = s.delay;
                if (flipActive)
                    baseDelaySec = LookupFlippedDelay(s.gridPos, flip, gridCols, gridRows);

                Vector2 pos = s.center + _rtOffset[i];
                Vector2 offset = s.wipeOffset;

                float startRotDeg = s.rotation + _rtRotation[i];
                float endRotDeg = s.endRotation + _rtRotation[i];
                float startRotRad = Mathf.Repeat(startRotDeg, 360f) * Mathf.Deg2Rad;
                float endRotRad = Mathf.Repeat(endRotDeg, 360f) * Mathf.Deg2Rad;

                // Precompute sin/cos on the CPU so the shader only does lerp.
                float startCos = Mathf.Cos(startRotRad);
                float startSin = Mathf.Sin(startRotRad);
                float endCos = Mathf.Cos(endRotRad);
                float endSin = Mathf.Sin(endRotRad);

                float normDelay, normExpand;
                if (instant)
                {
                    normDelay = 0f;
                    normExpand = 0.0001f;
                }
                else
                {
                    float delaySec = Mathf.Max(0f, baseDelaySec + _rtDelay[i]);
                    float remainSec = Mathf.Max(0.0001f, safeDuration - delaySec);
                    float expandSec = (s.expandDuration > 0f) ? s.expandDuration : remainSec;

                    normDelay = Mathf.Clamp01(delaySec / safeDuration);
                    normExpand = Mathf.Clamp(expandSec / safeDuration, 0.0001f, 1f);
                }

                float endX = s.endScale.x * _rtEndScaleX[i];
                float endY = s.endScale.y * _rtEndScaleY[i];

                _shapeData[i] = new Vector4(pos.x, pos.y, startCos, s.startScale.x);
                _shapeTarget[i] = new Vector4(s.startScale.y, endX, endY, normDelay);
                _shapeMotion[i] = new Vector4(offset.x, offset.y, normExpand, endCos);
                _shapeRotation[i] = new Vector4(startSin, endSin, 0f, 0f);
            }

            // Unity 2018 doesn't have the (name, array, count) overload, so we
            // upload arrays sized exactly to the active count.
            if (count > 0)
            {
                maskMaterial.SetVectorArray(_shapeDataID, _shapeData);
                maskMaterial.SetVectorArray(_shapeTargetID, _shapeTarget);
                maskMaterial.SetVectorArray(_shapeMotionID, _shapeMotion);
                maskMaterial.SetVectorArray(_shapeRotationID, _shapeRotation);
            }

            maskMaterial.SetFloat(_activeCountID, count);
        }

        [ContextMenu("Test Play Transition")]
        private void TestPlayTransition()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[ShapeWipe] Test Play works only in Play Mode.");
                return;
            }
            if (!_isPlaying) StartCoroutine(Play());
        }

        // ====================================================================
        // Flip helpers
        // ====================================================================

        private bool BuildFlippedDelayGrid(int count, FlipMode flip,
                                           out int cols, out int rows)
        {
            cols = 0;
            rows = 0;

            if (flip == FlipMode.None || count <= 0) return false;

            int maxRow = 0, maxCol = 0;
            for (int i = 0; i < count; i++)
            {
                if (shapes[i].gridPos.x > maxCol) maxCol = shapes[i].gridPos.x;
                if (shapes[i].gridPos.y > maxRow) maxRow = shapes[i].gridPos.y;
            }
            rows = maxRow + 1;
            cols = maxCol + 1;

            int gridSize = rows * cols;
            if (_delayGrid == null || _delayGrid.Length < gridSize)
                _delayGrid = new float[gridSize];

            for (int g = 0; g < gridSize; g++) _delayGrid[g] = 0f;

            for (int i = 0; i < count; i++)
            {
                int r = shapes[i].gridPos.y;
                int c = shapes[i].gridPos.x;
                _delayGrid[r * cols + c] = shapes[i].delay;
            }

            return true;
        }

        private float LookupFlippedDelay(Vector2Int gridPos, FlipMode flip,
                                         int cols, int rows)
        {
            int r = gridPos.y;
            int c = gridPos.x;

            if (flip == FlipMode.Horizontal || flip == FlipMode.Both)
                c = cols - 1 - c;
            if (flip == FlipMode.Vertical || flip == FlipMode.Both)
                r = rows - 1 - r;

            r = Mathf.Clamp(r, 0, rows - 1);
            c = Mathf.Clamp(c, 0, cols - 1);

            return _delayGrid[r * cols + c];
        }

        // ====================================================================
        // Internal
        // ====================================================================

        private void ZeroRuntimeRandom()
        {
            for (int i = 0; i < MAX_SHAPES; i++)
            {
                _rtOffset[i] = Vector2.zero;
                _rtRotation[i] = 0f;
                _rtDelay[i] = 0f;
                _rtEndScaleX[i] = 1f;
                _rtEndScaleY[i] = 1f;
            }
        }

        private void RollRuntimeRandom()
        {
            int count = (shapes != null) ? Mathf.Min(shapes.Length, MAX_SHAPES) : 0;

            for (int i = 0; i < MAX_SHAPES; i++)
            {
                if (i < count)
                {
                    _rtOffset[i] = randomizePosition
                        ? new Vector2(Random.Range(-randomOffsetX, randomOffsetX),
                                      Random.Range(-randomOffsetY, randomOffsetY))
                        : Vector2.zero;

                    if (randomizeRotation)
                    {
                        float lo = Mathf.Min(randomRotationMin, randomRotationMax);
                        float hi = Mathf.Max(randomRotationMin, randomRotationMax);
                        _rtRotation[i] = Random.Range(lo, hi);
                    }
                    else
                    {
                        _rtRotation[i] = 0f;
                    }

                    _rtDelay[i] = randomizeDelay
                        ? Random.Range(randomDelayMin, randomDelayMax)
                        : 0f;

                    if (randomizeEndScale)
                    {
                        float lo = Mathf.Min(randomEndScaleMin, randomEndScaleMax);
                        float hi = Mathf.Max(randomEndScaleMin, randomEndScaleMax);
                        float mx = Random.Range(lo, hi);

                        switch (randomEndScaleAxis)
                        {
                            case EndScaleAxis.XOnly:
                                _rtEndScaleX[i] = mx;
                                _rtEndScaleY[i] = 1f;
                                break;

                            case EndScaleAxis.YOnly:
                                _rtEndScaleX[i] = 1f;
                                _rtEndScaleY[i] = mx;
                                break;

                            default: // Both
                                _rtEndScaleX[i] = mx;
                                _rtEndScaleY[i] = randomEndScaleUniform
                                    ? mx
                                    : Random.Range(lo, hi);
                                break;
                        }
                    }
                    else
                    {
                        _rtEndScaleX[i] = 1f;
                        _rtEndScaleY[i] = 1f;
                    }
                }
                else
                {
                    _rtOffset[i] = Vector2.zero;
                    _rtRotation[i] = 0f;
                    _rtDelay[i] = 0f;
                    _rtEndScaleX[i] = 1f;
                    _rtEndScaleY[i] = 1f;
                }
            }
        }

        private IEnumerator AnimateProgress(float from, float to, float duration)
        {
            if (duration <= 0f)
            {
                maskMaterial.SetFloat(_progressID, to);
                yield break;
            }

            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float raw = Mathf.Clamp01(t / duration);
                float eased = ApplyEasing(raw, easing);
                float p = Mathf.Lerp(from, to, eased);
                maskMaterial.SetFloat(_progressID, p);
                yield return null;
            }
            maskMaterial.SetFloat(_progressID, to);
        }

        private static float ApplyEasing(float t, EasingType type)
        {
            t = Mathf.Clamp01(t);
            switch (type)
            {
                case EasingType.EaseIn:
                    return t * t;

                case EasingType.EaseOut:
                    return 1f - (1f - t) * (1f - t);

                case EasingType.EaseInOut:
                    return (t < 0.5f)
                        ? 2f * t * t
                        : 1f - Mathf.Pow(-2f * t + 2f, 2f) * 0.5f;

                default:
                    return t;
            }
        }

        private IEnumerator ScaleUITo(Vector3 to, float duration)
        {
            if (scaleTarget == null) yield break;

            Vector3 from = scaleTarget.localScale;

            if (duration <= 0f)
            {
                scaleTarget.localScale = to;
                yield break;
            }

            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(t / duration);
                scaleTarget.localScale = Vector3.Lerp(from, to, p);
                yield return null;
            }
            scaleTarget.localScale = to;
        }
    }
}
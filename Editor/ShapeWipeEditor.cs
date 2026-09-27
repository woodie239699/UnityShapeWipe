// ============================================================================
// ShapeWipe Custom Editor
// Compatible with Unity 2018.4+.
// ============================================================================

using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using System.Collections.Generic;
using ShapeWipe;

namespace ShapeWipeEditorTools
{
    [CustomEditor(typeof(ShapeWipe))]
    public class ShapeWipeEditor : UnityEditor.Editor
    {
        // ====================================================================
        // Constants
        // ====================================================================

        private const float PreviewAspect = 16f / 9f;
        private const float DotHitRadius = 14f;
        private const float DotSize = 12f;
        private const float SnapStep = 0.01f;
        private const float NeutralProgress = 0f;
        private const float NeutralMode = 0f;
        private const string PrefKeyPrefix = "ShapeWipeEditor.";

        // ====================================================================
        // Foldout state cache
        // ====================================================================

        private static readonly Dictionary<string, bool> _foldCache =
            new Dictionary<string, bool>();

        private static bool Fold(string key, string label, bool header = false)
        {
            bool state;
            if (!_foldCache.TryGetValue(key, out state))
            {
                state = EditorPrefs.GetBool(PrefKeyPrefix + key, true);
                _foldCache[key] = state;
            }

            bool newState = header
                ? FoldoutHeader(state, label)
                : EditorGUILayout.Foldout(state, label, true);

            if (newState != state)
            {
                _foldCache[key] = newState;
                EditorPrefs.SetBool(PrefKeyPrefix + key, newState);
            }
            return newState;
        }

        // ====================================================================
        // Runtime state
        // ====================================================================

        private int _selectedIndex = -1;
        private int _draggingIndex = -1;
        private int _dragControlID = 0;
        private float _previewProgress = NeutralProgress;

        private bool _isAutoPlaying = false;
        private double _autoPlayStart = 0.0;

        // ====================================================================
        // Lifecycle
        // ====================================================================

        private void OnEnable()
        {
            ApplyNeutralState();
            _previewProgress = NeutralProgress;
        }

        private void OnDisable()
        {
            StopAutoPlay();
            if (!Application.isPlaying)
                ApplyNeutralState();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            ShapeWipe t = (ShapeWipe)target;

            DrawMask();
            DrawPlayback(t);
            DrawRandomization(t);
            DrawEvents(t);

            DrawGridGenerator(t);
            DrawPreview(t);
            DrawPreviewProgress(t);
            DrawSelectedShapePanel(t);

            serializedObject.ApplyModifiedProperties();
        }

        // ====================================================================
        // Mask
        // ====================================================================

        private void DrawMask()
        {
            GUILayout.Space(8);
            if (!Fold("mask", "Mask", header: true)) return;

            EditorGUI.indentLevel++;

            EditorGUILayout.PropertyField(serializedObject.FindProperty("maskMaterial"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("shapes"), true);

            EditorGUI.indentLevel--;
        }

        // ====================================================================
        // Playback
        // ====================================================================

        private void DrawPlayback(ShapeWipe t)
        {
            GUILayout.Space(8);
            if (!Fold("playback", "Playback", header: true)) return;

            EditorGUI.indentLevel++;

            Undo.RecordObject(t, "Playback Change");
            EditorGUI.BeginChangeCheck();

            // ---- Timing ----
            if (Fold("pbTiming", "Timing"))
            {
                EditorGUI.indentLevel++;
                t.closeDuration = Mathf.Max(0f, EditorGUILayout.FloatField(
                    "Close Duration", t.closeDuration));
                t.openDuration = Mathf.Max(0f, EditorGUILayout.FloatField(
                    "Open Duration", t.openDuration));
                t.holdDuration = Mathf.Max(0f, EditorGUILayout.FloatField(
                    "Hold Duration", t.holdDuration));
                t.easing = (EasingType)EditorGUILayout.EnumPopup("Easing", t.easing);
                EditorGUI.indentLevel--;
            }

            // ---- Phase Modes ----
            if (Fold("pbPhase", "Phase Modes"))
            {
                EditorGUI.indentLevel++;

                t.reverseClosePhase = EditorGUILayout.Toggle(
                    "Reverse Close Phase", t.reverseClosePhase);
                t.closeFlip = (FlipMode)EditorGUILayout.EnumPopup(
                    "Close Flip", t.closeFlip);

                t.reverseOpenPhase = EditorGUILayout.Toggle(
                    "Reverse Open Phase", t.reverseOpenPhase);
                t.openFlip = (FlipMode)EditorGUILayout.EnumPopup(
                    "Open Flip", t.openFlip);

                EditorGUILayout.HelpBox(
                    "Flip mirrors the delay ramp spatially. Horizontal / Vertical / " +
                    "Both require shapes generated by the Grid Generator so their " +
                    "grid positions are known.",
                    MessageType.None);

                EditorGUI.indentLevel--;
            }

            // ---- UI Scale Sync ----
            if (Fold("pbScale", "UI Scale Sync (optional)"))
            {
                EditorGUI.indentLevel++;
                t.scaleTarget = (RectTransform)EditorGUILayout.ObjectField(
                    "Scale Target", t.scaleTarget, typeof(RectTransform), true);
                t.scaleInDuration = Mathf.Max(0f, EditorGUILayout.FloatField(
                    "Scale In Duration", t.scaleInDuration));
                t.scaleOutDuration = Mathf.Max(0f, EditorGUILayout.FloatField(
                    "Scale Out Duration", t.scaleOutDuration));
                EditorGUI.indentLevel--;
            }

            if (EditorGUI.EndChangeCheck())
                EditorUtility.SetDirty(t);

            EditorGUI.indentLevel--;
        }

        // ====================================================================
        // Runtime Randomization
        // ====================================================================

        private bool DrawRandomSubGroup(string key, string label,
                                        bool enabled, System.Action body)
        {
            if (!Fold(key, label)) return enabled;

            EditorGUI.indentLevel++;
            bool newEnabled = EditorGUILayout.Toggle("Enabled", enabled);

            using (new EditorGUI.DisabledScope(!newEnabled))
            {
                if (body != null) body();
            }

            EditorGUI.indentLevel--;
            return newEnabled;
        }

        private void DrawRandomization(ShapeWipe t)
        {
            GUILayout.Space(8);
            if (!Fold("random", "Runtime Randomization", header: true)) return;

            EditorGUI.indentLevel++;

            Undo.RecordObject(t, "Randomization Change");
            EditorGUI.BeginChangeCheck();

            t.randomizePosition = DrawRandomSubGroup("randomPosition", "Position",
                t.randomizePosition, () =>
                {
                    Vector2 rOff = EditorGUILayout.Vector2Field(
                        "Offset", new Vector2(t.randomOffsetX, t.randomOffsetY));
                    t.randomOffsetX = rOff.x;
                    t.randomOffsetY = rOff.y;
                });

            t.randomizeRotation = DrawRandomSubGroup("randomRotation", "Rotation",
                t.randomizeRotation, () =>
                {
                    t.randomRotationMin = EditorGUILayout.FloatField("Min (deg)", t.randomRotationMin);
                    t.randomRotationMax = EditorGUILayout.FloatField("Max (deg)", t.randomRotationMax);
                });

            t.randomizeDelay = DrawRandomSubGroup("randomDelay", "Delay",
                t.randomizeDelay, () =>
                {
                    t.randomDelayMin = EditorGUILayout.FloatField("Min (sec)", t.randomDelayMin);
                    t.randomDelayMax = EditorGUILayout.FloatField("Max (sec)", t.randomDelayMax);
                });

            t.randomizeEndScale = DrawRandomSubGroup("randomEndScale", "End Scale",
                t.randomizeEndScale, () =>
                {
                    t.randomEndScaleAxis = (EndScaleAxis)EditorGUILayout.EnumPopup(
                        "Axis", t.randomEndScaleAxis);

                    t.randomEndScaleMin = EditorGUILayout.FloatField(
                        "Min Multiplier", t.randomEndScaleMin);
                    t.randomEndScaleMax = EditorGUILayout.FloatField(
                        "Max Multiplier", t.randomEndScaleMax);

                    if (t.randomEndScaleAxis == EndScaleAxis.Both)
                    {
                        t.randomEndScaleUniform = EditorGUILayout.Toggle(
                            "Uniform (same X/Y)", t.randomEndScaleUniform);
                    }
                });

            GUILayout.Space(2);
            t.randomizeEachPhase = EditorGUILayout.Toggle(
                "Randomize Each Phase", t.randomizeEachPhase);

            if (EditorGUI.EndChangeCheck())
                EditorUtility.SetDirty(t);

            EditorGUI.indentLevel--;
        }

        // ====================================================================
        // Events
        // ====================================================================

        private void DrawEvents(ShapeWipe t)
        {
            GUILayout.Space(8);
            if (!Fold("events", "Events", header: true)) return;

            EditorGUI.indentLevel++;

            EditorGUILayout.PropertyField(serializedObject.FindProperty("onScreenCovered"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("onTransitionDone"));

            EditorGUILayout.HelpBox(
                "Tip: call Play(loadAction) to wait for an async load instead of " +
                "relying on Hold Duration.",
                MessageType.None);

            Undo.RecordObject(t, "Test Key Change");
            EditorGUI.BeginChangeCheck();
            t.testKey = (KeyCode)EditorGUILayout.EnumPopup("Test Key", t.testKey);
            if (EditorGUI.EndChangeCheck())
                EditorUtility.SetDirty(t);

            EditorGUI.indentLevel--;
        }

        // ====================================================================
        // Grid Generator
        // ====================================================================

        private static void ComputeRawIndex(
            GridGeneratorSettings g, int row, int col, int cols, int rows,
            bool oddRow,
            out float rawDelayIndex, out float rawGroupIndex)
        {
            rawDelayIndex = 0f;
            rawGroupIndex = 0f;

            switch (g.delayOrder)
            {
                case DelayOrder.RowMajor:
                    rawDelayIndex = row * cols + col;
                    break;

                case DelayOrder.ColumnMajor:
                    rawDelayIndex = col * rows + row;
                    break;

                case DelayOrder.Diagonal:
                    float delayCol = col;
                    if (g.staggerRows && oddRow)
                        delayCol += g.staggerDelayCompensation;
                    rawDelayIndex = delayCol + row;
                    break;

                case DelayOrder.RowPairs:
                    rawDelayIndex = col;
                    rawGroupIndex = (row + 1) / 2;
                    break;

                case DelayOrder.RowConstant:
                    rawDelayIndex = row;
                    break;

                case DelayOrder.ColumnConstant:
                    rawDelayIndex = col;
                    break;
            }
        }

        private static void GetMaxIndex(
            GridGeneratorSettings g, int cols, int rows,
            out float maxDelayIndex, out float maxGroupIndex)
        {
            maxDelayIndex = 0f;
            maxGroupIndex = 0f;

            switch (g.delayOrder)
            {
                case DelayOrder.RowMajor:
                case DelayOrder.ColumnMajor:
                    maxDelayIndex = cols * rows - 1;
                    break;

                case DelayOrder.Diagonal:
                    maxDelayIndex = (cols - 1) + (rows - 1);
                    break;

                case DelayOrder.RowPairs:
                    maxDelayIndex = cols - 1;
                    maxGroupIndex = rows / 2;
                    break;

                case DelayOrder.RowConstant:
                    maxDelayIndex = rows - 1;
                    break;

                case DelayOrder.ColumnConstant:
                    maxDelayIndex = cols - 1;
                    break;
            }
        }

        private static int GenerateGrid(ShapeWipe target, bool append)
        {
            GridGeneratorSettings g = target.gridSettings;

            int cols = Mathf.Max(1, g.columns);
            int rows = Mathf.Max(1, g.rows);
            int requested = cols * rows;

            int existing = (append && target.shapes != null) ? target.shapes.Length : 0;
            int available = Mathf.Max(0, ShapeWipe.MAX_SHAPES - existing);
            int total = Mathf.Min(requested, available);

            if (total <= 0) return 0;

            ShapeInstance[] newShapes;
            int startIndex;

            if (append && target.shapes != null)
            {
                newShapes = new ShapeInstance[existing + total];
                for (int i = 0; i < existing; i++) newShapes[i] = target.shapes[i];
                startIndex = existing;
            }
            else
            {
                newShapes = new ShapeInstance[total];
                startIndex = 0;
            }

            float usableW = Mathf.Max(0f, 1f - 2f * g.marginX);
            float usableH = Mathf.Max(0f, 1f - 2f * g.marginY);

            float stepX = (cols > 1) ? usableW / (cols - 1) : 0f;
            float stepY = (rows > 1) ? usableH / (rows - 1) : 0f;
            float cellW = (cols > 0) ? usableW / cols : 0f;

            Vector2 generatedOffset = new Vector2(g.offsetX, g.offsetY);

            Vector2 generatedStartScale = new Vector2(
                Mathf.Max(0f, g.startX), Mathf.Max(0f, g.startY));
            Vector2 generatedEndScale = new Vector2(
                Mathf.Max(0f, g.endScaleX), Mathf.Max(0f, g.endScaleY));

            float maxDelayIndex, maxGroupIndex;
            GetMaxIndex(g, cols, rows, out maxDelayIndex, out maxGroupIndex);

            bool flipDelay = g.delayStep < 0f;
            bool flipGroup = g.rowGroupStep < 0f;
            float absDelayStep = Mathf.Abs(g.delayStep);
            float absGroupStep = Mathf.Abs(g.rowGroupStep);
            float safeBase = Mathf.Max(0f, g.baseDelay);

            int idx = 0;
            for (int row = 0; row < rows && idx < total; row++)
            {
                for (int col = 0; col < cols && idx < total; col++)
                {
                    int effectiveCol = (g.snakeOrder && row % 2 == 1)
                        ? (cols - 1 - col)
                        : col;

                    bool oddRow = (row % 2 == 1);
                    float stagger = (g.staggerRows && oddRow)
                        ? cellW * 0.5f
                        : 0f;

                    float x = (cols == 1)
                        ? 0.5f
                        : g.marginX + effectiveCol * stepX + stagger;

                    float y = (rows == 1)
                        ? 0.5f
                        : (1f - g.marginY) - row * stepY;

                    x += g.postOffsetX;
                    y += g.postOffsetY;

                    float rawDelayIndex, rawGroupIndex;
                    ComputeRawIndex(g, row, effectiveCol, cols, rows, oddRow,
                                    out rawDelayIndex, out rawGroupIndex);

                    float effDelayIndex = flipDelay
                        ? (maxDelayIndex - rawDelayIndex)
                        : rawDelayIndex;

                    float effGroupIndex = flipGroup
                        ? (maxGroupIndex - rawGroupIndex)
                        : rawGroupIndex;

                    float delay = safeBase
                                + effDelayIndex * absDelayStep
                                + effGroupIndex * absGroupStep;

                    newShapes[startIndex + idx] = new ShapeInstance
                    {
                        center = new Vector2(x, y),
                        wipeOffset = generatedOffset,
                        startScale = generatedStartScale,
                        endScale = generatedEndScale,
                        rotation = g.startRotation,
                        endRotation = g.endRotation,
                        delay = delay,
                        expandDuration = g.expand,
                        gridPos = new Vector2Int(effectiveCol, row)
                    };
                    idx++;
                }
            }

            target.shapes = newShapes;
            return total;
        }

        private void DrawGridGenerator(ShapeWipe t)
        {
            GUILayout.Space(8);
            if (!Fold("grid", "Grid Generator", header: true)) return;

            EditorGUI.indentLevel++;
            EditorGUILayout.HelpBox(
                "Generates an N x M grid of shapes as the baked baseline. " +
                "Grid endpoints land on the margins (margin = 0 puts the outer " +
                "shapes on the screen edges).",
                MessageType.Info);

            GridGeneratorSettings g = t.gridSettings;

            Undo.RecordObject(t, "Grid Generator Change");
            EditorGUI.BeginChangeCheck();

            // ---- Layout ----
            if (Fold("gridLayout", "Layout"))
            {
                EditorGUI.indentLevel++;
                g.columns = EditorGUILayout.IntField("Columns", g.columns);
                g.rows = EditorGUILayout.IntField("Rows", g.rows);

                Vector2 margin = EditorGUILayout.Vector2Field(
                    "Margin", new Vector2(g.marginX, g.marginY));
                g.marginX = Mathf.Clamp(margin.x, 0f, 0.4f);
                g.marginY = Mathf.Clamp(margin.y, 0f, 0.4f);

                g.staggerRows = EditorGUILayout.Toggle("Stagger Rows", g.staggerRows);
                g.snakeOrder = EditorGUILayout.Toggle("Snake Order", g.snakeOrder);

                Vector2 postOffset = EditorGUILayout.Vector2Field(
                    "Post Offset", new Vector2(g.postOffsetX, g.postOffsetY));
                g.postOffsetX = postOffset.x;
                g.postOffsetY = postOffset.y;

                EditorGUI.indentLevel--;
            }

            // ---- Timing ----
            if (Fold("gridTiming", "Timing"))
            {
                EditorGUI.indentLevel++;

                g.baseDelay = Mathf.Max(0f, EditorGUILayout.FloatField(
                    "Base Delay (sec)", g.baseDelay));

                g.delayStep = EditorGUILayout.FloatField("Delay Step (sec)", g.delayStep);

                if (g.delayStep < 0f)
                {
                    EditorGUILayout.HelpBox(
                        "Negative Delay Step flips the order: the last shape fires first. " +
                        "Delays never go below Base Delay.",
                        MessageType.None);
                }
                else if (Mathf.Approximately(g.delayStep, 0f))
                {
                    EditorGUILayout.HelpBox(
                        "Delay Step = 0 gives every shape the same delay.",
                        MessageType.None);
                }

                g.delayOrder = (DelayOrder)EditorGUILayout.EnumPopup(
                    "Delay Order", g.delayOrder);

                if (g.delayOrder == DelayOrder.RowPairs)
                {
                    g.rowGroupStep = EditorGUILayout.FloatField(
                        "Row Group Step (sec)", g.rowGroupStep);

                    if (g.rowGroupStep < 0f)
                    {
                        EditorGUILayout.HelpBox(
                            "Negative Row Group Step flips the group order.",
                            MessageType.None);
                    }
                }

                if (g.delayOrder == DelayOrder.Diagonal)
                {
                    g.staggerDelayCompensation = EditorGUILayout.FloatField(
                        "Stagger Delay Comp (steps)", g.staggerDelayCompensation);
                }

                g.expand = EditorGUILayout.FloatField("Expand Duration (sec)", g.expand);

                EditorGUI.indentLevel--;
            }

            // ---- Size ----
            if (Fold("gridSize", "Size"))
            {
                EditorGUI.indentLevel++;

                Vector2 startSize = EditorGUILayout.Vector2Field(
                    "Start Scale", new Vector2(g.startX, g.startY));
                g.startX = Mathf.Max(0f, startSize.x);
                g.startY = Mathf.Max(0f, startSize.y);

                Vector2 endSize = EditorGUILayout.Vector2Field(
                    "End Scale", new Vector2(g.endScaleX, g.endScaleY));
                g.endScaleX = Mathf.Max(0f, endSize.x);
                g.endScaleY = Mathf.Max(0f, endSize.y);

                EditorGUI.indentLevel--;
            }

            // ---- Rotation ----
            if (Fold("gridRotation", "Rotation"))
            {
                EditorGUI.indentLevel++;
                g.startRotation = EditorGUILayout.FloatField("Start Rotation", g.startRotation);
                g.endRotation = EditorGUILayout.FloatField("End Rotation", g.endRotation);
                EditorGUI.indentLevel--;
            }

            // ---- Offset ----
            if (Fold("gridMotion", "Offset"))
            {
                EditorGUI.indentLevel++;
                Vector2 off = EditorGUILayout.Vector2Field(
                    "Offset", new Vector2(g.offsetX, g.offsetY));
                g.offsetX = off.x;
                g.offsetY = off.y;
                EditorGUI.indentLevel--;
            }

            if (EditorGUI.EndChangeCheck())
                EditorUtility.SetDirty(t);

            // ---- Status ----
            GUILayout.Space(4);
            int existingCount = (t.shapes != null) ? t.shapes.Length : 0;
            int requested = Mathf.Max(1, g.columns) * Mathf.Max(1, g.rows);
            int appendRoom = Mathf.Max(0, ShapeWipe.MAX_SHAPES - existingCount);

            EditorGUILayout.LabelField("Current shapes: " + existingCount + " / " + ShapeWipe.MAX_SHAPES);

            bool willTruncateAppend = requested > appendRoom;

            // ---- Actions ----
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();

            EditorGUI.BeginDisabledGroup(appendRoom <= 0);
            if (GUILayout.Button("Append", GUILayout.Height(22)))
            {
                Undo.RecordObject(t, "Append Shape Grid");
                int added = GenerateGrid(t, true);
                EditorUtility.SetDirty(t);
                Repaint();

                if (added < requested && willTruncateAppend)
                    Debug.LogWarning("[ShapeWipe] Append truncated: added " + added +
                                     " of " + requested + " (hit MAX_SHAPES = " +
                                     ShapeWipe.MAX_SHAPES + ").");
            }
            EditorGUI.EndDisabledGroup();

            if (GUILayout.Button("Replace All", GUILayout.Height(22)))
            {
                Undo.RecordObject(t, "Replace Shape Grid");
                int added = GenerateGrid(t, false);
                EditorUtility.SetDirty(t);
                Repaint();

                if (added < requested)
                    Debug.LogWarning("[ShapeWipe] Grid truncated: only " + added +
                                     " shapes generated (hit MAX_SHAPES = " +
                                     ShapeWipe.MAX_SHAPES + ").");
            }

            GUILayout.EndHorizontal();

            if (willTruncateAppend)
            {
                EditorGUILayout.HelpBox(
                    "Append will be truncated to " + appendRoom + " shape(s) " +
                    "because MAX_SHAPES is " + ShapeWipe.MAX_SHAPES + ".",
                    MessageType.Warning);
            }

            EditorGUI.indentLevel--;
        }

        // ====================================================================
        // Preview
        // ====================================================================

        private void DrawPreview(ShapeWipe t)
        {
            GUILayout.Space(8);
            if (!Fold("preview", "Shape Position Preview (16:9)", header: true)) return;

            EditorGUILayout.HelpBox(
                "Drag a dot to move it (snaps to 0.01). Click a dot to select. " +
                "Dashed = baseline end position after wipe offset.",
                MessageType.Info);

            float w = EditorGUIUtility.currentViewWidth - 40f;
            float h = w / PreviewAspect;
            Rect rect = GUILayoutUtility.GetRect(w, h);

            EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f));

            DrawGrid(rect);
            DrawMarginOverlay(t, rect);
            DrawShapeDots(t, rect);
            HandleMouseInput(t, rect);
        }

        private void DrawGrid(Rect rect)
        {
            Handles.BeginGUI();
            Handles.color = new Color(1f, 1f, 1f, 0.1f);
            for (int i = 1; i < 4; i++)
            {
                float gx = rect.x + rect.width * i / 4f;
                float gy = rect.y + rect.height * i / 4f;
                Handles.DrawLine(new Vector3(gx, rect.y), new Vector3(gx, rect.yMax));
                Handles.DrawLine(new Vector3(rect.x, gy), new Vector3(rect.xMax, gy));
            }
            Handles.EndGUI();
        }

        private void DrawMarginOverlay(ShapeWipe t, Rect rect)
        {
            float mx = Mathf.Clamp01(t.gridSettings.marginX);
            float my = Mathf.Clamp01(t.gridSettings.marginY);

            float x0 = rect.x + rect.width * mx;
            float x1 = rect.x + rect.width * (1f - mx);
            float y0 = rect.y + rect.height * my;
            float y1 = rect.y + rect.height * (1f - my);

            Rect usable = new Rect(x0, y0, x1 - x0, y1 - y0);

            Handles.BeginGUI();
            Handles.DrawSolidRectangleWithOutline(
                usable,
                new Color(1f, 0.8f, 0.3f, 0.06f),
                new Color(1f, 0.8f, 0.3f, 0.6f));
            Handles.EndGUI();
        }

        private void DrawShapeDots(ShapeWipe t, Rect rect)
        {
            if (t.shapes == null) return;

            int count = Mathf.Min(t.shapes.Length, ShapeWipe.MAX_SHAPES);

            for (int i = 0; i < count; i++)
            {
                Vector2 guiPos = UVtoGUI(t.shapes[i].center, rect);

                Vector2 endUV = t.shapes[i].center + t.shapes[i].wipeOffset;
                Vector2 endPos = UVtoGUI(endUV, rect);

                Handles.BeginGUI();
                Handles.color = new Color(1f, 1f, 1f, 0.25f);
                Handles.DrawLine(guiPos, endPos);
                Handles.color = new Color(1f, 0.7f, 0.2f, 0.9f);
                Handles.DrawWireDisc(endPos, Vector3.forward, 4f);
                Handles.EndGUI();

                Rect dotRect = new Rect(
                    guiPos.x - DotSize * 0.5f,
                    guiPos.y - DotSize * 0.5f,
                    DotSize, DotSize);

                Color dotColor;
                if (i == _draggingIndex) dotColor = Color.yellow;
                else if (i == _selectedIndex) dotColor = new Color(0.3f, 0.8f, 1f);
                else dotColor = Color.white;

                EditorGUI.DrawRect(dotRect, dotColor);

                float startRad = t.shapes[i].rotation * Mathf.Deg2Rad;
                float endRad = t.shapes[i].endRotation * Mathf.Deg2Rad;
                float lineLen = 14f + Mathf.Clamp(t.shapes[i].endScale.y * 8f, 0f, 50f);

                Vector2 startEnd = guiPos + new Vector2(
                    Mathf.Cos(startRad), -Mathf.Sin(startRad)) * lineLen;
                Vector2 endEnd = guiPos + new Vector2(
                    Mathf.Cos(endRad), -Mathf.Sin(endRad)) * lineLen;

                Handles.BeginGUI();
                Handles.color = new Color(dotColor.r, dotColor.g, dotColor.b, 0.9f);
                Handles.DrawLine(guiPos, startEnd);
                Handles.color = new Color(0.4f, 1f, 0.4f, 0.7f);
                Handles.DrawLine(guiPos, endEnd);
                Handles.EndGUI();

                if (count <= 30)
                {
                    string label = i + " e:" + t.shapes[i].endScale.x.ToString("F2")
                                     + "x" + t.shapes[i].endScale.y.ToString("F2")
                                     + " r:" + t.shapes[i].rotation.ToString("F0")
                                     + "→" + t.shapes[i].endRotation.ToString("F0");
                    if (t.shapes[i].delay > 0f)
                        label += " d:" + t.shapes[i].delay.ToString("F2");

                    GUI.Label(new Rect(dotRect.x + DotSize + 2f, dotRect.y - 6f, 260f, 20f), label);
                }
            }
        }

        private void HandleMouseInput(ShapeWipe t, Rect rect)
        {
            Event e = Event.current;
            int controlID = GUIUtility.GetControlID(FocusType.Passive);

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0 && rect.Contains(e.mousePosition))
                    {
                        int hit = FindShapeAt(t, e.mousePosition, rect);
                        if (hit >= 0)
                        {
                            Undo.RecordObject(t, "Move Shape");
                            _draggingIndex = hit;
                            _selectedIndex = hit;
                            _dragControlID = controlID;
                            GUIUtility.hotControl = _dragControlID;
                            e.Use();
                        }
                    }
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == _dragControlID && _draggingIndex >= 0)
                    {
                        float u = (e.mousePosition.x - rect.x) / rect.width;
                        float v = 1f - (e.mousePosition.y - rect.y) / rect.height;
                        u = SnapToStep(u, SnapStep);
                        v = SnapToStep(v, SnapStep);

                        t.shapes[_draggingIndex].center = new Vector2(u, v);
                        EditorUtility.SetDirty(t);
                        Repaint();
                        e.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl == _dragControlID)
                    {
                        GUIUtility.hotControl = 0;
                        _draggingIndex = -1;
                        e.Use();
                    }
                    break;
            }
        }

        private int FindShapeAt(ShapeWipe t, Vector2 mouse, Rect rect)
        {
            if (t.shapes == null) return -1;
            int count = Mathf.Min(t.shapes.Length, ShapeWipe.MAX_SHAPES);

            for (int i = count - 1; i >= 0; i--)
            {
                Vector2 guiPos = UVtoGUI(t.shapes[i].center, rect);
                if (Vector2.Distance(mouse, guiPos) < DotHitRadius)
                    return i;
            }
            return -1;
        }

        // ====================================================================
        // Preview Progress
        // ====================================================================

        private void DrawPreviewProgress(ShapeWipe t)
        {
            GUILayout.Space(8);
            if (!Fold("progress", "Preview Progress", header: true)) return;

            EditorGUILayout.HelpBox(
                "Drag the slider for preview. Use Play Preview to see the full transition.",
                MessageType.None);

            EditorGUI.BeginDisabledGroup(_isAutoPlaying);
            EditorGUI.BeginChangeCheck();
            _previewProgress = EditorGUILayout.Slider("Progress", _previewProgress, 0f, 1f);

            if (EditorGUI.EndChangeCheck() && t.maskMaterial != null)
            {
                t.maskMaterial.SetFloat("_Progress", _previewProgress);
                RequestRepaint();
            }
            EditorGUI.EndDisabledGroup();

            GUILayout.BeginHorizontal();

            EditorGUI.BeginDisabledGroup(_isAutoPlaying);
            if (GUILayout.Button("Play Preview"))
                StartAutoPlay();
            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(!_isAutoPlaying);
            if (GUILayout.Button("Stop"))
                StopAutoPlay();
            EditorGUI.EndDisabledGroup();

            if (GUILayout.Button("Set Mode: Black"))
            {
                if (t.maskMaterial != null) t.maskMaterial.SetFloat("_Mode", 0f);
                RequestRepaint();
            }
            if (GUILayout.Button("Set Mode: Erase"))
            {
                if (t.maskMaterial != null) t.maskMaterial.SetFloat("_Mode", 1f);
                RequestRepaint();
            }
            if (GUILayout.Button("Reset"))
            {
                StopAutoPlay();
                ApplyNeutralState();
                _previewProgress = NeutralProgress;
                RequestRepaint();
            }

            GUILayout.EndHorizontal();
        }

        private static void RequestRepaint()
        {
            EditorApplication.delayCall += () =>
            {
                SceneView.RepaintAll();
                EditorApplication.RepaintHierarchyWindow();
            };
        }

        private void StartAutoPlay()
        {
            if (_isAutoPlaying) return;
            _isAutoPlaying = true;
            _autoPlayStart = EditorApplication.timeSinceStartup;
            EditorApplication.update += OnAutoPlayUpdate;
        }

        private void StopAutoPlay()
        {
            if (!_isAutoPlaying) return;
            _isAutoPlaying = false;
            EditorApplication.update -= OnAutoPlayUpdate;

            ApplyNeutralState();
            _previewProgress = NeutralProgress;
            RequestRepaint();
            Repaint();
        }

        private void OnAutoPlayUpdate()
        {
            if (!_isAutoPlaying) return;

            ShapeWipe t = target as ShapeWipe;
            if (t == null || t.maskMaterial == null)
            {
                StopAutoPlay();
                return;
            }

            float elapsed = (float)(EditorApplication.timeSinceStartup - _autoPlayStart);
            float closeDur = Mathf.Max(0f, t.closeDuration);
            float holdDur = Mathf.Max(0f, t.holdDuration);
            float openDur = Mathf.Max(0f, t.openDuration);
            float total = closeDur + holdDur + openDur;

            if (total <= 0f || elapsed >= total)
            {
                StopAutoPlay();
                return;
            }

            float p;
            if (elapsed < closeDur)
            {
                p = t.reverseClosePhase
                    ? Mathf.Lerp(1f, 0f, elapsed / closeDur)
                    : Mathf.Lerp(0f, 1f, elapsed / closeDur);
                t.maskMaterial.SetFloat("_Mode", t.reverseClosePhase ? 1f : 0f);
            }
            else if (elapsed < closeDur + holdDur)
            {
                p = t.reverseClosePhase ? 0f : 1f;
            }
            else
            {
                float openElapsed = elapsed - closeDur - holdDur;
                p = t.reverseOpenPhase
                    ? Mathf.Lerp(1f, 0f, openElapsed / openDur)
                    : Mathf.Lerp(0f, 1f, openElapsed / openDur);
                t.maskMaterial.SetFloat("_Mode", t.reverseOpenPhase ? 0f : 1f);
            }

            t.maskMaterial.SetFloat("_Progress", p);
            _previewProgress = p;

            SceneView.RepaintAll();
            Repaint();
        }

        // ====================================================================
        // Selected shape panel
        // ====================================================================

        private void DrawSelectedShapePanel(ShapeWipe t)
        {
            if (_selectedIndex < 0 || t.shapes == null || _selectedIndex >= t.shapes.Length)
                return;

            GUILayout.Space(10);
            EditorGUILayout.LabelField("Selected Shape [" + _selectedIndex + "]", EditorStyles.boldLabel);

            ShapeInstance s = t.shapes[_selectedIndex];

            Undo.RecordObject(t, "Selected Shape Change");
            EditorGUI.BeginChangeCheck();

            Vector2 newStartScale = EditorGUILayout.Vector2Field("Start Scale", s.startScale);
            Vector2 newEndScale = EditorGUILayout.Vector2Field("End Scale", s.endScale);

            float newStartRot = EditorGUILayout.Slider("Start Rotation", s.rotation, -360f, 360f);
            float newEndRot = EditorGUILayout.Slider("End Rotation", s.endRotation, -360f, 360f);

            float newDelay = EditorGUILayout.FloatField("Delay (sec)", s.delay);
            if (newDelay < 0f) newDelay = 0f;

            float newExpand = EditorGUILayout.FloatField("Expand (sec, 0=auto)", s.expandDuration);
            if (newExpand < 0f) newExpand = 0f;

            Vector2 newCenter = EditorGUILayout.Vector2Field("Center (UV)", s.center);
            Vector2 newWipe = EditorGUILayout.Vector2Field("Wipe Offset", s.wipeOffset);

            if (EditorGUI.EndChangeCheck())
            {
                s.startScale = new Vector2(Mathf.Max(0f, newStartScale.x), Mathf.Max(0f, newStartScale.y));
                s.endScale = new Vector2(Mathf.Max(0f, newEndScale.x), Mathf.Max(0f, newEndScale.y));
                s.rotation = newStartRot;
                s.endRotation = newEndRot;
                s.delay = newDelay;
                s.expandDuration = newExpand;
                s.center = new Vector2(
                    SnapToStep(newCenter.x, SnapStep),
                    SnapToStep(newCenter.y, SnapStep));
                s.wipeOffset = newWipe;

                EditorUtility.SetDirty(t);
                Repaint();
            }
        }

        // ====================================================================
        // Helpers
        // ====================================================================

        private static bool FoldoutHeader(bool state, string label)
        {
            Rect r = EditorGUILayout.GetControlRect(false, 20f);
            EditorGUI.DrawRect(r, new Color(0.2f, 0.2f, 0.2f, 0.35f));

            bool newState = EditorGUI.Foldout(r, state, GUIContent.none, true);

            GUIStyle style = new GUIStyle(EditorStyles.boldLabel);
            style.alignment = TextAnchor.MiddleLeft;
            Rect labelRect = new Rect(r.x + 16f, r.y, r.width - 16f, r.height);
            EditorGUI.LabelField(labelRect, label, style);

            return newState;
        }

        private void ApplyNeutralState()
        {
            ShapeWipe t = target as ShapeWipe;
            if (t == null || t.maskMaterial == null) return;

            t.maskMaterial.SetFloat("_Progress", NeutralProgress);
            t.maskMaterial.SetFloat("_Mode", NeutralMode);
        }

        private static Vector2 UVtoGUI(Vector2 uv, Rect rect)
        {
            return new Vector2(
                rect.x + uv.x * rect.width,
                rect.y + (1f - uv.y) * rect.height);
        }

        private static float SnapToStep(float value, float step)
        {
            if (step <= 0f) return value;
            return Mathf.Round(value / step) * step;
        }
    }
}
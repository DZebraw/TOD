using System;
using System.Collections.Generic;
using System.Linq; // 新增：引入LINQ，解决First()方法问题
using UnityEngine;
using UnityEditor;
using DawnTOD;

namespace DawnTODEditor
{
    public class GradientDrawer : IEditorDrawer
    {
        private LightingEditorState _state;
        private TrackManager _trackManager;

        public void Initialize(LightingEditorState state, TrackManager trackManager)
        {
            _state = state;
            _trackManager = trackManager;
        }

        public void Draw(Rect drawRect)
        {

        }

        public bool HandleEvent(Rect drawRect, Event evt)
        {
            return false; 
        }

        public void DrawGradient(Rect rect, Gradient gradient, float height)
        {
            if (gradient == null) return;

            Texture2D gradientTexture = new Texture2D(256, 1, TextureFormat.RGBA32, false);
            Color[] colors = new Color[256];
            for (int i = 0; i < 256; i++)
            {
                colors[i] = gradient.Evaluate(i / 255f);
            }
            gradientTexture.SetPixels(colors);
            gradientTexture.Apply();

            Rect drawRect = new Rect(
                rect.x,
                rect.y + (rect.height - height) * 0.5f,
                rect.width,
                height
            );

            EditorGUI.DrawPreviewTexture(drawRect, gradientTexture, null, ScaleMode.StretchToFill, 0f);
            UnityEngine.Object.DestroyImmediate(gradientTexture);

            if (_state.CurrentEditorMode != EditorMode.Curves)
                return;

            const float MARKER_SIZE = 8f;
            const float MARGIN = 4f;
            const float NORMAL_BORDER_WIDTH = 1f;
            const float SELECTED_BORDER_WIDTH = 2f;
            Color selectedBorderColor = Color.yellow;

            Handles.BeginGUI();

            GradientColorKey[] colorKeys = gradient.colorKeys;
            if (colorKeys.Length > 0)
            {
                for (int i = 0; i < colorKeys.Length; i++)
                {
                    float centerX = drawRect.x + colorKeys[i].time * drawRect.width;
                    float bottomY = drawRect.yMax + MARGIN;

                    EditorGUIUtility.AddCursorRect(
                        new Rect(centerX - MARKER_SIZE, bottomY, MARKER_SIZE * 2, MARKER_SIZE),
                        MouseCursor.Arrow
                    );

                    bool isSelected = IsGradientKeySelected(i, GradientKeyType.Color);

                    if (isSelected)
                    {
                        Rect highlightRect = new Rect(
                            centerX - MARKER_SIZE - 1,
                            bottomY - 1,
                            MARKER_SIZE * 2 + 2,
                            MARKER_SIZE + 2
                        );
                        EditorGUI.DrawRect(highlightRect, new Color(1f, 1f, 0f, 0.2f));
                    }

                    Rect markerRect = new Rect(centerX - MARKER_SIZE, bottomY, MARKER_SIZE * 2, MARKER_SIZE);
                    float borderRadius = MARKER_SIZE * 0.2f;

                    Handles.color = colorKeys[i].color;
                    Handles.DrawSolidRectangleWithOutline(
                        markerRect,
                        colorKeys[i].color,
                        isSelected ? selectedBorderColor : Color.white
                    );

                    if (isSelected)
                    {
                        Handles.color = selectedBorderColor;
                        GUI.DrawTexture(markerRect, MakeRoundedRectTexture(markerRect.size, borderRadius, selectedBorderColor, SELECTED_BORDER_WIDTH), ScaleMode.StretchToFill);
                    }
                    else
                    {
                        Handles.color = Color.white;
                        GUI.DrawTexture(markerRect, MakeRoundedRectTexture(markerRect.size, borderRadius, Color.white, NORMAL_BORDER_WIDTH), ScaleMode.StretchToFill);
                    }
                }
            }

            GradientAlphaKey[] alphaKeys = gradient.alphaKeys;
            if (alphaKeys.Length > 0)
            {
                for (int i = 0; i < alphaKeys.Length; i++)
                {
                    float centerX = drawRect.x + alphaKeys[i].time * drawRect.width;
                    float topY = drawRect.y - MARGIN - MARKER_SIZE;

                    EditorGUIUtility.AddCursorRect(
                        new Rect(centerX - MARKER_SIZE, topY, MARKER_SIZE * 2, MARKER_SIZE),
                        MouseCursor.Arrow
                    );

                    bool isSelected = IsGradientKeySelected(i, GradientKeyType.Alpha);

                    if (isSelected)
                    {
                        Rect highlightRect = new Rect(
                            centerX - MARKER_SIZE - 1,
                            topY - 1,
                            MARKER_SIZE * 2 + 2,
                            MARKER_SIZE + 2
                        );
                        EditorGUI.DrawRect(highlightRect, new Color(1f, 1f, 0f, 0.2f));
                    }

                    Rect markerRect = new Rect(centerX - MARKER_SIZE, topY, MARKER_SIZE * 2, MARKER_SIZE);
                    float borderRadius = MARKER_SIZE * 0.2f;

                    Color alphaColor = new Color(0.7f, 0.7f, 0.7f, alphaKeys[i].alpha);
                    Handles.color = alphaColor;
                    Handles.DrawSolidRectangleWithOutline(
                        markerRect,
                        alphaColor,
                        isSelected ? selectedBorderColor : Color.white
                    );

                    if (isSelected)
                    {
                        Handles.color = selectedBorderColor;
                        GUI.DrawTexture(markerRect, MakeRoundedRectTexture(markerRect.size, borderRadius, selectedBorderColor, SELECTED_BORDER_WIDTH), ScaleMode.StretchToFill);
                    }
                    else
                    {
                        Handles.color = Color.white;
                        GUI.DrawTexture(markerRect, MakeRoundedRectTexture(markerRect.size, borderRadius, Color.white, NORMAL_BORDER_WIDTH), ScaleMode.StretchToFill);
                    }
                }
            }

            Handles.EndGUI();
        }

        private bool IsGradientKeySelected(int keyIndex, GradientKeyType keyType)
        {
            if (_state.SelectedGradientKey == null)
                return false;

            var (selectedTrackIndex, selectedKeyIndex, selectedKeyType) = _state.SelectedGradientKey.Value;

            if (_state.SelectedTrackIndices.Count == 1 &&
                _state.SelectedTrackIndices.First() == selectedTrackIndex &&
                selectedKeyIndex == keyIndex &&
                selectedKeyType == keyType)
            {
                return true;
            }

            return false;
        }

        private Texture2D MakeRoundedRectTexture(Vector2 size, float borderRadius, Color borderColor, float borderWidth)
        {
            int width = Mathf.RoundToInt(size.x);
            int height = Mathf.RoundToInt(size.y);
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.HideAndDontSave;

            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color(0, 0, 0, 0);
            }

            int borderInt = Mathf.RoundToInt(borderWidth);
            int radius = Mathf.RoundToInt(borderRadius);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool isBorder = false;

                    if (y < borderInt) isBorder = true;
                    else if (y >= height - borderInt) isBorder = true;
                    else if (x < borderInt) isBorder = true;
                    else if (x >= width - borderInt) isBorder = true;

                    if (!isBorder)
                    {
                        if (x < radius && y < radius)
                        {
                            float dx = x - radius + 1;
                            float dy = y - radius + 1;
                            if (dx * dx + dy * dy <= radius * radius)
                                isBorder = true;
                        }
                        else if (x >= width - radius && y < radius)
                        {
                            float dx = x - (width - radius);
                            float dy = y - radius + 1;
                            if (dx * dx + dy * dy <= radius * radius)
                                isBorder = true;
                        }
                        else if (x < radius && y >= height - radius)
                        {
                            float dx = x - radius + 1;
                            float dy = y - (height - radius);
                            if (dx * dx + dy * dy <= radius * radius)
                                isBorder = true;
                        }
                        else if (x >= width - radius && y >= height - radius)
                        {
                            float dx = x - (width - radius);
                            float dy = y - (height - radius);
                            if (dx * dx + dy * dy <= radius * radius)
                                isBorder = true;
                        }
                    }

                    if (isBorder)
                    {
                        pixels[y * width + x] = borderColor;
                    }
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }
    }
}
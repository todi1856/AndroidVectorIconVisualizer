using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Minimal Android VectorDrawable renderer for UI Toolkit.
///
/// Supports:
/// - vector
/// - path (absolute M/L/H/V/C/S/Z)
/// - fillColor (solid)
/// - fillColor via aapt:attr gradient (linear – approximated with Unity Gradient)
/// - strokeColor / strokeWidth
/// - fillType (nonZero / evenOdd)
///
/// Does NOT support:
/// - Radial / sweep gradients
/// - clipping / trimPath / arcs
/// - Animated vectors
/// </summary>
public class AndroidVectorDrawableElement : VisualElement
{
    // ---------------------------------------------------------------
    // Namespace constants
    // ---------------------------------------------------------------

    private static readonly XNamespace AndroidNs =
        "http://schemas.android.com/apk/res/android";

    private static readonly XNamespace AaptNs =
        "http://schemas.android.com/aapt";

    // ---------------------------------------------------------------
    // State
    // ---------------------------------------------------------------

    private readonly List<VectorPath> _paths = new();

    private float _viewportWidth = 24f;
    private float _viewportHeight = 24f;

    // ---------------------------------------------------------------
    // Public constructor
    // ---------------------------------------------------------------

    public AndroidVectorDrawableElement(string xml)
    {
        Parse(xml);
        generateVisualContent += OnGenerateVisualContent;
    }

    // ---------------------------------------------------------------
    // Internal types
    // ---------------------------------------------------------------

    private enum FillType { NonZero, EvenOdd }

    private class GradientData
    {
        public enum GradientType { Linear, Radial, Sweep }

        public GradientType Type = GradientType.Linear;

        // Geometry (in viewport coordinates)
        public float StartX, StartY, EndX, EndY;

        // Color stops: offset → color
        public List<(float offset, Color color)> Stops = new();

        /// <summary>
        /// Build a Unity Gradient from the stops list.
        /// Unity Gradient supports up to 8 color keys + 8 alpha keys.
        /// </summary>
        public Gradient ToUnityGradient()
        {
            var gradient = new Gradient();
            int maxKeys = Mathf.Min(Stops.Count, 8);

            var colorKeys = new GradientColorKey[maxKeys];
            var alphaKeys = new GradientAlphaKey[maxKeys];

            for (int i = 0; i < maxKeys; i++)
            {
                float t = Stops[i].offset;
                Color c = Stops[i].color;

                colorKeys[i] = new GradientColorKey(new Color(c.r, c.g, c.b), t);
                alphaKeys[i] = new GradientAlphaKey(c.a, t);
            }

            gradient.SetKeys(colorKeys, alphaKeys);
            return gradient;
        }

        /// <summary>
        /// Simple approximation: sample the gradient at the midpoint.
        /// Used as a solid-color fallback when Painter2D doesn't support
        /// gradient fills in the current Unity version.
        /// </summary>
        public Color MidpointColor()
        {
            if (Stops.Count == 0)
                return Color.white;

            if (Stops.Count == 1)
                return Stops[0].color;

            // Lerp between the first and last stop at t = 0.5
            Color a = Stops[0].color;
            Color b = Stops[^1].color;
            return Color.Lerp(a, b, 0.5f);
        }
    }

    private class VectorPath
    {
        public List<PathCommand> Commands = new();

        // Fill
        public Color FillColor = Color.white;
        public FillType Fill = FillType.NonZero;

        // Optional gradient (overrides FillColor when present)
        public GradientData? Gradient;

        // Stroke
        public bool HasStroke;
        public Color StrokeColor;
        public float StrokeWidth;
    }

    private enum CommandType
    {
        MoveTo,
        LineTo,
        CubicTo,
        SmoothCubicTo,
        Close
    }

    private struct PathCommand
    {
        public CommandType Type;
        public Vector2 P1, P2, P3;
    }

    // ---------------------------------------------------------------
    // XML Parsing
    // ---------------------------------------------------------------

    private void Parse(string xml)
    {
        var doc = XDocument.Parse(xml);
        var vector = doc.Root
            ?? throw new Exception("Invalid VectorDrawable XML: missing root element.");

        _viewportWidth = ParseFloat(vector.Attribute(AndroidNs + "viewportWidth")?.Value, 24f);
        _viewportHeight = ParseFloat(vector.Attribute(AndroidNs + "viewportHeight")?.Value, 24f);

        foreach (var pathElement in vector.Elements("path"))
            _paths.Add(ParsePath(pathElement));
    }

    private static VectorPath ParsePath(XElement pathElement)
    {
        var path = new VectorPath();

        // ── Fill type ────────────────────────────────────────────────
        string? fillTypeStr = pathElement.Attribute(AndroidNs + "fillType")?.Value;
        path.Fill = fillTypeStr?.ToLowerInvariant() == "evenodd"
            ? FillType.EvenOdd
            : FillType.NonZero;

        // ── Solid fill color ─────────────────────────────────────────
        string? fillColor = pathElement.Attribute(AndroidNs + "fillColor")?.Value;
        if (!string.IsNullOrEmpty(fillColor))
            ColorUtility.TryParseHtmlString(fillColor, out path.FillColor);

        // ── Gradient fill via aapt:attr ──────────────────────────────
        //
        // Structure inside the path element:
        //   <aapt:attr name="android:fillColor">
        //     <gradient android:type="linear" … >
        //       <item android:offset="0.0" android:color="#…" />
        //       <item android:offset="1.0" android:color="#…" />
        //     </gradient>
        //   </aapt:attr>

        var aaptAttr = pathElement.Element(AaptNs + "attr");
        if (aaptAttr != null)
        {
            string attrName = aaptAttr.Attribute("name")?.Value ?? "";

            if (attrName == "android:fillColor")
            {
                var gradientElement = aaptAttr.Element("gradient");
                if (gradientElement != null)
                    path.Gradient = ParseGradient(gradientElement);
            }
        }

        // ── Stroke ───────────────────────────────────────────────────
        string? strokeColor = pathElement.Attribute(AndroidNs + "strokeColor")?.Value;
        if (!string.IsNullOrEmpty(strokeColor))
        {
            // Treat fully-transparent strokes as no stroke
            Color sc = Color.clear;
            ColorUtility.TryParseHtmlString(strokeColor, out sc);

            if (sc.a > 0.001f)
            {
                path.HasStroke = true;
                path.StrokeColor = sc;
                path.StrokeWidth = ParseFloat(
                    pathElement.Attribute(AndroidNs + "strokeWidth")?.Value, 1f);
            }
        }

        // ── Path data ────────────────────────────────────────────────
        string? pathData = pathElement.Attribute(AndroidNs + "pathData")?.Value;
        if (!string.IsNullOrEmpty(pathData))
            path.Commands = ParsePathData(pathData);

        return path;
    }

    private static GradientData ParseGradient(XElement gradientElement)
    {
        var gd = new GradientData();

        // Gradient type
        string typeStr = gradientElement.Attribute(AndroidNs + "type")?.Value ?? "linear";
        gd.Type = typeStr.ToLowerInvariant() switch
        {
            "radial" => GradientData.GradientType.Radial,
            "sweep" => GradientData.GradientType.Sweep,
            _ => GradientData.GradientType.Linear
        };

        // Geometry
        gd.StartX = ParseFloat(gradientElement.Attribute(AndroidNs + "startX")?.Value, 0f);
        gd.StartY = ParseFloat(gradientElement.Attribute(AndroidNs + "startY")?.Value, 0f);
        gd.EndX = ParseFloat(gradientElement.Attribute(AndroidNs + "endX")?.Value, 0f);
        gd.EndY = ParseFloat(gradientElement.Attribute(AndroidNs + "endY")?.Value, 0f);

        // Color stops  <item android:offset="…" android:color="#…" />
        foreach (var item in gradientElement.Elements("item"))
        {
            float offset = ParseFloat(item.Attribute(AndroidNs + "offset")?.Value, 0f);
            string? colorStr = item.Attribute(AndroidNs + "color")?.Value;

            Color color = Color.clear;
            if (!string.IsNullOrEmpty(colorStr))
                ColorUtility.TryParseHtmlString(colorStr, out color);

            gd.Stops.Add((offset, color));
        }

        // Sort stops by offset (spec requires it, but be defensive)
        gd.Stops.Sort((a, b) => a.offset.CompareTo(b.offset));

        return gd;
    }

    // ---------------------------------------------------------------
    // Rendering
    // ---------------------------------------------------------------

    private void OnGenerateVisualContent(MeshGenerationContext ctx)
    {
        var painter = ctx.painter2D;

        float scaleX = contentRect.width / _viewportWidth;
        float scaleY = contentRect.height / _viewportHeight;

        foreach (var path in _paths)
        {
            painter.BeginPath();

            foreach (var cmd in path.Commands)
            {
                switch (cmd.Type)
                {
                    case CommandType.MoveTo:
                        painter.MoveTo(Scale(cmd.P1, scaleX, scaleY));
                        break;

                    case CommandType.LineTo:
                        painter.LineTo(Scale(cmd.P1, scaleX, scaleY));
                        break;

                    case CommandType.CubicTo:
                    case CommandType.SmoothCubicTo:
                        painter.BezierCurveTo(
                            Scale(cmd.P1, scaleX, scaleY),
                            Scale(cmd.P2, scaleX, scaleY),
                            Scale(cmd.P3, scaleX, scaleY));
                        break;

                    case CommandType.Close:
                        painter.ClosePath();
                        break;
                }
            }

            // ── Fill ─────────────────────────────────────────────────
            ApplyFill(painter, path, scaleX, scaleY);

            // ── Stroke ───────────────────────────────────────────────
            if (path.HasStroke)
            {
                painter.strokeColor = path.StrokeColor;
                painter.lineWidth = path.StrokeWidth * Mathf.Min(scaleX, scaleY);
                painter.Stroke();
            }
        }
    }

    private static void ApplyFill(
        Painter2D painter,
        VectorPath path,
        float scaleX,
        float scaleY)
    {
        // FillRule mapping
        var fillRule = path.Fill == FillType.EvenOdd
            ? FillRule.OddEven
            : FillRule.NonZero;

        if (path.Gradient != null)
        {
            // ── Attempt gradient fill ─────────────────────────────
            // Unity UI Toolkit 2022.2+ exposes Painter2D.fillGradient.
            // We try it via reflection so the code compiles on older
            // versions too, and fall back to midpoint solid color.

            bool gradientApplied = TryApplyGradient(painter, path.Gradient, scaleX, scaleY);

            if (!gradientApplied)
            {
                // Fallback: use averaged midpoint color
                Color mid = path.Gradient.MidpointColor();
                if (mid.a > 0.001f)
                {
                    painter.fillColor = mid;
                    painter.Fill(fillRule);
                }
            }
            else
            {
                painter.Fill(fillRule);
            }
        }
        else
        {
            // ── Solid fill ────────────────────────────────────────
            if (path.FillColor.a > 0.001f)
            {
                painter.fillColor = path.FillColor;
                painter.Fill(fillRule);
            }
        }
    }

    /// <summary>
    /// Tries to assign a Unity Gradient to Painter2D.fillGradient.
    /// This property was introduced in Unity 2022.2.
    /// Returns true if the assignment succeeded.
    /// </summary>
    private static bool TryApplyGradient(
        Painter2D painter,
        GradientData gd,
        float scaleX,
        float scaleY)
    {
        if (gd.Type != GradientData.GradientType.Linear)
            return false; // Only linear gradients are approximated

        try
        {
            var prop = typeof(Painter2D).GetProperty("fillGradient");
            if (prop == null)
                return false;

            prop.SetValue(painter, gd.ToUnityGradient());
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private static Vector2 Scale(Vector2 v, float sx, float sy)
        => new(v.x * sx, v.y * sy);

    private static float ParseFloat(string? s, float fallback)
    {
        if (string.IsNullOrEmpty(s))
            return fallback;

        s = s.Replace("dp", "").Trim();

        return float.TryParse(
            s,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out float result)
            ? result
            : fallback;
    }

    // ---------------------------------------------------------------
    // PATH DATA PARSER
    // Handles absolute & relative: M m L l H h V v C c S s Z z
    // ---------------------------------------------------------------

    private static List<PathCommand> ParsePathData(string data)
    {
        var commands = new List<PathCommand>();

        int i = 0;
        char cmd = ' ';
        Vector2 current = Vector2.zero;
        Vector2 subPathStart = Vector2.zero;
        Vector2 lastCtrl = Vector2.zero; // for S/s smooth cubic

        while (i < data.Length)
        {
            SkipSeparators(data, ref i);
            if (i >= data.Length) break;

            char c = data[i];
            if (char.IsLetter(c))
            {
                cmd = c;
                i++;
                // 'Z'/'z' has no arguments – handle immediately
                if (char.ToUpperInvariant(cmd) == 'Z')
                {
                    commands.Add(new PathCommand { Type = CommandType.Close });
                    current = subPathStart;
                    lastCtrl = current;
                    continue;
                }
            }

            bool rel = char.IsLower(cmd);
            char upper = char.ToUpperInvariant(cmd);

            switch (upper)
            {
                // ── Move ────────────────────────────────────────────
                case 'M':
                    {
                        Vector2 p = ReadPoint(data, ref i);
                        if (rel) p += current;

                        current = p;
                        subPathStart = p;
                        lastCtrl = p;

                        commands.Add(new PathCommand
                        {
                            Type = CommandType.MoveTo,
                            P1 = p
                        });

                        // Subsequent coordinate pairs after M become implicit L
                        cmd = rel ? 'l' : 'L';
                        break;
                    }

                // ── Line ────────────────────────────────────────────
                case 'L':
                    {
                        Vector2 p = ReadPoint(data, ref i);
                        if (rel) p += current;

                        current = p;
                        lastCtrl = p;

                        commands.Add(new PathCommand
                        {
                            Type = CommandType.LineTo,
                            P1 = p
                        });
                        break;
                    }

                // ── Horizontal line ──────────────────────────────────
                case 'H':
                    {
                        float x = ReadFloat(data, ref i);
                        current = rel
                            ? new Vector2(current.x + x, current.y)
                            : new Vector2(x, current.y);
                        lastCtrl = current;

                        commands.Add(new PathCommand
                        {
                            Type = CommandType.LineTo,
                            P1 = current
                        });
                        break;
                    }

                // ── Vertical line ────────────────────────────────────
                case 'V':
                    {
                        float y = ReadFloat(data, ref i);
                        current = rel
                            ? new Vector2(current.x, current.y + y)
                            : new Vector2(current.x, y);
                        lastCtrl = current;

                        commands.Add(new PathCommand
                        {
                            Type = CommandType.LineTo,
                            P1 = current
                        });
                        break;
                    }

                // ── Cubic Bézier ─────────────────────────────────────
                case 'C':
                    {
                        Vector2 p1 = ReadPoint(data, ref i);
                        Vector2 p2 = ReadPoint(data, ref i);
                        Vector2 p3 = ReadPoint(data, ref i);

                        if (rel) { p1 += current; p2 += current; p3 += current; }

                        lastCtrl = p2;
                        current = p3;

                        commands.Add(new PathCommand
                        {
                            Type = CommandType.CubicTo,
                            P1 = p1,
                            P2 = p2,
                            P3 = p3
                        });
                        break;
                    }

                // ── Smooth cubic Bézier ───────────────────────────────
                // Control point 1 is the reflection of the previous CP2.
                case 'S':
                    {
                        Vector2 p2 = ReadPoint(data, ref i);
                        Vector2 p3 = ReadPoint(data, ref i);

                        if (rel) { p2 += current; p3 += current; }

                        // Reflect last control point
                        Vector2 p1 = 2f * current - lastCtrl;

                        lastCtrl = p2;
                        current = p3;

                        commands.Add(new PathCommand
                        {
                            Type = CommandType.SmoothCubicTo,
                            P1 = p1,
                            P2 = p2,
                            P3 = p3
                        });
                        break;
                    }

                default:
                    throw new Exception($"Unsupported SVG/VD command: '{cmd}'");
            }
        }

        return commands;
    }

    // ---------------------------------------------------------------
    // Low-level token readers
    // ---------------------------------------------------------------

    private static Vector2 ReadPoint(string s, ref int i)
        => new(ReadFloat(s, ref i), ReadFloat(s, ref i));

    private static void SkipSeparators(string s, ref int i)
    {
        while (i < s.Length && (char.IsWhiteSpace(s[i]) || s[i] == ','))
            i++;
    }

    private static float ReadFloat(string s, ref int i)
    {
        SkipSeparators(s, ref i);

        int start = i;
        bool hasDot = false;
        bool hasExp = false;

        while (i < s.Length)
        {
            char c = s[i];

            if (char.IsDigit(c)) { i++; continue; }
            if (c == '.' && !hasDot) { hasDot = true; i++; continue; }
            if ((c == 'e' || c == 'E')
                && !hasExp && i > start) { hasExp = true; i++; continue; }
            if ((c == '+' || c == '-')
                && i == start) { i++; continue; }
            // Allow sign directly after exponent marker
            if ((c == '+' || c == '-')
                && hasExp
                && (s[i - 1] == 'e' || s[i - 1] == 'E')) { i++; continue; }

            break;
        }

        string token = s.Substring(start, i - start);

        SkipSeparators(s, ref i);

        return float.Parse(token, CultureInfo.InvariantCulture);
    }
}

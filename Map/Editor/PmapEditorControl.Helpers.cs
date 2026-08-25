using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Polaris.Map.Authoring;

namespace PolarisTools.Map.Editor;

public partial class PmapEditorControl
{
    private static bool IsImageElement(PmapElementKind kind)
        => kind == PmapElementKind.Chip || kind == PmapElementKind.Picture;

    private static string ElementKindName(PmapElementKind kind)
    {
        switch (kind)
        {
            case PmapElementKind.Chip: return "Chip";
            case PmapElementKind.Picture: return "Picture";
            case PmapElementKind.LabelPoint: return "Label Point (LP)";
            case PmapElementKind.Gradation: return "Gradation (GRD)";
            case PmapElementKind.SubMap: return "Sub-map (SM)";
            case PmapElementKind.Joint: return "Joint";
            default: return kind.ToString();
        }
    }

    private static string ElementPrefix(PmapElementKind kind)
    {
        switch (kind)
        {
            case PmapElementKind.LabelPoint: return "lp";
            case PmapElementKind.Gradation: return "grd";
            case PmapElementKind.SubMap: return "sm";
            default: return kind.ToString().ToLowerInvariant();
        }
    }

    private static string ElementGlyph(PmapElementKind kind)
    {
        switch (kind)
        {
            case PmapElementKind.Chip: return "■";
            case PmapElementKind.Picture: return "▰";
            case PmapElementKind.LabelPoint: return "▣";
            case PmapElementKind.Gradation: return "◩";
            case PmapElementKind.SubMap: return "▧";
            case PmapElementKind.Joint: return "⌁";
            default: return "·";
        }
    }

    private static string DefaultElementColor(PmapElementKind kind)
    {
        switch (kind)
        {
            case PmapElementKind.Chip: return "#5B6477";
            case PmapElementKind.Picture: return "#B68A5A";
            case PmapElementKind.LabelPoint: return "#4B9B77";
            case PmapElementKind.Gradation: return "#8874B8AA";
            case PmapElementKind.SubMap: return "#4A83A8";
            case PmapElementKind.Joint: return "#E0A048";
            default: return "#7F7F7F";
        }
    }

    private static string DefaultElementLabel(PmapElement element)
    {
        if (IsImageElement(element.Kind)) return Path.GetFileNameWithoutExtension(element.Image);
        if (element.Kind == PmapElementKind.SubMap) return element.TargetMap;
        if (element.Kind == PmapElementKind.Joint) return "Joint";
        return element.Key;
    }

    private static string RuntimeKey(PmapElement element)
    {
        if (IsImageElement(element.Kind)) return element.Image;
        if (element.Kind == PmapElementKind.SubMap) return element.TargetMap;
        return element.Key;
    }

    private static string Pair(float x, float y) => F(x) + "," + F(y);
    private static string FloatList(IEnumerable<float> values) => string.Join(",", values.Select(F));

    private static bool TryPair(string value, out float x, out float y)
    {
        x = y = 0;
        string[] fields = (value ?? "").Split(',');
        return fields.Length == 2 && TryFloat(fields[0].Trim(), out x) && TryFloat(fields[1].Trim(), out y);
    }

    private static bool TryInts(string value, int count, out int[] values)
    {
        string[] fields = (value ?? "").Split(',');
        values = new int[count];
        if (fields.Length != count) return false;
        for (int i = 0; i < count; i++)
            if (!int.TryParse(fields[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out values[i])) return false;
        return true;
    }

    private static bool TryReplaceFloatList(ICollection<float> target, string text)
    {
        var parsed = new List<float>();
        if (!string.IsNullOrWhiteSpace(text))
        {
            foreach (string field in text.Split(','))
            {
                if (!TryFloat(field.Trim(), out float value)) return false;
                parsed.Add(value);
            }
        }
        target.Clear();
        foreach (float value in parsed) target.Add(value);
        return true;
    }

    private static bool TryReplaceJointPoints(ICollection<PmapJointPoint> target, string text)
    {
        var parsed = new List<PmapJointPoint>();
        string[] lines = (text ?? "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string line in lines)
        {
            string[] fields = line.Split(',');
            if (fields.Length < 2 || fields.Length > 3
                || !TryFloat(fields[0].Trim(), out float x) || !TryFloat(fields[1].Trim(), out float y)) return false;
            parsed.Add(new PmapJointPoint { X = x, Y = y, ChipId = fields.Length == 3 ? fields[2].Trim() : "" });
        }
        target.Clear();
        foreach (PmapJointPoint point in parsed) target.Add(point);
        return true;
    }

    private static void ReplaceLines(ICollection<string> target, string text)
    {
        target.Clear();
        foreach (string line in (text ?? "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            target.Add(line);
    }

    private static string FormatMeshRects(IEnumerable<PmapMeshRect> values)
        => string.Join(Environment.NewLine, values.Select(value => value.Index + "," + Pair(value.X, value.Y)
            + "," + F(value.Width) + "," + F(value.Height)));

    private static bool TryReplaceMeshRects(ICollection<PmapMeshRect> target, string text)
    {
        var parsed = new List<PmapMeshRect>();
        foreach (string line in (text ?? "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = line.Split(',');
            if (fields.Length != 5 || !int.TryParse(fields[0].Trim(), out int index)
                || !TryFloat(fields[1].Trim(), out float x) || !TryFloat(fields[2].Trim(), out float y)
                || !TryFloat(fields[3].Trim(), out float width) || !TryFloat(fields[4].Trim(), out float height)) return false;
            parsed.Add(new PmapMeshRect { Index = index, X = x, Y = y, Width = width, Height = height });
        }
        target.Clear();
        foreach (PmapMeshRect value in parsed) target.Add(value);
        return true;
    }
}

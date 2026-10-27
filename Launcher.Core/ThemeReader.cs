using System;
using System.Text.Json;

namespace Odinsons.ValheimLauncher
{
    /// <summary>
    /// Reads theme.json. A theme never makes a profile unusable, so nothing here throws: whatever is
    /// missing or malformed falls back to its default and the rest still applies.
    /// </summary>
    public static class ThemeReader
    {
        public const int SupportedSchema = 1;
        public const int MaxBytes = 16 * 1024;
        public const int MaxButtonRadius = 12;
        public const int MaxBorderWidth = 4;

        public static ThemeSpec Read(byte[] json)
        {
            if (json == null || json.Length == 0 || json.Length > MaxBytes)
                return ThemeSpec.Default;

            try
            {
                using JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
                JsonElement root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !TryInt(root, "themeSchema", out int schema) || schema != SupportedSchema)
                    return ThemeSpec.Default;

                int borderWidth = 0;
                int radius = ThemeSpec.DefaultButtonRadius;
                if (root.TryGetProperty("button", out JsonElement button) && button.ValueKind == JsonValueKind.Object)
                {
                    if (TryInt(button, "radius", out int r))
                        radius = Math.Clamp(r, 0, MaxButtonRadius);
                    if (TryInt(button, "borderWidth", out int w))
                        borderWidth = Math.Clamp(w, 0, MaxBorderWidth);
                }

                SchemeSpec dark = null, light = null;
                if (root.TryGetProperty("schemes", out JsonElement schemes) && schemes.ValueKind == JsonValueKind.Object)
                {
                    dark = ParseScheme(schemes, "dark", ColorSchemeKind.Dark, borderWidth);
                    light = ParseScheme(schemes, "light", ColorSchemeKind.Light, borderWidth);
                }

                return new ThemeSpec
                {
                    Layout = ParseLayout(root),
                    Online = TryString(root, "online", out string side) && side == "right" ? OnlineSide.Right : OnlineSide.Left,
                    Font = TryString(root, "font", out string font) && font == ThemeSpec.DefaultFont ? font : ThemeSpec.DefaultFont,
                    LogoFile = ParseLogo(root),
                    ButtonRadius = radius,
                    ButtonBorderWidth = borderWidth,
                    Dark = dark ?? light ?? ThemeSpec.DefaultScheme(ColorSchemeKind.Dark),
                    Light = light ?? dark ?? ThemeSpec.DefaultScheme(ColorSchemeKind.Light)
                };
            }
            catch (JsonException)
            {
                return ThemeSpec.Default;
            }
        }

        private static LayoutTemplate ParseLayout(JsonElement root)
        {
            if (!TryString(root, "layout", out string layout))
                return LayoutTemplate.Classic;
            return layout switch
            {
                "sidebar" => LayoutTemplate.Sidebar,
                "banner" => LayoutTemplate.Banner,
                "minimal" => LayoutTemplate.Minimal,
                _ => LayoutTemplate.Classic
            };
        }

        private static string ParseLogo(JsonElement root)
        {
            if (root.TryGetProperty("logo", out JsonElement logo) && logo.ValueKind == JsonValueKind.Object
                && TryString(logo, "file", out string file) && RegistryIndexReader.IsRelativePath(file))
                return file;
            return null;
        }

        private static SchemeSpec ParseScheme(JsonElement schemes, string name, ColorSchemeKind kind, int borderWidth)
        {
            if (!schemes.TryGetProperty(name, out JsonElement scheme) || scheme.ValueKind != JsonValueKind.Object)
                return null;

            SchemeSpec defaults = ThemeSpec.DefaultScheme(kind);
            Rgb accent = TryString(scheme, "accent", out string text) && Rgb.TryParse(text, out Rgb parsed)
                ? parsed : defaults.Accent;

            Paint fill = scheme.TryGetProperty("buttonFill", out JsonElement fillElement)
                && TryPaint(fillElement, allowNone: true, out Paint fillPaint) ? fillPaint : Paint.Solid(accent);
            Paint border = scheme.TryGetProperty("buttonBorder", out JsonElement borderElement)
                && TryPaint(borderElement, allowNone: true, out Paint borderPaint) ? borderPaint : Paint.None;

            if (fill.Kind == PaintKind.None && (border.Kind == PaintKind.None || borderWidth < 1))
                fill = Paint.Solid(accent);

            ArtSpec art = scheme.TryGetProperty("art", out JsonElement artElement) && TryArt(artElement, out ArtSpec artSpec)
                ? artSpec : ArtSpec.Default;

            return new SchemeSpec(accent, fill, border, art);
        }

        private static bool TryArt(JsonElement element, out ArtSpec art)
        {
            art = null;
            if (element.ValueKind != JsonValueKind.Object || !TryString(element, "type", out string type))
                return false;

            if (type == "image")
            {
                if (!TryString(element, "file", out string file) || !RegistryIndexReader.IsRelativePath(file))
                    return false;
                ArtPosition position = ArtPosition.Center;
                if (element.TryGetProperty("position", out JsonElement positionElement))
                {
                    string value = positionElement.ValueKind == JsonValueKind.String ? positionElement.GetString() : null;
                    switch (value)
                    {
                        case "left": position = ArtPosition.Left; break;
                        case "center": position = ArtPosition.Center; break;
                        case "right": position = ArtPosition.Right; break;
                        default: return false;
                    }
                }
                art = ArtSpec.FromImage(file, position);
                return true;
            }

            if (!TryPaint(element, allowNone: false, out Paint paint))
                return false;
            art = ArtSpec.FromPaint(paint);
            return true;
        }

        private static bool TryPaint(JsonElement element, bool allowNone, out Paint paint)
        {
            paint = default;
            if (element.ValueKind != JsonValueKind.Object || !TryString(element, "type", out string type))
                return false;

            switch (type)
            {
                case "none" when allowNone:
                    paint = Paint.None;
                    return true;
                case "solid":
                    if (!TryColor(element, "color", out Rgb color))
                        return false;
                    paint = Paint.Solid(color);
                    return true;
                case "gradient":
                    if (!TryColor(element, "from", out Rgb from) || !TryColor(element, "to", out Rgb to))
                        return false;
                    GradientDirection direction = GradientDirection.Horizontal;
                    if (element.TryGetProperty("direction", out JsonElement directionElement))
                    {
                        string value = directionElement.ValueKind == JsonValueKind.String ? directionElement.GetString() : null;
                        switch (value)
                        {
                            case "horizontal": direction = GradientDirection.Horizontal; break;
                            case "vertical": direction = GradientDirection.Vertical; break;
                            case "diagonal": direction = GradientDirection.Diagonal; break;
                            default: return false;
                        }
                    }
                    paint = Paint.Gradient(from, to, direction);
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryColor(JsonElement obj, string name, out Rgb color)
        {
            color = default;
            return TryString(obj, name, out string text) && Rgb.TryParse(text, out color);
        }

        private static bool TryString(JsonElement obj, string name, out string value)
        {
            value = null;
            if (!obj.TryGetProperty(name, out JsonElement element) || element.ValueKind != JsonValueKind.String)
                return false;
            value = element.GetString();
            return true;
        }

        private static bool TryInt(JsonElement obj, string name, out int value)
        {
            value = 0;
            return obj.TryGetProperty(name, out JsonElement element) && element.ValueKind == JsonValueKind.Number
                && element.TryGetInt32(out value);
        }
    }
}

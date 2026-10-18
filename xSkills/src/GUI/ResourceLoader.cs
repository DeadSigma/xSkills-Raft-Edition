using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HMLLibrary;
using UnityEngine;

namespace xSkills.GUI
{
    internal static class ResourceLoader
    {
        private static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, byte[]> Bytes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        private static readonly Assembly Source = typeof(ResourceLoader).Assembly;
        private static Mod mod;
        private static int missingWarnings;

        public static void Configure(Mod currentMod)
        {
            Clear();
            mod = currentMod;
            Debug.Log("[xSkills] Asset loader initialized (RML archive + DLL fallback)");
            Verify("assets/textures/gui/skilltree/elements/bg.png");
            Verify("assets/textures/gui/skilltree/elements/frame.png");
            Verify("assets/textures/gui/skilltree/skillicon/cooking.png");
            Verify("assets/textures/gui/skilltree/abilityicon/steadyhelm.png");
            Verify("assets/sounds/open.ogg");
        }

        private static void Verify(string path)
        {
            byte[] data = FileBytes(path, false);
            Debug.Log("[xSkills] Asset " + path + ": " + (data == null ? "NOT FOUND" : "OK, " + data.Length + " bytes"));
        }

        public static Sprite Sprite(string category, string name, float border = 0f)
        {
            string id = category + "/" + name + ":" + border;
            Sprite result;
            if (Sprites.TryGetValue(id, out result)) return result;
            Texture2D texture = Texture(category, name);
            if (texture == null) return null;
            float b = Mathf.Min(border, Mathf.Min(texture.width, texture.height) * .48f);
            result = UnityEngine.Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
            result.name = category + "/" + name;
            Sprites[id] = result;
            return result;
        }

        public static Texture2D Texture(string category, string name)
        {
            string id = category + "/" + name;
            Texture2D cached;
            if (Textures.TryGetValue(id, out cached)) return cached;
            string path = "assets/textures/gui/skilltree/" + category + "/" + name + ".png";
            byte[] data = FileBytes(path);
            if (data == null)
            {
                Textures[id] = null;
                return null;
            }
            var texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
            texture.name = id;
            texture.filterMode = FilterMode.Bilinear;
            if (!texture.LoadImage(data))
            {
                Debug.LogWarning("[xSkills] Invalid PNG: " + path);
                UnityEngine.Object.Destroy(texture);
                Textures[id] = null;
                return null;
            }
            Textures[id] = texture;
            return texture;
        }

        public static byte[] FileBytes(string relativePath, bool logMissing = true)
        {
            relativePath = relativePath.Replace('\\', '/').TrimStart('/');
            byte[] cached;
            if (Bytes.TryGetValue(relativePath, out cached)) return cached;
            byte[] buffer = null;
            if (mod != null)
            {
                try
                {
                    // Файл считывается непосредственно из архива RML
                    buffer = mod.GetEmbeddedFileBytes(relativePath);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[xSkills] RML asset read failed: " + relativePath + " - " + ex.Message);
                }
            }
            if (buffer == null || buffer.Length == 0)
                buffer = ReadManifestResource(relativePath);
            if (buffer != null && buffer.Length == 0) buffer = null;
            Bytes[relativePath] = buffer;
            if (buffer == null && logMissing && missingWarnings++ < 15)
                Debug.LogWarning("[xSkills] Missing asset: " + relativePath + " (check the .rmod /assets folder)");
            return buffer;
        }

        private static byte[] ReadManifestResource(string path)
        {
            string suffix = path.Replace('/', '.');
            foreach (string name in Source.GetManifestResourceNames())
            {
                if (!name.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase)) continue;
                using (Stream input = Source.GetManifestResourceStream(name))
                {
                    if (input == null) return null;
                    using (var output = new MemoryStream())
                    {
                        input.CopyTo(output);
                        return output.ToArray();
                    }
                }
            }
            return null;
        }

        public static void Clear()
        {
            foreach (Sprite sprite in Sprites.Values) if (sprite != null) UnityEngine.Object.Destroy(sprite);
            foreach (Texture2D texture in Textures.Values) if (texture != null) UnityEngine.Object.Destroy(texture);
            Sprites.Clear();
            Textures.Clear();
            Bytes.Clear();
            missingWarnings = 0;
            mod = null;
        }
    }
}

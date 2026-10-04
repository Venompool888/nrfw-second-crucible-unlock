using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace CrucibleUnlock
{
    // Plain managed resource reader, also exercised without loading game assemblies.
    internal static class RitualBloodRouteData
    {
        internal static Exception RestoreOrHide(Action restore, Action hide)
        {
            try { restore(); return null; }
            catch (Exception restoreError)
            {
                try { hide(); }
                catch (Exception hideError) { return new AggregateException(restoreError, hideError); }
                return restoreError;
            }
        }
        internal const string Prefix = "NRFW.SecondBowlBlood.";
        internal sealed class Layout
        {
            public int Version { get; set; }
            public float[] Position { get; set; }
            public float[] Rotation { get; set; }
            public float[] Scale { get; set; }
            public Map[] Textures { get; set; }
        }
        internal sealed class Map
        {
            public int Part { get; set; }
            public string Property { get; set; }
            public string Resource { get; set; }
            public int Size { get; set; }
            public string RawSha256 { get; set; }
            public int? SourceColorSpace { get; set; }
        }
        internal static Stream Open(string name) => typeof(RitualBloodRouteData).Assembly.GetManifestResourceStream(Prefix + name)
            ?? throw new InvalidDataException("Missing blood route resource: " + name);
        internal static Layout ReadLayout()
        {
            using var stream = Open("layout.json");
            var layout = JsonSerializer.Deserialize<Layout>(stream);
            if (layout == null || layout.Version != 1 || layout.Position?.Length != 3 || layout.Rotation?.Length != 4 ||
                layout.Scale?.Length != 3 || layout.Textures?.Length != 6 ||
                layout.Position.Concat(layout.Rotation).Concat(layout.Scale).Any(v => !float.IsFinite(v)) ||
                layout.Scale.Any(v => v <= 0)) throw new InvalidDataException("Invalid blood route layout");
            foreach (int part in new[] { 1, 2 })
                foreach (string property in new[] { "_AlbedoTex", "_OffsetMaskTex", "_NoiseTex" })
                    if (layout.Textures.Count(t => t.Part == part && t.Property == property && t.Size == 1024 &&
                        (t.SourceColorSpace == 0 || t.SourceColorSpace == 1)) != 1)
                        throw new InvalidDataException("Invalid blood route map set");
            return layout;
        }
        internal static byte[] ReadPixels(Map map)
        {
            using var stream = Open(map.Resource);
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var decoded = new MemoryStream();
            gzip.CopyTo(decoded);
            byte[] pixels = decoded.ToArray();
            if (pixels.Length != map.Size * map.Size * 4 ||
                !Convert.ToHexString(SHA256.HashData(pixels)).Equals(map.RawSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Invalid blood route pixel payload: " + map.Resource);
            return pixels;
        }
    }
}

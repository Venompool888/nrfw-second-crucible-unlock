using System;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using CrucibleUnlock;

internal static class RitualBloodRouteDataTests
{
    internal static void Run()
    {
        var layout = RitualBloodRouteData.ReadLayout();
        // Independent native-scene reference: second bowl transform must put the
        // decal origin back on its real ground-engraving center, not the old route.
        var bowl = new Matrix4x4(1.9513693f,0,0.9099389f,0, 0,2.1530979f,0,0,
            -0.9099389f,0,1.9513693f,0, -4101.98645f,39.702f,-2138.33142f,1);
        var p = new Vector3(layout.Position[0],layout.Position[1],layout.Position[2]);
        var actual = Vector3.Transform(p,bowl);
        var expected = new Vector3(-4100.69458f,38.720002f,-2140.76659f);
        if (Vector3.Distance(actual,expected) > .002f) throw new Exception("FAIL ground engraving world anchor");
        float length = layout.Rotation.Sum(x => x*x);
        if (Math.Abs(length-1) > .00001f) throw new Exception("FAIL route quaternion normalization");
        foreach (var map in layout.Textures)
            if (RitualBloodRouteData.ReadPixels(map).Length != 4194304) throw new Exception("FAIL route RGBA resource");
        using var sourceMetadata = JsonDocument.Parse(RitualBloodRouteData.Open("layout.json"));
        foreach (var map in sourceMetadata.RootElement.GetProperty("Textures").EnumerateArray())
            if (!map.TryGetProperty("SourceColorSpace", out var colorSpace) || colorSpace.GetInt32() != 1)
                throw new Exception("FAIL color space must come from all six verified original texture assets");
        bool hidden = false;
        var error = RitualBloodRouteData.RestoreOrHide(() => throw new InvalidOperationException("material sync failed"), () => hidden = true);
        if (error == null || !hidden) throw new Exception("FAIL auxiliary route rollback must hide failed clone without escaping to offering cleanup");
        var both = RitualBloodRouteData.RestoreOrHide(() => throw new Exception("restore"), () => throw new Exception("hide"));
        if (both is not AggregateException) throw new Exception("FAIL rollback/hide errors must remain contained");
        Console.WriteLine("PASS: 16 blood route anchor/rotation/payload/color-space/failure-isolation checks; no game assemblies loaded.");
    }
}

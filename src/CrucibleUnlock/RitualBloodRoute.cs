using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Il2CppMoon.Forsaken;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace CrucibleUnlock
{
    internal static class RitualBloodRoute
    {
        // Six maps, shared by this mod's second-bowl views for the process lifetime.
        // Textures are never attached to the native first bowl or its materials.
        private static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>();
        private static RitualBloodRouteData.Layout _layout;
        private sealed class TextureChange
        {
            internal CustomizedMaterial.MaterialPropertyTexture Property;
            internal Texture Value, Atlas, NewValue;
            internal Vector4 AtlasUvs;
            internal bool Enabled, WasEnabled;
        }
        private sealed class Change
        {
            internal Transform Transform;
            internal Vector3 Position, Scale;
            internal Quaternion Rotation;
            internal CustomizedMaterial Owner;
            internal CustomizedMaterial.MaterialProperties Properties;
            internal readonly List<TextureChange> Values = new List<TextureChange>();
        }

        internal static bool TryApply(Transform firstModel, IReadOnlyDictionary<IntPtr, GameObject> copies)
        {
            var changes = new List<Change>();
            bool started = false;
            try
            {
                var layout = _layout ??= RitualBloodRouteData.ReadLayout();
                // Prepare every texture and validate clone ownership before changing any view.
                foreach (string name in new[] { "bloodFlowDecal_1", "bloodFlowDecal_2", "bloodFlowDecal_2 (1)" })
                {
                    var source = firstModel.Find(name);
                    var copy = copies[source.gameObject.Pointer];
                    var original = source.GetComponent<CustomizedMaterial>();
                    var owner = copy.GetComponent<CustomizedMaterial>();
                    if (owner == null || original == null || owner.Pointer == original.Pointer ||
                        owner.MaterialsProperties?.Length != 1 || original.MaterialsProperties?.Length != 1)
                        throw new InvalidOperationException("Unexpected blood customized material");
                    var props = owner.MaterialsProperties[0];
                    var originalProps = original.MaterialsProperties[0];
                    if (props == null || props.Pointer == originalProps?.Pointer)
                        throw new InvalidOperationException("Blood property data was not independently cloned");
                    var change = new Change { Transform = copy.transform, Position = copy.transform.localPosition,
                        Rotation = copy.transform.localRotation, Scale = copy.transform.localScale, Owner = owner, Properties = props };
                    int part = name == "bloodFlowDecal_1" ? 1 : 2;
                    foreach (var map in layout.Textures)
                    {
                        if (map.Part != part) continue;
                        var property = Find(props, map.Property);
                        var originalProperty = Find(originalProps, map.Property);
                        if (property == null || property.Pointer == originalProperty?.Pointer)
                            throw new InvalidOperationException("Blood texture property missing or shared: " + map.Property);
                        var template = source.GetComponent<Renderer>()?.sharedMaterial?.GetTexture(map.Property);
                        if (template == null) throw new InvalidOperationException("Native blood map missing: " + map.Property);
                        var texture = GetTexture(map);
                        change.Values.Add(new TextureChange { Property = property, Value = property.Value,
                            Enabled = property.Enabled, WasEnabled = property.m_wasEnabled,
                            Atlas = property.AtlasValue, AtlasUvs = property.AtlasUvs, NewValue = texture });
                    }
                    if (change.Values.Count != 3) throw new InvalidOperationException("Blood map set incomplete");
                    changes.Add(change);
                }
                started = true;
                foreach (var change in changes)
                {
                    change.Transform.localPosition = new Vector3(layout.Position[0], layout.Position[1], layout.Position[2]);
                    change.Transform.localRotation = new Quaternion(layout.Rotation[0], layout.Rotation[1], layout.Rotation[2], layout.Rotation[3]);
                    change.Transform.localScale = new Vector3(layout.Scale[0], layout.Scale[1], layout.Scale[2]);
                    foreach (var value in change.Values)
                    {
                        change.Properties.SetTexture(change.Owner, value.Property.PropertyName, value.NewValue);
                        value.Property.ForceEnableProperty();
                        if (change.Properties.GetTextureIfEnabled(value.Property.PropertyName)?.Pointer != value.NewValue.Pointer)
                            throw new InvalidOperationException("Blood map binding readback failed");
                    }
                    change.Owner.SyncProperties();
                    // A cloned property may have acquired a first-bowl atlas binding.
                    // Use the private generated texture directly on this clone. A later
                    // native atlas rebuild may legitimately pack the NEW texture.
                    foreach (var value in change.Values) value.Property.AtlasValue = null;
                    change.Owner.UpdateMaterialsPropertyBlock();
                    var block = new MaterialPropertyBlock();
                    change.Owner.m_Renderer.GetPropertyBlock(block, 0);
                    foreach (var value in change.Values)
                    {
                        if (block.GetTexture(Shader.PropertyToID(value.Property.PropertyName))?.Pointer != value.NewValue.Pointer)
                            throw new InvalidOperationException("Renderer property block retained a different blood texture");
                        var atlas = block.GetVector(Shader.PropertyToID(value.Property.PropertyName + "AtlasST"));
                        if (atlas.x != 0 || atlas.y != 0 || atlas.z != 1 || atlas.w != 1)
                            throw new InvalidOperationException("Renderer retained first-bowl atlas UV coordinates");
                    }
                }
                TrialRepairLog.Write("[ritual-blood] APPLIED second-floor engraving projection; decals=3; maps=6; rendererTextureReadback=True; atlasUvReadback=True; native flow timing retained; visual acceptance pending");
                return true;
            }
            catch (Exception error)
            {
                // An auxiliary visual failure must not remove the working hand-cut timeline.
                // Roll back only clone-local transforms and property overrides.
                int restoreErrors = 0;
                if (started)
                    foreach (var change in changes)
                    {
                        var restoreError = RitualBloodRouteData.RestoreOrHide(() =>
                        {
                            change.Transform.localPosition = change.Position;
                            change.Transform.localRotation = change.Rotation;
                            change.Transform.localScale = change.Scale;
                            foreach (var value in change.Values)
                            {
                                value.Property.Value = value.Value; value.Property.Enabled = value.Enabled;
                                value.Property.m_wasEnabled = value.WasEnabled;
                                value.Property.AtlasValue = value.Atlas; value.Property.AtlasUvs = value.AtlasUvs;
                            }
                            change.Owner.SyncProperties();
                            change.Owner.UpdateMaterialsPropertyBlock();
                        }, () => change.Transform.gameObject.SetActive(false));
                        if (restoreError != null)
                        { ++restoreErrors; TrialRepairLog.Write("[ritual-blood] rollback ERROR; isolated blood clone: " + restoreError); }
                    }
                TrialRepairLog.Write("[ritual-blood] ERROR; keeping hand-cut timeline; restoreErrors=" + restoreErrors + "; " + error);
                return false;
            }
        }

        private static CustomizedMaterial.MaterialPropertyTexture Find(CustomizedMaterial.MaterialProperties props, string name)
        {
            if (props?.Textures == null) return null;
            foreach (var property in props.Textures)
                if (property != null && property.PropertyName == name) return property;
            return null;
        }

        private static Texture2D GetTexture(RitualBloodRouteData.Map map)
        {
            // The generated isDataSRGB getter reaches a stripped internal method.
            // Preserve m_ColorSpace from the exact original Texture2D assets, read
            // offline and validated with the embedded layout instead of invoking it.
            bool srgb = map.SourceColorSpace == 1;
            string key = map.Resource + "/" + srgb;
            if (Textures.TryGetValue(key, out var cached) && cached != null) return cached;
            byte[] pixels = RitualBloodRouteData.ReadPixels(map);
            Texture2D texture = null;
            try
            {
                texture = new Texture2D(map.Size, map.Size, TextureFormat.RGBA32, false, !srgb);
                var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
                try { texture.LoadRawTextureData(handle.AddrOfPinnedObject(), pixels.Length); }
                finally { handle.Free(); }
                texture.Apply(false, true);
                texture.name = "NRFW_SecondBowl_" + map.Resource;
                texture.filterMode = FilterMode.Bilinear;
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
                Textures[key] = texture;
                return texture;
            }
            catch { if (texture != null) UObject.Destroy(texture); throw; }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMoon.Forsaken;
using Il2CppQuantum;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace Necromancy
{
    public sealed partial class NecromancyMod
    {
        private static RuntimeAssetResolutionContext _bossViewContext;
        private static BossSession _bossViewSession;
        private static readonly Dictionary<long, BossView> BossViews = new();
        private static readonly HashSet<long> BossViewFailures = new();
        private static bool _bossViewWaitLogged;

        private sealed class BossView
        {
            internal BossRune Rune;
            internal HeroRuneDataAsset Item;
            internal RaiseMinionActionDataAsset Action;
            internal bool ItemListed, ActionListed;
        }

        private static void BeforeBossResourceManagerReset()
        {
            try { RetireBossViews(Il2CppMoon.Forsaken.Core.AssetCtx?.Pointer ?? IntPtr.Zero); }
            catch (Exception e) { BossError("VIEW reset", e); }
        }

        private static void RetireBossViews(IntPtr currentResolver)
        {
            foreach (var view in BossViews.Values) view.Rune.ViewReady = false;
            if (_bossViewContext != null && _bossViewContext.Pointer == currentResolver)
            {
                foreach (var view in BossViews.Values)
                {
                    try { RemoveBossView(_bossViewContext, view); }
                    catch (Exception e) { BossError("VIEW remove", e); }
                }
            }
            BossViews.Clear();
            BossViewFailures.Clear();
            _bossViewContext = null;
            _bossViewSession = null;
            _bossViewWaitLogged = false;
        }

        private static void PumpBossViews()
        {
            try
            {
                var session = _bossSession;
                if (session == null && _bossViewSession == null) return;
                var resolver = Il2CppMoon.Forsaken.Core.AssetCtx;
                var manager = Il2CppMoon.Forsaken.Core.ResourceManager;
                if (_bossViewSession != null && (session != _bossViewSession || resolver?.Pointer != _bossViewContext?.Pointer ||
                    manager?.Pointer != _bossViewSession.Manager))
                    RetireBossViews(resolver?.Pointer ?? IntPtr.Zero);
                if (session == null || session.Runes.Count == 0) return;
                var ctx = manager?.Pointer == session.Manager ? _bossViewContext ?? resolver?.TryCast<RuntimeAssetResolutionContext>() : null;
                if (ctx == null || ctx.m_assetByGuid == null || ctx.m_loadedWrappers == null || ctx.m_allAssets == null)
                {
                    if (!session.ViewContextWaitLogged)
                    {
                        session.ViewContextWaitLogged = true;
                        Write("boss-runes VIEW waiting for runtime resolver; frame manager=" + session.Manager + "; view manager=" + manager?.Pointer);
                    }
                    return;
                }
                _bossViewSession = session;
                _bossViewContext = ctx;
                if (BossViews.Count + BossViewFailures.Count == session.Runes.Count) return;

                if (!ctx.TryFindWrapperImpl(BossGuid(NecromancyPolicy.RaiseMinionRune), out var itemBase, true) ||
                    !ctx.TryFindWrapperImpl(BossGuid(RaiseMinionActionGuid), out var actionBase, true))
                {
                    if (!_bossViewWaitLogged) { _bossViewWaitLogged = true; Write("boss-runes VIEW waiting for shipped raiseMinion wrappers"); }
                    return;
                }
                var item = itemBase?.TryCast<HeroRuneDataAsset>();
                var action = actionBase?.TryCast<RaiseMinionActionDataAsset>();
                foreach (var rune in session.Runes.Values)
                {
                    long id = rune.Rule.ItemGuid;
                    if (BossViews.ContainsKey(id) || BossViewFailures.Contains(id)) continue;
                    if (_bossSession != session || Il2CppMoon.Forsaken.Core.AssetCtx?.Pointer != ctx.Pointer) return;
                    var view = new BossView { Rune = rune };
                    try
                    {
                        RegisterBossView(ctx, view, item, action);
                        BossViews.Add(id, view);
                        rune.ViewReady = true;
                        Write(BossLabel(rune.Rule) + " VIEW READY item/action TryFindWrapperImpl verified");
                    }
                    catch (Exception e)
                    {
                        BossViewFailures.Add(id);
                        Write(BossLabel(rune.Rule) + " VIEW NOT READY: " + e.GetType().Name + ": " + e.Message);
                        try { RemoveBossView(ctx, view); }
                        catch (Exception cleanup) { BossError("VIEW rollback", cleanup); }
                    }
                }
            }
            catch (Exception e) { BossError("VIEW pump", e); }
        }

        private static void RegisterBossView(RuntimeAssetResolutionContext ctx, BossView view, HeroRuneDataAsset item, RaiseMinionActionDataAsset action)
        {
            if (item == null || item.AssetGuid.Value != NecromancyPolicy.RaiseMinionRune || action == null || action.AssetGuid.Value != RaiseMinionActionGuid)
                throw new InvalidOperationException("shipped wrapper type/GUID mismatch");
            var rune = view.Rune;
            var i = BossGuid(rune.Rule.ItemGuid);
            var a = BossGuid(rune.Rule.ActionGuid);
            foreach (var guid in new[] { i, a })
                if (ctx.m_assetByGuid.ContainsKey(guid) || ctx.m_loadedWrappers.ContainsKey(guid) || ctx.m_wrappersBeingLoaded?.ContainsKey(guid) == true)
                    throw new InvalidOperationException("resolver GUID collision " + BossId(guid.Value));
            view.Item = UnityObject.Instantiate(item);
            view.Action = UnityObject.Instantiate(action);
            if (view.Item == null || view.Action == null) throw new InvalidOperationException("wrapper clone failed");
            view.Item.AssetGuid = i;
            view.Action.AssetGuid = a;
            view.Item.hideFlags = HideFlags.DontSave;
            view.Action.hideFlags = HideFlags.DontSave;
            ApplyBossIcon(view);
            ctx.m_assetByGuid.Add(i, rune.Item);
            ctx.m_assetByGuid.Add(a, rune.Action);
            ctx.m_loadedWrappers.Add(i, view.Item);
            ctx.m_loadedWrappers.Add(a, view.Action);
            ctx.m_allAssets.Add(rune.Item);
            view.ItemListed = true;
            ctx.m_allAssets.Add(rune.Action);
            view.ActionListed = true;
            VerifyBossWrapper(ctx, i, view.Item, rune.Item);
            VerifyBossWrapper(ctx, a, view.Action, rune.Action);
        }

        private static void VerifyBossWrapper(RuntimeAssetResolutionContext ctx, AssetGuid guid, AssetBase wrapper, AssetObject asset)
        {
            if (!ctx.TryFindWrapperImpl(guid, out var found, false) || found?.Pointer != wrapper.Pointer || found.AssetGuid.Value != guid.Value ||
                !ctx.m_assetByGuid.TryGetValue(guid, out var data) || data?.Pointer != asset.Pointer || found.AssetObject?.Pointer != asset.Pointer)
                throw new InvalidOperationException("wrapper readback failed " + BossId(guid.Value));
        }

        private static void RemoveBossView(RuntimeAssetResolutionContext ctx, BossView view)
        {
            RemoveBossWrapper(ctx, view.Rune.Item, view.Item, view.ItemListed);
            RemoveBossWrapper(ctx, view.Rune.Action, view.Action, view.ActionListed);
        }

        private static void RemoveBossWrapper(RuntimeAssetResolutionContext ctx, AssetObject data, AssetBase wrapper, bool listed)
        {
            if (wrapper == null) return;
            var guid = data.Guid;
            if (ctx.m_loadedWrappers.TryGetValue(guid, out var old) && old?.Pointer == wrapper.Pointer) ctx.m_loadedWrappers.Remove(guid);
            if (ctx.m_assetByGuid.TryGetValue(guid, out var asset) && asset?.Pointer == data.Pointer) ctx.m_assetByGuid.Remove(guid);
            if (listed) ctx.m_allAssets.Remove(data);
            UnityObject.Destroy(wrapper);
        }

#if BOSS_RUNE_PNG
        private static readonly Dictionary<long, (Texture2D Texture, Sprite Sprite)> BossIcons = new();
#endif
        private static void ApplyBossIcon(BossView view)
        {
#if BOSS_RUNE_PNG
            var rule = view.Rune.Rule;
            if (rule.Icon.Length == 0) return;
            if (!BossIcons.TryGetValue(rule.ItemGuid, out var image))
            {
                Texture2D texture = null;
                Sprite sprite = null;
                try
                {
                    string path = Path.Combine(_bossIconDirectory, rule.Icon);
                    if (!File.Exists(path)) throw new FileNotFoundException("PNG not found: " + rule.Icon);
                    var bytes = File.ReadAllBytes(path);
                    texture = new Texture2D(2, 2) { hideFlags = HideFlags.DontSave };
                    if (!ImageConversion.LoadImage(texture, new Il2CppStructArray<byte>(bytes), false))
                        throw new InvalidOperationException("PNG decode failed");
                    sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100);
                    if (sprite == null) throw new InvalidOperationException("Sprite.Create returned null");
                    sprite.hideFlags = HideFlags.DontSave;
                    Write(BossLabel(rule) + " ICON loaded " + rule.Icon + " " + texture.width + "x" + texture.height);
                }
                catch (Exception e)
                {
                    Write(BossLabel(rule) + " ICON kept original: " + e.Message);
                    if (sprite != null) UnityObject.Destroy(sprite);
                    if (texture != null) UnityObject.Destroy(texture);
                    texture = null;
                    sprite = null;
                }
                BossIcons.Add(rule.ItemGuid, image = (texture, sprite));
            }
            if (image.Sprite == null) return;
            view.Item.ItemIcon = image.Sprite;
            view.Item.ItemTexture = image.Sprite;
            view.Action.Icon = image.Sprite;
#endif
        }

        private static BossRuneRule BossTextRule(HeroItemDataAsset wrapper) => wrapper != null &&
            BossViews.TryGetValue(wrapper.AssetGuid.Value, out var view) && view.Item.Pointer == wrapper.Pointer ? view.Rune.Rule : null;

        private static void AfterBossName(HeroItemDataAsset __instance, ref string __result)
        {
            try
            {
                var rule = BossTextRule(__instance);
                if (rule != null) __result = _bossEnglish ? rule.NameEn : rule.NameZh;
            }
            catch (Exception e) { BossError("GetName", e); }
        }

        private static void AfterBossDescription(HeroItemDataAsset __instance, ref string __result)
        {
            try
            {
                var rule = BossTextRule(__instance);
                if (rule != null) __result = _bossEnglish ? rule.DescEn : rule.DescZh;
            }
            catch (Exception e) { BossError("GetDescription", e); }
        }

        private static void AfterBossActionView(ActionData __0, ActionView __result)
        {
            try
            {
                if (__0 == null) return;
                var rune = BossAction(_bossSession, __0.Guid.Value);
                if (rune != null && Interlocked.Increment(ref rune.ActionViewLogs) <= 3)
                    Write(BossLabel(rune.Rule) + " GetActionView A=" + BossId(rune.Rule.ActionGuid) + " null=" + (__result == null));
            }
            catch (Exception e) { BossError("GetActionView", e); }
        }
    }
}

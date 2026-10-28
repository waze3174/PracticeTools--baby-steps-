using HarmonyLib;
using Il2Cpp;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace PracticeTools;

// the infinite load: a prop's asset can stay loaded after the area loader let go of it (a dead load's handle can't
// be released, an unload that throws). the loader's next load of that prop asks its AssetReference again, which
// refuses ("Attempting to load AssetReference that has already been loaded"), hands back an empty handle, and the
// load dies "loading" for good, restart or not. a teleport waits for it forever, and nate can fall through the world
// where it never came. such a load takes the asset that's already there, and loads that already died that way are
// started again. the asset is taken over without a hold of its own: nobody else holds it any more, so the loader's
// own unload frees it and its AssetReference is clean again (with an extra hold it stayed loaded, and the next load
// died the same way)
static class LoadFix
{
    static float scanAt;
    static int logged;

    // patched on its own (Core), so a failure here leaves the other patches alone
    internal static void Install(HarmonyLib.Harmony h)
    {
        h.Patch(AccessTools.Method(typeof(BestRegionLoader), nameof(BestRegionLoader.LoadProp)), prefix: new HarmonyMethod(typeof(LoadFix), nameof(PropPrefix)));
        h.Patch(AccessTools.Method(typeof(BestRegionLoader), nameof(BestRegionLoader.LoadPropLOD)), prefix: new HarmonyMethod(typeof(LoadFix), nameof(LodPrefix)));
    }

    static bool PropPrefix(BestRegionLoader __instance, int i) => !Adopt(__instance, i, false);
    static bool LodPrefix(BestRegionLoader __instance, int i) => !Adopt(__instance, i, true);

    // true when the asset was already loaded and is now the loader's (the game's own load is skipped)
    static bool Adopt(BestRegionLoader rl, int i, bool lod)
    {
        try
        {
            var refs = lod ? rl.propLODs : rl.propRefs;
            var r = refs != null && i < refs.Length ? refs[i] : null;
            if (r == null || !r.IsValid()) return false;
            var h = r.OperationHandle;
            if (!h.IsValid() || !h.IsDone || h.Result == null) return false;
            if (lod)
            {
                rl.propLODLoadHandles[i] = h;
                rl.loadedLODs[i] = h.Result.TryCast<GabeLOD>();
                SetState(rl, 0x1D0, i, 1);
                rl.lodNeedsUpdating[i] = true;   // the loader's next update marks it loaded
            }
            else
            {
                rl.propLoadHandles[i] = h;
                rl.loadedProps[i] = h.Result.TryCast<GameObject>();
                SetState(rl, 0x1E0, i, 1);
                rl.propLoadNeedsUpdating[i] = true;
            }
            if (logged++ < 20) DebugLog.Write($"area loader: prop {(lod ? "visual" : "prefab")} {i} was still loaded from before: taken as is");
            return true;
        }
        catch (System.Exception e)
        {
            Core.Inst.LoggerInstance.Warning($"area loader: prop {i}: {e.Message}");
            return false;
        }
    }

    // once a second: loads that died that way (still "loading", empty handle, asset loaded) go back to "not loaded" so
    // the loader starts them again, through Adopt
    internal static void Update()
    {
        if (Time.unscaledTime < scanAt) return;
        scanAt = Time.unscaledTime + 1f;
        var rl = BestRegionLoader.me;
        if (rl == null) return;
        int props = Restart(rl, false), visuals = Restart(rl, true);
        if (props + visuals > 0) DebugLog.Write($"area loader: {props} prop prefabs and {visuals} prop visuals had died loading: started again");
    }

    static int Restart(BestRegionLoader rl, bool lod)
    {
        int offset = lod ? 0x1D0 : 0x1E0, k = 0;
        if (!Fly.Native(rl, offset, out var st, out int len)) return 0;
        try
        {
            var refs = lod ? rl.propLODs : rl.propRefs;
            var handles = lod ? rl.propLODLoadHandles : rl.propLoadHandles;
            if (refs == null || handles == null) return 0;
            int n = Mathf.Min(len, Mathf.Min(refs.Length, handles.Length));
            for (int i = 0; i < n; i++)
            {
                if (System.Runtime.InteropServices.Marshal.ReadInt32(st, i * 4) != 1) continue;
                var r = refs[i];
                if (r == null || !r.IsValid() || handles[i].IsValid()) continue;   // a load under way, or one not tried yet
                System.Runtime.InteropServices.Marshal.WriteInt32(st, i * 4, 0);
                k++;
            }
        }
        catch (System.Exception e) { Core.Inst.LoggerInstance.Warning("area loader: dead loads: " + e.Message); }
        return k;
    }

    static void SetState(BestRegionLoader rl, int offset, int i, int v)
    {
        if (Fly.Native(rl, offset, out var st, out int len) && i < len) System.Runtime.InteropServices.Marshal.WriteInt32(st, i * 4, v);
    }
}

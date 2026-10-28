using System.Collections.Generic;
using Il2Cpp;
using UnityEngine;

namespace PracticeTools;

// the pedometer's blinking light on every item nate has picked up: the game's own PedometerBlinker blinks once an
// item has been touched and isn't held, so a dropped item can be found again. its light and material come from the
// pedometer's prefab (loaded through its GlobalObjectLoader). replaces BBSItemBlinker
static class Blinker
{
    const string PedometerLoader = "BigManagerPrefab/GlobalObjectParent/Savables/Pedometer_Loader";
    static Material mat;
    static GameObject quad;
    static float nextTry;
    static bool on;
    static readonly List<(Grabable item, PedometerBlinker blinker, GameObject light)> added = new();
    static readonly HashSet<int> done = new();   // items already looked at (instance ids)

    internal static void Update()
    {
        var pm = PlayerMovement.me;
        if (pm == null) return;
        if (Core.ItemBlinker.Value != on) Switch(Core.ItemBlinker.Value);
        if (!on) return;
        if (quad == null && !Load()) return;
        if (pm.handItems != null) foreach (var it in pm.handItems) Add(it);
        Add(pm.currentHat);
    }

    static bool Load()
    {
        if (Time.unscaledTime < nextTry) return false;
        nextTry = Time.unscaledTime + 1f;
        try
        {
            var loader = GameObject.Find(PedometerLoader)?.GetComponent<GlobalObjectLoader>();
            var asset = loader?.loadee;
            if (asset == null || !asset.RuntimeKeyIsValid()) return false;
            // by its key, not through the AssetReference: that one is the game's, and an AssetReference loads once.
            // asked again by the game it would refuse and the pedometer itself wouldn't load
            var prefab = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<GameObject>(asset.RuntimeKey).WaitForCompletion();
            var b = prefab != null ? prefab.GetComponentInChildren<PedometerBlinker>(true) : null;
            if (b == null || b.blinkerQuad == null || b.blinkerMat == null) { nextTry = float.MaxValue; Core.Inst.LoggerInstance.Warning("item blinker: no blinker on the pedometer"); return false; }
            mat = b.blinkerMat; quad = b.blinkerQuad;
            return true;
        }
        catch (System.Exception e) { nextTry = Time.unscaledTime + 10f; Core.Inst.LoggerInstance.Warning("item blinker: " + e.Message); return false; }
    }

    static void Add(Grabable g)
    {
        if (g == null || !done.Add(g.GetInstanceID())) return;
        if (g.GetComponentInChildren<PedometerBlinker>(true) != null) return;   // the pedometer
        // the pedometer's light and material as they are (the material is what makes it face the camera and blink),
        // at the item's middle, a bit smaller than the pedometer's
        var light = Object.Instantiate(quad, g.transform);
        light.name = "PracticeToolsBlinker";
        light.transform.localScale = new Vector3(0.4f, 0.4f, 0.4f);
        light.transform.localPosition = Vector3.zero;
        light.SetActive(false);
        var b = g.gameObject.AddComponent<PedometerBlinker>();
        b.blinkerMat = mat; b.blinkerQuad = light; b.grabable = g;
        b.enabled = true;
        added.Add((g, b, light));
    }

    // off: every light we added off (the components stay, switched off); on: back
    static void Switch(bool to)
    {
        on = to;
        added.RemoveAll(x => x.item == null || x.blinker == null);
        foreach (var (_, b, light) in added)
        {
            b.enabled = to;
            if (!to && light != null) light.SetActive(false);
            if (to) b.blinking = false;   // let it light up again on its own
        }
    }
}

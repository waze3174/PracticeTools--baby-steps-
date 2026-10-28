using System.Collections.Generic;
using Il2Cpp;
using UnityEngine;

namespace PracticeTools;

// the camera's yaw and pitch, for lining up glitch setups. F5 / B + R3 marks where the camera looks now, up to five
// marks (a sixth drops the oldest): each gets a numbered gold cross on screen, and the display says how far off each
// one it is. pressed twice quickly, it clears them all
static class CamAngle
{
    internal class Mark { public float Yaw, Pitch; public Vector3 Dir; }
    internal const int MaxMarks = 5;
    internal static readonly List<Mark> Marks = new();
    static float lastPress = -10f;

    internal static void Update()
    {
        Now(out _, out _);   // keeps the game camera's turn for when the game pauses
        if (!Binds.Pressed(Binds.MarkAngle)) return;
        if (Marks.Count > 0 && Time.unscaledTime - lastPress < 0.4f)
        {
            Marks.Clear(); lastPress = -10f;
            Hud.Flash("ANGLE MARKS CLEARED");
            return;
        }
        lastPress = Time.unscaledTime;
        if (!Now(out float yaw, out float pitch, out var dir)) return;
        if (Marks.Count >= MaxMarks) Marks.RemoveAt(0);
        Marks.Add(new Mark { Yaw = yaw, Pitch = pitch, Dir = dir });
        Hud.Flash(string.Format(System.Globalization.CultureInfo.InvariantCulture, "ANGLE {0} MARKED  YAW {1:0.00}  PITCH {2:0.00}", Marks.Count, yaw, pitch));
    }

    // yaw 0..360 clockwise from +z, pitch up is positive
    // paused, Camera.main is the pause screen's own camera far away: the game camera's last turn before the pause stands in
    static Quaternion playRot;
    static bool playSet;

    internal static bool Now(out float yaw, out float pitch) => Now(out yaw, out pitch, out _);

    internal static bool Now(out float yaw, out float pitch, out Vector3 dir)
    {
        yaw = pitch = 0f; dir = Vector3.forward;
        var cam = Camera.main;
        bool paused = Menu.me != null && Menu.me.paused;
        if (!paused && cam != null) { playRot = cam.transform.rotation; playSet = true; }
        if (paused ? !playSet : cam == null) return false;
        var rot = paused ? playRot : cam.transform.rotation;
        dir = rot * Vector3.forward;
        var e = rot.eulerAngles;
        yaw = e.y;
        pitch = -Mathf.DeltaAngle(0f, e.x);
        return true;
    }
}

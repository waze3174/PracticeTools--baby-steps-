using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace PracticeTools;

// camera angles for setting up the water glitch: a chain, in order. with "Glitch markers" on, the first shows (a
// coloured cross where the camera should look, its name and how far off it is, an arrow to the cross while it's off
// screen) once nate is within its range of its spot: range across, height up and down. each one the crosshair gets
// to brings the next up and drops the one before it, so two show at a time; a fling ends the chain (Hud.GlitchMarkers).
// the other markers' spots only say how far nate can wander before the chain is forgotten.
// kept in UserData/PracticeTools/glitch-markers.txt, one per line:
// name|r g b|x y z|range m|yaw|pitch|shoulder (left, right or any)|height m (optional, the range if left out)
static class GlitchMarks
{
    internal class Marker
    {
        public string Name; public Color Color; public Vector3 At; public float Range, Height, Yaw, Pitch;
        public int Shoulder = -1;   // -1 any, 0 right, 1 left (PlayerMovement.camRight)
        public Vector3 Dir => Quaternion.Euler(-Pitch, Yaw, 0f) * Vector3.forward;
    }

    internal const int Max = 8;
    internal static readonly List<Marker> List = new();
    static string File_ => Path.Combine(Core.Dir, "glitch-markers.txt");

    // in order along the setup, taken from my own runs (the last one's heading from a 12.7 km fling)
    static readonly string[] Defaults =
    {
        "hold this angle to unload the water (5 secs)|0.35 0.9 1|473.23 267.63 689.18|8|93.88|-31.75|right|8",
        "look up to load the water back in|0.4 1 0.45|472.36 156.76 689.90|12|131.83|40.00|right|100",
        "move here to keep water loaded as you rotate nate to head south, switch to left shoulder|1 0.85 0.3|469.33 146.44 693.37|12|312.08|40.00|any|100",
        "start swimming back left and move to the next angle (set fps to 30)|1 0.4 0.85|468.60 139.62 673.60|12|285.95|40.00|any|100",
        "hold left (moving SSW 192). prepare to let go briefly before exiting the water and hold left as you exit|1 0.5 0.25|470.21 140.01 652.76|12|283.97|38.85|left|100",
    };

    internal static void Load()
    {
        List.Clear();
        var c = CultureInfo.InvariantCulture;
        try
        {
            if (!File.Exists(File_))
            {
                var lines = new List<string>
                {
                    "# glitch markers: name|colour r g b (0 to 1)|nate's spot x y z|range m (across)|camera yaw|camera pitch|shoulder (left, right, any)|height m (up and down)",
                    "# a chain: the first shows near its spot, each one the crosshair gets to brings up the next (PracticeTools menu, Glitch tab)",
                };
                lines.AddRange(Defaults);
                File.WriteAllLines(File_, lines);
            }
            foreach (var line in File.ReadAllLines(File_))
            {
                if (List.Count >= Max) break;
                var f = line.Split('|');
                if (f.Length < 6 || line.StartsWith("#")) continue;
                var col = Nums(f[1]); var at = Nums(f[2]);
                if (col.Length < 3 || at.Length < 3) continue;
                var m = new Marker
                {
                    Name = f[0].Trim(), Color = new Color(col[0], col[1], col[2], 0.95f), At = new Vector3(at[0], at[1], at[2]),
                    Range = float.Parse(f[3], c), Yaw = float.Parse(f[4], c), Pitch = float.Parse(f[5], c),
                };
                if (f.Length > 6) m.Shoulder = f[6].Trim() == "left" ? 1 : f[6].Trim() == "right" ? 0 : -1;
                m.Height = f.Length > 7 && float.TryParse(f[7], NumberStyles.Float, c, out float h) ? h : m.Range;
                List.Add(m);
            }
        }
        catch (System.Exception e) { Core.Inst.LoggerInstance.Warning("glitch markers: " + e.Message); }
    }

    static float[] Nums(string s)
    {
        var parts = s.Trim().Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        var n = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++) n[i] = float.Parse(parts[i], CultureInfo.InvariantCulture);
        return n;
    }
}

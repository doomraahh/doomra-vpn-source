using System.Drawing;
namespace Luna {
 internal static class Palette {
  internal static bool Light;
  internal static Color Background { get { return Gray(Light?224:15); } }
  internal static Color Surface { get { return Gray(Light?240:26); } }
  internal static Color SurfaceBottom { get { return Gray(Light?234:22); } }
  internal static Color Input { get { return Gray(Light?224:15); } }
  internal static Color Text { get { return Gray(Light?28:238); } }
  internal static Color Muted { get { return Gray(Light?82:155); } }
  internal static Color Accent { get { return Gray(Light?38:218); } }
  internal static Color OnAccent { get { return Gray(Light?234:22); } }
  internal static Color Border { get { return Color.FromArgb(Light?44:35,Light?0:255,Light?0:255,Light?0:255); } }
  internal static Color Hover { get { return Gray(Light?218:37); } }
  internal static Color Selection { get { return Gray(Light?207:43); } }
  internal static Color Gray(int v) { return Color.FromArgb(v,v,v); }
 }
}

# Debug helper: lists the desktop window hierarchy (Progman / WorkerW / icon layer / wallpaper window); takes no screenshot
Add-Type @'
using System; using System.Text; using System.Runtime.InteropServices;
public static class DeskInspect {
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string c, string t);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
  [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
  static void Walk(IntPtr parent, int depth, StringBuilder sb) {
    for (IntPtr h = GetWindow(parent, 5); h != IntPtr.Zero; h = GetWindow(h, 2)) {
      var c = new StringBuilder(64); GetClassName(h, c, 64);
      var t = new StringBuilder(64); GetWindowText(h, t, 64);
      RECT r; GetWindowRect(h, out r);
      sb.Append(new string(' ', depth * 2) + c + " '" + t + "' visible=" + IsWindowVisible(h) + " rect=" + r.L + "," + r.T + "-" + r.R + "," + r.B + "\n");
      if (depth < 3) Walk(h, depth + 1, sb);
    }
  }
  public static string Dump() {
    SetProcessDPIAware();
    IntPtr prog = FindWindow("Progman", null);
    var sb = new StringBuilder("Progman (child windows in z-order, top to bottom)\n");
    Walk(prog, 1, sb);
    return sb.ToString();
  }
}
'@
[DeskInspect]::Dump()

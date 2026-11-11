// Terrarium · desktop host (forked from Deskland Preview, GPL-3.0)
//
// What it does:
//   1. Attaches a borderless window below the desktop icons (the wallpaper layer) and shows the current theme's page in WebView2 (e.g. prototype/flat.html?wallpaper=1)
//   0. Settings window (prototype/settings.html): pick a theme and tweak its parameters, applied live to the wallpaper and saved to settings.json; one-click sharing (the wallpaper page draws a pixelated picture of the desktop)
//   2. Every 400ms reads the real desktop icon positions (cross-process messages to the desktop ListView) and sends them to the page only when they change
//   3. Pauses the animation while the foreground window is maximized / fullscreen; the tray icon can pause, reload and exit (exiting restores the original wallpaper)
//   4. When Explorer rebuilds the wallpaper layer (e.g. on Win+D) our window is destroyed with it → re-attach automatically
//
// Compiled with the .NET Framework compiler that ships with Windows (C# 5 syntax); see build.ps1.

using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;

static class Program
{
    public static readonly string ExitEventName = "TerrariumHost_Exit" + InstallProfile.Suffix;
    public static readonly string MenuEventName = "TerrariumHost_Menu" + InstallProfile.Suffix;
    public static readonly string SettingsEventName = "TerrariumHost_Settings" + InstallProfile.Suffix;

    // Signal the running instance (exit / show menu / open settings)
    static void Signal(string name)
    {
        Native.AllowSetForegroundWindow(-1);            // lets the running instance bring its menu to the front
        EventWaitHandle ev;
        if (EventWaitHandle.TryOpenExisting(name, out ev)) ev.Set();
    }

    [STAThread]
    static void Main(string[] args)
    {
        // Terrarium.exe --exit : tell the running instance to exit cleanly (restores the original wallpaper)
        // Terrarium.exe --menu : make the running instance pop up its menu at the mouse position
        // Terrarium.exe --settings : make the running instance open the settings window
        if (args.Length > 0 && args[0] == "--exit") { Signal(ExitEventName); return; }
        if (args.Length > 0 && args[0] == "--menu") { Signal(MenuEventName); return; }
        if (args.Length > 0 && args[0] == "--settings") { Signal(SettingsEventName); return; }
        if (args.Length > 0 && args[0] == "--exit-installed") { Environment.ExitCode = InstallProfile.StopInstalled(); return; }
        bool fromAutostart = Array.IndexOf(args, "--autostart") >= 0;
        if (fromAutostart) AutoStartCheck.MarkLaunched();  // start-on-sign-in did run (whether or not an instance is already running)
        bool created;
        var mutex = new Mutex(true, "TerrariumHost_Singleton" + InstallProfile.Suffix, out created);
        if (!created)
        {
            if (!fromAutostart) Signal(SettingsEventName); // already running: launching the program again = open settings (an autostart launch just exits quietly)
            return;
        }
        // Work in physical pixels: desktop icon coordinates and the screen size are both physical pixels
        if (!Native.SetProcessDpiAwarenessContext(new IntPtr(-4))) Native.SetProcessDPIAware();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        // Log every unexpected error; never show a dialog or exit silently
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { Log.Write("Unhandled exception: " + e.Exception); };
        AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e) { Log.Write("Fatal exception: " + e.ExceptionObject); };
        string exeDir = AppDomain.CurrentDomain.BaseDirectory;
        string pageDir = Path.GetFullPath(Path.Combine(exeDir, @"..\..\prototype"));
        // Attach mode on 24H2+: by default a layered child window of Progman (flickers briefly when the system wallpaper changes);
        //   --defview: inside the icon layer, below the icon list (experimental: renders nothing in testing); --legacy: inside WorkerW (destroyed along with it when the wallpaper changes)
        string attach = "progman";
        bool autostart = false;                           // --autostart: launched at sign-in, don't open the settings window
        foreach (string a in args)
        {
            if (a == "--legacy") attach = "legacy";
            else if (a == "--defview") attach = "defview";
            else if (a == "--autostart") autostart = true;
            else if (!a.StartsWith("--")) pageDir = a;
        }
        WallpaperForm.AttachMode = attach;
        Application.Run(new HostContext(pageDir, !autostart));
        GC.KeepAlive(mutex);
    }
}

static class Log
{
    public static readonly string DataDir = InstallProfile.DataDir;
    public static readonly string FilePath = Path.Combine(DataDir, "host.log");

    public static void Write(string msg)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + msg + "\r\n");
        }
        catch { }
    }
}

// Detects whether start-on-sign-in was blocked (e.g. by a security product's startup manager: the startup entry is still there, but it isn't allowed to run at sign-in).
// Every --autostart launch records a timestamp. When the user opens the program manually with start-on-sign-in enabled, more than 3 minutes after signing in,
// and there is no autostart record since that sign-in → the launch at sign-in didn't run
static class AutoStartCheck
{
    static string LastPath { get { return Path.Combine(Log.DataDir, "autostart.last"); } }
    static string EnabledPath { get { return Path.Combine(Log.DataDir, "autostart.enabled"); } }

    static void Stamp(string path)
    {
        try { Directory.CreateDirectory(Log.DataDir); File.WriteAllText(path, DateTime.UtcNow.Ticks.ToString()); } catch { }
    }
    static DateTime ReadStamp(string path)
    {
        try { return new DateTime(long.Parse(File.ReadAllText(path).Trim()), DateTimeKind.Utc); } catch { return DateTime.MinValue; }
    }
    public static void MarkLaunched() { Stamp(LastPath); }
    public static void MarkEnabled() { Stamp(EnabledPath); }

    // Time of the current sign-in (UTC): LogonTime from WTSQuerySessionInformation(WTSSessionInfo)
    public static DateTime LogonTimeUtc()
    {
        IntPtr buf; int bytes;
        if (!Native.WTSQuerySessionInformation(IntPtr.Zero, -1, 24, out buf, out bytes)) return DateTime.MinValue;
        try { return bytes >= 216 ? DateTime.FromFileTimeUtc(Marshal.ReadInt64(buf, 200)) : DateTime.MinValue; }
        catch { return DateTime.MinValue; }
        finally { Native.WTSFreeMemory(buf); }
    }

    // Returns null when everything is fine, or "" when start-on-sign-in didn't run this session
    // (the settings page then shows the generic "your security software may have blocked it" hint).
    public static string Detect(bool autoStartOn)
    {
        if (!autoStartOn) return null;
        DateTime logon = LogonTimeUtc();
        if (logon == DateTime.MinValue || (DateTime.UtcNow - logon).TotalMinutes < 3) return null;   // just signed in: startup apps may not have run yet
        if (ReadStamp(EnabledPath) > logon) return null;                                           // start-on-sign-in was switched on after this sign-in
        if (ReadStamp(LastPath) > logon) return null;                                              // it did start this session
        Log.Write("Start on sign-in did not run this session (signed in at " + logon.ToLocalTime().ToString("yyyy-MM-dd HH:mm") + ")");
        return "";
    }
}

static class Json
{
    static readonly JavaScriptSerializer S = new JavaScriptSerializer();
    public static object Parse(string s) { return S.DeserializeObject(s); }
    public static string Stringify(object o) { return S.Serialize(o); }
    public static Dictionary<string, object> Obj(params object[] kv)
    {
        var d = new Dictionary<string, object>();
        for (int i = 0; i + 1 < kv.Length; i += 2) d[(string)kv[i]] = kv[i + 1];
        return d;
    }
}

// User settings: the current theme + per-theme parameters (only values the user changed are stored; the rest use the theme defaults)
// Stored in %LOCALAPPDATA%\Terrarium\settings.json
static class Settings
{
    static readonly string FilePath = Path.Combine(Log.DataDir, "settings.json");
    public static string Theme = "beach";
    public static string Lang = "auto";                // interface language setting: "auto" or one of I18n.Supported (English only)
    public static Dictionary<string, Dictionary<string, object>> Params = new Dictionary<string, Dictionary<string, object>>();

    public static void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var root = Json.Parse(File.ReadAllText(FilePath, Encoding.UTF8)) as Dictionary<string, object>;
            if (root == null) return;
            object v;
            if (root.TryGetValue("theme", out v) && v is string) Theme = (string)v;
            if (root.TryGetValue("lang", out v) && v is string) Lang = (string)v;
            if (root.TryGetValue("params", out v) && v is Dictionary<string, object>)
                foreach (var kv in (Dictionary<string, object>)v)
                {
                    var d = kv.Value as Dictionary<string, object>;
                    if (d != null) Params[kv.Key] = d;
                }
            Log.Write("Settings loaded: theme " + Theme);
        }
        catch (Exception ex) { Log.Write("Failed to read settings (using defaults): " + ex.Message); }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Log.DataDir);
            File.WriteAllText(FilePath, Json.Stringify(Json.Obj("version", 1, "theme", Theme, "lang", Lang, "params", Params)), new UTF8Encoding(false));
        }
        catch (Exception ex) { Log.Write("Failed to save settings: " + ex.Message); }
    }

    public static Dictionary<string, object> For(string theme)
    {
        Dictionary<string, object> d;
        if (!Params.TryGetValue(theme, out d)) Params[theme] = d = new Dictionary<string, object>();
        return d;
    }
}

// Interface text lives in prototype/i18n/en.json: "ui" for the settings page, "host" for the tray menu and message boxes.
// Terrarium ships in English only; the language plumbing is kept so another language can be added later.
static class I18n
{
    // Keep in sync with LANGS in settings.html.
    public static readonly string[] Supported = { "en" };
    public static string Code = "en", Sys = "en";
    static Dictionary<string, object> en;

    public static void Load(string pageDir, string setting)
    {
        Code = Sys = "en";
        en = Read(pageDir, "en");
    }

    static Dictionary<string, object> Read(string pageDir, string code)
    {
        try
        {
            var root = Json.Parse(File.ReadAllText(Path.Combine(Path.Combine(pageDir, "i18n"), code + ".json"), Encoding.UTF8)) as Dictionary<string, object>;
            object h;
            return root != null && root.TryGetValue("host", out h) ? h as Dictionary<string, object> : null;
        }
        catch (Exception ex) { Log.Write("Failed to read language file (" + code + "): " + ex.Message); return null; }
    }

    // Look up a string; {name} placeholders are replaced by the following key/value pairs.
    public static string T(string key, params string[] kv)
    {
        object v = null;
        bool found = en != null && en.TryGetValue(key, out v);
        string str = found ? (v as string ?? key) : key;
        for (int i = 0; i + 1 < kv.Length; i += 2) str = str.Replace("{" + kv[i] + "}", kv[i + 1]);
        return str;
    }
}

// Lifetime of the whole program: the tray icon stays resident; if the wallpaper window goes away, a new one is created
// Ways to open the menu: left / right click on the tray icon, the Ctrl+Alt+H hotkey (or --menu)
// Ways to open settings: launch the program (it starts first if not running), double-click the tray icon, menu → Settings... (or --settings)
class HostContext : ApplicationContext
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", RunName = "Terrarium";
    static readonly MethodInfo ShowTrayMenu =
        typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);

    readonly string pageDir;
    readonly NotifyIcon tray;
    readonly MenuItem pauseItem, autoStartItem, settingsItem, reloadItem, exitItem;
    readonly System.Windows.Forms.Timer watch;
    readonly EventWaitHandle exitEvent, menuEvent, settingsEvent;
    readonly HotkeyWindow hotkey = new HotkeyWindow();
    readonly Queue<DateTime> restarts = new Queue<DateTime>();
    WallpaperForm form, dead;
    SettingsForm settingsWin;
    DateTime nextStart = DateTime.MaxValue, saveDue = DateTime.MaxValue;
    bool manualPause, exiting;
    string autoStartSw;                                 // start-on-sign-in didn't run (checked only on a manual launch): "" when it was blocked, null when everything is fine
    string updateVersion, updateUrl, updateStatus = "", updateError = "";
    double updateProgress = -1;                         // -1 = no percentage to show for the current stage

    public HostContext(string pageDir, bool showSettings)
    {
        this.pageDir = pageDir;
        Log.Write("Starting, page directory: " + pageDir + (showSettings ? "" : " (launched at sign-in)"));
        if (showSettings) autoStartSw = AutoStartCheck.Detect(IsAutoStart());
        Settings.Load();
        I18n.Load(pageDir, Settings.Lang);
        exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ExitEventName);
        menuEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.MenuEventName);
        settingsEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.SettingsEventName);

        // Ctrl+Alt+H hotkey: pop up the menu at the mouse position
        bool hotkeyOk = hotkey.Register(Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, Keys.H);
        hotkey.Pressed += delegate { ShowMenu(); };
        Log.Write(hotkeyOk ? "Hotkey Ctrl+Alt+H registered" : "Failed to register hotkey Ctrl+Alt+H (it may be taken by another program)");

        pauseItem = new MenuItem(I18n.T("trayPause"), delegate
        {
            manualPause = !manualPause;
            pauseItem.Checked = manualPause;
            if (form != null) form.SetManualPause(manualPause);
        });
        autoStartItem = new MenuItem(I18n.T("trayAutostart"), delegate { SetAutoStart(!autoStartItem.Checked); });
        autoStartItem.Checked = IsAutoStart();
        if (autoStartItem.Checked && AutoStartValue() != "")
        {
            try
            {
                if (CreateAutoStartShortcut())
                {
                    DeleteRunAutoStart();
                    Log.Write("Start on sign-in migrated to the current user's Startup folder");
                }
                else if (AutoStartValue().IndexOf("--autostart") < 0)
                    using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))   // add the missing argument to the fallback entry
                        k.SetValue(RunName, "\"" + Application.ExecutablePath + "\" --autostart");
            }
            catch (Exception ex) { Log.Write("Failed to migrate start on sign-in, keeping the original entry: " + ex.Message); }
        }
        settingsItem = new MenuItem(I18n.T("traySettings"), delegate { OpenSettings(); });
        settingsItem.DefaultItem = true;                // bold: the main entry

        tray = new NotifyIcon();
        tray.Icon = AppIcon.Create();
        tray.Text = "Terrarium";
        tray.ContextMenu = new ContextMenu(new[] {
            settingsItem,
            pauseItem,
            reloadItem = new MenuItem(I18n.T("trayReload"), delegate { if (form != null) form.Reload(); }),
            autoStartItem,
            new MenuItem("-"),
            exitItem = new MenuItem(I18n.T("trayExit"), delegate { Exit(); }),
        });
        tray.MouseUp += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ShowMenu(); };
        tray.DoubleClick += delegate { OpenSettings(); };   // double-clicking the tray icon opens settings
        tray.Visible = true;
        ShowWelcomeOnce(hotkeyOk);

        watch = new System.Windows.Forms.Timer();
        watch.Interval = 500;
        watch.Tick += delegate { Watch(); };
        watch.Start();
        StartForm();
        if (showSettings) OpenSettings();                // the user launched the program: open settings right away
    }

    // Refresh the tray menu text (when the interface language setting changes)
    void ApplyTrayTexts()
    {
        settingsItem.Text = I18n.T("traySettings");
        pauseItem.Text = I18n.T("trayPause");
        reloadItem.Text = I18n.T("trayReload");
        autoStartItem.Text = I18n.T("trayAutostart");
        exitItem.Text = I18n.T("trayExit");
    }

    // ---------------------------------------------------------------- Settings window
    public void OpenSettings()
    {
        if (exiting) return;
        if (settingsWin == null || settingsWin.IsDisposed)
        {
            settingsWin = new SettingsForm(pageDir, this);
            settingsWin.FormClosed += delegate { settingsWin = null; };
            Log.Write("Opening settings window");
        }
        if (settingsWin.WindowState == FormWindowState.Minimized) settingsWin.WindowState = FormWindowState.Normal;
        settingsWin.Show();
        // When launched externally with SW_HIDE, WinForms may already consider the form Visible
        // while the native window is still hidden; Activate / another Show won't fix that, so restore it explicitly.
        // SW_RESTORE handles both hidden and minimized; SW_SHOW doesn't un-minimize a minimized window.
        Native.ShowWindow(settingsWin.Handle, Native.SW_RESTORE);
        settingsWin.Activate();
    }

    string SettingsMessage()
    {
        return Json.Stringify(Json.Obj("t", "settings", "v", Settings.For(Settings.Theme)));
    }

    string UpdateStateMessage()
    {
        return Json.Stringify(Json.Obj("t", "updateState", "version", updateVersion,
            "status", updateStatus, "progress", updateProgress, "error", updateError));
    }

    void PostUpdateState()
    {
        if (settingsWin != null) settingsWin.Post(UpdateStateMessage());
    }

    // Only for development or non-Store update sources; the Store build always uses StoreContext and never accepts a URL from the page.
    public void OfferUpdate(string version, string url)
    {
        Uri uri;
        if (string.IsNullOrWhiteSpace(version) || !Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Update info requires a version and a valid HTTPS URL");
        updateVersion = version.Trim();
        updateUrl = uri.AbsoluteUri;
        updateStatus = "available";
        updateProgress = -1;
        updateError = "";
        PostUpdateState();
    }

    void SaveSoon() { saveDue = DateTime.Now.AddMilliseconds(600); }

    bool ThemeAllowed(string id)
    {
        return Themes.Allowed(pageDir, id, delegate(string product) { return false; });
    }

    // Messages from the settings page: hello / set / reset / theme / autostart / lang / updateProgram
    public void OnSettingsMessage(string s)
    {
        var m = Json.Parse(s) as Dictionary<string, object>;
        if (m == null || settingsWin == null) return;
        object t;
        m.TryGetValue("t", out t);
        switch (t as string)
        {
            case "hello":
                Rectangle scr = Screen.PrimaryScreen.Bounds;
                settingsWin.Post(Json.Stringify(Json.Obj("t", "init", "theme", Settings.Theme, "params", Settings.Params,
                    "screen", Json.Obj("w", scr.Width, "h", scr.Height), "autostart", IsAutoStart(),
                    "autostartWarn", autoStartSw != null, "autostartSw", autoStartSw ?? "",
                    "lang", Settings.Lang, "langEff", I18n.Code, "langSys", I18n.Sys)));
                if (updateVersion != null) PostUpdateState();
                if (form != null && form.LastIcons != "") settingsWin.Post(form.LastIcons);
                break;
            case "set":
            {
                string theme = m["theme"] as string, key = m["key"] as string;
                if (theme == null || key == null) return;
                Settings.For(theme)[key] = m["value"];
                if (theme == Settings.Theme && form != null)
                    form.Post(Json.Stringify(Json.Obj("t", "settings", "v", Json.Obj(key, m["value"]))));
                SaveSoon();
                break;
            }
            case "reset":
            {
                string theme = m["theme"] as string;
                if (theme == null) return;
                Settings.Params.Remove(theme);
                if (theme == Settings.Theme && form != null) form.Post("{\"t\":\"reset\"}");
                SaveSoon();
                Log.Write("Reset to defaults: " + theme);
                break;
            }
            case "theme":
            {
                string id = m["id"] as string;
                if (id == null || !ThemeAllowed(id))
                {
                    settingsWin.Post(Json.Stringify(Json.Obj("t", "themeChanged", "id", Settings.Theme)));
                    Log.Write("Rejected switch to an unauthorized or unknown theme: " + (id ?? "null"));
                    return;
                }
                string page = Themes.PageFor(pageDir, id);
                if (page == null || id == Settings.Theme) return;
                Settings.Theme = id;
                SaveSoon();
                if (form != null) form.LoadPage(page);
                Log.Write("Switched theme: " + id);
                break;
            }
            case "autostartWarnSeen":
                autoStartSw = null;
                break;
            case "lang":                                    // interface language changed: save it, refresh the tray menu, tell the settings page which language is in effect
            {
                object v;
                if (!m.TryGetValue("v", out v) || !(v is string)) return;
                Settings.Lang = (string)v;
                SaveSoon();
                I18n.Load(pageDir, Settings.Lang);
                ApplyTrayTexts();
                settingsWin.Post(Json.Stringify(Json.Obj("t", "lang", "v", Settings.Lang, "eff", I18n.Code, "sys", I18n.Sys)));
                break;
            }
            case "share":
                if (form == null) { PostShareResult(false, null, I18n.T("errNotReady")); break; }
                form.RequestSnapshot(OnShareImage);
                break;
            case "shareVideo":
                if (form == null) { PostShareResult(false, null, I18n.T("errNotReady")); break; }
                form.RequestRecording(OnShareVideo);
                break;
            case "openShare":
                if (lastShare != null && File.Exists(lastShare)) Process.Start(lastShare);
                break;
            case "openWebsite":
                Process.Start("https://github.com/alexmercer500/Terrarium");
                break;
            case "revealShare":
                if (lastShare != null && File.Exists(lastShare)) Process.Start("explorer.exe", "/select,\"" + lastShare + "\"");
                break;
            case "updateProgram":
                if (updateUrl != null) Process.Start(updateUrl);
                break;
            case "autostart":
                SetAutoStart(m["v"] is bool && (bool)m["v"]);
                settingsWin.Post(Json.Stringify(Json.Obj("t", "autostart", "v", IsAutoStart())));
                break;
        }
    }

    // ---- One-click share: image drawn by the wallpaper page → saved to "Pictures\Terrarium", copied to the clipboard, thumbnail sent back to the settings page ----
    string lastShare;                                   // path of the latest share file (the settings page can only ask us to open this one)
    void OnShareImage(string thumb, string png, string err)
    {
        if (err != null || png == null) { PostShareResult(false, null, err ?? I18n.T("errNoImage")); Log.Write("Failed to create share image: " + err); return; }
        try
        {
            int comma = png.IndexOf(',');
            byte[] bytes = Convert.FromBase64String(comma >= 0 ? png.Substring(comma + 1) : png);
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Terrarium");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "Terrarium " + DateTime.Now.ToString("yyyy-MM-dd HHmmss") + ".png");
            File.WriteAllBytes(path, bytes);
            lastShare = path;
            try
            {
                using (var ms = new MemoryStream(bytes))
                using (var img = Image.FromStream(ms))
                {
                    var data = new DataObject();
                    data.SetImage(new Bitmap(img));                       // most programs (chat apps etc.) accept this
                    data.SetData("PNG", false, new MemoryStream(bytes));  // programs that understand PNG (browsers, Office) use this; it's sharper
                    Clipboard.SetDataObject(data, true);
                }
            }
            catch (Exception ex) { Log.Write("Failed to copy share image to clipboard: " + ex.Message); }
            Log.Write("Share image created: " + path);
            if (settingsWin != null)
                settingsWin.Post(Json.Stringify(Json.Obj("t", "shareResult", "ok", true, "thumb", thumb, "path", path)));
        }
        catch (Exception ex)
        {
            Log.Write("Failed to save share image: " + ex);
            PostShareResult(false, null, ex.Message);
        }
    }
    void PostShareResult(bool ok, string path, string msg)
    {
        if (settingsWin != null) settingsWin.Post(Json.Stringify(Json.Obj("t", "shareResult", "ok", ok, "path", path, "msg", msg)));
    }
    // Share video: saved as MP4 (WebM when MP4 isn't supported) and put on the clipboard as a file (paste it straight into a chat window to send it);
    // the settings page plays it from https://share.local/<file name>
    void OnShareVideo(string mime, string poster, string video, string err)
    {
        if (err != null || video == null) { PostShareResult(false, null, err ?? I18n.T("errNoVideo")); Log.Write("Failed to record share video: " + err); return; }
        try
        {
            int comma = video.IndexOf(',');
            byte[] bytes = Convert.FromBase64String(comma >= 0 ? video.Substring(comma + 1) : video);
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Terrarium");
            Directory.CreateDirectory(dir);
            string name = "Terrarium " + DateTime.Now.ToString("yyyy-MM-dd HHmmss") + (mime == "video/webm" ? ".webm" : ".mp4");
            string path = Path.Combine(dir, name);
            File.WriteAllBytes(path, bytes);
            lastShare = path;
            try
            {
                var files = new System.Collections.Specialized.StringCollection();
                files.Add(path);
                Clipboard.SetFileDropList(files);
            }
            catch (Exception ex) { Log.Write("Failed to copy share video to clipboard: " + ex.Message); }
            Log.Write("Share video recorded: " + path + " (" + (bytes.Length / 1024) + " KB)");
            if (settingsWin != null)
            {
                settingsWin.MapShareFolder(dir);
                settingsWin.Post(Json.Stringify(Json.Obj("t", "shareResult", "ok", true, "kind", "video", "thumb", poster, "path", path,
                    "url", "https://share.local/" + Uri.EscapeDataString(name))));
            }
        }
        catch (Exception ex)
        {
            Log.Write("Failed to save share video: " + ex);
            PostShareResult(false, null, ex.Message);
        }
    }

    // Pop up the tray menu at the mouse position (NotifyIcon's own popup logic handles the foreground window, so clicking elsewhere closes the menu properly)
    void ShowMenu()
    {
        if (exiting) return;
        autoStartItem.Checked = IsAutoStart();
        if (ShowTrayMenu != null) ShowTrayMenu.Invoke(tray, null);
    }

    // On first run, show a system notification telling the user where the menu is
    void ShowWelcomeOnce(bool hotkeyOk)
    {
        try
        {
            string flag = Path.Combine(Log.DataDir, "welcome.shown");
            if (File.Exists(flag)) return;
            Directory.CreateDirectory(Log.DataDir);
            tray.ShowBalloonTip(10000, I18n.T("welcomeTitle"),
                I18n.T("welcomeBody", "hotkey", hotkeyOk ? I18n.T("welcomeHotkey") : ""), ToolTipIcon.Info);
            File.WriteAllText(flag, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        }
        catch (Exception ex) { Log.Write("First-run tip failed: " + ex.Message); }
    }

    // Start on sign-in: prefer a shortcut in the current user's Startup folder; legacy Run entries are migrated at startup.
    // No scheduled task (that needs extra privileges); if creating the shortcut fails, the Run entry is kept as a fallback.
    static string AutoStartShortcutPath
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Terrarium.lnk"); }
    }

    static object OpenAutoStartShortcut()
    {
        object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        try
        {
            return shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod,
                null, shell, new object[] { AutoStartShortcutPath });
        }
        finally { Marshal.FinalReleaseComObject(shell); }
    }

    static bool AutoStartShortcutEnabled()
    {
        if (!File.Exists(AutoStartShortcutPath)) return false;
        object link = null;
        try
        {
            link = OpenAutoStartShortcut();
            string target = link.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty,
                null, link, null) as string;
            string arguments = link.GetType().InvokeMember("Arguments", BindingFlags.GetProperty,
                null, link, null) as string;
            return string.Equals(target, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase)
                && arguments != null && arguments.IndexOf("--autostart", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch { return false; }
        finally { if (link != null) Marshal.FinalReleaseComObject(link); }
    }

    static bool CreateAutoStartShortcut()
    {
        object link = null;
        try
        {
            link = OpenAutoStartShortcut();
            link.GetType().InvokeMember("TargetPath", BindingFlags.SetProperty,
                null, link, new object[] { Application.ExecutablePath });
            link.GetType().InvokeMember("Arguments", BindingFlags.SetProperty,
                null, link, new object[] { "--autostart" });
            link.GetType().InvokeMember("WorkingDirectory", BindingFlags.SetProperty,
                null, link, new object[] { Path.GetDirectoryName(Application.ExecutablePath) });
            link.GetType().InvokeMember("IconLocation", BindingFlags.SetProperty,
                null, link, new object[] { Application.ExecutablePath + ",0" });
            link.GetType().InvokeMember("Description", BindingFlags.SetProperty,
                null, link, new object[] { "Terrarium" });
            link.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
            return File.Exists(AutoStartShortcutPath);
        }
        catch (Exception ex) { Log.Write("Failed to create Startup folder shortcut: " + ex.Message); return false; }
        finally { if (link != null) Marshal.FinalReleaseComObject(link); }
    }

    static string AutoStartValue()
    {
        if (InstallProfile.Isolated) return "";
        using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
        {
            string v = k == null ? null : k.GetValue(RunName) as string;
            return v ?? "";
        }
    }

    static void DeleteRunAutoStart()
    {
        using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
            if (k != null) k.DeleteValue(RunName, false);
    }

    static bool IsAutoStart()
    {
        return AutoStartShortcutEnabled()
            || AutoStartValue().IndexOf(Application.ExecutablePath, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    void SetAutoStart(bool on)
    {
        if (InstallProfile.Isolated) return;
        try
        {
            if (on)
            {
                bool shortcut = CreateAutoStartShortcut();
                if (shortcut) DeleteRunAutoStart();
                else
                    using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
                        k.SetValue(RunName, "\"" + Application.ExecutablePath + "\" --autostart");
                AutoStartCheck.MarkEnabled();
                Log.Write(shortcut ? "Start on sign-in enabled (current user's Startup folder)" : "Start on sign-in enabled (legacy Run entry fallback)");
            }
            else
            {
                DeleteRunAutoStart();
                if (File.Exists(AutoStartShortcutPath)) File.Delete(AutoStartShortcutPath);
                Log.Write(IsAutoStart() ? "Failed to disable start on sign-in; the startup entry still exists" : "Start on sign-in disabled");
            }
        }
        catch (Exception ex) { Log.Write("Failed to change start on sign-in: " + ex.Message); }
        autoStartItem.Checked = IsAutoStart();
    }

    void StartForm()
    {
        nextStart = DateTime.MaxValue;
        if (!ThemeAllowed(Settings.Theme)) Settings.Theme = ThemeAllowed("beach") ? "beach" : "meadow";
        string page = ThemeAllowed(Settings.Theme) ? Themes.PageFor(pageDir, Settings.Theme) : null;
        if (page == null)
        {
            Log.Write("No authorized theme available to load, retrying in 5 seconds");
            nextStart = DateTime.Now.AddSeconds(5);
            return;
        }
        form = new WallpaperForm(pageDir, page, manualPause);
        form.SettingsJson = SettingsMessage;
        form.IconsChanged += delegate(string json) { if (settingsWin != null) settingsWin.Post(json); };
        form.RecordProgress += delegate(string p) { if (settingsWin != null) settingsWin.Post("{\"t\":\"recProgress\",\"p\":" + p + "}"); };
        form.HandleDestroyed += OnFormGone;
        form.Show();
    }

    void OnFormGone(object sender, EventArgs e)
    {
        var f = (WallpaperForm)sender;
        if (f != form) return;
        form = null;
        dead = f;                                        // disposed later from the timer, to avoid re-entrancy inside the destroy callback
        if (exiting) return;
        if (f.Fatal) { Exit(); return; }

        // Destroyed repeatedly within 1 minute: stop retrying rather than fight with Explorer
        restarts.Enqueue(DateTime.Now);
        while (restarts.Count > 0 && (DateTime.Now - restarts.Peek()).TotalSeconds > 60) restarts.Dequeue();
        if (restarts.Count > 5)
        {
            Log.Write("Wallpaper window destroyed more than 5 times within 1 minute, giving up");
            MessageBox.Show(I18n.T("errRebuilt") + "\n\n" + I18n.T("errSeeLog", "path", Log.FilePath), "Terrarium");
            Exit();
            return;
        }
        Log.Write("Wallpaper window destroyed (Explorer rebuilt the wallpaper layer), re-attaching in 1 second");
        nextStart = DateTime.Now.AddSeconds(1);
    }

    void Watch()
    {
        if (dead != null) { try { dead.Dispose(); } catch { } dead = null; }
        if (exitEvent.WaitOne(0)) { Exit(); return; }
        if (menuEvent.WaitOne(0)) ShowMenu();
        if (settingsEvent.WaitOne(0)) OpenSettings();
        if (DateTime.Now >= saveDue) { saveDue = DateTime.MaxValue; Settings.Save(); }
        if (form == null && !exiting && DateTime.Now >= nextStart) StartForm();
    }

    void Exit()
    {
        if (exiting) return;
        exiting = true;
        watch.Stop();
        if (saveDue != DateTime.MaxValue) Settings.Save();
        if (settingsWin != null) { try { settingsWin.Close(); } catch { } }
        hotkey.Dispose();
        tray.Visible = false;
        if (form != null) form.Shutdown();
        Log.Write("Exiting");
        ExitThread();
    }
}

// Hidden window that receives the global hotkey (message-only, never shown)
class HotkeyWindow : NativeWindow, IDisposable
{
    public event EventHandler Pressed;
    bool registered;

    public HotkeyWindow()
    {
        var cp = new CreateParams();
        cp.Parent = new IntPtr(-3);                     // HWND_MESSAGE: message-only window
        CreateHandle(cp);
    }

    public bool Register(uint modifiers, Keys key)
    {
        registered = Native.RegisterHotKey(Handle, 1, modifiers, (uint)key);
        return registered;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312 /* WM_HOTKEY */ && Pressed != null) Pressed(this, EventArgs.Empty);
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (registered) Native.UnregisterHotKey(Handle, 1);
        registered = false;
        if (Handle != IntPtr.Zero) DestroyHandle();
    }
}

// The tray and the settings window share the Terrarium icon; the build script copies the icon next to the executable.
static class AppIcon
{
    public static Icon Create()
    {
        return new Icon(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Terrarium.ico"));
    }

    static GraphicsPath RoundRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

// Settings window: a regular desktop window showing prototype/settings.html in WebView2
class SettingsForm : Form
{
    readonly WebView2 web;
    readonly HostContext host;
    readonly string pageDir;

    public SettingsForm(string pageDir, HostContext host)
    {
        this.pageDir = pageDir;
        this.host = host;
        Text = "Terrarium";
        AccessibleName = "Terrarium";
        ShowIcon = true;
        Icon = AppIcon.Create();
        StartPosition = FormStartPosition.CenterScreen;
        float s = Native.GetDpiForSystem() / 96f;       // the process works in physical pixels, so scale the window size ourselves
        ClientSize = new Size((int)(1120 * s), (int)(800 * s));     // left: themes in three columns; right: preview on top, parameters below
        MinimumSize = new Size((int)(940 * s), (int)(680 * s));
        BackColor = Color.White;

        web = new WebView2();
        web.Dock = DockStyle.Fill;
        web.DefaultBackgroundColor = BackColor;
        web.CreationProperties = new CoreWebView2CreationProperties();
        web.CreationProperties.UserDataFolder = Path.Combine(Log.DataDir, "WebView2");
        Controls.Add(web);
    }

    // Paint the native title bar the same color as the page: settings.html follows the system light/dark mode (light #ffffff / dark #171717),
    // so the title bar picks the same color for the same mode. DWMWA_CAPTION_COLOR / dark mode need Win11; on older systems the calls fail and the default look stays
    void ThemeTitleBar()
    {
        bool light = true;
        try
        {
            object v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
            light = v == null || Convert.ToInt32(v) != 0;
        }
        catch { }
        int dark = light ? 0 : 1;
        int color = light ? 0x00FFFFFF : 0x00171717;      // COLORREF is 0x00BBGGRR
        Native.DwmSetWindowAttribute(Handle, 20, ref dark, 4);    // DWMWA_USE_IMMERSIVE_DARK_MODE: title text and buttons follow light/dark
        Native.DwmSetWindowAttribute(Handle, 35, ref color, 4);   // DWMWA_CAPTION_COLOR
    }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); ThemeTitleBar(); }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x001A /* WM_SETTINGCHANGE */) ThemeTitleBar();   // update the title bar when the system switches light/dark
        base.WndProc(ref m);
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try
        {
            await web.EnsureCoreWebView2Async(null);
            if (IsDisposed) return;
            var core = web.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping("app.local", pageDir, CoreWebView2HostResourceAccessKind.Allow);
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.WebMessageReceived += delegate(object s, CoreWebView2WebMessageReceivedEventArgs a)
            {
                try { host.OnSettingsMessage(a.TryGetWebMessageAsString()); }
                catch (Exception ex) { Log.Write("Failed to handle settings message: " + ex.Message); }
            };
            string settingsUrl = "https://app.local/settings.html?v=" + Uri.EscapeDataString(Application.ProductVersion);
            Log.Write("Settings page: " + settingsUrl);
            core.Navigate(settingsUrl);
        }
        catch (Exception ex)
        {
            Log.Write("Settings window failed to start: " + ex);
            MessageBox.Show(I18n.T("errSettings", "msg", ex.Message), "Terrarium");
            Close();
        }
    }

    public void Post(string json)
    {
        // The settings window is transient: if its WebView2 is dead, just drop the message (closing and reopening recovers); don't throw back into the wallpaper's polling
        try { if (!IsDisposed && web.CoreWebView2 != null) web.CoreWebView2.PostWebMessageAsString(json); }
        catch { }
    }

    // Folder holding shared images / videos: mapped to https://share.local/ so the settings page can show and play them directly
    bool shareMapped;
    public void MapShareFolder(string dir)
    {
        if (shareMapped || IsDisposed || web.CoreWebView2 == null) return;
        web.CoreWebView2.SetVirtualHostNameToFolderMapping("share.local", dir, CoreWebView2HostResourceAccessKind.Allow);
        shareMapped = true;
    }
}

class WallpaperForm : Form
{
    readonly WebView2 web;
    readonly System.Windows.Forms.Timer iconTimer, fgTimer;
    readonly string pageDir;
    readonly IconReader reader = new IconReader();
    Rectangle screen;
    IntPtr parentHwnd, insertAfter;
    string lastIcons = "";
    int lastPause = -1;
    bool manualPause, shuttingDown;

    public bool Fatal { get; private set; }

    string page;                                        // current theme's page ("page" in themes.json)
    public Func<string> SettingsJson;                   // settings message sent to the page first once it is ready
    public event Action<string> IconsChanged;           // icon changes, forwarded to the settings window's preview
    public string LastIcons { get { return lastIcons; } }
    protected override bool ShowWithoutActivation { get { return true; } }

    public WallpaperForm(string pageDir, string page, bool manualPause)
    {
        this.pageDir = pageDir;
        this.page = page;
        this.manualPause = manualPause;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(26, 163, 196);
        Text = "TerrariumWindow";

        web = new WebView2();
        web.Dock = DockStyle.Fill;
        web.DefaultBackgroundColor = BackColor;
        web.CreationProperties = new CoreWebView2CreationProperties();
        web.CreationProperties.UserDataFolder = Path.Combine(Log.DataDir, "WebView2");
        Controls.Add(web);

        iconTimer = new System.Windows.Forms.Timer();
        iconTimer.Interval = 400;
        iconTimer.Tick += delegate { PollIcons(); };
        fgTimer = new System.Windows.Forms.Timer();
        fgTimer.Interval = 500;
        fgTimer.Tick += delegate { PollForeground(); };
    }

    public void SetManualPause(bool v) { manualPause = v; PollForeground(); }

    // ---- One-click share: ask the page to draw a pixelated picture of the desktop (the page takes no screenshot; it only uses the icon positions) ----
    Action<string, string, string> snapshotDone;           // (thumbnail, full PNG, error message)
    System.Windows.Forms.Timer snapshotTimeout;
    public void RequestSnapshot(Action<string, string, string> done)
    {
        if (web.CoreWebView2 == null) { done(null, null, I18n.T("errNotReady")); return; }
        if (snapshotDone != null) { done(null, null, I18n.T("errBusyImage")); return; }
        snapshotDone = done;
        snapshotTimeout = new System.Windows.Forms.Timer();
        snapshotTimeout.Interval = 20000;
        snapshotTimeout.Tick += delegate { FinishSnapshot(null, null, I18n.T("errImageTimeout")); };
        snapshotTimeout.Start();
        try { web.CoreWebView2.PostWebMessageAsString("{\"t\":\"snapshot\"}"); }
        catch (InvalidOperationException ex) { FinishSnapshot(null, null, I18n.T("errNotReady")); WebDead(ex); }
        catch (COMException ex) { FinishSnapshot(null, null, I18n.T("errNotReady")); WebDead(ex); }
    }
    void OnSnapshot(string msg)
    {
        string[] p = msg.Split(new[] { '|' }, 3);
        if (p.Length < 3) FinishSnapshot(null, null, I18n.T("errBadData"));
        else if (p[1] == "error") FinishSnapshot(null, null, p[2]);
        else FinishSnapshot(p[1], p[2], null);
    }
    void FinishSnapshot(string thumb, string png, string err)
    {
        if (snapshotTimeout != null) { snapshotTimeout.Stop(); snapshotTimeout.Dispose(); snapshotTimeout = null; }
        var done = snapshotDone;
        snapshotDone = null;
        if (done != null) done(thumb, png, err);
    }

    // ---- Share video: record the theme animation with the current icon layout, reporting progress along the way; when done the page sends "record|format|poster|full video" ----
    Action<string, string, string, string> recordDone;    // (format, poster, full video, error message)
    System.Windows.Forms.Timer recordTimeout;
    public event Action<string> RecordProgress;            // 0..1 (as a string)
    public void RequestRecording(Action<string, string, string, string> done)
    {
        if (web.CoreWebView2 == null) { done(null, null, null, I18n.T("errNotReady")); return; }
        if (recordDone != null) { done(null, null, null, I18n.T("errBusyVideo")); return; }
        recordDone = done;
        recordTimeout = new System.Windows.Forms.Timer();
        recordTimeout.Interval = 45000;
        recordTimeout.Tick += delegate { FinishRecord(null, null, null, I18n.T("errVideoTimeout")); };
        recordTimeout.Start();
        try { web.CoreWebView2.PostWebMessageAsString("{\"t\":\"record\"}"); }
        catch (InvalidOperationException ex) { FinishRecord(null, null, null, I18n.T("errNotReady")); WebDead(ex); }
        catch (COMException ex) { FinishRecord(null, null, null, I18n.T("errNotReady")); WebDead(ex); }
    }
    void OnRecord(string msg)
    {
        string[] p = msg.Split(new[] { '|' }, 4);
        if (p.Length >= 3 && p[1] == "error") FinishRecord(null, null, null, msg.Substring(13));
        else if (p.Length < 4) FinishRecord(null, null, null, I18n.T("errBadData"));
        else FinishRecord(p[1], p[2], p[3], null);
    }
    void FinishRecord(string mime, string poster, string video, string err)
    {
        if (recordTimeout != null) { recordTimeout.Stop(); recordTimeout.Dispose(); recordTimeout = null; }
        var done = recordDone;
        recordDone = null;
        if (done != null) done(mime, poster, video, err);
    }
    public void Reload()
    {
        if (web.CoreWebView2 == null) return;
        try { web.CoreWebView2.Reload(); }
        catch (InvalidOperationException ex) { WebDead(ex); }
        catch (COMException ex) { WebDead(ex); }
    }
    public void Post(string json)
    {
        if (web.CoreWebView2 == null) return;
        try { web.CoreWebView2.PostWebMessageAsString(json); }
        catch (InvalidOperationException ex) { WebDead(ex); }
        catch (COMException ex) { WebDead(ex); }
    }

    // After a background WebView2 runtime update or the browser process being killed, CoreWebView2 is not null but already disposed, and every call throws
    // (the null checks above don't catch this state). Handle it like "wallpaper window destroyed by the system": tear this window down and HostContext rebuilds it after 1 second
    void WebDead(Exception ex)
    {
        if (shuttingDown) return;
        Log.Write("WebView2 is dead (runtime update or process exit), re-attaching the wallpaper window: " + ex.Message);
        Shutdown();
    }
    string PageUrl { get { return "https://app.local/" + page + "?wallpaper=1"; } }

    // Switch theme: navigate to the new page; once it is ready it receives the settings and icons again
    public void LoadPage(string newPage)
    {
        page = newPage;
        if (web.CoreWebView2 == null) return;
        try { web.CoreWebView2.Navigate(PageUrl); }
        catch (InvalidOperationException ex) { WebDead(ex); }
        catch (COMException ex) { WebDead(ex); }
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try
        {
            if (!File.Exists(Path.Combine(pageDir, page)))
            {
                Fail(I18n.T("errNoPage", "path", Path.Combine(pageDir, page)));
                return;
            }
            if (!AttachToDesktop())
            {
                Fail(I18n.T("errNoDesktop"));
                return;
            }
            await web.EnsureCoreWebView2Async(null);
            if (shuttingDown || IsDisposed) return;
            var core = web.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping("app.local", pageDir, CoreWebView2HostResourceAccessKind.Allow);
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = true;
            core.Settings.IsStatusBarEnabled = false;
            // Page ready (sends "ready"): send the settings first, then icons and pause state (the shoreline depends on the offshore distance setting)
            // One-click share: once the page has drawn the share image it sends "snapshot|thumbnail|full PNG" (both data URLs; the full image is large, so it bypasses JSON)
            core.WebMessageReceived += delegate(object s, CoreWebView2WebMessageReceivedEventArgs a)
            {
                string msg = null;
                try { msg = a.TryGetWebMessageAsString(); } catch { }
                if (msg != null && msg.StartsWith("snapshot|")) { OnSnapshot(msg); return; }
                if (msg != null && msg.StartsWith("recprogress|")) { if (RecordProgress != null) RecordProgress(msg.Substring(12)); return; }
                if (msg != null && msg.StartsWith("record|")) { OnRecord(msg); return; }
                if (SettingsJson != null) core.PostWebMessageAsString(SettingsJson());
                lastIcons = ""; lastPause = -1; PollIcons(); PollForeground();
            };
            core.Navigate(PageUrl);
            iconTimer.Start();
            fgTimer.Start();
            Log.Write("WebView2 ready: " + core.Environment.BrowserVersionString);
        }
        catch (Exception ex)
        {
            if (shuttingDown || IsDisposed) return;       // window destroyed by the system during startup: let HostContext retry
            Log.Write("Startup failed: " + ex);
            Fail(I18n.T("errStart", "msg", ex.Message));
        }
    }

    void Fail(string msg)
    {
        Log.Write(msg);
        MessageBox.Show(msg + "\n\n" + I18n.T("errSeeLog", "path", Log.FilePath), "Terrarium");
        Fatal = true;
        Shutdown();
    }

    // ---------------------------------------------------------------- Attach to the wallpaper layer
    // Two desktop structures:
    //   · Windows 11 24H2 and later ("raised desktop"): Progman contains the icon layer (SHELLDLL_DefView) → the system wallpaper layer (WorkerW), in that order.
    //     The system wallpaper layer is destroyed and recreated when the wallpaper changes (slideshow / Spotlight), so we don't attach inside it; instead we become a
    //     layered child window of Progman (WS_EX_LAYERED, alpha 255), placed right below the icon layer and above the system wallpaper layer, and keep that order enforced.
    //     (Progman has no surface ordinary child windows can draw on, so it must be layered; layered child windows require app.manifest to declare Win8+ support)
    //   · Older versions: the icon layer lives in a top-level WorkerW, directly followed by the top-level WorkerW that draws the wallpaper → attach inside that WorkerW
    public static string AttachMode = "progman";
    bool raised;
    IntPtr zAnchor;                                     // the window we must stay directly below (icon list / icon layer)
    IntPtr winEventHook;
    Native.WinEventDelegate winEventProc;               // keep a reference so the GC doesn't collect it

    static IntPtr FindClassicWorker()
    {
        IntPtr host = IntPtr.Zero;
        Native.EnumWindows(delegate(IntPtr h, IntPtr l)
        {
            if (Native.FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero) { host = h; return false; }
            return true;
        }, IntPtr.Zero);
        return host != IntPtr.Zero ? Native.FindWindowEx(IntPtr.Zero, host, "WorkerW", null) : IntPtr.Zero;
    }

    // Ask Explorer to create the wallpaper layer (send 0xD,1 only once; sending 0xD,0 afterwards deletes the layer just created), waiting up to 2 seconds
    static void RequestWorkerW(IntPtr progman, Func<bool> ready)
    {
        IntPtr res;
        Native.SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), new IntPtr(1), 0, 1000, out res);
        for (int i = 0; i < 20 && !ready(); i++) Thread.Sleep(100);
    }

    bool AttachToDesktop()
    {
        screen = Screen.PrimaryScreen.Bounds;          // the process is DPI-aware → physical pixels
        IntPtr progman = Native.FindWindow("Progman", null);
        if (progman == IntPtr.Zero) { Log.Write("Progman not found"); return false; }
        IntPtr defView = Native.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        string mode;
        raised = defView != IntPtr.Zero;

        if (raised)
        {
            if (Native.FindWindowEx(progman, IntPtr.Zero, "WorkerW", null) == IntPtr.Zero)
                RequestWorkerW(progman, delegate { return Native.FindWindowEx(progman, IntPtr.Zero, "WorkerW", null) != IntPtr.Zero; });
            IntPtr listView = Native.FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
            if (AttachMode == "legacy")
            {
                parentHwnd = Native.FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
                if (parentHwnd == IntPtr.Zero) { Log.Write("WorkerW not found"); return false; }
                insertAfter = IntPtr.Zero; raised = false; mode = "24H2+ legacy: WorkerW child window";
            }
            else if (AttachMode == "defview" && listView != IntPtr.Zero)
            {
                // Experimental: inside the icon layer, below the icon list (not reordered on wallpaper changes, but renders nothing on 24H2 in testing)
                parentHwnd = defView; insertAfter = listView; zAnchor = listView;
                mode = "24H2+: layered child window of the icon layer (DefView), below the icon list";
            }
            else
            {
                parentHwnd = progman; insertAfter = defView; zAnchor = defView;
                mode = "24H2+: layered child window of Progman, below the icon layer";
            }
            if (raised)
            {
                int ex = Native.GetWindowLong(Handle, Native.GWL_EXSTYLE);
                Native.SetWindowLong(Handle, Native.GWL_EXSTYLE, ex | Native.WS_EX_LAYERED);
                Native.SetLayeredWindowAttributes(Handle, 0, 255, Native.LWA_ALPHA);
            }
        }
        else
        {
            IntPtr worker = FindClassicWorker();
            if (worker == IntPtr.Zero)
            {
                RequestWorkerW(progman, delegate { return FindClassicWorker() != IntPtr.Zero; });
                worker = FindClassicWorker();
            }
            if (worker == IntPtr.Zero) { Log.Write("WorkerW / SHELLDLL_DefView not found"); return false; }
            parentHwnd = worker; insertAfter = IntPtr.Zero; mode = "classic: top-level WorkerW child window";
        }

        int style = Native.GetWindowLong(Handle, Native.GWL_STYLE);
        style = (style & ~(Native.WS_POPUP | Native.WS_CAPTION | Native.WS_THICKFRAME)) | Native.WS_CHILD | Native.WS_VISIBLE;
        Native.SetWindowLong(Handle, Native.GWL_STYLE, style);
        Native.SetParent(Handle, parentHwnd);

        var pt = new Native.POINT { X = screen.X, Y = screen.Y };
        Native.ScreenToClient(parentHwnd, ref pt);
        Native.SetWindowPos(Handle, insertAfter, pt.X, pt.Y, screen.Width, screen.Height,
            Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW | Native.SWP_FRAMECHANGED);
        EnsureZOrder();

        if (raised)
        {
            // Fix the order as soon as Explorer creates / reorders the wallpaper layer (on a wallpaper change it inserts a new WorkerW above us)
            uint pid;
            Native.GetWindowThreadProcessId(progman, out pid);
            winEventProc = delegate(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
            {
                if (idObject == 0 && !shuttingDown && IsHandleCreated) EnsureZOrder();
            };
            winEventHook = Native.SetWinEventHook(0x8000 /* OBJECT_CREATE */, 0x8004 /* OBJECT_REORDER */, IntPtr.Zero,
                winEventProc, pid, 0, 0 /* WINEVENT_OUTOFCONTEXT */);
        }
        Log.Write(string.Format("Attached ({0}), screen {1}", mode, screen));
        return true;
    }

    // Keep the order: directly below zAnchor (icon list / icon layer); in Progman mode also push the system wallpaper layer to the bottom (only when the order is disturbed)
    int zFixes;
    void EnsureZOrder()
    {
        if (!raised || zAnchor == IntPtr.Zero || !Native.IsWindow(zAnchor)) return;
        bool fixedAny = false;
        if (Native.GetWindow(zAnchor, 2 /* GW_HWNDNEXT */) != Handle)
        {
            Native.SetWindowPos(Handle, zAnchor, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            fixedAny = true;
        }
        if (AttachMode == "progman")
        {
            for (IntPtr w = Native.FindWindowEx(parentHwnd, IntPtr.Zero, "WorkerW", null); w != IntPtr.Zero;
                 w = Native.FindWindowEx(parentHwnd, w, "WorkerW", null))
            {
                if (Native.GetWindow(w, 2) != IntPtr.Zero)   // not at the bottom → push it to the bottom
                {
                    Native.SetWindowPos(w, new IntPtr(1) /* HWND_BOTTOM */, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                    fixedAny = true;
                }
            }
        }
        if (fixedAny && ++zFixes <= 20) Log.Write("Fixed desktop z-order (Explorer reordered the windows)");
    }

    // ---------------------------------------------------------------- Read desktop icons
    void PollIcons()
    {
        EnsureZOrder();
        if (web.CoreWebView2 == null) return;
        bool visible;
        List<Native.RECT> rects = reader.Read(out visible);
        if (rects == null) return;
        var sb = new StringBuilder();
        sb.Append("{\"t\":\"icons\",\"vis\":").Append(visible ? "true" : "false").Append(",\"r\":[");
        for (int i = 0; i < rects.Count; i++)
        {
            Native.RECT r = rects[i];
            if (i > 0) sb.Append(',');
            sb.Append('[').Append(r.Left - screen.X).Append(',').Append(r.Top - screen.Y).Append(',')
              .Append(r.Right - screen.X).Append(',').Append(r.Bottom - screen.Y).Append(']');
        }
        sb.Append("]}");
        string json = sb.ToString();
        if (json == lastIcons) return;
        lastIcons = json;
        if (IconsChanged != null) IconsChanged(json);
        Post(json);
        Log.Write(string.Format("Icons changed: {0}, visible={1}", rects.Count, visible));
    }

    // ---------------------------------------------------------------- Pause while maximized / fullscreen
    void PollForeground()
    {
        if (web.CoreWebView2 == null) return;
        bool pause = manualPause;
        string pauseBy = "";
        IntPtr fg = Native.GetForegroundWindow();
        int cloaked = 0;
        if (!pause && fg != IntPtr.Zero && fg != Handle && Native.IsWindowVisible(fg)
            && (Native.DwmGetWindowAttribute(fg, 14 /* DWMWA_CLOAKED */, out cloaked, 4) != 0 || cloaked == 0))
        {
            var cls = new StringBuilder(64);
            Native.GetClassName(fg, cls, 64);
            string c = cls.ToString();
            if (c != "Progman" && c != "WorkerW" && c != "Shell_TrayWnd" && c != "Shell_SecondaryTrayWnd")
            {
                Native.RECT wr;
                Native.GetWindowRect(fg, out wr);
                bool covers = wr.Left <= screen.Left && wr.Top <= screen.Top && wr.Right >= screen.Right && wr.Bottom >= screen.Bottom;
                int cx = (wr.Left + wr.Right) / 2, cy = (wr.Top + wr.Bottom) / 2;
                if (screen.Contains(cx, cy) && (covers || Native.IsZoomed(fg))) { pause = true; pauseBy = c; }
            }
        }
        int p = pause ? 1 : 0;
        if (p == lastPause) return;
        lastPause = p;
        Post("{\"t\":\"pause\",\"v\":" + (pause ? "true" : "false") + "}");
        Log.Write(pause ? "Paused (a window is maximized / fullscreen, or paused manually)" + (pauseBy == "" ? "" : ", foreground window class " + pauseBy) : "Resumed");
    }

    // ---------------------------------------------------------------- Exit: remove the window and let Explorer redraw the original wallpaper
    public void Shutdown()
    {
        if (shuttingDown) return;
        shuttingDown = true;
        iconTimer.Stop(); fgTimer.Stop();
        reader.Dispose();
        IntPtr parent = parentHwnd;
        try { web.Dispose(); } catch { }
        if (IsHandleCreated) { Native.SetParent(Handle, IntPtr.Zero); Hide(); }
        // Wallpaper settings are left untouched (slideshow / Spotlight are unaffected); Explorer just redraws
        if (parent != IntPtr.Zero && Native.IsWindow(parent))
            Native.RedrawWindow(parent, IntPtr.Zero, IntPtr.Zero, 0x1 | 0x4 | 0x80 | 0x100);
        Close();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (winEventHook != IntPtr.Zero) { Native.UnhookWinEvent(winEventHook); winEventHook = IntPtr.Zero; }
        iconTimer.Stop(); fgTimer.Stop();
        reader.Dispose();
        base.OnHandleDestroyed(e);
    }
}

// Reads icon rectangles through cross-process messages to the desktop ListView (icon + label, physical pixels, screen coordinates)
class IconReader : IDisposable
{
    IntPtr listView, proc, mem;

    static IntPtr FindListView()
    {
        IntPtr defView = IntPtr.Zero;
        IntPtr progman = Native.FindWindow("Progman", null);
        if (progman != IntPtr.Zero) defView = Native.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (defView == IntPtr.Zero)
        {
            Native.EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                IntPtr d = Native.FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (d != IntPtr.Zero) { defView = d; return false; }
                return true;
            }, IntPtr.Zero);
        }
        return defView == IntPtr.Zero ? IntPtr.Zero : Native.FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
    }

    bool Open()
    {
        Close();
        listView = FindListView();
        if (listView == IntPtr.Zero) return false;
        uint pid;
        Native.GetWindowThreadProcessId(listView, out pid);
        proc = Native.OpenProcess(0x0008 | 0x0010 | 0x0020, false, pid);   // VM_OPERATION | VM_READ | VM_WRITE
        if (proc == IntPtr.Zero) return false;
        mem = Native.VirtualAllocEx(proc, IntPtr.Zero, new IntPtr(64), 0x1000 | 0x2000, 0x04);
        return mem != IntPtr.Zero;
    }

    public List<Native.RECT> Read(out bool visible)
    {
        visible = false;
        if (listView == IntPtr.Zero || mem == IntPtr.Zero || !Native.IsWindow(listView)) { if (!Open()) return null; }
        visible = Native.IsWindowVisible(listView);
        var result = new List<Native.RECT>();
        if (!visible) return result;                   // the user hid the desktop icons → all sea
        int count = (int)Native.SendMessage(listView, 0x1004 /* LVM_GETITEMCOUNT */, IntPtr.Zero, IntPtr.Zero);
        var origin = new Native.POINT();
        Native.ClientToScreen(listView, ref origin);
        for (int i = 0; i < count; i++)
        {
            var r = new Native.RECT();                 // Left = LVIR_BOUNDS (0): full bounds of icon + label
            IntPtr n;
            if (!Native.WriteProcessMemory(proc, mem, ref r, 16, out n)) { Close(); return null; }
            Native.SendMessage(listView, 0x100E /* LVM_GETITEMRECT */, new IntPtr(i), mem);
            if (!Native.ReadProcessMemory(proc, mem, out r, 16, out n)) { Close(); return null; }
            r.Left += origin.X; r.Right += origin.X; r.Top += origin.Y; r.Bottom += origin.Y;
            result.Add(r);
        }
        return result;
    }

    void Close()
    {
        if (mem != IntPtr.Zero && proc != IntPtr.Zero) Native.VirtualFreeEx(proc, mem, IntPtr.Zero, 0x8000);
        if (proc != IntPtr.Zero) Native.CloseHandle(proc);
        mem = proc = IntPtr.Zero;
    }

    public void Dispose() { Close(); }
}

static class Native
{
    public const int GWL_STYLE = -16, GWL_EXSTYLE = -20, WS_EX_LAYERED = 0x00080000;
    public const uint LWA_ALPHA = 0x2, SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002;
    public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000;
    public const int SW_SHOW = 5, SW_RESTORE = 9;
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint modifiers, uint vk);
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
    [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int pid);
    [DllImport("user32.dll")] public static extern uint GetDpiForSystem();
    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode)] public static extern bool WTSQuerySessionInformation(IntPtr server, int session, int infoClass, out IntPtr buffer, out int bytes);
    [DllImport("wtsapi32.dll")] public static extern void WTSFreeMemory(IntPtr p);
    public delegate void WinEventDelegate(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventDelegate proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
    public const int WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_POPUP = unchecked((int)0x80000000);
    public const int WS_CAPTION = 0x00C00000, WS_THICKFRAME = 0x00040000;
    public const uint SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040, SWP_FRAMECHANGED = 0x0020;

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int index, int value);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool RedrawWindow(IntPtr h, IntPtr rect, IntPtr rgn, uint flags);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attribute, out int value, int size);
    [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] public static extern IntPtr VirtualAllocEx(IntPtr proc, IntPtr addr, IntPtr size, uint type, uint protect);
    [DllImport("kernel32.dll")] public static extern bool VirtualFreeEx(IntPtr proc, IntPtr addr, IntPtr size, uint type);
    [DllImport("kernel32.dll")] public static extern bool WriteProcessMemory(IntPtr proc, IntPtr addr, ref RECT buf, int size, out IntPtr written);
    [DllImport("kernel32.dll")] public static extern bool ReadProcessMemory(IntPtr proc, IntPtr addr, out RECT buf, int size, out IntPtr read);
}

// Terrarium has its own data and instance identity; isolated profiles support installation QA.
static class InstallProfile
{
    public static readonly string Override = Environment.GetEnvironmentVariable("TERRARIUM_PROFILE_DIR");
    public static readonly bool Isolated = !string.IsNullOrEmpty(Override);
    public static readonly string DataDir = Isolated ? Path.GetFullPath(Override) : Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Terrarium");
    public static readonly string Suffix = MakeSuffix();
    static string MakeSuffix()
    {
        if (!Isolated) return "";
        using (var hash = System.Security.Cryptography.SHA256.Create())
            return "_" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(DataDir.ToUpperInvariant()))).Replace("-", "").Substring(0, 24);
    }

    // An installer may only close the instance using this exact executable path.
    public static int StopInstalled()
    {
        foreach (Process process in Process.GetProcessesByName("Terrarium"))
        {
            using (process)
            {
                if (process.Id == Process.GetCurrentProcess().Id) continue;
                try
                {
                    if (!string.Equals(process.MainModule.FileName, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase)) continue;
                    EventWaitHandle ev;
                    if (!EventWaitHandle.TryOpenExisting(Program.ExitEventName, out ev)) return 1;
                    using (ev) ev.Set();
                    if (!process.WaitForExit(10000)) return 1;
                }
                catch (InvalidOperationException) { }
                catch { return 1; }
            }
        }
        return 0;
    }
}

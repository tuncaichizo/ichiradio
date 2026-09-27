// Ichi Radyo — hafif internet radyosu.
// Derleme: build.ps1 (Windows'un kendi .NET Framework csc'si, ek kurulum gerekmez; C# 5 sözdizimi).
// Ses: Windows Media Player motoru (COM). İstasyonlar: radio-browser.info.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("Ichi Radyo")]
[assembly: AssemblyDescription("Hafif internet radyosu")]
[assembly: AssemblyProduct("Ichi Radyo")]
[assembly: AssemblyCompany("Kripto Ichizo")]
[assembly: AssemblyCopyright("Kripto Ichizo 2026")]
[assembly: AssemblyVersion("1.0.1.0")]
[assembly: AssemblyFileVersion("1.0.1.0")]

namespace IchiRadyo
{
    static class Native
    {
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] public static extern int SetWindowTheme(IntPtr h, string app, string id);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
    }

    static class Tema
    {
        public static readonly Color Bg = Color.FromArgb(10, 10, 18);
        public static readonly Color Panel = Color.FromArgb(19, 19, 34);
        public static readonly Color Panel2 = Color.FromArgb(34, 34, 60);
        public static readonly Color Cyan = Color.FromArgb(0, 240, 255);
        public static readonly Color Mag = Color.FromArgb(255, 43, 214);
        public static readonly Color Yel = Color.FromArgb(245, 230, 99);
        public static readonly Color Txt = Color.FromArgb(222, 226, 245);
        public static readonly Color Dim = Color.FromArgb(112, 116, 145);

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Text(Graphics g, string s, Font f, Color c, RectangleF r, StringAlignment h = StringAlignment.Near)
        {
            using (var sf = new StringFormat(StringFormatFlags.NoWrap))
            using (var b = new SolidBrush(c))
            {
                sf.Trimming = StringTrimming.EllipsisCharacter;
                sf.Alignment = h;
                sf.LineAlignment = StringAlignment.Center;
                g.DrawString(s ?? "", f, b, r, sf);
            }
        }

        public static void Logo(Graphics g, float s)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(s * 0.04f, s * 0.04f, s * 0.92f, s * 0.92f);
            using (var p = Round(r, s * 0.22f))
            {
                using (var b = new SolidBrush(Bg)) g.FillPath(b, p);
                using (var pen = new Pen(Mag, Math.Max(1f, s * 0.06f))) g.DrawPath(pen, p);
            }
            float cx = s * 0.5f, cy = s * 0.68f;
            using (var b = new SolidBrush(Mag))
            {
                float d = s * 0.17f;
                g.FillEllipse(b, cx - d / 2, cy - d / 2, d, d);
            }
            using (var pen = new Pen(Cyan, Math.Max(1.3f, s * 0.08f)))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                foreach (float k in new[] { 0.22f, 0.37f })
                {
                    float rr = s * k;
                    g.DrawArc(pen, cx - rr, cy - rr, rr * 2, rr * 2, 225, 90);
                }
            }
        }
    }

    class Station
    {
        public string Uuid = "", Name = "", Url = "", Codec = "", Country = "";
        public int Bitrate;
        public double Lat = double.NaN, Lon = double.NaN; // yalnız haritada kullanılır, kaydedilmez

        public bool HasGeo { get { return !double.IsNaN(Lat) && !double.IsNaN(Lon) && !(Lat == 0 && Lon == 0); } }

        public string Sub()
        {
            var parts = new List<string>();
            if (Codec != "") parts.Add(Codec);
            if (Bitrate > 0) parts.Add(Bitrate + "k");
            if (Country != "") parts.Add(Country);
            return string.Join("  ·  ", parts.ToArray());
        }

        static string Clean(string s) { return (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim(); }

        public string Save()
        {
            return string.Join("\t", new[] { Clean(Uuid), Clean(Name), Clean(Url), Clean(Codec), Bitrate.ToString(), Clean(Country) });
        }

        public static Station Load(string line)
        {
            var a = (line ?? "").Split('\t');
            if (a.Length < 6 || a[2] == "") return null;
            int br;
            int.TryParse(a[4], out br);
            return new Station { Uuid = a[0], Name = a[1], Url = a[2], Codec = a[3], Bitrate = br, Country = a[5] };
        }

        public override string ToString() { return Name; }
    }

    static class Api
    {
        static readonly string[] Servers = { "de1", "de2", "fi1" };

        // Boş sorgu: Türkiye popüler. "#tür": etikete göre. Diğer: isimde geçen.
        public static List<Station> Search(string q)
        {
            q = (q ?? "").Trim();
            string path;
            if (q == "") path = "stations/search?countrycode=TR";
            else if (q.StartsWith("#")) path = "stations/search?tag=" + Uri.EscapeDataString(q.Substring(1).Trim());
            else path = "stations/search?name=" + Uri.EscapeDataString(q);
            return Stations(path + "&order=clickcount&reverse=true&hidebroken=true&limit=150", 80);
        }

        // Harita için: bir ülkenin en popüler istasyonları (konumlarıyla).
        public static List<Station> ByCountry(string iso)
        {
            return Stations("stations/search?countrycode=" + Uri.EscapeDataString(iso) + "&order=clickcount&reverse=true&hidebroken=true&limit=400", 250);
        }

        // Ülke kodu → istasyon sayısı (haritayı renklendirmek için).
        public static Dictionary<string, int> CountryCounts()
        {
            var res = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var arr = Parse(Get("countries")) as object[];
            if (arr == null) return res;
            foreach (object o in arr)
            {
                var d = o as Dictionary<string, object>;
                if (d == null) continue;
                string iso = S(d, "iso_3166_1");
                if (iso != "") res[iso] = I(d, "stationcount");
            }
            return res;
        }

        static object Parse(string json) { return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(json); }

        static List<Station> Stations(string path, int max)
        {
            var arr = Parse(Get(path)) as object[];
            var res = new List<Station>();
            var seen = new HashSet<string>();
            if (arr == null) return res;
            foreach (object o in arr)
            {
                var d = o as Dictionary<string, object>;
                if (d == null) continue;
                string url = S(d, "url_resolved"), codec = S(d, "codec").ToUpperInvariant();
                // WMP'nin sorunsuz çaldığı biçimler; HLS dışarıda.
                if (url == "" || I(d, "hls") == 1) continue;
                if (codec != "MP3" && codec != "AAC" && codec != "AAC+") continue;
                if (!seen.Add(url)) continue;
                res.Add(new Station
                {
                    Uuid = S(d, "stationuuid"), Name = S(d, "name").Trim(), Url = url, Codec = codec,
                    Bitrate = I(d, "bitrate"), Country = S(d, "countrycode"), Lat = D(d, "geo_lat"), Lon = D(d, "geo_long")
                });
                if (res.Count >= max) break;
            }
            return res;
        }

        // Sadece istasyon listesi (JSON metin) okunur; dosya indirilmez, çalıştırılmaz.
        static string Get(string path)
        {
            Exception last = null;
            foreach (var s in Servers)
            {
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create("https://" + s + ".api.radio-browser.info/json/" + path);
                    req.UserAgent = "IchiRadyo/1.0";
                    req.Accept = "application/json";
                    req.Timeout = 8000;
                    req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                    using (var resp = req.GetResponse())
                    using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                        return sr.ReadToEnd();
                }
                catch (Exception ex) { last = ex; }
            }
            throw last;
        }

        static string S(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) && v != null ? v.ToString() : ""; }
        static int I(Dictionary<string, object> d, string k) { int n; int.TryParse(S(d, k), out n); return n; }

        static double D(Dictionary<string, object> d, string k)
        {
            object v;
            if (d.TryGetValue(k, out v) && v != null)
                try { return Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture); } catch { }
            return double.NaN;
        }
    }

    class Player
    {
        readonly dynamic w;

        public Player()
        {
            w = Activator.CreateInstance(Type.GetTypeFromProgID("WMPlayer.OCX"));
            w.settings.autoStart = true;
        }

        public void Play(string url) { w.URL = url; w.controls.play(); }
        public void Stop() { try { w.controls.stop(); w.URL = ""; } catch { } }
        public void SetMute(bool m) { try { w.settings.mute = m; } catch { } }

        // Kaydırıcı (0-100) algıya uygun eğriyle kazanca çevrilir: %50 ≈ -12 dB, %4 ≈ -56 dB.
        public static float Gain(int v) { float f = v / 100f; return f * f; }

        // WMP ana ses seviyesini tam sayı yüzde olarak yönetir ve oturumun ana sesini buna yuvarlayıp geri yazar;
        // bu yüzden ana sese dokunmayız. WMP'ye üst tam sayıyı veririz, aradaki ince farkı kanal sesine yazarız
        // (WMP kanal sesini izlemez). Kanal sesi sıfırlanırsa ses en fazla %1 yüksek çıkar.
        public void SetVolume(int v)
        {
            float g = Gain(v);
            int wv = (int)Math.Ceiling(g * 100 - 1e-4);
            try { w.settings.volume = wv; } catch { }
            SessionVolume.SetChannels(wv > 0 ? Math.Min(1f, g / (wv / 100f)) : 0f);
        }
        public int State { get { try { return (int)w.playState; } catch { return 0; } } }
        public int Buffering { get { try { return (int)w.network.bufferingProgress; } catch { return 0; } } }
        public void Close() { try { w.close(); } catch { } }

        public string Title
        {
            get
            {
                try
                {
                    dynamic m = w.currentMedia;
                    if (m == null) return "";
                    return (m.getItemInfo("Title") as string) ?? "";
                }
                catch { return ""; }
            }
        }
    }

    // Bu sürecin Windows ses oturumlarına ondalıklı ses seviyesi yazar (Core Audio).
    static class SessionVolume
    {
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCo { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(int flow, int mask, out IntPtr devs);
            int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice ep);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            int Activate(ref Guid iid, int ctx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object o);
        }

        [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionManager2
        {
            int GetAudioSessionControl(IntPtr g, int f, out IntPtr c);
            int GetSimpleAudioVolume(IntPtr g, int f, out IntPtr v);
            int GetSessionEnumerator(out IAudioSessionEnumerator e);
        }

        [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionEnumerator
        {
            int GetCount(out int n);
            int GetSession(int i, out IAudioSessionControl2 s);
        }

        [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionControl2
        {
            int GetState(out int s);
            int GetDisplayName(out IntPtr n);
            int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string n, ref Guid g);
            int GetIconPath(out IntPtr p);
            int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string p, ref Guid g);
            int GetGroupingParam(out Guid g);
            int SetGroupingParam(ref Guid g, ref Guid c);
            int RegisterAudioSessionNotification(IntPtr n);
            int UnregisterAudioSessionNotification(IntPtr n);
            int GetSessionIdentifier(out IntPtr s);
            int GetSessionInstanceIdentifier(out IntPtr s);
            int GetProcessId(out uint pid);
        }

        [ComImport, Guid("1C158861-B533-4B30-B1CF-E853E51C59B8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IChannelAudioVolume
        {
            int GetChannelCount(out uint n);
            int SetChannelVolume(uint i, float v, ref Guid g);
            int GetChannelVolume(uint i, out float v);
        }

        static readonly uint Me = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

        public static void SetChannels(float v)
        {
            try
            {
                var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCo();
                IMMDevice dev;
                if (en.GetDefaultAudioEndpoint(0, 1, out dev) != 0) return;
                var iid = typeof(IAudioSessionManager2).GUID;
                object o;
                if (dev.Activate(ref iid, 23, IntPtr.Zero, out o) != 0) return;
                IAudioSessionEnumerator se;
                if (((IAudioSessionManager2)o).GetSessionEnumerator(out se) != 0) return;
                int n;
                se.GetCount(out n);
                var ctx = Guid.Empty;
                for (int i = 0; i < n; i++)
                {
                    IAudioSessionControl2 c;
                    if (se.GetSession(i, out c) != 0 || c == null) continue;
                    uint pid;
                    if (c.GetProcessId(out pid) == 0 && pid == Me)
                    {
                        var ch = (IChannelAudioVolume)c;
                        uint cn;
                        if (ch.GetChannelCount(out cn) == 0)
                            for (uint k = 0; k < cn; k++) ch.SetChannelVolume(k, v, ref ctx);
                    }
                    Marshal.ReleaseComObject(c);
                }
                Marshal.ReleaseComObject(se);
                Marshal.ReleaseComObject(o);
                Marshal.ReleaseComObject(dev);
                Marshal.ReleaseComObject(en);
            }
            catch { }
        }
    }

    static class Store
    {
        static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IchiRadyo");
        static string FavFile { get { return Path.Combine(Dir, "favoriler.txt"); } }
        static string SetFile { get { return Path.Combine(Dir, "ayarlar.txt"); } }

        public static List<Station> LoadFavs()
        {
            var l = new List<Station>();
            try
            {
                if (File.Exists(FavFile))
                    foreach (var line in File.ReadAllLines(FavFile, Encoding.UTF8))
                    {
                        var s = Station.Load(line);
                        if (s != null) l.Add(s);
                    }
            }
            catch { }
            return l;
        }

        public static void SaveFavs(List<Station> l)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllLines(FavFile, l.ConvertAll(s => s.Save()).ToArray(), Encoding.UTF8);
            }
            catch { }
        }

        public static Dictionary<string, string> LoadSettings()
        {
            var d = new Dictionary<string, string>();
            try
            {
                if (File.Exists(SetFile))
                    foreach (var line in File.ReadAllLines(SetFile, Encoding.UTF8))
                    {
                        int i = line.IndexOf('=');
                        if (i > 0) d[line.Substring(0, i)] = line.Substring(i + 1);
                    }
            }
            catch { }
            return d;
        }

        public static void SaveSettings(Dictionary<string, string> d)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var lines = new List<string>();
                foreach (var kv in d) lines.Add(kv.Key + "=" + kv.Value);
                File.WriteAllLines(SetFile, lines.ToArray(), Encoding.UTF8);
            }
            catch { }
        }
    }

    class DarkColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Tema.Panel; } }
        public override Color ImageMarginGradientBegin { get { return Tema.Panel; } }
        public override Color ImageMarginGradientMiddle { get { return Tema.Panel; } }
        public override Color ImageMarginGradientEnd { get { return Tema.Panel; } }
        public override Color MenuBorder { get { return Tema.Panel2; } }
        public override Color MenuItemBorder { get { return Color.FromArgb(0, 140, 160); } }
        public override Color MenuItemSelected { get { return Tema.Panel2; } }
        public override Color SeparatorDark { get { return Tema.Panel2; } }
        public override Color SeparatorLight { get { return Tema.Panel; } }
    }

    class DarkRenderer : ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Tema.Txt : Tema.Cyan;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Tema.Dim;
            base.OnRenderArrow(e);
        }
    }

    class MainForm : Form
    {
        const int W = 360, H = 580;

        readonly float K;
        readonly Player player = new Player();
        readonly List<Station> favs;
        List<Station> found = new List<Station>();
        Station current;
        bool wantPlaying, playing, searched, dragVol, muted;
        int badTicks, retries, volume = 70, tab, searchSeq, volTicks;
        string status = "HAZIR", song = "", exploreMsg = "", hover = "";
        DateTime? sleepAt;
        readonly float[] bars = new float[14], barT = new float[14];
        readonly Random rnd = new Random();

        readonly ListBox list;
        readonly TextBox search;
        readonly NotifyIcon tray;
        readonly ContextMenuStrip trayMenu;
        readonly DarkRenderer renderer = new DarkRenderer();
        readonly System.Windows.Forms.Timer poll, anim;
        readonly Font fTitle, fMono, fStation, fSong, fTab, fItem, fSmall;

        // Mantıksal (96 dpi) koordinatlar; çizimde K ile ölçeklenir.
        // Harita açıkken pencere sağa MW kadar genişler; başlık düğmeleri sağ kenarda kalır.
        const int MW = 660;
        MapView map;
        string fontFamily, exploreLabel = "";
        bool startWithMap;
        float LW { get { return W + (map != null ? MW : 0); } }
        RectangleF rClose { get { return new RectangleF(LW - 42, 0, 42, 38); } }
        RectangleF rTray { get { return new RectangleF(LW - 84, 0, 42, 38); } }
        RectangleF rMap { get { return new RectangleF(LW - 126, 0, 42, 38); } }
        readonly RectangleF rNow = new RectangleF(12, 44, W - 24, 120);
        readonly RectangleF rBars = new RectangleF(W - 26 - 84, 122, 84, 32);
        readonly RectangleF rPlay = new RectangleF(18, 178, 50, 50);
        readonly RectangleF rFav = new RectangleF(80, 189, 28, 28);
        readonly RectangleF rSpk = new RectangleF(120, 192, 26, 26);
        readonly RectangleF rVol = new RectangleF(156, 202, W - 156 - 66, 6);
        readonly RectangleF rVolHit = new RectangleF(150, 180, W - 150 - 10, 44);
        readonly RectangleF rTab0 = new RectangleF(12, 242, (W - 24) / 2f, 30), rTab1 = new RectangleF(12 + (W - 24) / 2f, 242, (W - 24) / 2f, 30);
        readonly RectangleF rSearch = new RectangleF(12, 282, W - 24, 32);
        RectangleF rList { get { float top = tab == 1 ? 322 : 282; return new RectangleF(12, top, W - 24, H - 12 - top); } }

        public MainForm()
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) K = g.DpiX / 96f;

            Text = "Ichi Radyo";
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(S(W), S(H));
            BackColor = Tema.Bg;
            KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Icon = MakeIcon(S(32));

            string ff = HasFont("Bahnschrift") ? "Bahnschrift" : "Segoe UI";
            fontFamily = ff;
            fTitle = new Font(ff, 14, FontStyle.Bold, GraphicsUnit.Pixel);
            fMono = new Font("Consolas", 11, FontStyle.Regular, GraphicsUnit.Pixel);
            fStation = new Font(ff, 22, FontStyle.Bold, GraphicsUnit.Pixel);
            fSong = new Font(ff, 14, FontStyle.Regular, GraphicsUnit.Pixel);
            fTab = new Font(ff, 12, FontStyle.Bold, GraphicsUnit.Pixel);
            fItem = new Font(ff, 14, FontStyle.Regular, GraphicsUnit.Pixel);
            fSmall = new Font(ff, 11, FontStyle.Regular, GraphicsUnit.Pixel);

            search = new TextBox { BorderStyle = BorderStyle.None, BackColor = Tema.Panel, ForeColor = Tema.Txt, Font = new Font(ff, 14 * K, GraphicsUnit.Pixel) };
            search.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; DoSearch(); }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; search.Text = ""; DoSearch(); }
            };
            search.GotFocus += delegate { Invalidate(); };
            search.LostFocus += delegate { Invalidate(); };

            list = new ListBox { BorderStyle = BorderStyle.None, BackColor = Tema.Panel, ForeColor = Tema.Txt, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = S(40), IntegralHeight = false };
            list.DrawItem += DrawItem;
            list.SelectedIndexChanged += delegate { if (map != null) { map.Highlight = list.SelectedItem as Station; map.Invalidate(); } };
            list.DoubleClick += delegate { PlayStation(list.SelectedItem as Station); };
            list.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; PlayStation(list.SelectedItem as Station); }
                else if (e.KeyCode == Keys.Delete && tab == 0 && list.SelectedItem != null) { e.SuppressKeyPress = true; ToggleFav(list.SelectedItem as Station); }
            };
            list.MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Right) return;
                int i = list.IndexFromPoint(e.Location);
                if (i < 0) return;
                list.SelectedIndex = i;
                ShowListMenu(list.Items[i] as Station, e.Location);
            };
            Controls.Add(list);
            Controls.Add(search);

            poll = new System.Windows.Forms.Timer { Interval = 1000 };
            poll.Tick += delegate { Poll(); };
            poll.Start();
            anim = new System.Windows.Forms.Timer { Interval = 90 };
            anim.Tick += delegate { Animate(); };

            favs = Store.LoadFavs();

            trayMenu = new ContextMenuStrip { Renderer = renderer, ShowImageMargin = false };
            trayMenu.Opening += delegate { BuildTrayMenu(); };
            BuildTrayMenu();
            tray = new NotifyIcon { Icon = MakeIcon(SystemInformation.SmallIconSize.Width), Text = "Ichi Radyo", ContextMenuStrip = trayMenu, Visible = true };
            tray.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) { if (Visible) HideToTray(); else ShowFromTray(); }
                else if (e.Button == MouseButtons.Middle) TogglePlay();
            };

            var st = Store.LoadSettings();
            volume = Math.Max(0, Math.Min(100, GetInt(st, "ses", 70)));
            player.SetVolume(volume);
            string last;
            if (st.TryGetValue("son", out last)) current = Station.Load(last);
            startWithMap = st.ContainsKey("harita") && st["harita"] == "1";
            int x = GetInt(st, "x", int.MinValue), y = GetInt(st, "y", int.MinValue);
            if (OnScreen(x, y)) Location = new Point(x, y);
            else
            {
                var wa = Screen.PrimaryScreen.WorkingArea;
                Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (wa.Height - Height) / 2);
            }
            tab = favs.Count > 0 ? 0 : 1;
            RefreshList();
            UpdateTray();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= 0x20000; // CS_DROPSHADOW
                cp.Style |= 0x20000;      // WS_MINIMIZEBOX: görev çubuğundan küçültme
                return cp;
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            int round = 2;
            Native.DwmSetWindowAttribute(Handle, 33, ref round, 4); // Win11 yuvarlak köşe
            int border = 0x826E00; // COLORREF (BGR): koyu camgöbeği
            Native.DwmSetWindowAttribute(Handle, 34, ref border, 4);
            Native.SetWindowTheme(list.Handle, "DarkMode_Explorer", null);
            Native.SendMessage(search.Handle, 0x1501, (IntPtr)1, "İstasyon ara…   (#pop gibi tür de olur)");
            if (tab == 1) DoSearch();
            if (startWithMap) ToggleMap();
        }

        // İkinci kopya açılınca bu olay tetiklenir; pencereyi öne getir.
        public void ListenForShow(EventWaitHandle ev)
        {
            var t = new Thread(delegate ()
            {
                while (ev.WaitOne())
                {
                    try { BeginInvoke((MethodInvoker)ShowFromTray); } catch { return; }
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        // Harita radyonun sağında açılır; kapatınca kontrol atılır, belleği boşalır.
        void ToggleMap()
        {
            if (map == null)
            {
                map = new MapView(this, K, fontFamily);
                map.CountryPicked += LoadCountry;
                map.StationPicked += s =>
                {
                    PlayStation(s);
                    if (tab == 1 && list.Items.Contains(s)) list.SelectedItem = s;
                };
                ClientSize = new Size(S(W + MW), S(H));
                map.Bounds = Sc(new RectangleF(W, 44, MW - 12, H - 56));
                Controls.Add(map);
                map.SetStations(found);
                map.Highlight = list.SelectedItem as Station;
                var wa = Screen.FromControl(this).WorkingArea;
                if (Right > wa.Right) Left = Math.Max(wa.Left, wa.Right - Width);
            }
            else
            {
                Controls.Remove(map);
                map.Dispose();
                map = null;
                ClientSize = new Size(S(W), S(H));
            }
            hover = "";
            Invalidate();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveSettings();
            poll.Stop();
            anim.Stop();
            tray.Visible = false;
            tray.Dispose();
            player.Stop();
            player.Close();
            base.OnFormClosing(e);
        }

        // ---------- çalma ----------

        public bool IsCurrent(Station s) { return s != null && current != null && current.Url == s.Url; }
        public bool IsPlaying(Station s) { return wantPlaying && IsCurrent(s); }

        public void PlayStation(Station s)
        {
            if (s == null) return;
            current = s;
            wantPlaying = true;
            playing = false;
            badTicks = retries = 0;
            song = "";
            status = "BAĞLANIYOR…";
            player.Play(s.Url);
            SaveSettings();
            list.Invalidate();
            if (map != null) map.Invalidate();
            Invalidate();
            UpdateTray();
        }

        void StopPlay()
        {
            wantPlaying = playing = false;
            player.Stop();
            status = "DURDU";
            song = "";
            anim.Stop();
            Array.Clear(bars, 0, bars.Length);
            Invalidate();
            UpdateTray();
        }

        void TogglePlay()
        {
            if (wantPlaying) StopPlay();
            else if (current != null) PlayStation(current);
            else PlayStation(list.SelectedItem as Station);
        }

        void Poll()
        {
            if (sleepAt.HasValue)
            {
                if (DateTime.Now >= sleepAt.Value) { sleepAt = null; StopPlay(); }
                else Invalidate(Sc(rNow));
            }
            if (!wantPlaying) return;

            int st = player.State;
            string ns = status, nsong = song;
            bool np = st == 3;
            if (np)
            {
                badTicks = retries = 0;
                ns = "● CANLI";
                nsong = CleanTitle(player.Title);
                // Ses oturumu yayın başlayınca oluşur; ince sesi o an ve arada bir yeniden yaz.
                if (!playing || ++volTicks >= 5) { volTicks = 0; player.SetVolume(volume); }
            }
            else if (st == 6 || st == 7 || st == 9)
            {
                ns = st == 6 ? "TAMPON %" + player.Buffering : "BAĞLANIYOR…";
                if (++badTicks > 25) { Reconnect(); return; }
            }
            else if (++badTicks >= 4) { Reconnect(); return; }

            if (ns != status || nsong != song || np != playing)
            {
                status = ns; song = nsong; playing = np;
                Invalidate();
                UpdateTray();
            }
            bool animOn = playing && Visible && WindowState != FormWindowState.Minimized;
            if (animOn != anim.Enabled) anim.Enabled = animOn;
        }

        void Reconnect()
        {
            badTicks = 0;
            playing = false;
            if (++retries > 5)
            {
                wantPlaying = false;
                player.Stop();
                status = "YAYIN AÇILAMADI";
                song = "";
            }
            else
            {
                status = "YENİDEN BAĞLANIYOR " + retries + "/5";
                player.Play(current.Url);
            }
            anim.Stop();
            Invalidate();
            UpdateTray();
        }

        string CleanTitle(string t)
        {
            if (string.IsNullOrEmpty(t) || current == null) return "";
            t = t.Trim();
            if (t.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return "";
            if (t.Equals(current.Name, StringComparison.OrdinalIgnoreCase)) return "";
            if (current.Url.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0) return "";
            return t;
        }

        void Animate()
        {
            int n = bars.Length;
            for (int i = 0; i < n; i++)
            {
                if (rnd.NextDouble() < 0.35)
                {
                    double shape = 1 - Math.Abs(i - n / 2.0) / n * 0.9;
                    barT[i] = (float)((0.12 + rnd.NextDouble() * 0.88) * shape);
                }
                bars[i] += (barT[i] - bars[i]) * 0.45f;
            }
            Invalidate(Sc(rBars));
        }

        void SetVolume(int v)
        {
            v = Math.Max(0, Math.Min(100, v));
            if (v == volume && !muted) return;
            volume = v;
            if (muted) { muted = false; player.SetMute(false); }
            player.SetVolume(v);
            Invalidate(Sc(RectangleF.Union(rSpk, rVolHit)));
        }

        void ToggleMute()
        {
            muted = !muted;
            player.SetMute(muted);
            Invalidate(Sc(RectangleF.Union(rSpk, rVolHit)));
        }

        // ---------- favoriler / liste ----------

        public bool IsFav(Station s) { return s != null && favs.Exists(f => f.Url == s.Url); }

        public void ToggleFav(Station s)
        {
            if (s == null) return;
            int i = favs.FindIndex(f => f.Url == s.Url);
            if (i >= 0) favs.RemoveAt(i); else favs.Add(s);
            Store.SaveFavs(favs);
            if (tab == 0) RefreshList(); else list.Invalidate();
            Invalidate();
        }

        void MoveFav(Station s, int d)
        {
            int i = favs.IndexOf(s), j = i + d;
            if (i < 0 || j < 0 || j >= favs.Count) return;
            favs[i] = favs[j];
            favs[j] = s;
            Store.SaveFavs(favs);
            RefreshList();
            list.SelectedIndex = j;
        }

        void SetTab(int t)
        {
            if (tab == t) return;
            tab = t;
            RefreshList();
            if (t == 1 && !searched) DoSearch();
            Invalidate();
        }

        void RefreshList()
        {
            var src = tab == 0 ? favs : found;
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var s in src) list.Items.Add(s);
            list.EndUpdate();
            search.Visible = tab == 1;
            search.Bounds = Sc(new RectangleF(rSearch.X + 12, rSearch.Y + 7, rSearch.Width - 24, 18));
            var r = rList;
            list.Bounds = Sc(new RectangleF(r.X + 4, r.Y + 4, r.Width - 8, r.Height - 8));
            list.Visible = list.Items.Count > 0;
        }

        void DoSearch()
        {
            string q = search.Text;
            if (map != null) map.ClearCountry();
            Explore(() => Api.Search(q), "");
        }

        // Haritada ülkeye tıklanınca: o ülkenin istasyonları KEŞFET listesine gelir.
        void LoadCountry(Country c)
        {
            tab = 1;
            search.Text = "";
            if (c.Iso == "--")
            {
                ++searchSeq;
                exploreLabel = c.Name;
                found = new List<Station>();
                exploreMsg = "Bu bölge için istasyon verisi yok.";
                RefreshList();
                if (map != null) map.SetStations(found);
                Invalidate();
                return;
            }
            string iso = c.Iso;
            Explore(() => Api.ByCountry(iso), c.Name);
        }

        void Explore(Func<List<Station>> fetch, string label)
        {
            int seq = ++searchSeq;
            searched = true;
            exploreLabel = label;
            found = new List<Station>();
            exploreMsg = "Yükleniyor…";
            if (tab == 1) RefreshList();
            if (map != null) map.SetStations(found);
            Invalidate();
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<Station> res = null;
                string err = null;
                try { res = fetch(); }
                catch (Exception ex) { err = ex.Message; }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (seq != searchSeq) return;
                        found = res ?? new List<Station>();
                        exploreMsg = err != null ? "Bağlantı hatası:\n" + err : "Sonuç bulunamadı.";
                        if (tab == 1) RefreshList();
                        if (map != null) map.SetStations(found);
                        Invalidate();
                    });
                }
                catch { }
            });
        }

        void ShowListMenu(Station s, Point at)
        {
            if (s == null) return;
            var m = new ContextMenuStrip { Renderer = renderer, ShowImageMargin = false };
            m.Items.Add("Çal", null, delegate { PlayStation(s); });
            m.Items.Add(IsFav(s) ? "Favorilerden çıkar" : "Favorilere ekle  ★", null, delegate { ToggleFav(s); });
            if (tab == 0)
            {
                int i = favs.IndexOf(s);
                if (i > 0) m.Items.Add("Yukarı taşı", null, delegate { MoveFav(s, -1); });
                if (i >= 0 && i < favs.Count - 1) m.Items.Add("Aşağı taşı", null, delegate { MoveFav(s, 1); });
            }
            m.Items.Add("Yayın adresini kopyala", null, delegate { try { Clipboard.SetText(s.Url); } catch { } });
            m.Closed += delegate { BeginInvoke((MethodInvoker)delegate { m.Dispose(); }); };
            m.Show(list, at);
        }

        void AddSleepItems(ToolStripItemCollection items)
        {
            foreach (int m in new[] { 15, 30, 45, 60, 90 })
            {
                int mm = m;
                items.Add(mm + " dakika sonra kapat", null, delegate { sleepAt = DateTime.Now.AddMinutes(mm); Invalidate(); });
            }
            if (sleepAt.HasValue) items.Add("Zamanlayıcıyı iptal et", null, delegate { sleepAt = null; Invalidate(); });
        }

        // ---------- tepsi ----------

        void BuildTrayMenu()
        {
            while (trayMenu.Items.Count > 0)
            {
                var it = trayMenu.Items[0];
                trayMenu.Items.RemoveAt(0);
                it.Dispose();
            }
            trayMenu.Items.Add(new ToolStripMenuItem(current != null ? current.Name : "Ichi Radyo") { Enabled = false });
            if (song != "") trayMenu.Items.Add(new ToolStripMenuItem(song) { Enabled = false });
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(wantPlaying ? "Durdur" : "Çal", null, delegate { TogglePlay(); });

            var fm = new ToolStripMenuItem("Favoriler");
            fm.DropDown.Renderer = renderer;
            ((ToolStripDropDownMenu)fm.DropDown).ShowImageMargin = false;
            foreach (var s in favs)
            {
                var st = s;
                bool on = wantPlaying && current != null && current.Url == st.Url;
                fm.DropDownItems.Add((on ? "▶  " : "     ") + st.Name, null, delegate { PlayStation(st); });
            }
            fm.Enabled = favs.Count > 0;
            trayMenu.Items.Add(fm);

            var sm = new ToolStripMenuItem("Uyku zamanlayıcı");
            sm.DropDown.Renderer = renderer;
            ((ToolStripDropDownMenu)sm.DropDown).ShowImageMargin = false;
            AddSleepItems(sm.DropDownItems);
            trayMenu.Items.Add(sm);

            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(Visible ? "Gizle" : "Göster", null, delegate { if (Visible) HideToTray(); else ShowFromTray(); });
            trayMenu.Items.Add("Çıkış", null, delegate { Close(); });
        }

        void UpdateTray()
        {
            if (tray == null) return;
            string t = current == null ? "Ichi Radyo" : (wantPlaying ? "▶ " : "■ ") + current.Name + (song != "" ? " — " + song : "");
            if (t.Length > 63) t = t.Substring(0, 62) + "…";
            tray.Text = t;
        }

        void HideToTray()
        {
            Hide();
            anim.Stop();
        }

        void ShowFromTray()
        {
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        void SaveSettings()
        {
            var d = new Dictionary<string, string>();
            d["ses"] = volume.ToString();
            d["harita"] = map != null ? "1" : "0";
            if (WindowState == FormWindowState.Normal) { d["x"] = Left.ToString(); d["y"] = Top.ToString(); }
            if (current != null) d["son"] = current.Save();
            Store.SaveSettings(d);
        }

        // ---------- fare / klavye ----------

        PointF L(Point p) { return new PointF(p.X / K, p.Y / K); }

        string HitTest(PointF p)
        {
            if (rClose.Contains(p)) return "close";
            if (rMap.Contains(p)) return "map";
            if (rTray.Contains(p)) return "tray";
            if (rPlay.Contains(p)) return "play";
            if (rFav.Contains(p) && current != null) return "fav";
            if (rSpk.Contains(p)) return "spk";
            if (rVolHit.Contains(p)) return "vol";
            if (rTab0.Contains(p)) return "tab0";
            if (rTab1.Contains(p)) return "tab1";
            if (p.Y < 38) return "drag";
            return "";
        }

        void SetVolFromX(float x) { SetVolume((int)Math.Round((x - rVol.X) / rVol.Width * 100)); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var p = L(e.Location);
            if (dragVol) { SetVolFromX(p.X); return; }
            string h = HitTest(p);
            if (h == "drag") h = "";
            if (h != hover)
            {
                hover = h;
                Cursor = h != "" ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hover != "" && !dragVol) { hover = ""; Cursor = Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            var p = L(e.Location);
            string h = HitTest(p);
            if (e.Button == MouseButtons.Right)
            {
                if (h == "play")
                {
                    var m = new ContextMenuStrip { Renderer = renderer, ShowImageMargin = false };
                    AddSleepItems(m.Items);
                    m.Closed += delegate { BeginInvoke((MethodInvoker)delegate { m.Dispose(); }); };
                    m.Show(this, e.Location);
                }
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            switch (h)
            {
                case "drag":
                    Native.ReleaseCapture();
                    Native.SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
                    break;
                case "vol": dragVol = true; Capture = true; SetVolFromX(p.X); break;
                case "spk": ToggleMute(); break;
                case "play": TogglePlay(); break;
                case "fav": ToggleFav(current); break;
                case "tab0": SetTab(0); break;
                case "tab1": SetTab(1); break;
                case "tray": HideToTray(); break;
                case "map": ToggleMap(); break;
                case "close": Close(); break;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!dragVol) return;
            dragVol = false;
            Capture = false;
            SaveSettings();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.F)
            {
                SetTab(1);
                search.Focus();
                search.SelectAll();
                e.SuppressKeyPress = true;
                return;
            }
            if (search.Focused) return;
            switch (e.KeyCode)
            {
                case Keys.Space: TogglePlay(); e.SuppressKeyPress = true; break;
                case Keys.Escape: HideToTray(); break;
                case Keys.Add: case Keys.Oemplus: SetVolume(volume + 5); break;
                case Keys.Subtract: case Keys.OemMinus: SetVolume(volume - 5); break;
                case Keys.Right: SetVolume(volume + 1); e.Handled = true; break;
                case Keys.Left: SetVolume(volume - 1); e.Handled = true; break;
                case Keys.M: ToggleMute(); break;
            }
        }

        // ---------- çizim ----------

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.ScaleTransform(K, K);

            // başlık
            Tema.Text(g, "ICHI", fTitle, Tema.Cyan, new RectangleF(16, 0, 60, 38));
            float iw = g.MeasureString("ICHI", fTitle, PointF.Empty, StringFormat.GenericTypographic).Width;
            Tema.Text(g, "// RADYO", fTitle, Tema.Mag, new RectangleF(16 + iw + 6, 0, 120, 38));
            DrawTitleBtn(g, rTray, "tray");
            DrawTitleBtn(g, rMap, "map");
            DrawTitleBtn(g, rClose, "close");

            // şimdi çalıyor paneli
            using (var p = Tema.Round(rNow, 10))
            {
                using (var b = new LinearGradientBrush(rNow, Tema.Panel, Color.FromArgb(30, 14, 42), 35f)) g.FillPath(b, p);
                using (var pen = new Pen(Color.FromArgb(playing ? 150 : 55, Tema.Cyan), 1)) g.DrawPath(pen, p);
            }
            using (var pen = new Pen(Tema.Mag, 2)) g.DrawLine(pen, rNow.X + 16, rNow.Bottom, rNow.X + 64, rNow.Bottom);

            Tema.Text(g, "// YAYIN", fMono, Tema.Dim, new RectangleF(26, 50, 120, 20));
            Color sc = playing ? Tema.Mag : (wantPlaying ? Tema.Yel : Tema.Dim);
            Tema.Text(g, status, fMono, sc, new RectangleF(W - 26 - 210, 50, 210, 20), StringAlignment.Far);
            Tema.Text(g, current != null ? current.Name : "İstasyon seç", fStation, Tema.Txt, new RectangleF(24, 70, W - 48, 30));
            string line2 = song != "" ? song : (current == null ? "Favorilerden ya da KEŞFET'ten bir yayın seç" : "");
            Tema.Text(g, line2, fSong, song != "" ? Tema.Cyan : Tema.Dim, new RectangleF(26, 100, W - 52, 20));
            string info = current != null ? current.Sub() : "";
            if (sleepAt.HasValue)
            {
                int left = Math.Max(1, (int)Math.Ceiling((sleepAt.Value - DateTime.Now).TotalMinutes));
                info += (info != "" ? "  ·  " : "") + "UYKU " + left + "dk";
            }
            Tema.Text(g, info, fMono, sleepAt.HasValue ? Tema.Yel : Tema.Dim, new RectangleF(26, 126, W - 52 - 96, 28));
            DrawBars(g);

            // çal/durdur
            bool hp = hover == "play";
            if (playing || hp)
                for (int i = 3; i >= 1; i--)
                    using (var pen = new Pen(Color.FromArgb(28, Tema.Cyan), i * 3)) g.DrawEllipse(pen, rPlay);
            using (var b = new SolidBrush(hp ? Tema.Panel2 : Tema.Panel)) g.FillEllipse(b, rPlay);
            using (var pen = new Pen(Tema.Cyan, 2)) g.DrawEllipse(pen, rPlay);
            float cx = rPlay.X + rPlay.Width / 2, cy = rPlay.Y + rPlay.Height / 2;
            using (var b = new SolidBrush(Tema.Cyan))
            {
                if (wantPlaying) g.FillRectangle(b, cx - 7, cy - 7, 14, 14);
                else g.FillPolygon(b, new[] { new PointF(cx - 5, cy - 9), new PointF(cx - 5, cy + 9), new PointF(cx + 10, cy) });
            }

            // favori yıldızı
            bool isFav = IsFav(current);
            var star = StarPoints(rFav);
            if (isFav) using (var b = new SolidBrush(Tema.Yel)) g.FillPolygon(b, star);
            Color stc = current == null ? Tema.Panel2 : (isFav || hover == "fav" ? Tema.Yel : Tema.Dim);
            using (var pen = new Pen(stc, 1.6f)) { pen.LineJoin = LineJoin.Round; g.DrawPolygon(pen, star); }

            // ses
            DrawSpeaker(g);
            Tema.Text(g, "SES", fMono, Tema.Dim, new RectangleF(rVol.X, 180, 60, 16));
            Tema.Text(g, muted ? "SESSİZ" : "%" + volume, fTab, muted ? Tema.Mag : Tema.Txt, new RectangleF(rVol.Right + 8, 188, 56, 18));
            Tema.Text(g, DbText(), fMono, Tema.Dim, new RectangleF(rVol.Right + 8, 205, 56, 16));
            using (var p = Tema.Round(rVol, 3)) using (var b = new SolidBrush(Tema.Panel2)) g.FillPath(b, p);
            float fw = rVol.Width * volume / 100f;
            if (fw > 0.5f)
            {
                var fr = new RectangleF(rVol.X, rVol.Y, fw, rVol.Height);
                Color c1 = muted ? Tema.Dim : Tema.Cyan, c2 = muted ? Tema.Panel2 : Tema.Mag;
                using (var b = new LinearGradientBrush(new RectangleF(rVol.X - 1, rVol.Y, rVol.Width + 2, rVol.Height), c1, c2, 0f))
                {
                    if (fw >= 6) using (var p = Tema.Round(fr, 3)) g.FillPath(b, p);
                    else g.FillRectangle(b, fr);
                }
            }
            float kx = rVol.X + fw, ky = rVol.Y + rVol.Height / 2;
            if (hover == "vol" || dragVol) using (var b = new SolidBrush(Color.FromArgb(60, Tema.Cyan))) g.FillEllipse(b, kx - 11, ky - 11, 22, 22);
            using (var b = new SolidBrush(Tema.Txt)) g.FillEllipse(b, kx - 7, ky - 7, 14, 14);

            // sekmeler
            DrawTab(g, rTab0, "FAVORİLER  " + favs.Count, tab == 0, hover == "tab0");
            DrawTab(g, rTab1, exploreLabel != "" ? "KEŞFET · " + exploreLabel.ToUpper(new System.Globalization.CultureInfo("tr-TR")) : "KEŞFET", tab == 1, hover == "tab1");

            if (tab == 1)
                using (var p = Tema.Round(rSearch, 6))
                {
                    using (var b = new SolidBrush(Tema.Panel)) g.FillPath(b, p);
                    using (var pen = new Pen(search.Focused ? Color.FromArgb(180, Tema.Cyan) : Tema.Panel2, 1)) g.DrawPath(pen, p);
                }

            var lr = rList;
            using (var p = Tema.Round(lr, 8)) using (var b = new SolidBrush(Tema.Panel)) g.FillPath(b, p);
            if (!list.Visible)
            {
                string msg = tab == 0 ? "Henüz favori yok.\nKEŞFET'ten bir istasyon seçip ★ ile ekle." : exploreMsg;
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisWord })
                using (var b = new SolidBrush(Tema.Dim))
                    g.DrawString(msg, fItem, b, RectangleF.Inflate(lr, -20, -20), sf);
            }

            g.ResetTransform();
            using (var pen = new Pen(Color.FromArgb(70, Tema.Cyan), 1)) g.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        void DrawTitleBtn(Graphics g, RectangleF r, string id)
        {
            bool h = hover == id;
            if (h) using (var b = new SolidBrush(id == "close" ? Color.FromArgb(190, 200, 30, 70) : Tema.Panel2)) g.FillRectangle(b, r);
            using (var p = new Pen(h ? Tema.Txt : Tema.Dim, 1.4f))
            {
                float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
                if (id == "close") { g.DrawLine(p, cx - 5, cy - 5, cx + 5, cy + 5); g.DrawLine(p, cx + 5, cy - 5, cx - 5, cy + 5); }
                else if (id == "map")
                {
                    if (!h) p.Color = Tema.Cyan;
                    g.DrawEllipse(p, cx - 7, cy - 7, 14, 14);
                    g.DrawEllipse(p, cx - 3, cy - 7, 6, 14);
                    g.DrawLine(p, cx - 7, cy, cx + 7, cy);
                }
                else g.DrawLine(p, cx - 6, cy + 4, cx + 6, cy + 4);
            }
        }

        string DbText()
        {
            float gain = Player.Gain(volume);
            if (gain <= 0) return "-∞ dB";
            return Math.Round(20 * Math.Log10(gain)).ToString("0") + " dB";
        }

        void DrawSpeaker(Graphics g)
        {
            bool h = hover == "spk";
            Color c = muted ? Tema.Mag : (h ? Tema.Txt : Tema.Dim);
            float x = rSpk.X, cy = rSpk.Y + rSpk.Height / 2;
            using (var b = new SolidBrush(c))
                g.FillPolygon(b, new[] {
                    new PointF(x + 3, cy - 3.5f), new PointF(x + 8, cy - 3.5f), new PointF(x + 13, cy - 8),
                    new PointF(x + 13, cy + 8), new PointF(x + 8, cy + 3.5f), new PointF(x + 3, cy + 3.5f) });
            using (var p = new Pen(c, 1.6f))
            {
                p.StartCap = p.EndCap = LineCap.Round;
                if (muted)
                {
                    g.DrawLine(p, x + 17, cy - 4, x + 24, cy + 4);
                    g.DrawLine(p, x + 24, cy - 4, x + 17, cy + 4);
                }
                else
                {
                    if (volume > 0) g.DrawArc(p, x + 9, cy - 5, 10, 10, -50, 100);
                    if (volume > 50) g.DrawArc(p, x + 7, cy - 9, 18, 18, -50, 100);
                }
            }
        }

        void DrawTab(Graphics g, RectangleF r, string text, bool active, bool hov)
        {
            Tema.Text(g, text, fTab, active || hov ? Tema.Txt : Tema.Dim, r, StringAlignment.Center);
            using (var p = new Pen(active ? Tema.Cyan : Tema.Panel2, active ? 2 : 1))
                g.DrawLine(p, r.X, r.Bottom - 1, r.Right, r.Bottom - 1);
        }

        void DrawBars(Graphics g)
        {
            float bw = rBars.Width / bars.Length;
            using (var b = new LinearGradientBrush(rBars, Tema.Mag, Tema.Cyan, 90f))
            using (var dim = new SolidBrush(Tema.Panel2))
                for (int i = 0; i < bars.Length; i++)
                {
                    float h = Math.Max(2, bars[i] * rBars.Height);
                    g.FillRectangle(playing ? (Brush)b : dim, rBars.X + i * bw + 1, rBars.Bottom - h, bw - 2, h);
                }
        }

        static PointF[] StarPoints(RectangleF r)
        {
            var pts = new PointF[10];
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2 + 1, R = r.Width / 2, rr = R * 0.45f;
            for (int i = 0; i < 10; i++)
            {
                double a = -Math.PI / 2 + i * Math.PI / 5;
                float rad = i % 2 == 0 ? R : rr;
                pts[i] = new PointF(cx + (float)(Math.Cos(a) * rad), cy + (float)(Math.Sin(a) * rad));
            }
            return pts;
        }

        void DrawItem(object sender, DrawItemEventArgs e) { DrawStationItem(list, e); }

        // Ana liste ve harita penceresindeki liste aynı görünümü kullanır.
        public void DrawStationItem(ListBox lb, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= lb.Items.Count) return;
            var s = lb.Items[e.Index] as Station;
            if (s == null) return;
            var g = e.Graphics;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(sel ? Tema.Panel2 : Tema.Panel)) g.FillRectangle(b, e.Bounds);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            var gs = g.Save();
            g.TranslateTransform(e.Bounds.X, e.Bounds.Y);
            g.ScaleTransform(K, K);
            float w = e.Bounds.Width / K;
            bool isCur = current != null && current.Url == s.Url;
            if (isCur) using (var b = new SolidBrush(Tema.Mag)) g.FillRectangle(b, 0, 8, 3, 24);
            Tema.Text(g, s.Name, fItem, isCur ? Tema.Mag : Tema.Txt, new RectangleF(12, 3, w - 44, 20));
            Tema.Text(g, s.Sub(), fSmall, Tema.Dim, new RectangleF(12, 21, w - 44, 16));
            if (IsFav(s)) Tema.Text(g, "★", fItem, Tema.Yel, new RectangleF(w - 34, 0, 26, 40), StringAlignment.Center);
            g.Restore(gs);
        }

        // ---------- yardımcılar ----------

        int S(float v) { return (int)Math.Round(v * K); }

        Rectangle Sc(RectangleF r)
        {
            return Rectangle.FromLTRB((int)Math.Floor(r.Left * K), (int)Math.Floor(r.Top * K), (int)Math.Ceiling(r.Right * K), (int)Math.Ceiling(r.Bottom * K));
        }

        static int GetInt(Dictionary<string, string> d, string k, int def)
        {
            string v;
            int n;
            return d.TryGetValue(k, out v) && int.TryParse(v, out n) ? n : def;
        }

        static bool OnScreen(int x, int y)
        {
            if (x == int.MinValue || y == int.MinValue) return false;
            foreach (var s in Screen.AllScreens)
                if (s.WorkingArea.Contains(x + 60, y + 12)) return true;
            return false;
        }

        static bool HasFont(string name)
        {
            using (var f = new Font(name, 10)) return f.Name == name;
        }

        public static Icon MakeIcon(int px)
        {
            var bmp = new Bitmap(px, px);
            using (var g = Graphics.FromImage(bmp)) Tema.Logo(g, px);
            return Icon.FromHandle(bmp.GetHicon());
        }
    }

    class Country
    {
        public string Iso, Name;
        public GraphicsPath Path;
        public RectangleF Bounds, Main; // Main: en büyük parçanın sınırı (yakınlaştırma için)
        public int Count;
    }

    static class MapData
    {
        // Miller silindirik projeksiyon. Dünya birimi ≈ derece; kuzey yukarıda (y aşağı doğru artar).
        public static PointF Project(double lon, double lat)
        {
            lat = Math.Max(-85, Math.Min(85, lat));
            double phi = lat * Math.PI / 180;
            double y = 1.25 * Math.Log(Math.Tan(Math.PI / 4 + 0.4 * phi));
            return new PointF((float)lon, (float)(-y * 180 / Math.PI));
        }

        // harita.bin: harita-hazirla.js'in ürettiği, exe'ye gömülü ülke sınırları.
        public static List<Country> Load()
        {
            var res = new List<Country>();
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("harita.bin"))
            {
                if (s == null) return res;
                using (var r = new BinaryReader(s, Encoding.UTF8))
                {
                    int n = r.ReadUInt16();
                    for (int i = 0; i < n; i++)
                    {
                        var c = new Country { Iso = r.ReadString(), Name = r.ReadString(), Path = new GraphicsPath(FillMode.Winding) };
                        int rings = r.ReadUInt16();
                        float best = -1;
                        for (int k = 0; k < rings; k++)
                        {
                            int m = r.ReadUInt16();
                            var pts = new PointF[m];
                            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                            for (int j = 0; j < m; j++)
                            {
                                short x = r.ReadInt16(), y = r.ReadInt16();
                                var p = Project(x / 100.0, y / 100.0);
                                pts[j] = p;
                                x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y);
                            }
                            c.Path.AddPolygon(pts);
                            float area = (x1 - x0) * (y1 - y0);
                            if (area > best) { best = area; c.Main = RectangleF.FromLTRB(x0, y0, x1, y1); }
                        }
                        c.Bounds = c.Path.GetBounds();
                        res.Add(c);
                    }
                }
            }
            return res;
        }
    }

    // Ana pencerenin sağında açılan harita paneli. Kapatılınca Dispose edilir, belleği bırakır.
    class MapView : Control
    {
        static readonly RectangleF World = RectangleF.FromLTRB(-180, MapData.Project(0, 84).Y, 180, MapData.Project(0, -58).Y);
        static readonly Color Ocean = Color.FromArgb(7, 7, 14);

        public event Action<Country> CountryPicked;
        public event Action<Station> StationPicked;
        public Station Highlight; // listede seçili istasyon

        readonly MainForm main;
        readonly float K;
        readonly List<Country> countries;
        readonly Font fMono, fName, fSmall, fTip;

        Country hover, selected;
        List<Station> stations = new List<Station>();
        Station hoverSt;
        float zoom, minZoom, layerZoom;
        PointF center, layerCenter;
        Bitmap layer;
        bool dirty = true, panning, moved, fitted;
        Point panStart, mouse;
        PointF panCenter;
        string hoverBtn = "";
        int withStations;

        public MapView(MainForm main, float k, string ff)
        {
            this.main = main;
            K = k;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Ocean;
            fMono = new Font("Consolas", 11, FontStyle.Regular, GraphicsUnit.Pixel);
            fName = new Font(ff, 20, FontStyle.Bold, GraphicsUnit.Pixel);
            fSmall = new Font(ff, 12, FontStyle.Regular, GraphicsUnit.Pixel);
            fTip = new Font(ff, 13, FontStyle.Bold, GraphicsUnit.Pixel);
            countries = MapData.Load();
            LoadCounts();
        }

        public void SetStations(List<Station> l)
        {
            stations = l ?? new List<Station>();
            hoverSt = null;
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (layer != null) layer.Dispose();
                foreach (var c in countries) c.Path.Dispose();
                countries.Clear();
                fMono.Dispose(); fName.Dispose(); fSmall.Dispose(); fTip.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (Width <= 0 || Height <= 0) return;
            if (!fitted) { FitWorld(); fitted = true; }
            using (var p = Tema.Round(new RectangleF(0, 0, Width, Height), 10 * K)) Region = new Region(p);
            dirty = true;
            Invalidate();
        }

        // ---------- görünüm ----------

        float LW { get { return Width / K; } }
        float LH { get { return Height / K; } }
        RectangleF rZin { get { return new RectangleF(LW - 46, LH - 128, 34, 34); } }
        RectangleF rZout { get { return new RectangleF(LW - 46, LH - 90, 34, 34); } }
        RectangleF rHome { get { return new RectangleF(LW - 46, LH - 52, 34, 34); } }

        void FitWorld()
        {
            zoom = minZoom = Math.Min(Width / World.Width, Height / World.Height);
            center = new PointF(World.X + World.Width / 2, World.Y + World.Height / 2);
            dirty = true;
        }

        void ClampCenter()
        {
            center = new PointF(Math.Max(World.Left, Math.Min(World.Right, center.X)), Math.Max(World.Top, Math.Min(World.Bottom, center.Y)));
        }

        Matrix View(PointF c)
        {
            return new Matrix(zoom, 0, 0, zoom, Width / 2f - c.X * zoom, Height / 2f - c.Y * zoom);
        }

        PointF ToScreen(PointF w) { return new PointF(Width / 2f + (w.X - center.X) * zoom, Height / 2f + (w.Y - center.Y) * zoom); }
        PointF ToWorld(Point p) { return new PointF(center.X + (p.X - Width / 2f) / zoom, center.Y + (p.Y - Height / 2f) / zoom); }
        Point Middle { get { return new Point(Width / 2, Height / 2); } }

        void ZoomAt(Point p, float f)
        {
            var before = ToWorld(p);
            zoom = Math.Max(minZoom, Math.Min(minZoom * 80, zoom * f));
            var after = ToWorld(p);
            center = new PointF(center.X + before.X - after.X, center.Y + before.Y - after.Y);
            ClampCenter();
            dirty = true;
            hoverSt = null;
            Invalidate();
        }

        Country CountryAt(Point p)
        {
            var w = ToWorld(p);
            Country best = null;
            float bestArea = float.MaxValue;
            // İç içe ülkelerde (ör. Lesotho) en küçüğü seçilsin.
            foreach (var c in countries)
            {
                if (!c.Bounds.Contains(w) || !c.Path.IsVisible(w)) continue;
                float a = c.Bounds.Width * c.Bounds.Height;
                if (a < bestArea) { bestArea = a; best = c; }
            }
            return best;
        }

        static Color Lerp(Color a, Color b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        // İstasyon sayısına göre renk: koyu lacivert → camgöbeği → magenta (log ölçek).
        static Color FillFor(int count)
        {
            if (count <= 0) return Color.FromArgb(24, 24, 42);
            double t = Math.Min(1, Math.Log10(count + 1) / 3.6);
            Color a = Color.FromArgb(20, 44, 70), b = Color.FromArgb(0, 128, 148), m = Color.FromArgb(150, 30, 130);
            return t < 0.65 ? Lerp(a, b, t / 0.65) : Lerp(b, m, (t - 0.65) / 0.35);
        }

        // ---------- veri ----------

        void LoadCounts()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                Dictionary<string, int> d = null;
                try { d = Api.CountryCounts(); } catch { }
                if (d == null) return;
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (IsDisposed) return;
                        withStations = 0;
                        foreach (var c in countries)
                        {
                            int n;
                            c.Count = d.TryGetValue(c.Iso, out n) ? n : 0;
                            if (c.Count > 0) withStations++;
                        }
                        dirty = true;
                        Invalidate();
                    });
                }
                catch { }
            });
        }

        void SelectCountry(Country c)
        {
            selected = c;
            var b = c.Main;
            float z = Math.Min(Width / (b.Width * 1.6f + 2f), Height / (b.Height * 1.6f + 2f));
            zoom = Math.Max(minZoom, Math.Min(minZoom * 80, z));
            center = new PointF(b.X + b.Width / 2, b.Y + b.Height / 2);
            ClampCenter();
            dirty = true;
            hover = null;
            hoverSt = null;
            Invalidate();
            if (CountryPicked != null) CountryPicked(c);
        }

        // Arama yapılınca ülke seçimi kalkar (noktalar dünyaya yayılabilir).
        public void ClearCountry()
        {
            if (selected == null) return;
            selected = null;
            Invalidate();
        }

        // ---------- fare ----------

        PointF L(Point p) { return new PointF(p.X / K, p.Y / K); }

        string BtnAt(PointF lp)
        {
            if (rZin.Contains(lp)) return "zin";
            if (rZout.Contains(lp)) return "zout";
            if (rHome.Contains(lp)) return "home";
            return "";
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (e.Button != MouseButtons.Left) return;
            switch (BtnAt(L(e.Location)))
            {
                case "zin": ZoomAt(Middle, 1.6f); return;
                case "zout": ZoomAt(Middle, 1 / 1.6f); return;
                case "home": FitWorld(); Invalidate(); return;
            }
            panning = true;
            moved = false;
            panStart = e.Location;
            panCenter = center;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            mouse = e.Location;
            if (panning)
            {
                int dx = e.X - panStart.X, dy = e.Y - panStart.Y;
                if (!moved && Math.Abs(dx) + Math.Abs(dy) > 4) moved = true;
                if (moved)
                {
                    center = new PointF(panCenter.X - dx / zoom, panCenter.Y - dy / zoom);
                    ClampCenter();
                    Invalidate(); // sürüklerken hazır katman kaydırılır, yeniden çizilmez
                }
                return;
            }
            UpdateHover(e.Location);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!panning) return;
            panning = false;
            Capture = false;
            if (moved) { dirty = true; Invalidate(); return; }
            if (hoverSt != null)
            {
                if (StationPicked != null) StationPicked(hoverSt);
                Invalidate();
                return;
            }
            var c = CountryAt(e.Location);
            if (c != null) SelectCountry(c);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hover != null || hoverSt != null || hoverBtn != "")
            {
                hover = null; hoverSt = null; hoverBtn = "";
                Cursor = Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ZoomAt(e.Location, (float)Math.Pow(1.3, e.Delta / 120.0));
            var he = e as HandledMouseEventArgs;
            if (he != null) he.Handled = true; // tekerlek ana pencereye geçmesin
        }

        void UpdateHover(Point p)
        {
            string hb = BtnAt(L(p));
            Station hs = null;
            Country hc = null;
            if (hb == "")
            {
                float best = 9 * K;
                foreach (var s in stations)
                {
                    if (!s.HasGeo) continue;
                    var sp = ToScreen(MapData.Project(s.Lon, s.Lat));
                    float d = (float)Math.Sqrt((sp.X - p.X) * (sp.X - p.X) + (sp.Y - p.Y) * (sp.Y - p.Y));
                    if (d < best) { best = d; hs = s; }
                }
                if (hs == null) hc = CountryAt(p);
            }
            hoverSt = hs; hover = hc; hoverBtn = hb;
            Cursor = hs != null || hc != null || hb != "" ? Cursors.Hand : Cursors.Default;
            Invalidate(); // ipucu fareyi izler
        }

        // ---------- çizim ----------

        void RenderLayer()
        {
            if (Width <= 0 || Height <= 0) return;
            if (layer == null || layer.Width != Width || layer.Height != Height)
            {
                if (layer != null) layer.Dispose();
                layer = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
            }
            using (var g = Graphics.FromImage(layer))
            {
                g.Clear(Ocean);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var m = View(center)) g.Transform = m;
                var vis = new RectangleF(center.X - Width / 2f / zoom, center.Y - Height / 2f / zoom, Width / zoom, Height / zoom);
                using (var grid = new Pen(Color.FromArgb(26, 0, 240, 255), 1f / zoom))
                {
                    for (int lon = -180; lon <= 180; lon += 30) g.DrawLine(grid, lon, World.Top, lon, World.Bottom);
                    for (int lat = -40; lat <= 80; lat += 20) { float y = MapData.Project(0, lat).Y; g.DrawLine(grid, -180, y, 180, y); }
                }
                using (var edge = new Pen(Color.FromArgb(0, 150, 170), 1f / zoom))
                    foreach (var c in countries)
                    {
                        if (!c.Bounds.IntersectsWith(vis)) continue;
                        using (var b = new SolidBrush(FillFor(c.Count))) g.FillPath(b, c.Path);
                        g.DrawPath(edge, c.Path);
                    }
            }
            layerCenter = center;
            layerZoom = zoom;
            dirty = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            if ((dirty && !panning) || layer == null || layerZoom != zoom) RenderLayer();
            float dx = (layerCenter.X - center.X) * zoom, dy = (layerCenter.Y - center.Y) * zoom;
            if (dx != 0 || dy != 0) g.Clear(Ocean);
            if (layer != null) g.DrawImageUnscaled(layer, (int)Math.Round(dx), (int)Math.Round(dy));

            using (var m = View(center)) g.Transform = m;
            if (selected != null) using (var p = new Pen(Tema.Mag, 2.2f * K / zoom)) g.DrawPath(p, selected.Path);
            if (hover != null && hover != selected) using (var p = new Pen(Tema.Txt, 1.5f * K / zoom)) g.DrawPath(p, hover.Path);
            g.ResetTransform();

            DrawDots(g);

            g.ScaleTransform(K, K);
            DrawHeader(g);
            DrawLegend(g);
            DrawBtn(g, rZin, "zin");
            DrawBtn(g, rZout, "zout");
            DrawBtn(g, rHome, "home");
            DrawTooltip(g);
            g.ResetTransform();

            using (var p = Tema.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 10 * K))
            using (var pen = new Pen(Color.FromArgb(90, Tema.Cyan), 1))
                g.DrawPath(pen, p);
        }

        void DrawDots(Graphics g)
        {
            float r = 3.2f * K;
            using (var glow = new SolidBrush(Color.FromArgb(60, Tema.Mag)))
            using (var dot = new SolidBrush(Tema.Mag))
                foreach (var s in stations)
                {
                    if (!s.HasGeo) continue;
                    var p = ToScreen(MapData.Project(s.Lon, s.Lat));
                    if (p.X < -10 || p.X > Width + 10 || p.Y < -10 || p.Y > Height + 10) continue;
                    g.FillEllipse(glow, p.X - r * 2, p.Y - r * 2, r * 4, r * 4);
                    g.FillEllipse(dot, p.X - r, p.Y - r, r * 2, r * 2);
                }
            foreach (var s in stations)
            {
                if (!s.HasGeo) continue;
                Color c;
                if (main.IsCurrent(s)) c = Tema.Cyan;
                else if (s == hoverSt) c = Tema.Txt;
                else if (s == Highlight) c = Tema.Yel;
                else continue;
                var p = ToScreen(MapData.Project(s.Lon, s.Lat));
                using (var pen = new Pen(c, 2 * K)) g.DrawEllipse(pen, p.X - r * 2.6f, p.Y - r * 2.6f, r * 5.2f, r * 5.2f);
            }
        }

        void DrawHeader(Graphics g)
        {
            string title = selected != null ? selected.Name : "DÜNYA";
            string sub;
            int geo = 0;
            foreach (var s in stations) if (s.HasGeo) geo++;
            if (selected != null) sub = (selected.Count > 0 ? selected.Count + " istasyon kayıtlı" : "kayıtlı istasyon yok") + (geo > 0 ? "  ·  " + geo + " haritada" : "");
            else if (stations.Count > 0) sub = "arama sonucu  ·  " + geo + " haritada";
            else sub = withStations > 0 ? withStations + " ülkede radyo var  ·  bir ülkeye tıkla" : "ülkeler yükleniyor…";
            float w = Math.Max(g.MeasureString(title, fName).Width, g.MeasureString(sub, fSmall).Width) + 24;
            var r = new RectangleF(10, 10, Math.Min(w, LW - 70), 54);
            using (var p = Tema.Round(r, 8)) using (var b = new SolidBrush(Color.FromArgb(215, 10, 10, 18))) g.FillPath(b, p);
            Tema.Text(g, title, fName, Tema.Txt, new RectangleF(r.X + 12, r.Y + 4, r.Width - 16, 26));
            Tema.Text(g, sub, fSmall, Tema.Dim, new RectangleF(r.X + 12, r.Y + 30, r.Width - 16, 18));
        }

        void DrawTooltip(Graphics g)
        {
            string text;
            if (hoverSt != null) text = hoverSt.Name + "\n" + hoverSt.Sub() + (main.IsPlaying(hoverSt) ? "  ·  ÇALIYOR" : "  ·  tıkla: çal");
            else if (hover != null) text = hover.Name + "\n" + (hover.Count > 0 ? hover.Count + " istasyon" : "istasyon yok");
            else return;
            var lp = L(mouse);
            var sz = g.MeasureString(text, fTip);
            var r = new RectangleF(lp.X + 16, lp.Y + 14, sz.Width + 16, sz.Height + 10);
            if (r.Right > LW - 6) r.X = lp.X - r.Width - 10;
            if (r.Bottom > LH - 6) r.Y = lp.Y - r.Height - 10;
            using (var p = Tema.Round(r, 6))
            {
                using (var b = new SolidBrush(Color.FromArgb(235, 20, 20, 36))) g.FillPath(b, p);
                using (var pen = new Pen(Color.FromArgb(160, Tema.Cyan))) g.DrawPath(pen, p);
            }
            using (var b = new SolidBrush(Tema.Txt)) g.DrawString(text, fTip, b, r.X + 8, r.Y + 5);
        }

        void DrawLegend(Graphics g)
        {
            var bar = new RectangleF(16, LH - 30, 150, 8);
            Tema.Text(g, "İSTASYON SAYISI", fMono, Tema.Dim, new RectangleF(16, LH - 50, 150, 16));
            int steps = 30;
            float sw = bar.Width / steps;
            for (int i = 0; i < steps; i++)
            {
                int count = (int)Math.Pow(10, 3.6 * (i + 1) / steps);
                using (var b = new SolidBrush(FillFor(count))) g.FillRectangle(b, bar.X + i * sw, bar.Y, sw + 0.5f, bar.Height);
            }
            Tema.Text(g, "1", fMono, Tema.Dim, new RectangleF(bar.X, bar.Bottom + 1, 40, 14));
            Tema.Text(g, "4000+", fMono, Tema.Dim, new RectangleF(bar.Right - 60, bar.Bottom + 1, 60, 14), StringAlignment.Far);
            Tema.Text(g, "tekerlek: yakınlaştır  ·  sürükle: kaydır", fMono, Tema.Dim, new RectangleF(bar.Right + 20, LH - 26, LW - bar.Right - 90, 16));
        }

        void DrawBtn(Graphics g, RectangleF r, string id)
        {
            bool h = hoverBtn == id;
            using (var p = Tema.Round(r, 6))
            {
                using (var b = new SolidBrush(h ? Tema.Panel2 : Color.FromArgb(220, 19, 19, 34))) g.FillPath(b, p);
                using (var pen = new Pen(Color.FromArgb(h ? 200 : 110, Tema.Cyan))) g.DrawPath(pen, p);
            }
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            using (var pen = new Pen(h ? Tema.Txt : Tema.Cyan, 1.8f))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                if (id != "home") g.DrawLine(pen, cx - 6, cy, cx + 6, cy);
                if (id == "zin") g.DrawLine(pen, cx, cy - 6, cx, cy + 6);
                if (id == "home")
                {
                    g.DrawEllipse(pen, cx - 7, cy - 7, 14, 14);
                    g.DrawEllipse(pen, cx - 3, cy - 7, 6, 14);
                    g.DrawLine(pen, cx - 7, cy, cx + 7, cy);
                }
            }
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--ikon") { WriteIco(args[1]); return; }

            bool created;
            using (var ev = new EventWaitHandle(false, EventResetMode.AutoReset, "IchiRadyo_Goster", out created))
            {
                if (!created) { ev.Set(); return; }
                Native.SetProcessDPIAware();
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                var form = new MainForm();
                form.ListenForShow(ev);
                Application.Run(form);
            }
        }

        // Derlemede exe ikonu üretmek için: her boyut PNG olarak .ico içine.
        static void WriteIco(string path)
        {
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };
            var pngs = new List<byte[]>();
            foreach (int s in sizes)
                using (var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bmp)) { g.Clear(Color.Transparent); Tema.Logo(g, s); }
                    using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); pngs.Add(ms.ToArray()); }
                }
            using (var fs = File.Create(path))
            using (var w = new BinaryWriter(fs))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int off = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    byte b = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                    w.Write(b); w.Write(b); w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32);
                    w.Write(pngs[i].Length); w.Write(off);
                    off += pngs[i].Length;
                }
                foreach (var p in pngs) w.Write(p);
            }
        }
    }
}

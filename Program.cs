using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Accessibility;

namespace Win10EndTask
{
    static class Program
    {
        private static volatile bool _running = true;
        private static Thread _inputThread;
        private static SynchronizationContext _syncContext;
        private static NotifyIcon _tray;
        private static EndTaskFloatingBar _activeBar;

        [STAThread]
        static void Main()
        {
            EnableDebugPrivilege();

            bool createdNew;
            using (var mutex = new Mutex(true, "Win10EndTask_Mutex", out createdNew))
            {
                if (!createdNew) return;

                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => { };

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                _syncContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

                _tray = new NotifyIcon
                {
                    Icon = SystemIcons.Shield,
                    Text = "Win10 End Task Bar",
                    Visible = true
                };

                var trayMenu = new ContextMenuStrip();
                var title = new ToolStripMenuItem("Win10 End Task Bar") { Enabled = false, Font = new Font(Control.DefaultFont, FontStyle.Bold) };
                trayMenu.Items.Add(title);
                trayMenu.Items.Add(new ToolStripSeparator());

                var startupItem = new ToolStripMenuItem("Start with Windows") { Checked = IsStartupEnabled() };
                startupItem.Click += (s, e) =>
                {
                    bool enable = !startupItem.Checked;
                    SetStartup(enable);
                    startupItem.Checked = enable;
                };
                trayMenu.Items.Add(startupItem);

                trayMenu.Items.Add(new ToolStripSeparator());
                trayMenu.Items.Add("Exit", null, (s, e) =>
                {
                    _running = false;
                    _tray.Visible = false;
                    _tray.Dispose();
                    Application.Exit();
                });
                _tray.ContextMenuStrip = trayMenu;

                StartInputPoller();

                Application.Run();

                _running = false;
            }
        }

        public static bool IsAdministrator()
        {
            try
            {
                var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        private static void EnableDebugPrivilege()
        {
            try
            {
                IntPtr hToken;
                if (OpenProcessToken(Process.GetCurrentProcess().Handle, 0x0020 | 0x0008, out hToken))
                {
                    TOKEN_PRIVILEGES tp = new TOKEN_PRIVILEGES
                    {
                        PrivilegeCount = 1,
                        Attributes = 0x00000002 // SE_PRIVILEGE_ENABLED
                    };
                    if (LookupPrivilegeValue(null, "SeDebugPrivilege", out tp.Luid))
                    {
                        AdjustTokenPrivileges(hToken, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
                    }
                    CloseHandle(hToken);
                }
            }
            catch { }
        }

        private static void StartInputPoller()
        {
            _inputThread = new Thread(() =>
            {
                bool wasRDown = false;
                while (_running)
                {
                    try
                    {
                        short rState = GetAsyncKeyState(0x02); // VK_RBUTTON
                        bool isRDown = (rState & 0x8000) != 0;

                        if (isRDown && !wasRDown)
                        {
                            if ((GetAsyncKeyState(0x10) & 0x8000) == 0) // Shift not held
                            {
                                Point pt;
                                if (GetCursorPos(out pt))
                                {
                                    var screen = Screen.FromPoint(pt);
                                    if (!screen.WorkingArea.Contains(pt))
                                    {
                                        if (_syncContext != null)
                                        {
                                            _syncContext.Post(s => ResolveAndShow(pt), null);
                                        }
                                    }
                                    else
                                    {
                                        DismissActiveBar();
                                    }
                                }
                            }
                        }
                        wasRDown = isRDown;

                        short lState = GetAsyncKeyState(0x01); // VK_LBUTTON
                        bool isLDown = (lState & 0x8000) != 0;
                        if (isLDown && _activeBar != null && !_activeBar.IsDisposed)
                        {
                            Point pt;
                            if (GetCursorPos(out pt) && !_activeBar.Bounds.Contains(pt))
                            {
                                DismissActiveBar();
                            }
                        }
                    }
                    catch { }

                    Thread.Sleep(5);
                }
            });
            _inputThread.IsBackground = true;
            _inputThread.Priority = ThreadPriority.AboveNormal;
            _inputThread.Start();
        }

        private static void DismissActiveBar()
        {
            if (_activeBar != null && !_activeBar.IsDisposed && _syncContext != null)
            {
                _syncContext.Post(s =>
                {
                    if (_activeBar != null && !_activeBar.IsDisposed)
                        _activeBar.Close();
                }, null);
            }
        }

        private static bool IsStartupEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false))
                {
                    return key != null && key.GetValue("Win10EndTask") != null;
                }
            }
            catch { return false; }
        }

        private static void SetStartup(bool enable)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key != null)
                    {
                        if (enable)
                            key.SetValue("Win10EndTask", "\"" + Application.ExecutablePath + "\"");
                        else
                            key.DeleteValue("Win10EndTask", false);
                    }
                }
            }
            catch { }
        }

        private static void ResolveAndShow(Point pt)
        {
            try
            {
                object accObj, varChild;
                if (AccessibleObjectFromPoint(pt, out accObj, out varChild) != 0 || accObj == null)
                    return;

                var acc = (IAccessible)accObj;
                string name = null;
                int bx = pt.X, by = pt.Y, bw = 40, bh = 40;

                object hit = null;
                try { hit = acc.accHitTest(pt.X, pt.Y); } catch { }

                if (hit is int && (int)hit != 0)
                {
                    int cId = (int)hit;
                    try { name = acc.get_accName(cId); } catch { }
                    try { acc.accLocation(out bx, out by, out bw, out bh, cId); } catch { }
                }
                else if (hit is IAccessible)
                {
                    var hitAcc = (IAccessible)hit;
                    try { name = hitAcc.get_accName(0); } catch { }
                    try { hitAcc.accLocation(out bx, out by, out bw, out bh, 0); } catch { }
                    Marshal.ReleaseComObject(hit);
                }
                else
                {
                    try { name = acc.get_accName(varChild ?? 0); } catch { }
                    try { acc.accLocation(out bx, out by, out bw, out bh, varChild ?? 0); } catch { }
                }

                if (string.IsNullOrEmpty(name) || name == "Running applications")
                {
                    var screen = Screen.FromPoint(pt);
                    var midPt = new Point(pt.X, (screen.WorkingArea.Bottom + screen.Bounds.Bottom) / 2);
                    object midAccObj, midVarChild;
                    if (AccessibleObjectFromPoint(midPt, out midAccObj, out midVarChild) == 0 && midAccObj != null)
                    {
                        var midAcc = (IAccessible)midAccObj;
                        object midHit = null;
                        try { midHit = midAcc.accHitTest(midPt.X, midPt.Y); } catch { }

                        if (midHit is int && (int)midHit != 0)
                        {
                            try { name = midAcc.get_accName((int)midHit); } catch { }
                            try { midAcc.accLocation(out bx, out by, out bw, out bh, (int)midHit); } catch { }
                        }
                        else
                        {
                            try { name = midAcc.get_accName(midVarChild ?? 0); } catch { }
                            try { midAcc.accLocation(out bx, out by, out bw, out bh, midVarChild ?? 0); } catch { }
                        }
                        Marshal.ReleaseComObject(midAccObj);
                    }
                }

                Marshal.ReleaseComObject(accObj);

                if (!string.IsNullOrEmpty(name) && name != "Running applications")
                {
                    int pid = ProcessResolver.FindPid(name);
                    if (pid > 0 && pid != Process.GetCurrentProcess().Id)
                    {
                        if (_activeBar != null && !_activeBar.IsDisposed)
                            _activeBar.Close();

                        _activeBar = new EndTaskFloatingBar(pid, name, new Rectangle(bx, by, bw, bh));
                        _activeBar.Show();
                    }
                }
            }
            catch { }
        }

        [StructLayout(LayoutKind.Sequential)] private struct LUID { public uint LowPart; public int HighPart; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct TOKEN_PRIVILEGES { public uint PrivilegeCount; public LUID Luid; public uint Attributes; }

        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)] private static extern bool LookupPrivilegeValue(string lpSystemName, string lpName, out LUID lpLuid);
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges, ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point lpPoint);
        [DllImport("oleacc.dll")] public static extern int AccessibleObjectFromPoint(Point pt, [Out, MarshalAs(UnmanagedType.IUnknown)] out object ppvObject, [Out] out object pvarChild);
    }

    public static class ProcessResolver
    {
        private static readonly Dictionary<string, string> _aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Visual Studio Code", "Code" },
            { "File Explorer", "explorer" },
            { "Command Prompt", "cmd" },
            { "Windows PowerShell", "powershell" },
            { "Task Manager", "Taskmgr" },
            { "Riot Client", "Riot Client" },
            { "Settings", "SystemSettings" },
            { "Notepad", "notepad" },
            { "Discord", "Discord" }
        };

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        public static int FindPid(string buttonName)
        {
            var candidates = GetCandidates(buttonName);

            // 1. Check known aliases
            foreach (var c in candidates)
            {
                string targetName;
                if (_aliases.TryGetValue(c, out targetName))
                {
                    int pid = GetFirstPid(targetName);
                    if (pid > 0) return pid;
                }
            }

            // 2. Direct ProcessName and compact matching
            foreach (var c in candidates)
            {
                int pid = GetFirstPid(c);
                if (pid > 0) return pid;

                string compact = c.Replace(" ", "");
                pid = GetFirstPid(compact);
                if (pid > 0) return pid;
            }

            // 3. First word matching (e.g. "AyuGram Desktop" -> "AyuGram", "Zoom Workplace" -> "Zoom")
            foreach (var c in candidates)
            {
                string firstWord = c.Split(' ')[0];
                if (!string.Equals(firstWord, c, StringComparison.OrdinalIgnoreCase))
                {
                    int pid = GetFirstPid(firstWord);
                    if (pid > 0) return pid;
                }
            }

            // 4. Fallback: EnumWindows checking window title against all candidates
            int foundPid = 0;
            var sb = new StringBuilder(256);
            var seenPids = new HashSet<uint>();

            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindowVisible(hWnd)) return true;

                uint pid;
                GetWindowThreadProcessId(hWnd, out pid);
                if (pid == 0 || pid == (uint)Process.GetCurrentProcess().Id) return true;
                if (!seenPids.Add(pid)) return true;

                sb.Length = 0;
                GetWindowText(hWnd, sb, 256);
                string title = sb.ToString();

                if (!string.IsNullOrWhiteSpace(title))
                {
                    foreach (var c in candidates)
                    {
                        if (title.IndexOf(c, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            foundPid = (int)pid;
                            return false;
                        }
                    }
                }

                try
                {
                    using (var p = Process.GetProcessById((int)pid))
                    {
                        string procName = p.ProcessName;
                        foreach (var c in candidates)
                        {
                            if (procName.IndexOf(c, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                c.IndexOf(procName, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                foundPid = (int)pid;
                                return false;
                            }
                        }
                    }
                }
                catch { }

                return true;
            }, IntPtr.Zero);

            return foundPid;
        }

        private static List<string> GetCandidates(string buttonName)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(buttonName)) return list;

            buttonName = buttonName.Trim();

            // Grouped suffix "App - 1 running window"
            if (buttonName.EndsWith(" running window", StringComparison.OrdinalIgnoreCase) ||
                buttonName.EndsWith(" running windows", StringComparison.OrdinalIgnoreCase))
            {
                int dash = buttonName.LastIndexOf(" - ");
                if (dash > 0) list.Add(buttonName.Substring(0, dash).Trim());
            }

            // Part after last dash (e.g. "#chat - Discord" -> "Discord")
            int lastDash = buttonName.LastIndexOf(" - ");
            if (lastDash > 0)
            {
                string after = buttonName.Substring(lastDash + 3).Trim();
                if (!after.EndsWith("window", StringComparison.OrdinalIgnoreCase) &&
                    !after.EndsWith("windows", StringComparison.OrdinalIgnoreCase))
                {
                    list.Add(after);
                }
            }

            // Part before first dash
            int firstDash = buttonName.IndexOf(" - ");
            if (firstDash > 0)
            {
                list.Add(buttonName.Substring(0, firstDash).Trim());
            }

            list.Add(buttonName);
            return list;
        }

        private static int GetFirstPid(string processName)
        {
            try
            {
                var procs = Process.GetProcessesByName(processName);
                if (procs.Length > 0)
                {
                    int pid = procs[0].Id;
                    for (int i = 0; i < procs.Length; i++) procs[i].Dispose();
                    return pid;
                }
            }
            catch { }
            return 0;
        }
    }

    public class EndTaskFloatingBar : Form
    {
        private readonly int _pid;
        private readonly string _appName;
        private readonly Rectangle _btnRect;
        private bool _isHovered;
        private bool _isPressed;

        private static readonly Font _labelFont = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
        private static readonly StringFormat _labelFormat = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
        private static readonly PointF[] _boltPoints = new PointF[]
        {
            new PointF(20.5f, 9.5f),
            new PointF(15.5f, 17.5f),
            new PointF(20.0f, 17.5f),
            new PointF(17.5f, 24.5f),
            new PointF(24.5f, 15.5f),
            new PointF(20.0f, 15.5f)
        };

        private const int CS_DROPSHADOW = 0x00020000;
        private const int WS_EX_TOPMOST = 0x00000008;

        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr hObject);

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= CS_DROPSHADOW;
                cp.ExStyle |= WS_EX_TOPMOST;
                return cp;
            }
        }

        public EndTaskFloatingBar(int pid, string appName, Rectangle btnRect)
        {
            _pid = pid;
            _appName = appName;
            _btnRect = btnRect;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(32, 32, 32);
            Size = new Size(118, 34);
            Cursor = Cursors.Hand;
            DoubleBuffered = true;

            var dismissTimer = new System.Windows.Forms.Timer { Interval = 8000 };
            dismissTimer.Tick += (s, e) =>
            {
                dismissTimer.Stop();
                dismissTimer.Dispose();
                Close();
            };
            dismissTimer.Start();

            var screen = Screen.FromPoint(_btnRect.Location);
            int x;
            if (_btnRect.Right + 105 + Width < screen.WorkingArea.Right)
                x = _btnRect.Right + 105;
            else
                x = _btnRect.Left - Width - 105;

            int y = _btnRect.Top - Height - 4;
            Location = new Point(x, y);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _isHovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _isHovered = false;
            _isPressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _isPressed = true;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _isPressed = false;
            Invalidate();
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            int pidToKill = _pid;
            string appToKill = _appName;
            Close();
            ThreadPool.QueueUserWorkItem(_ => KillProcess(pidToKill, appToKill));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);

            var bg = _isPressed ? Color.FromArgb(168, 32, 20) :
                     _isHovered ? Color.FromArgb(196, 43, 28) :
                     Color.FromArgb(36, 36, 36);

            var border = _isHovered ? Color.FromArgb(220, 60, 45) : Color.FromArgb(62, 62, 62);

            using (var path = GetRoundedRect(rect, 7))
            {
                using (var b = new SolidBrush(bg))
                    g.FillPath(b, path);

                using (var p = new Pen(border, 1.0f))
                    g.DrawPath(p, path);
            }

            var iconColor = _isHovered ? Color.White : Color.FromArgb(245, 185, 35);
            using (var iconBrush = new SolidBrush(iconColor))
                g.FillPolygon(iconBrush, _boltPoints);

            var textColor = _isHovered ? Color.White : Color.FromArgb(242, 242, 242);
            using (var tb = new SolidBrush(textColor))
                g.DrawString("End task", _labelFont, tb, new RectangleF(32, 0, Width - 32, Height), _labelFormat);
        }

        private static GraphicsPath GetRoundedRect(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static void KillProcess(int pid, string appName)
        {
            if (pid <= 0 || pid == Process.GetCurrentProcess().Id) return;

            string procName = null;
            string exeDir = null;

            try
            {
                using (var p = Process.GetProcessById(pid))
                {
                    procName = p.ProcessName;
                    try
                    {
                        var module = p.MainModule;
                        if (module != null && !string.IsNullOrEmpty(module.FileName))
                            exeDir = Path.GetDirectoryName(module.FileName);
                    }
                    catch { }
                }
            }
            catch { }

            // 1. Terminate direct target PID instantly
            TerminatePid(pid);

            // 2. Terminate all processes sharing the same executable name
            if (!string.IsNullOrEmpty(procName) && !IsSystemProcess(procName))
            {
                KillAllByName(procName);
            }

            // 3. Riot Games client suite
            bool isRiot = (procName != null && procName.IndexOf("Riot", StringComparison.OrdinalIgnoreCase) >= 0) ||
                          (appName != null && appName.IndexOf("Riot", StringComparison.OrdinalIgnoreCase) >= 0) ||
                          (!string.IsNullOrEmpty(exeDir) && exeDir.IndexOf("Riot Games", StringComparison.OrdinalIgnoreCase) >= 0);

            if (isRiot)
            {
                string[] riotNames = new string[] { "RiotClientServices", "Riot Client", "RiotClientCrashHandler", "RiotClientUx" };
                foreach (var rx in riotNames)
                {
                    KillAllByName(rx);
                }
            }

            // 4. Discord suite
            bool isDiscord = (procName != null && procName.IndexOf("Discord", StringComparison.OrdinalIgnoreCase) >= 0) ||
                             (appName != null && appName.IndexOf("Discord", StringComparison.OrdinalIgnoreCase) >= 0) ||
                             (!string.IsNullOrEmpty(exeDir) && exeDir.IndexOf("Discord", StringComparison.OrdinalIgnoreCase) >= 0);

            if (isDiscord)
            {
                KillAllByName("Discord");
            }

            // 5. Steam suite
            bool isSteam = (procName != null && procName.Equals("steam", StringComparison.OrdinalIgnoreCase)) ||
                           (appName != null && appName.IndexOf("Steam", StringComparison.OrdinalIgnoreCase) >= 0);

            if (isSteam)
            {
                string[] steamNames = new string[] { "steam", "steamservice", "steamwebhelper" };
                foreach (var sx in steamNames)
                {
                    KillAllByName(sx);
                }
            }
        }

        private static void KillAllByName(string processName)
        {
            if (string.IsNullOrEmpty(processName)) return;
            try
            {
                foreach (var p in Process.GetProcessesByName(processName))
                {
                    TerminatePid(p.Id);
                    p.Dispose();
                }
            }
            catch { }
        }

        private static bool TerminatePid(int pid)
        {
            if (pid <= 0 || pid == Process.GetCurrentProcess().Id) return false;
            try
            {
                IntPtr hProcess = OpenProcess(0x0001, false, pid); // PROCESS_TERMINATE
                if (hProcess != IntPtr.Zero)
                {
                    bool res = TerminateProcess(hProcess, 1);
                    CloseHandle(hProcess);
                    return res;
                }
                using (var p = Process.GetProcessById(pid))
                {
                    p.Kill();
                    return true;
                }
            }
            catch { return false; }
        }

        private static bool IsSystemProcess(string name)
        {
            if (string.IsNullOrEmpty(name)) return true;
            string[] sys = new string[] { "explorer", "dwm", "svchost", "csrss", "smss", "services", "lsass", "winlogon", "System", "Idle" };
            foreach (var s in sys)
            {
                if (name.Equals(s, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}

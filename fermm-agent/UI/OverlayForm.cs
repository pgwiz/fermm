using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Collections.Generic;

namespace FermmAgent.UI;

public class OverlayForm : Form
{
    // Win32 imports for topmost + click-through + always on top behavior
    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    const uint SWP_NOMOVE = 0x0002;
    const uint SWP_NOSIZE = 0x0001;
    const int GWL_EXSTYLE = -20;
    const int WS_EX_LAYERED = 0x80000;
    const int WS_EX_TRANSPARENT = 0x20;
    const int WM_HOTKEY = 0x0312;
    const uint MOD_CTRL = 0x0002;
    const uint MOD_SHIFT = 0x0004;
    const uint VK_SPACE = 0x20;
    const uint VK_F1 = 0x70;

    // UI Controls
    private Panel titleBar = null!;
    private Label titleLabel = null!;
    private Label hotkeyHint = null!;
    private TabControl tabControl = null!;
    private Button btnAddTab = null!;
    private Button btnClose = null!;
    private Button btnMinimize = null!;
    private Button btnToggleClickThrough = null!;
    private TrackBar opacitySlider = null!;
    private Label opacityLabel = null!;
    private bool isClickThrough = false;
    private Point dragStart;
    private bool isDragging = false;

    // hotkey IDs
    const int HOTKEY_TOGGLE = 1;
    const int HOTKEY_NEWTAB = 2;

    // IPC Communication
    private NamedPipeClientStream? _pipeClient;
    private StreamWriter? _pipeWriter;
    private string _deviceId;
    private System.Threading.CancellationTokenSource _cts;

    public OverlayForm(string deviceId)
    {
        _deviceId = deviceId;
        _cts = new System.Threading.CancellationTokenSource();
        
        InitializeComponent();
        SetupWindowBehavior();
        RegisterHotkeys();
        
        _ = Task.Run(() => ConnectToPipeAsync());
    }

    void SetupWindowBehavior()
    {
        // always on top
        SetWindowPos(this.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
        this.TopMost = true;
    }

    void RegisterHotkeys()
    {
        // Ctrl+Shift+Space = toggle visibility
        RegisterHotKey(this.Handle, HOTKEY_TOGGLE, MOD_CTRL | MOD_SHIFT, VK_SPACE);
        // Ctrl+Shift+F1 = new tab
        RegisterHotKey(this.Handle, HOTKEY_NEWTAB, MOD_CTRL | MOD_SHIFT, VK_F1);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            int id = m.WParam.ToInt32();
            if (id == HOTKEY_TOGGLE)
                this.Visible = !this.Visible;
            else if (id == HOTKEY_NEWTAB)
                AddNewTab("New Tab");
        }
        base.WndProc(ref m);
    }

    void InitializeComponent()
    {
        this.Size = new Size(420, 560);
        this.StartPosition = FormStartPosition.Manual;
        var screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
        this.Location = new Point(
            screen.WorkingArea.Width - 440,
            screen.WorkingArea.Height / 2 - 280
        );
        this.FormBorderStyle = FormBorderStyle.None;
        this.BackColor = Color.FromArgb(13, 17, 28);
        this.Opacity = 0.93;
        this.TopMost = true;
        this.ShowInTaskbar = false; // hide from taskbar
        this.Text = "FERMM Overlay";

        // rounded corners via region
        ApplyRoundedCorners();

        BuildTitleBar();
        BuildTabArea();
        BuildStatusBar();

        // default tabs
        AddNewTab("💬 Chat");

        // populate defaults
        if (tabControl.TabPages.Count > 0)
        {
            var rtb = tabControl.TabPages[0].Controls[0] as RichTextBox;
            if (rtb != null) rtb.Text = "Connected to FERMM Dashboard\n\nHotkeys:\n  Ctrl+Shift+Space → toggle visibility\n  Ctrl+Shift+F1 → new tab\n\nType below to chat with dashboard:";
        }
    }

    void ApplyRoundedCorners()
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        int radius = 12;
        path.AddArc(0, 0, radius * 2, radius * 2, 180, 90);
        path.AddArc(Width - radius * 2, 0, radius * 2, radius * 2, 270, 90);
        path.AddArc(Width - radius * 2, Height - radius * 2, radius * 2, radius * 2, 0, 90);
        path.AddArc(0, Height - radius * 2, radius * 2, radius * 2, 90, 90);
        path.CloseAllFigures();
        this.Region = new Region(path);
    }

    void BuildTitleBar()
    {
        titleBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 42,
            BackColor = Color.FromArgb(22, 28, 44),
            Cursor = Cursors.SizeAll
        };

        // drag behavior
        titleBar.MouseDown += (s, e) => { isDragging = true; dragStart = e.Location; };
        titleBar.MouseMove += (s, e) =>
        {
            if (isDragging)
            {
                var p = PointToScreen(e.Location);
                Location = new Point(p.X - dragStart.X, p.Y - dragStart.Y);
            }
        };
        titleBar.MouseUp += (s, e) => isDragging = false;

        // accent line at top
        var accentLine = new Panel
        {
            Dock = DockStyle.Top,
            Height = 2,
            BackColor = Color.FromArgb(59, 130, 246)
        };
        titleBar.Controls.Add(accentLine);

        titleLabel = new Label
        {
            Text = "⬡ FERMM OVERLAY",
            ForeColor = Color.FromArgb(148, 163, 184),
            Font = new Font("Consolas", 9f, FontStyle.Bold),
            Location = new Point(12, 12),
            AutoSize = true
        };
        titleBar.Controls.Add(titleLabel);

        // window buttons
        btnClose = MakeWindowBtn("✕", Color.FromArgb(239, 68, 68), new Point(388, 11));
        btnClose.Click += (s, e) => this.Hide(); // hide not close
        titleBar.Controls.Add(btnClose);

        btnMinimize = MakeWindowBtn("−", Color.FromArgb(234, 179, 8), new Point(362, 11));
        btnMinimize.Click += (s, e) => this.WindowState = FormWindowState.Minimized;
        titleBar.Controls.Add(btnMinimize);

        btnToggleClickThrough = MakeWindowBtn("◎", Color.FromArgb(34, 197, 94), new Point(336, 11));
        btnToggleClickThrough.Click += ToggleClickThrough;
        titleBar.Controls.Add(btnToggleClickThrough);

        this.Controls.Add(titleBar);
    }

    Button MakeWindowBtn(string text, Color color, Point location)
    {
        var btn = new Button
        {
            Text = text,
            Size = new Size(20, 20),
            Location = location,
            FlatStyle = FlatStyle.Flat,
            ForeColor = color,
            BackColor = Color.Transparent,
            Font = new Font("Consolas", 8f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            TabStop = false
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(40, 50, 70);
        return btn;
    }

    void BuildTabArea()
    {
        // tab header row
        var tabHeader = new Panel
        {
            Height = 36,
            BackColor = Color.FromArgb(17, 22, 36),
            Dock = DockStyle.Top
        };
        tabHeader.Location = new Point(0, 42);

        btnAddTab = new Button
        {
            Text = "+ Tab",
            Size = new Size(60, 26),
            Location = new Point(8, 5),
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.FromArgb(100, 116, 139),
            BackColor = Color.FromArgb(30, 41, 59),
            Font = new Font("Consolas", 8f),
            Cursor = Cursors.Hand
        };
        btnAddTab.FlatAppearance.BorderColor = Color.FromArgb(51, 65, 85);
        btnAddTab.Click += (s, e) =>
        {
            string name = Microsoft.VisualBasic.Interaction.InputBox("Tab name:", "New Tab", "Tab " + (tabControl.TabCount + 1));
            if (!string.IsNullOrWhiteSpace(name))
                AddNewTab(name);
        };
        tabHeader.Controls.Add(btnAddTab);

        tabControl = new TabControl
        {
            Location = new Point(0, 78),
            Size = new Size(420, 440),
            Appearance = TabAppearance.FlatButtons,
            DrawMode = TabDrawMode.OwnerDrawFixed,
            ItemSize = new Size(100, 28),
            SizeMode = TabSizeMode.Fixed,
            Padding = new Point(8, 4),
            Font = new Font("Consolas", 8.5f),
        };

        tabControl.DrawItem += DrawTab;
        tabControl.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

        this.Controls.Add(tabControl);
        this.Controls.Add(tabHeader);
    }

    void DrawTab(object? sender, DrawItemEventArgs e)
    {
        var tab = tabControl.TabPages[e.Index];
        bool selected = e.Index == tabControl.SelectedIndex;

        var bgColor = selected ? Color.FromArgb(30, 58, 138) : Color.FromArgb(17, 22, 36);
        var fgColor = selected ? Color.FromArgb(219, 234, 254) : Color.FromArgb(100, 116, 139);

        e.Graphics.FillRectangle(new SolidBrush(bgColor), e.Bounds);

        if (selected)
        {
            e.Graphics.FillRectangle(
                new SolidBrush(Color.FromArgb(59, 130, 246)),
                new Rectangle(e.Bounds.X, e.Bounds.Bottom - 2, e.Bounds.Width, 2)
            );
        }

        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        e.Graphics.DrawString(tab.Text, tabControl.Font, new SolidBrush(fgColor), e.Bounds, sf);
    }

    void BuildStatusBar()
    {
        var statusBar = new Panel
        {
            Height = 36,
            BackColor = Color.FromArgb(17, 22, 36),
            Dock = DockStyle.Bottom
        };

        opacityLabel = new Label
        {
            Text = "Opacity: 93%",
            ForeColor = Color.FromArgb(71, 85, 105),
            Font = new Font("Consolas", 7.5f),
            Location = new Point(8, 10),
            AutoSize = true
        };
        statusBar.Controls.Add(opacityLabel);

        opacitySlider = new TrackBar
        {
            Minimum = 30,
            Maximum = 100,
            Value = 93,
            Size = new Size(120, 30),
            Location = new Point(90, 3),
            TickFrequency = 10,
            BackColor = Color.FromArgb(17, 22, 36)
        };
        opacitySlider.ValueChanged += (s, e) =>
        {
            this.Opacity = opacitySlider.Value / 100.0;
            opacityLabel.Text = $"Opacity: {opacitySlider.Value}%";
        };
        statusBar.Controls.Add(opacitySlider);

        hotkeyHint = new Label
        {
            Text = "Ctrl+Shift+Space to hide",
            ForeColor = Color.FromArgb(51, 65, 85),
            Font = new Font("Consolas", 7f),
            Location = new Point(220, 10),
            AutoSize = true
        };
        statusBar.Controls.Add(hotkeyHint);

        this.Controls.Add(statusBar);
    }

    void AddNewTab(string name)
    {
        var page = new TabPage(name)
        {
            BackColor = Color.FromArgb(13, 17, 28),
            Padding = new Padding(0)
        };

        // Create a panel for input + display
        var mainPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(13, 17, 28)
        };

        // Message display area
        var rtb = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(13, 17, 28),
            ForeColor = Color.FromArgb(203, 213, 225),
            Font = new Font("Consolas", 9f),
            BorderStyle = BorderStyle.None,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            Margin = new Padding(0),
            Padding = new Padding(8),
            WordWrap = true,
            ReadOnly = false
        };

        // Input area at bottom
        var inputPanel = new Panel
        {
            Height = 64,
            BackColor = Color.FromArgb(17, 22, 36),
            Dock = DockStyle.Bottom,
            Padding = new Padding(8)
        };

        var inputBox = new TextBox
        {
            Multiline = true,
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(22, 28, 44),
            ForeColor = Color.FromArgb(203, 213, 225),
            Font = new Font("Consolas", 9f),
            Margin = new Padding(4),
            Padding = new Padding(4),
            ScrollBars = ScrollBars.Vertical,
            AcceptsTab = false,
            AcceptsReturn = true
        };

        var sendBtn = new Button
        {
            Text = "Send",
            Width = 70,
            Dock = DockStyle.Right,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.FromArgb(100, 116, 139),
            BackColor = Color.FromArgb(30, 41, 59),
            Font = new Font("Consolas", 8f),
            Cursor = Cursors.Hand
        };
        sendBtn.FlatAppearance.BorderColor = Color.FromArgb(51, 65, 85);
        sendBtn.Click += (s, e) =>
        {
            var msg = inputBox.Text.Trim();
            if (!string.IsNullOrEmpty(msg))
            {
                rtb.AppendText($"\n[You]: {msg}");
                SendMessageToAgentAsync(msg).Wait();
                inputBox.Clear();
                inputBox.Focus();
            }
        };

        inputPanel.Controls.Add(inputBox);
        inputPanel.Controls.Add(sendBtn);

        mainPanel.Controls.Add(rtb);
        mainPanel.Controls.Add(inputPanel);

        // context menu for copy/paste
        var ctx = new ContextMenuStrip();
        ctx.BackColor = Color.FromArgb(22, 28, 44);
        ctx.ForeColor = Color.FromArgb(203, 213, 225);
        ctx.RenderMode = ToolStripRenderMode.System;

        AddMenuItem(ctx, "📋 Copy", (s, e) => rtb.Copy());
        AddMenuItem(ctx, "📌 Paste", (s, e) => rtb.Paste());
        AddMenuItem(ctx, "✂️ Cut", (s, e) => rtb.Cut());
        ctx.Items.Add(new ToolStripSeparator());
        AddMenuItem(ctx, "✏️ Select All", (s, e) => rtb.SelectAll());
        ctx.Items.Add(new ToolStripSeparator());
        AddMenuItem(ctx, "🗑 Clear Tab", (s, e) => { if (MessageBox.Show("Clear this tab?", "Confirm", MessageBoxButtons.YesNo) == DialogResult.Yes) rtb.Clear(); });
        AddMenuItem(ctx, "❌ Close Tab", (s, e) => { tabControl.TabPages.Remove(page); });

        rtb.ContextMenuStrip = ctx;
        page.Controls.Add(mainPanel);
        tabControl.TabPages.Add(page);
        tabControl.SelectedTab = page;
    }

    void AddMenuItem(ContextMenuStrip ctx, string text, EventHandler handler)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += handler;
        item.BackColor = Color.FromArgb(22, 28, 44);
        item.ForeColor = Color.FromArgb(203, 213, 225);
        ctx.Items.Add(item);
    }

    void ToggleClickThrough(object? sender, EventArgs e)
    {
        isClickThrough = !isClickThrough;
        int style = GetWindowLong(this.Handle, GWL_EXSTYLE);
        if (isClickThrough)
        {
            SetWindowLong(this.Handle, GWL_EXSTYLE, style | WS_EX_TRANSPARENT | WS_EX_LAYERED);
            btnToggleClickThrough.ForeColor = Color.FromArgb(239, 68, 68);
            this.Opacity = 0.35; // very transparent in click-through mode
        }
        else
        {
            SetWindowLong(this.Handle, GWL_EXSTYLE, style & ~WS_EX_TRANSPARENT);
            btnToggleClickThrough.ForeColor = Color.FromArgb(34, 197, 94);
            this.Opacity = opacitySlider.Value / 100.0;
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        UnregisterHotKey(this.Handle, HOTKEY_TOGGLE);
        UnregisterHotKey(this.Handle, HOTKEY_NEWTAB);
        _cts.Cancel();
        _pipeWriter?.Dispose();
        _pipeClient?.Dispose();
        base.OnFormClosed(e);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ApplyRoundedCorners();
    }

    // IPC Communication
    private async Task ConnectToPipeAsync()
    {
        try
        {
            var pipeName = $"fermm_overlay_{_deviceId}";
            _pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);

            await _pipeClient.ConnectAsync(5000, _cts.Token);
            _pipeWriter = new StreamWriter(_pipeClient, Encoding.UTF8) { AutoFlush = true };

            using var reader = new StreamReader(_pipeClient, Encoding.UTF8);
            while (!_cts.Token.IsCancellationRequested)
            {
                var message = await reader.ReadLineAsync(_cts.Token);
                if (string.IsNullOrEmpty(message))
                    continue;

                // Append received message to chat
                if (tabControl.TabPages.Count > 0)
                {
                    var rtb = tabControl.TabPages[0].Controls[0].Controls[0] as RichTextBox;
                    if (rtb != null)
                    {
                        rtb.Invoke(() => rtb.AppendText($"\n[Dashboard]: {message}"));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Pipe connection error: {ex.Message}");
        }
    }

    private async Task SendMessageToAgentAsync(string message)
    {
        try
        {
            if (_pipeWriter != null)
            {
                await _pipeWriter.WriteLineAsync(message);
                await _pipeWriter.FlushAsync();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to send message: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

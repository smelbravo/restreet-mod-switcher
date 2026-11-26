using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class SwitcherSetupForm : Form
{
    private static readonly Color Background = Color.FromArgb(13, 21, 26);
    private static readonly Color Surface = Color.FromArgb(27, 43, 53);
    private static readonly Color Border = Color.FromArgb(54, 75, 87);
    private static readonly Color Ink = Color.FromArgb(244, 241, 232);
    private static readonly Color Muted = Color.FromArgb(177, 189, 198);
    private static readonly Color Accent = Color.FromArgb(255, 203, 50);

    private const int ArtWidth = 354;
    private const int FormLeft = 390;
    private const int FormWidth = 496;
    private readonly Bitmap artwork = LoadImage("switcher-court.jpg");
    private readonly Bitmap mark = LoadImage("switcher-mark.png");
    private readonly Icon appIcon;
    private readonly TextBox folderPath;
    private readonly TextBox isoPath;
    private readonly Label isoLabel;
    private readonly Button browseFolder;
    private readonly Button browseIso;
    private readonly Button apply;
    private readonly Button close;
    private readonly Label status;
    private readonly Label modeHint;
    private readonly ProgressBar progress;
    private readonly int? launcherPid;
    private string installedPath;
    private bool working;

    internal int Result { get; private set; }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    internal SwitcherSetupForm(string destination, int? launcherPid)
    {
        this.launcherPid = launcherPid;
        Text = "ReStreet Mod Switcher Setup";
        using (var stream = GetResource("switcher.ico"))
            appIcon = new Icon(stream);
        Icon = appIcon;
        ClientSize = new Size(928, 574);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        DoubleBuffered = true;
        BackColor = Background;
        ForeColor = Ink;
        Font = SystemFonts.MessageBoxFont;

        var update = launcherPid.HasValue;
        AddLabel(update ? "Launcher update" : "Install or update", 33, 46, 28, Ink, FontStyle.Bold);
        AddLabel(update ? "Applying the update to your installed launcher." :
            "Choose an empty folder for a fresh installation, or select your existing Mod Switcher folder to update.",
            89, 50, 11, Muted);

        AddLabel("Installation folder", 156, 22, 10, Ink, FontStyle.Bold);
        folderPath = AddPath("Installation folder", 187);
        folderPath.Text = destination ?? string.Empty;
        browseFolder = MakeButton("Browse…", FormLeft + FormWidth - 102, 182, 102, 36);
        browseFolder.AccessibleName = "Browse for the installation folder";
        browseFolder.Click += (sender, args) => BrowseFolder();

        isoLabel = AddLabel("FIFA Street (2012) Xbox 360 ISO", 241, 22, 10, Ink, FontStyle.Bold);
        isoPath = AddPath("FIFA Street (2012) Xbox 360 ISO", 272);
        browseIso = MakeButton("Browse…", FormLeft + FormWidth - 102, 267, 102, 36);
        browseIso.AccessibleName = "Browse for the FIFA Street ISO";
        browseIso.Click += (sender, args) => BrowseIso();

        modeHint = AddLabel(string.Empty, 317, 44, 10, Muted);
        status = AddLabel(string.Empty, 377, 62, 10, Ink);
        status.AccessibleName = "Setup status";
        progress = new ProgressBar { Left = FormLeft, Top = 442, Width = FormWidth, Height = 9, Visible = false };
        Controls.Add(progress);

        apply = MakeButton("Install Mod Switcher", FormLeft, 478, 274, 52, true);
        close = MakeButton("Close", FormLeft + FormWidth - 102, 478, 102, 52);
        apply.Click += async (sender, args) => await Apply();
        close.Click += (sender, args) => Close();
        AcceptButton = apply;
        CancelButton = close;
        folderPath.TextChanged += (sender, args) => UpdateMode();
        UpdateMode();

        FormClosing += (sender, args) => args.Cancel = working;
        if (update)
        {
            apply.Visible = false;
            browseFolder.Visible = false;
            folderPath.ReadOnly = true;
            Shown += async (sender, args) => await Apply();
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int dark = 1;
        DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        var g = e.Graphics;
        var original = g.Save();
        float scale = DeviceDpi / 96f;
        g.ScaleTransform(scale, scale);
        const int height = 574;
        var artBounds = new Rectangle(0, 0, ArtWidth, height);
        float artSize = Math.Max((float)ArtWidth / artwork.Width, (float)height / artwork.Height);
        g.SetClip(artBounds);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        float width = artwork.Width * artSize;
        g.DrawImage(artwork, (ArtWidth - width) / 2, 0, width, artwork.Height * artSize);
        using (var shade = new LinearGradientBrush(artBounds, Color.FromArgb(15, Background), Background, 90))
        {
            shade.Blend = new Blend { Positions = new[] { 0f, .45f, .82f, 1f }, Factors = new[] { 0f, .13f, .92f, 1f } };
            g.FillRectangle(shade, artBounds);
        }
        g.ResetClip();

        g.DrawImage(mark, 27, 27, 64, 64);
        using (var accentBrush = new SolidBrush(Accent))
        using (var inkBrush = new SolidBrush(Ink))
        using (var mutedBrush = new SolidBrush(Muted))
        using (var wordmark = new Font("Segoe UI", 42, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var subtitle = new Font("Segoe UI", 22, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var credit = new Font("Segoe UI", 13, FontStyle.Regular, GraphicsUnit.Pixel))
        {
            g.FillRectangle(accentBrush, 31, 397, 44, 3);
            g.DrawString("ReStreet", wordmark, inkBrush, 27, 411);
            g.DrawString("MOD SWITCHER", subtitle, accentBrush, 29, 464);
            g.DrawString("ReStreet port by SamuelitoDaVila", credit, mutedBrush, 30, 527);
        }
        using (var line = new Pen(Border)) g.DrawLine(line, ArtWidth, 0, ArtWidth, height);
        g.Restore(original);
    }

    private static Stream GetResource(string name)
    {
        return Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
    }

    private static Bitmap LoadImage(string name)
    {
        using (var stream = GetResource(name))
        using (var source = Image.FromStream(stream)) return new Bitmap(source);
    }

    private Label AddLabel(string text, int top, int height, float size, Color color, FontStyle style = FontStyle.Regular)
    {
        var label = new Label {
            Text = text, Left = FormLeft, Top = top, Width = FormWidth, Height = height,
            ForeColor = color, Font = new Font("Segoe UI", size, style)
        };
        Controls.Add(label);
        return label;
    }

    private TextBox AddPath(string name, int top)
    {
        var box = new TextBox {
            AccessibleName = name, Left = FormLeft, Top = top, Width = FormWidth - 116,
            BackColor = Surface, ForeColor = Ink, Font = new Font("Segoe UI", 11)
        };
        Controls.Add(box);
        return box;
    }

    private Button MakeButton(string text, int left, int top, int width, int height, bool primary = false)
    {
        var button = new Button {
            Text = text, Left = left, Top = top, Width = width, Height = height,
            FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false,
            BackColor = primary ? Accent : Surface, ForeColor = primary ? Background : Ink,
            Font = new Font("Segoe UI", primary ? 13 : 10, FontStyle.Bold)
        };
        button.FlatAppearance.BorderColor = primary ? Accent : Border;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(255, 221, 105) : Border;
        Controls.Add(button);
        return button;
    }

    private void UpdateMode()
    {
        string path = folderPath.Text.Trim();
        bool update = launcherPid.HasValue ||
            (path.Length > 0 && path.IndexOfAny(Path.GetInvalidPathChars()) < 0 &&
             File.Exists(Path.Combine(path, SwitcherSetup.LauncherName)));
        isoLabel.Visible = isoPath.Visible = browseIso.Visible = !update;
        modeHint.Top = (update ? 241 : 317) * DeviceDpi / 96;
        apply.Text = update ? "Update Mod Switcher" : "Install Mod Switcher";
        modeHint.Text = update ? "Your game files, mods and settings are kept." :
            "A fresh installation downloads the ReStreet base installer and uses your ISO for the game files.";
        if (!working) status.Text = string.Empty;
    }

    private void BrowseFolder()
    {
        using (var dialog = new FolderBrowserDialog {
            Description = "Choose an existing Mod Switcher installation or an empty folder.",
            SelectedPath = folderPath.Text
        })
            if (dialog.ShowDialog(this) == DialogResult.OK) folderPath.Text = dialog.SelectedPath;
    }

    private void BrowseIso()
    {
        using (var dialog = new OpenFileDialog {
            Title = "Select your FIFA Street (2012) Xbox 360 ISO",
            Filter = "Xbox 360 ISO (*.iso)|*.iso", CheckFileExists = true
        })
            if (dialog.ShowDialog(this) == DialogResult.OK) isoPath.Text = dialog.FileName;
    }

    private void Report(string message, int? percentage)
    {
        status.ForeColor = Ink;
        status.Text = message;
        progress.Visible = true;
        progress.Style = percentage.HasValue ? ProgressBarStyle.Continuous : ProgressBarStyle.Marquee;
        if (percentage.HasValue) progress.Value = percentage.Value;
    }

    private async Task Apply()
    {
        try
        {
            if (installedPath != null)
            {
                SwitcherSetup.OpenLauncher(installedPath);
                Close();
                return;
            }

            bool updating = launcherPid.HasValue;
            string destination = updating ? folderPath.Text : SwitcherSetup.ResolveDestination(folderPath.Text, isoPath.Text.Trim(), out updating);
            working = true;
            apply.Enabled = close.Enabled = browseFolder.Enabled = browseIso.Enabled = false;
            folderPath.ReadOnly = isoPath.ReadOnly = true;
            await SwitcherSetup.ApplyAsync(destination, updating ? null : isoPath.Text.Trim(), launcherPid, Report);
            if (launcherPid.HasValue)
            {
                SwitcherSetup.OpenLauncher(destination);
                Result = 0;
                working = false;
                Close();
                return;
            }

            installedPath = destination;
            Result = 0;
            Report(updating ? "The Mod Switcher is up to date." : "The Mod Switcher is ready to play.", 100);
            apply.Text = "Open Mod Switcher";
        }
        catch (Exception error)
        {
            if (installedPath == null) Result = 1;
            progress.Visible = false;
            status.ForeColor = Color.FromArgb(255, 168, 171);
            status.Text = error.Message;
            status.AutoEllipsis = true;
            MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            working = false;
            if (!IsDisposed)
            {
                apply.Enabled = close.Enabled = true;
                if (installedPath == null && !launcherPid.HasValue)
                {
                    browseFolder.Enabled = browseIso.Enabled = true;
                    folderPath.ReadOnly = isoPath.ReadOnly = false;
                }
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            artwork.Dispose();
            mark.Dispose();
            appIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}

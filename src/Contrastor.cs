using System;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Windows.Forms.Automation;

[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]

internal static class Contrast
{
    internal static bool TryColor(string input, out Color color)
    {
        string hex = input.Trim();
        if (hex.StartsWith("#", StringComparison.Ordinal))
            hex = hex.Substring(1);
        color = Color.Empty;
        if (!Regex.IsMatch(hex, @"\A(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})\z"))
            return false;
        if (hex.Length == 3)
            hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);
        color = Color.FromArgb(unchecked((int)0xFF000000) | int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        return true;
    }

    internal static string Hex(Color color)
    {
        return "#" + (color.ToArgb() & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture);
    }

    private static double Linear(byte channel)
    {
        double value = channel / 255d;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static double Luminance(Color color)
    {
        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }

    internal static double Ratio(Color first, Color second)
    {
        double a = Luminance(first);
        double b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }
}

internal sealed class MainWindow : Form
{
    private readonly TextBox foreground = new TextBox();
    private readonly TextBox background = new TextBox();
    private readonly Label preview = new Label();
    private readonly Label ratio = new Label();
    private readonly Label status = new Label { LiveSetting = AutomationLiveSetting.Polite };
    private readonly Button copy = new Button();
    private readonly VerdictLabel[] verdicts = new VerdictLabel[5];
    private readonly Timer resultAnnouncement = new Timer { Interval = 600 };
    private readonly Button read = new Button { Text = "&Lire les résultats", AutoSize = true };
    private string summary;
    private ScreenPicker picker;
    private readonly string[] criteria = {
        "1.4.3 AA · Texte courant", "1.4.3 AA · Grand texte",
        "1.4.6 AAA · Texte courant", "1.4.6 AAA · Grand texte",
        "1.4.11 AA · Composants et éléments graphiques"
    };
    private readonly double[] thresholds = { 4.5, 3, 7, 4.5, 3 };

    private string report;

    internal MainWindow()
    {
        Text = "Contrastor — Analyse des contrastes";
        Font = new Font("Segoe UI", 10);
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(640, 565);
        MinimumSize = new Size(370, 320);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16),
            ColumnCount = 1, AutoScroll = true };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(layout);
        AddRow(layout, new Label { Text = "Contraste des couleurs", AutoSize = true,
            Font = new Font(Font.FontFamily, 17, FontStyle.Bold), Margin = new Padding(0, 0, 0, 10) });
        AddRow(layout, ColorRow("&Premier plan", foreground, "P&ipette premier plan", "#000000"));
        AddRow(layout, ColorRow("&Arrière-plan", background, "Pipe&tte arrière-plan", "#FFFFFF"));

        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Top, Margin = new Padding(0, 3, 0, 6) };
        var swap = new Button { Text = "&Inverser", AutoSize = true };
        swap.Click += delegate
        {
            string original = foreground.Text;
            foreground.Text = background.Text;
            background.Text = original;
        };
        copy.Text = "&Copier le résultat";
        copy.AutoSize = true;
        copy.Click += delegate
        {
            try
            {
                Clipboard.SetText(report);
                status.Text = "Résultat copié.";
            }
            catch (ExternalException)
            {
                status.Text = "Presse-papiers occupé. Réessayez.";
            }
        };
        actions.Controls.Add(swap);
        actions.Controls.Add(copy);
        read.AccessibleDescription = "Ouvre le rapport détaillé. Raccourci Contrôle plus R.";
        read.Click += delegate { ReadResults(); };
        actions.Controls.Add(read);
        AddRow(layout, actions);

        preview.AccessibleName = "Aperçu visuel des couleurs";
        preview.Text = "Aperçu du texte · Aa";
        preview.TextAlign = ContentAlignment.MiddleCenter;
        preview.Font = new Font(Font.FontFamily, 15);
        preview.BorderStyle = BorderStyle.FixedSingle;
        preview.Dock = DockStyle.Fill;
        preview.MinimumSize = new Size(0, 46);
        AddRow(layout, preview);

        ratio.AutoSize = true;
        ratio.Font = new Font(Font.FontFamily, 16, FontStyle.Bold);
        ratio.Margin = new Padding(0, 10, 0, 4);
        AddRow(layout, ratio);
        AddRow(layout, new Label { Text = "Résultats WCAG 2.2", AutoSize = true,
            Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 2, 0, 3) });
        for (int i = 0; i < verdicts.Length; i++)
        {
            verdicts[i] = new VerdictLabel { AutoSize = true, Padding = new Padding(23, 0, 0, 0), Margin = new Padding(0, 2, 0, 2) };
            AddRow(layout, verdicts[i]);
        }
        AddRow(layout, new Label { Text = "Grand texte : au moins 18 pt, ou 14 pt en gras.",
            AutoSize = true, Margin = new Padding(0, 8, 0, 0) });

        status.AutoSize = true;
        status.Margin = new Padding(0, 10, 0, 0);
        status.AccessibleRole = AccessibleRole.StaticText;
        AddRow(layout, status);
        foreground.TextChanged += ColorChanged;
        background.TextChanged += ColorChanged;
        resultAnnouncement.Tick += delegate
        {
            resultAnnouncement.Stop();
            status.Text = summary;
        };
        layout.ClientSizeChanged += delegate
        {
            int width = Math.Max(1, layout.ClientSize.Width - layout.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
            foreach (Control row in layout.Controls)
                if (row is Label && row != preview || row is FlowLayoutPanel)
                    row.MaximumSize = new Size(width, 0);
        };
        FormClosed += delegate { resultAnnouncement.Dispose(); };
        UpdateResults();
        status.Text = summary;
    }

    private static void AddRow(TableLayoutPanel layout, Control control)
    {
        int row = layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(control, 0, row);
    }

    private Control ColorRow(string name, TextBox field, string pickerName, string value)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Top, Margin = Padding.Empty };
        row.Controls.Add(new Label { Text = name, AutoSize = true, MinimumSize = new Size(115, 29),
            TextAlign = ContentAlignment.MiddleLeft });
        field.Text = value;
        field.Width = 120;
        field.AccessibleName = name.Replace("&", "") + " hexadécimal";
        field.AccessibleDescription = "Saisissez trois ou six caractères hexadécimaux. Le dièse est facultatif.";
        row.Controls.Add(field);
        var picker = new Button { Text = pickerName, AutoSize = true };
        picker.AccessibleDescription = "Flèches pour déplacer la mire. Espace pour lire la couleur et la position. Entrée pour choisir. Échap pour annuler.";
        picker.Click += delegate
        {
            // Wait until the button has finished processing the activating click.
            BeginInvoke((Action)delegate { BeginPick(field); });
        };
        row.Controls.Add(picker);
        return row;
    }

    private void ColorChanged(object sender, EventArgs args)
    {
        UpdateResults();
        resultAnnouncement.Stop();
        resultAnnouncement.Start();
    }

    protected override bool ProcessCmdKey(ref Message message, Keys key)
    {
        if (key == (Keys.Control | Keys.R))
        {
            ReadResults();
            return true;
        }
        return base.ProcessCmdKey(ref message, key);
    }

    private void ReadResults()
    {
        using (var dialog = new Form { Text = "Résultats WCAG 2.2", Font = Font,
            StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false,
            ClientSize = new Size(590, 285), MinimumSize = new Size(300, 200), AutoScaleMode = AutoScaleMode.Dpi })
        {
            var close = new Button { Text = "&Fermer", AutoSize = true, DialogResult = DialogResult.Cancel, Dock = DockStyle.Bottom };
            var details = new TextBox { AccessibleName = "Rapport de contraste", ReadOnly = true, Multiline = true,
                WordWrap = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
                BackColor = SystemColors.Window, ForeColor = SystemColors.WindowText, Text = report, TabIndex = 0 };
            close.TabIndex = 1;
            dialog.Controls.Add(details);
            dialog.Controls.Add(close);
            dialog.CancelButton = close;
            dialog.ActiveControl = details;
            dialog.ShowDialog(this);
        }
    }

    protected override void OnSystemColorsChanged(EventArgs e)
    {
        base.OnSystemColorsChanged(e);
        if (verdicts[verdicts.Length - 1] != null)
            UpdateResults();
    }

    private void UpdateResults()
    {
        Color first;
        Color second;
        bool firstValid = Contrast.TryColor(foreground.Text, out first);
        bool secondValid = Contrast.TryColor(background.Text, out second);
        bool valid = firstValid && secondValid;
        bool showColors = valid && !SystemInformation.HighContrast;
        preview.ForeColor = showColors ? first : SystemColors.WindowText;
        preview.BackColor = showColors ? second : SystemColors.Window;
        copy.Enabled = valid;
        string invalidField = !firstValid && !secondValid ? "premier plan et arrière-plan"
            : !firstValid ? "premier plan" : "arrière-plan";
        double value = valid ? Contrast.Ratio(first, second) : 0;
        ratio.Text = valid ? value.ToString("0.00000", CultureInfo.GetCultureInfo("fr-FR")) + " : 1"
            : "Hexadécimal invalide · " + invalidField;
        var lines = new System.Collections.Generic.List<string>();
        int passedCount = 0;
        if (valid)
        {
            lines.Add("Contraste WCAG 2.2 — " + Contrast.Hex(first) + " sur " + Contrast.Hex(second));
            lines.Add("Ratio calculé " + value.ToString("G17", CultureInfo.GetCultureInfo("fr-FR")) + " : 1");
        }
        for (int i = 0; i < verdicts.Length; i++)
        {
            string threshold = thresholds[i].ToString("0.0", CultureInfo.GetCultureInfo("fr-FR"));
            bool? passed = valid ? (bool?)(value >= thresholds[i]) : null;
            if (passed == true)
                passedCount++;
            string outcome = !passed.HasValue ? "À évaluer" : passed.Value ? "Conforme" : "Non conforme";
            verdicts[i].SetVerdict(criteria[i] + " · " + threshold + " : 1 · " + outcome, passed);
            lines.Add(verdicts[i].Text);
        }
        if (!valid)
            lines.Insert(0, ratio.Text + ". Formats acceptés : #RGB ou #RRGGBB.");
        report = string.Join(Environment.NewLine, lines);
        summary = valid ? "Ratio " + ratio.Text + ". Seuils atteints : " + passedCount + " sur 5."
            : ratio.Text + ". Formats acceptés : #RGB ou #RRGGBB.";
    }

    private void BeginPick(TextBox field)
    {
        if (picker != null)
            return;
        resultAnnouncement.Stop();
        picker = new ScreenPicker(delegate(Color? color) { RestoreAfterPick(field, color); });
        Hide();

        // Give Windows time to redraw the windows the analyzer had covered.
        var delay = new Timer { Interval = 150 };
        delay.Tick += delegate
        {
            delay.Dispose();
            try
            {
                picker.Show();
            }
            catch (Exception error)
            {
                if (!(error is System.ComponentModel.Win32Exception || error is ExternalException ||
                    error is ArgumentException || error is InvalidOperationException || error is OutOfMemoryException))
                    throw;
                picker.Dispose();
                RestoreAfterPick(field, null);
                status.Text = "Capture d’écran indisponible. Réessayez depuis le bureau Windows.";
            }
        };
        delay.Start();
    }

    private void RestoreAfterPick(TextBox field, Color? color)
    {
        picker = null;
        Show();
        Activate();
        field.Focus();
        if (color.HasValue)
        {
            string hex = Contrast.Hex(color.Value);
            field.Text = hex;
            resultAnnouncement.Stop();
            status.Text = "Couleur prélevée " + hex + ". " + summary;
        }
        else
        {
            status.Text = "Pipette annulée.";
        }
    }
}

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainWindow());
    }
}

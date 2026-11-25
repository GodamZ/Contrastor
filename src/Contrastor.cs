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
    private string summary;
    private ScreenPicker picker;
    private static readonly string[] Criteria = { "1.4.3 AA", "1.4.3 AA", "1.4.6 AAA", "1.4.6 AAA", "1.4.11 AA" };
    private static readonly string[] Categories = {
        "Texte courant", "Grand texte", "Texte courant", "Grand texte", "Composants et graphiques"
    };
    private const string ReportRow = "{0,-10} | {1,-24} | {2,-7} | {3}";
    private const string LargeText = "Un texte est considéré comme grand à partir de 18 pt (24 px), ou de 14 pt (environ 18,67 px) s’il est en gras.";
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");
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
        AddRow(layout, actions);

        preview.AccessibleName = "Aperçu visuel des couleurs";
        preview.Text = "Aperçu du texte (Aa)";
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
        var results = CreateResultsTable();
        AddRow(layout, results);

        var note = new RichTextBox
        {
            Text = LargeText, AccessibleName = "Définition du grand texte", ReadOnly = true,
            TabStop = false, ScrollBars = RichTextBoxScrollBars.None, Dock = DockStyle.Top,
            BorderStyle = BorderStyle.None, BackColor = SystemColors.Control, ForeColor = SystemColors.ControlText,
            Margin = new Padding(0, 8, 0, 0)
        };
        note.ContentsResized += delegate(object sender, ContentsResizedEventArgs e)
        {
            int height = e.NewRectangle.Height + note.Font.Height / 4;
            if (height > 0 && note.Height != height)
                note.Height = height;
        };
        AddRow(layout, note);
        Shown += delegate { EmphasizeLargeText(note); };

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
    }

    private static void EmphasizeLargeText(RichTextBox text)
    {
        int start = text.Text.IndexOf(LargeText, StringComparison.Ordinal);
        using (var emphasis = new Font(text.Font, FontStyle.Bold))
        {
            foreach (string phrase in new[] { "grand", "18 pt (24 px)", "14 pt (environ 18,67 px)", "en gras" })
            {
                text.Select(start + LargeText.IndexOf(phrase, StringComparison.Ordinal), phrase.Length);
                text.SelectionFont = emphasis;
            }
        }
        text.Select(0, 0);
    }

    private TableLayoutPanel CreateResultsTable()
    {
        var table = new TableLayoutPanel
        {
            ColumnCount = 4, RowCount = verdicts.Length + 1, AutoSize = true, Dock = DockStyle.Top,
            AccessibleName = "Seuils WCAG 2.2", AccessibleRole = AccessibleRole.Table,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var headingFont = new Font(Font, FontStyle.Bold);
        table.Disposed += delegate { headingFont.Dispose(); };
        string[] headings = { "Critère", "Élément", "Seuil", "Verdict" };
        var categoryCells = new Label[Categories.Length];
        for (int column = 0; column < headings.Length; column++)
        {
            Label heading = TableCell(headings[column]);
            heading.Font = headingFont;
            table.Controls.Add(heading, column, 0);
        }
        for (int i = 0; i < verdicts.Length; i++)
        {
            table.Controls.Add(TableCell(Criteria[i]), 0, i + 1);
            categoryCells[i] = TableCell(Categories[i]);
            table.Controls.Add(categoryCells[i], 1, i + 1);
            table.Controls.Add(TableCell(FormatRatio(thresholds[i], "0.#")), 2, i + 1);
            verdicts[i] = new VerdictLabel { AutoSize = true, Padding = new Padding(23, 0, 0, 0),
                Margin = new Padding(5, 4, 5, 4), Anchor = AnchorStyles.Left };
            table.Controls.Add(verdicts[i], 3, i + 1);
        }
        table.Layout += delegate
        {
            int verdictWidth = table.ClientSize.Width < 370 ? 96 : 0;
            foreach (VerdictLabel verdict in verdicts)
                if (verdict.MaximumSize.Width != verdictWidth)
                    verdict.MaximumSize = new Size(verdictWidth, 0);
            int[] widths = table.GetColumnWidths();
            if (widths.Length < 2)
                return;
            int width = Math.Max(1, widths[1] - 12);
            foreach (Label cell in categoryCells)
                if (cell.MaximumSize.Width != width)
                    cell.MaximumSize = new Size(width, 0);
        };
        return table;
    }

    private static Label TableCell(string text)
    {
        return new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left,
            Margin = new Padding(5, 4, 5, 4) };
    }

    private static string FormatRatio(double value, string format)
    {
        return value.ToString(format, French) + ":1";
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
        string invalidField = !firstValid && !secondValid ? "le premier plan et l’arrière-plan"
            : !firstValid ? "le premier plan" : "l’arrière-plan";
        double value = valid ? Contrast.Ratio(first, second) : 0;
        ratio.Text = valid ? FormatRatio(Math.Floor(value * 10) / 10, "0.0")
            : "Valeur hexadécimale invalide pour " + invalidField;
        var lines = new System.Collections.Generic.List<string>();
        int passedCount = 0;
        if (valid)
        {
            lines.Add("Contraste WCAG 2.2 — " + Contrast.Hex(first) + " sur " + Contrast.Hex(second));
            lines.Add("Ratio " + ratio.Text);
        }
        lines.Add(string.Empty);
        lines.Add(string.Format(ReportRow, "Critère", "Élément", "Seuil", "Verdict"));
        lines.Add("-----------+--------------------------+---------+-------------");
        for (int i = 0; i < verdicts.Length; i++)
        {
            string threshold = FormatRatio(thresholds[i], "0.#");
            bool? passed = valid ? (bool?)(value >= thresholds[i]) : null;
            if (passed == true)
                passedCount++;
            string outcome = !passed.HasValue ? "À évaluer" : passed.Value ? "Conforme" : "Non conforme";
            verdicts[i].SetVerdict(outcome, passed);
            verdicts[i].AccessibleName = Criteria[i] + ", " + Categories[i] + ", seuil " + threshold + ", " + outcome;
            lines.Add(string.Format(ReportRow, Criteria[i], Categories[i], threshold, outcome));
        }
        if (!valid)
            lines.Insert(0, ratio.Text + ". Formats acceptés : #RGB ou #RRGGBB.");
        lines.Add(string.Empty);
        lines.Add(LargeText);
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
            status.Text = string.Empty;
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

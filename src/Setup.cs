using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class Setup
{
    private static readonly string InstallDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Contrastor");
    private static readonly string Executable = Path.Combine(InstallDirectory, "Contrastor.exe");
    private static readonly string Shortcut = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Contrastor.lnk");

    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        using (var dialog = new Form())
        {
            dialog.Text = "Installation de Contrastor";
            dialog.AutoScaleMode = AutoScaleMode.Font;
            dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
            dialog.MaximizeBox = false;
            dialog.MinimizeBox = false;
            dialog.StartPosition = FormStartPosition.CenterScreen;
            var panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown,
                WrapContents = false, Padding = new Padding(14), Dock = DockStyle.Fill };
            dialog.AutoSize = true;
            dialog.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            dialog.Controls.Add(panel);
            panel.Controls.Add(new Label { Text = "Installation pour votre compte Windows.", AutoSize = true });
            var install = new Button { Text = "&Installer ou mettre à jour", AutoSize = true };
            var uninstall = new Button { Text = "&Désinstaller", AutoSize = true, Enabled = File.Exists(Executable) || File.Exists(Shortcut) };
            panel.Controls.Add(install);
            panel.Controls.Add(uninstall);
            dialog.AcceptButton = install;
            install.Click += delegate
            {
                Run(dialog, delegate
                {
                    Install();
                    uninstall.Enabled = true;
                    if (MessageBox.Show(dialog, "Contrastor est disponible dans le menu Démarrer. L’ouvrir maintenant ?",
                        "Installation terminée", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    {
                        Process.Start(Executable);
                        dialog.Close();
                    }
                });
            };
            uninstall.Click += delegate
            {
                if (MessageBox.Show(dialog, "Supprimer Contrastor et son raccourci du menu Démarrer ?",
                    "Désinstallation", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return;
                Run(dialog, delegate
                {
                    File.Delete(Executable);
                    File.Delete(Shortcut);
                    if (Directory.Exists(InstallDirectory) &&
                        Directory.GetFileSystemEntries(InstallDirectory).Length == 0)
                        Directory.Delete(InstallDirectory);
                    uninstall.Enabled = false;
                    MessageBox.Show(dialog, "Contrastor a été désinstallé.", "Désinstallation terminée");
                });
            };
            Application.Run(dialog);
        }
    }

    private static void Run(IWin32Window owner, Action operation)
    {
        try
        {
            operation();
        }
        catch (Exception error)
        {
            MessageBox.Show(owner, "L’opération a échoué. Si Contrastor est ouvert, fermez-le puis réessayez."
                + Environment.NewLine + error.GetBaseException().Message, "Contrastor",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void Install()
    {
        Directory.CreateDirectory(InstallDirectory);
        string temporary = Path.Combine(InstallDirectory, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("Contrastor.exe"))
            using (FileStream destination = File.Open(temporary, FileMode.CreateNew))
                source.CopyTo(destination);

            if (File.Exists(Executable))
                File.Replace(temporary, Executable, null);
            else
                File.Move(temporary, Executable);

            CreateShortcut();
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static void CreateShortcut()
    {
        object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
        try
        {
            object link = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell,
                new object[] { Shortcut });
            try
            {
                Type type = link.GetType();
                type.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { Executable });
                type.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { InstallDirectory });
                type.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
            }
            finally
            {
                Marshal.ReleaseComObject(link);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(shell);
        }
    }
}

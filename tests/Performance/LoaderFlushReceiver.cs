#if IMM_FLUSH_RECEIVER
using System;
using System.IO;
using System.Windows.Forms;

// Standalone synthetic input receiver; compile with the .NET Framework csc and IMM_FLUSH_RECEIVER.
internal static class LoaderFlushReceiver
{
    [STAThread]
    private static void Main(string[] args)
    {
        string path = args[0];
        var form = new Form { Text = "IMM synthetic F10 receiver", Width = 420, Height = 200, KeyPreview = true };
        form.Controls.Add(new Label { Text = "Synthetic loader: F10 saves memory value 7. No game access.", Dock = DockStyle.Fill });
        form.KeyDown += delegate(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.F10) return;
            e.Handled = true; e.SuppressKeyPress = true;
            if (!File.Exists(path + ".ignore"))
                File.WriteAllText(path, "[Constants]\n$\\Mods\\C\\mod.ini\\x = 7\n");
            File.AppendAllText(path + ".received", "F10\n");
        };
        var timer = new Timer { Interval = 45000 };
        timer.Tick += delegate { form.Close(); };
        timer.Start();
        Application.Run(form);
        timer.Dispose();
    }
}
#endif

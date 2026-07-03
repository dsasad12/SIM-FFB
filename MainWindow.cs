// ============================================================
//  SIM FFB - Ventana nativa (WinForms) que embebe la UI (WebView2)
//  Como G HUB: ventana de app real que renderiza la interfaz web.
// ============================================================
using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;

namespace SimFfb
{
    class MainWindow : Form
    {
        WebView2 web;

        public MainWindow()
        {
            Text = "Sim FFB · G923";
            ClientSize = new Size(940, 662);
            MinimumSize = new Size(760, 560);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(19, 20, 21);
            try { Icon = SystemIcons.Application; } catch { }

            web = new WebView2();
            web.Dock = DockStyle.Fill;
            web.DefaultBackgroundColor = Color.FromArgb(23, 24, 25);
            Controls.Add(web);

            Load += OnLoad;
            Shown += OnShown;
            FormClosing += delegate { try { Program.Eng.Stop(); } catch { } };
        }

        async void OnLoad(object sender, EventArgs e)
        {
            try
            {
                var env = await CoreWebView2Environment.CreateAsync(null,
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SimFfbWebView2"));
                await web.EnsureCoreWebView2Async(env);
                web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                web.CoreWebView2.Settings.IsZoomControlEnabled = false;
                web.CoreWebView2.Settings.AreDevToolsEnabled = false;
                web.Source = new Uri("http://127.0.0.1:8770/");
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo iniciar la interfaz (WebView2):\n" + ex.Message,
                    "Sim FFB", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void OnShown(object sender, EventArgs e)
        {
            try { Program.Eng.Start(this.Handle); } catch { }
        }
    }
}

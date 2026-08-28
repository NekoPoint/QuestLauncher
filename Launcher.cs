using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MinimalQuestLauncher
{
    public class DummyForm : Form
    {
        public DummyForm()
        {
            string exeName = Path.GetFileName(Process.GetCurrentProcess().MainModule.FileName);
            
            this.Text = "Discord Quest - " + exeName;
            this.Size = new Size(500, 200);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(30, 31, 34);
            this.ForeColor = Color.White;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;

            Label lblText = new Label();
            lblText.Text = "Спасибо за пользование\n(не закрывайте это окно до завершения квеста)!";
            lblText.Font = new Font("Segoe UI", 11, FontStyle.Bold);
            lblText.TextAlign = ContentAlignment.MiddleCenter;
            lblText.Dock = DockStyle.Fill;
            this.Controls.Add(lblText);

            Label lblAuthor = new Label();
            lblAuthor.Text = "By forgor_pills";
            lblAuthor.Font = new Font("Segoe UI", 9, FontStyle.Italic);
            lblAuthor.ForeColor = Color.FromArgb(150, 150, 150);
            lblAuthor.TextAlign = ContentAlignment.BottomRight;
            lblAuthor.Dock = DockStyle.Bottom;
            lblAuthor.Height = 30;
            this.Controls.Add(lblAuthor);
        }
    }

    public class LauncherForm : Form
    {
        private TextBox tbName;
        private Button btnLaunch;

        public LauncherForm()
        {
            this.Text = "Quest Launcher by forgor_pills";
            this.Size = new Size(350, 180);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(43, 45, 49);
            this.ForeColor = Color.White;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;

            Label lbl = new Label();
            lbl.Text = "Введите название игры (с .exe или без):";
            lbl.Location = new Point(20, 20);
            lbl.AutoSize = true;
            lbl.Font = new Font("Segoe UI", 9);
            this.Controls.Add(lbl);

            tbName = new TextBox();
            tbName.Location = new Point(20, 45);
            tbName.Width = 290;
            tbName.Font = new Font("Segoe UI", 10);
            tbName.BackColor = Color.FromArgb(30, 31, 34);
            tbName.ForeColor = Color.White;
            tbName.BorderStyle = BorderStyle.FixedSingle;
            this.Controls.Add(tbName);

            btnLaunch = new Button();
            btnLaunch.Text = "Запуск";
            btnLaunch.Location = new Point(20, 85);
            btnLaunch.Size = new Size(290, 35);
            btnLaunch.FlatStyle = FlatStyle.Flat;
            btnLaunch.FlatAppearance.BorderSize = 0;
            btnLaunch.BackColor = Color.FromArgb(88, 101, 242);
            btnLaunch.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            btnLaunch.Cursor = Cursors.Hand;
            btnLaunch.Click += BtnLaunch_Click;
            this.Controls.Add(btnLaunch);
        }

        private void BtnLaunch_Click(object sender, EventArgs e)
        {
            string name = tbName.Text.Trim();
            if (string.IsNullOrEmpty(name)) return;

            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                name += ".exe";
            }

            try
            {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "games");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string targetPath = Path.Combine(dir, name);
                File.Copy(Application.ExecutablePath, targetPath, true);

                Process.Start(targetPath, "--dummy");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка запуска: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args.Length > 0 && args[0] == "--dummy")
            {
                Application.Run(new DummyForm());
            }
            else
            {
                Application.Run(new LauncherForm());
            }
        }
    }
}

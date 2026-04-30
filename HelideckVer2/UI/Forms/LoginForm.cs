using System;
using System.Drawing;
using System.Windows.Forms;
using HelideckVer2.UI.Theme;

namespace HelideckVer2
{
    public partial class LoginForm : Form
    {
        private TextBox txtPass;
        private Button btnLogin, btnCancel;
        

        public LoginForm()
        {
            InitializeComponent();
            SetupFastUI(); // Tự vẽ giao diện
        }

        private void SetupFastUI()
        {
            this.Text            = "SYSTEM LOGIN";
            this.Size            = new Size(400, 250);
            this.StartPosition   = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox     = false;
            this.MinimizeBox     = false;
            this.BackColor       = Palette.AppBg;

            var lbl = new Label
            {
                Text      = "ENTER ADMIN PASSWORD:",
                Location  = new Point(30, 30),
                AutoSize  = true,
                Font      = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Palette.TextLabel,
                BackColor = Color.Transparent
            };
            this.Controls.Add(lbl);

            txtPass = new TextBox
            {
                Location     = new Point(30, 60),
                Size         = new Size(320, 30),
                Font         = new Font("Segoe UI", 12),
                PasswordChar = '*',
                BackColor    = Palette.InputBg,
                ForeColor    = Palette.TextValue,
                BorderStyle  = BorderStyle.FixedSingle
            };
            this.Controls.Add(txtPass);

            btnLogin = new Button
            {
                Text      = "LOGIN",
                Location  = new Point(30, 110),
                Size      = new Size(150, 40),
                BackColor = Palette.BtnPrimaryBg,
                ForeColor = Palette.BtnPrimaryFg,
                FlatStyle = FlatStyle.Flat,
                Font      = new Font("Segoe UI", 10, FontStyle.Bold)
            };
            btnLogin.FlatAppearance.BorderColor = Palette.BorderCard;
            btnLogin.FlatAppearance.BorderSize  = 1;
            btnLogin.Click += BtnLogin_Click;
            this.Controls.Add(btnLogin);

            btnCancel = new Button
            {
                Text      = "CANCEL",
                Location  = new Point(200, 110),
                Size      = new Size(150, 40),
                BackColor = Palette.PanelBg,
                ForeColor = Palette.TextLabel,
                FlatStyle = FlatStyle.Flat,
                Font      = new Font("Segoe UI", 10)
            };
            btnCancel.FlatAppearance.BorderColor = Palette.BorderCard;
            btnCancel.FlatAppearance.BorderSize  = 1;
            btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
            this.Controls.Add(btnCancel);

            this.AcceptButton = btnLogin;
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int dark = 1;
            DwmSetWindowAttribute(this.Handle, 20, ref dark, sizeof(int));
        }

        private void BtnLogin_Click(object sender, EventArgs e)
        {
            if (txtPass.Text == HelideckVer2.Models.SystemConfig.AdminPassword)
            {
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            else
            {
                MessageBox.Show("Sai mật khẩu! Vui lòng thử lại.", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
                txtPass.SelectAll();
                txtPass.Focus();
            }

        }
    }
}
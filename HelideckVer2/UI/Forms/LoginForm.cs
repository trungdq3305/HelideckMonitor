using System;
using System.Drawing;
using System.Windows.Forms;

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
            this.Text = "SYSTEM LOGIN";
            this.Size = new Size(400, 250);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            // Label
            Label lbl = new Label();
            lbl.Text = "ENTER ADMIN PASSWORD:";
            lbl.Location = new Point(30, 30);
            lbl.AutoSize = true;
            lbl.Font = new Font("Arial", 10);
            this.Controls.Add(lbl);

            // TextBox Password
            txtPass = new TextBox();
            txtPass.Location = new Point(30, 60);
            txtPass.Size = new Size(320, 30);
            txtPass.Font = new Font("Arial", 12);
            txtPass.PasswordChar = '*'; // Che mật khẩu
            this.Controls.Add(txtPass);

            // Nút Đăng nhập
            btnLogin = new Button();
            btnLogin.Text = "LOGIN";
            btnLogin.Location = new Point(30, 110);
            btnLogin.Size = new Size(150, 40);
            btnLogin.BackColor = Color.DodgerBlue;
            btnLogin.ForeColor = Color.White;
            btnLogin.FlatStyle = FlatStyle.Flat;
            btnLogin.Click += BtnLogin_Click; // Gắn sự kiện thủ công
            this.Controls.Add(btnLogin);

            // Nút Hủy
            btnCancel = new Button();
            btnCancel.Text = "CANCEL";
            btnCancel.Location = new Point(200, 110);
            btnCancel.Size = new Size(150, 40);
            btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
            this.Controls.Add(btnCancel);

            // Cho phép bấm Enter để đăng nhập luôn
            this.AcceptButton = btnLogin;
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
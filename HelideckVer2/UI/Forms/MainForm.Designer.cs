using System.Drawing;
using System.Windows.Forms;

namespace HelideckVer2
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            tableLayoutPanel1 = new TableLayoutPanel();
            tableLayoutPanel2 = new TableLayoutPanel();
            label1 = new Label();
            label2 = new Label();
            label3 = new Label();
            label4 = new Label();
            label5 = new Label();
            label6 = new Label();
            label7 = new Label();
            label8 = new Label();
            lblPosition = new Label();
            lblSpeed = new Label();
            lblHeading = new Label();
            lblRoll = new Label();
            lblPitch = new Label();
            lblHeave = new Label();
            lblHeaveCycle = new Label();
            lblWindSpeed = new Label();
            label9 = new Label();
            lblWindRelated = new Label();
            tableLayoutPanel3 = new TableLayoutPanel();
            tableLayoutPanel4 = new TableLayoutPanel();
            pictureBox1 = new PictureBox();
            pictureBox2 = new PictureBox();
            tableLayoutPanelTrend = new TableLayoutPanel();
            panelTrendButtons = new FlowLayoutPanel();
            panelChartHost = new Panel();
            tableLayoutPanel1.SuspendLayout();
            tableLayoutPanel2.SuspendLayout();
            tableLayoutPanel3.SuspendLayout();
            tableLayoutPanel4.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)pictureBox1).BeginInit();
            ((System.ComponentModel.ISupportInitialize)pictureBox2).BeginInit();
            tableLayoutPanelTrend.SuspendLayout();
            SuspendLayout();
            // 
            // tableLayoutPanel1
            // 
            tableLayoutPanel1.ColumnCount = 2;
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70F));
            tableLayoutPanel1.Controls.Add(tableLayoutPanel2, 0, 0);
            tableLayoutPanel1.Controls.Add(tableLayoutPanel3, 1, 0);
            tableLayoutPanel1.Dock = DockStyle.Fill;
            tableLayoutPanel1.Location = new Point(0, 0);
            tableLayoutPanel1.Name = "tableLayoutPanel1";
            tableLayoutPanel1.RowCount = 1;
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            tableLayoutPanel1.Size = new Size(1262, 753);
            tableLayoutPanel1.TabIndex = 0;
            // 
            // tableLayoutPanel2
            // 
            tableLayoutPanel2.CellBorderStyle = TableLayoutPanelCellBorderStyle.Single;
            tableLayoutPanel2.ColumnCount = 2;
            tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tableLayoutPanel2.Controls.Add(label1, 0, 0);
            tableLayoutPanel2.Controls.Add(label2, 0, 1);
            tableLayoutPanel2.Controls.Add(label3, 0, 2);
            tableLayoutPanel2.Controls.Add(label4, 0, 3);
            tableLayoutPanel2.Controls.Add(label5, 0, 4);
            tableLayoutPanel2.Controls.Add(label6, 0, 5);
            tableLayoutPanel2.Controls.Add(label7, 0, 6);
            tableLayoutPanel2.Controls.Add(label8, 0, 7);
            tableLayoutPanel2.Controls.Add(lblPosition, 1, 0);
            tableLayoutPanel2.Controls.Add(lblSpeed, 1, 1);
            tableLayoutPanel2.Controls.Add(lblHeading, 1, 2);
            tableLayoutPanel2.Controls.Add(lblRoll, 1, 3);
            tableLayoutPanel2.Controls.Add(lblPitch, 1, 4);
            tableLayoutPanel2.Controls.Add(lblHeave, 1, 5);
            tableLayoutPanel2.Controls.Add(lblHeaveCycle, 1, 6);
            tableLayoutPanel2.Controls.Add(lblWindSpeed, 1, 7);
            tableLayoutPanel2.Controls.Add(label9, 0, 8);
            tableLayoutPanel2.Controls.Add(lblWindRelated, 1, 8);
            tableLayoutPanel2.Dock = DockStyle.Fill;
            tableLayoutPanel2.Font = new Font("Arial", 12F, FontStyle.Bold, GraphicsUnit.Point, 0);
            tableLayoutPanel2.Location = new Point(3, 3);
            tableLayoutPanel2.Name = "tableLayoutPanel2";
            tableLayoutPanel2.RowCount = 10;
            tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 10F));
            tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 10F));
            tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 10F));
            tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 10F));
            tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 10F));
            tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 10F));
            tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 10F));
            tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 10F));
            tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 10F));
            tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 10F));
            tableLayoutPanel2.Size = new Size(372, 747);
            tableLayoutPanel2.TabIndex = 0;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Dock = DockStyle.Fill;
            label1.Font = new Font("Arial", 13.8F, FontStyle.Bold);
            label1.Location = new Point(4, 1);
            label1.Name = "label1";
            label1.Size = new Size(178, 73);
            label1.TabIndex = 0;
            label1.Text = "POSITION";
            label1.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Dock = DockStyle.Fill;
            label2.Font = new Font("Arial", 13.8F, FontStyle.Bold);
            label2.Location = new Point(4, 75);
            label2.Name = "label2";
            label2.Size = new Size(178, 73);
            label2.TabIndex = 1;
            label2.Text = "SPEED";
            label2.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Dock = DockStyle.Fill;
            label3.Font = new Font("Arial", 13.8F, FontStyle.Bold);
            label3.Location = new Point(4, 149);
            label3.Name = "label3";
            label3.Size = new Size(178, 73);
            label3.TabIndex = 2;
            label3.Text = "HEADING";
            label3.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Dock = DockStyle.Fill;
            label4.Font = new Font("Arial", 13.8F, FontStyle.Bold);
            label4.Location = new Point(4, 223);
            label4.Name = "label4";
            label4.Size = new Size(178, 73);
            label4.TabIndex = 3;
            label4.Text = "ROLL DATA";
            label4.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Dock = DockStyle.Fill;
            label5.Font = new Font("Arial", 13.8F, FontStyle.Bold);
            label5.Location = new Point(4, 297);
            label5.Name = "label5";
            label5.Size = new Size(178, 73);
            label5.TabIndex = 4;
            label5.Text = "PITCH DATA";
            label5.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // label6
            // 
            label6.AutoSize = true;
            label6.Dock = DockStyle.Fill;
            label6.Font = new Font("Arial", 13.8F, FontStyle.Bold);
            label6.Location = new Point(4, 371);
            label6.Name = "label6";
            label6.Size = new Size(178, 73);
            label6.TabIndex = 5;
            label6.Text = "HEAVE DATA";
            label6.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // label7
            // 
            label7.AutoSize = true;
            label7.Dock = DockStyle.Fill;
            label7.Font = new Font("Arial", 13.8F, FontStyle.Bold);
            label7.Location = new Point(4, 445);
            label7.Name = "label7";
            label7.Size = new Size(178, 73);
            label7.TabIndex = 6;
            label7.Text = "HEAVE CYCLE";
            label7.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // label8
            // 
            label8.AutoSize = true;
            label8.Dock = DockStyle.Fill;
            label8.Font = new Font("Arial", 13.8F, FontStyle.Bold);
            label8.Location = new Point(4, 519);
            label8.Name = "label8";
            label8.Size = new Size(178, 73);
            label8.TabIndex = 7;
            label8.Text = "WIND SPEED";
            label8.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // lblPosition
            // 
            lblPosition.Dock = DockStyle.Fill;
            lblPosition.Font = new Font("Courier New", 19.8000011F, FontStyle.Bold);
            lblPosition.ForeColor = Color.Green;
            lblPosition.Location = new Point(189, 1);
            lblPosition.Name = "lblPosition";
            lblPosition.Size = new Size(179, 73);
            lblPosition.TabIndex = 8;
            lblPosition.Text = "0.00";
            lblPosition.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // lblSpeed
            // 
            lblSpeed.Dock = DockStyle.Fill;
            lblSpeed.Font = new Font("Courier New", 19.8000011F, FontStyle.Bold);
            lblSpeed.ForeColor = Color.Green;
            lblSpeed.Location = new Point(189, 75);
            lblSpeed.Name = "lblSpeed";
            lblSpeed.Size = new Size(179, 73);
            lblSpeed.TabIndex = 9;
            lblSpeed.Text = "0.00";
            lblSpeed.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // lblHeading
            // 
            lblHeading.Dock = DockStyle.Fill;
            lblHeading.Font = new Font("Courier New", 19.8000011F, FontStyle.Bold);
            lblHeading.ForeColor = Color.Green;
            lblHeading.Location = new Point(189, 149);
            lblHeading.Name = "lblHeading";
            lblHeading.Size = new Size(179, 73);
            lblHeading.TabIndex = 10;
            lblHeading.Text = "0.00";
            lblHeading.TextAlign = ContentAlignment.MiddleCenter;
            lblHeading.Click += lblHeading_Click;
            // 
            // lblRoll
            // 
            lblRoll.Dock = DockStyle.Fill;
            lblRoll.Font = new Font("Courier New", 19.8000011F, FontStyle.Bold);
            lblRoll.ForeColor = Color.Green;
            lblRoll.Location = new Point(189, 223);
            lblRoll.Name = "lblRoll";
            lblRoll.Size = new Size(179, 73);
            lblRoll.TabIndex = 11;
            lblRoll.Text = "0.00";
            lblRoll.TextAlign = ContentAlignment.MiddleCenter;
            lblRoll.Click += lblRoll_Click;
            // 
            // lblPitch
            // 
            lblPitch.Dock = DockStyle.Fill;
            lblPitch.Font = new Font("Courier New", 19.8000011F, FontStyle.Bold);
            lblPitch.ForeColor = Color.Green;
            lblPitch.Location = new Point(189, 297);
            lblPitch.Name = "lblPitch";
            lblPitch.Size = new Size(179, 73);
            lblPitch.TabIndex = 12;
            lblPitch.Text = "0.00";
            lblPitch.TextAlign = ContentAlignment.MiddleCenter;
            lblPitch.Click += lblPitch_Click;
            // 
            // lblHeave
            // 
            lblHeave.Dock = DockStyle.Fill;
            lblHeave.Font = new Font("Courier New", 19.8000011F, FontStyle.Bold);
            lblHeave.ForeColor = Color.Green;
            lblHeave.Location = new Point(189, 371);
            lblHeave.Name = "lblHeave";
            lblHeave.Size = new Size(179, 73);
            lblHeave.TabIndex = 13;
            lblHeave.Text = "0.00";
            lblHeave.TextAlign = ContentAlignment.MiddleCenter;
            lblHeave.Click += lblHeave_Click;
            // 
            // lblHeaveCycle
            // 
            lblHeaveCycle.Dock = DockStyle.Fill;
            lblHeaveCycle.Font = new Font("Courier New", 19.8000011F, FontStyle.Bold);
            lblHeaveCycle.ForeColor = Color.Green;
            lblHeaveCycle.Location = new Point(189, 445);
            lblHeaveCycle.Name = "lblHeaveCycle";
            lblHeaveCycle.Size = new Size(179, 73);
            lblHeaveCycle.TabIndex = 14;
            lblHeaveCycle.Text = "0.00";
            lblHeaveCycle.TextAlign = ContentAlignment.MiddleCenter;
            lblHeaveCycle.Click += lblHeaveCycle_Click;
            // 
            // lblWindSpeed
            // 
            lblWindSpeed.Dock = DockStyle.Fill;
            lblWindSpeed.Font = new Font("Courier New", 19.8000011F, FontStyle.Bold);
            lblWindSpeed.ForeColor = Color.Green;
            lblWindSpeed.Location = new Point(189, 519);
            lblWindSpeed.Name = "lblWindSpeed";
            lblWindSpeed.Size = new Size(179, 73);
            lblWindSpeed.TabIndex = 15;
            lblWindSpeed.Text = "0.00";
            lblWindSpeed.TextAlign = ContentAlignment.MiddleCenter;
            lblWindSpeed.Click += lblWindSpeed_Click;
            // 
            // label9
            // 
            label9.AutoSize = true;
            label9.Dock = DockStyle.Fill;
            label9.Font = new Font("Arial", 13.8F, FontStyle.Bold);
            label9.Location = new Point(4, 593);
            label9.Name = "label9";
            label9.Size = new Size(178, 73);
            label9.TabIndex = 16;
            label9.Text = "WIND RELATED";
            label9.TextAlign = ContentAlignment.MiddleLeft;
            label9.Click += label9_Click;
            // 
            // lblWindRelated
            // 
            lblWindRelated.Dock = DockStyle.Fill;
            lblWindRelated.Font = new Font("Courier New", 19.8000011F, FontStyle.Bold);
            lblWindRelated.ForeColor = Color.Green;
            lblWindRelated.Location = new Point(189, 593);
            lblWindRelated.Name = "lblWindRelated";
            lblWindRelated.Size = new Size(179, 73);
            lblWindRelated.TabIndex = 17;
            lblWindRelated.Text = "0°";
            lblWindRelated.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // tableLayoutPanel3
            // 
            tableLayoutPanel3.ColumnCount = 1;
            tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            tableLayoutPanel3.Controls.Add(tableLayoutPanel4, 0, 0);
            tableLayoutPanel3.Controls.Add(tableLayoutPanelTrend, 0, 1);
            tableLayoutPanel3.Dock = DockStyle.Fill;
            tableLayoutPanel3.Location = new Point(381, 3);
            tableLayoutPanel3.Name = "tableLayoutPanel3";
            tableLayoutPanel3.RowCount = 2;
            tableLayoutPanel3.RowStyles.Add(new RowStyle(SizeType.Percent, 40F));
            tableLayoutPanel3.RowStyles.Add(new RowStyle(SizeType.Percent, 60F));
            tableLayoutPanel3.Size = new Size(878, 747);
            tableLayoutPanel3.TabIndex = 1;
            tableLayoutPanel3.Paint += tableLayoutPanel3_Paint;
            // 
            // tableLayoutPanel4
            // 
            tableLayoutPanel4.ColumnCount = 2;
            tableLayoutPanel4.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tableLayoutPanel4.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tableLayoutPanel4.Controls.Add(pictureBox1, 0, 0);
            tableLayoutPanel4.Controls.Add(pictureBox2, 1, 0);
            tableLayoutPanel4.Dock = DockStyle.Fill;
            tableLayoutPanel4.Location = new Point(3, 3);
            tableLayoutPanel4.Name = "tableLayoutPanel4";
            tableLayoutPanel4.RowCount = 1;
            tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            tableLayoutPanel4.Size = new Size(872, 292);
            tableLayoutPanel4.TabIndex = 0;
            // 
            // pictureBox1
            // 
            pictureBox1.BorderStyle = BorderStyle.FixedSingle;
            pictureBox1.Dock = DockStyle.Fill;
            pictureBox1.Location = new Point(3, 3);
            pictureBox1.Name = "pictureBox1";
            pictureBox1.Size = new Size(430, 286);
            pictureBox1.SizeMode = PictureBoxSizeMode.Zoom;
            pictureBox1.TabIndex = 0;
            pictureBox1.TabStop = false;
            // 
            // pictureBox2
            // 
            pictureBox2.BorderStyle = BorderStyle.FixedSingle;
            pictureBox2.Dock = DockStyle.Fill;
            pictureBox2.Location = new Point(439, 3);
            pictureBox2.Name = "pictureBox2";
            pictureBox2.Size = new Size(430, 286);
            pictureBox2.SizeMode = PictureBoxSizeMode.Zoom;
            pictureBox2.TabIndex = 1;
            pictureBox2.TabStop = false;
            // 
            // tableLayoutPanelTrend
            // 
            tableLayoutPanelTrend.ColumnCount = 1;
            tableLayoutPanelTrend.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            tableLayoutPanelTrend.Controls.Add(panelTrendButtons, 0, 0);
            tableLayoutPanelTrend.Controls.Add(panelChartHost, 0, 1);
            tableLayoutPanelTrend.Dock = DockStyle.Fill;
            tableLayoutPanelTrend.Location = new Point(3, 301);
            tableLayoutPanelTrend.Name = "tableLayoutPanelTrend";
            tableLayoutPanelTrend.RowCount = 2;
            tableLayoutPanelTrend.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            tableLayoutPanelTrend.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            tableLayoutPanelTrend.Size = new Size(872, 443);
            tableLayoutPanelTrend.TabIndex = 1;
            // 
            // panelTrendButtons
            // 
            panelTrendButtons.BackColor = Color.White;
            panelTrendButtons.Dock = DockStyle.Fill;
            panelTrendButtons.Location = new Point(3, 3);
            panelTrendButtons.Name = "panelTrendButtons";
            panelTrendButtons.Padding = new Padding(6);
            panelTrendButtons.Size = new Size(866, 38);
            panelTrendButtons.TabIndex = 0;
            panelTrendButtons.WrapContents = false;
            // 
            // panelChartHost
            // 
            panelChartHost.BackColor = Color.White;
            panelChartHost.Dock = DockStyle.Fill;
            panelChartHost.Location = new Point(3, 47);
            panelChartHost.Name = "panelChartHost";
            panelChartHost.Size = new Size(866, 393);
            panelChartHost.TabIndex = 1;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1262, 753);
            Controls.Add(tableLayoutPanel1);
            Icon = (Icon)resources.GetObject("$this.Icon");
            Name = "MainForm";
            Text = "HELIDECK MONITORING SYSTEM";
            WindowState = FormWindowState.Maximized;
            tableLayoutPanel1.ResumeLayout(false);
            tableLayoutPanel2.ResumeLayout(false);
            tableLayoutPanel2.PerformLayout();
            tableLayoutPanel3.ResumeLayout(false);
            tableLayoutPanel4.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)pictureBox1).EndInit();
            ((System.ComponentModel.ISupportInitialize)pictureBox2).EndInit();
            tableLayoutPanelTrend.ResumeLayout(false);
            ResumeLayout(false);
        }
        #endregion

        private TableLayoutPanel tableLayoutPanel1;
        private TableLayoutPanel tableLayoutPanel2;
        private Label label1;
        private Label label2;
        private Label label3;
        private Label label4;
        private Label label5;
        private Label label6;
        private Label label7;
        private Label label8;
        private Label lblPosition;
        private Label lblSpeed;
        private Label lblHeading;
        private Label lblRoll;
        private Label lblPitch;
        private Label lblHeave;
        private Label lblHeaveCycle;

        private TableLayoutPanel tableLayoutPanel3;
        private TableLayoutPanel tableLayoutPanel4;
        private PictureBox pictureBox1;
        private PictureBox pictureBox2;

        // NEW
        private TableLayoutPanel tableLayoutPanelTrend;
        private FlowLayoutPanel panelTrendButtons;
        private Panel panelChartHost;
        private Label lblWindSpeed;
        private Label label9;
        private Label lblWindRelated;
    }
}

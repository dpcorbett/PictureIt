namespace PictureIt
{
    partial class MainForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.pnlMain = new System.Windows.Forms.Panel();
            this.tplMain = new System.Windows.Forms.TableLayoutPanel();
            this.spcBody = new System.Windows.Forms.SplitContainer();
            this.pcbImage = new System.Windows.Forms.PictureBox();
            this.lsbData = new System.Windows.Forms.ListBox();
            this.tlpFile = new System.Windows.Forms.TableLayoutPanel();
            this.btnGetData = new System.Windows.Forms.Button();
            this.btnBrowse = new System.Windows.Forms.Button();
            this.txtFilePath = new System.Windows.Forms.TextBox();
            this.stsMain = new System.Windows.Forms.StatusStrip();
            this.ofdImage = new System.Windows.Forms.OpenFileDialog();
            this.tslCamera = new System.Windows.Forms.ToolStripStatusLabel();
            this.pnlMain.SuspendLayout();
            this.tplMain.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.spcBody)).BeginInit();
            this.spcBody.Panel1.SuspendLayout();
            this.spcBody.Panel2.SuspendLayout();
            this.spcBody.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pcbImage)).BeginInit();
            this.tlpFile.SuspendLayout();
            this.stsMain.SuspendLayout();
            this.SuspendLayout();
            // 
            // pnlMain
            // 
            this.pnlMain.Controls.Add(this.tplMain);
            this.pnlMain.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlMain.Location = new System.Drawing.Point(0, 0);
            this.pnlMain.Name = "pnlMain";
            this.pnlMain.Size = new System.Drawing.Size(830, 612);
            this.pnlMain.TabIndex = 0;
            // 
            // tplMain
            // 
            this.tplMain.ColumnCount = 1;
            this.tplMain.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tplMain.Controls.Add(this.spcBody, 0, 1);
            this.tplMain.Controls.Add(this.tlpFile, 0, 0);
            this.tplMain.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tplMain.Location = new System.Drawing.Point(0, 0);
            this.tplMain.Name = "tplMain";
            this.tplMain.RowCount = 3;
            this.tplMain.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 15F));
            this.tplMain.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 60F));
            this.tplMain.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.tplMain.Size = new System.Drawing.Size(830, 612);
            this.tplMain.TabIndex = 0;
            // 
            // spcBody
            // 
            this.spcBody.Dock = System.Windows.Forms.DockStyle.Fill;
            this.spcBody.Location = new System.Drawing.Point(3, 94);
            this.spcBody.Name = "spcBody";
            // 
            // spcBody.Panel1
            // 
            this.spcBody.Panel1.Controls.Add(this.lsbData);
            // 
            // spcBody.Panel2
            // 
            this.spcBody.Panel2.Controls.Add(this.pcbImage);
            this.spcBody.Size = new System.Drawing.Size(824, 361);
            this.spcBody.SplitterDistance = 274;
            this.spcBody.TabIndex = 0;
            // 
            // pcbImage
            // 
            this.pcbImage.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.pcbImage.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pcbImage.Location = new System.Drawing.Point(0, 0);
            this.pcbImage.Name = "pcbImage";
            this.pcbImage.Size = new System.Drawing.Size(546, 361);
            this.pcbImage.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.pcbImage.TabIndex = 0;
            this.pcbImage.TabStop = false;
            // 
            // lsbData
            // 
            this.lsbData.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lsbData.FormattingEnabled = true;
            this.lsbData.Location = new System.Drawing.Point(0, 0);
            this.lsbData.Name = "lsbData";
            this.lsbData.Size = new System.Drawing.Size(274, 361);
            this.lsbData.TabIndex = 0;
            // 
            // tlpFile
            // 
            this.tlpFile.ColumnCount = 3;
            this.tlpFile.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 20F));
            this.tlpFile.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 60F));
            this.tlpFile.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 20F));
            this.tlpFile.Controls.Add(this.btnGetData, 0, 0);
            this.tlpFile.Controls.Add(this.btnBrowse, 2, 0);
            this.tlpFile.Controls.Add(this.txtFilePath, 1, 0);
            this.tlpFile.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tlpFile.Location = new System.Drawing.Point(3, 3);
            this.tlpFile.Name = "tlpFile";
            this.tlpFile.RowCount = 1;
            this.tlpFile.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tlpFile.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 20F));
            this.tlpFile.Size = new System.Drawing.Size(824, 85);
            this.tlpFile.TabIndex = 1;
            // 
            // btnGetData
            // 
            this.btnGetData.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnGetData.Location = new System.Drawing.Point(3, 3);
            this.btnGetData.Name = "btnGetData";
            this.btnGetData.Size = new System.Drawing.Size(158, 79);
            this.btnGetData.TabIndex = 0;
            this.btnGetData.Text = "Get Data";
            this.btnGetData.UseVisualStyleBackColor = true;
            this.btnGetData.Click += new System.EventHandler(this.btnGetData_Click);
            // 
            // btnBrowse
            // 
            this.btnBrowse.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnBrowse.Location = new System.Drawing.Point(661, 3);
            this.btnBrowse.Name = "btnBrowse";
            this.btnBrowse.Size = new System.Drawing.Size(160, 79);
            this.btnBrowse.TabIndex = 1;
            this.btnBrowse.Text = "Browse";
            this.btnBrowse.UseVisualStyleBackColor = true;
            this.btnBrowse.Click += new System.EventHandler(this.btnBrowse_Click);
            // 
            // txtFilePath
            // 
            this.txtFilePath.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtFilePath.Location = new System.Drawing.Point(167, 3);
            this.txtFilePath.Multiline = true;
            this.txtFilePath.Name = "txtFilePath";
            this.txtFilePath.Size = new System.Drawing.Size(488, 79);
            this.txtFilePath.TabIndex = 2;
            // 
            // stsMain
            // 
            this.stsMain.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tslCamera});
            this.stsMain.Location = new System.Drawing.Point(0, 590);
            this.stsMain.Name = "stsMain";
            this.stsMain.Size = new System.Drawing.Size(830, 22);
            this.stsMain.TabIndex = 4;
            this.stsMain.Text = "statusStrip1";
            // 
            // ofdImage
            // 
            this.ofdImage.FileName = "openFileDialog1";
            // 
            // tslCamera
            // 
            this.tslCamera.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.tslCamera.Name = "tslCamera";
            this.tslCamera.Size = new System.Drawing.Size(52, 17);
            this.tslCamera.Text = "Camera:";
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(830, 612);
            this.Controls.Add(this.stsMain);
            this.Controls.Add(this.pnlMain);
            this.Name = "MainForm";
            this.Text = "Picture It";
            this.pnlMain.ResumeLayout(false);
            this.tplMain.ResumeLayout(false);
            this.spcBody.Panel1.ResumeLayout(false);
            this.spcBody.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.spcBody)).EndInit();
            this.spcBody.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.pcbImage)).EndInit();
            this.tlpFile.ResumeLayout(false);
            this.tlpFile.PerformLayout();
            this.stsMain.ResumeLayout(false);
            this.stsMain.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Panel pnlMain;
        private System.Windows.Forms.TableLayoutPanel tplMain;
        private System.Windows.Forms.SplitContainer spcBody;
        private System.Windows.Forms.ListBox lsbData;
        private System.Windows.Forms.PictureBox pcbImage;
        private System.Windows.Forms.TableLayoutPanel tlpFile;
        private System.Windows.Forms.Button btnGetData;
        private System.Windows.Forms.Button btnBrowse;
        private System.Windows.Forms.TextBox txtFilePath;
        private System.Windows.Forms.StatusStrip stsMain;
        private System.Windows.Forms.OpenFileDialog ofdImage;
        private System.Windows.Forms.ToolStripStatusLabel tslCamera;
    }
}


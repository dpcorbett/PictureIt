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
            this.tlpFile = new System.Windows.Forms.TableLayoutPanel();
            this.btnGetData = new System.Windows.Forms.Button();
            this.btnBrowse = new System.Windows.Forms.Button();
            this.txtFilePath = new System.Windows.Forms.TextBox();
            this.tplBody = new System.Windows.Forms.TableLayoutPanel();
            this.pcbImage = new System.Windows.Forms.PictureBox();
            this.pnlMetaData = new System.Windows.Forms.Panel();
            this.lblMetaData = new System.Windows.Forms.Label();
            this.lsbMetaData = new System.Windows.Forms.ListBox();
            this.pnlExifData = new System.Windows.Forms.Panel();
            this.lblExifData = new System.Windows.Forms.Label();
            this.lsbExifData = new System.Windows.Forms.ListBox();
            this.stsMain = new System.Windows.Forms.StatusStrip();
            this.tslCamera = new System.Windows.Forms.ToolStripStatusLabel();
            this.ofdImage = new System.Windows.Forms.OpenFileDialog();
            this.pnlMain.SuspendLayout();
            this.tplMain.SuspendLayout();
            this.tlpFile.SuspendLayout();
            this.tplBody.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pcbImage)).BeginInit();
            this.pnlMetaData.SuspendLayout();
            this.pnlExifData.SuspendLayout();
            this.stsMain.SuspendLayout();
            this.SuspendLayout();
            // 
            // pnlMain
            // 
            this.pnlMain.Controls.Add(this.tplMain);
            this.pnlMain.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlMain.Location = new System.Drawing.Point(0, 0);
            this.pnlMain.Name = "pnlMain";
            this.pnlMain.Size = new System.Drawing.Size(911, 670);
            this.pnlMain.TabIndex = 0;
            // 
            // tplMain
            // 
            this.tplMain.ColumnCount = 1;
            this.tplMain.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tplMain.Controls.Add(this.tlpFile, 0, 0);
            this.tplMain.Controls.Add(this.tplBody, 0, 1);
            this.tplMain.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tplMain.Location = new System.Drawing.Point(0, 0);
            this.tplMain.Name = "tplMain";
            this.tplMain.RowCount = 3;
            this.tplMain.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 15F));
            this.tplMain.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 60F));
            this.tplMain.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.tplMain.Size = new System.Drawing.Size(911, 670);
            this.tplMain.TabIndex = 0;
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
            this.tlpFile.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 94F));
            this.tlpFile.Size = new System.Drawing.Size(905, 94);
            this.tlpFile.TabIndex = 1;
            // 
            // btnGetData
            // 
            this.btnGetData.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnGetData.Location = new System.Drawing.Point(3, 3);
            this.btnGetData.Name = "btnGetData";
            this.btnGetData.Size = new System.Drawing.Size(175, 88);
            this.btnGetData.TabIndex = 0;
            this.btnGetData.Text = "Get Data";
            this.btnGetData.UseVisualStyleBackColor = true;
            this.btnGetData.Click += new System.EventHandler(this.btnGetData_Click);
            // 
            // btnBrowse
            // 
            this.btnBrowse.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnBrowse.Location = new System.Drawing.Point(727, 3);
            this.btnBrowse.Name = "btnBrowse";
            this.btnBrowse.Size = new System.Drawing.Size(175, 88);
            this.btnBrowse.TabIndex = 1;
            this.btnBrowse.Text = "Browse";
            this.btnBrowse.UseVisualStyleBackColor = true;
            this.btnBrowse.Click += new System.EventHandler(this.btnBrowse_Click);
            // 
            // txtFilePath
            // 
            this.txtFilePath.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtFilePath.Location = new System.Drawing.Point(184, 3);
            this.txtFilePath.Multiline = true;
            this.txtFilePath.Name = "txtFilePath";
            this.txtFilePath.Size = new System.Drawing.Size(537, 88);
            this.txtFilePath.TabIndex = 2;
            // 
            // tplBody
            // 
            this.tplBody.ColumnCount = 3;
            this.tplBody.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 20F));
            this.tplBody.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 60F));
            this.tplBody.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 20F));
            this.tplBody.Controls.Add(this.pcbImage, 1, 0);
            this.tplBody.Controls.Add(this.pnlMetaData, 0, 0);
            this.tplBody.Controls.Add(this.pnlExifData, 2, 0);
            this.tplBody.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tplBody.Location = new System.Drawing.Point(3, 103);
            this.tplBody.Name = "tplBody";
            this.tplBody.RowCount = 1;
            this.tplBody.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.tplBody.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 20F));
            this.tplBody.Size = new System.Drawing.Size(905, 396);
            this.tplBody.TabIndex = 2;
            // 
            // pcbImage
            // 
            this.pcbImage.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.pcbImage.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pcbImage.Location = new System.Drawing.Point(184, 3);
            this.pcbImage.Name = "pcbImage";
            this.pcbImage.Size = new System.Drawing.Size(537, 390);
            this.pcbImage.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.pcbImage.TabIndex = 8;
            this.pcbImage.TabStop = false;
            // 
            // pnlMetaData
            // 
            this.pnlMetaData.Controls.Add(this.lblMetaData);
            this.pnlMetaData.Controls.Add(this.lsbMetaData);
            this.pnlMetaData.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlMetaData.Location = new System.Drawing.Point(3, 3);
            this.pnlMetaData.Name = "pnlMetaData";
            this.pnlMetaData.Size = new System.Drawing.Size(175, 390);
            this.pnlMetaData.TabIndex = 7;
            // 
            // lblMetaData
            // 
            this.lblMetaData.AutoSize = true;
            this.lblMetaData.Location = new System.Drawing.Point(0, 0);
            this.lblMetaData.Name = "lblMetaData";
            this.lblMetaData.Size = new System.Drawing.Size(52, 13);
            this.lblMetaData.TabIndex = 9;
            this.lblMetaData.Text = "Metadata";
            // 
            // lsbMetaData
            // 
            this.lsbMetaData.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lsbMetaData.FormattingEnabled = true;
            this.lsbMetaData.Location = new System.Drawing.Point(0, 22);
            this.lsbMetaData.Name = "lsbMetaData";
            this.lsbMetaData.Size = new System.Drawing.Size(175, 368);
            this.lsbMetaData.TabIndex = 5;
            // 
            // pnlExifData
            // 
            this.pnlExifData.Controls.Add(this.lblExifData);
            this.pnlExifData.Controls.Add(this.lsbExifData);
            this.pnlExifData.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlExifData.Location = new System.Drawing.Point(727, 3);
            this.pnlExifData.Name = "pnlExifData";
            this.pnlExifData.Size = new System.Drawing.Size(175, 390);
            this.pnlExifData.TabIndex = 9;
            // 
            // lblExifData
            // 
            this.lblExifData.AutoSize = true;
            this.lblExifData.Location = new System.Drawing.Point(0, 0);
            this.lblExifData.Name = "lblExifData";
            this.lblExifData.Size = new System.Drawing.Size(30, 13);
            this.lblExifData.TabIndex = 8;
            this.lblExifData.Text = "EXIF";
            // 
            // lsbExifData
            // 
            this.lsbExifData.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lsbExifData.FormattingEnabled = true;
            this.lsbExifData.Location = new System.Drawing.Point(0, 19);
            this.lsbExifData.Name = "lsbExifData";
            this.lsbExifData.Size = new System.Drawing.Size(175, 368);
            this.lsbExifData.TabIndex = 7;
            // 
            // stsMain
            // 
            this.stsMain.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tslCamera});
            this.stsMain.Location = new System.Drawing.Point(0, 648);
            this.stsMain.Name = "stsMain";
            this.stsMain.Size = new System.Drawing.Size(911, 22);
            this.stsMain.TabIndex = 4;
            this.stsMain.Text = "statusStrip1";
            // 
            // tslCamera
            // 
            this.tslCamera.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.tslCamera.Name = "tslCamera";
            this.tslCamera.Size = new System.Drawing.Size(52, 17);
            this.tslCamera.Text = "Camera:";
            // 
            // ofdImage
            // 
            this.ofdImage.FileName = "openFileDialog1";
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(911, 670);
            this.Controls.Add(this.stsMain);
            this.Controls.Add(this.pnlMain);
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Picture It";
            this.pnlMain.ResumeLayout(false);
            this.tplMain.ResumeLayout(false);
            this.tlpFile.ResumeLayout(false);
            this.tlpFile.PerformLayout();
            this.tplBody.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.pcbImage)).EndInit();
            this.pnlMetaData.ResumeLayout(false);
            this.pnlMetaData.PerformLayout();
            this.pnlExifData.ResumeLayout(false);
            this.pnlExifData.PerformLayout();
            this.stsMain.ResumeLayout(false);
            this.stsMain.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Panel pnlMain;
        private System.Windows.Forms.TableLayoutPanel tplMain;
        private System.Windows.Forms.TableLayoutPanel tlpFile;
        private System.Windows.Forms.Button btnGetData;
        private System.Windows.Forms.Button btnBrowse;
        private System.Windows.Forms.TextBox txtFilePath;
        private System.Windows.Forms.StatusStrip stsMain;
        private System.Windows.Forms.OpenFileDialog ofdImage;
        private System.Windows.Forms.ToolStripStatusLabel tslCamera;
        private System.Windows.Forms.TableLayoutPanel tplBody;
        private System.Windows.Forms.PictureBox pcbImage;
        private System.Windows.Forms.Panel pnlMetaData;
        private System.Windows.Forms.Label lblMetaData;
        private System.Windows.Forms.ListBox lsbMetaData;
        private System.Windows.Forms.Panel pnlExifData;
        private System.Windows.Forms.Label lblExifData;
        private System.Windows.Forms.ListBox lsbExifData;
    }
}


using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PictureIt
{
    public partial class MainForm : Form
    {
        PropertyItem[] propItems;

        public MainForm()
        {
            InitializeComponent();
        }


        private void btnBrowse_Click(object sender, EventArgs e)
        {
            if (ofdImage.ShowDialog() == DialogResult.OK)
            {
                txtFilePath.Text = ofdImage.FileName;
                pcbImage.Image = Image.FromFile(ofdImage.FileName);
            }
            
        }

        private void btnGetData_Click(object sender, EventArgs e)
        {
            int propCount = 0;
            ASCIIEncoding encodings = new ASCIIEncoding();
            propItems = pcbImage.Image.PropertyItems;

            for(propCount = 0; propCount < propItems.Length; propCount++)
            {
                PropertyItem item = propItems[propCount];
                lsbData.Items.Add("Property Item " + propCount.ToString());
                lsbData.Items.Add("ID: 0x" + item.Id.ToString("x"));
                lsbData.Items.Add("Property Type " + item.Type);
                lsbData.Items.Add("Property Value " + encodings.GetString(item.Value));
                if (propCount == 1) // Camera
                {
                    tslCamera.Text = "Camera: " + encodings.GetString(propItems[1].Value);
                }
            }

        }
    }
}

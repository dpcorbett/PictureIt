using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
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
            if (pcbImage.Image == null)
            {
                MessageBox.Show("Please select an image.", "No Picture Loaded", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return
                    ;
            }

            int propCount = 0;
            ASCIIEncoding encodings = new ASCIIEncoding();
            propItems = pcbImage.Image.PropertyItems;
            // Clear existing data.
            lsbMetaData.Items.Clear();
            for(propCount = 0; propCount < propItems.Length; propCount++)
            {
                PropertyItem item = propItems[propCount];
                if (item.Type == 2 && encodings.GetString(item.Value).Trim() != string.Empty)
                {
                    lsbMetaData.Items.Add("Property Item " + propCount.ToString());
                    lsbMetaData.Items.Add("ID: 0x" + item.Id.ToString("x"));
                    lsbMetaData.Items.Add("Property Type " + item.Type);
                    lsbMetaData.Items.Add("Property Value " + encodings.GetString(item.Value));
                }
                if (propCount == 1) // Camera
                {
                    tslCamera.Text = "Camera: " + encodings.GetString(propItems[1].Value);
                }
            }

        }
    }
}

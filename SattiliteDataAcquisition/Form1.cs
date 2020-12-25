using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO.Ports;
using System.IO;
using System.Timers;
using System.Windows.Forms.DataVisualization.Charting;
using StackExchange.Redis;

namespace SattiliteDataAcquisition
{
    public partial class Form1 : Form
    {
        private SerialPort comPort;
        private System.Timers.Timer timer;
        private string path;
        private FileStream fileStream;
        private byte[] buffer;
        private Object locker;
        private ConnectionMultiplexer redis;

        private List<MeterCom> sattiliteComList;

        private List<SerialPortConfig> configList;

        public Form1()
        {
            InitializeComponent();
            redis = ConnectionMultiplexer.Connect("127.0.0.1,abortConnect=false");
            this.timer = new System.Timers.Timer(1000 * 60);
            this.timer.Elapsed += new ElapsedEventHandler(TimerTimeout);
            this.buffer = new byte[1024];
            this.locker = new Object();
            this.buttonOpen.Enabled = true;
            this.buttonClose.Enabled = false;
            this.path = @"D:\Work";
            this.configList = new List<SerialPortConfig>();
            this.sattiliteComList = new List<MeterCom>();

            StreamReader sr = new StreamReader("config.csv", Encoding.UTF8);
            String line;

            char[] chs = { ',' };
            while ((line = sr.ReadLine()) != null)
            {
                string[] items = line.Split(chs);
                //COM23,9600,None,8,1,牛栏江桥上行线7#桥墩
                SerialPortConfig config = new SerialPortConfig(items[0], items[1], items[2], items[3], items[4], items[6]);
                MeterCom sc = new MeterCom(config, redis, this);
                this.configList.Add(config);
                this.sattiliteComList.Add(sc);

                ListViewItem listItem = new ListViewItem(items);
                this.listView1.Items.Add(listItem);
            }
            sr.Close();

            chart1.Series.Add("工况瞬时流量");
            chart1.Series["工况瞬时流量"].ChartType = SeriesChartType.Line;
            //chart1.Series["工况瞬时流量"].IsValueShownAsLabel = true;

            chart1.Series.Add("标况瞬时流量");
            chart1.Series["标况瞬时流量"].ChartType = SeriesChartType.Line;
            //chart1.Series["标况瞬时流量"].IsValueShownAsLabel = true;

            chart1.Series.Add("燃气温度");
            chart1.Series["燃气温度"].ChartType = SeriesChartType.Line;
            //chart1.Series["燃气温度"].IsValueShownAsLabel = true;

            chart1.Series.Add("燃气绝对压力");
            chart1.Series["燃气绝对压力"].ChartType = SeriesChartType.Line;
            chart1.Series["燃气温度"].YAxisType = AxisType.Secondary;
            //chart1.Series["燃气绝对压力"].IsValueShownAsLabel = true;


            chart1.ChartAreas[0].AxisY2.Enabled = AxisEnabled.True;
        }

        

        private void TimerTimeout(object sender, System.Timers.ElapsedEventArgs e)
        {
            lock (this.locker)
            {
                //关闭文件
                this.fileStream.Flush();
                this.fileStream.Close();

                //创建新文件
                string fileName = DateTime.Now.ToString() + ".rtcm";

                fileName = fileName.Replace('/', '-');
                fileName = fileName.Replace(':', '-');

                string pathString = Path.Combine(this.path, fileName);

                this.fileStream = new FileStream(pathString, FileMode.Create);
            }
            
        }

        private void buttonOpen_Click(object sender, EventArgs e)
        {
            if(comPort == null)
            {
                string portName = this.textBoxPortName.Text;
                comPort = new SerialPort(portName, 38400, Parity.None, 8, StopBits.One);
                comPort.DataReceived += new SerialDataReceivedEventHandler(ComDataReceive);
            }
            
            comPort.Open();

           string fileName = DateTime.Now.ToString()+".rtcm";

           fileName = fileName.Replace('/', '-');
           fileName = fileName.Replace(':', '-');

            string pathString = Path.Combine(this.path, fileName); 

            this.fileStream = new FileStream(pathString,FileMode.Create);

            this.timer.Start();

            this.buttonOpen.Enabled = false;
            this.buttonClose.Enabled = true;
        }

        private void ComDataReceive(object sender, SerialDataReceivedEventArgs e)
        {

            string message = DateTime.Now.ToString()+" Received " + comPort.BytesToRead + " \r\n";
            
            lock (this.locker)
            {
                int bytesToRead = comPort.BytesToRead;
                comPort.Read(this.buffer, 0, bytesToRead);
                this.fileStream.Write(this.buffer, 0, bytesToRead);
            }

            this.Invoke((EventHandler)(
                        delegate {
                            textBox2.AppendText(message);
                        }));
        }

        public void AppendLog(string message)
        {
            this.Invoke((EventHandler)(
                        delegate {
                            textBox2.AppendText(message + "\r\n");
                        }));
        }

        public void AppendChartData(string chartName,double data,DateTime date)
        {
            chart1.BeginInvoke(new MethodInvoker(() =>
            {
                //chart1.Series[chartName].Points.AddXY(date, data);
                chart1.Series[chartName].Points.AddY( Math.Round(data,3));
                if (chart1.Series[chartName].Points.Count > 512)
                {
                    chart1.Series[chartName].Points.RemoveAt(0);
                }
                
            }));

            
            
        }

        private void buttonClose_Click(object sender, EventArgs e)
        {
            if(comPort != null)
            {
                comPort.Close();
            }

            timer.Stop();

            this.fileStream.Flush();
            this.fileStream.Close();

            this.buttonOpen.Enabled = true;
            this.buttonClose.Enabled = false;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            //string pathString = Path.Combine(this.path, "SubFolder");
            //Directory.CreateDirectory(pathString);
        }

        private void buttonStartBatch_Click(object sender, EventArgs e)
        {
            this.groupBox3.Enabled = false;
            this.buttonStartBatch.Enabled = false;
            this.buttonStopBatch.Enabled = true;
            foreach(MeterCom sc in this.sattiliteComList)
            {
                sc.Start();
            }
        }

        private void buttonSelectFolder_Click(object sender, EventArgs e)
        {
            //string pathString = Path.Combine(this.path, "SubFolder");
            //Directory.CreateDirectory(pathString);
            //FolderBrowserDialog fbd = new FolderBrowserDialog();
            //fbd.ShowDialog();

            //string pathString = Path.Combine(fbd.SelectedPath, "SubFolder");
            //Directory.CreateDirectory(pathString);

            //this.textBox2.Text = pathString;

            StreamReader sr = new StreamReader("config.csv", Encoding.UTF8);
            String line;

            char[] chs = { ',' };
            while ((line = sr.ReadLine()) != null)
            {
                textBox2.AppendText(line + "\r\n");
                //string[] items = line.Split(chs);
                //Config config = new Config(items[0], items[0], items[0], items[0]);
                //deviceList.Add(config);
            }

            sr.Close();
        }

        private void buttonStopBatch_Click(object sender, EventArgs e)
        {
            this.groupBox3.Enabled = true;
            this.buttonStartBatch.Enabled = true;
            this.buttonStopBatch.Enabled = false;
            foreach (MeterCom sc in this.sattiliteComList)
            {
                sc.Stop();
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            // 注意判断关闭事件reason来源于窗体按钮，否则用菜单退出时无法退出!
            if (e.CloseReason == CloseReason.UserClosing)
            {
                //取消"关闭窗口"事件
                e.Cancel = true; // 取消关闭窗体 

                //使关闭时窗口向右下角缩小的效果
                this.WindowState = FormWindowState.Minimized;
                this.notifyIcon1.Visible = true;
                this.Hide();
                return;
            }
        }

        private void notifyIcon1_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (this.Visible)
            {
                this.WindowState = FormWindowState.Minimized;
                this.notifyIcon1.Visible = true;
                this.Hide();
            }
            else
            {
                this.Visible = true;
                this.WindowState = FormWindowState.Normal;
                this.Activate();
            }
        }

        private void RestoreToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Visible = true;
            this.WindowState = FormWindowState.Normal;
            this.notifyIcon1.Visible = true;
            this.Show();
        }

        private void ExitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("确定要退出？", "系统提示", MessageBoxButtons.YesNo, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1) == DialogResult.Yes)
            {
                buttonStopBatch_Click(null,null);
                this.notifyIcon1.Visible = false;
                this.Close();
                this.Dispose();
            }
        }
    }
}

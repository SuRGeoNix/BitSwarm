namespace WinFormsApp1
{
    partial class FrmMain
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
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
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FrmMain));
            this.input = new System.Windows.Forms.TextBox();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.downPath = new System.Windows.Forms.TextBox();
            this.maxCon = new System.Windows.Forms.TextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.label5 = new System.Windows.Forms.Label();
            this.label6 = new System.Windows.Forms.Label();
            this.label7 = new System.Windows.Forms.Label();
            this.label8 = new System.Windows.Forms.Label();
            this.label9 = new System.Windows.Forms.Label();
            this.maxThreads = new System.Windows.Forms.TextBox();
            this.peersFromTrackers = new System.Windows.Forms.TextBox();
            this.conTimeout = new System.Windows.Forms.TextBox();
            this.handTimeout = new System.Windows.Forms.TextBox();
            this.pieceTimeout = new System.Windows.Forms.TextBox();
            this.metaTimeout = new System.Windows.Forms.TextBox();
            this.label10 = new System.Windows.Forms.Label();
            this.output = new System.Windows.Forms.TextBox();
            this.button1 = new System.Windows.Forms.Button();
            this.statusStrip1 = new System.Windows.Forms.StatusStrip();
            this.toolStripStatusLabel1 = new System.Windows.Forms.ToolStripStatusLabel();
            this.bDownloaded = new System.Windows.Forms.ToolStripStatusLabel();
            this.toolStripStatusLabel3 = new System.Windows.Forms.ToolStripStatusLabel();
            this.bDropped = new System.Windows.Forms.ToolStripStatusLabel();
            this.toolStripStatusLabel5 = new System.Windows.Forms.ToolStripStatusLabel();
            this.pPeers = new System.Windows.Forms.ToolStripStatusLabel();
            this.toolStripStatusLabel7 = new System.Windows.Forms.ToolStripStatusLabel();
            this.pInqueue = new System.Windows.Forms.ToolStripStatusLabel();
            this.toolStripStatusLabel9 = new System.Windows.Forms.ToolStripStatusLabel();
            this.pConnecting = new System.Windows.Forms.ToolStripStatusLabel();
            this.toolStripStatusLabel11 = new System.Windows.Forms.ToolStripStatusLabel();
            this.pConnected = new System.Windows.Forms.ToolStripStatusLabel();
            this.toolStripStatusLabel14 = new System.Windows.Forms.ToolStripStatusLabel();
            this.pFailed = new System.Windows.Forms.ToolStripStatusLabel();
            this.toolStripStatusLabel16 = new System.Windows.Forms.ToolStripStatusLabel();
            this.pFailed1 = new System.Windows.Forms.ToolStripStatusLabel();
            this.toolStripStatusLabel18 = new System.Windows.Forms.ToolStripStatusLabel();
            this.pFailed2 = new System.Windows.Forms.ToolStripStatusLabel();
            this.toolStripStatusLabel19 = new System.Windows.Forms.ToolStripStatusLabel();
            this.pChoked = new System.Windows.Forms.ToolStripStatusLabel();
            this.toolStripStatusLabel13 = new System.Windows.Forms.ToolStripStatusLabel();
            this.pUnchocked = new System.Windows.Forms.ToolStripStatusLabel();
            this.toolStripStatusLabel23 = new System.Windows.Forms.ToolStripStatusLabel();
            this.pDownloading = new System.Windows.Forms.ToolStripStatusLabel();
            this.progress = new System.Windows.Forms.ToolStripProgressBar();
            this.label11 = new System.Windows.Forms.Label();
            this.label12 = new System.Windows.Forms.Label();
            this.label13 = new System.Windows.Forms.Label();
            this.label14 = new System.Windows.Forms.Label();
            this.downRate = new System.Windows.Forms.Label();
            this.downRateAvg = new System.Windows.Forms.Label();
            this.etaCur = new System.Windows.Forms.Label();
            this.etaAvg = new System.Windows.Forms.Label();
            this.label15 = new System.Windows.Forms.Label();
            this.maxRate = new System.Windows.Forms.Label();
            this.label16 = new System.Windows.Forms.Label();
            this.eta = new System.Windows.Forms.Label();
            this.listBox1 = new System.Windows.Forms.ListBox();
            this.button2 = new System.Windows.Forms.Button();
            this.label17 = new System.Windows.Forms.Label();
            this.label18 = new System.Windows.Forms.Label();
            this.sleepLimit = new System.Windows.Forms.TextBox();
            this.statusStrip1.SuspendLayout();
            this.SuspendLayout();
            // 
            // input
            // 
            this.input.Location = new System.Drawing.Point(167, 15);
            this.input.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.input.Name = "input";
            this.input.Size = new System.Drawing.Size(954, 23);
            this.input.TabIndex = 2;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(66, 48);
            this.label1.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(88, 15);
            this.label1.TabIndex = 3;
            this.label1.Text = "Download Path";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(8, 18);
            this.label2.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(142, 15);
            this.label2.TabIndex = 4;
            this.label2.Text = "Torrent File / Manget Link";
            // 
            // downPath
            // 
            this.downPath.Location = new System.Drawing.Point(167, 45);
            this.downPath.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.downPath.Name = "downPath";
            this.downPath.Size = new System.Drawing.Size(954, 23);
            this.downPath.TabIndex = 5;
            // 
            // maxCon
            // 
            this.maxCon.Location = new System.Drawing.Point(167, 75);
            this.maxCon.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.maxCon.Name = "maxCon";
            this.maxCon.Size = new System.Drawing.Size(90, 23);
            this.maxCon.TabIndex = 6;
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(308, 78);
            this.label3.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(72, 15);
            this.label3.TabIndex = 7;
            this.label3.Text = "Min Threads";
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(79, 78);
            this.label4.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(74, 15);
            this.label4.TabIndex = 8;
            this.label4.Text = "Max Threads";
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(738, 78);
            this.label5.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(106, 15);
            this.label5.TabIndex = 9;
            this.label5.Text = "Peers From Tracker";
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(41, 108);
            this.label6.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(116, 15);
            this.label6.TabIndex = 10;
            this.label6.Text = "Connection Timeout";
            // 
            // label7
            // 
            this.label7.AutoSize = true;
            this.label7.Location = new System.Drawing.Point(265, 108);
            this.label7.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(113, 15);
            this.label7.TabIndex = 11;
            this.label7.Text = "Handshake Timeout";
            // 
            // label8
            // 
            this.label8.AutoSize = true;
            this.label8.Location = new System.Drawing.Point(490, 108);
            this.label8.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(82, 15);
            this.label8.TabIndex = 12;
            this.label8.Text = "Piece Timeout";
            // 
            // label9
            // 
            this.label9.AutoSize = true;
            this.label9.Location = new System.Drawing.Point(712, 108);
            this.label9.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label9.Name = "label9";
            this.label9.Size = new System.Drawing.Size(135, 15);
            this.label9.TabIndex = 13;
            this.label9.Text = "Metadata Piece Timeout";
            // 
            // maxThreads
            // 
            this.maxThreads.Location = new System.Drawing.Point(392, 75);
            this.maxThreads.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.maxThreads.Name = "maxThreads";
            this.maxThreads.Size = new System.Drawing.Size(90, 23);
            this.maxThreads.TabIndex = 14;
            // 
            // peersFromTrackers
            // 
            this.peersFromTrackers.Location = new System.Drawing.Point(862, 75);
            this.peersFromTrackers.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.peersFromTrackers.Name = "peersFromTrackers";
            this.peersFromTrackers.Size = new System.Drawing.Size(90, 23);
            this.peersFromTrackers.TabIndex = 15;
            // 
            // conTimeout
            // 
            this.conTimeout.Location = new System.Drawing.Point(167, 105);
            this.conTimeout.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.conTimeout.Name = "conTimeout";
            this.conTimeout.Size = new System.Drawing.Size(90, 23);
            this.conTimeout.TabIndex = 16;
            // 
            // handTimeout
            // 
            this.handTimeout.Location = new System.Drawing.Point(392, 105);
            this.handTimeout.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.handTimeout.Name = "handTimeout";
            this.handTimeout.Size = new System.Drawing.Size(90, 23);
            this.handTimeout.TabIndex = 17;
            // 
            // pieceTimeout
            // 
            this.pieceTimeout.Location = new System.Drawing.Point(614, 105);
            this.pieceTimeout.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.pieceTimeout.Name = "pieceTimeout";
            this.pieceTimeout.Size = new System.Drawing.Size(90, 23);
            this.pieceTimeout.TabIndex = 18;
            // 
            // metaTimeout
            // 
            this.metaTimeout.Location = new System.Drawing.Point(862, 105);
            this.metaTimeout.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.metaTimeout.Name = "metaTimeout";
            this.metaTimeout.Size = new System.Drawing.Size(90, 23);
            this.metaTimeout.TabIndex = 19;
            // 
            // label10
            // 
            this.label10.AutoSize = true;
            this.label10.Location = new System.Drawing.Point(614, 161);
            this.label10.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label10.Name = "label10";
            this.label10.Size = new System.Drawing.Size(45, 15);
            this.label10.TabIndex = 20;
            this.label10.Text = "Output";
            // 
            // output
            // 
            this.output.Location = new System.Drawing.Point(617, 179);
            this.output.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.output.Multiline = true;
            this.output.Name = "output";
            this.output.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.output.Size = new System.Drawing.Size(504, 247);
            this.output.TabIndex = 21;
            // 
            // button1
            // 
            this.button1.Location = new System.Drawing.Point(392, 449);
            this.button1.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(422, 65);
            this.button1.TabIndex = 22;
            this.button1.Text = "Start";
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += new System.EventHandler(this.button1_Click);
            // 
            // statusStrip1
            // 
            this.statusStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripStatusLabel1,
            this.bDownloaded,
            this.toolStripStatusLabel3,
            this.bDropped,
            this.toolStripStatusLabel5,
            this.pPeers,
            this.toolStripStatusLabel7,
            this.pInqueue,
            this.toolStripStatusLabel9,
            this.pConnecting,
            this.toolStripStatusLabel11,
            this.pConnected,
            this.toolStripStatusLabel14,
            this.pFailed,
            this.toolStripStatusLabel16,
            this.pFailed1,
            this.toolStripStatusLabel18,
            this.pFailed2,
            this.toolStripStatusLabel19,
            this.pChoked,
            this.toolStripStatusLabel13,
            this.pUnchocked,
            this.toolStripStatusLabel23,
            this.pDownloading,
            this.progress});
            this.statusStrip1.Location = new System.Drawing.Point(0, 530);
            this.statusStrip1.Name = "statusStrip1";
            this.statusStrip1.Padding = new System.Windows.Forms.Padding(1, 0, 16, 0);
            this.statusStrip1.Size = new System.Drawing.Size(1133, 24);
            this.statusStrip1.TabIndex = 23;
            this.statusStrip1.Text = "statusStrip1";
            // 
            // toolStripStatusLabel1
            // 
            this.toolStripStatusLabel1.Name = "toolStripStatusLabel1";
            this.toolStripStatusLabel1.Size = new System.Drawing.Size(74, 19);
            this.toolStripStatusLabel1.Text = "Downloaded";
            // 
            // bDownloaded
            // 
            this.bDownloaded.Name = "bDownloaded";
            this.bDownloaded.Size = new System.Drawing.Size(13, 19);
            this.bDownloaded.Text = "0";
            // 
            // toolStripStatusLabel3
            // 
            this.toolStripStatusLabel3.Name = "toolStripStatusLabel3";
            this.toolStripStatusLabel3.Size = new System.Drawing.Size(53, 19);
            this.toolStripStatusLabel3.Text = "Dropped";
            // 
            // bDropped
            // 
            this.bDropped.Name = "bDropped";
            this.bDropped.Size = new System.Drawing.Size(13, 19);
            this.bDropped.Text = "0";
            // 
            // toolStripStatusLabel5
            // 
            this.toolStripStatusLabel5.Name = "toolStripStatusLabel5";
            this.toolStripStatusLabel5.Size = new System.Drawing.Size(35, 19);
            this.toolStripStatusLabel5.Text = "Peers";
            // 
            // pPeers
            // 
            this.pPeers.Name = "pPeers";
            this.pPeers.Size = new System.Drawing.Size(13, 19);
            this.pPeers.Text = "0";
            // 
            // toolStripStatusLabel7
            // 
            this.toolStripStatusLabel7.Name = "toolStripStatusLabel7";
            this.toolStripStatusLabel7.Size = new System.Drawing.Size(52, 19);
            this.toolStripStatusLabel7.Text = "InQueue";
            // 
            // pInqueue
            // 
            this.pInqueue.Name = "pInqueue";
            this.pInqueue.Size = new System.Drawing.Size(13, 19);
            this.pInqueue.Text = "0";
            // 
            // toolStripStatusLabel9
            // 
            this.toolStripStatusLabel9.Name = "toolStripStatusLabel9";
            this.toolStripStatusLabel9.Size = new System.Drawing.Size(69, 19);
            this.toolStripStatusLabel9.Text = "Connecting";
            // 
            // pConnecting
            // 
            this.pConnecting.Name = "pConnecting";
            this.pConnecting.Size = new System.Drawing.Size(13, 19);
            this.pConnecting.Text = "0";
            // 
            // toolStripStatusLabel11
            // 
            this.toolStripStatusLabel11.Name = "toolStripStatusLabel11";
            this.toolStripStatusLabel11.Size = new System.Drawing.Size(65, 19);
            this.toolStripStatusLabel11.Text = "Connected";
            // 
            // pConnected
            // 
            this.pConnected.Name = "pConnected";
            this.pConnected.Size = new System.Drawing.Size(13, 19);
            this.pConnected.Text = "0";
            // 
            // toolStripStatusLabel14
            // 
            this.toolStripStatusLabel14.Name = "toolStripStatusLabel14";
            this.toolStripStatusLabel14.Size = new System.Drawing.Size(30, 19);
            this.toolStripStatusLabel14.Text = "DHT";
            // 
            // pFailed
            // 
            this.pFailed.Name = "pFailed";
            this.pFailed.Size = new System.Drawing.Size(13, 19);
            this.pFailed.Text = "0";
            // 
            // toolStripStatusLabel16
            // 
            this.toolStripStatusLabel16.Name = "toolStripStatusLabel16";
            this.toolStripStatusLabel16.Size = new System.Drawing.Size(61, 19);
            this.toolStripStatusLabel16.Text = "DHT Peers";
            // 
            // pFailed1
            // 
            this.pFailed1.Name = "pFailed1";
            this.pFailed1.Size = new System.Drawing.Size(13, 19);
            this.pFailed1.Text = "0";
            // 
            // toolStripStatusLabel18
            // 
            this.toolStripStatusLabel18.Name = "toolStripStatusLabel18";
            this.toolStripStatusLabel18.Size = new System.Drawing.Size(58, 19);
            this.toolStripStatusLabel18.Text = "TRK Peers";
            // 
            // pFailed2
            // 
            this.pFailed2.Name = "pFailed2";
            this.pFailed2.Size = new System.Drawing.Size(13, 19);
            this.pFailed2.Text = "0";
            // 
            // toolStripStatusLabel19
            // 
            this.toolStripStatusLabel19.Name = "toolStripStatusLabel19";
            this.toolStripStatusLabel19.Size = new System.Drawing.Size(48, 19);
            this.toolStripStatusLabel19.Text = "Choked";
            // 
            // pChoked
            // 
            this.pChoked.Name = "pChoked";
            this.pChoked.Size = new System.Drawing.Size(13, 19);
            this.pChoked.Text = "0";
            // 
            // toolStripStatusLabel13
            // 
            this.toolStripStatusLabel13.Name = "toolStripStatusLabel13";
            this.toolStripStatusLabel13.Size = new System.Drawing.Size(63, 19);
            this.toolStripStatusLabel13.Text = "UnChoked";
            // 
            // pUnchocked
            // 
            this.pUnchocked.Name = "pUnchocked";
            this.pUnchocked.Size = new System.Drawing.Size(13, 19);
            this.pUnchocked.Text = "0";
            // 
            // toolStripStatusLabel23
            // 
            this.toolStripStatusLabel23.Name = "toolStripStatusLabel23";
            this.toolStripStatusLabel23.Size = new System.Drawing.Size(78, 19);
            this.toolStripStatusLabel23.Text = "Downloading";
            // 
            // pDownloading
            // 
            this.pDownloading.Name = "pDownloading";
            this.pDownloading.Size = new System.Drawing.Size(13, 19);
            this.pDownloading.Text = "0";
            // 
            // progress
            // 
            this.progress.Name = "progress";
            this.progress.Size = new System.Drawing.Size(117, 18);
            // 
            // label11
            // 
            this.label11.AutoSize = true;
            this.label11.Location = new System.Drawing.Point(974, 440);
            this.label11.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label11.Name = "label11";
            this.label11.Size = new System.Drawing.Size(26, 15);
            this.label11.TabIndex = 24;
            this.label11.Text = "ETA";
            // 
            // label12
            // 
            this.label12.AutoSize = true;
            this.label12.Location = new System.Drawing.Point(974, 474);
            this.label12.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label12.Name = "label12";
            this.label12.Size = new System.Drawing.Size(50, 15);
            this.label12.TabIndex = 25;
            this.label12.Text = "ETA Avg";
            // 
            // label13
            // 
            this.label13.AutoSize = true;
            this.label13.Location = new System.Drawing.Point(12, 440);
            this.label13.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label13.Name = "label13";
            this.label13.Size = new System.Drawing.Size(87, 15);
            this.label13.TabIndex = 26;
            this.label13.Text = "Download Rate";
            // 
            // label14
            // 
            this.label14.AutoSize = true;
            this.label14.Location = new System.Drawing.Point(12, 474);
            this.label14.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label14.Name = "label14";
            this.label14.Size = new System.Drawing.Size(111, 15);
            this.label14.TabIndex = 27;
            this.label14.Text = "Download Rate Avg";
            // 
            // downRate
            // 
            this.downRate.AutoSize = true;
            this.downRate.Location = new System.Drawing.Point(157, 440);
            this.downRate.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.downRate.Name = "downRate";
            this.downRate.Size = new System.Drawing.Size(0, 15);
            this.downRate.TabIndex = 28;
            // 
            // downRateAvg
            // 
            this.downRateAvg.AutoSize = true;
            this.downRateAvg.Location = new System.Drawing.Point(157, 474);
            this.downRateAvg.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.downRateAvg.Name = "downRateAvg";
            this.downRateAvg.Size = new System.Drawing.Size(0, 15);
            this.downRateAvg.TabIndex = 29;
            // 
            // etaCur
            // 
            this.etaCur.AutoSize = true;
            this.etaCur.Location = new System.Drawing.Point(1060, 505);
            this.etaCur.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.etaCur.Name = "etaCur";
            this.etaCur.Size = new System.Drawing.Size(0, 15);
            this.etaCur.TabIndex = 30;
            // 
            // etaAvg
            // 
            this.etaAvg.AutoSize = true;
            this.etaAvg.Location = new System.Drawing.Point(1060, 474);
            this.etaAvg.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.etaAvg.Name = "etaAvg";
            this.etaAvg.Size = new System.Drawing.Size(0, 15);
            this.etaAvg.TabIndex = 31;
            // 
            // label15
            // 
            this.label15.AutoSize = true;
            this.label15.Location = new System.Drawing.Point(12, 505);
            this.label15.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label15.Name = "label15";
            this.label15.Size = new System.Drawing.Size(56, 15);
            this.label15.TabIndex = 32;
            this.label15.Text = "Max Rate";
            // 
            // maxRate
            // 
            this.maxRate.AutoSize = true;
            this.maxRate.Location = new System.Drawing.Point(157, 505);
            this.maxRate.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.maxRate.Name = "maxRate";
            this.maxRate.Size = new System.Drawing.Size(0, 15);
            this.maxRate.TabIndex = 33;
            // 
            // label16
            // 
            this.label16.AutoSize = true;
            this.label16.Location = new System.Drawing.Point(974, 505);
            this.label16.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label16.Name = "label16";
            this.label16.Size = new System.Drawing.Size(48, 15);
            this.label16.TabIndex = 34;
            this.label16.Text = "ETA Cur";
            // 
            // eta
            // 
            this.eta.AutoSize = true;
            this.eta.Location = new System.Drawing.Point(1060, 440);
            this.eta.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.eta.Name = "eta";
            this.eta.Size = new System.Drawing.Size(0, 15);
            this.eta.TabIndex = 35;
            // 
            // listBox1
            // 
            this.listBox1.FormattingEnabled = true;
            this.listBox1.ItemHeight = 15;
            this.listBox1.Location = new System.Drawing.Point(12, 182);
            this.listBox1.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.listBox1.Name = "listBox1";
            this.listBox1.SelectionMode = System.Windows.Forms.SelectionMode.MultiExtended;
            this.listBox1.Size = new System.Drawing.Size(578, 244);
            this.listBox1.TabIndex = 36;
            // 
            // button2
            // 
            this.button2.Location = new System.Drawing.Point(66, 146);
            this.button2.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.button2.Name = "button2";
            this.button2.Size = new System.Drawing.Size(158, 30);
            this.button2.TabIndex = 37;
            this.button2.Text = "Download Only Selected";
            this.button2.UseVisualStyleBackColor = true;
            this.button2.Click += new System.EventHandler(this.button2_Click);
            // 
            // label17
            // 
            this.label17.AutoSize = true;
            this.label17.Location = new System.Drawing.Point(8, 164);
            this.label17.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label17.Name = "label17";
            this.label17.Size = new System.Drawing.Size(30, 15);
            this.label17.TabIndex = 38;
            this.label17.Text = "Files";
            // 
            // label18
            // 
            this.label18.AutoSize = true;
            this.label18.Location = new System.Drawing.Point(490, 78);
            this.label18.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label18.Name = "label18";
            this.label18.Size = new System.Drawing.Size(100, 15);
            this.label18.TabIndex = 39;
            this.label18.Text = "Sleep Limit (KB/s)";
            // 
            // sleepLimit
            // 
            this.sleepLimit.Location = new System.Drawing.Point(614, 75);
            this.sleepLimit.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.sleepLimit.Name = "sleepLimit";
            this.sleepLimit.Size = new System.Drawing.Size(90, 23);
            this.sleepLimit.TabIndex = 40;
            // 
            // FrmMain
            // 
            this.AllowDrop = true;
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1133, 554);
            this.Controls.Add(this.sleepLimit);
            this.Controls.Add(this.label18);
            this.Controls.Add(this.label17);
            this.Controls.Add(this.button2);
            this.Controls.Add(this.eta);
            this.Controls.Add(this.label16);
            this.Controls.Add(this.maxRate);
            this.Controls.Add(this.label15);
            this.Controls.Add(this.etaAvg);
            this.Controls.Add(this.etaCur);
            this.Controls.Add(this.downRateAvg);
            this.Controls.Add(this.downRate);
            this.Controls.Add(this.label14);
            this.Controls.Add(this.label13);
            this.Controls.Add(this.label12);
            this.Controls.Add(this.label11);
            this.Controls.Add(this.statusStrip1);
            this.Controls.Add(this.button1);
            this.Controls.Add(this.output);
            this.Controls.Add(this.label10);
            this.Controls.Add(this.metaTimeout);
            this.Controls.Add(this.pieceTimeout);
            this.Controls.Add(this.handTimeout);
            this.Controls.Add(this.conTimeout);
            this.Controls.Add(this.peersFromTrackers);
            this.Controls.Add(this.maxThreads);
            this.Controls.Add(this.label9);
            this.Controls.Add(this.label8);
            this.Controls.Add(this.label7);
            this.Controls.Add(this.label6);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.maxCon);
            this.Controls.Add(this.downPath);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.input);
            this.Controls.Add(this.listBox1);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.Name = "FrmMain";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "BitSwarm 2.0";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.frmMain_FormClosing);
            this.Load += new System.EventHandler(this.frmMain_Load);
            this.DragDrop += new System.Windows.Forms.DragEventHandler(this.frmMain_DragDrop);
            this.DragEnter += new System.Windows.Forms.DragEventHandler(this.frmMain_DragEnter);
            this.statusStrip1.ResumeLayout(false);
            this.statusStrip1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.TextBox input;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.TextBox downPath;
        private System.Windows.Forms.TextBox maxCon;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.Label label8;
        private System.Windows.Forms.Label label9;
        private System.Windows.Forms.TextBox maxThreads;
        private System.Windows.Forms.TextBox peersFromTrackers;
        private System.Windows.Forms.TextBox conTimeout;
        private System.Windows.Forms.TextBox handTimeout;
        private System.Windows.Forms.TextBox pieceTimeout;
        private System.Windows.Forms.TextBox metaTimeout;
        private System.Windows.Forms.Label label10;
        private System.Windows.Forms.TextBox output;
        private System.Windows.Forms.Button button1;
        private System.Windows.Forms.StatusStrip statusStrip1;
        private System.Windows.Forms.ToolStripStatusLabel toolStripStatusLabel1;
        private System.Windows.Forms.ToolStripStatusLabel bDownloaded;
        private System.Windows.Forms.ToolStripStatusLabel toolStripStatusLabel3;
        private System.Windows.Forms.ToolStripStatusLabel bDropped;
        private System.Windows.Forms.ToolStripStatusLabel toolStripStatusLabel5;
        private System.Windows.Forms.ToolStripStatusLabel pPeers;
        private System.Windows.Forms.ToolStripStatusLabel toolStripStatusLabel7;
        private System.Windows.Forms.ToolStripStatusLabel pInqueue;
        private System.Windows.Forms.ToolStripStatusLabel toolStripStatusLabel9;
        private System.Windows.Forms.ToolStripStatusLabel pConnecting;
        private System.Windows.Forms.ToolStripStatusLabel toolStripStatusLabel11;
        private System.Windows.Forms.ToolStripStatusLabel pConnected;
        private System.Windows.Forms.ToolStripStatusLabel toolStripStatusLabel14;
        private System.Windows.Forms.ToolStripStatusLabel pFailed;
        private System.Windows.Forms.ToolStripStatusLabel toolStripStatusLabel16;
        private System.Windows.Forms.ToolStripStatusLabel pFailed1;
        private System.Windows.Forms.ToolStripStatusLabel toolStripStatusLabel18;
        private System.Windows.Forms.ToolStripStatusLabel pFailed2;
        private System.Windows.Forms.ToolStripStatusLabel toolStripStatusLabel19;
        private System.Windows.Forms.ToolStripStatusLabel pChoked;
        private System.Windows.Forms.ToolStripStatusLabel toolStripStatusLabel13;
        private System.Windows.Forms.ToolStripStatusLabel pUnchocked;
        private System.Windows.Forms.ToolStripStatusLabel toolStripStatusLabel23;
        private System.Windows.Forms.ToolStripStatusLabel pDownloading;
        private System.Windows.Forms.ToolStripProgressBar progress;
        private System.Windows.Forms.Label label11;
        private System.Windows.Forms.Label label12;
        private System.Windows.Forms.Label label13;
        private System.Windows.Forms.Label label14;
        private System.Windows.Forms.Label downRate;
        private System.Windows.Forms.Label downRateAvg;
        private System.Windows.Forms.Label etaCur;
        private System.Windows.Forms.Label etaAvg;
        private System.Windows.Forms.Label label15;
        private System.Windows.Forms.Label maxRate;
        private System.Windows.Forms.Label label16;
        private System.Windows.Forms.Label eta;
        private System.Windows.Forms.ListBox listBox1;
        private System.Windows.Forms.Button button2;
        private System.Windows.Forms.Label label17;
        private System.Windows.Forms.Label label18;
        private System.Windows.Forms.TextBox sleepLimit;
    }
}

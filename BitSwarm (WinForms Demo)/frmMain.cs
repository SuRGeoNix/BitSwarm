using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;

using SuRGeoNix;
using SuRGeoNix.BitSwarmLib;
using SuRGeoNix.BitSwarmLib.BEP;

namespace WinFormsApp1
{
    public partial class FrmMain : Form
    {
        static Torrent  torrent;
        static BitSwarm bitSwarm;
        static Options  opt;

        long requestedBytes = 0;
        public FrmMain()
        {
            InitializeComponent();
        }

        private void frmMain_Load(object sender, EventArgs e)
        {
            Options opt = new Options();

            downPath.Text           = opt.FolderComplete;
            maxCon.Text             = opt.MaxTotalConnections.ToString();
            maxThreads.Text         = opt.MaxNewConnections.ToString();
            peersFromTrackers.Text  = opt.PeersFromTracker.ToString();
            conTimeout.Text         = opt.ConnectionTimeout.ToString();
            handTimeout.Text        = opt.HandshakeTimeout.ToString();
            pieceTimeout.Text       = opt.PieceTimeout.ToString();
            metaTimeout.Text        = opt.MetadataTimeout.ToString();
        }
        private void button1_Click(object sender, EventArgs e)
        {
            if (button1.Text == "Start")
            {
                output.Text = "";
                listBox1.Items.Clear();
                button2.Enabled = false;

                try
                {
                    opt = new Options();

                    opt.FolderComplete      = downPath.Text;

                    opt.MaxTotalConnections = int.Parse(maxCon.Text);
                    opt.MaxNewConnections   = int.Parse(maxThreads.Text);
                    opt.PeersFromTracker    = int.Parse(peersFromTrackers.Text);
                    opt.ConnectionTimeout   = int.Parse(conTimeout.Text);
                    opt.HandshakeTimeout    = int.Parse(handTimeout.Text);
                    opt.PieceTimeout        = int.Parse(pieceTimeout.Text);
                    opt.MetadataTimeout     = int.Parse(metaTimeout.Text);

                    opt.Verbosity           = 0;
                    opt.LogDHT              = false;
                    opt.LogStats            = false;
                    opt.LogTracker          = false;
                    opt.LogPeer             = false;
                
                    output.Text     = "Started at " + DateTime.Now.ToString("G", DateTimeFormatInfo.InvariantInfo) + "\r\n";
                    button1.Text    = "Stop";

                    bitSwarm = new BitSwarm(opt);

                    bitSwarm.StatsUpdated       += BitSwarm_StatsUpdated;
                    bitSwarm.MetadataReceived   += BitSwarm_MetadataReceived;
                    bitSwarm.StatusChanged      += BitSwarm_StatusChanged;

                    bitSwarm.Open(input.Text);
                    bitSwarm.Start();
                }
                catch (Exception e1)
                {
                    output.Text += e1.Message + "\r\n" + e1.StackTrace;
                    button1.Text = "Start";
                    button2.Enabled = false;
                }

            } else
            {
                bitSwarm.Dispose();
                button1.Text = "Start";
            }
        }

        private void BitSwarm_MetadataReceived(object source, BitSwarm.MetadataReceivedArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => BitSwarm_MetadataReceived(source, e)));
                return;
            }
            else
            {
                torrent      = e.Torrent;
                output.Text += bitSwarm.DumpTorrent().Replace("\n", "\r\n");

                for (int i = 0; i < torrent.file.paths.Count; i++)
                    listBox1.Items.Add(torrent.file.paths[i]);

                listBox1.BeginUpdate();
                for (int i = 0; i < listBox1.Items.Count; i++)
                    listBox1.SetSelected(i, true);
                listBox1.EndUpdate();
                button2.Enabled = true;
            }
        }
        private void BitSwarm_StatusChanged(object source, BitSwarm.StatusChangedArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => BitSwarm_StatusChanged(this, e)));
                return;
            }

            button1.Text = "Start";

            if (e.Status == 0)
            {
                string fileName = "";
                if (torrent.file.name != null) fileName = torrent.file.name;
                if (torrent != null) { torrent.Dispose(); torrent = null; }

                output.Text += "\r\n\r\nFinished at "   + DateTime.Now.ToString("G", DateTimeFormatInfo.InvariantInfo);
                MessageBox.Show("Downloaded successfully!\r\n" + fileName);
            }
            else
            {
                output.Text += "\r\n\r\nStopped at "    + DateTime.Now.ToString("G", DateTimeFormatInfo.InvariantInfo);

                if (e.Status == 2)
                {
                    output.Text += "\r\n\r\n" + "An error occurred :(\r\n\t" + e.ErrorMsg;
                    MessageBox.Show("An error occured :( \r\n" + e.ErrorMsg);
                }
            }

            if (torrent != null) torrent.Dispose();
        }
        private void BitSwarm_StatsUpdated(object source, BitSwarm.StatsUpdatedArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => BitSwarm_StatsUpdated(source, e)));
                return;
            } 
            else
            {
                downRate.Text       = String.Format("{0:n0}", (e.Stats.DownRate / 1024)) + " KB/s";
                downRateAvg.Text    = String.Format("{0:n0}", (e.Stats.AvgRate  / 1024)) + " KB/s";
                maxRate.Text        = String.Format("{0:n0}", (e.Stats.MaxRate  / 1024)) + " KB/s";
                eta.Text            = TimeSpan.FromSeconds((e.Stats.ETA + e.Stats.AvgETA)/2).ToString(@"hh\:mm\:ss");
                etaAvg.Text         = TimeSpan.FromSeconds(e.Stats.AvgETA).ToString(@"hh\:mm\:ss");
                etaCur.Text         = TimeSpan.FromSeconds(e.Stats.ETA).ToString(@"hh\:mm\:ss");

                bDownloaded.Text    = Utils.BytesToReadableString(e.Stats.BytesDownloaded + e.Stats.BytesDownloadedPrevSession);
                bDropped.Text       = Utils.BytesToReadableString(e.Stats.BytesDropped);
                pPeers.Text         = e.Stats.PeersTotal.ToString();
                pInqueue.Text       = e.Stats.PeersInQueue.ToString();
                pConnecting.Text    = e.Stats.PeersConnecting.ToString();
                pConnected.Text     = (e.Stats.PeersConnecting + e.Stats.PeersConnected).ToString();
                pFailed.Text        = bitSwarm.isDHTRunning ? "On" : "Off";
                pFailed1.Text       = e.Stats.DHTPeers.ToString();
                pFailed2.Text       = e.Stats.TRKPeers.ToString();
                pChoked.Text        = e.Stats.PeersChoked.ToString();
                pUnchocked.Text     = e.Stats.PeersUnChoked.ToString();
                pDownloading.Text   = e.Stats.PeersDownloading.ToString();

                if (torrent != null && torrent.data.totalSize != 0) 
                    progress.Value = e.Stats.Progress;
                    //progress.Value = (int) (torrent.data.progress.setsCounter * 100.0 / torrent.data.progress.size);
                    //progress.Value  = (int) (stats.BytesDownloaded * 100.0 / torrent.data.totalSize);
            }

        }
        private void frmMain_DragEnter(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.All;
        }
        private void frmMain_DragDrop(object sender, DragEventArgs e)
        {
            Cursor = Cursors.Default;

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] filenames = (string[])e.Data.GetData(DataFormats.FileDrop, false);
                if (filenames.Length > 0) input.Text = filenames[0];
            } else
            {
                input.Text = e.Data.GetData(DataFormats.Text, false).ToString();
            }
        }
        private void frmMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (bitSwarm != null) bitSwarm.Dispose();
        }

        
        private void button2_Click(object sender, EventArgs e)
        {
            if (listBox1.SelectedItems.Count < 1 || torrent == null) return;

            List<string> fileNames = new List<string>();

            foreach (var o in listBox1.SelectedItems)
                fileNames.Add(o.ToString());
            
            bitSwarm.IncludeFiles(fileNames);

            requestedBytes = 0;
            for (int i=0; i<torrent.file.paths.Count; i++)
                foreach (string fileName in fileNames)
                    if (fileName == torrent.file.paths[i]) {  requestedBytes += torrent.file.lengths[i]; break; }

            output.Text += "\r\nNew Total Size Requested: " + Utils.BytesToReadableString(requestedBytes) + "\r\n";
        }
    }
}

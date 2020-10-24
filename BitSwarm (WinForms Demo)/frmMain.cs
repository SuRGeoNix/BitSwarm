using System;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using System.Collections.Generic;

using SuRGeoNix.BEP;

namespace SuRGeoNix.BitSwarmClient
{
    public partial class frmMain : Form
    {
        static Torrent                 torrent;
        static BitSwarm                bitSwarm;
        static BitSwarm.OptionsStruct  opt;

        long requestedBytes = 0;

        public frmMain()
        {
            InitializeComponent();
        }
        
        private void frmMain_Load(object sender, EventArgs e)
        {
            BitSwarm.OptionsStruct opt = BitSwarm.GetDefaultsOptions();

            downPath.Text           = opt.DownloadPath;
            maxCon.Text             = opt.MaxConnections.ToString();
            minThreads.Text         = opt.MinThreads.ToString();
            peersFromTrackers.Text  = opt.PeersFromTracker.ToString();
            conTimeout.Text         = opt.ConnectionTimeout.ToString();
            handTimeout.Text        = opt.HandshakeTimeout.ToString();
            pieceTimeout.Text       = opt.PieceTimeout.ToString();
            metaTimeout.Text        = opt.MetadataTimeout.ToString();
        }
        private void button1_Click(object sender, EventArgs e)
        {
            if ( button1.Text == "Start" )
            {
                output.Text = "";
                listBox1.Items.Clear();
                button2.Enabled = false;

                try
                {
                    opt = BitSwarm.GetDefaultsOptions();
                    
                    opt.DownloadPath        = downPath.Text;

                    opt.MaxConnections      = int.Parse(maxCon.Text);
                    opt.MinThreads          = int.Parse(minThreads.Text);
                    opt.PeersFromTracker    = int.Parse(peersFromTrackers.Text);
                    opt.ConnectionTimeout   = int.Parse(conTimeout.Text);
                    opt.HandshakeTimeout    = int.Parse(handTimeout.Text);
                    opt.PieceTimeout        = int.Parse(pieceTimeout.Text);
                    opt.MetadataTimeout     = int.Parse(metaTimeout.Text);

                    opt.EnableDHT           = true;

                    opt.Verbosity           = 0;
                    opt.LogDHT              = false;
                    opt.LogStats            = false;
                    opt.LogTracker          = false;
                    opt.LogPeer             = false;
                
                    output.Text     = "Started at " + DateTime.Now.ToString("G", DateTimeFormatInfo.InvariantInfo) + "\r\n";
                    button1.Text    = "Stop";

                    if (File.Exists(input.Text.Trim())) 
                        bitSwarm = new BitSwarm(input.Text.Trim(), opt);
                    else
                        bitSwarm = new BitSwarm(new Uri(input.Text.Trim()), opt);

                    bitSwarm.StatsUpdated       += BitSwarm_StatsUpdated;
                    bitSwarm.MetadataReceived   += BitSwarm_MetadataReceived;
                    bitSwarm.StatusChanged      += BitSwarm_StatusChanged;

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

        private void BitSwarm_StatusChanged2(object source, BitSwarm.StatusChangedArgs e)
        {
            throw new NotImplementedException();
        }

        private void BitSwarm_StatsUpdated2(object source, BitSwarm.StatsUpdatedArgs e)
        {
            throw new NotImplementedException();
        }

        private void BitSwarm_MetadataReceived2(object source, BitSwarm.MetadataReceivedArgs e)
        {
            throw new NotImplementedException();
        }

        private void BitSwarm_MetadataReceived(object source, BitSwarm.MetadataReceivedArgs e)
        {
            if ( InvokeRequired )
            {
                BeginInvoke(new Action(() => BitSwarm_MetadataReceived(source, e)));
                return;
            }
            else
            {
                torrent = e.Torrent;
                string str = "Name ->\t\t" + torrent.file.name + "\r\nSize ->\t\t" + Utils.BytesToReadableString(torrent.data.totalSize) + "\r\n\r\nFiles\r\n==============================\r\n";

                for (int i=0; i<torrent.data.files.Count; i++)
                {
                    str += torrent.data.files[i].FileName + "\t\t(" + Utils.BytesToReadableString(torrent.data.files[i].Size) + ")\r\n";
                    listBox1.Items.Add(torrent.file.paths[i]);
                }

                output.Text += str;

                listBox1.BeginUpdate();
                for (int i = 0; i < listBox1.Items.Count; i++)
                    listBox1.SetSelected(i, true);
                listBox1.EndUpdate();
                button2.Enabled = true;
            }
        }
        private void BitSwarm_StatusChanged(object source, BitSwarm.StatusChangedArgs e)
        {
            if ( InvokeRequired )
            {
                BeginInvoke(new Action(() => BitSwarm_StatusChanged(this, e)));
                return;
            }

            button1.Text = "Start";

            if (e.Status == 0)
            {
                output.Text += "\r\n\r\nFinished at "   + DateTime.Now.ToString("G", DateTimeFormatInfo.InvariantInfo);
                if ( torrent.file.name != null ) MessageBox.Show("Downloaded successfully!\r\n" + torrent.file.name);
            }
            else
            {
                output.Text += "\r\n\r\nStopped at "    + DateTime.Now.ToString("G", DateTimeFormatInfo.InvariantInfo);

                if (e.Status == 0)
                {
                    output.Text += "\r\n\r\n" + "An error occurred :(\r\n\t" + e.ErrorMsg;
                    MessageBox.Show("An error occured :( " + e.ErrorMsg);
                }
            }

            if (torrent != null) torrent.Dispose();
        }
        private void BitSwarm_StatsUpdated(object source, BitSwarm.StatsUpdatedArgs e)
        {
            if ( InvokeRequired )
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

                bDownloaded.Text    = Utils.BytesToReadableString(e.Stats.BytesDownloaded);
                bDropped.Text       = Utils.BytesToReadableString(e.Stats.BytesDropped);
                pPeers.Text         = e.Stats.PeersTotal.ToString();
                pInqueue.Text       = e.Stats.PeersInQueue.ToString();
                pConnected.Text     = e.Stats.PeersConnected.ToString();
                pFailed.Text        = (e.Stats.PeersFailed1 + e.Stats.PeersFailed2).ToString();
                pFailed1.Text       = e.Stats.PeersFailed1.ToString();
                pFailed2.Text       = e.Stats.PeersFailed2.ToString();
                pChoked.Text        = e.Stats.PeersChoked.ToString();
                pUnchocked.Text     = e.Stats.PeersUnChoked.ToString();
                pDownloading.Text   = e.Stats.PeersDownloading.ToString();
                pDropped.Text       = e.Stats.PeersDropped.ToString();

                if ( torrent != null && torrent.data.totalSize != 0) 
                    progress.Value  = (int) (torrent.data.progress.setsCounter * 100.0 / torrent.data.progress.size);
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
            if ( bitSwarm != null) bitSwarm.Dispose();
        }

        
        private void button2_Click(object sender, EventArgs e)
        {
            if ( listBox1.SelectedItems.Count < 1 || torrent == null ) return;

            List<string> fileNames = new List<string>();

            foreach (var o in listBox1.SelectedItems)
                fileNames.Add(o.ToString());
            
            bitSwarm.IncludeFiles(fileNames);

            requestedBytes = 0;
            for (int i=0; i<torrent.file.paths.Count; i++)
                foreach (string fileName in fileNames)
                    if ( fileName == torrent.file.paths[i] ) {  requestedBytes += torrent.file.lengths[i]; break; }

            output.Text += "\r\nNew Total Size Requested: " + Utils.BytesToReadableString(requestedBytes) + "\r\n";
        }
    }
}

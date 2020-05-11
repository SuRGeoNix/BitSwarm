/* DHT Protocol 
 * http://www.bittorrent.org/beps/bep_0005.html
 * 
 */
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

using BencodeNET.Objects;
using BencodeNET.Parsing;

namespace SuRGeoNix.TorSwarm
{
    public class DHT
    {
        public struct Options
        {
            public int MaxThreads           { get; set; }
            public int ConnectionTimeout    { get; set; }
            public int MaxBucketNodes       { get; set; }
            public int MinBucketDistance    { get; set; }
            public int MinBucketDistance2   { get; set; }
            public int NodesPerLevel        { get; set; }

            public int      Verbosity       { get; set; }
            public Logger   LogFile         { get; set; }


            public Action<Dictionary<string, int>>  NewPeersClbk;
        }
        public enum Status
        {
            RUNNING,
            PAUSED,
            STOPPED
        }

        private static readonly BencodeParser   bParser     = new BencodeParser();
        private static readonly Random          rnd         = new Random();
        private static readonly object          lockerNodes = new object();
        private static readonly object          lockerPeers = new object();

        private Dictionary<string, Node>        bucketNodesPointer;
        private Dictionary<string, Node>        bucketNodes;        // Weird
        private Dictionary<string, Node>        bucketNodes2;       // Normal
        private Dictionary<string, int>         cachedPeers;
        private Thread                          beggar;
        public Status                           status;

        private Options                         options;
        private string                          infoHash;
        private byte[]                          infoHashBytes;

        private byte[]                          getPeersBytes;
        private IPEndPoint                      ipEP;

        private int     havePeers, requested, responded, inBucket;
        private bool    isWeirdStrategy;
        private int     minBucketDistance;

        class Node
        {
            public Node(string host, int port, short distance = 160)
            {
                this.host       = host;
                this.port       = port;
                this.distance   = distance;
                this.status     = Status.NEW;
            }
            public enum Status
            {
                NEW,
                REQUESTING,
                REQUESTED,
                FAILED
            }

            public string   host;
            public int      port;
            public short    distance;
            public bool     hasPeers;

            public Status   status;
            public long     lastRequestedAt;

            public UdpClient udpClient;
        }
        public DHT(string infoHash, Options? opt = null)
        {
            options         = (opt == null) ? GetDefaultOptions() : (Options) opt;
            bucketNodes     = new Dictionary<string, Node>();
            bucketNodes2    = new Dictionary<string, Node>();
            cachedPeers     = new Dictionary<string, int>();
            ipEP            = new IPEndPoint(IPAddress.Any, 0);
            infoHashBytes   = Utils.StringHexToArray(infoHash);
            this.infoHash   = infoHash;

            status          = Status.STOPPED;

            // Add Bootstraps
            bucketNodesPointer = bucketNodes2;
            AddBootstrapNodes();
            bucketNodesPointer = bucketNodes;
            AddBootstrapNodes();

            // Start with Weird Strategy
            isWeirdStrategy = false;
            FlipStrategy();

            PrepareRequest();
        }
        public static Options GetDefaultOptions()
        {
            Options options = new Options();
            options.MaxThreads          = 5;
            options.ConnectionTimeout   = 600;
            options.MaxBucketNodes      = 200;
            options.MinBucketDistance   = 145;
            options.MinBucketDistance2  = 67;
            options.NodesPerLevel       = 4;
            
            return options;
        }

        private void AddNewNode(string host, int port, short distance = 160) 
        {  
            bucketNodesPointer.Add(host, new Node(host, port, distance)); 
        }
        private void AddBootstrapNodes()
        {
            //AddNewNode("dht.libtorrent.org"       , 25401);
            AddNewNode("router.bittorrent.com"    ,  6881);
            AddNewNode("router.utorrent.com"      ,  6881);
            //AddNewNode("dht.transmissionbt.com"   ,  6881);
            //AddNewNode("dht.aelitis.com"          ,  6881);
        }

        private void PrepareRequest()
        {
            /* BEndode get_peers Request
             * 
             * "t" -> <transId>, 
             * "y" -> "q", 
             * "q" -> "get_peers", 
             * "a" -> { "id" -> <nodeId> , "info_hash" -> <infoHashBytes> }
             * 
             */
            byte[] transId  = new byte[ 2];  rnd.NextBytes(transId);
            byte[] nodeId   = new byte[20]; rnd.NextBytes(nodeId);

            BDictionary bRequest    = new BDictionary();
            bRequest.Add("t",       new BString(transId));
            bRequest.Add("y", "q");
            bRequest.Add("q", "get_peers");

            BDictionary bDicA   =   new BDictionary();
            bDicA.Add("id",         new BString(nodeId));
            bDicA.Add("info_hash",  new BString(infoHashBytes));
            bRequest.Add("a", bDicA);

            getPeersBytes = bRequest.EncodeAsBytes();
        }
        private BDictionary GetResponse(Node node)
        {
            try
            {
                // Connect
                node.udpClient = new UdpClient();
                node.udpClient.AllowNatTraversal(true);
                node.udpClient.DontFragment = true;
                node.udpClient.Connect(node.host, node.port);

                // Send
                node.udpClient.Send(getPeersBytes, getPeersBytes.Length);
                node.udpClient.Send(getPeersBytes, getPeersBytes.Length);

                // Receive
                IAsyncResult asyncResult = node.udpClient.BeginReceive(null, null);
                asyncResult.AsyncWaitHandle.WaitOne(options.ConnectionTimeout);

                if ( !asyncResult.IsCompleted )
                {
                    node.udpClient.Close();
                    asyncResult = null;
                    return null;
                }

                byte[] recvBuff = node.udpClient.EndReceive(asyncResult, ref ipEP);
                node.udpClient.Close();
                asyncResult = null;

                return bParser.Parse<BDictionary>(recvBuff);

            } catch ( Exception ) { node.udpClient.Close(); return null;}
        }

        private void FlipStrategy()
        {
            isWeirdStrategy     = !isWeirdStrategy;
            minBucketDistance   = isWeirdStrategy ? options.MinBucketDistance : options.MinBucketDistance2;
            bucketNodesPointer  = isWeirdStrategy ? bucketNodes : bucketNodes2;

            Log($"[STRATEGY] Flip to {isWeirdStrategy}");
        }
        // The Right Way | Closest Nodes | Stable
        private short CalculateDistance2(byte[] nodeId)
        {
            short distance = 0;

            for ( int i=0; i<20; i++ )
            {
                if ( nodeId[i] != infoHashBytes[i] )
                {
                    string ab = Convert.ToString(nodeId[i], 2).PadLeft(8, '0');
                    string bb = Convert.ToString(infoHashBytes[i], 2).PadLeft(8, '0');

                    for ( int l=0; l<8; l++ )
                    {
                        if ( ab[l] != bb[l] )
                            distance += 1;
                    }
                }
            }

            return (short) distance;
        }
        // The Weird Way | More Like Our Hash Nodes | Faster
        private short CalculateDistance(byte[] nodeId)
        {
            short distance = 0;

            for ( int i=0; i<20; i++ )
            {
                if ( nodeId[i] != infoHashBytes[i] )
                {
                    string ab = Convert.ToString(nodeId[i], 2).PadLeft(8, '0');
                    string bb = Convert.ToString(infoHashBytes[i], 2).PadLeft(8, '0');

                    for ( int l=0; l<8; l++ )
                    {
                        if ( ab[l] != bb[l] )
                            { distance = (short) ((i * 8) + l + 1); break; }
                    }

                    break;
                }
            }

            if ( distance == 0 ) return 0;

            return (short) (160 - distance);
        }

        private string GetMinDistanceNode()
        {
            int curMin = 161;
            
            string host = null;
            foreach (KeyValuePair<string, Node> node in bucketNodesPointer)
                if ( node.Value.status == Node.Status.NEW && node.Value.distance < curMin ) { curMin = node.Value.distance; host = node.Value.host; }

            return host;
        }

        private void GetPeers(Node node)
        {
            Log($"[{node.distance}] [{node.host}] [REQ ]");
            requested++;

            try
            {
                /* BEndode get_peers Response
                 * 
                 * "t" -> <transId>, 
                 * "y" -> "r", 
                 * "r" -> { "id" -> <nodeId> , "token" -> <token> , "nodes" -> "nodeId + host + port...", "values" -> [host + port, ...] }
                 * 
                 */

                BDictionary bResponse = GetResponse(node);

                if ( bResponse == null || !bResponse.ContainsKey("y") || ((BString) bResponse["y"]).ToString() != "r" ) 
                    { node.status = Node.Status.FAILED; if ( bResponse != null ) bResponse.Clear(); return; }

                Log($"[{node.distance}] [{node.host}] [RESP]");

                bResponse = (BDictionary) bResponse["r"];

                // r -> Nodes 
                if ( bResponse.ContainsKey("nodes") )
                {
                    byte[] curNodes = ((BString) bResponse["nodes"]).Value.ToArray();

                    for ( int i=0; i<curNodes.Length; i += 26 )
                    {
                        byte[]  curNodeId   = Utils.ArraySub(ref curNodes, (uint) i, 20, false);
                        short   curDistance = isWeirdStrategy ? CalculateDistance(curNodeId) : CalculateDistance2(curNodeId);
                        string  curIP       = (new IPAddress(Utils.ArraySub(ref curNodes, (uint) i + 20, 4))).ToString();
                        UInt16  curPort     = (UInt16) BitConverter.ToInt16(Utils.ArraySub(ref curNodes, (uint) i + 24, 2, true), 0);
                    
                        if ( curPort < 100) continue; // Drop fake

                        Log($"[{node.distance}] [{node.host}] [NODE] [{curDistance}] {curIP}:{curPort}");

                        lock ( lockerNodes)
                        {
                            if ( !bucketNodesPointer.ContainsKey(curIP) )
                            {
                                //if ( curDistance <= minBucketDistance ) inBucket++;
                                AddNewNode(curIP, curPort, curDistance);
                            }
                        }
                    }
                }

                // r -> Peers
                if ( bResponse.ContainsKey("values") )
                {
                    BList values = (BList) bResponse["values"];

                    Dictionary<string, int> curPeers = new Dictionary<string, int>();

                    if ( values.Count > 0 )
                    {
                        node.hasPeers = true;
                        havePeers++;
                    }

                    foreach (IBObject cur in values)
                    {
                        byte[] value    = ((BString) cur).Value.ToArray();
                        string curIP    = (new IPAddress(Utils.ArraySub(ref value, 0, 4))).ToString();
                        UInt16 curPort  = (UInt16) BitConverter.ToInt16(Utils.ArraySub(ref value, 4, 2, true), 0);

                        if ( curPort < 100) continue; // Drop fake

                        Log($"[{node.distance}] [{node.host}] [PEER] {curIP}:{curPort}");

                        lock ( lockerPeers )
                        {
                            if ( cachedPeers.ContainsKey(curIP) ) continue;
                            cachedPeers.Add(curIP, curPort);
                        }

                        curPeers.Add(curIP, curPort);
                    }

                    if ( curPeers.Count > 0 )
                        options.NewPeersClbk?.BeginInvoke(curPeers, null, null);
                }

                node.status = Node.Status.REQUESTED;
                responded++;
                bResponse.Clear();
            } 
            catch ( Exception e )
            {
                node.status = Node.Status.FAILED;
                Log($"[{node.distance}] [{node.host}] [ERROR] {e.Message}\r\n{e.StackTrace}");
            }
        }
        private void Beggar()
        {
            Log($"[BEGGAR] STARTED {infoHash}");

            long lastTicks      = DateTime.UtcNow.Ticks;
            int curThreads      = 0;
            int curSeconds      = 0;
            bool clearBucket    = false;
            List<string> curNodeKeys;

            while ( status == Status.RUNNING )
            {
                // Prepare <NodesPerLevel> Nodes for Requesting
                curNodeKeys = new List<string>();
                for ( int i=0; i<options.NodesPerLevel; i++ )
                {
                    string newNodeHost = GetMinDistanceNode(); // Node.Status.New && Min(distance)
                    if ( newNodeHost == null) break;
                    bucketNodesPointer[newNodeHost].status             = Node.Status.REQUESTING;
                    bucketNodesPointer[newNodeHost].lastRequestedAt    = DateTime.UtcNow.Ticks;
                    curNodeKeys.Add(newNodeHost);
                }

                curThreads = curNodeKeys.Count;

                // End of Recursion | Reset This BucketNodes with Bootstraps and Flip Strategy
                if ( curThreads == 0 ) 
                { 
                    Log($"[BEGGAR] Recursion Ended (curThreads=0)... Flippping");
                    bucketNodesPointer.Clear();
                    AddBootstrapNodes();
                    FlipStrategy();

                    continue;
                }

                // Start <NodesPerLevel> Nodes & Wait For Them
                foreach (string curNodeKey in curNodeKeys)
                {
                    Node node = bucketNodesPointer[curNodeKey];

                    ThreadPool.QueueUserWorkItem(new WaitCallback(x => { 

                        GetPeers(node); 

                        Interlocked.Decrement(ref curThreads);

                    }), null);
                }

                while ( curThreads > 0 ) Thread.Sleep(15);

                // Clean Bucket from FAILED | REQUESTED | Out Of Distance
                inBucket = 0;
                curNodeKeys = new List<string>();
                
                if ( bucketNodesPointer.Count > options.MaxBucketNodes )
                    clearBucket = true;

                foreach ( KeyValuePair<string, Node> nodeKV in bucketNodesPointer )
                {
                    Node node = nodeKV.Value;
                    if ( node.status == Node.Status.FAILED) 
                        curNodeKeys.Add(node.host);
                    else if ( node.status == Node.Status.REQUESTED && node.distance > minBucketDistance && !node.hasPeers )
                        curNodeKeys.Add(node.host);
                    else if ( clearBucket && node.distance > minBucketDistance && !node.hasPeers)
                        curNodeKeys.Add(node.host);

                    if ( node.distance <= minBucketDistance ) inBucket++;
                }

                foreach (string curNodeKey in curNodeKeys)
                    bucketNodesPointer.Remove(curNodeKey);

                clearBucket = false;

                // Scheduler | NOTE: Currently not exact second & curSeconds will be reset with TorSwarm Stop/Start | Calculate 'Wait for Nodes' Time
                if ( DateTime.UtcNow.Ticks - lastTicks > 9000000 )
                {
                    lastTicks = DateTime.UtcNow.Ticks;
                    curSeconds++;

                    // Stats
                    Log($"[STATS] [REQs: {requested}]\t[RESPs: {responded}]\t[BUCKETSIZE: {bucketNodesPointer.Count}]\t[INBUCKET: {inBucket}]\t[PEERNODES: {havePeers}]\t[PEERS: {cachedPeers.Count}]");

                    // Flip Strategy
                    if ( curSeconds % 6 == 0 )
                        FlipStrategy();

                    // TODO: Re-request existing Peer Nodes for new Peers
                }

                Thread.Sleep(15);
            }

            Log($"[BEGGAR] STOPPED {infoHash}");
        }

        // Public
        public void Start()
        {
            if ( status == Status.RUNNING ) return;

            status = Status.RUNNING;

            beggar = new Thread(() =>
            {
                Beggar();
            });

            beggar.SetApartmentState(ApartmentState.STA);
            beggar.Start();
        }
        public void Stop()
        {
            if ( status == Status.STOPPED ) return;

            status = Status.STOPPED;
        }
        public void ClearCachedPeers()
        {
            lock ( lockerPeers ) cachedPeers.Clear();
            Log($"Cached Peers cleaned");
        }

        // Misc
        internal void Log(string msg) { if (options.Verbosity > 0) options.LogFile.Write($"[DHT] {msg}"); }
    }
}

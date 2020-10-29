/* DHT Protocol 
 * http://www.bittorrent.org/beps/bep_0005.html
 * 
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

using BencodeNET.Objects;
using BencodeNET.Parsing;

namespace SuRGeoNix.BEP
{
    public class DHT
    {
        public struct Options
        {
            public int ConnectionTimeout    { get; set; }
            public int MaxBucketNodes       { get; set; }
            public int MinBucketDistance    { get; set; }
            public int MinBucketDistance2   { get; set; }
            public int NodesPerLevel        { get; set; }

            public int      Verbosity       { get; set; }
            public Logger   LogFile         { get; set; }


            public Action<ConcurrentDictionary<string, int>, bool>  NewPeersClbk;
        }
        public enum Status
        {
            RUNNING,
            PAUSED,
            STOPPING,
            STOPPED
        }

        public Status                           status      { get; private set; }
        public  ConcurrentDictionary<string, int>CachedPeers { get; private set; }
        public long                             StartedAt   { get; private set; }
        public long                             StoppedAt   { get; private set; }


        private List<Node>                      bootStrapNodes;
        private Dictionary<string, Node>        bucketNodesPointer;
        private Dictionary<string, Node>        bucketNodes;        // Weird
        private Dictionary<string, Node>        bucketNodes2;       // Normal
        private Thread                          beggar;
        
        static readonly BencodeParser           bParser     = new BencodeParser();
        readonly Random                         rnd         = new Random();
        readonly object                         lockerNodes = new object();
        readonly object                         lockerPeers = new object();

        private Options                         options;
        private string                          infoHash;
        private byte[]                          infoHashBytes;

        private byte[]                          getPeersBytes;
        private IPEndPoint                      ipEP;

        private int     havePeers, requested, responded, inBucket;
        private bool    isWeirdStrategy;
        private int     minBucketDistance;

        public long     lastRootsAskedAt;
        private int     weirdPeers;
        private int     normalPeers;

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

            public void Dispose() { udpClient?.Dispose(); }
        }
        public DHT(string infoHash, Options? opt = null)
        {
            options         = (opt == null) ? GetDefaultOptions() : (Options) opt;
            bucketNodes     = new Dictionary<string, Node>();
            bucketNodes2    = new Dictionary<string, Node>();
            bootStrapNodes  = new List<Node>();
            CachedPeers     = new ConcurrentDictionary<string, int>();
            ipEP            = new IPEndPoint(IPAddress.Any, 0);
            infoHashBytes   = Utils.StringHexToArray(infoHash);
            this.infoHash   = infoHash;

            status          = Status.STOPPED;

            // Start with Weird Strategy
            isWeirdStrategy = false;
            FlipStrategy();

            PrepareRequest();
        }
        public static Options GetDefaultOptions()
        {
            Options options = new Options();
            options.ConnectionTimeout   = 180;
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
            List<Node> randomNodes;

            if (bucketNodesPointer.Count > 0) 
                Log("[ERROR] AddBootstrapNodes() call with bucketNodesPointer.Count > 0");

            if (bootStrapNodes.Count == 0 || DateTime.UtcNow.Ticks - lastRootsAskedAt > 8 * 1000 * 10000)
            {
                bootStrapNodes.Clear();
                lastRootsAskedAt = DateTime.UtcNow.Ticks;

                bootStrapNodes.Add(new Node("router.utorrent.com", 6881));
                bootStrapNodes.Add(new Node("dht.libtorrent.org", 25401));
                bootStrapNodes.Add(new Node("router.bittorrent.com", 6881));
                //bootStrapNodes.Add(new Node("dht.transmissionbt.com", 6881));
                //bootStrapNodes.Add(new Node("dht.aelitis.com", 6881));

                randomNodes = new List<Node>();
                foreach (var node in bootStrapNodes)
                    randomNodes.Add(node);

                while (status == Status.RUNNING && randomNodes.Count != 0)
                {
                    int cur = rnd.Next(0, randomNodes.Count);

                    GetPeers(randomNodes[cur]);
                    if (bucketNodesPointer.Count > 15)
                    {
                        bootStrapNodes.Clear();
                        foreach (var nodes in bucketNodesPointer)
                            bootStrapNodes.Add(new Node(nodes.Value.host, nodes.Value.port));

                        break;
                    }

                    randomNodes.RemoveAt(cur);
                }
            }
            else
            {
                randomNodes = new List<Node>();
                foreach (var node in bootStrapNodes)
                    randomNodes.Add(node);

                while (status == Status.RUNNING && randomNodes.Count != 0)
                {
                    int cur = rnd.Next(0, randomNodes.Count);

                    bucketNodesPointer.Add(randomNodes[cur].host, new Node(randomNodes[cur].host, randomNodes[cur].port));
                    if (bucketNodesPointer.Count == 8) break;

                    randomNodes.RemoveAt(cur);
                }
            }

            if (status != Status.RUNNING) return;

            if (bucketNodesPointer.Count == 0)
            {
                Log("[ERROR] Bootstrap Nodes Failed (Retrying)");
                Thread.Sleep(150);
                bootStrapNodes.Clear();
                AddBootstrapNodes();
                return;
            }
        }

        private void FlipStrategy()
        {
            isWeirdStrategy     = !isWeirdStrategy;
            minBucketDistance   = isWeirdStrategy ? options.MinBucketDistance : options.MinBucketDistance2;
            bucketNodesPointer  = isWeirdStrategy ? bucketNodes : bucketNodes2;

            Log($"[STRATEGY] Flip to {isWeirdStrategy}");
        }
        private string GetMinDistanceNode()
        {
            int curMin = 161;
            
            string host = null;
            foreach (KeyValuePair<string, Node> node in bucketNodesPointer)
                if (node.Value.status == Node.Status.NEW && node.Value.distance < curMin) { curMin = node.Value.distance; host = node.Value.host; }

            return host;
        }
        // The Right Way | Closest Nodes | Stable
        private short CalculateDistance2(byte[] nodeId)
        {
            short distance = 0;

            for (int i=0; i<20; i++)
            {
                if (nodeId[i] != infoHashBytes[i])
                {
                    string ab = Convert.ToString(nodeId[i], 2).PadLeft(8, '0');
                    string bb = Convert.ToString(infoHashBytes[i], 2).PadLeft(8, '0');

                    for (int l=0; l<8; l++)
                    {
                        if (ab[l] != bb[l])
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

            for (int i=0; i<20; i++)
            {
                if (nodeId[i] != infoHashBytes[i])
                {
                    string ab = Convert.ToString(nodeId[i], 2).PadLeft(8, '0');
                    string bb = Convert.ToString(infoHashBytes[i], 2).PadLeft(8, '0');

                    for (int l=0; l<8; l++)
                    {
                        if (ab[l] != bb[l])
                            { distance = (short) ((i * 8) + l + 1); break; }
                    }

                    break;
                }
            }

            if (distance == 0) return 0;

            return (short) (160 - distance);
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
            byte[] transId  = new byte[ 2]; rnd.NextBytes(transId);
            byte[] nodeId   = new byte[20]; rnd.NextBytes(nodeId);

            BDictionary bRequest=   new BDictionary();
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
                if (Utils.IsWindows)
                {
                    node.udpClient.AllowNatTraversal(true);
                    node.udpClient.DontFragment = true;
                }
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
        private void GetPeers(Node node, int selfRecursionLevel = 0)
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

                if (bResponse == null || !bResponse.ContainsKey("y") || ((BString) bResponse["y"]).ToString() != "r") 
                    { node.status = Node.Status.FAILED; if ( bResponse != null ) bResponse.Clear(); return; }

                Log($"[{node.distance}] [{node.host}] [RESP]");

                bResponse = (BDictionary) bResponse["r"];

                // r -> Nodes 
                if (bResponse.ContainsKey("nodes"))
                {
                    byte[] curNodes = ((BString) bResponse["nodes"]).Value.ToArray();

                    for (int i=0; i<curNodes.Length; i += 26)
                    {
                        byte[]  curNodeId   = Utils.ArraySub(ref curNodes, (uint) i, 20, false);
                        short   curDistance = isWeirdStrategy ? CalculateDistance(curNodeId) : CalculateDistance2(curNodeId);
                        string  curIP       = (new IPAddress(Utils.ArraySub(ref curNodes, (uint) i + 20, 4))).ToString();
                        UInt16  curPort     = (UInt16) BitConverter.ToInt16(Utils.ArraySub(ref curNodes, (uint) i + 24, 2, true), 0);
                    
                        if ( curPort < 100) continue; // Drop fake

                        Log($"[{node.distance}] [{node.host}] [NODE] [{curDistance}] {curIP}:{curPort}");

                        lock (lockerNodes) if ( !bucketNodesPointer.ContainsKey(curIP) ) AddNewNode(curIP, curPort, curDistance);
                    }
                }

                // r -> Peers
                if (bResponse.ContainsKey("values"))
                {
                    int newPeers = 0;

                    BList values = (BList) bResponse["values"];

                    ConcurrentDictionary<string, int> curPeers = new ConcurrentDictionary<string, int>();

                    if (values.Count > 0)
                    {

                        if (isWeirdStrategy)
                            weirdPeers += values.Count;
                        else
                            normalPeers += values.Count;

                        node.hasPeers = true;
                        havePeers++;
                    }

                    foreach (IBObject cur in values)
                    {
                        byte[] value    = ((BString) cur).Value.ToArray();
                        string curIP    = (new IPAddress(Utils.ArraySub(ref value, 0, 4))).ToString();
                        UInt16 curPort  = (UInt16) BitConverter.ToInt16(Utils.ArraySub(ref value, 4, 2, true), 0);

                        if (curPort < 100) continue; // Drop fake

                        Log($"[{node.distance}] [{node.host}] [PEER] {curIP}:{curPort}");

                        lock (lockerPeers)
                        {
                            if (CachedPeers.ContainsKey(curIP)) continue;
                            CachedPeers.TryAdd(curIP, curPort);
                            newPeers++;
                        }

                        curPeers.TryAdd(curIP, curPort);
                    }

                    if (curPeers.Count > 0) options.NewPeersClbk?.Invoke(curPeers, true);

                    Log($"[{node.distance}] [{node.host}] [NEW PEERS] {newPeers}");

                    // Re-requesting same Node with Peers > 99 (max returned peers are 100?)
                    // Possible fake/random peers (escape recursion? 50 loops?)
                    if (status == Status.RUNNING && newPeers > 2 && values.Count > 99)
                    {
                        if (selfRecursionLevel > 30)
                        {
                            Log($"[{node.distance}] [{node.host}] [RE-REQUEST LIMIT EXCEEDED]");
                        }
                        else
                        {
                            Log($"[{node.distance}] [{node.host}] [RE-REQUEST {selfRecursionLevel}] {newPeers}");
                            Thread.Sleep(10);
                            GetPeers(node, selfRecursionLevel++); 
                        }
                        
                    }
                }

                node.status = Node.Status.REQUESTED;
                responded++;
                bResponse.Clear();
            } 
            catch (Exception e)
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

            if (bucketNodesPointer.Count == 0) AddBootstrapNodes();

            while (status == Status.RUNNING)
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
                if (curThreads == 0) 
                { 
                    if (status != Status.RUNNING) break;

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

                while (curThreads > 0) Thread.Sleep(20);

                // Clean Bucket from FAILED | REQUESTED | Out Of Distance
                inBucket = 0;
                curNodeKeys = new List<string>();
                
                if (bucketNodesPointer.Count > options.MaxBucketNodes)
                    clearBucket = true;

                foreach (KeyValuePair<string, Node> nodeKV in bucketNodesPointer)
                {
                    Node node = nodeKV.Value;
                    if (node.status == Node.Status.FAILED) 
                        curNodeKeys.Add(node.host);
                    //else if (node.status == Node.Status.REQUESTED && node.distance > minBucketDistance)
                    else if (node.status == Node.Status.REQUESTED) // Currently we dont re-use In Bucket Nodes
                        curNodeKeys.Add(node.host);
                    else if (clearBucket && node.distance > minBucketDistance)
                        curNodeKeys.Add(node.host);

                    if (node.distance <= minBucketDistance) inBucket++;
                }

                foreach (string curNodeKey in curNodeKeys)
                    { bucketNodesPointer[curNodeKey].Dispose(); bucketNodesPointer.Remove(curNodeKey); }

                clearBucket = false;

                if (status != Status.RUNNING) break;

                // Scheduler | NOTE: Currently not exact second & curSeconds will be reset with BitSwarm Stop/Start | Calculate 'Wait for Nodes' Time
                // Also with addition for GetPeers Recursion this is not accurate at all (TBR)
                if (DateTime.UtcNow.Ticks - lastTicks > 9000000)
                {
                    lastTicks = DateTime.UtcNow.Ticks;
                    curSeconds++;

                    // Stats
                    Log($"[STATS] [REQs: {requested}]\t[RESPs: {responded}]\t[BUCKETSIZE: {bucketNodesPointer.Count}]\t[INBUCKET: {inBucket}]\t[PEERNODES: {havePeers}]\t[PEERS: {CachedPeers.Count}] | [WEIRD]: {weirdPeers} | [NORMAL] {normalPeers}");

                    // Flip Strategy
                    if (curSeconds % 6 == 0)
                        FlipStrategy();

                    // TODO: Re-request existing Peer Nodes for new Peers
                }

                Thread.Sleep(20);
            }

            Log($"[BEGGAR] STOPPED {infoHash}");
        }

        // Public
        public void Start()
        {
            if (status == Status.RUNNING || (beggar != null && beggar.IsAlive)) return;

            status = Status.RUNNING;

            beggar = new Thread(() =>
            {
                StartedAt = DateTime.UtcNow.Ticks;
                Beggar();
                status = Status.STOPPED;
                StoppedAt = DateTime.UtcNow.Ticks;
            });
            beggar.IsBackground = true;
            beggar.Start();
        }
        public void Stop()
        {
            if (status != Status.RUNNING) return;

            status = Status.STOPPING;
        }
        public void ClearCachedPeers()
        {
            lock (lockerPeers) CachedPeers.Clear();
            Log($"Cached Peers cleaned");
        }

        // Misc
        internal void Log(string msg) { if (options.Verbosity > 0) options.LogFile.Write($"[DHT] {msg}"); }
    }
}
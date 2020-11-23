namespace SuRGeoNix.BitSwarmLib
{
    public class Stats
    {
        public int      DownRate            { get; internal set; }
        public int      AvgRate             { get; internal set; }
        public int      MaxRate             { get; internal set; }

        public int      AvgETA              { get; internal set; }
        public int      ETA                 { get; internal set; }

        public int      Progress            { get; internal set; }
        public int      PiecesIncluded      { get; internal set; }
        public long     BytesIncluded       { get; internal set; }
        public long     BytesCurDownloaded  { get; internal set; } // Bytes Saved in Files

        public long     BytesDownloaded     { get; internal set; } // Bytes Saved in Files + Working Pieces
        public long     BytesDownloadedPrev { get; internal set; }
        public long     BytesDownloadedPrevSession  { get; internal set; } // Bytes Saved in Files Previously
        public long     BytesUploaded       { get; internal set; }
        public long     BytesDropped        { get; internal set; }

        public int      PeersTotal          { get; internal set; }
        public int      PeersInQueue        { get; internal set; }
        public int      PeersConnecting     { get; internal set; }
        public int      PeersConnected      { get; internal set; }
        public int      PeersFailed1        { get; internal set; }
        public int      PeersFailed2        { get; internal set; }
        public int      PeersFailed         { get; internal set; }
        public int      PeersChoked         { get; internal set; }
        public int      PeersUnChoked       { get; internal set; }
        public int      PeersDownloading    { get; internal set; }
        public int      PeersDropped        { get; internal set; }

        public long     StartTime           { get; internal set; }
        public long     CurrentTime         { get; internal set; }
        public long     EndTime             { get; internal set; }

        public bool     SleepMode           { get; internal set; }
        public bool     BoostMode           { get; internal set; }
        public bool     EndGameMode         { get; internal set; }

        public int      AlreadyReceived     { get; internal set; }

        public int      DHTPeers            { get; internal set; }
        public int      PEXPeers            { get; internal set; }
        public int      TRKPeers            { get; internal set; }
        
        public int      SHA1Failures        { get; internal set; }

        public int      Rejects;

        //public int      ConnectTimeouts; // Not used
        public int      HandshakeTimeouts;
        public int      PieceTimeouts;
    }
}

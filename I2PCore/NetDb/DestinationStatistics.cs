using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using I2PCore.Utils;
using System.Globalization;
using I2PCore.Tunnel;

namespace I2PCore
{
    public class DestinationStatistics: I2PType
    {
        const long DefaultTunnelBuildTimeMsPerHop = Tunnel.Tunnel.MeassuredTunnelBuildTimePerHopSeconds * 1200;

        public readonly I2PIdentHash Id;
        public I2PDate LastSeen;

        public long SuccessfulConnects;
        public long FailedConnects;
        public long InformationFaulty;
        public long SlowHandshakeConnect;

        public long TunnelBuildTimeout;
        public long FloodfillUpdateTimeout;
        public long FloodfillUpdateSuccess;

        public long SuccessfulTunnelMember;
        public long DeclinedTunnelMember;
        public long SuccessfulTunnelTest;
        public long FailedTunnelTest;
        public long TunnelBuildTimeMsPerHop = DefaultTunnelBuildTimeMsPerHop;

        public float MaxBandwidthSeen;

        public int StoreIx;

        public DestinationStatistics( I2PIdentHash id )
        {
            Id = id;
            LastSeen = new I2PDate( DateTime.UtcNow );
            UpdateScore();
        }

        const float MaxScore = 70f;
        const float MedTargetPeriods = 30f;

        float DiminishingReturns( float val )
        {
            return (float)( MaxScore - MaxScore / Math.Pow( 2, val / MedTargetPeriods ) );
        }

        float CachedScore;
        internal void UpdateScore()
        {
            var score = SuccessfulConnects * 1.0f - FailedConnects * 3.00f 
                - SlowHandshakeConnect * 0.5f;
            score += SuccessfulTunnelMember * 3.0f - DeclinedTunnelMember * 0.20f 
                - TunnelBuildTimeout * 2.0f
                + FloodfillUpdateSuccess * 1.0f - FloodfillUpdateTimeout * 3.0f;
            score += SuccessfulTunnelTest * 0.01f - FailedTunnelTest * 0.003f;

            CachedScore = score + MaxBandwidthSeen / 1E5f
                    - TunnelBuildTimeMsPerHop / 1000f
                    - InformationFaulty * 15.00f;
        }

        public float Score
        {
            get
            {
                return CachedScore;
            }
        }

        long TryGet( I2PMapping map, string ix )
        {
            try
            {
                return long.Parse( map[ix] );
            }
            catch ( Exception )
            {
                return 0;
            }
        }

        long TryGet( I2PMapping map, string ix, long def )
        {
            try
            {
                return long.Parse( map[ix] );
            }
            catch ( Exception )
            {
                return def;
            }
        }

        float TryGetFloat( I2PMapping map, string ix )
        {
            try
            {
                return float.Parse( map[ix], CultureInfo.InvariantCulture );
            }
            catch ( Exception )
            {
                return 0f;
            }
        }

        public DestinationStatistics( BufRef buf )
        {
            Id = new I2PIdentHash( buf );
            LastSeen = new I2PDate( buf );
            buf.Seek( 60 ); // Reserved space

            var mapping = new I2PMapping( buf );

            SuccessfulConnects = TryGet( mapping, "SuccessfulConnects" );
            FailedConnects = TryGet( mapping, "FailedConnects" );
            InformationFaulty = TryGet( mapping, "InformationFaulty" );
            SuccessfulTunnelMember = TryGet( mapping, "SuccessfulTunnelMember" );
            DeclinedTunnelMember = TryGet( mapping, "DeclinedTunnelMember" );
            SlowHandshakeConnect = TryGet( mapping, "SlowHandshakeConnect" );
            MaxBandwidthSeen = TryGetFloat( mapping, "MaxBandwidthSeen" );
            TunnelBuildTimeout = TryGet( mapping, "TunnelBuildTimeout" );
            TunnelBuildTimeMsPerHop = TryGet( mapping, "TunnelBuildTimeMsPerHop", DefaultTunnelBuildTimeMsPerHop );
            FloodfillUpdateTimeout = TryGet( mapping, "FloodfillUpdateTimeout" );
            FloodfillUpdateSuccess = TryGet( mapping, "FloodfillUpdateSuccess" );
            SuccessfulTunnelTest = TryGet( mapping, "SuccessfulTunnelTest" );
            FailedTunnelTest = TryGet( mapping, "FailedTunnelTest" );
        }

        private I2PMapping CreateMapping()
        {
            var mapping = new I2PMapping();

            mapping["SuccessfulConnects"] = SuccessfulConnects.ToString();
            mapping["FailedConnects"] = FailedConnects.ToString();
            mapping["InformationFaulty"] = InformationFaulty.ToString();
            mapping["SuccessfulTunnelMember"] = SuccessfulTunnelMember.ToString();
            mapping["DeclinedTunnelMember"] = DeclinedTunnelMember.ToString();
            mapping["SlowHandshakeConnect"] = SlowHandshakeConnect.ToString();
            mapping["MaxBandwidthSeen"] = MaxBandwidthSeen.ToString( CultureInfo.InvariantCulture );
            mapping["TunnelBuildTimeout"] = TunnelBuildTimeout.ToString();
            mapping["TunnelBuildTimeMsPerHop"] = TunnelBuildTimeMsPerHop.ToString();
            mapping["FloodfillUpdateTimeout"] = FloodfillUpdateTimeout.ToString();
            mapping["FloodfillUpdateSuccess"] = FloodfillUpdateSuccess.ToString();
            mapping["SuccessfulTunnelTest"] = SuccessfulTunnelTest.ToString();
            mapping["FailedTunnelTest"] = FailedTunnelTest.ToString();

            return mapping;
        }

        public void Write( BufRefStream dest )
        {
            Id.Write( dest );
            LastSeen.Write( dest );
            dest.Write( BufUtils.Random( 60 ) ); // Reserved space

            var mapping = CreateMapping();

            mapping.Write( dest );
        }

        public override string ToString()
        {
            StringBuilder result = new StringBuilder();
            var mapping = CreateMapping();
            result.Append( "DestinationStatistics: " );
            result.Append( mapping.ToString() );
            return result.ToString();
        }
    }
}

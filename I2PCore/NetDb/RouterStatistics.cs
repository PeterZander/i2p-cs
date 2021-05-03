using System;
using System.Text;
using I2PCore.Data;
using I2PCore.Utils;
using System.Globalization;
using I2PCore.TunnelLayer;

namespace I2PCore
{
    public class RouterStatistics: I2PType
    {
        static long DefaultTunnelBuildTimeMsPerHop
        {
            get
            {
                return Tunnel.ExpectedTunnelBuildTimePerHop.ToMilliseconds;
            }
        }

        public readonly I2PIdentHash Id;
        public I2PDate Created = I2PDate.Now;
        public I2PDate LastSeen = I2PDate.Now;

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
        public long TunnelBuildTimeMsPerHop;

        public float MaxBandwidthSeen;

        public bool IsFirewalled;

        public int StoreIx;
        public bool Deleted = false;
        public bool Updated = false;

        public RouterStatistics( I2PIdentHash id )
        {
            Id = id;
            UpdateScore();
        }

        const float MaxScore = 30f;
        const float MedTargetPeriods = 30f;

        float DiminishingReturns( float val )
        {
            return (float)( MaxScore * Math.Tanh( val / MedTargetPeriods ) );
        }

        float CachedScore;
        internal void UpdateScore()
        {
            var score = DiminishingReturns( SuccessfulConnects * 1.0f - FailedConnects * 5.00f 
                - SlowHandshakeConnect * 0.5f );
            score += DiminishingReturns( SuccessfulTunnelMember * 3.0f - DeclinedTunnelMember * 0.5f 
                - TunnelBuildTimeout * 1.0f )
                + DiminishingReturns( FloodfillUpdateSuccess * 1.0f - FloodfillUpdateTimeout * 3.0f );
            score += DiminishingReturns( SuccessfulTunnelTest * 0.3f - FailedTunnelTest * 0.1f )
                - ( IsFirewalled ? MaxScore / 4f : 0f );

            CachedScore = score + MaxScore * ( MaxBandwidthSeen / RoutersStatistics.BandwidthMax )
                    - ( TunnelBuildTimeMsPerHop == 0 ? 5000f / 100f : TunnelBuildTimeMsPerHop / 100f )
                    - 3f * DiminishingReturns( InformationFaulty * 10f );
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

        public RouterStatistics( BufRef buf )
        {
            Id = new I2PIdentHash( buf );
            LastSeen = new I2PDate( buf );
            Created = new I2PDate( buf );
            buf.Seek( 52 ); // Reserved space

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
            IsFirewalled = TryGet( mapping, "IsFirewalled" ) != 0;
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
            mapping["IsFirewalled"] = IsFirewalled ? "1" : "0";

            return mapping;
        }

        public void Write( BufRefStream dest )
        {
            Id.Write( dest );
            LastSeen.Write( dest );
            Created.Write( dest );
            dest.Write( BufUtils.RandomBytes( 52 ) ); // Reserved space

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

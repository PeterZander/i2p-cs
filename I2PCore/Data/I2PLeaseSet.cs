using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using I2PCore.Utils;

namespace I2PCore.Data
{
    public class I2PLeaseSet: I2PType
    {
        public I2PDestination Destination;
        public I2PPublicKey PublicKey;
        public I2PSigningPublicKey PublicSigningKey;

        public IEnumerable<I2PLease> Leases { get => LeasesField; }

        private readonly List<I2PLease> LeasesField = 
                new List<I2PLease>();

        public I2PSignature Signature;

        I2PLeaseInfo Info;

        public I2PLeaseSet( I2PDestination dest, IEnumerable<I2PLease> leases, I2PLeaseInfo info )
        {
            Destination = dest;

            Info = info;
            PublicKey = info.PublicKey;
            PublicSigningKey = info.PublicSigningKey;

            if ( leases != null && leases.Any() )
            {
                foreach ( var lease in leases )
                {
                    LeasesField.Add( lease );
                }
            }
        }

        public I2PLeaseSet( BufRef reader )
        {
            Destination = new I2PDestination( reader );
            PublicKey = new I2PPublicKey( reader, I2PKeyType.DefaultAsymetricKeyCert );
            PublicSigningKey = new I2PSigningPublicKey( reader, Destination.Certificate );

            int leasecount = reader.Read8();
            for ( int i = 0; i < leasecount; ++i )
            {
                LeasesField.Add( new I2PLease( reader ) );
            }

            Signature = new I2PSignature( reader, Destination.Certificate );
        }

        public void AddLease( I2PLease lease )
        {
            RemoveExpired();

            var expsort = LeasesField
                    .OrderBy( l => (ulong)l.EndDate )
                    .ToArray();

            foreach ( var ls in expsort )
            {
                ls.EndDate.Nudge();

                if ( LeasesField.Count >= 16 )
                {
                    LeasesField.Remove( ls );
                }
                else
                {
                    break;
                }
            }

            LeasesField.Add( lease );
        }

        public void RemoveExpired()
        {
            var now = DateTime.UtcNow;

            foreach ( var ls in LeasesField.ToArray() )
            {
                if ( (DateTime)ls.EndDate < now )
                {
                    LeasesField.Remove( ls );
                }
            }
        }

        public void RemoveLease( I2PIdentHash tunnelgw, uint tunnelid )
        {
            var remove = LeasesField
                    .Where( l => 
                        l.TunnelId == tunnelid 
                        && l.TunnelGw == tunnelgw )
                    .Select( l => l )
                    .ToArray();

            if ( remove.Length != 0 )
            {
                foreach ( var one in remove ) LeasesField.Remove( one );
            }
#if LOG_ALL_TUNNEL_TRANSFER
            else
            {
                Logging.LogDebug( "I2PLeaseSet RemoveLease: No lease found to remove" );
            }
#endif
        }

        public bool VerifySignature()
        {
            var versig = I2PSignature.SupportedSignatureType( PublicSigningKey.Certificate.SignatureType );

            if ( !versig )
            {
                Logging.LogDebug( "I2PLeaseSet: VerifySignature false. Not supported: " +
                    PublicSigningKey.Certificate.SignatureType.ToString() );
                return false;
            }

            var signfields = new List<BufLen>
            {
                new BufLen( Destination.ToByteArray() ),
                PublicKey.Key,
                PublicSigningKey.Key,
                (BufLen)(byte)LeasesField.Count
            };

            foreach ( var lease in LeasesField )
            {
                signfields.Add( new BufLen( lease.ToByteArray() ) );
            }

            versig = I2PSignature.DoVerify( PublicSigningKey, Signature, signfields.ToArray() );
            if ( !versig )
            {
                Logging.LogDebug( $"I2PLeaseSet: I2PSignature.DoVerify failed: {PublicSigningKey.Certificate.SignatureType}" );
                return false;
            }

            return true;
        }

        public void Write( BufRefStream dest )
        {
            Destination.Write( dest );
            Info.PublicKey.Write( dest );
            Info.PublicSigningKey.Write( dest );

            var cnt = (byte)LeasesField.Count;
            if ( cnt > 16 ) throw new OverflowException( "Max 16 leases per I2PLeaseSet" );

            var signfields = new List<BufLen>
            {
                new BufLen( Destination.ToByteArray() ),
                Info.PublicKey.Key,
                Info.PublicSigningKey.Key,
                (BufLen)cnt
            };

            dest.Write( (byte)LeasesField.Count );

            foreach ( var lease in LeasesField )
            {
                var buf = lease.ToByteArray();
                dest.Write( buf );
                signfields.Add( new BufLen( buf ) );
            }

            dest.Write( I2PSignature.DoSign( Info.PrivateSigningKey, signfields.ToArray() ) );
        }

        public override string ToString()
        {
            var result = new StringBuilder();

            result.AppendLine( "I2PLeaseSet" );

            result.AppendLine( $"Destination      : {Destination}" );
            result.AppendLine( $"PublicKey        : {PublicKey}" );
            result.AppendLine( $"PublicSigningKey : {PublicSigningKey}" );
            result.AppendLine( $"Lease count      : {LeasesField.Count}" );

            foreach ( var one in LeasesField )
            {
                result.AppendLine( $"Lease            : {one}" );
            }

            return result.ToString();
        }
    }
}

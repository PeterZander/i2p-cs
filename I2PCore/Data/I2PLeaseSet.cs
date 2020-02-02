using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;

namespace I2PCore.Data
{
    public class I2PLeaseSet: I2PType
    {
        public I2PKeysAndCert Destination;
        public I2PPublicKey PublicKey;
        public I2PSigningPublicKey PublicSigningKey;

        public List<I2PLease> Leases;

        public I2PSignature Signature;

        I2PLeaseInfo Info;

        public I2PLeaseSet( I2PKeysAndCert dest, IEnumerable<I2PLease> leases, I2PLeaseInfo info )
        {
            Destination = dest;
            Leases = new List<I2PLease>();
            if ( leases != null ) Leases.AddRange( leases );
            Info = info;
            PublicKey = info.PublicKey;
            PublicSigningKey = info.PublicSigningKey;
        }

        public I2PLeaseSet( BufRef reader )
        {
            Destination = new I2PDestination( reader );
            PublicKey = new I2PPublicKey( reader, I2PKeyType.DefaultAsymetricKeyCert );
            PublicSigningKey = new I2PSigningPublicKey( reader, Destination.Certificate );

            var leases = new List<I2PLease>();
            int leasecount = reader.Read8();
            for ( int i = 0; i < leasecount; ++i )
            {
                leases.Add( new I2PLease( reader ) );
            }
            Leases = leases;
            Signature = new I2PSignature( reader, Destination.Certificate );
        }

        public void AddLease( I2PLease lease )
        {
            lock ( Leases )
            {
                if ( Leases.Count >= 16 )
                {
                    var expsort = Leases.OrderBy( l => (ulong)l.EndDate ).GetEnumerator();
                    while ( Leases.Count >= 16 && expsort.MoveNext() )
                    {
                        Leases.Remove( expsort.Current );
                    }
                }

                Leases.Add( lease );
            }
        }

        public void RemoveLease( I2PIdentHash tunnelgw, uint tunnelid )
        {
            I2PLease[] remove;

            lock ( Leases )
            {
                remove = Leases.Where( l => l.TunnelId == tunnelid && l.TunnelGw == tunnelgw ).ToArray();
            }

            if ( remove.Length != 0 )
            {
                lock ( Leases )
                {
                    foreach ( var one in remove ) Leases.Remove( one );
                }
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

            var signfields = new List<BufLen>();

            signfields.Add( new BufLen( Destination.ToByteArray() ) );
            signfields.Add( PublicKey.Key );
            signfields.Add( PublicSigningKey.Key );
            signfields.Add( (BufLen)(byte)Leases.Count );

            lock ( Leases )
            {
                foreach ( var lease in Leases )
                {
                    signfields.Add( new BufLen( lease.ToByteArray() ) );
                }
            }

            versig = I2PSignature.DoVerify( PublicSigningKey, Signature, signfields.ToArray() );
            if ( !versig )
            {
                Logging.LogDebug( "I2PLeaseSet: I2PSignature.DoVerify failed: " + PublicSigningKey.Certificate.SignatureType.ToString() );
                return false;
            }

            return true;
        }

        public void Write( BufRefStream dest )
        {
            Destination.Write( dest );
            Info.PublicKey.Write( dest );
            Info.PublicSigningKey.Write( dest );

            var cnt = (byte)Leases.Count;
            if ( cnt > 16 ) throw new OverflowException( "Max 16 leases per I2PLeaseSet" );

            var signfields = new List<BufLen>();

            signfields.Add( new BufLen( Destination.ToByteArray() ) );
            signfields.Add( Info.PublicKey.Key );
            signfields.Add( Info.PublicSigningKey.Key );
            signfields.Add( (BufLen)cnt );

            lock ( Leases )
            {
                dest.Write( (byte)Leases.Count );

                foreach ( var lease in Leases )
                {
                    var buf = lease.ToByteArray();
                    dest.Write( buf );
                    signfields.Add( new BufLen( buf ) );
                }
            }
            
            dest.Write( I2PSignature.DoSign( Info.PrivateSigningKey, signfields.ToArray() ) );
        }

        public override string ToString()
        {
            var result = new StringBuilder();

            result.AppendLine( "I2PLeaseSet" );

            result.AppendLine( "Destination      : " + Destination.ToString() );
            result.AppendLine( "PublicKey        : " + ( PublicKey == null ? "(null)": PublicKey.ToString() ) );
            result.AppendLine( "PublicSigningKey : " + ( PublicSigningKey == null ? "(null)": PublicSigningKey.ToString() ) );

            foreach( var one in Leases )
            {
                result.AppendLine( "Lease            : " + one.ToString() );
            }

            return result.ToString();
        }
    }
}

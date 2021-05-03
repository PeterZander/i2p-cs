using System;
using System.Collections.Generic;
using System.Linq;
using I2PCore.TunnelLayer.I2NP.Messages;
using I2PCore.Utils;

namespace I2PCore.Data
{
    public class I2PLeaseSet: I2PType, ILeaseSet
    {
        public DatabaseStoreMessage.MessageContent MessageType { get => DatabaseStoreMessage.MessageContent.LeaseSet; }

        public I2PDestination Destination { get; private set; }
        public I2PPublicKey PublicKey { get; private set; }
        public I2PSigningPublicKey PublicSigningKey;

        public IEnumerable<ILease> Leases { get => LeasesField; }

        private readonly List<I2PLease> LeasesField = 
                new List<I2PLease>();

        public I2PSignature Signature;

        public I2PLeaseSet(
                I2PDestination dest,
                IEnumerable<I2PLease> leases,
                I2PPublicKey pubkey,
                I2PSigningPublicKey spubkey,
                I2PSigningPrivateKey sprivkey )
        {
            Destination = dest;

            PublicKey = pubkey;
            PublicSigningKey = spubkey;

            if ( leases != null && leases.Any() )
            {
                LeasesField.AddRange( leases );
            }

            if ( sprivkey != null && ( Leases?.Any() ?? false ) )
            {
                Signature = new I2PSignature(
                    new BufRefLen(
                        CreateSignature( sprivkey ) ),
                    sprivkey.Certificate );
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

            if ( !VerifySignature( Destination.SigningPublicKey ) )
            {
                throw new SignatureCheckFailureException();
            }
        }

        public void AddLease( I2PIdentHash tunnelgw, I2PTunnelId tunnelid, I2PDate enddate )
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

            LeasesField.Add( new I2PLease( tunnelgw, tunnelid, enddate ) );
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

        public void RemoveLease( I2PIdentHash tunnelgw, I2PTunnelId tunnelid )
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

        public bool VerifySignature( I2PSigningPublicKey spkey )
        {
            try
            {
                var signfields = new List<BufLen>
                {
                    new BufLen( Destination.ToByteArray() ),
                    PublicKey.Key,
                    PublicSigningKey.Key,
                    BufUtils.To8BL( (byte)LeasesField.Count )
                };

                foreach ( var lease in LeasesField )
                {
                    signfields.Add( new BufLen( lease.ToByteArray() ) );
                }

                var versig = I2PSignature.DoVerify( spkey, Signature, signfields.ToArray() );
                if ( !versig )
                {
                    Logging.LogDebug( $"I2PLeaseSet: I2PSignature.DoVerify failed: {spkey.Certificate.SignatureType}" );
                    return false;
                }

                return true;
            }
            catch ( Exception ex )
            {
                Logging.LogDebug( ex );
                return false;
            }
        }

        public void Write( BufRefStream dest )
        {
            Destination.Write( dest );
            PublicKey.Write( dest );
            PublicSigningKey.Write( dest );

            dest.Write( (byte)LeasesField.Count );

            foreach ( var lease in LeasesField )
            {
                var buf = lease.ToByteArray();
                dest.Write( buf );
            }

            Signature.Write( dest );
        }

        private byte[] CreateSignature( I2PSigningPrivateKey privsignkey )
        {
            var cnt = (byte)LeasesField.Count;
            if ( cnt > 16 ) throw new OverflowException( "Max 16 leases per I2PLeaseSet" );

            var signfields = new List<BufLen>
            {
                new BufLen( Destination.ToByteArray() ),
                PublicKey.Key,
                PublicSigningKey.Key,
                BufUtils.To8BL( cnt )
            };

            foreach ( var lease in LeasesField )
            {
                var buf = lease.ToByteArray();
                signfields.Add( new BufLen( buf ) );
            }

            return I2PSignature.DoSign( privsignkey, signfields.ToArray() );
        }

        /// <summary>
        /// UTC of the largest EndDate for a lease
        /// </summary>
        /// <value>The end of life.</value>
        public DateTime Expire
        {
            get
            {
                if ( !Leases?.Any() ?? true ) return DateTime.UtcNow;
                return (DateTime)LeasesField.Max( l => l.EndDate );
            }
        }

        public IEnumerable<I2PPublicKey> PublicKeys
        {
            get
            {
                return new I2PPublicKey[] { PublicKey };
            }
        }

        public override string ToString()
        {
            return $"I2PLeaseSet [{Leases?.Count()}]: {Destination?.IdentHash.Id32Short} " +
                $"{string.Join( ",", LeasesField )}";
        }
        byte[] ILeaseSet.ToByteArray()
        {
            return this.ToByteArray();
        }
    }
}

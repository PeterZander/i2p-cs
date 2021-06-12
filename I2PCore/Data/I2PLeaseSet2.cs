using System;
using System.Collections.Generic;
using System.Linq;
using I2PCore.TunnelLayer.I2NP.Messages;
using I2PCore.Utils;

namespace I2PCore.Data
{
    public class I2PLeaseSet2: I2PType, ILeaseSet
    {
        public DatabaseStoreMessage.MessageContent MessageType
        { 
            get => DatabaseStoreMessage.MessageContent.LeaseSet2;
        }

        public I2PLeaseSet2Header Header { get; set; }
        public I2PMapping Options { get; set; }
        IList<I2PPublicKey> PublicKeysField;
        public List<I2PLease2> LeasesField;
        public IEnumerable<ILease> Leases { get => LeasesField; }
        public I2PSignature Signature { get; set; }

        I2PSigningPublicKey PublicSigningKey;
        I2PSigningPrivateKey PrivateSigningKey;

        public I2PLeaseSet2(
                I2PDestination dest,
                IEnumerable<I2PLease2> leases,
                IList<I2PPublicKey> pubkeys,
                I2PSigningPublicKey spubkey,
                I2PSigningPrivateKey sprivkey )
        {
            Header = new I2PLeaseSet2Header(
                dest,
                new I2PDateShort( DateTime.UtcNow ),
                I2PLeaseSet2Header.HeaderFlagTypes.None );
            Options = new I2PMapping();

            PublicKeysField = pubkeys;
            LeasesField = new List<I2PLease2>( leases?.Where( l => l.Expire > DateTime.UtcNow ) );
            PublicSigningKey = spubkey;
            PrivateSigningKey = sprivkey;
        }

        static readonly byte[] ThreeArray = { 3 };
        
        public I2PLeaseSet2( BufRef reader )
        {
            var start = new BufRef( reader );

            Header = new I2PLeaseSet2Header( reader );
            Options = new I2PMapping( reader );
            
            var keycount = reader.Read8();
            PublicKeysField = new List<I2PPublicKey>();
            for ( int i = 0; i < keycount; ++i )
            {
                var keytype = reader.ReadFlip16();
                var keylen = reader.ReadFlip16();
                var cert = new I2PCertificate( (I2PKeyType.KeyTypes)keytype, keylen );
                PublicKeysField.Add( new I2PPublicKey( reader, cert ) );
            }

            var leasecount = reader.Read8();
            LeasesField = new List<I2PLease2>();
            for ( int i = 0; i < leasecount; ++i )
            {
                LeasesField.Add( new I2PLease2( reader ) );
            }

            var body = new BufLen( start, 0, reader - start );
            Signature = new I2PSignature( reader, Header.Destination.Certificate );

            var spkey = Header.Destination.SigningPublicKey;
            var versig = I2PSignature.DoVerify( spkey, Signature, new BufLen( ThreeArray ), body );
            if ( !versig )
            {
                var msg = $"I2PLeaseSet: I2PSignature.DoVerify failed: {spkey.Certificate.SignatureType}";
                Logging.LogDebug( msg );
                throw new SignatureCheckFailureException( msg );
            }
        }

        public void Write( BufRefStream dest )
        {
            var ar = WriteBody().ToByteArray();

            if ( Signature is null )
            {
                Signature = new I2PSignature(
                    new BufRefLen(
                        I2PSignature.DoSign( PrivateSigningKey, new BufLen( ThreeArray ), new BufLen( ar ) ) ),
                    PrivateSigningKey.Certificate );
            }

            dest.Write( ar );
            Signature.Write( dest );
        }

        BufRefStream WriteBody()
        {
            var lbuf = new BufRefStream();
            
            Header.Write( lbuf );
            Options.Write( lbuf );

            var keycount = (byte)PublicKeysField.Count;
            lbuf.Write( keycount );
            foreach ( var key in PublicKeysField )
            {
                lbuf.Write( BufUtils.Flip16BL( (ushort)key.Certificate.PublicKeyType ) );
                lbuf.Write( BufUtils.Flip16BL( (ushort)key.Certificate.PublicKeyLength ) );
                key.Write( lbuf );
            }

            lbuf.Write( (byte)LeasesField.Count );
            foreach ( var lease in LeasesField )
            {
                lease.Write( lbuf );
            }

            return lbuf;
        }

        // ILeaseSet
        public I2PDestination Destination { get => Header?.Destination; }
        public DateTime Expire { get => (DateTime)Header?.Published + TimeSpan.FromSeconds( (double)Header?.ExpiresSeconds ); }
        public IEnumerable<I2PPublicKey> PublicKeys
        {
            get
            {
                return PublicKeysField;
            }
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
        public void AddLease( I2PIdentHash tunnelgw, I2PTunnelId tunnelid, I2PDate enddate )
        {
            if ( (DateTime)enddate > DateTime.UtcNow ) return;

            RemoveExpired();

            var endshort = new I2PDateShort( enddate );

            var expsort = LeasesField
                    .OrderBy( l => l.Expire )
                    .ToArray();

            foreach ( var ls in expsort )
            {
                if ( LeasesField.Count >= 16 )
                {
                    LeasesField.Remove( ls );
                }
                else
                {
                    break;
                }
            }

            LeasesField.Add( new I2PLease2( tunnelgw, tunnelid, endshort ) );
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

        public override string ToString()
        {
            return $"I2PLeaseSet2 [{Leases?.Count()}]: {Destination?.IdentHash.Id32Short} " +
                $"TTL: {Expire - DateTime.UtcNow} {string.Join( ",", LeasesField )}";
        }

        byte[] ILeaseSet.ToByteArray()
        {
            return this.ToByteArray();
        }
    }
}

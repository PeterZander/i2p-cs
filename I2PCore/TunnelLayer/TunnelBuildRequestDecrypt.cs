using System;
using System.Collections.Generic;
using I2PCore.Data;
using I2PCore.TunnelLayer.I2NP.Data;
using System.Linq;
using I2PCore.Utils;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Engines;

namespace I2PCore.TunnelLayer
{
    public class TunnelBuildRequestDecrypt
    {
        readonly IEnumerable<AesEGBuildRequestRecord> RecordsField;
        readonly I2PIdentHash Me;
        private readonly I2PPrivateKey Key;
        private readonly AesEGBuildRequestRecord ToMeField;
        private readonly EGBuildRequestRecord MyRecord;
        private readonly BuildRequestRecord DecryptedRecord;

        public TunnelBuildRequestDecrypt(
            IEnumerable<AesEGBuildRequestRecord> records,
            I2PIdentHash me,
            I2PPrivateKey key )
        {
            RecordsField = records;
            Me = me;
            Key = key;

            ToMeField = RecordsField.FirstOrDefault( rec => Me.Hash16 == rec.ToPeer16 );

            if ( ToMeField != null )
            {
                MyRecord = new EGBuildRequestRecord( ToMeField );
                DecryptedRecord = MyRecord.Decrypt( key );
            }
        }

        public TunnelBuildRequestDecrypt Clone()
        {
            return new TunnelBuildRequestDecrypt(
                Records.Select( r => r.Clone() ),
                Me,
                Key );
        }

        public AesEGBuildRequestRecord ToMe()
        {
            return ToMeField;
        }

        public BuildRequestRecord Decrypted => DecryptedRecord;
        public IEnumerable<AesEGBuildRequestRecord> Records => RecordsField;

        public IEnumerable<AesEGBuildRequestRecord> CreateTunnelBuildReplyRecords( 
            BuildResponseRecord.RequestResponse response )
        {
            var newrecords = new List<AesEGBuildRequestRecord>(
                Records.Select( r => r.Clone() )
            );

            var tmp = new TunnelBuildRequestDecrypt( newrecords, Me, Key );

            tmp.ToMeField.Data.Randomize();
            var responserec = new BuildResponseRecord( tmp.ToMeField.Data )
            {
                Reply = response
            };
            responserec.UpdateHash();

            var cipher = new CbcBlockCipher( new AesEngine() );
            cipher.Init( true, Decrypted.ReplyKeyBuf.ToParametersWithIV( Decrypted.ReplyIV ) );

            foreach ( var one in newrecords )
            {
                cipher.Reset();
                one.Process( cipher );
            }

            return newrecords;
        }


        public override string ToString()
        {
            return $"{GetType().Name} {Decrypted}";
        }
    }
}

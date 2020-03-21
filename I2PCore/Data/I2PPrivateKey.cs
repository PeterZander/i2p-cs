using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using I2PCore.Utils;
using System.Threading;
using I2PCore.SessionLayer;

namespace I2PCore.Data
{
    public class I2PPrivateKey : I2PKeyType
    {
        public I2PPrivateKey( I2PCertificate cert ): base( cert )
        {
            Key = new BufLen( BufUtils.RandomBytes( KeySizeBytes ) );
        }

        public I2PPrivateKey( BufRef reader, I2PCertificate cert ) : base( reader, cert ) { }

        public override int KeySizeBytes { get { return Certificate.PrivateKeyLength; } }

        #region Precalculated keys

        protected static Thread Worker;
        private static LinkedList<I2PDHKeyPair> PrecalculatedKeys;
        private static AutoResetEvent KeyConsumed = new AutoResetEvent( true );

        public static I2PDHKeyPair GetNewKeyPair()
        {
            if ( Worker == null )
            {
                PrecalculatedKeys = new LinkedList<I2PDHKeyPair>();

                Worker = new Thread( () => Run() );
                Worker.Name = "DH Key pair generator";
                Worker.Priority = ThreadPriority.Lowest;
                Worker.IsBackground = true;
                Worker.Start();
            }

            I2PDHKeyPair result;

            lock ( PrecalculatedKeys )
            {
                if ( PrecalculatedKeys.Count > 0 )
                {
                    result = PrecalculatedKeys.Last.Value;
                    PrecalculatedKeys.RemoveLast();
                    KeyConsumed.Set();
                    return result;
                }
            }

            result.PrivateKey = new I2PPrivateKey( DefaultAsymetricKeyCert );
            result.PublicKey = new I2PPublicKey( result.PrivateKey );
            return result;
        }

        private static void Run()
        {
            try
            {
                try
                {
                    while ( true )
                    {
                        KeyConsumed.WaitOne( 5000 );

                        while ( PrecalculatedKeys.Count < 10 )
                        {
                            I2PDHKeyPair keys;
                            keys.PrivateKey = new I2PPrivateKey( DefaultAsymetricKeyCert );
                            keys.PublicKey = new I2PPublicKey( keys.PrivateKey );
                            lock ( PrecalculatedKeys )
                            {
                                PrecalculatedKeys.AddFirst( keys );
                            }
                        }
                    }
                }
                catch ( ThreadAbortException )
                {
                }
                catch ( Exception ex )
                {
                    Logging.Log( ex );
                }
            }
            finally
            {
                Worker = null;
            }
        }

        #endregion
    }

    public struct I2PDHKeyPair
    {
        public I2PPrivateKey PrivateKey;
        public I2PPublicKey PublicKey;
    }
}

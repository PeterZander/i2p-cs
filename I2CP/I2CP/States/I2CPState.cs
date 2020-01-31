using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;
using System.Net;
using I2PCore.Data;
using I2PCore.Router;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;
using I2P.I2CP.Messages;

namespace I2P.I2CP.States
{
    public abstract class I2CPState
    {
        public static readonly TickSpan InactivityTimeout = TickSpan.Minutes( 20 );
        public static readonly TickSpan HandshakeTimeout = TickSpan.Seconds( 30 );
        public static readonly TickSpan EstablishedDestinationTimeout = TickSpan.Seconds( 150 );

        public TickCounter Created = TickCounter.Now;
        public TickCounter LastAction = TickCounter.Now;
        public int Retries = 0;

        protected I2CPSession Session;

        protected I2CPState( I2CPSession sess ) 
        { 
            Session = sess; 
            ReceiveWriter = new BufRefLen( ReceiveMessageBuffer );
        }

        protected bool Timeout( TickSpan timeout ) { return LastAction.DeltaToNow > timeout; }
        protected void DataSent() { LastAction.SetNow(); }

        internal abstract I2CPState Run();

        BufLen ReceiveMessageBuffer = new BufLen( new byte[65536] );
        BufRefLen ReceiveWriter;

        internal abstract I2CPState MessageReceived( I2CPMessage msg );

        internal virtual I2CPState DataReceived( BufLen recv )
        {
            I2CPState ns = this;
            BufRefLen recvreader = new BufRefLen( recv );

            while ( recvreader.Length > 0 )
            {
                var recvlen = ReceiveWriter - ReceiveMessageBuffer;
                if ( recvlen > 4 )
                {
                    var msglen = (int)ReceiveMessageBuffer.PeekFlip32( 0 );
                check_msg_ok:
                    if ( msglen <= recvlen )
                    {
                        var data = new BufRefLen( ReceiveMessageBuffer, 0, msglen );
                        Logging.LogDebug( () => string.Format( "I2CPState: Message received. {0} bytes, message {1} {2}.",
                            msglen, data[4], (I2CPMessage.ProtocolMessageType)data[4] ) );
                        ns = MessageReceived( I2CPMessage.GetMessage( data ) );
                        ReceiveWriter = new BufRefLen( ReceiveMessageBuffer );
                    }
                    else
                    {
                        var readlen = Math.Min( msglen - recvlen, recvreader.Length );
                        ReceiveWriter.Write( new BufLen( recvreader, 0, readlen ) );
                        recvreader.Seek( readlen );
                        recvlen = ReceiveWriter - ReceiveMessageBuffer;
                        goto check_msg_ok;
                    }
                }
                else
                {
                    var readlen = Math.Min( 5 - recvlen, recvreader.Length );
                    ReceiveWriter.Write( new BufLen( recvreader, 0, readlen ) );
                    recvreader.Seek( readlen );
                }
            }

            return ns;
        }
    }
}

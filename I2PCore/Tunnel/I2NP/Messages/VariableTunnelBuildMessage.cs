using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;
using I2PCore.Data;
using I2PCore.Tunnel.I2NP.Data;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Modes;

namespace I2PCore.Tunnel.I2NP.Messages
{
    public class VariableTunnelBuildMessage : I2NPMessage
    {
        public override MessageTypes MessageType { get { return MessageTypes.VariableTunnelBuild; } }

        public List<AesEGBuildRequestRecord> Records = new List<AesEGBuildRequestRecord>();

        public VariableTunnelBuildMessage( BufRef reader )
        {
            var start = new BufRef( reader );
            var records = reader.Read8();

            for ( int i = 0; i < records; ++i )
            {
                var r = new AesEGBuildRequestRecord( reader );
                Records.Add( r );
            }
            SetBuffer( start, reader );
        }

        private VariableTunnelBuildMessage( byte hops )
        {
            AllocateBuffer( 1 + hops * AesEGBuildRequestRecord.Length );
            var writer = new BufRefLen( Payload );
            writer.Write8( hops );
            for ( int i = 0; i < hops; ++i ) Records.Add( new AesEGBuildRequestRecord( writer ) );
        }

        public override string ToString()
        {
            var result = new StringBuilder();

            result.AppendLine( "VariableTunnelBuild" );
            for ( int i = 0; i < Records.Count; ++i )
            {
                result.Append( Records[i].ToString() );
            }

            return result.ToString();
        }

        public static VariableTunnelBuildMessage BuildOutboundTunnel(
            TunnelInfo setup,
            I2PIdentHash replyaddr, I2PTunnelId replytunnel,
            uint replymessageid )
        {
            byte usehops = (byte)( setup.Hops.Count > 5 ? 8 : 5 );
            //byte usehops = 7; // 8 makes the response "TunnelBuildReply"
            var result = new VariableTunnelBuildMessage( usehops );

            // Hop sort order
            var requests = new List<BuildRequestRecord>();

            for ( int i = 0; i < setup.Hops.Count; ++i )
            {
                // Hop order: Our out dest -> Endpoint
                var endpoint = i == setup.Hops.Count - 1;
                var gateway = i == 0;

                var rec = new BuildRequestRecord();

                rec.Data.Randomize();
                rec.Flag = 0;

                var hop = setup.Hops[i];

                rec.OurIdent = hop.Peer.IdentHash;
                rec.ReceiveTunnel = hop.TunnelId;

                if ( !endpoint )
                {
                    var nexthop = setup.Hops[i + 1];

                    rec.NextIdent = nexthop.Peer.IdentHash;
                    rec.NextTunnel = nexthop.TunnelId;
                }
                else
                {
                    rec.SendMessageId = replymessageid;

                    rec.NextIdent = replyaddr;
                    rec.NextTunnel = replytunnel;
                }

                rec.RequestTime = DateTime.UtcNow;
                rec.ToAnyone = endpoint;

                hop.LayerKey = new I2PSessionKey( rec.LayerKey.Clone() );
                hop.IVKey = new I2PSessionKey( rec.IVKey.Clone() );

                requests.Add( rec );

                DebugUtils.Log( rec.ToString() );
            }

            // Physical record sort order
            var order = new List<AesEGBuildRequestRecord>( result.Records );
            order.Shuffle();

            // Scramble the rest
            for ( int i = setup.Hops.Count; i < usehops; ++i )
            {
                order[i].Data.Randomize();
            }

            // ElGamal encrypt all of the non random records
            // and place them in shuffled order.
            for ( int i = 0; i < setup.Hops.Count; ++i )
            {
                var hop = setup.Hops[i];
                var egrec = new EGBuildRequestRecord( order[i].Data, requests[i], hop.Peer.IdentHash, hop.Peer.PublicKey );
            }

            var cipher = new CbcBlockCipher( new AesEngine() );

            // Dont Aes the first destination
            for ( int i = setup.Hops.Count - 2; i >= 0 ; --i )
            {
                var prevhop = requests[i];

                cipher.Init( false, prevhop.ReplyKeyBuf.ToParametersWithIV( prevhop.ReplyIV ) );

                for ( int j = i + 1; j < setup.Hops.Count; ++j )
                {
                    cipher.Reset();
                    cipher.ProcessBytes( order[j].Data );
                }
            }

            for ( int i = 0; i < setup.Hops.Count; ++i )
            {
                setup.Hops[i].ReplyProcessing = new ReplyProcessingInfo()
                {
                    BuildRequestIndex = result.Records.IndexOf( order[i] ),
                    ReplyIV = requests[i].ReplyIV.Clone(),
                    ReplyKey = new I2PSessionKey( requests[i].ReplyKeyBuf.Clone() )
                };
            }

            return result;
        }

        public static VariableTunnelBuildMessage BuildInboundTunnel(
            TunnelInfo setup )
        {
            byte usehops = (byte)( setup.Hops.Count > 5 ? 8 : 5 );
            var result = new VariableTunnelBuildMessage( usehops );

            // Hop sort order
            var requests = new List<BuildRequestRecord>();

            for ( int i = 0; i < setup.Hops.Count; ++i )
            {
                // Hop order: GW -> us
                var endpoint = i == setup.Hops.Count - 1;
                var gateway = i == 0;

                var rec = new BuildRequestRecord();

                rec.Data.Randomize();
                rec.Flag = 0;

                var hop = setup.Hops[i];

                rec.OurIdent = hop.Peer.IdentHash;
                rec.ReceiveTunnel = hop.TunnelId;

                if ( !endpoint )
                {
                    var nexthop = setup.Hops[i + 1];

                    rec.NextIdent = nexthop.Peer.IdentHash;
                    rec.NextTunnel = nexthop.TunnelId;
                }
                else
                {
                    // Used to identify the record as the last in an inbound tunnel to us
                    rec.NextIdent = hop.Peer.IdentHash;
                    rec.NextTunnel = hop.TunnelId;
                }

                rec.RequestTime = DateTime.UtcNow;
                rec.FromAnyone = gateway;

                hop.LayerKey = new I2PSessionKey( rec.LayerKey.Clone() );
                hop.IVKey = new I2PSessionKey( rec.IVKey.Clone() );

                requests.Add( rec );

                DebugUtils.Log( rec.ToString() );
            }

            // Physical record sort order
            var order = new List<AesEGBuildRequestRecord>( result.Records );
            order.Shuffle();

            // Scramble the rest
            for ( int i = setup.Hops.Count; i < usehops; ++i )
            {
                order[i].Data.Randomize();
            }

            // ElGamal encrypt all of the non random records
            // and place them in shuffled order.
            for ( int i = 0; i < setup.Hops.Count; ++i )
            {
                var hop = setup.Hops[i];
                var egrec = new EGBuildRequestRecord( order[i].Data, requests[i], hop.Peer.IdentHash, hop.Peer.PublicKey );
            }

            var cipher = new BufferedBlockCipher( new CbcBlockCipher( new AesEngine() ) );

            // Dont Aes the first block
            for ( int i = setup.Hops.Count - 2; i >= 0; --i )
            {
                var prevhop = requests[i];

                cipher.Init( false, new ParametersWithIV( new KeyParameter( prevhop.ReplyKey.ToByteArray() ), prevhop.ReplyIV.ToByteArray() ) );

                for ( int j = i + 1; j < setup.Hops.Count; ++j )
                {
                    cipher.Reset();
                    order[j].Process( cipher );
                }
            }

            for ( int i = 0; i < setup.Hops.Count; ++i )
            {
                setup.Hops[i].ReplyProcessing = new ReplyProcessingInfo()
                {
                    BuildRequestIndex = result.Records.IndexOf( order[i] ),
                    ReplyIV = requests[i].ReplyIV.Clone(),
                    ReplyKey = new I2PSessionKey( requests[i].ReplyKeyBuf.Clone() )
                };
            }

            return result;
        }

        public class ReplyProcessingInfo
        {
            public int BuildRequestIndex;
            public I2PSessionKey ReplyKey;
            public BufLen ReplyIV;
        }
    }
}

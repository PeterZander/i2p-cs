using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using I2PCore.Utils;
using I2PCore.Tunnel.I2NP.Data;

namespace I2PCore.Tunnel.I2NP.Messages
{
    public class TunnelDataMessage: I2NPMessage
    {
        public override MessageTypes MessageType { get { return MessageTypes.TunnelData; } }

        BufLen FirstDeliveryInstruction;

        public TunnelDataMessage( I2PTunnelId desttunnel )
        {
            AllocateBuffer( DataLength + 4 );

            TunnelId = desttunnel;
            IV.Randomize();
        }

        public TunnelDataMessage( byte[] data, I2PTunnelId tunnel )
        {
            var start = new BufRef( data );
            var reader = new BufRefLen( data );
            reader.Seek( 4 + DataLength );
            SetBuffer( start, reader );
            TunnelId = tunnel;

            UpdateFirstDeliveryInstructionPosition();
        }

        public TunnelDataMessage( I2NPHeader header, BufRef reader )
        {
            var start = new BufRef( reader );
            reader.Seek( 4 + DataLength );
            SetBuffer( start, reader );

            // The message is encrypted at this stage
            //UpdateFirstDeliveryInstructionPosition();
        }

        public void UpdateFirstDeliveryInstructionPosition()
        {
            FirstDeliveryInstruction = FindTheZero( PaddingStart );
        }

        internal void SetFirstDeliveryInstructionPoint( BufLen di )
        {
            FirstDeliveryInstruction = di;
        }

        static BufLen FindTheZero( BufLen start )
        {
            var result = new BufRefLen( start );
            while ( result.Read8() != 0 ) ;
            return result.View;
        }

        public I2PTunnelId TunnelId
        {
            get
            {
                return new I2PTunnelId( Payload.Peek32( 0 ) );
            }
            set
            {
                Payload.Poke32( value, 0 );
            }
        }

        public BufLen IV
        {
            get
            {
                return new BufLen( Payload, 4, 16 );
            }
            set
            {
                Payload.Poke( value, 4, 16 );
            }
        }

        public BufLen EncryptedWindow
        {
            get
            {
                return new BufLen( Payload, 20, 1008 );
            }
            set
            {
                Payload.Poke( Payload, 20, 1008 );
            }
        }

        public BufLen Checksum
        {
            get
            {
                return new BufLen( Payload, 20, 4 );
            }
            set
            {
                Payload.Poke( value, 20, 4 );
            }
        }

        public BufLen PaddingStart
        {
            get
            {
                return new BufLen( Payload, 24, 1004 );
            }
            set
            {
                Payload.Poke( value, 24, 1004 );
            }
        }

        public ushort DataLength
        {
            get
            {
                return 1024;
            }
        }

        public BufLen TunnelDataPayload
        {
            get
            {
                if ( FirstDeliveryInstruction == null ) UpdateFirstDeliveryInstructionPosition();
                return new BufLen( FirstDeliveryInstruction );
            }
        }

        public override string ToString()
        {
            var result = new StringBuilder();

            result.AppendLine( "TunnelData " + GetType().ToString() );
            result.AppendLine( "TunnelId         : " + TunnelId.ToString() );
            if ( FirstDeliveryInstruction != null ) result.AppendLine( "TunnelDataPayload: " + TunnelDataPayload.ToString() );

            return result.ToString();
        }

        public static IEnumerable<TunnelDataMessage> MakeFragments( IEnumerable<TunnelMessage> messages, I2PTunnelId desttunnel )
        {
			var padcalc = CalculatePadding( messages, desttunnel );

            foreach ( var one in padcalc.TDMessages )
            {
                //DebugUtils.Log( "New message" );

                var writer = new BufRefLen( one.TunnelDataInstance.Payload );

                writer.Seek( 24 ); // TunnelID, IV, Checksum of "Tunnel Message (Decrypted)"
                if ( one.PaddingNeeded > 0 )
                {
                    //DebugUtils.Log( "Padding " + one.PaddingNeeded.ToString() + " bytes" );
                    writer.Write( new BufRefLen( BufUtils.RandomNZ( one.PaddingNeeded ) ) );
                }

                writer.Write8( 0 ); // The Zero
                one.TunnelDataInstance.SetFirstDeliveryInstructionPoint( writer.View );

                foreach ( var frag in one.Fragments )
                {
                    var fragstart = new BufRefLen( writer );
                    frag.Append( writer );
                    //DebugUtils.Log( "Fragment " + ( one.TunnelDataInstance.Writer - fragstart ).ToString() + " bytes" );
                }

                one.TunnelDataInstance.Checksum.Poke( I2PHashSHA256.GetHash( 
                        one.TunnelDataInstance.FirstDeliveryInstruction,
                        one.TunnelDataInstance.IV ), 0, 4 );

                if ( writer.Length != 0 )
                {
                    Logging.LogCritical( "TunnelData: MakeFragments. Tunnel block is not filled!" );
                    throw new Exception( "TunnelData message not filled. Something is wrong." );
                }
            }

            return padcalc.TDMessages.Select( msg => msg.TunnelDataInstance );
        }

        internal class TunnelDataConstructionInfo
        {
            internal TunnelDataConstructionInfo( I2PTunnelId desttunnel )
            {
                TunnelDataInstance = new TunnelDataMessage( desttunnel );
            }

            internal TunnelDataMessage TunnelDataInstance;
            internal int PaddingNeeded;
            internal List<TunnelDataFragmentCreation> Fragments = new List<TunnelDataFragmentCreation>();
        }

        internal class PaddingInfo
        {
            //internal int MessagesNeeded;
            internal List<TunnelDataConstructionInfo> TDMessages = new List<TunnelDataConstructionInfo>();
		}

		const int FREE_SPACE_IN_DATA_MESSAGE_BODY =  1003; // 1028 - 4 - 16 - 4 - 1 (Zero)
		const int FOLLOW_ON_FRAGMENT_HEADER_SIZE = 7; // 1 + 4 + 2

		// Minimum space left for payload to start a new fragment in a TunnelData block.
		const int TUNNEL_DATA_MESSAGE_FREE_SPACE_LOW_WATERMARK = 10;

        private static PaddingInfo CalculatePadding( IEnumerable<TunnelMessage> messages, I2PTunnelId desttunnel )
        {
            var result = new PaddingInfo();

            int tunneldatabufferavailable = FREE_SPACE_IN_DATA_MESSAGE_BODY;
            var currenttdrecord = new TunnelDataConstructionInfo( desttunnel );
            result.TDMessages.Add( currenttdrecord );

            var lastix = messages.Count() - 1;
            var ix = 0;
			foreach( var one in messages )
			{
				var lastmessage = ix++ == lastix;

                var data = one.Header.HeaderAndPayload;
                var datareader = new BufRefLen( data );

			nexttunneldatablock:

				int firstfragmentheadersize;
                bool fragmented = false;

	            switch ( one.Delivery )
	            {
	                case TunnelMessage.DeliveryTypes.Local:
	                    firstfragmentheadersize = 3;
                        if ( data.Length + firstfragmentheadersize > tunneldatabufferavailable )
                        {
                            firstfragmentheadersize += 4; // Need message id
                            fragmented = true;
                        }
	                    break;

	                case TunnelMessage.DeliveryTypes.Router:
                        firstfragmentheadersize = 35;
                        if ( data.Length + firstfragmentheadersize > tunneldatabufferavailable )
                        {
                            firstfragmentheadersize += 4; // Need message id
                            fragmented = true;
                        }
	                    break;

	                case TunnelMessage.DeliveryTypes.Tunnel:
                        firstfragmentheadersize = 39;
                        if ( data.Length + firstfragmentheadersize > tunneldatabufferavailable )
                        {
                            firstfragmentheadersize += 4; // Need message id
                            fragmented = true;
                        }
	                    break;

	                default:
	                    throw new NotImplementedException();
	            }

				var freespace = tunneldatabufferavailable - firstfragmentheadersize;

                if ( freespace < TUNNEL_DATA_MESSAGE_FREE_SPACE_LOW_WATERMARK )
	            {
                    currenttdrecord.PaddingNeeded = tunneldatabufferavailable;

                    currenttdrecord = new TunnelDataConstructionInfo( desttunnel );
                    result.TDMessages.Add( currenttdrecord );
                    tunneldatabufferavailable = FREE_SPACE_IN_DATA_MESSAGE_BODY;
                    goto nexttunneldatablock;
	            }

                // Might fit, and have at least TUNNEL_DATA_MESSAGE_FREE_SPACE_LOW_WATERMARK bytes for payload.

                var useddata = fragmented ? freespace : datareader.Length;
                var usedspace = firstfragmentheadersize + useddata;

                currenttdrecord.Fragments.Add( 
                    new TunnelDataFragmentCreation( 
                        currenttdrecord.TunnelDataInstance, 
                        one, 
                        new BufLen( datareader, 0, useddata ), 
                        fragmented ) );

                datareader.Seek( useddata );

				tunneldatabufferavailable -= usedspace;

                int fragnr = 1;
                while ( datareader.Length > 0 )
	            {
                    if ( tunneldatabufferavailable < TUNNEL_DATA_MESSAGE_FREE_SPACE_LOW_WATERMARK + FOLLOW_ON_FRAGMENT_HEADER_SIZE )
                    {
                        currenttdrecord.PaddingNeeded = tunneldatabufferavailable;

                        currenttdrecord = new TunnelDataConstructionInfo( desttunnel );
                        result.TDMessages.Add( currenttdrecord );
                        tunneldatabufferavailable = FREE_SPACE_IN_DATA_MESSAGE_BODY;
                    }

					freespace = tunneldatabufferavailable - FOLLOW_ON_FRAGMENT_HEADER_SIZE;

                    if ( datareader.Length <= freespace )
					{
                        currenttdrecord.Fragments.Add( 
                            new TunnelDataFragmentFollowOn( 
                                currenttdrecord.TunnelDataInstance, 
                                one,
                                new BufLen( datareader ),
                                fragnr++, true
                                ) );

                        tunneldatabufferavailable -= FOLLOW_ON_FRAGMENT_HEADER_SIZE + datareader.Length;
                        datareader.Seek( datareader.Length );

                        if ( lastmessage )
                        {
                            currenttdrecord.PaddingNeeded = tunneldatabufferavailable;
                            return result;
                        }
                    }
					else
	                {
                        useddata = freespace;

                        currenttdrecord.Fragments.Add( 
                            new TunnelDataFragmentFollowOn( 
                                currenttdrecord.TunnelDataInstance, 
                                one, 
                                new BufLen( datareader, 0, useddata ), 
                                fragnr++,
                                false
                                ) );
                        datareader.Seek( useddata );

                        tunneldatabufferavailable = 0;
                    }
	            }
			}

            currenttdrecord.PaddingNeeded = tunneldatabufferavailable;
			return result;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Tunnel.I2NP.Messages;
using I2PCore.Tunnel.I2NP.Data;
using I2PCore.Utils;
using I2PCore.Data;

namespace I2PCore.Tunnel
{
    public class GarlicCreationInfo
    {
        public readonly TickCounter Created = new TickCounter();

        public readonly I2PIdentHash Destination;

        public enum KeyUsed { ElGamal, Aes }
        public readonly KeyUsed KeyType;

        // Id of the group of cloves (plus additional ack messages) initially passed to Send
        public readonly uint TrackingId;

        // The cloves initially passed to Send
        public readonly GarlicCloveDelivery[] Cloves;

        // Generated and ecrypted Garlic
        public readonly EGGarlic Garlic;

        // MessageId of the associated Ack message or null if none
        public readonly uint? AckMessageId;

        // ACK Message id for the ElGamal clove with tags for this garlic message
        public readonly uint EGAckMessageId;

        public TickCounter LastSend = null;

        /// <summary>
        /// Number of tags available after this operation.
        /// </summary>
        public readonly int AesTagsAvailable;

        public GarlicCreationInfo( 
            I2PIdentHash dest,
            GarlicCloveDelivery[] cloves, 
            EGGarlic msg, 
            KeyUsed key, 
            int tags, 
            uint trackingid, 
            uint? ackmsgid, 
            uint egackmsgid )
        {
            Destination = dest;
            Cloves = cloves;
            Garlic = msg;
            TrackingId = trackingid;
            KeyType = key;
            AesTagsAvailable = tags;
            EGAckMessageId = egackmsgid;
            AckMessageId = ackmsgid;
        }
    }
}

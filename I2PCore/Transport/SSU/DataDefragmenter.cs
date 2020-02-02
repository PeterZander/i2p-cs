using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;

namespace I2PCore.Transport.SSU
{
    public class DataDefragmenter
    {
        const int RememberCompleteMessageIdsSeconds = 15;
        const int RememberIncompleteMessagesSeconds = 45;
        const int NumberOfExplicitAckResends = 2;
        const int NumberOfBitmapAckResends = 20;
        const int MillisecondsBetweenAcks = 400;
        const int MaxExplicitAcksPerMessage = 5;
        const int MaxBitmapAcksPerMessage = 20;

        Dictionary<uint, RebuildI2NPMessage> Messages = new Dictionary<uint, RebuildI2NPMessage>();
        LinkedList<RebuildI2NPMessage> AckQueue = new LinkedList<RebuildI2NPMessage>();
        ItemFilterWindow<uint> DupFilter = new ItemFilterWindow<uint>( TickSpan.Seconds( RememberCompleteMessageIdsSeconds ), 1 );

        public bool GotAcks { get { return AckQueue.Count > 0; } }

        public RebuildI2NPMessage Add( DataFragment frag )
        {
            bool isdup = !DupFilter.Test( frag.MessageId );

            if ( isdup )
            {
                // Our acks where not received
                lock ( AckQueue )
                {
                    if ( !AckQueue.Where( m => m.MessageId == frag.MessageId ).Any() )
                    {
                        AckQueue.AddFirst( new AckI2NPMessage( frag.MessageId ) );
                    }
                }

#if LOG_ALL_TRANSPORT
                Logging.LogTransport( "SSU DataDefragmenter got msgid dup. Dropped. Will ACK " + frag.MessageId.ToString() );
#endif
                return null;
            }


            RebuildI2NPMessage msgbuilder;
            bool newmessage = false;
            lock ( Messages )
            {
                if ( !Messages.ContainsKey( frag.MessageId ) )
                {
                    msgbuilder = new RebuildI2NPMessage( frag.MessageId );
                    Messages[frag.MessageId] = msgbuilder;
                    newmessage = true;
                }
                else
                {
                    msgbuilder = Messages[frag.MessageId];
                }
            }

            if ( newmessage )
            {
                lock ( AckQueue )
                {
                    AckQueue.AddFirst( msgbuilder );
                }
            }

            var result = msgbuilder.Add( frag );
            if ( result != null )
            {
                lock ( Messages )
                {
                    Messages.Remove( frag.MessageId );
                }

                DupFilter.Update( frag.MessageId );
                return result;
            }

            return null;
        }

        PeriodicAction RemoveOldMessages = new PeriodicAction( TickSpan.Seconds( RememberIncompleteMessagesSeconds / 2 ) );

        public void SendAcks( BufRefLen writer, out bool explicitacks, out bool bitfieldsacks )
        {
            RemoveOldMessages.Do( () =>
            {
                RemoveExpired();
            } );

            List<RebuildI2NPMessage> expl = null;
            List<RebuildI2NPMessage> bitm = null;

            var space = writer.Length;

            var remainingackmsgs = AckQueue.Count;

            while ( space > 30 && 
                ( expl == null || expl.Count < MaxExplicitAcksPerMessage ) && 
                ( bitm == null || bitm.Count < MaxBitmapAcksPerMessage ) &&
                remainingackmsgs-- > 0 )
            {
                RebuildI2NPMessage msg = null;
                lock ( AckQueue )
                {
                    if ( AckQueue.Count == 0 ) break;
                    var lln = AckQueue.Last;

                    msg = lln.Value;
                    if ( msg.AckSent.DeltaToNowMilliseconds < MillisecondsBetweenAcks ) continue;

                    AckQueue.RemoveLast();

                    bool resendsok;
                    if ( msg.AllFragmentsFound )
                    {
                        resendsok = msg.ExplicitAcksSent < NumberOfExplicitAckResends;
                    }
                    else
                    {
                        resendsok = msg.BitmapAcksSent < NumberOfBitmapAckResends;
                    }
                    if ( resendsok && msg != null ) AckQueue.AddFirst( lln );
                }

                if ( msg == null ) continue;

                if ( msg.AllFragmentsFound )
                {
                    if ( expl == null )
                    {
                        expl = new List<RebuildI2NPMessage>( MaxExplicitAcksPerMessage );
                        space -= 1;
                    }
                    space -= 4;
                    expl.Add( msg );
                }
                else
                {
                    if ( bitm == null )
                    {
                        bitm = new List<RebuildI2NPMessage>( MaxBitmapAcksPerMessage );
                        space -= 1;
                    }
                    space -= msg.AckBitmapSize;
                    bitm.Add( msg );
                }

            }

            if ( expl != null )
            {
                writer.Write8( (byte)expl.Count );
                foreach ( var msg in expl )
                {
#if LOG_ALL_TRANSPORT
                    Logging.LogTransport( "SSU DataDefragmenter sent expl ack: " + msg.MessageId.ToString() + " (" + msg.ExplicitAcksSent.ToString() + ")" );
#endif
                    writer.Write32( msg.MessageId );
                    msg.AckSent.SetNow();
                    ++msg.ExplicitAcksSent;
                }
            }

            if ( bitm != null )
            {
                writer.Write8( (byte)bitm.Count );
                foreach ( var msg in bitm )
                {
#if LOG_ALL_TRANSPORT
                    Logging.LogTransport( "SSU DataDefragmenter sent bitmap ack: " + msg.MessageId.ToString() );
#endif
                    writer.Write32( msg.MessageId );
                    writer.Write( msg.AckBitmap() );
                    msg.AckSent.SetNow();
                    ++msg.BitmapAcksSent;
                }
            }

            explicitacks = expl != null;
            bitfieldsacks = bitm != null;
        }

        private void RemoveExpired()
        {
            lock ( Messages )
            {
                var remove = Messages.Where( p => p.Value.Created.DeltaToNowSeconds
                    > RememberIncompleteMessagesSeconds ).ToArray();

                foreach ( var one in remove )
                {
#if LOG_ALL_TRANSPORT || LOG_MUCH_TRANSPORT
                    Logging.LogTransport( string.Format(
                        "SSU DataDefragmenter discarding incomplete message: {0} age {1}, Bitmap acks sent: {2}, expl acks: {3}. Bitmap: {4}.",
                        one.Key, one.Value.Created, one.Value.BitmapAcksSent, one.Value.ExplicitAcksSent, one.Value.CurrentlyACKedBitmapDebug() ) );
#endif
                    Messages.Remove( one.Key );
                }
            }

            lock ( AckQueue )
            {
                var remove = AckQueue.Where( p => p.Created.DeltaToNowSeconds
                    > RememberIncompleteMessagesSeconds ).ToArray();

                foreach ( var one in remove )
                {
#if LOG_ALL_TRANSPORT || LOG_MUCH_TRANSPORT
                    Logging.LogTransport( string.Format(
                        "SSU DataDefragmenter discarding incomplete message acks for: {0} age {1}, Bitmap acks sent: {2}, expl acks: {3}. Bitmap: {4}.",
                        one.MessageId, one.Created, one.BitmapAcksSent, one.ExplicitAcksSent, one.CurrentlyACKedBitmapDebug() ) );
#endif
                    AckQueue.Remove( one );
                }
            }
        }
    }
}

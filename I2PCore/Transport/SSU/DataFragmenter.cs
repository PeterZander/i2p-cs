using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Utils;
using I2PCore.Tunnel.I2NP.Messages;
using I2PCore.Tunnel.I2NP.Data;

namespace I2PCore.Transport.SSU
{
    public class DataFragmenter
    {
        public const int MaxTimeToRememberMessageForAckSeconds = 30;

        Dictionary<uint, FragmentedMessage> Messages = new Dictionary<uint, FragmentedMessage>();
        FragmentedMessage CurrentMessage = null;

        LinkedList<FragmentedMessage> AckQueue = new LinkedList<FragmentedMessage>();

        public DataFragmenter()
        {
        }

        public int Send( BufRefLen writer, LinkedList<II2NPHeader5> sendqueue )
        {
            var result = 0;

            while ( writer.Length > 10 )
            {
                if ( CurrentMessage == null || CurrentMessage.AllFragmentsSent )
                {
                    CurrentMessage = null;

                    lock ( sendqueue )
                    {
                        if ( sendqueue.Count == 0 ) return result;
                        CurrentMessage = new FragmentedMessage( sendqueue.First.Value );
                        sendqueue.RemoveFirst();
                    }

                    if ( CurrentMessage != null ) lock ( Messages )
                    {
                        Messages[CurrentMessage.MessageId] = CurrentMessage;
#if LOG_ALL_TRANSPORT
                        DebugUtils.Log( "SSU Message to fragment: " + CurrentMessage.MessageId.ToString() + 
                            ", (" + CurrentMessage.Message.MessageType.ToString() + ")." );
#endif
                    }
                }

                if ( CurrentMessage.Send( writer ) != null ) ++result;

                if ( CurrentMessage.AllFragmentsSent )
                {
                    lock ( AckQueue )
                    {
                        AckQueue.AddFirst( CurrentMessage );
                    }
#if LOG_ALL_TRANSPORT
                    DebugUtils.Log( "SSU Message " + CurrentMessage.MessageId.ToString() + " all fragments sent." );
#endif
                }
            }

            return result;
        }

        public bool GotUnsentFragments { get { return CurrentMessage != null; } }
        public bool AllFragmentsAcked { get { return AckQueue.Count == 0; } }

        PeriodicAction RemoveOldMessages = new PeriodicAction( TickSpan.Seconds( 45 ) );

        public IEnumerable<DataFragment> NotAckedFragments()
        {
            RemoveOldMessages.Do( () =>
            {
                CleanUpOldMessages();
            } );

            lock ( AckQueue )
            {
                var one = AckQueue.First;
                while ( one != null )
                {
                    var next = one.Next;

                    if ( !one.Value.AllFragmentsAcked )
                    {
                        var naf = one.Value.NotAckedFragments();
                        foreach ( var frag in naf ) yield return frag;
                    }
                    else
                    {
                        AckQueue.Remove( one );
                    }

                    one = next;
                }
            }
        }

        private void CleanUpOldMessages()
        {
            lock ( Messages )
            {
                var remove = Messages.Where( p => p.Value.Created.DeltaToNowSeconds
                    > MaxTimeToRememberMessageForAckSeconds ).ToArray();

                foreach ( var one in remove )
                {

#if LOG_ALL_TRANSPORT || LOG_MUCH_TRANSPORT
                    DebugUtils.LogDebug( () => string.Format(
                            "SSU DataFragmenter discarding unACKed message: {0}, expl sends {1}, frag sends {2}/{3}, age {4}, " +
                            "Expl ACK: {5}, Bitmap acks: {6}.",
                            one.Key, one.Value.SendCount, one.Value.FragmentSendCount(), one.Value.FragmentCount(), one.Value.Created, 
                            one.Value.AllFragmentsAcked, one.Value.BitmapACKStatusDebug() ) );
#endif
                    Messages.Remove( one.Key );
                }
            }

            lock ( AckQueue )
            {
                var remove = AckQueue.Where( p => p.Created.DeltaToNowSeconds
                    > MaxTimeToRememberMessageForAckSeconds ).ToArray();

                foreach ( var one in remove )
                {
#if LOG_ALL_TRANSPORT || LOG_MUCH_TRANSPORT
                    DebugUtils.LogDebug( () => string.Format(
                            "SSU DataFragmenter discarding queued unACKed message {0}, expl sends {1}, frag sends {2}/{3}, age {4}, " +
                            "Expl ACK: {5}, Bitmap acks: {6}.",
                            one.MessageId, one.SendCount, one.FragmentSendCount(), one.FragmentCount(), one.Created, 
                            one.AllFragmentsAcked, one.BitmapACKStatusDebug() ) );
#endif
                    AckQueue.Remove( one );
                }
            }
        }

        public void GotAck( List<uint> acks )
        {
#if LOG_ALL_TRANSPORT
            DebugUtils.Log( "SSU Received explicit ACKs: " + acks.Count.ToString() );
#endif
            lock ( Messages )
            {
                foreach( var ackmsgid in acks )
                {
                    if ( Messages.ContainsKey( ackmsgid ) )
                    {
                        Messages[ackmsgid].GotAck();
#if LOG_ALL_TRANSPORT
                        DebugUtils.Log( "SSU Message " + ackmsgid.ToString() + " fully ACKed." );
#endif

                        Messages.Remove( ackmsgid );
                    }
                    else
                    {
#if LOG_ALL_TRANSPORT
                        DebugUtils.Log( "SSU Explicit ACK for unknown message " + ackmsgid.ToString() + " received." );
#endif
                    }
                }
            }
        }

        public void GotAck( List<KeyValuePair<uint,List<byte>>> ackbf )
        {
            lock ( Messages )
            {
                foreach ( var ackinfo in ackbf )
                {
                    if ( Messages.ContainsKey( ackinfo.Key ) )
                    {
                        var msg = Messages[ackinfo.Key];
                        msg.GotAck( ackinfo.Value );
#if LOG_ALL_TRANSPORT
                        DebugUtils.Log( "SSU Message " + ackinfo.Key.ToString() + " bitmap ACK. Fully ACKed: " + msg.AllFragmentsAcked.ToString() );
#endif

                        if ( msg.AllFragmentsAcked )
                        {
                            Messages.Remove( ackinfo.Key );
                        }
                    }
                    else
                    {
#if LOG_ALL_TRANSPORT
                        DebugUtils.Log( "SSU Bitmap ACK for unknown message " + ackinfo.Key.ToString() + " received." );
#endif
                    }
                }
            }
        }
    }
}

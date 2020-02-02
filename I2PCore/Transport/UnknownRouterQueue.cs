using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using I2PCore.Tunnel.I2NP.Messages;
using I2PCore.Utils;
using I2PCore.Tunnel;

namespace I2PCore.Transport
{
 
    internal class UnknownRouterQueue
    {
        const int TimeBetweenRetriesSeconds = 7;
        const int TimeUntilConsideredUnresolvableSeconds = TimeBetweenRetriesSeconds * 3;
        const int MaxMessagesInQueue = 500;

        Dictionary<I2PIdentHash, LookupDestination> QueuedMessages = new Dictionary<I2PIdentHash, LookupDestination>();
        UnresolvableRouters CurrentlyUnresolvableRouters;

        internal UnknownRouterQueue( UnresolvableRouters unres )
        {
            CurrentlyUnresolvableRouters = unres;
            NetDb.Inst.IdentHashLookup.LookupFailure += new IdentResolver.IdentResolverResultFail( IdentHashLookup_LookupFailure );
            NetDb.Inst.IdentHashLookup.RouterInfoReceived += new IdentResolver.IdentResolverResultRouterInfo( IdentHashLookup_RouterInfoReceived );
        }

        void IdentHashLookup_RouterInfoReceived( I2PRouterInfo ri )
        {
            LookupDestination lud = null;

            lock ( QueuedMessages )
            {
                if ( QueuedMessages.TryGetValue( ri.Identity.IdentHash, out lud ) )
                {
                    Logging.LogTransport( "UnknownRouterQueue: IdentHashLookup_RouterInfoReceived: Destination " + 
                        ri.Identity.IdentHash.Id32Short + " found. Sending." );

                    QueuedMessages.Remove( ri.Identity.IdentHash );
                }
            }

            if ( lud != null )
            {
                try
                {
                    foreach ( var msg in lud.Messages )
                    {
                        TransportProvider.Send( ri.Identity.IdentHash, msg );
                    }
                }
                catch ( Exception ex )
                {
                    Logging.Log( "UnknownRouterQueue", ex );
                }
            }
        }

        void IdentHashLookup_LookupFailure( I2PIdentHash key )
        {
            LookupDestination lud = null;

            lock ( QueuedMessages )
            {
                if ( QueuedMessages.TryGetValue( key, out lud ) )
                {
                    Logging.LogTransport( "UnknownRouterQueue: IdentHashLookup_LookupFailure: Destination " + 
                        key.Id32Short + " not found. Marking unresolvable." );

                    QueuedMessages.Remove( key );
                }
            }

            if ( lud != null )
            {
                CurrentlyUnresolvableRouters.Add( key );
            }
        }

        internal int Count { get { return QueuedMessages.Count; } }

        internal void Add( I2PIdentHash dest, I2NPMessage msg )
        {
            if ( CurrentlyUnresolvableRouters.Contains( dest ) ) throw new RouterUnresolvableException( "Destination is tagged as unresolvable " + dest.ToString() );

            var sendlookup = false;

            lock ( QueuedMessages )
            {
                if ( !QueuedMessages.ContainsKey( dest ) )
                {
                    var newld = new LookupDestination( dest );
                    newld.Add( msg );
                    QueuedMessages[dest] = newld;
                    sendlookup = true;
                }
                else
                {
                    var queue = QueuedMessages[dest];
                    if ( queue.Messages.Count < MaxMessagesInQueue ) queue.Add( msg );
#if DEBUG
                    else
                    {
                        Logging.LogWarning( "UnknownRouterQueue: Add: Too many messages in queue. Dropping new message." );
                    }
#endif
                }
            }

            if ( sendlookup ) NetDb.Inst.IdentHashLookup.LookupRouterInfo( dest );
        }

        internal bool Contains( I2PIdentHash dest )
        {
            if ( CurrentlyUnresolvableRouters.Contains( dest ) ) throw new RouterUnresolvableException( "Unable to resolve " + dest.ToString() );

            lock ( QueuedMessages )
            {
                return QueuedMessages.ContainsKey( dest );
            }
        }

        internal LookupDestination[] FindKnown()
        {
            LookupDestination[] result;
            I2PIdentHash[] remove;

            lock ( QueuedMessages )
            {
                var found = QueuedMessages.Where( m => NetDb.Inst.Contains( m.Key ) );
                result = found.Select( m => m.Value ).ToArray();
                foreach ( var one in found.ToArray() )
                {
                    Logging.LogTransport( "UnknownRouterQueue: FindKnown: Destination " + one.Value.Destination.Id32Short + " found." );
                    QueuedMessages.Remove( one.Key );
                }

                remove = QueuedMessages.Where( m => m.Value.Created.DeltaToNowSeconds > TimeUntilConsideredUnresolvableSeconds ).
                    Select( m => m.Key ).ToArray();
                foreach ( var one in remove )
                {
                    Logging.LogTransport( "UnknownRouterQueue: FindKnown: Destination " + one.Id32Short + " timeout. Marked Unresolvable." );
                    QueuedMessages.Remove( one );
                }
            }

            foreach ( var one in remove )
            {
                CurrentlyUnresolvableRouters.Add( one );
                //NetDb.Inst.Statistics.DestinationInformationFaulty( one );
            }
            NetDb.Inst.RemoveRouterInfo( remove );

            return result;
        }
    }
}

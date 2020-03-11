using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.TunnelLayer.I2NP.Data;
using I2PCore.TunnelLayer.I2NP.Messages;
using I2PCore.Utils;
using I2PCore.Data;
using System.Collections.Concurrent;
using System.Collections;

namespace I2PCore.TunnelLayer
{
    public class TunnelDataFragmentReassembly
    {
        public static readonly TickSpan RememberUnmatchedFragmentsFor = TickSpan.Minutes( 10 );

        class TunnelDataFragmentList: IEnumerable<TunnelDataFragment>
        {
            public readonly TickCounter Created = new TickCounter();
            readonly List<TunnelDataFragment> List;

            public TunnelDataFragmentList()
            {
                List = new List<TunnelDataFragment>();
            }

            public TunnelDataFragmentList( List<TunnelDataFragment> list )
            {
                List = list;
            }

            public int Count { get => List.Count; }

            public TunnelDataFragment this[int ix]
            {
                get
                {
                    return List[ix];
                }
                set
                {
                    if ( Count <= ix )
                    {
                        List.AddRange( new TunnelDataFragment[ix - Count + 1] );
                    }

                    List[ix] = value;
                }
            }

            public IEnumerator<TunnelDataFragment> GetEnumerator()
            {
                return List.GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return List.GetEnumerator();
            }
        }

        ConcurrentDictionary<uint, TunnelDataFragmentList> MessageFragments = 
                new ConcurrentDictionary<uint, TunnelDataFragmentList>();

        public int BufferedFragmentCount
        {
            get
            {
                lock ( MessageFragments )
                {
                    return MessageFragments.Sum( mid => mid.Value.Count( fr => fr != null ) );
                }
            }
        }

        public TunnelDataFragmentReassembly()
        {
        }

        PeriodicAction RemoveUnmatchedFragments = new PeriodicAction( RememberUnmatchedFragmentsFor );

        public IEnumerable<TunnelMessage> Process( IEnumerable<TunnelDataMessage> tdmsgs, out bool failure )
        {
            var result = new List<TunnelMessage>();
            failure = false;

            RemoveUnmatchedFragments.Do( () =>
            {
                lock ( MessageFragments )
                {
                    var remove = MessageFragments.Where( p => p.Value.Created.DeltaToNow > RememberUnmatchedFragmentsFor ).
                        Select( p => p.Key ).ToArray();
                    foreach ( var key in remove )
                    {
                        Logging.LogDebug( $"TunnelDataFragmentReassembly: Removing old unmatched fragment for {key}" );
                        MessageFragments.TryRemove( key, out _ );
                    }
                }
            } );

            foreach ( var msg in tdmsgs )
            {
                var hash = I2PHashSHA256.GetHash( msg.TunnelDataPayload, msg.IV );
                var eq = BufUtils.Equal( msg.Checksum.PeekB( 0, 4 ), 0, hash, 0, 4 );
                if ( !eq )
                {
                    Logging.LogDebug( $"TunnelDataFragmentReassembly: SHA256 check failed in TunnelData." );
                    failure = true;
                    continue;
                }

                var reader = (BufRefLen)msg.TunnelDataPayload;

                while ( reader.Length > 0 )
                {
                    var frag = new TunnelDataFragment( reader );

                    if ( frag.FollowOnFragment )
                    {
                        var fragments = MessageFragments.GetOrAdd( frag.MessageId,
                                id => new TunnelDataFragmentList() );

                        fragments[frag.FragmentNumber] = frag;

                        CheckForAllFragmentsFound( result, frag.MessageId, fragments );
                    }
                    else
                    {
                        if ( frag.Fragmented )
                        {
                            var fragments = MessageFragments.GetOrAdd( frag.MessageId,
                                    id => new TunnelDataFragmentList() );

                            fragments[0] = frag;

                            CheckForAllFragmentsFound( result, frag.MessageId, fragments );
                        }
                        else
                        {
                            AddTunnelMessage( result, frag, frag.Payload );
                        }
                    }
                }
            }

            return result;
        }

        private void CheckForAllFragmentsFound( List<TunnelMessage> result, uint msgid, TunnelDataFragmentList fragments )
        {
            var lastfound = fragments.Count > 1 && fragments[fragments.Count - 1].LastFragment;
            if ( lastfound && !fragments.Any( f => f == null ) )
            {
                var s = new BufRefStream();
                for ( int i = 0; i < fragments.Count; ++i )
                {
                    s.Write( fragments[i].Payload );
                }
                AddTunnelMessage( result, fragments[0], new BufRefLen( s.ToByteArray() ) );
                MessageFragments.TryRemove( msgid, out _ );
            }
        }

        private static void AddTunnelMessage( List<TunnelMessage> result, TunnelDataFragment initialfragment, BufRefLen buf )
        {
            switch ( initialfragment.Delivery )
            {
                case TunnelMessage.DeliveryTypes.Local:
                    result.Add( 
                        new TunnelMessageLocal( 
                            I2NPMessage.ReadHeader16( buf ).Message ) );
                    break;

                case TunnelMessage.DeliveryTypes.Router:
                    result.Add( new TunnelMessageRouter( 
                        I2NPMessage.ReadHeader16( buf ).Message,
                        new I2PIdentHash( (BufRefLen)initialfragment.ToHash ) ) );
                    break;

                case TunnelMessage.DeliveryTypes.Tunnel:
                    result.Add( 
                        new TunnelMessageTunnel( 
                            I2NPMessage.ReadHeader16( buf ).Message,
                            new I2PIdentHash( (BufRefLen)initialfragment.ToHash ),
                            initialfragment.Tunnel ) );
                    break;
            }
        }

    }
}

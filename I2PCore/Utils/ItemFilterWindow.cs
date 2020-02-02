using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;

namespace I2PCore.Utils
{
    public class ItemFilterWindow<T> : IEnumerable<T>
    {
        private readonly TickSpan MemorySpan;
        readonly int Limit;
        Dictionary<T, LinkedList<TickCounter>> Memory = new Dictionary<T, LinkedList<TickCounter>>();

        TickCounter LastCleanup = TickCounter.Now;

        public ItemFilterWindow( TickSpan span, int limit )
        {
            MemorySpan = span;
            Limit = limit;
        }

        /// <summary>
        /// Returns True if the number of occurances (including a new one now) in the memory span is below the limit.
        /// </summary>
        public bool Update( T ident )
        {
            lock ( Memory )
            {
                if ( LastCleanup.DeltaToNowSeconds > 240 )
                {
                    Cleanup();
                }

                if ( !Memory.TryGetValue( ident, out var list ) )
                {
                    list = new LinkedList<TickCounter>();
                    Memory[ident] = list;
                }

                list.AddLast( TickCounter.Now );
                return list.Count < Limit;
            }
        }

        /// <summary>
        /// Returns True if the number of occurances in the memory span is below limit without changing the set.
        /// </summary>
        public bool Test( T ident )
        {
            lock ( Memory )
            {
                if ( LastCleanup.DeltaToNowSeconds > 240 )
                {
                    Cleanup();
                }

                if ( Memory.TryGetValue( ident, out var list ) )
                {
                    return list.Count( t => t.DeltaToNowMilliseconds < MemorySpan.ToMilliseconds ) < Limit;
                }

                return true;
            }
        }

        public int Count( T ident )
        {
            lock ( Memory )
            {
                if ( LastCleanup.DeltaToNowSeconds > 240 )
                {
                    Cleanup();
                }

                if ( Memory.TryGetValue( ident, out var list ) )
                {
                    return list.Count( t => t.DeltaToNowMilliseconds < MemorySpan.ToMilliseconds );
                }

                return 0;
            }
        }

        public void ProcessEvents( T key, Action<TickCounter> action )
        {
            lock ( Memory )
            {
                if ( Memory.ContainsKey( key ) )
                {
                    var active = Memory[key]
                        .Where( t => t.DeltaToNowMilliseconds < MemorySpan.ToMilliseconds );
                    foreach ( var one in active ) action( one );
                }
            }
        }

        // Lock Memory before calling
        void Cleanup()
        {
            LastCleanup.SetNow();

            foreach ( var identpair in Memory.ToArray() )
            {
                while ( identpair.Value.Count > 0 && identpair.Value.First.Value.DeltaToNowMilliseconds > MemorySpan.ToMilliseconds )
                    identpair.Value.RemoveFirst();
                if ( identpair.Value.Count == 0 ) Memory.Remove( identpair.Key );
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            lock ( Memory )
            {
                return Memory
                    .Where( k => k.Value.Count( t => t.DeltaToNowMilliseconds < MemorySpan.ToMilliseconds ) >= Limit )
                    .Select( k => k.Key )
                    .ToList<T>()
                    .GetEnumerator();
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            lock ( Memory )
            {
                return Memory
                    .Where( k => k.Value.Count( t => t.DeltaToNowMilliseconds < MemorySpan.ToMilliseconds ) >= Limit )
                    .Select( k => k.Key )
                    .ToList<T>()
                    .GetEnumerator();
            }
        }
    }
}

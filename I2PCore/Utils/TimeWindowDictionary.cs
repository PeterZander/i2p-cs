using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace I2PCore.Utils
{
    public class TimeWindowDictionary<T, V> : IEnumerable<KeyValuePair<T, V>> where V : class 
    {
        TickSpan MemorySpan;
        Dictionary<T, KeyValuePair<V, TickCounter>> Memory = new Dictionary<T, KeyValuePair<V, TickCounter>>();

        TickCounter LastCleanup = TickCounter.Now;

        public TimeWindowDictionary( TickSpan span )
        {
            MemorySpan = span;
        }

        public V this[T key]
        {
            get { return Get( key ); }
            set { Set( key, value ); }
        }

        public void Set( T ident, V value )
        {
            lock ( Memory )
            {
                if ( LastCleanup.DeltaToNowSeconds > 240 )
                {
                    Cleanup();
                }

                KeyValuePair<V, TickCounter> pair;

                if ( Memory.TryGetValue( ident, out pair ) )
                {
                    pair.Value.SetNow();
                }
                else
                {
                    Memory[ident] = new KeyValuePair<V, TickCounter>( value, TickCounter.Now );
                }
            }
        }

        /// <summary>
        /// Returns null if item have not been stored or is too old.
        /// </summary>
        public V Get( T ident )
        {
            lock ( Memory )
            {
                if ( LastCleanup.DeltaToNowSeconds > 240 )
                {
                    Cleanup();
                }

                KeyValuePair<V, TickCounter> pair;

                if ( Memory.TryGetValue( ident, out pair ) )
                {
                    if ( pair.Value.DeltaToNow > MemorySpan )
                    {
                        Memory.Remove( ident );
                        return null;
                    }
                    return pair.Key;
                }

                return null;
            }
        }

        public bool Remove( T ident )
        {
            lock ( Memory )
            {
                if ( LastCleanup.DeltaToNowSeconds > 240 )
                {
                    Cleanup();
                }

                return Memory.Remove( ident );
            }
        }

        public V Get( T ident, Func<V> generator )
        {
            var result = Get( ident );
            if ( result != null ) return result;
            result = generator();
            Set( ident, result );
            return result;
        }

        public void ProcessItem( T key, Action<T,V> action )
        {
            lock ( Memory )
            {
                KeyValuePair<V, TickCounter> pair;

                if ( Memory.TryGetValue( key, out pair ) )
                {
                    action( key, pair.Key );
                }
            }
        }

        // Lock Memory before calling
        void Cleanup()
        {
            LastCleanup.SetNow();

            foreach ( var identpair in Memory.ToArray() )
            {
                if ( identpair.Value.Value.DeltaToNow > MemorySpan ) Memory.Remove( identpair.Key );
            }
        }

        #region IEnumerable<KeyValuePair<T,V>> Members
        public IEnumerator<KeyValuePair<T, V>> GetEnumerator()
        {
            lock ( Memory ) Cleanup();
            return Memory.AsEnumerable().Select( p => new KeyValuePair<T, V>( p.Key, p.Value.Key ) ).GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            lock ( Memory ) Cleanup();
            return Memory.AsEnumerable().Select( p => new KeyValuePair<T, V>( p.Key, p.Value.Key ) ).GetEnumerator();
        }
        #endregion
    }
}

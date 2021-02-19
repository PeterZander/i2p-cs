using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace I2PCore.Utils
{
    public class TimeWindowDictionary<T, V> : IDisposable, IEnumerable<KeyValuePair<T, V>> where V : class
    {
        TickSpan MemorySpan;
        ConcurrentDictionary<T, KeyValuePair<V, TickCounter>> Memory = 
                new ConcurrentDictionary<T, KeyValuePair<V, TickCounter>>();

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

        internal void Clear()
        {
            Memory.Clear();
        }

        public bool IsEmpty
        {
            get
            {
                Cleanup();
                return Memory.IsEmpty;
            }
        }

        public int Count
        {
            get
            {
                Cleanup();
                return Memory.Count;
            }
        }

        public void Set( T ident, V value )
        {
            if ( LastCleanup.DeltaToNowSeconds > 240 )
            {
                Cleanup();
            }

            if ( Memory.TryGetValue( ident, out var pair ) )
            {
                pair.Value.SetNow();
            }
            else
            {
                Memory[ident] = new KeyValuePair<V, TickCounter>( value, TickCounter.Now );
            }
        }

        public bool TryGetValue( T ident, out V value )
        {
            if ( LastCleanup.DeltaToNowSeconds > 240 )
            {
                Cleanup();
            }

            if ( Memory.TryGetValue( ident, out var pair ) )
            {
                if ( pair.Value.DeltaToNow > MemorySpan )
                {
                    RemoveAndDispose( ident, out _ );
                    value = null;
                    return false;
                }

                value = pair.Key;
                return true;
            }

            value = null;
            return false;
        }

        /// <summary>
        /// Returns null if item have not been stored or is too old.
        /// </summary>
        public V Get( T ident )
        {
            if ( LastCleanup.DeltaToNowSeconds > 240 )
            {
                Cleanup();
            }

            if ( Memory.TryGetValue( ident, out var pair ) )
            {
                if ( pair.Value.DeltaToNow > MemorySpan )
                {
                    RemoveAndDispose( ident, out _ );
                    return null;
                }
                return pair.Key;
            }

            return null;
        }

        public bool Remove( T ident )
        {
            if ( LastCleanup.DeltaToNowSeconds > 240 )
            {
                Cleanup();
            }

            return RemoveAndDispose( ident, out _ );
        }

        protected bool RemoveAndDispose( T ident, out V value )
        {
            var result = Memory.TryRemove( ident, out var removed );
            value = removed.Key;

            if ( value is IDisposable )
            {
                ( (IDisposable)value ).Dispose();
            }

            return result;
        }

        public bool TryRemove( T ident, out V value )
        {
            if ( LastCleanup.DeltaToNowSeconds > 240 )
            {
                Cleanup();
            }

            var result = RemoveAndDispose( ident, out var val );
            value = val;
            return result;
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
            if ( Memory.TryGetValue( key, out var pair ) )
            {
                action( key, pair.Key );
            }
        }

        // Lock Memory before calling
        void Cleanup()
        {
            LastCleanup.SetNow();

            foreach ( var identpair in Memory.ToArray() )
            {
                if ( identpair.Value.Value.DeltaToNow > MemorySpan )
                {
                    RemoveAndDispose( identpair.Key, out _ );
                }
            }
        }

        #region IEnumerable<KeyValuePair<T,V>> Members
        public IEnumerator<KeyValuePair<T, V>> GetEnumerator()
        {
            Cleanup();
            return Memory
                .AsEnumerable()
                .Select( p => new KeyValuePair<T, V>( p.Key, p.Value.Key ) )
                .GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            Cleanup();
            return Memory
                    .AsEnumerable()
                    .Select( p => new KeyValuePair<T, V>( p.Key, p.Value.Key ) )
                    .GetEnumerator();
        }

        void IDisposable.Dispose()
        {
            foreach ( var identpair in Memory.ToArray() )
            {
                RemoveAndDispose( identpair.Key, out _ );
            }
        }
        #endregion
    }
}

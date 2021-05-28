using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace I2PCore.Utils
{
    public class TickSpan : IEquatable<TickSpan>, IComparable<TickSpan>
    {
        public readonly int Ticks;

        public TickSpan( int ticks )
        {
            Ticks = ticks;
        }

        public TickSpan( double ticks )
        {
            Ticks = (int)ticks;
        }

        public int ToMilliseconds { get { return Ticks; } }
        public float ToSeconds { get { return Ticks / 1000f; } }
        public float ToMinutes { get { return Ticks / ( 60f * 1000f ); } }
        public float ToHours { get { return Ticks / ( 60f * 60f * 1000f ); } }
        public float ToDays { get { return Ticks / ( 24f * 60f * 60f * 1000f ); } }

        public static TickSpan Milliseconds( int ms ) { return new TickSpan( ms ); }
        public static TickSpan Seconds( int s ) { return new TickSpan( s * 1000 ); }
        public static TickSpan Seconds( double s ) { return new TickSpan( (int)( s * 1000 ) ); }
        public static TickSpan Minutes( int minutes ) { return new TickSpan( minutes * 60 * 1000 ); }
        public static TickSpan Hours( int hours ) { return new TickSpan( hours * 60 * 60 * 1000 ); }
        public static TickSpan Days( int days ) { return new TickSpan( days * 24 * 60 * 60 * 1000 ); }

        class TimeDivisionName
        {
            public char FormatCode;
            public string Label;
            public int Milliseconds;
        }

        static TimeDivisionName[] TimeDivisions = new TimeDivisionName[] {
            new TimeDivisionName {
                FormatCode = 'D',
                Label = "d",
                Milliseconds = 1000 * 60 * 60 * 24
            },
            new TimeDivisionName {
                FormatCode = 'H',
                Label = "h",
                Milliseconds = 1000 * 60 * 60
            },
            new TimeDivisionName {
                FormatCode = 'M',
                Label = "m",
                Milliseconds = 1000 * 60
            },
            new TimeDivisionName {
                FormatCode = 'S',
                Label = "s",
                Milliseconds = 1000
            },
            new TimeDivisionName {
                FormatCode = 'm',
                Label = "ms",
                Milliseconds = 1
            },
        };

        public static string DebugText( TickSpan tickspan, string format = null )
        {
            var result = new StringBuilder();
            var reminder = tickspan.ToMilliseconds;

            foreach( var span in TimeDivisions )
            {
                var remove = reminder / span.Milliseconds;
                if ( remove > 0 && ( format?.Contains( span.FormatCode ) ?? true ) )
                {
                    result.AppendFormat( "{0}{1}{2}",
                        ( result.Length == 0 ? "" : " " ),
                        remove,
                        span.Label );
                    reminder -= remove * span.Milliseconds;
                }
            }

            return result.ToString();
        }

        public override string ToString()
        {
            return $"TickSpan: {DebugText( this )}";
        }

        /// <summary>
        /// Generate a string with only selected time spans: "D" days, "H" hours, "M" minutes, "S" seconds, "m" milliseconds.
        /// </summary>
        public string ToString( string format )
        {
            return $"{DebugText( this, format )}";
        }
        #region IEquatable<TickSpan> Members

        bool IEquatable<TickSpan>.Equals( TickSpan other )
        {
            if ( other is null ) return false;
            if ( Object.ReferenceEquals( this, other ) ) return true;
            return Ticks == other.Ticks;
        }

        public override bool Equals( object obj )
        {
            if ( obj is null ) return false;
            if ( Object.ReferenceEquals( this, obj ) ) return true;
            var other = obj as TickSpan;
            if ( other is null ) return false;
            return Ticks == other.Ticks;
        }

        public override int GetHashCode()
        {
            return Ticks;
        }

        public static bool operator ==( TickSpan left, TickSpan right )
        {
            if ( left is null && right is null ) return true;
            if ( left is null || right is null ) return false;
            if ( Object.ReferenceEquals( left, right ) ) return true;
            return left.Ticks == right.Ticks;
        }

        public static bool operator !=( TickSpan left, TickSpan right )
        {
            if ( left is null && right is null ) return false;
            if ( left is null || right is null ) return true;
            if ( Object.ReferenceEquals( left, right ) ) return false;
            return left.Ticks != right.Ticks;
        }

        #endregion

        #region IComparable<TickSpan> Members

        int IComparable<TickSpan>.CompareTo( TickSpan other )
        {
            if ( other is null ) return 1;
            if ( Object.ReferenceEquals( this, other ) ) return 0;
            return Ticks.CompareTo( other.Ticks );
        }

        public static bool operator >( TickSpan left, TickSpan right )
        {
            if ( left is null && !( right is null ) ) return false;
            if ( !( left is null ) && right is null ) return true;
            if ( Object.ReferenceEquals( left, right ) ) return false;
            return left.Ticks > right.Ticks;
        }

        public static bool operator <( TickSpan left, TickSpan right )
        {
            if ( left is null && !( right is null ) ) return true;
            if ( !( left is null ) && right is null ) return false;
            if ( Object.ReferenceEquals( left, right ) ) return false;
            return left.Ticks < right.Ticks;
        }

        public static TickSpan operator +( TickSpan left, TickSpan right )
        {
            if ( left is null || right is null ) return null;
            return new TickSpan( left.Ticks + right.Ticks );
        }

        public static TickSpan operator -( TickSpan left, TickSpan right )
        {
            if ( left is null || right is null ) return null;
            return new TickSpan( left.Ticks - right.Ticks );
        }

        public static TickSpan operator *( TickSpan left, int multi )
        {
            if ( left is null ) return null;
            return new TickSpan( left.Ticks * multi );
        }

        public static TickSpan operator *( TickSpan left, double multi )
        {
            if ( left is null ) return null;
            return new TickSpan( left.Ticks * multi );
        }

        public static TickSpan operator /( TickSpan left, int divi )
        {
            if ( left is null ) return null;
            return new TickSpan( left.Ticks / divi );
        }

        public static TickSpan operator /( TickSpan left, double divi )
        {
            if ( left is null ) return null;
            return new TickSpan( left.Ticks / divi );
        }
        public static explicit operator TimeSpan( TickSpan tickspan )
        {
            if ( tickspan is null ) return default( TimeSpan );
            return TimeSpan.FromMilliseconds( (double)tickspan.Ticks );
        }
        #endregion
    }

    public class TickCounter: IComparable<TickCounter>
    {
        int TickCount;
        public int Ticks { get { return TickCount; } }

        public TickCounter()
        {
            SetNow();
        }

        public TickCounter( int value )
        {
            TickCount = value & int.MaxValue;
        }

        public static int NowMilliseconds
        {
            get
            {
                return Environment.TickCount & int.MaxValue;
            }
        }

        public static TickCounter Now
        {
            get
            {
                return new TickCounter( Environment.TickCount );
            }
        }

        public static TickCounter MaxDelta
        {
            get
            {
                return new TickCounter( (int)( ( (long)Environment.TickCount + int.MaxValue / 2 ) & int.MaxValue ) );
            }
        }

        public void SetNow()
        {
            TickCount = NowMilliseconds;
        }

        public void Set( int val )
        {
            TickCount = val & int.MaxValue;
        }

        public int DeltaToNowMilliseconds
        {
            get
            {
                return TimeDeltaMS( Environment.TickCount & int.MaxValue, TickCount );
            }
        }

        public int DeltaToNowSeconds
        {
            get
            {
                return TimeDeltaMS( Environment.TickCount & int.MaxValue, TickCount ) / 1000;
            }
        }

        public TickSpan DeltaToNow
        {
            get
            {
                return TimeDelta( Now, this );
            }
        }

        public static TickSpan TimeDelta( TickCounter end, TickCounter start )
        {
            return new TickSpan( TimeDeltaMS( end.TickCount, start.TickCount ) );
        }

        // Taking care of counter wraparound
        public static int TimeDeltaMS( int end, int start )
        {
            return start <= end ? end - start : int.MaxValue - start + end;
        }

        public static TickSpan operator -( TickCounter left, TickCounter right )
        {
            if ( left is null || right is null ) throw new ArgumentException( "Tickcounter op - null" );
            return TimeDelta( left, right );
        }

        public static TickCounter operator -( TickCounter left, int right )
        {
            if ( left is null ) throw new ArgumentException( "Tickcounter op - null" );
            return new TickCounter( left.TickCount - right );
        }

        public static TickCounter operator +( TickCounter left, TickSpan span )
        {
            if ( left is null || span is null ) throw new ArgumentException( "Tickcounter op - null" );
            return new TickCounter( left.TickCount + span.Ticks );
        }

        public static TickCounter operator -( TickCounter left, TickSpan span )
        {
            if ( left is null || span is null ) throw new ArgumentException( "Tickcounter op - null" );
            return new TickCounter( left.TickCount - span.Ticks );
        }

        public override string ToString()
        {
            return $"delta {TickSpan.DebugText( DeltaToNow )}";
        }

        public int CompareTo( TickCounter other )
        {
            if ( other is null ) return 1;
            if ( Object.ReferenceEquals( this, other ) ) return 0;
            return DeltaToNowMilliseconds.CompareTo( other.DeltaToNowMilliseconds );
        }
    }
}

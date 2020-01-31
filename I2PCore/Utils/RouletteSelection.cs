using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Data;
using I2PCore.Utils;
using I2PCore.Router;

namespace I2PCore.Utils
{
    public class RouletteSelection<T,K>
    {
        public class RouletteSpace<K2>
        {
            public K2 Id;
            public float Space;
            public float Fit;

            public override string ToString()
            {
                return string.Format( "RouletteSpace: Fit {0:0.00}, Space {1:0.00}: {2}", Fit, Space, Id );
            }
        }

        public readonly List<RouletteSpace<K>> Wheel = new List<RouletteSpace<K>>();
        public readonly IEnumerable<RouletteSpace<K>> WheelAverageOrBetter;
        float TotalSpaceSum;

        public float AverageFit;
        public float StdDevFit;

        public float MinFit { get; protected set; }
        public float MaxFit { get; protected set; }

        public static float RandomFitEMA = 0f;

        public RouletteSelection( IEnumerable<T> infos, Func<T,K> selkey, Func<K,float> selfit )
        {
            MinFit = float.MaxValue;
            TotalSpaceSum = 0;

            foreach ( var info in infos )
            {
                var space = new RouletteSpace<K>()
                {
                    Id = selkey( info ),
                };
                space.Fit = selfit( space.Id );

                if ( space.Fit < MinFit ) MinFit = space.Fit;
                Wheel.Add( space );
            }

            if ( !Wheel.Any() )
            {
                MinFit = 0f;
                MaxFit = 0f;
                AverageFit = 0f;
                return;
            }

            MaxFit = Wheel.Max( sp => sp.Fit );
            AverageFit = Wheel.Average( sp => sp.Fit );
            StdDevFit = Wheel.StdDev( sp => sp.Fit );

            var limit = AverageFit - StdDevFit / 10f;
            var goodlist = Wheel.Where( sp => sp.Fit >= limit );
            WheelAverageOrBetter = goodlist.Count() > 100 ? goodlist.ToArray() : Wheel.ToArray();

            foreach ( var one in Wheel )
            {
                one.Space = one.Fit - MinFit + 1;
                TotalSpaceSum += one.Space;
            }
        }

        public IEnumerable<RouletteSelection<T, K>.RouletteSpace<K>> MinItems 
        { 
            get 
            {
                var minval = Wheel.Min( sp => sp.Fit );
                return Wheel.Where( sp => sp.Fit == minval ); 
            } 
        }

        public IEnumerable<RouletteSelection<T, K>.RouletteSpace<K>> AvgItems 
        { 
            get 
            { 
                return Wheel.Where( sp => Math.Abs( sp.Fit - AverageFit ) < StdDevFit * 0.3 ); 
            } 
        }

        public IEnumerable<RouletteSelection<T, K>.RouletteSpace<K>> MaxItems 
        { 
            get 
            {
                var maxval = Wheel.Max( sp => sp.Fit );
                return Wheel.Where( sp => sp.Fit == maxval ); 
            } 
        }

        public K GetWeightedRandom()
        {
            lock ( Wheel )
            {
                var pos = 0f;
                var target = BufUtils.RandomFloat( TotalSpaceSum );

                foreach ( var one in Wheel )
                {
                    pos += one.Space;
                    if ( pos >= target )
                    {
                        RandomFitEMA = ( 49 * RandomFitEMA + one.Space + MinFit - 1 ) / 50f;
                        Logging.LogDebug( () => "Roulette: " + one.Id.ToString() );
                        return one.Id;
                    }
                }

                return Wheel[BufUtils.RandomInt( Wheel.Count )].Id;
            }
        }
    }
}

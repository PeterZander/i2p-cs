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
            public double Space;
            public float Fit;

            public override string ToString()
            {
                return $"RouletteSpace: Fit {Fit:0.00}, Space {Space:0.00}: {Id}";
            }
        }

        private const double Elitism = 5.0;
        private const double MinAbsDevs = 1.0;

        public readonly IEnumerable<RouletteSpace<K>> Wheel;
        readonly double TotalSpaceSum;

        public readonly float AverageFit;
        public readonly float AbsDevFit;
        public readonly float StdDevFit;

        public readonly float MinFit;
        public readonly float MaxFit;

        public RouletteSelection( 
            IEnumerable<T> infos, 
            Func<T,K> selkey, 
            Func<K,float> selfit )
        {
            MinFit = float.MaxValue;
            TotalSpaceSum = 0;

            var newwheel = new List<RouletteSpace<K>>();
            foreach ( var info in infos )
            {
                var space = new RouletteSpace<K>()
                {
                    Id = selkey( info ),
                };
                space.Fit = selfit( space.Id );

                if ( space.Fit < MinFit ) MinFit = space.Fit;
                newwheel.Add( space );
            }

            Wheel = newwheel;

            if ( !Wheel.Any() )
            {
                MinFit = 0f;
                MaxFit = 0f;
                AverageFit = 0f;
                return;
            }

            var fits = Wheel.Select( sp => sp.Fit );
            MaxFit = fits.Max();
            AverageFit = fits.Average();
            AbsDevFit = fits.AbsDev();
            StdDevFit = fits.StdDev();

            if ( AbsDevFit > 1.0 )
            {
                var limit = AverageFit - MinAbsDevs * AbsDevFit;
                Wheel = Wheel.Where( sp => sp.Fit > limit );

                fits = Wheel.Select( sp => sp.Fit );
                MinFit = fits.Min();
                MaxFit = fits.Max();
                AverageFit = fits.Average();
                AbsDevFit = fits.AbsDev();
                StdDevFit = fits.StdDev();
            }

            // Make positive, and offset bottom from 0
            var baseoffset = Math.Max( 0.01, AbsDevFit );
            var offset = -MinFit + baseoffset;
            foreach ( var one in Wheel )
            {
                one.Space = Math.Pow( one.Fit + offset, Elitism );
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

        public K GetWeightedRandom( HashSet<K> exclude )
        {
            lock ( Wheel )
            {
                var pos = 0.0;
                var subset = Wheel.Where( one => !exclude.Contains( one.Id ) );
                var subsetsum = subset.Sum( one => one.Space );
                var target = BufUtils.RandomDouble( subsetsum );

                foreach ( var one in subset )
                {
                    pos += one.Space;
                    if ( pos >= target )
                    {
                        Logging.LogDebugData( $"Roulette: {one.Id}" );
                        return one.Id;
                    }
                }

                return subset.Random().Id;
            }
        }
    }
}

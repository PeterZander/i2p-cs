using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("I2PCore.NTests")]
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

        private const double Elitism = 1.002;
        internal const int IncludeTop = 3000;

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
            TotalSpaceSum = 0;

            var newwheel = new ConcurrentBag<RouletteSpace<K>>();
            foreach ( var info in infos )
            {
                var space = new RouletteSpace<K>()
                {
                    Id = selkey( info ),
                };
                space.Fit = selfit( space.Id );

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

            var i = 1.0;

            var selection = Wheel
                .Select( sp => sp );

            var selcount = selection.Count();

            if ( selcount > IncludeTop )
            {
                selection = selection
                    .Skip( selcount - IncludeTop )
                    .Take( IncludeTop );
            }

            Wheel = new ConcurrentBag<RouletteSpace<K>>( selection );

            var fits = Wheel.Select( sp => sp.Fit );
            MinFit = fits.Min();
            MaxFit = fits.Max();
            AverageFit = fits.Average();
            AbsDevFit = fits.AbsDev();
            StdDevFit = fits.StdDev();

            foreach ( var one in Wheel.OrderBy( sp => sp.Fit ) )
            {
                one.Space = i;
                i *= Elitism;
                TotalSpaceSum += one.Space;
            }
        }

        public K GetWeightedRandom( ConcurrentBag<K> exclude )
        {
            lock ( Wheel )
            {
                var pos = 0.0;

                var subset = Wheel;

                if ( exclude != null && exclude.Any() )
                {
                    subset = Wheel.Where( one => !exclude.Contains( one.Id ) );
                }

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

                if ( subset.Any() )
                {
                    return subset.Random().Id;
                }

                return Wheel.Random().Id;
            }
        }
    }
}

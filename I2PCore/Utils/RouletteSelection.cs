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

        private const double Elitism = 1.009;
        private const int IncludeTop = 2000;

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

            var newwheel = new List<RouletteSpace<K>>();
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

            var fits = Wheel.Select( sp => sp.Fit );
            MinFit = fits.Min();
            MaxFit = fits.Max();
            AverageFit = fits.Average();
            AbsDevFit = fits.AbsDev();
            StdDevFit = fits.StdDev();

            var i = 0.01;

            var selection = Wheel
                .OrderBy( sp => sp.Fit )
                .Select( sp => sp );

            var selcount = selection.Count();

            if ( selcount > IncludeTop )
            {
                selection = selection
                    .Skip( selcount - IncludeTop )
                    .Take( IncludeTop );
            }

            foreach ( var one in selection )
            {
                one.Space = i;
                i *= Elitism;
                TotalSpaceSum += one.Space;
            }
        }

        public K GetWeightedRandom( HashSet<K> exclude )
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

using System;
using System.Collections.Generic;
using System.Linq;

namespace CoreDTO.Tools
{
    public static class WeightRandomizer
    {
        public static T WeightedRandom<T>(this IEnumerable<T> sequence, Func<T, double> weightFunc, Random rand)
        {
            var totalWeight = sequence.Sum(weightFunc);
            var itemWeightIndex = rand.NextDouble() * totalWeight;
            var currentWeightIndex = 0.0;

            foreach (var item in from weightedItem in sequence select new { Value = weightedItem, Weight = weightFunc(weightedItem) })
            {
                currentWeightIndex += item.Weight;
                if (currentWeightIndex >= itemWeightIndex)
                {
                    return item.Value;
                }
            }

            return default(T);
        }
    }
}

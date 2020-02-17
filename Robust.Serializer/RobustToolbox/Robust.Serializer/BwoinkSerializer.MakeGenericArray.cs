using System;
using System.Collections;
using JetBrains.Annotations;

namespace Robust.Shared.Serialization
{

    public sealed partial class BwoinkSerializer
    {

        [CanBeNull]
        internal Array MakeGenericArray(IEnumerable objects, Type itemType, int size)
        {
            if (objects == null)
            {
                return null;
            }

            if (size < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            var array = Array.CreateInstance(itemType, size);

            if (size == 0)
            {
                return array;
            }

            var e = objects.GetEnumerator();

            for (var i = 0; i < size; i++)
            {
                if (!e.MoveNext())
                {
                    throw new NotImplementedException("Mismatched length and enumeration.");
                }

                array.SetValue(e.Current, i);
            }

            return array;
        }

    }

}
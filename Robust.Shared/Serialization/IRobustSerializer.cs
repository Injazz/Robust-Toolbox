using System;
using System.Collections.Generic;
using System.IO;

namespace Robust.Shared.Interfaces.Serialization
{
    public interface IRobustSerializer
    {
        void Initialize();
        void Serialize(Stream stream, object obj);
        void Serialize<T>(Stream stream, T obj);
        T Deserialize<T>(Stream stream);
        object Deserialize(Stream stream);
        bool CanSerialize(Type type);

        byte[] StringTableHash { get; }
        IReadOnlyList<string> StringTable { get; }

        bool UseCompression { get; set; }

        bool RegisterStrings(IEnumerable<string> strs, bool clear = false);


    }
}

using System;
using System.IO;

namespace Robust.Shared.Interfaces.Serialization
{
    public interface IRobustSerializer
    {
        void Initialize();
        void Serialize(Stream stream, object obj);
        T Deserialize<T>(Stream stream);
        object Deserialize(Stream stream);
        bool CanSerialize(Type type);

    }
}

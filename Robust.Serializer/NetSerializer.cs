using NetSerializer;
using Robust.Shared.Interfaces.Reflection;
using Robust.Shared.Interfaces.Serialization;
using Robust.Shared.IoC;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Robust.Shared.Network;

[module: IoCRegister(typeof(IRobustSerializer), typeof(Robust.Shared.Serialization.NetSerializer))]


namespace Robust.Shared.Serialization
{

    public class NetSerializer : IRobustSerializer
    {

        [Dependency]
#pragma warning disable 649
        private readonly IReflectionManager reflectionManager;
#pragma warning restore 649
        private Serializer Serializer;

        private HashSet<Type> SerializableTypes;

#region Statistics
        public static long TotalObjectsSerialized { get; private set; }

        public static long TotalObjectsDeserialized { get; private set; }

        public static long TotalBytesWritten { get; private set; }

        public static long TotalBytesRead { get; private set; }

        public long ObjectsSerialized
        {
            get => _objectsSerialized;
            private set
            {
                TotalObjectsSerialized += (value - _objectsSerialized);
                _objectsSerialized = value;
            }
        }

        public long ObjectsDeserialized
        {
            get => _objectsDeserialized;
            private set
            {
                TotalObjectsDeserialized += (value - _objectsDeserialized);
                _objectsDeserialized = value;
            }
        }

        public long BytesWritten
        {
            get => _bytesWritten;
            private set
            {
                TotalBytesWritten += (value - _bytesWritten);
                _bytesWritten = value;
            }
        }

        public long BytesRead
        {
            get => _bytesRead;
            private set
            {
                TotalBytesRead += (value - _bytesRead);
                _bytesRead = value;
            }
        }

        private long _bytesWritten;

        private long _bytesRead;

        private long _objectsSerialized;

        private long _objectsDeserialized;
#endregion

        public void Initialize()
        {
            _bytesWritten = 0;
            _bytesRead = 0;
            _objectsSerialized = 0;
            _objectsDeserialized = 0;

            var types = reflectionManager
                .FindTypesWithAttribute<NetSerializableAttribute>().ToList();
#if DEBUG
            foreach (var type in types)
            {
                if (type.Assembly.FullName.Contains("Server") || type.Assembly.FullName.Contains("Client"))
                {
                    throw new InvalidOperationException($"Type {type} is server/client specific but has a NetSerializableAttribute!");
                }
            }
#endif

            var settings = new Settings();
            Serializer = new Serializer(types, settings);
            SerializableTypes = new HashSet<Type>(Serializer.GetTypeMap().Keys);
        }

        public void Serialize(Stream stream, object toSerialize)
        {
            var writeStatsStream = new StatisticsGatheringStreamWrapper(stream);
            Serializer.Serialize(stream, toSerialize);
            BytesWritten += writeStatsStream.BytesWritten;
            ++ObjectsSerialized;
        }

        public void Serialize<T>(Stream stream, T obj)
            => Serialize(stream, (object)obj);

        public T Deserialize<T>(Stream stream)
        {
            return (T) Deserialize(stream);
        }

        public object Deserialize(Stream stream)
        {
            var readStatsStream = new StatisticsGatheringStreamWrapper(stream);
            var result = Serializer.Deserialize(stream);
            BytesRead += readStatsStream.BytesWritten;
            TotalBytesRead += readStatsStream.BytesWritten;
            ++ObjectsDeserialized;
            return result;
        }

        public bool CanSerialize(Type type)
        {
            return SerializableTypes.Contains(type);
        }

        public byte[] StringTableHash => Array.Empty<byte>();

        public IReadOnlyList<string> StringTable => Array.Empty<string>();

        public bool UseCompression { get; set; }

        public bool RegisterStrings(IEnumerable<string> strs, bool clear = false) => false;

    }

}

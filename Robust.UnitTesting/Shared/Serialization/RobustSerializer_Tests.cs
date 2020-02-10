using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using NUnit.Framework;
using Robust.Server.Reflection;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Interfaces.Reflection;
using Robust.Shared.Interfaces.Serialization;
using Robust.Shared.IoC;
using Robust.Shared.Serialization;

namespace Robust.UnitTesting.Shared.GameObjects
{

    [TestFixture, Serializable]
    class RobustSerializer_Tests
    {

        /// <summary>
        ///     Used to measure the size of <see cref="object"/>s in bytes. This is not actually a test,
        ///     but a useful benchmark tool, so i'm leaving it here.
        /// </summary>
        [Test]
        public void ComponentChangedSerialized()
        {
            var serializer = new RobustSerializer();

            serializer.Initialize();

            byte[] array;

            var source = new EntityState(new EntityUid(512),
                new List<ComponentChanged> // will not be null
                {
                    new ComponentChanged(true, 12, null),
                    new ComponentChanged(false, 23, "Collision"),
                    new ComponentChanged(false, 42, "BallisticBullet")
                },
                new List<ComponentState>() // will be null
            );
            using (var stream = new MemoryStream())
            {
                serializer.Serialize(stream, source);
                array = stream.ToArray();
            }

            Assert.IsNotEmpty(array);

            using (var stream = new MemoryStream(array, false))
            {
                var payload = (EntityState) serializer.Deserialize(stream);
                Assert.AreEqual(new EntityUid(512), payload.Uid);
                Assert.NotNull(payload.ComponentChanges);
                Assert.True(payload.ComponentChanges[0].Deleted);
                Assert.AreEqual(12, payload.ComponentChanges[0].NetID);
                Assert.IsNull(payload.ComponentChanges[0].ComponentName);
                Assert.False(payload.ComponentChanges[1].Deleted);
                Assert.AreEqual(23, payload.ComponentChanges[1].NetID);
                Assert.AreEqual("Collision", payload.ComponentChanges[1].ComponentName);
                Assert.False(payload.ComponentChanges[2].Deleted);
                Assert.AreEqual(42, payload.ComponentChanges[2].NetID);
                Assert.AreEqual("BallisticBullet", payload.ComponentChanges[2].ComponentName);
                Assert.IsNull(payload.ComponentStates);
            }

            Assert.Pass($"Size in Bytes: {array.Length.ToString()}");
        }

        [Test]
        public unsafe void RoundTripIntArray()
        {
            var serializer = new RobustSerializer();

            serializer.Initialize();

            byte[] array;

            const int sourceSize = 32;
            const int accountingForTypeAndSizeInfo = 4 + 4;
            var source = new int[sourceSize];
            var sourceBytes = sizeof(int) * sourceSize;
            fixed (int* p = &source[0])
            {
                RandomNumberGenerator.Fill(new Span<byte>(p, sourceBytes));
            }

            using (var stream = new MemoryStream())
            {
                serializer.Serialize(stream, source);
                array = stream.ToArray();
            }

            Assert.IsNotEmpty(array);

            using (var stream = new MemoryStream(array, false))
            {
                var payload = (int[]) serializer.Deserialize(stream);
                Assert.AreEqual(source, payload);
            }

            Console.WriteLine($"Size in Bytes: {array.Length.ToString()} (overhead of {array.Length - sourceBytes+accountingForTypeAndSizeInfo})");
        }

        [Test]
        public unsafe void RoundTripBoxedIntArray()
        {
            var serializer = new RobustSerializer();

            serializer.Initialize();

            byte[] array;

            const int sourceSize = 32;
            const int accountingForTypeAndSizeInfo = 4 + 4;
            var source = new object[sourceSize];
            var sourceBytes = sizeof(int) * sourceSize;
            for (var i = 0; i < sourceSize; ++i)
            {
                var v = 0;
                RandomNumberGenerator.Fill(new Span<byte>(&v, 4));
                source[i] = v;
            }

            using (var stream = new MemoryStream())
            {
                serializer.Serialize(stream, source);
                array = stream.ToArray();
            }

            Assert.IsNotEmpty(array);

            using (var stream = new MemoryStream(array, false))
            {
                var payload = (object[]) serializer.Deserialize(stream);
                Assert.AreEqual(source, payload);
            }

            Console.WriteLine($"Size in Bytes: {array.Length.ToString()} (overhead of {array.Length - sourceBytes+accountingForTypeAndSizeInfo})");
        }


    }

}

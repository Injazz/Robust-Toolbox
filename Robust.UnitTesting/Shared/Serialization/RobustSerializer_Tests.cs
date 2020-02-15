using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using JetBrains.Annotations;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.GameObjects.Components;
using Robust.Shared.GameObjects.Components.Map;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Serialization;

namespace Robust.UnitTesting.Shared.GameObjects
{

    [TestFixture, Serializable]
    partial class RobustSerializer_Tests
    {

        [OneTimeSetUp]
        public void SetUpConsoleTraceListener() => Trace.Listeners.Add(new ConsoleTraceListener() {Name = nameof(ConsoleTraceListener)});

        [OneTimeTearDown]
        public void TearDownConsoleTraceListener() => Trace.Listeners.Remove(nameof(ConsoleTraceListener));

        /// <summary>
        ///     Used to measure the size of <see cref="object"/>s in bytes. This is not actually a test,
        ///     but a useful benchmark tool, so i'm leaving it here.
        /// </summary>
        [Test]
        public void ComponentChangedSerialized()
        {

            BwoinkSerializer.TraceWriter = Console.Out;
            var serializer = new BwoinkSerializer {Tracing = true};

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
                Assert.AreEqual(3, payload.ComponentChanges.Count);
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

            Console.WriteLine(BitConverter.ToString(array).Replace('-', ' '));
        }

        [Test]
        public void ComponentChangedSerialized2()
        {
            BwoinkSerializer.TraceWriter = Console.Out;
            var serializer = new BwoinkSerializer {Tracing = true};

            serializer.Initialize();

            byte[] array;

            var source = new EntityState(new EntityUid(512),
                new List<ComponentChanged>
                {
                    new ComponentChanged(true, 12, null),
                    new ComponentChanged(false, 23, "Collision"),
                    new ComponentChanged(false, 42, "BallisticBullet")
                },
                new List<ComponentState>
                {
                    new MapComponentState(new MapId(1)),
                    new ClickableComponentState(new Box2(-1, 1, 1, -1)),
                    new PhysicsComponentState(32f, Vector2.UnitX)
                }
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
                Assert.AreEqual(3, payload.ComponentChanges.Count);
                Assert.True(payload.ComponentChanges[0].Deleted);
                Assert.AreEqual(12, payload.ComponentChanges[0].NetID);
                Assert.IsNull(payload.ComponentChanges[0].ComponentName);
                Assert.False(payload.ComponentChanges[1].Deleted);
                Assert.AreEqual(23, payload.ComponentChanges[1].NetID);
                Assert.AreEqual("Collision", payload.ComponentChanges[1].ComponentName);
                Assert.False(payload.ComponentChanges[2].Deleted);
                Assert.AreEqual(42, payload.ComponentChanges[2].NetID);
                Assert.AreEqual("BallisticBullet", payload.ComponentChanges[2].ComponentName);
                Assert.IsNotNull(payload.ComponentStates);
                Assert.AreEqual(3, payload.ComponentStates.Count);
                Assert.AreEqual(typeof(MapComponentState), payload.ComponentStates[0].GetType());
                Assert.AreEqual(new MapId(1), ((MapComponentState) payload.ComponentStates[0]).MapId);
                Assert.AreEqual(typeof(ClickableComponentState), payload.ComponentStates[1].GetType());
                Assert.AreEqual(new Box2(-1, 1, 1, -1), ((ClickableComponentState) payload.ComponentStates[1]).LocalBounds);
                Assert.AreEqual(typeof(PhysicsComponentState), payload.ComponentStates[2].GetType());
                Assert.AreEqual(32f * 1000, ((PhysicsComponentState) payload.ComponentStates[2]).Mass); // wtf @ PhysicsComponentState ctor
                Assert.AreEqual(Vector2.UnitX, ((PhysicsComponentState) payload.ComponentStates[2]).Velocity);
            }

            Assert.Pass($"Size in Bytes: {array.Length.ToString()}");

            Console.WriteLine(BitConverter.ToString(array).Replace('-', ' '));
        }

        [Test]
        public void ItemValueTupleSerialized1()
        {
            BwoinkSerializer.TraceWriter = Console.Out;
            var serializer = new BwoinkSerializer {Tracing = true};

            serializer.Initialize();

            byte[] array;

            var source = new List<ValueTuple<int, object>>
            {
                (1, null),
                (2, true),
                (3, new EntityState(new EntityUid(1), null, null))
            };
            using (var stream = new MemoryStream())
            {
                serializer.Serialize(stream, source);
                array = stream.ToArray();
            }

            Assert.IsNotEmpty(array);

            using (var stream = new MemoryStream(array, false))
            {
                var payload = (List<ValueTuple<int, object>>) serializer.Deserialize(stream);
            }

            Assert.Pass($"Size in Bytes: {array.Length.ToString()}");

            Console.WriteLine(BitConverter.ToString(array).Replace('-', ' '));
        }

        [Test]
        public unsafe void RoundTripInt()
        {
            BwoinkSerializer.TraceWriter = Console.Out;
            var serializer = new BwoinkSerializer {Tracing = true};

            serializer.Initialize();

            byte[] array;

            const int sourceSize = 1;
            const int accountingForTypeInfo = 4;
            var source = new int[sourceSize];
            var sourceBytes = sizeof(int) * sourceSize;
            fixed (int* p = &source[0])
            {
                RandomNumberGenerator.Fill(new Span<byte>(p, sourceBytes));
            }

            using (var stream = new MemoryStream())
            {
                serializer.Serialize(stream, source[0]);
                array = stream.ToArray();
            }

            Assert.IsNotEmpty(array);

            using (var stream = new MemoryStream(array, false))
            {
                var payload = (int) serializer.Deserialize(stream);
                Assert.AreEqual(source[0], payload);
            }

            Console.WriteLine($"Size in Bytes: {array.Length.ToString()} (overhead of {array.Length - (sourceBytes + accountingForTypeInfo)})");

            Console.WriteLine(BitConverter.ToString(array).Replace('-', ' '));
        }

        [Test]
        public unsafe void RoundTripNullableInt()
        {
            BwoinkSerializer.TraceWriter = Console.Out;
            var serializer = new BwoinkSerializer {Tracing = true};

            serializer.Initialize();

            byte[] array;

            const int sourceSize = 1;
            const int accountingForTypeInfo = 4;
            var source = new int[sourceSize];
            var sourceBytes = sizeof(int) * sourceSize;
            fixed (int* p = &source[0])
            {
                RandomNumberGenerator.Fill(new Span<byte>(p, sourceBytes));
            }

            // ReSharper disable once ConvertNullableToShortForm
            object sourceNullableInt = (object) (Nullable<int>) new Nullable<int>(source[0]);

            using (var stream = new MemoryStream())
            {
                serializer.Serialize(stream, sourceNullableInt);
                array = stream.ToArray();
            }

            Assert.IsNotEmpty(array);

            using (var stream = new MemoryStream(array, false))
            {
                var payload = (int) serializer.Deserialize(stream);
                Assert.AreEqual(sourceNullableInt, payload);
            }

            Console.WriteLine($"Size in Bytes: {array.Length.ToString()} (overhead of {array.Length - (sourceBytes + accountingForTypeInfo)})");

            Console.WriteLine(BitConverter.ToString(array).Replace('-', ' '));
        }

        [Test]
        public unsafe void RoundTripNullableIntNull()
        {
            BwoinkSerializer.TraceWriter = Console.Out;
            var serializer = new BwoinkSerializer {Tracing = true};

            serializer.Initialize();

            byte[] array;

            const int sourceSize = 1;
            const int accountingForTypeInfo = 4;
            var sourceBytes = sizeof(int) * sourceSize;

            int? sourceNullableInt = null;

            using (var stream = new MemoryStream())
            {
                // ReSharper disable once ExpressionIsAlwaysNull
                serializer.Serialize(stream, sourceNullableInt);
                array = stream.ToArray();
            }

            Assert.IsNotEmpty(array);

            using (var stream = new MemoryStream(array, false))
            {
                var payload = serializer.Deserialize(stream);
                // ReSharper disable once ExpressionIsAlwaysNull
                Assert.AreEqual(sourceNullableInt, payload);
            }

            Console.WriteLine($"Size in Bytes: {array.Length.ToString()} (overhead of {array.Length - (sourceBytes + accountingForTypeInfo)})");

            Console.WriteLine(BitConverter.ToString(array).Replace('-', ' '));
        }

        private struct NullableWrapper<T> where T : struct
        {

            [CanBeNull]
            public T? Value;

        }

        [Test]
        public unsafe void RoundTripNullableIntNullStruct()
        {
            BwoinkSerializer.TraceWriter = Console.Out;
            RobustNetTypeInfo.RegisterAssembly(typeof(NullableWrapper<>).Assembly);

            var serializer = new BwoinkSerializer {Tracing = true};

            serializer.Initialize();

            byte[] array;

            const int sourceSize = 1;
            const int accountingForTypeInfo = 4;
            var sourceBytes = sizeof(int) * sourceSize;

            var sourceNullableInt = new NullableWrapper<int>();

            using (var stream = new MemoryStream())
            {
                // ReSharper disable once ExpressionIsAlwaysNull
                serializer.Serialize(stream, sourceNullableInt);
                array = stream.ToArray();
            }

            Assert.IsNotEmpty(array);

            using (var stream = new MemoryStream(array, false))
            {
                var payload = serializer.Deserialize(stream);
                // ReSharper disable once ExpressionIsAlwaysNull
                Assert.AreEqual(sourceNullableInt, payload);
            }

            Console.WriteLine($"Size in Bytes: {array.Length.ToString()} (overhead of {array.Length - (sourceBytes + accountingForTypeInfo)})");

            Console.WriteLine(BitConverter.ToString(array).Replace('-', ' '));
        }

        [Test]
        public unsafe void RoundTripIntArray()
        {
            BwoinkSerializer.TraceWriter = Console.Out;
            var serializer = new BwoinkSerializer {Tracing = true};

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

            Console.WriteLine($"Size in Bytes: {array.Length.ToString()} (overhead of {array.Length - sourceBytes + accountingForTypeAndSizeInfo})");

            Console.WriteLine(BitConverter.ToString(array).Replace('-', ' '));
        }

        [Test]
        public unsafe void RoundTripBoxedIntArray()
        {
            BwoinkSerializer.TraceWriter = Console.Out;
            var serializer = new BwoinkSerializer {Tracing = true};

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

            Console.WriteLine($"Size in Bytes: {array.Length.ToString()} (overhead of {array.Length - sourceBytes + accountingForTypeAndSizeInfo})");

            Console.WriteLine(BitConverter.ToString(array).Replace('-', ' '));
        }

        [Test]
        public unsafe void RoundTripString()
        {
            BwoinkSerializer.TraceWriter = Console.Out;
            var serializer = new BwoinkSerializer {Tracing = true};

            serializer.Initialize();

            byte[] array;

            const int sourceSize = 10;
            const int accountingForTypeAndSizeInfo = 4 + 4;
            const int sourceBytes = sizeof(char) * sourceSize;
            const string source = "Collidable";

            using (var stream = new MemoryStream())
            {
                serializer.Serialize(stream, source);
                array = stream.ToArray();
            }

            Assert.IsNotEmpty(array);

            using (var stream = new MemoryStream(array, false))
            {
                var payload = (string) serializer.Deserialize(stream);
                Assert.AreEqual(source, payload);
            }

            Console.WriteLine($"Size in Bytes: {array.Length.ToString()} (overhead of {array.Length - sourceBytes + accountingForTypeAndSizeInfo})");

            Console.WriteLine(BitConverter.ToString(array).Replace('-', ' '));
        }

        [Test]
        public unsafe void RoundTripUniqueString()
        {
            BwoinkSerializer.TraceWriter = Console.Out;
            var serializer = new BwoinkSerializer {Tracing = true};

            serializer.Initialize();

            byte[] array;

            const int sourceSize = 10;
            const int accountingForTypeAndSizeInfo = 4 + 4;
            const int sourceBytes = sizeof(char) * sourceSize;
            var source = new char[sourceSize];


            string normalized;
            for (;;)
            {
                fixed (char* p = &source[0])
                {
                    RandomNumberGenerator.Fill(new Span<byte>(p, sourceBytes));
                }

                try
                {
                    normalized = new string(source).Normalize();
                }
                catch (ArgumentException)
                {
                    continue;
                }

                break;
            }

            using (var stream = new MemoryStream())
            {
                serializer.Serialize(stream, normalized);
                array = stream.ToArray();
            }

            Assert.IsNotEmpty(array);

            using (var stream = new MemoryStream(array, false))
            {
                var payload = (string) serializer.Deserialize(stream);
                Assert.AreEqual(normalized, payload.ToCharArray());
            }

            Console.WriteLine($"Size in Bytes: {array.Length.ToString()} (overhead of {array.Length - sourceBytes + accountingForTypeAndSizeInfo})");

            Console.WriteLine(BitConverter.ToString(array).Replace('-', ' '));
        }


        [Test]
        public unsafe void RoundTripUniqueStringInArray()
        {
            BwoinkSerializer.TraceWriter = Console.Out;
            var serializer = new BwoinkSerializer {Tracing = true};

            serializer.Initialize();

            byte[] array;

            const int sourceSize = 10;
            const int accountingForTypeAndSizeInfo = 4 + 4;
            const int sourceBytes = sizeof(char) * sourceSize;
            var source = new char[sourceSize];

            string normalized;
            for (;;)
            {
                fixed (char* p = &source[0])
                {
                    RandomNumberGenerator.Fill(new Span<byte>(p, sourceBytes));
                }

                try
                {
                    normalized = new string(source).Normalize();
                }
                catch (ArgumentException)
                {
                    continue;
                }

                break;
            }

            using (var stream = new MemoryStream())
            {
                serializer.Serialize(stream, new [] { normalized });
                array = stream.ToArray();
            }

            Assert.IsNotEmpty(array);

            using (var stream = new MemoryStream(array, false))
            {
                var payload = (string[]) serializer.Deserialize(stream);
                Assert.AreEqual(normalized, payload[0].ToCharArray());
            }

            Console.WriteLine($"Size in Bytes: {array.Length.ToString()} (overhead of {array.Length - sourceBytes + accountingForTypeAndSizeInfo})");

            Console.WriteLine(BitConverter.ToString(array).Replace('-', ' '));
        }
    }

}

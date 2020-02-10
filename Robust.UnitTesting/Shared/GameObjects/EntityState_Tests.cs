using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    class EntityState_Tests
    {

        [Test]
        public void RobustNetTypeInfoStringAutoDictTest()
        {
            Assert.True(RobustNetTypeInfo.TryGetStringId("Collidable", out var stringId));
            Assert.That(stringId.AssemblyIndex, Is.GreaterThanOrEqualTo(1));
            Assert.That(stringId.StringIndex, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void RobustNetTypeInfoStringAutoDictTestNegative()
        {
            Assert.False(RobustNetTypeInfo.TryGetStringId($"Unique {DateTime.Now.Ticks:X16}", out var stringId));
            Assert.That(stringId.AssemblyIndex, Is.EqualTo(0));
            Assert.That(stringId.StringIndex, Is.EqualTo(0));
        }

        [Test]
        public void RobustNetTypeInfoStringAutoDictHashTest()
        {
            var hash = RobustNetTypeInfo.GetAssemblyStringsHash(2);
            Assert.NotNull(hash);
            Assert.AreEqual(512 / 8, hash.Length);
            Assert.AreNotEqual(new byte[512 / 8], hash);
        }

        /// <summary>
        ///     Used to measure the size of <see cref="object"/>s in bytes. This is not actually a test,
        ///     but a useful benchmark tool, so i'm leaving it here.
        /// </summary>
        [Test]
        public void ComponentChangedSerialized()
        {
            var container = new DependencyCollection();
            container.Register<IReflectionManager, ServerReflectionManager>();
            container.Register<IRobustSerializer, RobustSerializer>();
            container.BuildGraph();

            container.Resolve<IReflectionManager>().LoadAssemblies(AppDomain.CurrentDomain.GetAssemblyByName("Robust.Shared"));

            var serializer = (RobustSerializer)container.Resolve<IRobustSerializer>();
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

        }

    }

}

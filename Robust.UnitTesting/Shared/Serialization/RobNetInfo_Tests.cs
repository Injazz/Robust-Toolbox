using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    class RobustNetInfo_Tests
    {


        [Test]
        public void RobustNetTypeInfoStringAutoDictTest()
        {
            const string knownStr = "Collidable";
            Assert.True(RobustNetTypeInfo.TryGetStringId(knownStr, out var stringId));
            Assert.That(stringId.AssemblyIndex, Is.GreaterThanOrEqualTo(1));
            Assert.That(stringId.StringIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(stringId, Is.Not.EqualTo(default((byte,int))));
            Assert.True(RobustNetTypeInfo.TryGetString(stringId, out var str));
            Assert.AreEqual(knownStr, str);
        }

        [Test]
        public void RobustNetTypeInfoStringAutoDictTestNegative()
        {
            Assert.False(RobustNetTypeInfo.TryGetStringId($"Unique {DateTime.Now.Ticks:X16} {RandomNumberGenerator.GetInt32(int.MaxValue):X8}", out var stringId));
            Assert.That(stringId.AssemblyIndex, Is.EqualTo(0));
            Assert.That(stringId.StringIndex, Is.EqualTo(0));
            Assert.That(stringId, Is.EqualTo(default((byte,int))));
        }

        [Test]
        public void RobustNetTypeInfoStringAutoDictHashTest()
        {
            var hash = RobustNetTypeInfo.GetAssemblyStringsHash(2);
            Assert.NotNull(hash);
            Assert.AreEqual(512 / 8, hash.Length);
            Assert.AreNotEqual(new byte[512 / 8], hash);
            Assert.Pass($"Assembly {RobustNetTypeInfo.GetAssembly(2).GetName().Name}: {BitConverter.ToString(hash)}");
        }

    }

}

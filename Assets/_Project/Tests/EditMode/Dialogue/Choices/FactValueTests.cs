using System.Globalization;
using System.Threading;
using NUnit.Framework;
using Pickleball.Sandbox.Dialogue;
using UnityEngine;

namespace Pickleball.Tests.Dialogue
{
    /// <summary>
    /// <see cref="FactValue"/>: reads convert between bool, int and string so
    /// content can't trip over a type, while equality is strict (same type and
    /// payload), which is what decides whether a fact "changed".
    /// </summary>
    public class FactValueTests
    {
        [Test]
        public void Default_IsBoolFalse()
        {
            FactValue value = default;
            Assert.That(value.Type, Is.EqualTo(FactType.Bool));
            Assert.That(value.AsBool, Is.False);
            Assert.That(value.AsInt, Is.Zero);
            Assert.That(value.AsString, Is.EqualTo("false"));
            Assert.That(value, Is.EqualTo(FactValue.Of(false)));
        }

        [TestCase(true, 1, "true")]
        [TestCase(false, 0, "false")]
        public void Bool_Reads(bool b, int asInt, string asString)
        {
            FactValue value = FactValue.Of(b);
            Assert.That(value.Type, Is.EqualTo(FactType.Bool));
            Assert.That(value.AsBool, Is.EqualTo(b));
            Assert.That(value.AsInt, Is.EqualTo(asInt));
            Assert.That(value.AsString, Is.EqualTo(asString));
        }

        [TestCase(0, false, "0")]
        [TestCase(1, true, "1")]
        [TestCase(-3, true, "-3")]
        [TestCase(int.MaxValue, true, "2147483647")]
        public void Int_Reads(int n, bool asBool, string asString)
        {
            FactValue value = FactValue.Of(n);
            Assert.That(value.Type, Is.EqualTo(FactType.Int));
            Assert.That(value.AsBool, Is.EqualTo(asBool));
            Assert.That(value.AsInt, Is.EqualTo(n));
            Assert.That(value.AsString, Is.EqualTo(asString));
        }

        [TestCase("12", true, 12)]
        [TestCase("-7", true, -7)]
        [TestCase(" 5 ", true, 5)]
        [TestCase("abc", true, 0)]
        [TestCase("1.5", true, 0)]
        [TestCase("", false, 0)]
        public void String_Reads(string s, bool asBool, int asInt)
        {
            FactValue value = FactValue.Of(s);
            Assert.That(value.Type, Is.EqualTo(FactType.String));
            Assert.That(value.AsBool, Is.EqualTo(asBool));
            Assert.That(value.AsInt, Is.EqualTo(asInt));
            Assert.That(value.AsString, Is.EqualTo(s));
        }

        [Test]
        public void NullString_ReadsAsEmpty_NeverNull()
        {
            FactValue value = FactValue.Of((string)null);
            Assert.That(value.AsString, Is.EqualTo(string.Empty));
            Assert.That(value, Is.EqualTo(FactValue.Of("")));
        }

        [Test]
        public void IntToString_IgnoresTheCurrentCulture()
        {
            CultureInfo saved = Thread.CurrentThread.CurrentCulture;
            try
            {
                // A culture with a non-ASCII minus sign.
                Thread.CurrentThread.CurrentCulture = new CultureInfo("sv-SE");
                Assert.That(FactValue.Of(-3).AsString, Is.EqualTo("-3"));
                Assert.That(FactValue.Of("-3").AsInt, Is.EqualTo(-3));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = saved;
            }
        }

        [TestCase(FactType.Bool)]
        [TestCase(FactType.Int)]
        [TestCase(FactType.String)]
        public void DefaultOf_IsTheTypesZero(FactType type)
        {
            FactValue value = FactValue.DefaultOf(type);
            Assert.That(value.Type, Is.EqualTo(type));
            Assert.That(value.AsBool, Is.False);
            Assert.That(value.AsInt, Is.Zero);
        }

        [Test]
        public void Equality_NeedsTheSameTypeAndPayload()
        {
            Assert.That(FactValue.Of(true) == FactValue.Of(true), Is.True);
            Assert.That(FactValue.Of(3) == FactValue.Of(3), Is.True);
            Assert.That(FactValue.Of("a") == FactValue.Of("a"), Is.True);

            Assert.That(FactValue.Of(3) != FactValue.Of(4), Is.True);
            Assert.That(FactValue.Of(true) == FactValue.Of(1), Is.False, "bool true and int 1 differ.");
            Assert.That(FactValue.Of("1") == FactValue.Of(1), Is.False);
            Assert.That(FactValue.Of("a") == FactValue.Of("A"), Is.False, "Ordinal, case-sensitive.");
        }

        [Test]
        public void Equality_ObjectOverload_AndHashCodesAgree()
        {
            Assert.That(FactValue.Of(5).Equals((object)FactValue.Of(5)), Is.True);
            Assert.That(FactValue.Of(5).Equals((object)5), Is.False);
            Assert.That(FactValue.Of(5).Equals(null), Is.False);

            Assert.That(FactValue.Of(5).GetHashCode(), Is.EqualTo(FactValue.Of(5).GetHashCode()));
            Assert.That(FactValue.Of("x").GetHashCode(), Is.EqualTo(FactValue.Of("x").GetHashCode()));
            Assert.That(FactValue.Of(true).GetHashCode(), Is.EqualTo(FactValue.Of(true).GetHashCode()));
        }

        [Test]
        public void ToString_ShowsTypeAndValue()
        {
            Assert.That(FactValue.Of(3).ToString(), Is.EqualTo("Int:3"));
            Assert.That(FactValue.Of(true).ToString(), Is.EqualTo("Bool:true"));
            Assert.That(FactValue.Of("hi").ToString(), Is.EqualTo("String:hi"));
        }

        [Test]
        public void JsonRoundTrip_KeepsTypeAndPayload()
        {
            foreach (FactValue original in new[] { FactValue.Of(true), FactValue.Of(-42), FactValue.Of("é \"q\" \n") })
            {
                var copy = JsonUtility.FromJson<Box>(JsonUtility.ToJson(new Box { value = original })).value;
                Assert.That(copy, Is.EqualTo(original), original.ToString());
            }
        }

        [System.Serializable]
        class Box
        {
            public FactValue value;
        }
    }
}
